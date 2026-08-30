using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            // 사유가 길어져 응답이 잘리면 판정을 통째로 잃는다(실제로 "건축물의 내화구조"에서 발생).
            // 여유를 두고, 그래도 잘린 경우는 ParsePartial이 판정을 살린다.
            max_tokens = 2048,
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
        var text = ExtractText(doc.RootElement);
        if (text is null)
        {
            // content[0]이 text 블록이 아닐 수 있다(사고 블록 등). 예전에는 여기서 예외가 나
            // 검토 전체가 중단됐다 — 이제는 해당 항목만 확인필요로 두고 계속 진행한다.
            var stop = doc.RootElement.TryGetProperty("stop_reason", out var s) ? s.GetString() : null;
            return new Judgment(Applicability.확인필요,
                $"판정 응답에서 텍스트를 찾지 못했습니다 (stop_reason: {stop ?? "?"}). 조문 원문을 직접 확인하세요.");
        }
        return ParseJudgment(text);
    }

    /// <summary>응답의 content 블록 중 첫 번째 text 블록을 꺼낸다. 없으면 null.</summary>
    internal static string? ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object) continue;
            if (block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        return null;
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
                return new Judgment(ToApplicability(verdict), reason ?? "");
            }
            catch (JsonException) { /* 아래 부분 파싱으로 복구 */ }
        }
        return ParsePartial(text);
    }

    private static readonly Regex VerdictPattern =
        new("\"판정\"\\s*:\\s*\"(적용|해당없음|확인필요)\"", RegexOptions.Compiled);
    private static readonly Regex ReasonPattern =
        new("\"사유\"\\s*:\\s*\"(.*)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// JSON이 미완성인 응답(max_tokens 초과로 문장 중간에 잘린 경우 등)에서 판정과 사유를 살린다.
    /// 판정 자체는 응답 앞부분에 있으므로, 사유가 잘렸다고 판정까지 버리면 안 된다.
    /// </summary>
    internal static Judgment ParsePartial(string text)
    {
        var verdict = VerdictPattern.Match(text);
        if (!verdict.Success)
            return new Judgment(Applicability.확인필요, $"판정 응답 해석 실패: {text}");

        var reason = "";
        if (ReasonPattern.Match(text) is { Success: true } m)
        {
            // 닫는 따옴표까지만 취하고(잘렸으면 끝까지), JSON 이스케이프를 되돌린다.
            var raw = m.Groups[1].Value;
            var sb = new StringBuilder();
            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                if (c == '\\' && i + 1 < raw.Length)
                {
                    sb.Append(raw[++i] switch { 'n' => '\n', 't' => '\t', var other => other });
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            reason = sb.ToString().TrimEnd();
            if (!text.TrimEnd().EndsWith('}'))
                reason += " (응답이 잘려 사유가 일부만 기록되었습니다.)";
        }
        return new Judgment(ToApplicability(verdict.Groups[1].Value), reason);
    }

    private static Applicability ToApplicability(string? verdict) => verdict switch
    {
        "적용" => Applicability.적용,
        "해당없음" => Applicability.해당없음,
        _ => Applicability.확인필요,
    };
}
