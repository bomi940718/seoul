using System.Net;
using System.Text.RegularExpressions;

namespace LawReview.Core.Municipal;

/// <summary>
/// 안양시 고시공고(www.anyang.go.kr) 지구단위계획 조회 클라이언트.
///
/// 서울과 달리 경기도 시·군에는 지구단위계획 전용 포털이 없다. 대신 **시청 고시공고 게시판**에
/// 도시관리계획(지구단위계획구역 및 계획) 결정 고시가 고시문·결정조서 첨부와 함께 올라온다.
/// 그래서 이 제공자는 "구역 목록"이 아니라 **고시 목록**을 돌려준다 — 검토서에 인용하는 것은
/// 어차피 구역명·고시번호·고시일자·원문 링크이므로 같은 역할을 한다.
///
/// 실측(2026-11)으로 확인한 경로 — 모두 서버 렌더링 HTML이라 세션·키가 필요 없다:
///  · 목록: GET /main/selectEminwonList.do?key=4101&amp;notAncmtSeCode=01,04
///          &amp;searchCnd=B_Subject&amp;searchKrwd={키워드}&amp;pageUnit={N}
///    행 구조: 고시공고번호 / 제목(상세 링크, **목록에서는 잘려 있다**) / 담당부서 / 등록일 / 게시기간
///  · 상세: GET /main/selectEminwonView.do?key=4101&amp;not_ancmt_mgt_no={번호}
///    → 잘리지 않은 제목과 첨부파일 링크(eminwon.anyang.go.kr/.../FileDown.jsp)를 여기서 얻는다.
///
/// 게시판 검색은 **제목 부분일치**다. 법정동으로 찾으면 그 동의 고시가 걸리지만
/// "평촌신도시"처럼 동 이름이 제목에 없는 구역은 걸리지 않으므로, 결과가 없으면
/// "지구단위계획"으로 한 번 더 찾아 최근 고시를 후보로 보여준다.
/// 필지-구역 정확 대응은 지도(WFS) 없이는 불가능하므로 **판정은 확인필요를 유지한다**(원칙 5).
/// </summary>
public sealed class AnyangNoticeClient : IDistrictPlanProvider
{
    private const string BaseUrl = "https://www.anyang.go.kr";
    private const string ListPath = "/main/selectEminwonList.do";
    private const string ViewPath = "/main/selectEminwonView.do";
    /// <summary>고시공고 게시판 (수동 열람 안내용).</summary>
    public const string PortalPageUrl = BaseUrl + ListPath + "?key=4101&notAncmtSeCode=01,04";

    private readonly HttpClient _http;

    public AnyangNoticeClient(HttpClient http) => _http = http;

    public string Province => "경기도";
    public string Name => "안양시 고시공고";

    public bool Matches(string province, string city) =>
        province.Replace(" ", "").StartsWith("경기") && city.Replace(" ", "").StartsWith("안양시");

    public async Task<IReadOnlyList<DistrictPlanRecord>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        var hits = (await SearchTitlesAsync(keyword, ct)).Where(IsPlanNotice).ToList();

        // 동 이름이 제목에 없는 구역(평촌신도시 등)은 법정동으로 안 걸린다 → 최근 고시를 후보로.
        // 다만 **그 필지의 고시가 아니라 참고 목록**이므로 그 사실을 레코드에 적어 둔다.
        var fallback = hits.Count == 0;
        if (fallback)
            hits = (await SearchTitlesAsync("지구단위계획", ct)).Where(IsPlanNotice).ToList();

        var records = new List<DistrictPlanRecord>();
        foreach (var row in hits.Take(6))
            records.Add(await ToRecordAsync(row, fallback ? $"\"{keyword}\" 검색 결과 없음 — 최근 고시 참고" : "", ct));
        return records;
    }

    /// <summary>
    /// 구역 고시인지. 제목에 "지구단위계획"이 들어가야 하고,
    /// **수립 지침 개정 고시는 제외한다** — 특정 구역의 결정이 아니라 시 전체 지침이라
    /// 필지 검토의 근거로 인용하면 엉뚱하다.
    /// </summary>
    private static bool IsPlanNotice(ListRow row) =>
        row.Title.Contains("지구단위계획")
        && !row.Title.Contains("수립 지침")
        && !row.Title.Contains("수립지침");

    private async Task<IReadOnlyList<ListRow>> SearchTitlesAsync(string keyword, CancellationToken ct)
    {
        var uri = new Uri($"{BaseUrl}{ListPath}?key=4101&notAncmtSeCode=01,04&searchCnd=B_Subject" +
                          $"&searchKrwd={Uri.EscapeDataString(keyword)}&pageUnit=30&pageIndex=1");
        return ParseList(await GetStringAsync(uri, ct));
    }

    // 목록 한 행. 제목 칸은 상세 링크를 품고 있고 번호 칸이 그 앞에 온다.
    private static readonly Regex RowPattern = new(
        @"<td class=""p-subject"">\s*(?<no>[^<]*?)\s*</td>\s*" +
        @"<td class=""p-subject""><a href=""[^""]*?not_ancmt_mgt_no=(?<id>\d+)[^""]*""[^>]*>(?<title>.*?)</a></td>\s*" +
        @"<td>(?<dept>[^<]*)</td>\s*<td>(?<date>[^<]*)</td>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    internal static IReadOnlyList<ListRow> ParseList(string html)
    {
        var rows = new List<ListRow>();
        foreach (Match m in RowPattern.Matches(html ?? ""))
            rows.Add(new ListRow(
                Id: m.Groups["id"].Value,
                NoticeNo: Clean(m.Groups["no"].Value),
                Title: Clean(m.Groups["title"].Value),
                Dept: Clean(m.Groups["dept"].Value),
                Date: Clean(m.Groups["date"].Value)));
        return rows;
    }

    private async Task<DistrictPlanRecord> ToRecordAsync(ListRow row, string note, CancellationToken ct)
    {
        var detailUrl = $"{BaseUrl}{ViewPath}?key=4101&notAncmtSeCode=01,04&not_ancmt_mgt_no={row.Id}";
        var title = row.Title;
        var file = "";
        try
        {
            var detail = ParseDetail(await GetStringAsync(new Uri(detailUrl), ct));
            // 목록 제목은 "…결정(변경) 정정…"처럼 잘려 온다 — 상세의 온전한 제목을 쓴다.
            if (detail.Title.Length > 0) title = detail.Title;
            file = detail.FileUrl;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // 상세를 못 읽어도 목록에서 얻은 고시번호·제목은 인용할 수 있다.
        }

        return new DistrictPlanRecord(
            ZoneName: ZoneNameFrom(title),
            // 게시판에는 위치 칸이 없다(고시문 원문에서 확인한다). 폴백으로 가져온 목록은 그 사실을 적는다.
            Location: note,
            NoticeOrgan: "안양시",
            NoticeNo: row.NoticeNo,
            NoticeDate: row.Date,
            NoticeTitle: title,
            AreaAfter: null,                   // 조서는 첨부 고시문 안에 있다
            NoticePdfUrl: file,
            PortalUrl: detailUrl);
    }

    // 상세 페이지의 제목 칸과 첨부파일 링크.
    private static readonly Regex DetailTitle = new(
        @"<th[^>]*>\s*제목\s*</th>\s*<td[^>]*>(?<v>.*?)</td>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex DetailFile = new(
        @"href=""(?<v>https?://eminwon\.anyang\.go\.kr/[^""]*FileDown\.jsp[^""]*)""", RegexOptions.Compiled);

    internal static (string Title, string FileUrl) ParseDetail(string html)
    {
        var text = html ?? "";
        var title = DetailTitle.Match(text) is { Success: true } t ? Clean(t.Groups["v"].Value) : "";
        var file = DetailFile.Match(text) is { Success: true } f ? WebUtility.HtmlDecode(f.Groups["v"].Value) : "";
        return (title, file);
    }

    /// <summary>
    /// 고시 제목에서 구역 이름을 뽑는다. "도시관리계획/지구단위계획"부터가 행위 표기이므로
    /// 그 앞까지가 구역명이다.
    ///
    ///  · "호계동 범내지구 도시관리계획(지구단위계획구역 및 계획) 결정(변경) 고시" → "호계동 범내지구"
    ///  · "도시관리계획(호계동 범내지구 지구단위계획) 결정(경미한 변경) 고시" → "호계동 범내지구"
    ///
    /// 두 번째처럼 **행위 표기로 시작하는 제목은 구역명이 괄호 안에 있다.** 이 경우를 따로 보지 않으면
    /// 괄호 한가운데서 잘려 "도시관리계획(호계동 범내지구" 같은 이름이 검토서에 실린다.
    /// </summary>
    internal static string ZoneNameFrom(string title)
    {
        var text = (title ?? "").Trim();

        // 구역명이 괄호 안에 있는 제목인지: 행위 표기가 맨 앞이거나 첫 괄호 뒤에 있으면 그렇다.
        var marker = FirstMarker(text);
        var paren = text.IndexOf('(');
        if (marker == 0 || (paren >= 0 && marker > paren))
            if (InsideParenthesis(text) is { Length: > 0 } inner) text = inner.Trim();

        var cut = FirstMarker(text);
        return cut > 0 ? text[..cut].Trim() : text;
    }

    /// <summary>행위 표기("도시관리계획"·"지구단위계획")가 처음 나오는 자리. 없으면 -1.</summary>
    private static int FirstMarker(string text) => Markers
        .Select(m => text.IndexOf(m, StringComparison.Ordinal))
        .Where(i => i >= 0)
        .DefaultIfEmpty(-1)
        .Min();

    private static readonly string[] Markers = { "도시관리계획", "지구단위계획" };

    /// <summary>첫 괄호 안의 내용. 닫는 괄호가 없으면 끝까지.</summary>
    private static string? InsideParenthesis(string text)
    {
        var open = text.IndexOf('(');
        if (open < 0) return null;
        var close = text.IndexOf(')', open + 1);
        return close < 0 ? text[(open + 1)..] : text[(open + 1)..close];
    }

    public async Task<bool> DownloadNoticePdfAsync(DistrictPlanRecord record, string filePath,
        CancellationToken ct = default)
    {
        if (record.NoticePdfUrl.Length == 0) return false;
        var bytes = await _http.GetByteArrayAsync(record.NoticePdfUrl, ct);
        if (bytes.Length == 0) return false;
        await File.WriteAllBytesAsync(filePath, bytes, ct);
        return true;
    }

    private async Task<string> GetStringAsync(Uri uri, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(uri, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>표 칸의 태그·엔티티·연속 공백을 정리한다.</summary>
    private static string Clean(string raw) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(raw ?? "", "<[^>]*>", "")), @"\s+", " ").Trim();

    /// <summary>고시공고 목록 한 행 (상세 조회 전).</summary>
    internal sealed record ListRow(string Id, string NoticeNo, string Title, string Dept, string Date);
}
