using System.Text;
using System.Text.Json;

namespace LawReview.Core.Municipal;

/// <summary>
/// 서울도시공간포털(urban.seoul.go.kr) 지구단위계획 조회 클라이언트.
///
/// 실측(2026-07)으로 확인한 비공개 API:
///  · 목록: POST /ctymgrpln/getDstplanList.json — JSON 본문(UTF-8 필수), 세션 불필요.
///    응답은 Spring Page({"content":[...],"totalElements":N}). classifyG "UQQ301" = 지구단위계획.
///  · 고시문 PDF: GET /{tnNtfc.tnNtfcImage.aImagePath}/{aImageName} — 정적 경로 직접 다운로드.
/// 조서정보(구역면적 기정/변경/변경후)는 목록 응답의 areaPrev/areaChange/areaAfter에 담겨 있다.
/// </summary>
public sealed class SeoulUrbanPortalClient : IDistrictPlanProvider
{
    private const string BaseUrl = "https://urban.seoul.go.kr";
    private const string ListPath = "/ctymgrpln/getDstplanList.json";
    /// <summary>포털의 지구단위계획 조회 화면 (수동 열람 안내용).</summary>
    public const string PortalPageUrl = BaseUrl + "/view/html/PMNU4030200001";

    private readonly HttpClient _http;

    public SeoulUrbanPortalClient(HttpClient http) => _http = http;

    public string Province => "서울특별시";

    public async Task<IReadOnlyList<DistrictPlanRecord>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            pageNo = 1,
            pageSize = 30,
            wtnnccode = "WPCD02",
            classifyType = "G",
            classifyG = "UQQ301",   // 지구단위계획
            classifyM = "",
            keywordList = new[] { keyword },
            srchType = "",
            siteCode = "",
            bgnDt = (string?)null,
            endDt = (string?)null,
            noticeBgnDt = (string?)null,
            noticeEndDt = (string?)null,
            tnNtfc = new { organCode = "" },
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(BaseUrl + ListPath, content, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return ParseList(doc.RootElement);
    }

    internal static IReadOnlyList<DistrictPlanRecord> ParseList(JsonElement root)
    {
        var results = new List<DistrictPlanRecord>();
        if (!root.TryGetProperty("content", out var items) || items.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var item in items.EnumerateArray())
        {
            var notice = item.TryGetProperty("tnNtfc", out var n) && n.ValueKind == JsonValueKind.Object ? n : default;

            string pdfUrl = "";
            if (notice.ValueKind == JsonValueKind.Object
                && notice.TryGetProperty("tnNtfcImage", out var img) && img.ValueKind == JsonValueKind.Object
                && GetString(img, "aImagePath") is { Length: > 0 } path
                && GetString(img, "aImageName") is { Length: > 0 } name)
            {
                pdfUrl = $"{BaseUrl}/{path.TrimStart('/')}/{Uri.EscapeDataString(name)}";
            }

            var organ = notice.ValueKind == JsonValueKind.Object
                ? GetString(notice, "subject")?.Split('|').FirstOrDefault()?.Trim() ?? ""
                : "";
            var noticeNo = notice.ValueKind == JsonValueKind.Object ? GetString(notice, "noticeNo") ?? "" : "";
            var noticeDate = notice.ValueKind == JsonValueKind.Object
                ? (GetString(notice, "noticeDate") ?? "").Split('T').FirstOrDefault() ?? ""
                : "";

            results.Add(new DistrictPlanRecord(
                ZoneName: GetString(item, "zoneName") ?? "",
                Location: GetString(item, "locationName") ?? "",
                NoticeOrgan: organ,
                NoticeNo: noticeNo.Length > 0 ? $"제{noticeNo}호" : "",
                NoticeDate: noticeDate,
                NoticeTitle: notice.ValueKind == JsonValueKind.Object ? GetString(notice, "title") ?? "" : "",
                AreaAfter: GetDouble(item, "areaAfter"),
                NoticePdfUrl: pdfUrl,
                PortalUrl: PortalPageUrl));
        }
        return results;
    }

    public async Task<bool> DownloadNoticePdfAsync(DistrictPlanRecord record, string filePath, CancellationToken ct = default)
    {
        if (record.NoticePdfUrl.Length == 0) return false;
        using var resp = await _http.GetAsync(record.NoticePdfUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return false;
        if (resp.Content.Headers.ContentType?.MediaType is string mt && !mt.Contains("pdf") && !mt.Contains("octet"))
            return false;   // 파일이 없으면 HTML 오류 페이지가 올 수 있다
        await using var file = File.Create(filePath);
        await resp.Content.CopyToAsync(file, ct);
        return true;
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static double? GetDouble(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : null;
}
