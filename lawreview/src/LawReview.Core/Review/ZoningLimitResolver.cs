using System.Globalization;
using System.Text.RegularExpressions;
using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>
/// 용도지역별 법정 건폐율·용적률을 지자체 **도시계획 조례**에서 뽑아낸다.
///
/// 지구단위계획이 없어도 검토가 되어야 한다 — 국토계획법 제77·78조가 시행령을 거쳐
/// 지자체 도시계획조례로 위임되고, 조례에 용도지역별 한도가 호(號)로 나열되어 있다.
/// (예: 대전광역시 도시계획 조례 제45조 "12. 일반공업지역: 70퍼센트 이하")
///
/// 지구단위계획이 있으면 그 값이 우선하므로, 여기서 얻은 값은 "조례 기준"으로만 쓰고
/// 사용자가 덮어쓸 수 있게 둔다 (적용 우선순위: 지구단위계획 &gt; 조례 &gt; 시행령).
/// </summary>
public static class ZoningLimitResolver
{
    /// <summary>
    /// 조문 제목의 핵심어. **제목 표기가 지자체마다 다르다** — 대전 "용도지역 안에서의 건폐율",
    /// 안양 "용도지역에서의 건폐율". 예전에는 앞의 것만 정확히 찾아서 안양에서는 법정 한도가
    /// 통째로 비었다("조례에서 찾지 못했습니다"). 그래서 "용도지역"과 "건폐율"이 함께 든 제목을 찾되,
    /// 같은 말이 들어가는 완화·특례 조문에는 걸리지 않게 뺀다.
    /// </summary>
    private const string CoverageWord = "건폐율";
    private const string FarWord = "용적률";
    private static readonly string[] ExcludedTitleWords = { "완화", "특례", "강화", "경관지구", "예외" };

    public static ZoningLimitLookup Resolve(LawText? ordinance, IEnumerable<string> useZones)
    {
        var zones = useZones.Select(z => z.Trim()).Where(z => z.Length > 0).ToList();
        if (ordinance is null || zones.Count == 0) return new ZoningLimitLookup();

        var coverage = Find(ordinance, CoverageWord, zones);
        var far = Find(ordinance, FarWord, zones);

        return new ZoningLimitLookup
        {
            CoverageRatio = coverage?.Value,
            CoverageBasis = Basis(ordinance, coverage),
            FloorAreaRatio = far?.Value,
            FloorAreaBasis = Basis(ordinance, far),
            MatchedZone = coverage?.Zone ?? far?.Zone,
        };
    }

    /// <summary>"안양시 도시계획 조례 제50조" + 단서가 있으면 다음 줄에 "※ 단서: …".</summary>
    private static string? Basis(LawText ordinance, Hit? hit) => hit is null ? null
        : $"{ordinance.Name} 제{hit.ArticleNo}조" + (hit.Proviso is null ? "" : $"\n※ 단서: {hit.Proviso}");

    private static Hit? Find(LawText ordinance, string word, List<string> zones)
    {
        foreach (var article in ordinance.FindArticlesByTitle(word).Where(IsZoneArticle))
        {
            var hit = MatchZone(article.Body, zones);
            if (hit is not null) return hit with { ArticleNo = article.Number };
        }
        return null;
    }

    /// <summary>용도지역별 한도를 정하는 본 조문인지 (완화·특례·경관지구 조문 제외).</summary>
    internal static bool IsZoneArticle(Article article) =>
        article.Title.Contains("용도지역") && !ExcludedTitleWords.Any(article.Title.Contains);

    // "12. 일반공업지역: 70퍼센트 이하" / "8. 일반상업지역: 1,100퍼센트 이하"
    private static readonly Regex ZoneLine = new(
        @"\d+\.\s*([가-힣0-9·ㆍ\s]+?)\s*:\s*([\d,]+(?:\.\d+)?)\s*퍼센트", RegexOptions.Compiled);

    /// <summary>
    /// 조문 본문의 호 목록에서 프로젝트 용도지역과 일치하는 줄을 찾는다.
    /// 용도지역이 여러 개면(도시지역 + 일반공업지역 등) 가장 구체적인 것,
    /// 즉 조례 목록에 실제로 등장하는 것을 쓴다.
    /// </summary>
    internal static Hit? MatchZone(string body, List<string> zones)
    {
        foreach (Match m in ZoneLine.Matches(body ?? ""))
        {
            var listed = m.Groups[1].Value.Replace(" ", "");
            foreach (var zone in zones)
            {
                var z = zone.Replace(" ", "");
                // 조례의 표기와 입력이 정확히 같을 때만 인정한다.
                // ("도시지역"이 "생산녹지지역" 같은 줄에 부분 일치하는 사고를 막는다)
                if (listed != z) continue;
                var raw = m.Groups[2].Value.Replace(",", "");
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return new Hit(v, zone, "", ProvisoAfter(body!, m.Index + m.Length));
            }
        }
        return null;
    }

    /// <summary>
    /// 값 바로 뒤에 붙은 단서를 꺼낸다. 없으면 null.
    ///
    /// 안양 조례: "13. 준공업지역: 300퍼센트 이하(「건축법 시행령」 별표 1 제17호의 공장 및 …
    /// 지식산업센터의 경우에는 350퍼센트 이하로 한다)". 앞의 300만 집으면 **공장 프로젝트의 법정
    /// 용적률이 틀린 값으로 채워진다.** 용도 판단은 코드가 단정하지 않고(원칙 5) 단서를 그대로 보여준다.
    /// </summary>
    internal static string? ProvisoAfter(string body, int from)
    {
        // 다음 호("14. …")가 시작되기 전까지가 이 호의 나머지다.
        var next = NextClause.Match(body, from);
        var rest = body[from..(next.Success ? next.Index : Math.Min(body.Length, from + 300))].Trim();
        var open = rest.IndexOf('(');
        if (open < 0) return null;

        var proviso = rest[(open + 1)..].TrimEnd(')', ' ', '.');
        // 다른 한도 값이 들어 있어야 단서다 — "(…를 제외한다)" 같은 설명은 값이 바뀌지 않는다.
        return proviso.Contains("퍼센트") ? proviso : null;
    }

    private static readonly Regex NextClause = new(@"\d+\.\s*[가-힣「]", RegexOptions.Compiled);

    internal sealed record Hit(double Value, string Zone, string ArticleNo, string? Proviso = null);
}

/// <summary>도시계획조례에서 찾은 법정 한도. 못 찾으면 각 값이 null이다.</summary>
public sealed class ZoningLimitLookup
{
    public double? CoverageRatio { get; set; }
    public string? CoverageBasis { get; set; }
    public double? FloorAreaRatio { get; set; }
    public string? FloorAreaBasis { get; set; }
    /// <summary>실제로 매칭된 용도지역 (예: 일반공업지역).</summary>
    public string? MatchedZone { get; set; }
}
