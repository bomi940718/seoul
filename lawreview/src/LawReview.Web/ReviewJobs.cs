using System.Collections.Concurrent;
using LawReview.Core;
using LawReview.Core.Ai;
using LawReview.Core.LawApi;
using LawReview.Core.Models;
using LawReview.Core.Municipal;
using LawReview.Core.Review;

namespace LawReview.Web;

/// <summary>
/// 검토 실행은 항목마다 조문 조회·AI 판정이 붙어 수 분이 걸린다.
/// 화면이 멈추지 않도록 백그라운드로 돌리고 진행 상황을 폴링으로 읽게 한다.
/// </summary>
public sealed class ReviewJob
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public List<string> Progress { get; } = new();
    public bool Done { get; set; }
    public string? Error { get; set; }
    public ReviewResult? Result { get; set; }
    public int Total { get; set; }
}

public static class ReviewJobs
{
    private static readonly ConcurrentDictionary<string, ReviewJob> Jobs = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static ReviewJob Start(ProjectInput project)
    {
        var job = new ReviewJob();
        Jobs[job.Id] = job;

        _ = Task.Run(async () =>
        {
            try
            {
                var settings = AppSettings.Load();
                if (settings.MolegApiKey.Length == 0)
                    throw new InvalidOperationException("설정에서 법제처 Open API 키(OC)를 먼저 입력하세요.");

                var checklist = ChecklistLoader.LoadDefault();
                job.Total = checklist.Count;

                var moleg = new MolegClient(Http, settings.MolegApiKey);
                IJudgmentProvider judge = settings.ClaudeApiKey.Length > 0
                    ? new ClaudeJudgmentProvider(Http, settings.ClaudeApiKey, settings.ClaudeModel)
                    : new OfflineJudgmentProvider();

                var districtPlan = DistrictPlanProviders.For(project.Province, Http);
                var progress = new Progress<string>(m => { lock (job.Progress) job.Progress.Add(m); });

                var engine = new ReviewEngine(moleg, judge, progress, districtPlan);
                job.Result = await engine.RunAsync(project, checklist);
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
            }
            finally
            {
                job.Done = true;
            }
        });

        return job;
    }

    public static ReviewJob? Get(string id) => Jobs.TryGetValue(id, out var j) ? j : null;

    /// <summary>검토 결과를 화면 탭 구성에 맞게 변환한다(검토서 p4~p7 순서).</summary>
    public static object ToView(ReviewResult r)
    {
        var rows = r.Rows;
        return new
        {
            // p4 검토법규 — 법령명과 시행일자
            laws = r.ReviewedLaws.OrderBy(k => k.Key)
                .Select(k => new { name = k.Key, effectiveDate = FormatDate(k.Value) }),

            // p5 요약 검토서
            summary = rows.Where(x => x.Item.InSummary).Select(ToSummaryRow),

            // p6 각종 인증 의무 대상 여부
            cert = rows.Where(x => x.Item.Section == "각종 인증 의무 대상 여부").Select(ToSummaryRow),

            // p6 지구단위계획 지침
            district = rows.Where(x => x.Item.Section == "건축허가 관련 지구단위계획 지침").Select(ToDetailRow),

            // p7 해당 지번 관련 주요 법규
            site = rows.Where(x => x.Item.Section == "해당 지번 관련 주요 법규").Select(ToDetailRow),

            // 장별 상세 (남은 전부)
            details = rows
                .Where(x => x.Item.Section is not ("요약" or "각종 인증 의무 대상 여부"
                    or "건축허가 관련 지구단위계획 지침" or "해당 지번 관련 주요 법규"))
                .GroupBy(x => x.Item.Section)
                .Select(g => new { section = g.Key, items = g.Select(ToDetailRow) }),

            counts = new
            {
                total = rows.Count,
                적용 = rows.Count(x => x.Applicability == Applicability.적용),
                해당없음 = rows.Count(x => x.Applicability == Applicability.해당없음),
                확인필요 = rows.Count(x => x.Applicability == Applicability.확인필요),
            },
        };
    }

    private static object ToSummaryRow(ReviewRow x) => new
    {
        title = x.Item.Title,
        basis = string.Join("\n", x.Citations
            .Select(c => $"{c.LawName} {FormatArticle(c.ArticleNo)}".TrimEnd()).Distinct()),
        criterion = x.CriterionText ?? "",
        calculation = x.CalculationText ?? "",
        verdict = x.Applicability.ToString(),
        reason = x.Reason ?? "",
    };

    private static object ToDetailRow(ReviewRow x) => new
    {
        title = x.Item.Title,
        verdict = x.Applicability.ToString(),
        reason = x.Reason ?? "",
        citations = x.Citations.Select(c => new
        {
            law = c.LawName,
            article = FormatArticle(c.ArticleNo),
            articleTitle = c.Title,
            body = c.Body,
            effectiveDate = FormatDate(c.EffectiveDate),
        }),
    };

    private static string FormatArticle(string no) =>
        string.IsNullOrEmpty(no) || no == "-" ? ""
        : no.Contains('의') ? $"제{no.Replace("의", "조의")}" : $"제{no}조";

    private static string FormatDate(string yyyymmdd) =>
        yyyymmdd.Length == 8 ? $"{yyyymmdd[..4]}.{yyyymmdd[4..6]}.{yyyymmdd[6..]}" : yyyymmdd;
}
