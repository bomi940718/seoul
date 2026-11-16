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
    /// <summary>
    /// 요약 검토표·인증 표에서의 표기 순서. 실무 표준 서식(HWP)의 순서를 그대로 따르며
    /// 협력사와 공유하는 서식이므로 임의로 바꾸지 않는다. 값이 없으면 파일 순서를 쓴다.
    /// </summary>
    public int? Order { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public JudgmentType Judgment { get; set; }
    public List<LawBasis> Basis { get; set; } = new(); // 근거 법령·조문 (우선순위 순서대로)
    public string? LegalCriterion { get; set; }        // 법적 기준 요약 (요약표용)
    public string? Note { get; set; }
    /// <summary>
    /// AI에 넘기기 전에 코드로 거르는 대상 요건(행위·규모·주체). 하나라도 명백히 못 미치면 "해당없음"으로 확정한다.
    /// 숫자 비교로 끝나는 판단을 AI에게 맡겨 "확인필요"가 나오던 문제(연면적 331㎡인데 에너지효율 확인필요 등) 때문.
    /// </summary>
    public ItemScreen? Screen { get; set; }
}

/// <summary>항목의 대상 요건. 모두 "이 조건을 만족해야 대상"이라는 뜻이다.</summary>
public sealed class ItemScreen
{
    /// <summary>신축·증축·개축·재축만 대상 (면적이 늘지 않는 리모델링·대수선·용도변경은 대상 아님).</summary>
    public bool NewWorkOnly { get; set; }
    /// <summary>대상 면적 하한(㎡): 신축은 연면적, 그 밖에는 증가 연면적.</summary>
    public double? MinArea { get; set; }
    /// <summary>국가·지방자치단체·공공기관이 건축하는 경우만 대상.</summary>
    public bool PublicOnly { get; set; }
}

/// <summary>근거 법령 참조. LawName의 "{시}"/"{구}"는 프로젝트의 지자체명으로 치환된다.</summary>
public sealed class LawBasis
{
    public string LawName { get; set; } = "";
    public string? Article { get; set; }               // 조문 번호 ("48의2" 형식 지원)
    /// <summary>
    /// 조문 제목 키워드. 조례처럼 지자체마다 조문번호가 다른 경우 번호 대신 사용.
    /// "|"로 여러 개를 줄 수 있다(예: "심의|건축위원회").
    /// </summary>
    public string? ArticleTitleKeyword { get; set; }
    /// <summary>
    /// 제목으로 못 찾으면 본문에서 찾을 키워드. 서울시 건축 조례의 심의 사항은 제목이 "기능 및 절차 등"이라
    /// 제목에 "심의"가 없다 — 본문의 "심의사항"으로 찾는다.
    /// </summary>
    public string? ArticleBodyKeyword { get; set; }
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

    /// <summary>
    /// 표준 체크리스트를 로드한다. 배포된 단일 exe에서도 동작하도록 어셈블리에 내장된 사본을 쓰되,
    /// 실행 폴더에 checklists/standard.json이 있으면(현장 커스터마이즈) 그 파일을 우선한다.
    /// </summary>
    public static List<ChecklistItem> LoadDefault()
    {
        var external = Path.Combine(AppContext.BaseDirectory, "checklists", "standard.json");
        if (File.Exists(external)) return Load(external);

        var asm = typeof(ChecklistLoader).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("standard.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("내장 체크리스트 리소스를 찾을 수 없습니다.");
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return LoadFromJson(reader.ReadToEnd());
    }
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
