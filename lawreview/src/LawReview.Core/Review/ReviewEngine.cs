using LawReview.Core.Ai;
using LawReview.Core.LawApi;
using LawReview.Core.Models;

namespace LawReview.Core.Review;

/// <summary>
/// 검토 파이프라인 오케스트레이터.
/// 흐름: 근거 법령 조회(법제처) → 정량 계산 → 항목별 판정(코드/AI) → ReviewResult.
/// 결과는 DocxReportBuilder가 검토서로 조립한다.
/// </summary>
public sealed class ReviewEngine
{
    private readonly MolegClient _law;
    private readonly IJudgmentProvider _judge;
    private readonly IProgress<string>? _progress;

    // 같은 법령을 항목마다 다시 받지 않도록 세션 내 캐시.
    private readonly Dictionary<string, LawText?> _lawCache = new();

    public ReviewEngine(MolegClient law, IJudgmentProvider judge, IProgress<string>? progress = null)
    {
        _law = law;
        _judge = judge;
        _progress = progress;
    }

    public async Task<ReviewResult> RunAsync(ProjectInput project, IReadOnlyList<ChecklistItem> checklist,
        CancellationToken ct = default)
    {
        var result = new ReviewResult { Project = project };

        Report("정량 항목 계산 중 (건폐율·용적률·주차)...");
        result.Overview = QuantitativeCalculator.Calculate(project);

        foreach (var item in checklist)
        {
            ct.ThrowIfCancellationRequested();
            Report($"검토 중: {item.Title}");
            var row = new ReviewRow { Item = item, CriterionText = item.LegalCriterion };

            // 1) 근거 조문 원문 수집 — 검토서에 인용되는 유일한 출처.
            foreach (var basis in item.Basis)
            {
                var lawName = ResolvePlaceholders(basis.LawName, project);
                var lawText = await GetLawCachedAsync(lawName, basis.Target, ct);
                if (lawText is null)
                {
                    row.Citations.Add(new CitedArticle(lawName, basis.Article ?? "-", "(조회 실패)",
                        "법제처에서 해당 법령을 찾지 못했습니다. 법령명을 확인하세요.", ""));
                    continue;
                }
                result.ReviewedLaws.TryAdd(lawName, lawText.EffectiveDate);

                if (basis.Article is string artNo)
                {
                    var art = lawText.FindArticle(artNo);
                    if (art is not null)
                        row.Citations.Add(new CitedArticle(lawText.Name, art.Number, art.Title, art.Body, lawText.EffectiveDate));
                    else
                        row.Citations.Add(new CitedArticle(lawText.Name, artNo, "(조문 없음)",
                            $"제{artNo}조를 현행 법령에서 찾지 못했습니다. 개정으로 조문번호가 바뀌었을 수 있습니다.", lawText.EffectiveDate));
                }
                else if (basis.ArticleTitleKeyword is string keyword)
                {
                    // 조례는 지자체마다 조문번호가 다르므로 제목 키워드로 찾는다.
                    var matches = lawText.FindArticlesByTitle(keyword);
                    if (matches.Count > 0)
                        foreach (var art in matches)
                            row.Citations.Add(new CitedArticle(lawText.Name, art.Number, art.Title, art.Body, lawText.EffectiveDate));
                    else
                        row.Citations.Add(new CitedArticle(lawText.Name, "-", "(조문 없음)",
                            $"제목에 \"{keyword}\"가 들어간 조문을 찾지 못했습니다. 해당 법령 전문을 직접 확인하세요.", lawText.EffectiveDate));
                }

                if (basis.Annex is string annexName)
                    row.Citations.Add(await GetAnnexCitationAsync(lawText, lawName, annexName, ct));
            }

            // 2) 판정.
            switch (item.Judgment)
            {
                case JudgmentType.Quantitative:
                    ApplyQuantitative(row, item.Id, result.Overview, project);
                    break;

                case JudgmentType.Ai:
                    var valid = row.Citations.Where(c => c.Title != "(조회 실패)" && c.Title != "(조문 없음)").ToList();
                    if (valid.Count == 0)
                    {
                        row.Applicability = Applicability.확인필요;
                        row.Reason = "근거 조문을 조회하지 못해 자동 판정을 보류합니다.";
                    }
                    else
                    {
                        var judgment = await _judge.JudgeAsync(item, valid, project, ct);
                        row.Applicability = judgment.Applicability;
                        row.Reason = judgment.Reason;
                    }
                    break;

                case JudgmentType.Manual:
                    row.Applicability = Applicability.확인필요;
                    row.Reason = item.Note ?? "자동 판정 대상이 아닙니다. 원문·도면을 직접 확인하세요.";
                    break;
            }

            result.Rows.Add(row);
        }

        Report("검토 완료.");
        return result;
    }

    /// <summary>정량 항목의 판정과 산정식 채움. 항목 Id로 계산 결과를 매핑한다.</summary>
    internal static void ApplyQuantitative(ReviewRow row, string id, DesignOverview o, ProjectInput p)
    {
        switch (id)
        {
            case "coverage_ratio":
                row.CalculationText = o.CoverageFormula;
                row.CriterionText ??= p.Zoning.MaxCoverageRatio is double c ? $"{c:0.00} % 이하" : null;
                row.Applicability = o.CoverageCompliant is null ? Applicability.확인필요 : Applicability.적용;
                row.Reason = o.CoverageCompliant switch
                {
                    true => $"계획 건폐율 {o.CoverageRatio:0.00}%로 법정 한도 이내.",
                    false => $"계획 건폐율 {o.CoverageRatio:0.00}%가 법정 한도를 초과! ({p.Zoning.Source})",
                    null => "법정 건폐율이 입력되지 않았습니다.",
                };
                break;

            case "floor_area_ratio":
                row.CalculationText = o.FloorAreaRatioFormula;
                row.CriterionText ??= p.Zoning.MaxFloorAreaRatio is double f ? $"{f:0.00} % 이하" : null;
                row.Applicability = o.FloorAreaCompliant is null ? Applicability.확인필요 : Applicability.적용;
                row.Reason = o.FloorAreaCompliant switch
                {
                    true => $"계획 용적률 {o.FloorAreaRatio:0.00}%로 법정 한도 이내.",
                    false => $"계획 용적률 {o.FloorAreaRatio:0.00}%가 법정 한도를 초과! ({p.Zoning.Source})",
                    null => "법정 용적률이 입력되지 않았습니다.",
                };
                break;

            case "parking":
                row.CalculationText = o.Parking.TotalFormula;
                row.CriterionText ??= $"시설면적 {p.Parking.AreaPerSpace:0.00} ㎡ 당 1대 ({p.Parking.Source})";
                row.Applicability = Applicability.적용;
                row.Reason = $"법정 {o.Parking.TotalSpaces}대 이상" +
                             (o.Parking.DisabledSpaces is int d ? $", 장애인전용 {d}대 이상." : ".");
                break;

            default:
                row.Applicability = Applicability.확인필요;
                row.Reason = $"정량 판정 매핑이 없는 항목입니다: {id}";
                break;
        }
    }

    /// <summary>"{시}" "{구}" 자리표시자를 프로젝트 지자체명으로 치환. 조례가 항상 해당 지자체 것만 조회되게 한다.</summary>
    internal static string ResolvePlaceholders(string lawName, ProjectInput p) =>
        lawName.Replace("{시}", p.Province).Replace("{구}", p.City);

    /// <summary>
    /// 별표는 내용이 파일(HWP 등)이므로 검토서에는 별표명 + 원문 링크로 인용한다.
    /// 본문 조회에 딸려온 별표 목록을 우선 쓰고(일부 법령은 별표 검색 색인에 없음), 없으면 별표 검색으로 보완한다.
    /// </summary>
    private async Task<CitedArticle> GetAnnexCitationAsync(LawText lawText, string lawName, string annexName,
        CancellationToken ct)
    {
        if (lawText.FindAnnex(annexName) is Annex annex)
            return new CitedArticle(lawText.Name, "-", $"[별표 {annex.Number}] {annex.Title}",
                $"별표 내용은 원문 파일을 확인하세요: {annex.Link}", lawText.EffectiveDate);
        try
        {
            var normalized = annexName.Replace(" ", "").Replace("별표", "");
            var hit = (await _law.SearchAnnexesAsync(lawName, ct)).FirstOrDefault(a =>
                a.LawName.Replace(" ", "") == lawName.Replace(" ", "") && a.Number == normalized);
            if (hit is not null)
                return new CitedArticle(lawName, "-", $"[별표 {hit.Number}] {hit.Name}",
                    $"별표 내용은 원문 파일을 확인하세요: {hit.Link}", lawText.EffectiveDate);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            Report($"별표 조회 실패: {lawName} {annexName} — {ex.Message}");
        }
        return new CitedArticle(lawName, "-", $"[{annexName}] (조회 실패)",
            $"{lawName}의 {annexName}를 법제처에서 찾지 못했습니다. 원문을 직접 확인하세요.", lawText.EffectiveDate);
    }

    private async Task<LawText?> GetLawCachedAsync(string lawName, LawTarget target, CancellationToken ct)
    {
        if (_lawCache.TryGetValue(lawName, out var cached)) return cached;

        LawText? text = null;
        try
        {
            var hits = await _law.SearchAsync(lawName, target, ct);
            var best = PickBestMatch(hits, lawName);
            if (best is not null)
                text = await _law.GetLawTextAsync(best.SerialNo, target, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            Report($"법령 조회 실패: {lawName} — {ex.Message}");
        }
        _lawCache[lawName] = text;
        return text;
    }

    /// <summary>
    /// 검색 결과에서 원하는 법령을 고른다. 정확 일치 → 공백 무시 일치 → 첫 결과 순.
    /// (예: "대전광역시 건축 조례" 검색 시 첫 결과는 "대전광역시 건축기본조례"라 첫 결과 폴백만으로는 위험하다.)
    /// </summary>
    internal static LawSummary? PickBestMatch(IReadOnlyList<LawSummary> hits, string lawName)
    {
        var normalized = lawName.Replace(" ", "");
        return hits.FirstOrDefault(h => h.Name == lawName)
            ?? hits.FirstOrDefault(h => h.Name.Replace(" ", "") == normalized)
            ?? hits.FirstOrDefault();
    }

    private void Report(string msg) => _progress?.Report(msg);
}

/// <summary>검토 전체 결과. 검토서 생성의 입력이 된다.</summary>
public sealed class ReviewResult
{
    public required ProjectInput Project { get; init; }
    public DesignOverview Overview { get; set; } = new();
    public List<ReviewRow> Rows { get; } = new();
    /// <summary>검토에 사용된 법령과 시행일자 — "검토법규" 목록이 된다.</summary>
    public Dictionary<string, string> ReviewedLaws { get; } = new();
}
