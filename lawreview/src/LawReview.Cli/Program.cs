using LawReview.Core.Ai;
using LawReview.Core.LawApi;
using LawReview.Core.Models;
using LawReview.Core.Report;
using LawReview.Core.Review;

// 개발·검증용 콘솔 러너 — 둔곡 프로젝트(실무 검토서의 검증 기준)로 전체 파이프라인을 실행한다.
//   사용법: dotnet run --project src/LawReview.Cli -- <OC키> [출력.docx]
//   Claude 키(환경변수 ANTHROPIC_API_KEY)가 없으면 AI 판정은 "확인필요"로 두고 조문 인용만 검증한다.

var oc = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("LAWREVIEW_OC");
if (string.IsNullOrWhiteSpace(oc))
{
    Console.Error.WriteLine("법제처 OC 키가 필요합니다: dotnet run --project src/LawReview.Cli -- <OC키> [출력.docx]");
    return 1;
}
var outPath = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "검토서_둔곡_검증.docx");

var project = new ProjectInput
{
    ProjectName = "세이퍼존 둔곡 공장",
    Client = "(주)세이퍼존",
    SiteAddress = "대전광역시 유성구 둔곡동 407-5",
    Province = "대전광역시",
    City = "유성구",
    UseZones = { "도시지역", "일반공업지역", "지구단위계획구역(국제과학비즈니스벨트 거점지구)" },
    PrimaryUse = "공장",
    SiteArea = 6030.10,
    PlannedBuildingArea = 1453.22,
    PlannedFloorsAbove = 2,
    Buildings =
    {
        new BuildingArea
        {
            Name = "A(공장동)",
            Floors =
            {
                new FloorArea { FloorLabel = "PIT", Use = "공장", CommonArea = 110.06, ExcludeFromGrossArea = true },
                new FloorArea { FloorLabel = "1층", Use = "공장", ExclusiveArea = 926.81, CommonArea = 241.07 },
                new FloorArea { FloorLabel = "2층", Use = "공장", ExclusiveArea = 1110.72, CommonArea = 187.03 },
            },
        },
        new BuildingArea
        {
            Name = "B(경비동)",
            Floors = { new FloorArea { FloorLabel = "1층", Use = "경비실", ExclusiveArea = 18.80 } },
        },
    },
    Zoning = new ZoningLimits
    {
        MaxCoverageRatio = 70.0,
        MaxFloorAreaRatio = 350.0,
        MaxFloors = 7,
        Source = "국제과학비즈니스벨트 거점지구단위계획",
    },
    Parking = new ParkingRule { AreaPerSpace = 200.0, Source = "대전광역시 주차장 조례 제16조" },
};

var checklistPath = FindRepoFile(Path.Combine("src", "LawReview.Core", "checklists", "standard.json"));
var checklist = ChecklistLoader.Load(checklistPath);
Console.WriteLine($"체크리스트 {checklist.Count}항목 로드: {checklistPath}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var moleg = new MolegClient(http, oc!);

var claudeKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
IJudgmentProvider judge = string.IsNullOrWhiteSpace(claudeKey)
    ? new OfflineJudgmentProvider()
    : new ClaudeJudgmentProvider(http, claudeKey);
Console.WriteLine(judge is OfflineJudgmentProvider
    ? "Claude 키 없음 — AI 판정은 '확인필요'로 두고 조문 조회만 검증합니다."
    : "Claude 판정 사용.");

var progress = new Progress<string>(msg => Console.WriteLine("  " + msg));
var engine = new ReviewEngine(moleg, judge, progress);
var result = await engine.RunAsync(project, checklist);

// ── 결과 요약 ─────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"검토법규 {result.ReviewedLaws.Count}건:");
foreach (var (name, date) in result.ReviewedLaws.OrderBy(kv => kv.Key))
    Console.WriteLine($"  {name} (시행 {FormatDate(date)})");

var failures = new List<string>();
Console.WriteLine();
Console.WriteLine($"검토 항목 {result.Rows.Count}건:");
foreach (var row in result.Rows)
{
    var bad = row.Citations.Where(c => c.Title is "(조회 실패)" or "(조문 없음)" || c.Title.EndsWith("(조회 실패)")).ToList();
    var mark = bad.Count == 0 ? "○" : "×";
    Console.WriteLine($"  {mark} [{row.Applicability}] {row.Item.Title} — 인용 {row.Citations.Count}건" +
                      (bad.Count > 0 ? $" (실패 {bad.Count})" : ""));
    foreach (var c in bad)
    {
        Console.WriteLine($"      ! {c.LawName} {c.ArticleNo}: {c.Body.Split('\n')[0]}");
        failures.Add($"{row.Item.Id}: {c.LawName} {c.ArticleNo}");
    }
}

new DocxReportBuilder().Build(result, outPath);
Console.WriteLine();
Console.WriteLine($"검토서 생성: {outPath} ({new FileInfo(outPath).Length:N0} bytes)");

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "조문 인용 전 항목 성공."
    : $"조문 인용 실패 {failures.Count}건 — 위 목록 확인.");
return failures.Count == 0 ? 0 : 2;

static string FormatDate(string yyyymmdd) =>
    yyyymmdd.Length == 8 ? $"{yyyymmdd[..4]}.{yyyymmdd[4..6]}.{yyyymmdd[6..]}." : yyyymmdd;

static string FindRepoFile(string relative)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, relative);
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent!;
    }
    throw new FileNotFoundException(relative);
}

/// <summary>Claude 키 없이 파이프라인을 돌릴 때의 판정 자리표시자.</summary>
file sealed class OfflineJudgmentProvider : IJudgmentProvider
{
    public Task<Judgment> JudgeAsync(ChecklistItem item, IReadOnlyList<CitedArticle> articles,
        ProjectInput project, CancellationToken ct = default) =>
        Task.FromResult(new Judgment(Applicability.확인필요, "AI 판정 미실행 (검증 러너 — Claude 키 없음)."));
}
