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
    /// <summary>조문 제목으로 찾는다. 완화·강화 조문("완화적용" 등)에 걸리지 않도록 정확한 제목을 쓴다.</summary>
    private const string CoverageTitle = "용도지역 안에서의 건폐율";
    private const string FarTitle = "용도지역 안에서의 용적률";

    public static ZoningLimitLookup Resolve(LawText? ordinance, IEnumerable<string> useZones)
    {
        var zones = useZones.Select(z => z.Trim()).Where(z => z.Length > 0).ToList();
        if (ordinance is null || zones.Count == 0) return new ZoningLimitLookup();

        var coverage = Find(ordinance, CoverageTitle, zones);
        var far = Find(ordinance, FarTitle, zones);

        return new ZoningLimitLookup
        {
            CoverageRatio = coverage?.Value,
            CoverageBasis = coverage is null ? null : $"{ordinance.Name} 제{coverage.ArticleNo}조",
            FloorAreaRatio = far?.Value,
            FloorAreaBasis = far is null ? null : $"{ordinance.Name} 제{far.ArticleNo}조",
            MatchedZone = coverage?.Zone ?? far?.Zone,
        };
    }

    private static Hit? Find(LawText ordinance, string title, List<string> zones)
    {
        foreach (var article in ordinance.FindArticlesByTitle(title))
        {
            var hit = MatchZone(article.Body, zones);
            if (hit is not null) return hit with { ArticleNo = article.Number };
        }
        return null;
    }

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
                    return new Hit(v, zone, "");
            }
        }
        return null;
    }

    internal sealed record Hit(double Value, string Zone, string ArticleNo);
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
