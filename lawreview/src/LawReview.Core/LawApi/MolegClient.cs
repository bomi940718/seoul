using System.Text.Json;
using System.Text.RegularExpressions;

namespace LawReview.Core.LawApi;

/// <summary>법제처 국가법령정보 Open API 조회 대상.</summary>
public enum LawTarget
{
    Law,        // 법령 (법률·시행령·시행규칙)
    Ordinance,  // 자치법규 (조례·규칙)
    AdminRule,  // 행정규칙 (고시·훈령 등)
}

/// <summary>
/// 법제처 국가법령정보센터 Open API 클라이언트 (open.law.go.kr 발급 OC 키 필요).
/// 검토서에 인용되는 모든 조문 원문은 반드시 이 클라이언트를 통해 조회한 현행 원문만 사용한다.
///
/// 실 API 검증(2026-07)으로 확인된 응답 구조 — target마다 다르다:
///  · 검색: 법령 LawSearch.law[], 자치법규 OrdinSearch.law[], 행정규칙 AdmRulSearch.admrul(단건이면 객체),
///          별표 licBylSearch.licbyl[] (법령명 검색은 search=2 필수)
///  · 본문: 법령 {"법령":{기본정보,조문.조문단위[],별표.별표단위[]}} — 조문은 항/호/목 중첩
///          자치법규 {"LawService":{자치법규기본정보,조문.조[]}} — 조내용에 전체 평문, 조문번호는 6자리("000400")
///          행정규칙 {"AdmRulService":{행정규칙기본정보,조문내용[문자열...],별표.별표단위[]}} — 구조 없는 평문
///  · 행정규칙 본문 조회는 MST 대신 ID 파라미터를 쓴다.
/// </summary>
public sealed class MolegClient
{
    private const string BaseUrl = "https://www.law.go.kr/DRF";
    private readonly HttpClient _http;
    private readonly string _oc;

    public MolegClient(HttpClient http, string oc)
    {
        _http = http;
        _oc = oc;
    }

    /// <summary>법령/자치법규/행정규칙 검색. 이름이 정확할수록 첫 결과가 원하는 법령이다.</summary>
    public async Task<IReadOnlyList<LawSummary>> SearchAsync(
        string query, LawTarget target = LawTarget.Law, CancellationToken ct = default)
    {
        var uri = BuildSearchUri(query, target);
        using var doc = await GetJsonAsync(uri, ct);
        return ParseSearch(doc.RootElement, target);
    }

    internal static IReadOnlyList<LawSummary> ParseSearch(JsonElement root, LawTarget target)
    {
        // 항목 키: 법령·자치법규는 "law", 행정규칙은 "admrul". 단건이면 배열이 아니라 객체로 온다.
        var itemKey = target == LawTarget.AdminRule ? "admrul" : "law";
        var results = new List<LawSummary>();
        foreach (var container in root.EnumerateObject())
        {
            if (!container.Value.TryGetProperty(itemKey, out var items))
            {
                items = FindFirstArray(container.Value);
                if (items.ValueKind == JsonValueKind.Undefined) continue;
            }
            foreach (var item in EnumerateArrayOrSingle(items))
            {
                results.Add(new LawSummary(
                    Name: GetString(item, "법령명한글") ?? GetString(item, "자치법규명")
                          ?? GetString(item, "행정규칙명") ?? "",
                    SerialNo: GetString(item, "법령일련번호") ?? GetString(item, "자치법규일련번호")
                              ?? GetString(item, "행정규칙일련번호") ?? "",
                    EffectiveDate: GetString(item, "시행일자") ?? "",
                    RevisionDate: GetString(item, "공포일자") ?? GetString(item, "발령일자") ?? "",
                    Kind: GetString(item, "법령구분명") ?? GetString(item, "자치법규종류")
                          ?? GetString(item, "행정규칙종류") ?? "",
                    Department: GetString(item, "소관부처명") ?? GetString(item, "지자체기관명") ?? ""));
            }
        }
        return results;
    }

    /// <summary>법령 본문 조회. 일련번호로 현행 전문·조문·별표 목록을 가져온다.</summary>
    public async Task<LawText> GetLawTextAsync(
        string serialNo, LawTarget target = LawTarget.Law, CancellationToken ct = default)
    {
        var uri = BuildServiceUri(serialNo, target);
        using var doc = await GetJsonAsync(uri, ct);
        return ParseLawText(doc.RootElement);
    }

    internal static LawText ParseLawText(JsonElement root)
    {
        string lawName = "";
        string effectiveDate = "";
        var articles = new List<Article>();
        var annexes = new List<Annex>();

        foreach (var container in root.EnumerateObject())
        {
            var body = container.Value;

            // 기본정보 키가 target마다 다르다: 법령 "기본정보" / 자치법규 "자치법규기본정보" / 행정규칙 "행정규칙기본정보".
            foreach (var basicKey in new[] { "기본정보", "자치법규기본정보", "행정규칙기본정보" })
            {
                if (!body.TryGetProperty(basicKey, out var basic)) continue;
                lawName = GetString(basic, "법령명_한글") ?? GetString(basic, "법령명한글")
                          ?? GetString(basic, "자치법규명") ?? GetString(basic, "행정규칙명") ?? "";
                effectiveDate = GetString(basic, "시행일자") ?? "";
                break;
            }

            if (body.TryGetProperty("조문", out var joSection))
            {
                if (joSection.TryGetProperty("조문단위", out var units))
                {
                    // 법령: 항/호/목 중첩 구조. 조문여부 "조문"만 실제 조문이다 ("전문"은 장·절 제목).
                    foreach (var jo in EnumerateArrayOrSingle(units))
                    {
                        if (GetString(jo, "조문여부") is string yn && yn != "조문") continue;
                        articles.Add(ParseArticle(jo));
                    }
                }
                else if (joSection.TryGetProperty("조", out var joItems))
                {
                    // 자치법규: 조내용에 조문 전체가 평문으로 담긴다. 조문여부는 "Y"/"N".
                    foreach (var jo in EnumerateArrayOrSingle(joItems))
                    {
                        if (GetString(jo, "조문여부") == "N") continue;
                        articles.Add(ParseOrdinanceArticle(jo));
                    }
                }
            }

            // 행정규칙: 구조 없는 문자열 배열. "제N조(제목) ..." 단위로 쪼갠다.
            if (body.TryGetProperty("조문내용", out var flat))
                articles.AddRange(ParseFlatArticles(FlattenStrings(flat)));

            if (body.TryGetProperty("별표", out var annexSection))
            {
                var units = annexSection.TryGetProperty("별표단위", out var u) ? u : annexSection;
                foreach (var b in EnumerateArrayOrSingle(units))
                {
                    var title = GetString(b, "별표제목") ?? GetString(b, "별표명") ?? "";
                    if (title.StartsWith("삭제")) continue;
                    annexes.Add(new Annex(
                        Number: FormatBranchedNumber(GetString(b, "별표번호") ?? "", GetString(b, "별표가지번호")),
                        Title: title,
                        Kind: GetString(b, "별표구분") ?? "별표",
                        Link: AbsoluteLink(GetString(b, "별표서식PDF파일링크") ?? GetString(b, "별표서식파일링크")
                                           ?? GetString(b, "별표첨부파일명") ?? ""),
                        // 법령 별표는 본문이 텍스트로 들어온다(자치법규 별표는 HWP 첨부라 비어 있다).
                        Content: b.TryGetProperty("별표내용", out var ac)
                            ? string.Join("\n", FlattenStrings(ac)) : ""));
                }
            }
        }
        return new LawText(lawName, effectiveDate, articles, annexes);
    }

    /// <summary>법령 조문 (항/호/목 중첩 구조).</summary>
    internal static Article ParseArticle(JsonElement jo)
    {
        var no = GetString(jo, "조문번호") ?? "";
        // 가지번호: "제48조의2"의 "2". 0이 아니면 조문번호에 "의N"을 붙인다.
        var branch = GetString(jo, "조문가지번호");
        if (branch is not null && branch.TrimStart('0').Length > 0)
            no = $"{no}의{branch.TrimStart('0')}";
        var title = GetString(jo, "조문제목") ?? "";
        var parts = new List<string>();
        if (GetText(jo, "조문내용") is string head && head.Length > 0) parts.Add(head.Trim());

        if (jo.TryGetProperty("항", out var hangs))
        {
            foreach (var hang in EnumerateArrayOrSingle(hangs))
            {
                if (GetText(hang, "항내용") is string hc && hc.Length > 0) parts.Add(hc.Trim());
                if (hang.TryGetProperty("호", out var hos))
                {
                    foreach (var ho in EnumerateArrayOrSingle(hos))
                    {
                        if (GetText(ho, "호내용") is string oc && oc.Length > 0) parts.Add(oc.Trim());
                        if (ho.TryGetProperty("목", out var moks))
                            foreach (var mok in EnumerateArrayOrSingle(moks))
                                if (GetText(mok, "목내용") is string mc && mc.Length > 0) parts.Add(mc.Trim());
                    }
                }
            }
        }
        return new Article(no, title, string.Join("\n", parts));
    }

    /// <summary>자치법규 조문. 조문번호가 6자리("000400" = 제4조, "001202" = 제12조의2)의 문자열(배열)로 온다.</summary>
    internal static Article ParseOrdinanceArticle(JsonElement jo)
    {
        var raw = jo.TryGetProperty("조문번호", out var noEl)
            ? EnumerateArrayOrSingle(noEl).Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                .FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? ""
            : "";
        var no = raw.Length == 6 && raw.All(char.IsDigit)
            ? FormatBranchedNumber(raw[..4], raw[4..])
            : raw.TrimStart('0');
        return new Article(no, GetString(jo, "조제목") ?? "", (GetText(jo, "조내용") ?? "").Trim());
    }

    private static readonly Regex FlatArticleHead = new(
        @"^제(\d+)조(?:의(\d+))?\s*(?:\(([^)]*)\))?", RegexOptions.Compiled);

    /// <summary>행정규칙 조문내용(구조 없는 문자열 목록)을 "제N조" 단위로 파싱. 장 제목 등 조문이 아닌 줄은 버린다.</summary>
    internal static IEnumerable<Article> ParseFlatArticles(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var text = line.Trim();
            var m = FlatArticleHead.Match(text);
            if (!m.Success) continue;
            var no = m.Groups[2].Success ? $"{m.Groups[1].Value}의{m.Groups[2].Value}" : m.Groups[1].Value;
            yield return new Article(no, m.Groups[3].Value, text);
        }
    }

    /// <summary>"0012"+"00" → "12", "0001"+"02" → "1의2".</summary>
    internal static string FormatBranchedNumber(string main, string? branch)
    {
        var no = main.TrimStart('0');
        if (no.Length == 0) no = "0";
        var b = (branch ?? "").TrimStart('0');
        return b.Length > 0 ? $"{no}의{b}" : no;
    }

    /// <summary>
    /// 별표·서식 검색 (target=licbyl, search=2 = 법령명으로 검색).
    /// 주의: 일부 법령(예: 건축법 시행령)은 이 색인에 없으므로, 본문 조회의 별표 목록(LawText.Annexes)을
    /// 우선 사용하고 이 검색은 보조로만 쓴다.
    /// </summary>
    public async Task<IReadOnlyList<AnnexSummary>> SearchAnnexesAsync(string lawName, CancellationToken ct = default)
    {
        var uri = BuildAnnexSearchUri(lawName);
        using var doc = await GetJsonAsync(uri, ct);
        return ParseAnnexSearch(doc.RootElement);
    }

    internal static IReadOnlyList<AnnexSummary> ParseAnnexSearch(JsonElement root)
    {
        var results = new List<AnnexSummary>();
        foreach (var container in root.EnumerateObject())
        {
            if (!container.Value.TryGetProperty("licbyl", out var items))
            {
                items = FindFirstArray(container.Value);
                if (items.ValueKind == JsonValueKind.Undefined) continue;
            }
            foreach (var item in EnumerateArrayOrSingle(items))
            {
                // 별표번호는 6자리 "001200" (= 별표 12), 뒤 2자리가 가지번호.
                var rawNo = GetString(item, "별표번호") ?? "";
                var number = rawNo.Length == 6 && rawNo.All(char.IsDigit)
                    ? FormatBranchedNumber(rawNo[..4], rawNo[4..])
                    : rawNo.TrimStart('0');
                results.Add(new AnnexSummary(
                    LawName: GetString(item, "관련법령명") ?? GetString(item, "법령명") ?? "",
                    Name: GetString(item, "별표명") ?? "",
                    Number: number,
                    Kind: GetString(item, "별표종류") ?? "별표",
                    Link: AbsoluteLink(GetString(item, "별표서식PDF파일링크") ?? GetString(item, "별표서식파일링크") ?? "")));
            }
        }
        return results;
    }

    private static string AbsoluteLink(string link) =>
        link.StartsWith('/') ? "https://www.law.go.kr" + link : link;

    internal Uri BuildAnnexSearchUri(string lawName) =>
        new($"{BaseUrl}/lawSearch.do?OC={Uri.EscapeDataString(_oc)}&target=licbyl" +
            $"&type=JSON&display=100&search=2&query={Uri.EscapeDataString(lawName)}");

    internal Uri BuildSearchUri(string query, LawTarget target) =>
        new($"{BaseUrl}/lawSearch.do?OC={Uri.EscapeDataString(_oc)}&target={TargetCode(target)}" +
            $"&type=JSON&display=50&query={Uri.EscapeDataString(query)}");

    internal Uri BuildServiceUri(string serialNo, LawTarget target) =>
        // 행정규칙 본문 조회는 MST가 아니라 ID(행정규칙일련번호) 파라미터를 쓴다 (실 API 검증).
        new($"{BaseUrl}/lawService.do?OC={Uri.EscapeDataString(_oc)}&target={TargetCode(target)}" +
            $"&type=JSON&{(target == LawTarget.AdminRule ? "ID" : "MST")}={Uri.EscapeDataString(serialNo)}");

    internal static string TargetCode(LawTarget t) => t switch
    {
        LawTarget.Law => "law",
        LawTarget.Ordinance => "ordin",
        LawTarget.AdminRule => "admrul",
        _ => "law",
    };

    private async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(uri, ct);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (text.TrimStart().StartsWith('<'))
            throw new InvalidOperationException(
                "법제처 API가 JSON 대신 HTML을 반환했습니다. OC 키가 유효한지, open.law.go.kr에서 API 사용 신청이 완료됐는지 확인하세요.");
        return JsonDocument.Parse(text);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    /// <summary>문자열 또는 문자열 배열로 오는 내용 필드를 하나의 텍스트로 합친다.</summary>
    private static string? GetText(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        var flat = FlattenStrings(v).ToList();
        return flat.Count == 0 ? null : string.Join("\n", flat);
    }

    private static IEnumerable<string> FlattenStrings(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) yield return s;
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    foreach (var inner in FlattenStrings(item))
                        yield return inner;
                break;
        }
    }

    private static IEnumerable<JsonElement> EnumerateArrayOrSingle(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
            foreach (var item in el.EnumerateArray()) yield return item;
        else if (el.ValueKind == JsonValueKind.Object)
            yield return el;
    }

    private static JsonElement FindFirstArray(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return default;
        foreach (var p in el.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Array) return p.Value;
        return default;
    }
}

/// <summary>검색 결과 요약. 검토법규 목록(법령명 + 시행일자)에 그대로 쓰인다.</summary>
public sealed record LawSummary(
    string Name, string SerialNo, string EffectiveDate, string RevisionDate, string Kind, string Department);

/// <summary>법령 본문. 조문 원문은 이 타입을 통해서만 검토서로 들어간다.</summary>
public sealed record LawText(
    string Name, string EffectiveDate, IReadOnlyList<Article> Articles, IReadOnlyList<Annex>? Annexes = null)
{
    public Article? FindArticle(string articleNo) =>
        Articles.FirstOrDefault(a => a.Number.TrimStart('0') == articleNo.TrimStart('0'));

    /// <summary>
    /// 조문 제목 키워드로 조문을 찾는다.
    /// 자치법규는 지자체마다 조문번호가 달라 번호를 고정할 수 없으므로
    /// (예: 공개공지 — 대전 건축조례 34조, 타 시는 다른 번호) 제목으로 매칭한다.
    /// </summary>
    public IReadOnlyList<Article> FindArticlesByTitle(string keyword)
    {
        var normalized = Normalize(keyword);
        return Articles.Where(a => Normalize(a.Title).Contains(normalized)).ToList();
    }

    /// <summary>"별표 11" / "별표 1의2" 표기로 본문의 별표를 찾는다.</summary>
    public Annex? FindAnnex(string annexName)
    {
        var no = Normalize(annexName).Replace("별표", "");
        return (Annexes ?? Array.Empty<Annex>()).FirstOrDefault(a => a.Number == no);
    }

    private static string Normalize(string s) => s.Replace(" ", "").Replace("ㆍ", "·");
}

/// <summary>
/// 법령 본문에 포함된 별표. 법령 별표는 <see cref="Content"/>에 표 텍스트가 들어오지만,
/// 자치법규 별표는 HWP 첨부파일이라 비어 있고 <see cref="Link"/>만 쓸 수 있다.
/// </summary>
public sealed record Annex(string Number, string Title, string Kind, string Link, string Content = "");

/// <summary>별표·서식 검색 결과 (보조 경로 — 본문의 Annexes를 우선 사용).</summary>
public sealed record AnnexSummary(string LawName, string Name, string Number, string Kind, string Link);

public sealed record Article(string Number, string Title, string Body);
