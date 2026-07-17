using System.Text;
using System.Text.Json;
using LawReview.Core.Models;
using LawReview.Core.Review;

namespace LawReview.Core.Ai;

/// <summary>Anthropic Messages API를 사용하는 판정자.</summary>
public sealed class ClaudeJudgmentProvider : IJudgmentProvider
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public ClaudeJudgmentProvider(HttpClient http, string apiKey, string model = "claude-sonnet-5")
    {
        _http = http;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<Judgment> JudgeAsync(ChecklistItem item, IReadOnlyList<CitedArticle> articles,
        ProjectInput project, CancellationToken ct = default)
    {
        var userPrompt = BuildPrompt(item, articles, project);

        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            max_tokens = 1024,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude API 오류 ({(int)resp.StatusCode}): {respText}");

        using var doc = JsonDocument.Parse(respText);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        return ParseJudgment(text);
    }

    private const string SystemPrompt =
        "당신은 건축 인허가 법규검토 전문가다. 제시된 조문 원문과 프로젝트 개요를 근거로, " +
        "해당 검토 항목이 이 프로젝트에 적용되는지 판정한다. " +
        "반드시 JSON 하나만 출력한다: {\"판정\":\"적용|해당없음|확인필요\",\"사유\":\"근거 조항을 인용한 한두 문장\"}. " +
        "제시된 조문 밖의 내용을 근거로 삼지 말고, 조문만으로 판단이 불가능하면 \"확인필요\"로 답한다.";

    internal static string BuildPrompt(ChecklistItem item, IReadOnlyList<CitedArticle> articles, ProjectInput p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 프로젝트 개요");
        sb.AppendLine($"- 사업명: {p.ProjectName}");
        sb.AppendLine($"- 대지위치: {p.SiteAddress} ({p.Province} {p.City})");
        sb.AppendLine($"- 지역/지구: {string.Join(", ", p.UseZones)}");
        sb.AppendLine($"- 용도: {p.PrimaryUse}, 대지면적: {p.SiteArea:#,##0.00} ㎡");
        sb.AppendLine($"- 규모: 지상 {p.PlannedFloorsAbove}층 / 지하 {p.PlannedFloorsBelow}층, 연면적 {p.GrossFloorArea:#,##0.00} ㎡, 건축면적 {p.PlannedBuildingArea:#,##0.00} ㎡");
        sb.AppendLine();
        sb.AppendLine($"## 검토 항목: {item.Title}");
        if (item.LegalCriterion is not null) sb.AppendLine($"법적 기준 요약: {item.LegalCriterion}");
        sb.AppendLine();
        sb.AppendLine("## 조문 원문 (법제처 현행)");
        foreach (var a in articles)
        {
            sb.AppendLine($"### {a.LawName} 제{a.ArticleNo}조 {a.Title} (시행 {a.EffectiveDate})");
            sb.AppendLine(a.Body);
            sb.AppendLine();
        }
        sb.AppendLine("이 항목이 위 프로젝트에 적용되는가?");
        return sb.ToString();
    }

    internal static Judgment ParseJudgment(string text)
    {
        // 응답에서 첫 JSON 객체를 찾아 파싱한다.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                using var doc = JsonDocument.Parse(text[start..(end + 1)]);
                var verdict = doc.RootElement.TryGetProperty("판정", out var v) ? v.GetString() : null;
                var reason = doc.RootElement.TryGetProperty("사유", out var r) ? r.GetString() : null;
                var applicability = verdict switch
                {
                    "적용" => Applicability.적용,
                    "해당없음" => Applicability.해당없음,
                    _ => Applicability.확인필요,
                };
                return new Judgment(applicability, reason ?? "");
            }
            catch (JsonException) { /* 아래 fallback */ }
        }
        return new Judgment(Applicability.확인필요, $"판정 응답 해석 실패: {text}");
    }
}
