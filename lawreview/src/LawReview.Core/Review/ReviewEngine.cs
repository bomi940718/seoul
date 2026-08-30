using System.Text;
using LawReview.Core.Ai;
using LawReview.Core.LawApi;
using LawReview.Core.Models;
using LawReview.Core.Municipal;

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
    private readonly IDistrictPlanProvider? _districtPlan;
    private readonly IProgress<string>? _progress;

    // 같은 법령을 항목마다 다시 받지 않도록 세션 내 캐시.
    private readonly Dictionary<string, LawText?> _lawCache = new();

    public ReviewEngine(MolegClient law, IJudgmentProvider judge, IProgress<string>? progress = null,
        IDistrictPlanProvider? districtPlan = null)
    {
        _law = law;
        _judge = judge;
        _progress = progress;
        _districtPlan = districtPlan;
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
                    row.Citations.Add(await GetAnnexCitationAsync(lawText, lawName, annexName, project, ct));
            }

            // 1-b) 별표에 값이 있는 항목은 법 위계대로 조례 별표까지 내려가 실제 기준을 붙인다.
            if (item.Id == "setback") await EnrichSetbackAsync(row, project, ct);

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
                        // 판정 한 건이 실패해도 나머지 50여 항목의 검토를 잃지 않도록 항목 단위로 격리한다.
                        try
                        {
                            var judgment = await _judge.JudgeAsync(item, valid, project, ct);
                            row.Applicability = judgment.Applicability;
                            row.Reason = judgment.Reason;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Report($"판정 실패: {item.Title} — {ex.Message}");
                            row.Applicability = Applicability.확인필요;
                            row.Reason = $"AI 판정 중 오류가 발생했습니다 ({ex.Message}). 인용된 조문 원문을 직접 확인하세요.";
                        }
                    }
                    break;

                case JudgmentType.Manual:
                    row.Applicability = Applicability.확인필요;
                    row.Reason = item.Note ?? "자동 판정 대상이 아닙니다. 원문·도면을 직접 확인하세요.";
                    if (item.Id == "district_unit_plan")
                        await EnrichDistrictPlanAsync(row, project, ct);
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

    /// <summary>
    /// "{시}" "{구}" 자리표시자를 프로젝트 지자체명으로 치환. 조례가 항상 해당 지자체 것만 조회되게 한다.
    /// {시}는 **조례를 제정하는 지자체**로 바뀐다 — 광역시는 광역시가, 도 산하는 시·군이 제정하므로
    /// 강원특별자치도 속초시라면 "속초시 건축 조례"가 된다 (Municipality 참고).
    /// </summary>
    internal static string ResolvePlaceholders(string lawName, ProjectInput p) =>
        lawName.Replace("{시}", Municipality.OrdinanceAuthority(p.Province, p.City))
               .Replace("{구}", p.City);

    /// <summary>
    /// 별표는 내용이 파일(HWP 등)이므로 검토서에는 별표명 + 원문 링크로 인용한다.
    /// 본문 조회에 딸려온 별표 목록을 우선 쓰고(일부 법령은 별표 검색 색인에 없음), 없으면 별표 검색으로 보완한다.
    /// </summary>
    private async Task<CitedArticle> GetAnnexCitationAsync(LawText lawText, string lawName, string annexName,
        ProjectInput project, CancellationToken ct)
    {
        if (lawText.FindAnnex(annexName) is Annex annex)
        {
            // 법령 별표는 본문 텍스트가 함께 온다 — 링크만 인용하면 판정이 근거 없이 이뤄지므로
            // 원문을 인용에 담는다. 다만 별표 전문이 수만 자인 경우가 있어(편의증진법 시행령 별표 2)
            // 해당 용도 항목만 발췌하고, 발췌 사실과 원문 링크를 함께 남긴다.
            var excerpt = AnnexText.ExcerptForUse(annex.Content, project.PrimaryUse);
            var body = excerpt.Length > 0
                ? $"{excerpt}\n\n(원문: {annex.Link})"
                : $"별표 내용은 원문 파일을 확인하세요: {annex.Link}";
            return new CitedArticle(lawText.Name, "-", $"[별표 {annex.Number}] {annex.Title}",
                body, lawText.EffectiveDate);
        }
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
    /// 대지 안의 공지(건축법 제58조) 항목에 **실제 적용 이격거리**를 붙인다.
    ///
    /// 조례 조문은 "…기준은 별표 3과 같다"로 끝나 그것만 인용하면 숫자가 하나도 없다.
    /// 값은 조례 별표(HWP)에 있으므로 법 위계대로 **기초 조례 → 광역 조례 → 건축법 시행령 별표 2**
    /// 순으로 내려가며 표를 읽는다(<see cref="SetbackStandardResolver"/>).
    ///
    /// 판정은 하지 않는다 — 표의 각 행에 "산업단지에 건축하는 공장은 제외한다" 같은 단서가 붙어
    /// 용도명만으로 단정할 수 없다. 해당될 수 있는 행을 원문 그대로 인용해 AI·사람이 판단하게 한다.
    /// </summary>
    private async Task EnrichSetbackAsync(ReviewRow row, ProjectInput project, CancellationToken ct)
    {
        var ordinances = new List<OrdinanceAnnex>();
        foreach (var authority in Municipality.OrdinanceHierarchy(project.Province, project.City))
        {
            var name = $"{authority} 건축 조례";
            var ordinance = await GetLawCachedAsync(name, LawTarget.Ordinance, ct);
            if (ordinance is null) continue;

            // 조례 별표는 한 HWP에 [별표 1]…[별표 N]이 모두 들어 있기도 하고 별표마다 파일이 따로이기도 하다.
            var lines = new List<string>();
            string? source = null;
            foreach (var link in await OrdinanceAnnexLinksAsync(ordinance, "공지 기준", ct))
            {
                var text = await _law.DownloadAnnexTextAsync(link, ct);
                if (text.Count == 0) continue;
                lines.AddRange(text);
                source ??= link;
            }
            // 별표를 못 읽었어도 조례는 목록에 남긴다 — 어느 조례를 확인해야 하는지 알려야 하기 때문이다.
            ordinances.Add(new OrdinanceAnnex(ordinance.Name, source ?? "", lines, ordinance.EffectiveDate));
        }

        var decree = await GetLawCachedAsync("건축법 시행령", LawTarget.Law, ct);
        var standard = SetbackStandardResolver.ResolveChain(ordinances, decree);
        var matched = standard.MatchedFor(project.PrimaryUse);
        if (matched.Count == 0) return;

        Report($"대지 안의 공지 기준: {standard.Basis}");
        row.CriterionText = standard.CriterionText(project.PrimaryUse) ?? row.CriterionText;

        var body = new StringBuilder();
        foreach (var rule in matched) body.AppendLine(rule.Text);
        if (matched.Any(r => r.HasProviso))
            body.AppendLine("\n※ 각 행의 괄호 안 단서(제외 규정)에 해당하는지 확인해야 합니다.");
        if (standard.Note is not null) body.AppendLine($"\n※ {standard.Note}");
        if (standard.AnnexLink.Length > 0) body.AppendLine($"\n(원문: {standard.AnnexLink})");

        row.Citations.Add(new CitedArticle(standard.SourceName, "-",
            $"[{standard.AnnexLabel}] 대지 안의 공지 기준", body.ToString().TrimEnd(), standard.EffectiveDate));
    }

    /// <summary>
    /// 조례 별표 파일 링크를 모은다.
    /// 본문 조회에 별표가 딸려오면 그것을 쓰고, **하나도 없으면**(서울특별시 건축 조례가 그렇다)
    /// 자치법규 별표 검색(ordinbyl)으로 보완한다. 검색 결과에는 다른 지자체 조례도 섞이므로
    /// 자치법규명이 일치하는 것만 고른다.
    /// </summary>
    private async Task<IReadOnlyList<string>> OrdinanceAnnexLinksAsync(
        LawText ordinance, string titleKeyword, CancellationToken ct)
    {
        var key = AnnexText.Normalize(titleKeyword);
        var annexes = (ordinance.Annexes ?? Array.Empty<Annex>()).Where(a => a.Link.Length > 0).ToList();

        // 별표 제목이 있으면 필요한 것만 받는다. 제목이 "별표"뿐인 조례(대전)는 전부 받아야 한다
        // — 한 파일에 별표가 모두 들어 있기 때문이다.
        var titled = annexes.Where(a => AnnexText.Normalize(a.Title).Contains(key)).ToList();
        if (titled.Count > 0) return titled.Select(a => a.Link).Distinct().ToList();
        if (annexes.Count > 0) return annexes.Select(a => a.Link).Distinct().ToList();

        try
        {
            var name = ordinance.Name.Replace(" ", "");
            var hits = (await _law.SearchOrdinanceAnnexesAsync(ordinance.Name, ct))
                .Where(a => a.LawName.Replace(" ", "") == name && a.Link.Length > 0
                            && !a.Name.StartsWith("삭제")).ToList();
            var match = hits.Where(a => AnnexText.Normalize(a.Name).Contains(key)).ToList();
            return (match.Count > 0 ? match : hits).Select(a => a.Link).Distinct().ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            Report($"자치법규 별표 검색 실패: {ordinance.Name} — {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 지구단위계획 항목의 근거를 붙인다. 출처 우선순위:
    ///   1) 사용자가 직접 등록한 결정도서 파일 — 해당 필지 문서를 직접 지정한 것이므로 가장 우선하며,
    ///      이때 포털 후보 목록은 노이즈가 되므로 조회하지 않는다.
    ///   2) 해당 지자체 포털 수집기(현재 서울) — 법정동 키워드로 후보 구역 조회.
    ///   3) 둘 다 없으면 체크리스트의 수동 확인 안내를 유지.
    /// 어느 경로든 판정은 "확인필요"를 유지한다 (도면 규제는 자동 판정 불가 — 검토 품질 원칙 5).
    /// </summary>
    private async Task EnrichDistrictPlanAsync(ReviewRow row, ProjectInput project, CancellationToken ct)
    {
        if (project.DistrictPlanFiles.Count > 0)
        {
            AddUploadedDistrictPlanCitations(row, project);
            return;     // 직접 등록이 있으면 포털 조회는 건너뛴다
        }
        if (_districtPlan is null) return;
        if (!project.Province.Replace(" ", "").StartsWith(_districtPlan.Province.Replace(" ", "")[..2])) return;
        var keyword = DistrictPlanProviders.KeywordFromAddress(project.SiteAddress);
        if (keyword is null) return;

        try
        {
            Report($"지구단위계획 조회 중 ({_districtPlan.Province} \"{keyword}\")...");
            var records = await _districtPlan.SearchAsync(keyword, ct);
            if (records.Count == 0)
            {
                row.Reason = $"\"{keyword}\" 검색 결과 지구단위계획구역이 조회되지 않았습니다. " +
                             $"포털에서 필지 기준으로 재확인하세요: {SeoulUrbanPortalClient.PortalPageUrl}";
                return;
            }
            foreach (var r in records.Take(5))
            {
                var body = $"{r.NoticeOrgan} {r.NoticeNo} ({r.NoticeDate} 고시) — {r.NoticeTitle}\n위치: {r.Location}" +
                           (r.AreaAfter is double a ? $"\n구역면적: {a:N1} ㎡" : "") +
                           (r.NoticePdfUrl.Length > 0 ? $"\n고시문 원문: {r.NoticePdfUrl}" : "") +
                           $"\n포털 열람: {r.PortalUrl}";
                row.Citations.Add(new CitedArticle("지구단위계획", "-", r.ZoneName, body, ""));
            }
            row.Reason = $"\"{keyword}\" 기준 후보 구역 {records.Count}건 조회. 해당 필지의 구역 포함 여부와 " +
                         "지침(도면 포함)은 결정도서 원문으로 직접 확인하세요. 지침도 규제는 자동 판정 대상이 아닙니다.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Report($"지구단위계획 조회 실패: {ex.Message}");
            row.Reason = $"지구단위계획 포털 조회에 실패했습니다 ({ex.Message}). 포털에서 직접 확인하세요: " +
                         SeoulUrbanPortalClient.PortalPageUrl;
        }
    }

    /// <summary>
    /// 사용자가 직접 등록한 결정도서를 검토서 근거로 인용한다.
    /// 파일 내용(특히 지침도)은 자동 해석하지 않으므로 출처·경로만 명시하고 판정은 확인필요를 유지한다.
    /// </summary>
    internal static void AddUploadedDistrictPlanCitations(ReviewRow row, ProjectInput project)
    {
        var missing = new List<string>();
        foreach (var f in project.DistrictPlanFiles)
        {
            var exists = f.FilePath.Length > 0 && File.Exists(f.FilePath);
            if (!exists) missing.Add(f.DisplayName);

            var lines = new List<string>();
            if (f.NoticeNo is { Length: > 0 } no)
                lines.Add(no + (f.NoticeDate is { Length: > 0 } d ? $" ({d} 고시)" : ""));
            else if (f.NoticeDate is { Length: > 0 } d2)
                lines.Add($"{d2} 고시");
            lines.Add($"등록 파일: {(f.FilePath.Length > 0 ? f.FilePath : "(경로 없음)")}");
            if (!exists) lines.Add("⚠ 파일을 찾을 수 없습니다. 경로를 확인하세요.");
            lines.Add("지침 내용(도면 포함)은 등록된 원본 문서에서 직접 확인해야 합니다.");

            row.Citations.Add(new CitedArticle(
                "지구단위계획 (직접 등록)", "-", f.DisplayName,
                string.Join("\n", lines), f.NoticeDate ?? ""));
        }

        row.Reason = $"사용자가 등록한 지구단위계획 결정도서 {project.DistrictPlanFiles.Count}건을 근거로 합니다" +
                     (missing.Count > 0 ? $" (파일 확인 필요: {string.Join(", ", missing)})" : "") +
                     ". 지침도 규제는 자동 판정 대상이 아니므로 원본 문서로 직접 확인하세요.";
    }

    /// <summary>
    /// 검색 결과에서 원하는 법령을 고른다. 정확 일치 → 공백 무시 일치 → 첫 결과 순.
    /// (예: "대전광역시 건축 조례" 검색 시 첫 결과는 "대전광역시 건축기본조례"라 첫 결과 폴백만으로는 위험하다.)
    /// </summary>
    public static LawSummary? PickBestMatch(IReadOnlyList<LawSummary> hits, string lawName)
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
