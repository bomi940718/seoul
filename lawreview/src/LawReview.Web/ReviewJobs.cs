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

    /// <summary>
    /// 검토 단계. 계약 전에는 주요 법규까지만 보므로 기본 검토와 장별 상세검토를 나눈다.
    /// (상세는 항목이 많아 AI 호출 비용이 크다 — 필요할 때만 돌린다.)
    /// </summary>
    public static bool IsDetailSection(string section) =>
        section is not ("요약" or "각종 인증 의무 대상 여부"
            or "건축허가 관련 지구단위계획 지침" or "해당 지번 관련 주요 법규");

    public static ReviewJob Start(ProjectInput project, string stage = "basic")
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

                var all = ChecklistLoader.LoadDefault();
                // basic: 요약·인증·지구단위·해당지번 / detail: 장별 상세 / all: 전부
                var checklist = stage switch
                {
                    "detail" => all.Where(i => IsDetailSection(i.Section)).ToList(),
                    "all" => all,
                    _ => all.Where(i => !IsDetailSection(i.Section) || i.InSummary).ToList(),
                };
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

    /// <summary>가장 최근에 끝난 검토 결과(검토서 저장에 쓴다).</summary>
    public static ReviewResult? LatestResult() =>
        Jobs.Values.Where(j => j.Done && j.Result is not null)
            .Select(j => j.Result!).LastOrDefault();

    /// <summary>검토 결과를 화면 탭 구성에 맞게 변환한다(검토서 p4~p7 순서).</summary>
    public static object ToView(ReviewResult r)
    {
        var rows = r.Rows;
        return new
        {
            // p4 검토법규 — 표준 서식 순서(중요도) + 법령/시행령/시행규칙/조례 순
            laws = SortLaws(r.ReviewedLaws)
                .Select(k => new { name = k.Key, effectiveDate = FormatDate(k.Value) }),

            // p5 요약 검토서 — 표준 서식 순서(Order)를 따른다
            summary = rows.Where(x => x.Item.InSummary).OrderBy(x => x.Item.Order ?? int.MaxValue)
                .Select(ToSummaryRow),

            // p6 각종 인증 의무 대상 여부
            cert = rows.Where(x => x.Item.Section == "각종 인증 의무 대상 여부")
                .OrderBy(x => x.Item.Order ?? int.MaxValue).Select(ToSummaryRow),

            // p6 지구단위계획 지침
            district = rows.Where(x => x.Item.Section == "건축허가 관련 지구단위계획 지침").Select(ToDetailRow),

            // p7 해당 지번 관련 주요 법규
            site = rows.Where(x => x.Item.Section == "해당 지번 관련 주요 법규").Select(ToDetailRow),

            // 장별 상세 (요약에만 들어가는 항목은 제외 — 상세검토를 따로 돌렸을 때만 채워진다)
            details = rows
                .Where(x => IsDetailSection(x.Item.Section) && !x.Item.InSummary)
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
        // 요약표의 "대상"은 최종 적용 근거 하나만 적는다(표준 서식).
        // 적용 우선순위: 지구단위계획 > 조례 > 시행령·규칙 > 법령
        basis = FinalBasis(x),
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

    /// <summary>
    /// 검토법규 목록의 표기 순서. 실무 표준 서식(HWP)의 중요도 순서를 그대로 따르고,
    /// 같은 법 안에서는 법령 → 시행령 → 시행규칙 → 조례 순으로 놓는다.
    /// 협력사와 공유하는 서식이므로 이 순서를 바꾸지 말 것.
    /// </summary>
    private static readonly string[] LawGroupOrder =
    {
        "건축법",
        "건축물의 구조기준 등에 관한 규칙",
        "건축물의 설비기준 등에 관한 규칙",
        "건축물의 피난ㆍ방화구조 등의 기준에 관한 규칙",
        "국토의 계획 및 이용에 관한 법률",
        "지구단위계획",
        "주차장법",
        "녹색건축물 조성 지원법",
        "신에너지 및 재생에너지 개발ㆍ이용ㆍ보급 촉진법",
        "장애인ㆍ노인ㆍ임산부 등의 편의증진 보장에 관한 법률",
        "매장유산 보호 및 조사에 관한 법률",
        "도시교통정비 촉진법",
        "문화예술진흥법",
    };

    internal static IEnumerable<KeyValuePair<string, string>> SortLaws(IReadOnlyDictionary<string, string> laws)
    {
        return laws.OrderBy(k => GroupIndex(k.Key)).ThenBy(k => KindIndex(k.Key)).ThenBy(k => k.Key);

        static int GroupIndex(string name)
        {
            // 시행령·시행규칙·조례는 모법 이름으로 묶는다.
            var baseName = name;
            foreach (var suffix in new[] { " 시행규칙", " 시행령" })
                if (baseName.EndsWith(suffix)) baseName = baseName[..^suffix.Length];

            for (var i = 0; i < LawGroupOrder.Length; i++)
            {
                var g = LawGroupOrder[i];
                if (baseName == g || baseName.StartsWith(g)) return i;
                // "대전광역시 건축 조례" → 건축법 그룹, "{시} 도시계획 조례" → 국토계획법 그룹
                if (g == "건축법" && name.Contains("건축 조례")) return i;
                if (g == "국토의 계획 및 이용에 관한 법률" && name.Contains("도시계획 조례")) return i;
                if (g == "주차장법" && name.Contains("주차장 조례")) return i;
                if (g == "녹색건축물 조성 지원법" && name.Contains("에너지절약설계기준")) return i;
            }
            return LawGroupOrder.Length;   // 목록에 없는 법은 뒤로
        }

        static int KindIndex(string name) =>
            name.Contains("조례") ? 3
            : name.EndsWith("시행규칙") ? 2
            : name.EndsWith("시행령") ? 1
            : name.Contains("기준") && !name.EndsWith("법") ? 2   // 에너지절약설계기준 등 고시
            : 0;
    }

    /// <summary>
    /// 요약표에 적을 최종 근거 하나를 고른다. 여러 법이 걸리면 실제로 적용되는
    /// 가장 구체적인 것(지구단위계획 &gt; 조례 &gt; 시행령·규칙 &gt; 법령)을 남긴다.
    /// </summary>
    private static string FinalBasis(ReviewRow x)
    {
        var cites = x.Citations
            .Where(c => c.Title is not ("(조회 실패)" or "(조문 없음)"))
            .Select(c => new { Text = $"{c.LawName} {FormatArticle(c.ArticleNo)}".TrimEnd(), c.LawName })
            .ToList();
        if (cites.Count == 0) return "";

        static int Rank(string name) =>
            name.Contains("지구단위계획") ? 0
            : name.Contains("조례") ? 1
            : name.Contains("시행령") || name.Contains("규칙") ? 2
            : 3;

        return cites.OrderBy(c => Rank(c.LawName)).First().Text;
    }

    private static string FormatArticle(string no) =>
        string.IsNullOrEmpty(no) || no == "-" ? ""
        : no.Contains('의') ? $"제{no.Replace("의", "조의")}" : $"제{no}조";

    private static string FormatDate(string yyyymmdd) =>
        yyyymmdd.Length == 8 ? $"{yyyymmdd[..4]}.{yyyymmdd[4..6]}.{yyyymmdd[6..]}" : yyyymmdd;
}
