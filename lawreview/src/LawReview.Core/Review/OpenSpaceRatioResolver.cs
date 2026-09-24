using System.Text;
using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>
/// **공개 공지 등의 확보**(건축법 제43조) 면적 비율을 지자체 건축조례에서 뽑는다.
///
/// 건축법 시행령 제27조의2제2항이 "대지면적의 100분의 10 이하의 범위에서 건축조례로 정한다"로
/// 위임하므로 **실제 비율은 조례에만 있다.** 시행령에는 상한(10%)뿐이라 폴백할 값이 없다.
///
/// 조례 표기가 제각각이라 네 가지를 모두 읽는다(실측):
///  · 대전 — 항의 호마다 용도군별 비율 ("문화 및 집회시설, 종교시설…: 100분의 8")
///  · 속초 — 호마다 용도 하나씩 ("1. 문화 및 집회시설 : 10퍼센트")
///  · 서울 — 용도 구분 없이 하나의 비율 ("대지면적의 10퍼센트 이상")
///  · 안양 — **비율 표가 이미지**(&lt;img&gt;)라 값을 읽을 수 없다 → 정직하게 "확인필요"로 남긴다
///
/// 비율이 정해지면 의무 면적은 코드가 계산한다(대지면적 × 비율) — 숫자는 코드가(원칙 3).
/// 다만 **의무 대상인지 여부는 판정하지 않는다.** 대상 기준(용도별 바닥면적 5천㎡ 이상 등)은
/// 조례 제1항에 따로 있고 용도·면적 조합이 지자체마다 달라, 조문을 인용해 AI·사람이 판단한다.
/// </summary>
public static class OpenSpaceRatioResolver
{
    private const string ArticleTitle = "공개 공지";

    /// <summary>건축조례에서 공개공지 비율을 읽는다. 못 찾으면 Rules가 빈 결과.</summary>
    public static OpenSpaceStandard Resolve(LawText? ordinance)
    {
        if (ordinance is null) return new OpenSpaceStandard();

        foreach (var article in ordinance.FindArticlesByTitle(ArticleTitle))
        {
            foreach (var paragraph in ArticleText.Paragraphs(article.Body))
            {
                if (!IsAreaParagraph(paragraph)) continue;

                var standard = new OpenSpaceStandard
                {
                    SourceName = ordinance.Name,
                    ArticleNo = article.Number,
                    EffectiveDate = ordinance.EffectiveDate,
                };

                // 표를 이미지로 넣은 조례가 있다(안양시). 값이 없다는 사실을 그대로 알린다.
                if (paragraph.Contains("<img", StringComparison.OrdinalIgnoreCase))
                {
                    standard.Note = "조례의 비율 표가 이미지로 되어 있어 자동으로 읽을 수 없습니다. " +
                                    "조례 원문의 표를 직접 확인하세요.";
                    return standard;
                }

                var rules = ParseRules(ArticleText.StripTags(paragraph));
                if (rules.Count == 0) continue;

                standard.Rules = rules;
                return standard;
            }
        }
        return new OpenSpaceStandard();
    }

    /// <summary>
    /// 면적 비율을 정하는 항인지. 설치기준(식재 40퍼센트)·완화 산식(용적률)에도 비율이 나오므로
    /// **완화 조항은 제외하고**, 비율 또는 대지면적을 말하는 첫 항만 쓴다.
    /// </summary>
    private static bool IsAreaParagraph(string paragraph) =>
        !paragraph.Contains("완화") && (paragraph.Contains("비율") || paragraph.Contains("대지면적"));

    private static List<OpenSpaceRule> ParseRules(string paragraph)
    {
        var rules = new List<OpenSpaceRule>();
        foreach (var clause in ArticleText.Clauses(paragraph))
        {
            if (clause.StartsWith("삭제")) continue;
            if (ArticleText.Ratio(clause) is not double ratio) continue;

            var label = Label(clause);
            var uses = IsUseLabel(label) ? ArticleText.SplitUses(label) : Array.Empty<string>();

            // 호 안이 다시 목(가·나·다)으로 갈리는 조례가 있다 — 성남시는 해당 용도 바닥면적
            // 구간별로 5/7/10%다. 첫 값만 읽으면 조용히 틀리므로 구간을 전부 가져온다.
            var bands = ParseBands(clause, uses);
            if (bands.Count > 0) { rules.AddRange(bands); continue; }

            // "2. 면적 : … 대지면적의 10퍼센트 이상"처럼 용도 구분이 없는 조례(서울)는
            // 용도 목록을 비워 모든 용도에 적용되는 행으로 둔다.
            rules.Add(new OpenSpaceRule(uses, ratio, clause.Trim()));
        }

        // 호가 아예 없고 항 본문에 비율만 있는 조례도 있다.
        if (rules.Count == 0 && ArticleText.Ratio(paragraph) is double only)
            rules.Add(new OpenSpaceRule(Array.Empty<string>(), only, paragraph.Trim()));

        return rules;
    }

    /// <summary>
    /// 호 안의 목(가·나·다)이 구간별 비율이면 구간마다 한 행을 만든다.
    ///
    /// "다."는 "…하여야 한다."의 끝과 생김새가 같아 잘못 끊길 수 있으므로,
    /// **모든 목에 비율이 있을 때만** 이 해석을 받아들인다. 하나라도 없으면 쪼개지 않은 것으로 본다
    /// (없는 구간을 만들어내느니 호 단위로 두는 편이 안전하다).
    /// </summary>
    private static List<OpenSpaceRule> ParseBands(string clause, IReadOnlyList<string> uses)
    {
        var items = ArticleText.SubItems(clause).Where(i => !i.StartsWith("삭제")).ToList();
        if (items.Count < 2) return new List<OpenSpaceRule>();

        var bands = new List<OpenSpaceRule>();
        foreach (var item in items)
        {
            if (ArticleText.Ratio(item) is not double ratio) return new List<OpenSpaceRule>();
            bands.Add(new OpenSpaceRule(uses, ratio, item.Trim()));
        }
        return bands;
    }

    /// <summary>호에서 ':' 앞의 이름표를 꺼낸다("문화 및 집회시설 : 10퍼센트" → 앞부분).</summary>
    private static string Label(string clause)
    {
        var cut = clause.IndexOfAny(new[] { ':', '：' });
        return cut > 0 ? clause[..cut].Trim() : "";
    }

    /// <summary>이름표가 용도명인지("면적"·"대상건축물" 같은 항목명과 구별).</summary>
    private static bool IsUseLabel(string label) =>
        label.Contains("시설") || label.Contains("식장") || label.Contains("주택");
}

/// <summary>공개공지 비율 한 줄. Uses가 비어 있으면 모든 용도에 적용된다.</summary>
public sealed record OpenSpaceRule(IReadOnlyList<string> Uses, double Ratio, string Text)
{
    /// <summary>비율이 붙은 조건 문구(구간별 조례에서 "…5천제곱미터 이상 1만제곱미터 미만").</summary>
    public string Condition => Text.Split(new[] { ':', '：' })[0].Trim();

    public bool Matches(string? use)
    {
        if (Uses.Count == 0) return true;
        var u = (use ?? "").Trim();
        return u.Length >= 2 && Uses.Any(x => u.Contains(x) || x.Contains(u));
    }
}

/// <summary>공개공지 비율 조회 결과. 못 찾으면 Rules가 비어 있다.</summary>
public sealed class OpenSpaceStandard
{
    public IReadOnlyList<OpenSpaceRule> Rules { get; set; } = Array.Empty<OpenSpaceRule>();
    public string SourceName { get; set; } = "";
    public string ArticleNo { get; set; } = "";
    public string EffectiveDate { get; set; } = "";
    /// <summary>자동으로 확정할 수 없는 사정(표가 이미지 등). 화면·검토서에 그대로 노출한다.</summary>
    public string? Note { get; set; }

    public string? Basis => SourceName.Length > 0 && ArticleNo.Length > 0
        ? $"{SourceName} 제{ArticleNo.TrimStart('0')}조" : null;

    /// <summary>해당 용도에 걸리는 행들. 용도별 행이 없으면 용도 구분 없는 행을 쓴다.</summary>
    public IReadOnlyList<OpenSpaceRule> RulesFor(string? primaryUse)
    {
        var use = (primaryUse ?? "").Trim();
        var byUse = Rules.Where(r => r.Uses.Count > 0 && r.Matches(use)).ToList();
        return byUse.Count > 0 ? byUse : Rules.Where(r => r.Uses.Count == 0).ToList();
    }

    /// <summary>해당 용도의 비율이 하나로 정해지는 경우 그 행. 구간별로 갈리면 null.</summary>
    public OpenSpaceRule? For(string? primaryUse)
    {
        var matched = RulesFor(primaryUse);
        return matched.Count == 1 ? matched[0] : null;
    }

    /// <summary>
    /// 요약 검토표의 "법적 기준". 대지면적을 주면 의무 면적까지 계산해 붙인다
    /// (검토서 표기와 같은 형식: "6,030.10 x 0.08 = 482.41 m² 이상").
    ///
    /// 조례가 **해당 용도 바닥면적 구간별로 비율을 나눈 경우**(성남시 5/7/10%)에는 구간을 모두
    /// 적는다 — 어느 구간인지는 "해당 용도로 쓰는 바닥면적"이라 연면적으로 단정할 수 없다(원칙 5).
    /// </summary>
    public string? CriterionText(string? primaryUse, double siteArea)
    {
        var matched = RulesFor(primaryUse);
        if (matched.Count == 0) return null;

        var sb = new StringBuilder();
        if (matched.Count == 1)
        {
            sb.Append($"대지면적의 {Percent(matched[0])} 이상{Formula(matched[0], siteArea)}");
        }
        else
        {
            sb.AppendLine("공개공지 면적 비율 (조례가 정한 구간별 — 해당 용도 바닥면적으로 확인할 것):");
            foreach (var rule in matched)
                sb.AppendLine($"· {rule.Condition}: 대지면적의 {Percent(rule)}{Formula(rule, siteArea)}");
        }
        if (Basis is not null) sb.Append($"\n({Basis})");
        return sb.ToString().Trim();
    }

    private static string Percent(OpenSpaceRule rule) => $"{rule.Ratio * 100:0.##}%";

    private static string Formula(OpenSpaceRule rule, double siteArea) => siteArea > 0
        ? $" — {siteArea:#,##0.00} x {rule.Ratio:0.####} = {siteArea * rule.Ratio:#,##0.00} m² 이상"
        : "";
}
