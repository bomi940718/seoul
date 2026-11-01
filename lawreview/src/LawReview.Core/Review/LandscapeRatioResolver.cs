using System.Globalization;
using System.Text.RegularExpressions;
using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>
/// 법정 조경면적 비율을 지자체 **건축 조례**에서 뽑는다.
///
/// 건축법 제42조가 "해당 지방자치단체의 조례로 정하는 기준"으로 위임하므로 값은 조례에 있고,
/// 보통 연면적 구간별로 나뉜다(예: 2천㎡ 이상 15% / 1천~2천㎡ 10% / 1천㎡ 미만 5%).
/// 표기는 지자체마다 "100분의 15" 또는 "15퍼센트"로 갈린다.
///
/// 법 위계(기초 조례 → 광역 조례 → 모법)는 <see cref="ParkingStandardResolver"/>와 동일하게 적용한다.
/// </summary>
public static class LandscapeRatioResolver
{
    private const string ArticleTitle = "대지의 조경";

    /// <summary>조례에서 연면적 구간별 조경 기준을 읽는다. 못 찾으면 빈 결과.</summary>
    public static LandscapeStandard Resolve(LawText? ordinance)
    {
        if (ordinance is null) return new LandscapeStandard();

        foreach (var article in ordinance.FindArticlesByTitle(ArticleTitle))
        {
            var rules = ParseRules(article.Body);
            if (rules.Count == 0) continue;
            return new LandscapeStandard
            {
                Rules = rules,
                Basis = $"{ordinance.Name} 제{article.Number}조",
            };
        }
        return new LandscapeStandard();
    }

    // "1. 연면적의 합계가 2천제곱미터 이상인 건축물: 대지면적의 100분의 15 이상"
    private static readonly Regex Clause = new(@"\d+\.\s*([^\d].*?)(?=\d+\.\s|$)", RegexOptions.Compiled);
    private static readonly Regex Ratio = new(@"100분의\s*([\d.]+)|([\d.]+)\s*퍼센트", RegexOptions.Compiled);
    // "2천제곱미터", "1,000제곱미터", "2000제곱미터"
    private static readonly Regex Area = new(@"([\d,]+)\s*(천)?\s*제곱미터\s*(이상|미만)", RegexOptions.Compiled);

    internal static List<LandscapeRule> ParseRules(string body)
    {
        var rules = new List<LandscapeRule>();
        foreach (Match c in Clause.Matches(body ?? ""))
        {
            var text = c.Groups[1].Value;
            if (!text.Contains("연면적") || !text.Contains("제곱미터")) continue;

            var rm = Ratio.Match(text);
            if (!rm.Success) continue;
            var raw = rm.Groups[1].Success ? rm.Groups[1].Value : rm.Groups[2].Value;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) continue;

            double? min = null, max = null;
            foreach (Match am in Area.Matches(text))
            {
                var value = ParseArea(am.Groups[1].Value, am.Groups[2].Success);
                if (am.Groups[3].Value == "이상") min = value;
                else max = value;
            }
            if (min is null && max is null) continue;

            rules.Add(new LandscapeRule(min ?? 0, max, percent / 100.0, text.Trim()));
        }
        return rules;
    }

    private static double ParseArea(string number, bool thousand)
    {
        var v = double.TryParse(number.Replace(",", ""), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        return thousand ? v * 1000 : v;
    }
}

/// <summary>연면적 구간 하나. MaxGrossArea가 null이면 상한 없음.</summary>
public sealed record LandscapeRule(double MinGrossArea, double? MaxGrossArea, double Ratio, string Text)
{
    public bool Matches(double grossArea) =>
        grossArea >= MinGrossArea && (MaxGrossArea is null || grossArea < MaxGrossArea);
}

/// <summary>조경 기준 조회 결과.</summary>
public sealed class LandscapeStandard
{
    public IReadOnlyList<LandscapeRule> Rules { get; set; } = Array.Empty<LandscapeRule>();
    public string? Basis { get; set; }

    /// <summary>연면적에 해당하는 비율을 고른다(예: 0.15).</summary>
    public LandscapeRule? For(double grossArea) => Rules.FirstOrDefault(r => r.Matches(grossArea));
}
