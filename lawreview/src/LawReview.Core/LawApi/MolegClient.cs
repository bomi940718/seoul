using System.Text.Json;

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

    /// <summary>법령/자치법규 검색. 이름이 정확할수록 첫 결과가 원하는 법령이다.</summary>
    public async Task<IReadOnlyList<LawSummary>> SearchAsync(
        string query, LawTarget target = LawTarget.Law, CancellationToken ct = default)
    {
        var uri = BuildSearchUri(query, target);
        using var doc = await GetJsonAsync(uri, ct);
        var root = doc.RootElement;

        // 응답 루트: 법령은 "LawSearch", 자치법규는 "OrdinSearch" 등 target에 따라 다르다.
        var results = new List<LawSummary>();
        foreach (var container in root.EnumerateObject())
        {
            if (!container.Value.TryGetProperty(target == LawTarget.Ordinance ? "law" : "law", out var items))
            {
                // 일부 응답은 "law" 배열 키를 쓰지 않으므로 배열 프로퍼티를 탐색한다.
                items = FindFirstArray(container.Value);
                if (items.ValueKind == JsonValueKind.Undefined) continue;
            }
            foreach (var item in EnumerateArrayOrSingle(items))
            {
                results.Add(new LawSummary(
                    Name: GetString(item, "법령명한글") ?? GetString(item, "자치법규명") ?? "",
                    SerialNo: GetString(item, "법령일련번호") ?? GetString(item, "자치법규일련번호") ?? "",
                    EffectiveDate: GetString(item, "시행일자") ?? "",
                    RevisionDate: GetString(item, "공포일자") ?? "",
                    Kind: GetString(item, "법령구분명") ?? GetString(item, "자치법규종류") ?? "",
                    Department: GetString(item, "소관부처명") ?? GetString(item, "지자체기관명") ?? ""));
            }
        }
        return results;
    }

    /// <summary>법령 본문 조회. 일련번호(MST)로 현행 법령 전문과 조문 목록을 가져온다.</summary>
    public async Task<LawText> GetLawTextAsync(
        string serialNo, LawTarget target = LawTarget.Law, CancellationToken ct = default)
    {
        var uri = BuildServiceUri(serialNo, target);
        using var doc = await GetJsonAsync(uri, ct);
        var root = doc.RootElement;

        string lawName = "";
        string effectiveDate = "";
        var articles = new List<Article>();

        foreach (var container in root.EnumerateObject())
        {
            var body = container.Value;
            if (body.TryGetProperty("기본정보", out var basic))
            {
                lawName = GetString(basic, "법령명_한글") ?? GetString(basic, "법령명한글")
                          ?? GetString(basic, "자치법규명") ?? "";
                effectiveDate = GetString(basic, "시행일자") ?? "";
            }
            if (body.TryGetProperty("조문", out var joSection))
            {
                var units = joSection.TryGetProperty("조문단위", out var u) ? u : joSection;
                foreach (var jo in EnumerateArrayOrSingle(units))
                {
                    // 조문여부 "조문"만 실제 조문이다 ("전문"은 장·절 제목).
                    if (GetString(jo, "조문여부") is string yn && yn != "조문") continue;
                    articles.Add(ParseArticle(jo));
                }
            }
        }
        return new LawText(lawName, effectiveDate, articles);
    }

    private static Article ParseArticle(JsonElement jo)
    {
        var no = GetString(jo, "조문번호") ?? "";
        var title = GetString(jo, "조문제목") ?? "";
        var parts = new List<string>();
        if (GetString(jo, "조문내용") is string head && head.Length > 0) parts.Add(head.Trim());

        if (jo.TryGetProperty("항", out var hangs))
        {
            foreach (var hang in EnumerateArrayOrSingle(hangs))
            {
                if (GetString(hang, "항내용") is string hc && hc.Length > 0) parts.Add(hc.Trim());
                if (hang.TryGetProperty("호", out var hos))
                {
                    foreach (var ho in EnumerateArrayOrSingle(hos))
                    {
                        if (GetString(ho, "호내용") is string oc && oc.Length > 0) parts.Add(oc.Trim());
                        if (ho.TryGetProperty("목", out var moks))
                            foreach (var mok in EnumerateArrayOrSingle(moks))
                                if (GetString(mok, "목내용") is string mc && mc.Length > 0) parts.Add(mc.Trim());
                    }
                }
            }
        }
        return new Article(no, title, string.Join("\n", parts));
    }

    internal Uri BuildSearchUri(string query, LawTarget target) =>
        new($"{BaseUrl}/lawSearch.do?OC={Uri.EscapeDataString(_oc)}&target={TargetCode(target)}" +
            $"&type=JSON&display=20&query={Uri.EscapeDataString(query)}");

    internal Uri BuildServiceUri(string serialNo, LawTarget target) =>
        new($"{BaseUrl}/lawService.do?OC={Uri.EscapeDataString(_oc)}&target={TargetCode(target)}" +
            $"&type=JSON&MST={Uri.EscapeDataString(serialNo)}");

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
public sealed record LawText(string Name, string EffectiveDate, IReadOnlyList<Article> Articles)
{
    public Article? FindArticle(string articleNo) =>
        Articles.FirstOrDefault(a => a.Number.TrimStart('0') == articleNo.TrimStart('0'));
}

public sealed record Article(string Number, string Title, string Body);
