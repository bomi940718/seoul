using System.Text.Json;
using System.Text.Json.Serialization;
using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>판정 방식.</summary>
public enum JudgmentType
{
    Quantitative,  // 정량: 코드가 계산해서 판정 (건폐율·용적률·주차 등)
    Ai,            // 정성: 조문 원문 + 프로젝트 개요를 근거로 AI가 적용 여부 판정
    Manual,        // 수동: 자동 판정 불가 → "확인 필요"로 출력 (도면 규제 등)
}

/// <summary>판정 결과.</summary>
public enum Applicability
{
    적용,
    해당없음,
    확인필요,
}

/// <summary>검토 항목 하나의 정의. checklists/*.json에서 로드된다.</summary>
public sealed class ChecklistItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";            // 항목명 (예: 대지 안의 조경)
    public string Section { get; set; } = "";          // 검토서의 소속 섹션 (예: 제4장 건축물의 대지와 도로)
    public bool InSummary { get; set; }                // 요약 검토표 포함 여부
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public JudgmentType Judgment { get; set; }
    public List<LawBasis> Basis { get; set; } = new(); // 근거 법령·조문 (우선순위 순서대로)
    public string? LegalCriterion { get; set; }        // 법적 기준 요약 (요약표용)
    public string? Note { get; set; }
}

/// <summary>근거 법령 참조. LawName의 "{시}"/"{구}"는 프로젝트의 지자체명으로 치환된다.</summary>
public sealed class LawBasis
{
    public string LawName { get; set; } = "";
    public string? Article { get; set; }               // 조문 번호 ("48의2" 형식 지원)
    /// <summary>조문 제목 키워드. 조례처럼 지자체마다 조문번호가 다른 경우 번호 대신 사용.</summary>
    public string? ArticleTitleKeyword { get; set; }
    /// <summary>별표 참조 (예: "별표 12"). 내용은 파일이므로 검토서에 원문 링크로 안내된다.</summary>
    public string? Annex { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LawTarget Target { get; set; } = LawTarget.Law;
}

public static class ChecklistLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static List<ChecklistItem> Load(string jsonPath) =>
        JsonSerializer.Deserialize<List<ChecklistItem>>(File.ReadAllText(jsonPath), Options)
        ?? throw new InvalidOperationException($"체크리스트를 읽을 수 없습니다: {jsonPath}");

    public static List<ChecklistItem> LoadFromJson(string json) =>
        JsonSerializer.Deserialize<List<ChecklistItem>>(json, Options)
        ?? throw new InvalidOperationException("체크리스트 JSON 파싱 실패");
}

/// <summary>검토 항목 하나의 최종 결과 행. 검토서 표의 한 줄에 대응한다.</summary>
public sealed class ReviewRow
{
    public required ChecklistItem Item { get; init; }
    public Applicability Applicability { get; set; } = Applicability.확인필요;
    public string? Reason { get; set; }                // 판정 사유
    public string? CriterionText { get; set; }         // 법적 기준 (요약표)
    public string? CalculationText { get; set; }       // 설계 기준/산정식 (요약표)
    public List<CitedArticle> Citations { get; set; } = new(); // 인용 조문 원문 (상세표)
}

/// <summary>검토서에 인용된 조문. 법제처 API에서 가져온 원문만 담는다.</summary>
public sealed record CitedArticle(string LawName, string ArticleNo, string Title, string Body, string EffectiveDate);
