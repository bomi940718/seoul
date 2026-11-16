using System.Text;
using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>
/// **일조 등의 확보를 위한 건축물의 높이 제한**(건축법 제61조) 중 조례 위임 부분을 읽는다.
///
/// 건축법 시행령 제86조가 두 가지를 조례로 넘긴다:
///  · 제1항 — 전용주거·일반주거지역에서 **정북방향 인접대지경계선 이격거리**
///    (대개 "높이 10미터 이하 1.5미터 / 초과 부분은 높이의 2분의 1"이지만 조례가 정하는 값이다)
///  · 제3항제2호 — 같은 대지에서 **두 동이 마주보는 경우의 인동거리 배수**.
///    여기가 지자체마다 실제로 갈린다: 대전·속초 1배 / 안양 0.8배 / 서울 0.5배.
///    공동주택 배치에 바로 영향을 주므로 값을 조례에서 가져와야 한다.
///
/// 값을 해석하지 않고 **조문 문구 그대로** 옮긴다 — 도시형 생활주택·정비사업 단서가 붙어 있어
/// 배수 하나로 줄이면 틀린다. 적용 여부(주거지역인지, 공동주택인지)는 AI·사람이 판단한다.
/// </summary>
public static class SunlightHeightResolver
{
    private const string ArticleTitle = "일조";

    public static SunlightStandard Resolve(LawText? ordinance)
    {
        if (ordinance is null) return new SunlightStandard();

        foreach (var article in ordinance.FindArticlesByTitle(ArticleTitle))
        {
            var standard = new SunlightStandard
            {
                SourceName = ordinance.Name,
                ArticleNo = article.Number,
                EffectiveDate = ordinance.EffectiveDate,
            };

            foreach (var raw in ArticleText.Paragraphs(article.Body))
            {
                var paragraph = ArticleText.StripTags(raw);
                if (standard.NorthSetback.Count == 0 && IsNorthSetback(paragraph))
                    standard.NorthSetback = LiveClauses(paragraph);
                else if (standard.BetweenBuildings.Count == 0 && IsBetweenBuildings(paragraph))
                    standard.BetweenBuildings = LiveClauses(paragraph);
            }

            if (standard.NorthSetback.Count > 0 || standard.BetweenBuildings.Count > 0) return standard;
        }
        return new SunlightStandard();
    }

    /// <summary>
    /// 정북방향 이격거리를 정하는 항인지. 조례마다 "정북방향"을 안 쓰기도 해서(대전)
    /// 위임 근거(영 제86조제1항)도 함께 본다. "이상"이 있어야 거리를 정하는 항이다 —
    /// "2층 이하로서 높이가 8미터 이하인 건축물은 적용하지 아니한다"(안양 제2항) 같은
    /// 적용 제외 항을 기준으로 잘못 잡지 않기 위해서다.
    /// </summary>
    private static bool IsNorthSetback(string p) =>
        (p.Contains("정북") || p.Contains("제86조제1항")) && p.Contains("이상");

    /// <summary>
    /// 같은 대지에서 두 동이 마주보는 경우(인동거리)를 정하는 항인지.
    /// 대전 조례처럼 "마주보고"라는 말 없이 위임 근거(영 제86조제3항제2호)만 쓰기도 한다.
    /// </summary>
    private static bool IsBetweenBuildings(string p) =>
        (p.Contains("마주보") || p.Contains("제86조제3항제2호")) && (p.Contains("배") || p.Contains("미터"));

    /// <summary>살아 있는 호만 (삭제된 호는 버린다). 호가 없으면 항 본문 자체를 한 줄로 본다.</summary>
    private static List<string> LiveClauses(string paragraph)
    {
        var clauses = ArticleText.Clauses(paragraph)
            .Where(c => !c.StartsWith("삭제") && c.Length > 0).ToList();
        return clauses.Count > 0 ? clauses : new List<string> { paragraph.Trim() };
    }
}

/// <summary>일조 높이제한 중 조례가 정하는 값들. 둘 다 비어 있으면 조회 실패.</summary>
public sealed class SunlightStandard
{
    /// <summary>정북방향 인접대지경계선 이격거리 (높이 구간별).</summary>
    public IReadOnlyList<string> NorthSetback { get; set; } = Array.Empty<string>();
    /// <summary>같은 대지에서 두 동이 마주보는 경우의 인동거리.</summary>
    public IReadOnlyList<string> BetweenBuildings { get; set; } = Array.Empty<string>();

    public string SourceName { get; set; } = "";
    public string ArticleNo { get; set; } = "";
    public string EffectiveDate { get; set; } = "";

    public string? Basis => SourceName.Length > 0 && ArticleNo.Length > 0
        ? $"{SourceName} 제{ArticleNo.TrimStart('0')}조" : null;

    /// <summary>요약 검토표의 "법적 기준" 칸. 조문 문구를 그대로 옮긴다.</summary>
    public string? CriterionText()
    {
        if (NorthSetback.Count == 0 && BetweenBuildings.Count == 0) return null;

        var sb = new StringBuilder();
        if (NorthSetback.Count > 0)
            sb.AppendLine($"정북방향 이격: {string.Join(" / ", NorthSetback)}");
        if (BetweenBuildings.Count > 0)
            sb.AppendLine($"인동거리(마주보는 두 동): {string.Join(" / ", BetweenBuildings)}");
        if (Basis is not null) sb.Append($"({Basis})");
        return sb.ToString().Trim();
    }
}
