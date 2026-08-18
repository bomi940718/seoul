using System.Text.Json;
using LawReview.Core;
using LawReview.Core.LandUse;
using LawReview.Core.LawApi;
using LawReview.Core.Models;
using LawReview.Core.Report;
using LawReview.Core.Review;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LawReview.Web;

/// <summary>화면(HTML)이 호출하는 엔진 API. 계산·조회는 전부 LawReview.Core가 수행한다.</summary>
public static class ApiEndpoints
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // 법제처 첨부파일(flDownload)은 User-Agent가 없으면 HWP 대신 HTML 안내 페이지를 돌려준다.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LawReview/1.0");
        return http;
    }

    public static void Map(WebApplication app)
    {
        // ── 설정 (키 보유 여부만 노출, 값은 내보내지 않는다) ──────────────
        app.MapGet("/api/settings", () =>
        {
            var s = AppSettings.Load();
            return Results.Ok(new
            {
                hasMoleg = s.MolegApiKey.Length > 0,
                hasClaude = s.ClaudeApiKey.Length > 0,
                hasVworld = s.VworldApiKey.Length > 0,
                claudeModel = s.ClaudeModel,
                vworldDomain = s.VworldDomain,
            });
        });

        app.MapPost("/api/settings", (SettingsDto dto) =>
        {
            var s = AppSettings.Load();
            if (dto.MolegApiKey is not null) s.MolegApiKey = dto.MolegApiKey.Trim();
            if (dto.ClaudeApiKey is not null) s.ClaudeApiKey = dto.ClaudeApiKey.Trim();
            if (dto.VworldApiKey is not null) s.VworldApiKey = dto.VworldApiKey.Trim();
            if (!string.IsNullOrWhiteSpace(dto.ClaudeModel)) s.ClaudeModel = dto.ClaudeModel.Trim();
            if (!string.IsNullOrWhiteSpace(dto.VworldDomain)) s.VworldDomain = dto.VworldDomain.Trim();
            s.Save();
            return Results.Ok(new { saved = true });
        });

        // ── 주소 자동조회 (지역지구·대지면적·지목·지자체) ─────────────────
        app.MapPost("/api/lookup", async (LookupDto dto) =>
        {
            var s = AppSettings.Load();
            if (s.VworldApiKey.Length == 0)
                return Results.Ok(new { ok = false, message = "설정에서 VWorld 키를 먼저 입력하세요." });
            if (string.IsNullOrWhiteSpace(dto.Address))
                return Results.Ok(new { ok = false, message = "대지위치(지번 주소)를 입력하세요." });

            try
            {
                var vworld = new VworldClient(Http, s.VworldApiKey, s.VworldDomain);
                var index = await vworld.GetLandUseIndexAsync(dto.Address.Trim());
                if (index is null)
                    return Results.Ok(new { ok = false, message = "조회 결과가 없습니다. 지번 주소인지 확인하세요." });

                // 용도지역이 정해지면 법정 건폐율·용적률도 지자체 도시계획조례에서 바로 가져온다.
                // (지구단위계획이 없어도 검토가 되어야 하므로 — 조례가 기본 기준)
                var limits = await ResolveZoningLimitsAsync(s, index.Province, index.City, index.Zones);

                return Results.Ok(new
                {
                    ok = true,
                    pnu = index.Pnu,
                    address = index.RefinedAddress,
                    province = index.Province,
                    city = index.City,
                    zones = index.Zones,
                    area = index.Area,
                    category = index.Category,   // 지목 — 설계개요 대지면적 칸에 병기
                    limits = new
                    {
                        coverage = limits.CoverageRatio,
                        coverageBasis = limits.CoverageBasis,
                        far = limits.FloorAreaRatio,
                        farBasis = limits.FloorAreaBasis,
                        zone = limits.MatchedZone,
                    },
                    parking = await ResolveParkingAsync(s, index.Province, index.City, dto.PrimaryUse),
                    landscape = await ResolveLandscapeAsync(s, index.Province, index.City),
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, message = ex.Message });
            }
        });

        // ── 설계개요 계산 ────────────────────────────────────────────────
        // 화면의 표 행(구분)에 그대로 대응하는 문자열을 돌려준다.
        // 표기 형식은 실무 표준 서식(HWP)을 따르며, 계산은 전부 엔진이 한다.
        app.MapPost("/api/overview", (ProjectInput project) =>
        {
            var o = QuantitativeCalculator.Calculate(project);
            var legalParking = o.ParkingAtLegalMax;

            return Results.Ok(new
            {
                grossFloorArea = o.GrossFloorArea,
                rows = new Dictionary<string, object?>
                {
                    ["buildingArea"] = Row(o.BuildingAreaFormula, o.MaxBuildingAreaFormula is { } f
                        ? $"{f} 이하" : null),
                    ["coverage"] = Row(o.CoverageFormula,
                        project.Zoning.MaxCoverageRatio is double c ? $"{c:0.00} % 이하" : null),
                    ["grossArea"] = Row(o.GrossAreaFormula, o.MaxGrossFloorAreaFormula is { } g
                        ? $"{g} 이하" : null),
                    ["floorRatio"] = Row(o.FloorAreaRatioFormula,
                        project.Zoning.MaxFloorAreaRatio is double r ? $"{r:0.00} % 이하" : null),
                    ["landscape"] = Row(null, o.LandscapeFormula),
                    ["parkingOut"] = Row(o.Parking.TotalFormula, legalParking?.TotalFormula),
                    ["parkingDis"] = Row(o.Parking.DisabledFormula, legalParking?.DisabledFormula),
                    ["parkingExt"] = Row(o.Parking.ExpandedFormula, legalParking?.ExpandedFormula),
                    ["parkingEco"] = Row(o.Parking.EcoFormula, legalParking?.EcoFormula),
                    ["parkingAll"] = Row(o.Parking.TotalDisplay, legalParking?.TotalDisplay),
                },
                compliance = new
                {
                    coverage = o.CoverageCompliant,
                    floorArea = o.FloorAreaCompliant,
                    floors = o.FloorsCompliant,
                },
            });

            static object Row(string? planned, string? legal) => new { planned, legal };
        });

        // ── 검토 실행 (백그라운드 + 진행상황 폴링) ────────────────────────
        // stage=basic(기본, 주요 법규까지) | detail(장별 상세) | all
        app.MapPost("/api/review/start", (ProjectInput project, string? stage) =>
        {
            var job = ReviewJobs.Start(project, stage ?? "basic");
            return Results.Ok(new { jobId = job.Id });
        });

        app.MapGet("/api/review/{id}", (string id) =>
        {
            var job = ReviewJobs.Get(id);
            if (job is null) return Results.NotFound(new { message = "검토 작업을 찾을 수 없습니다." });

            string[] progress;
            lock (job.Progress) progress = job.Progress.ToArray();

            return Results.Ok(new
            {
                done = job.Done,
                error = job.Error,
                total = job.Total,
                progress,
                result = job.Result is null ? null : ReviewJobs.ToView(job.Result),
            });
        });

        // ── 프로젝트 저장/불러오기 ───────────────────────────────────────
        app.MapGet("/api/projects", () => Results.Ok(ProjectStore.List()));

        app.MapPost("/api/projects/{name}", (string name, JsonElement data) =>
        {
            try { ProjectStore.Save(name, data); return Results.Ok(new { saved = true }); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        app.MapGet("/api/projects/{name}", (string name) =>
        {
            var json = ProjectStore.Load(name);
            return json is null
                ? Results.NotFound(new { message = "저장된 프로젝트가 없습니다." })
                : Results.Content(json, "application/json");
        });

        app.MapDelete("/api/projects/{name}", (string name) =>
            ProjectStore.Delete(name) ? Results.Ok(new { deleted = true })
                                      : Results.NotFound(new { message = "없는 프로젝트입니다." }));

        // ── 검토서 저장 (DOCX) ───────────────────────────────────────────
        // 검토를 돌린 결과가 있으면 그것을, 없으면 설계개요만으로 문서를 만든다.
        app.MapPost("/api/report", (ReportRequest req) =>
        {
            try
            {
                var result = ReviewJobs.LatestResult();
                if (result is null || req.Project is not null)
                {
                    var project = req.Project ?? new ProjectInput();
                    result ??= new ReviewResult { Project = project };
                    // 저장 시점의 설계개요 입력을 문서에 반영한다.
                    if (req.Project is not null) result = CloneWithProject(result, req.Project);
                }

                // 사람이 화면에서 고친 판정을 얹는다. 검토서에 나가는 것은 사람이 확정한 값이다.
                var edited = JudgmentOverrides.CountApplied(result, req.Overrides);
                result = JudgmentOverrides.Apply(result, req.Overrides);

                var dir = string.IsNullOrWhiteSpace(req.Folder)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "법규검토서")
                    : req.Folder!;
                Directory.CreateDirectory(dir);

                var name = ProjectStore.SafeName(
                    string.IsNullOrWhiteSpace(req.FileName)
                        ? $"{result.Project.ProjectName}_법규검토서_{DateTime.Now:yyyyMMdd}"
                        : req.FileName!);
                if (name.Length == 0) name = $"법규검토서_{DateTime.Now:yyyyMMdd}";
                var path = Path.Combine(dir, name + ".docx");

                new DocxReportBuilder().Build(result, path);
                return Results.Ok(new { ok = true, path, size = new FileInfo(path).Length, edited });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, message = ex.Message });
            }
        });

        // 저장한 문서를 바로 열어본다.
        app.MapPost("/api/report/open", (OpenRequest req) =>
        {
            if (!File.Exists(req.Path)) return Results.NotFound(new { message = "파일이 없습니다." });
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(req.Path) { UseShellExecute = true });
            return Results.Ok(new { opened = true });
        });

        app.MapGet("/api/ping", () => Results.Ok(new { ok = true }));
    }

    /// <summary>
    /// 부설주차장 설치기준(시설면적 N㎡당 1대)을 주차장법 시행령 별표 1에서 가져오고,
    /// 해당 지자체 주차장 조례의 별표 원문 링크를 함께 준다.
    /// (조례 별표는 HWP 첨부라 값을 읽을 수 없어 사용자가 직접 확인해야 한다)
    /// </summary>
    private static async Task<object?> ResolveParkingAsync(
        AppSettings s, string province, string city, string? primaryUse)
    {
        if (s.MolegApiKey.Length == 0 || string.IsNullOrWhiteSpace(primaryUse)) return null;

        try
        {
            var moleg = new MolegClient(Http, s.MolegApiKey);
            var hits = await moleg.SearchAsync("주차장법 시행령", LawTarget.Law);
            var best = ReviewEngine.PickBestMatch(hits, "주차장법 시행령");
            if (best is null) return null;

            var decree = await moleg.GetLawTextAsync(best.SerialNo, LawTarget.Law);

            // 법 위계대로 조례를 모은다: 기초(시·군) → 광역(도). 각 별표(HWP)를 받아 읽는다.
            var annexes = new List<OrdinanceAnnex>();
            foreach (var authority in Municipality.OrdinanceHierarchy(province, city))
            {
                var ord = await FindParkingOrdinanceAsync(moleg, authority);
                if (ord is null) continue;
                // 별표가 여러 개면(주차요금표·표지판 등) 어느 것이 설치기준인지 제목만으로는 모른다.
                // 전부 받아 이어 붙인 뒤 기준을 찾는다.
                var lines = new List<string>();
                foreach (var link in ord.Value.Links)
                    lines.AddRange(await DownloadAnnexTextAsync(link));
                annexes.Add(new OrdinanceAnnex(ord.Value.Name, ord.Value.Links.FirstOrDefault() ?? "", lines));
            }

            // 기초 조례 → 광역 조례 → 모법 → (모법의 "그 밖의 건축물")
            var std = ParkingStandardResolver.ResolveChain(annexes, decree, primaryUse!);
            if (std.AreaPerSpace is null) return null;

            return new
            {
                areaPerSpace = std.AreaPerSpace,
                basis = std.Basis,
                matchedUse = std.MatchedUse,
                note = std.Note,
                ordinanceName = annexes.FirstOrDefault()?.Name,
                ordinanceAnnexLink = annexes.FirstOrDefault()?.Link,
                checkedOrdinances = annexes.Select(a => a.Name),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 지자체 주차장 조례를 찾는다. 이름이 지자체마다 달라("○○시 주차장 조례",
    /// "○○시 주차장 설치 및 관리 조례") 검색 결과에서 골라야 한다.
    /// </summary>
    private static async Task<(string Name, IReadOnlyList<string> Links)?> FindParkingOrdinanceAsync(
        MolegClient moleg, string authority)
    {
        if (authority.Length == 0) return null;
        var hits = await moleg.SearchAsync($"{authority} 주차장", LawTarget.Ordinance);

        // 부설주차장 설치기준을 담은 "본체" 조례만 골라야 한다.
        // 무료개방·지원·요금·특별회계 같은 곁가지 조례가 잡히면 엉뚱한 별표를 읽게 된다.
        static bool IsSideOrdinance(string n) =>
            n.Contains("개방") || n.Contains("지원") || n.Contains("특별회계") || n.Contains("기금")
            || n.Contains("공공청사") || n.Contains("전용주차") || n.Contains("운영");

        var best = hits.FirstOrDefault(h => h.Name == $"{authority} 주차장 조례")
                   ?? hits.FirstOrDefault(h => h.Name == $"{authority} 주차장 설치 및 관리 조례")
                   ?? hits.FirstOrDefault(h => h.Name.StartsWith($"{authority} 주차장")
                                               && h.Name.EndsWith("조례") && !IsSideOrdinance(h.Name));
        if (best is null) return null;

        var text = await moleg.GetLawTextAsync(best.SerialNo, LawTarget.Ordinance);
        var links = (text.Annexes ?? Array.Empty<Annex>())
            .Select(a => a.Link).Where(l => l.Length > 0).Distinct().ToList();
        return (best.Name, links);
    }

    /// <summary>
    /// 법정 조경면적 비율을 건축조례에서 가져온다. 연면적 구간별로 갈리므로 구간표를 통째로 넘기고,
    /// 실제 적용 비율은 화면이 현재 연면적으로 고른다(연면적은 자동조회 시점에 아직 없을 수 있다).
    /// 법 위계는 주차와 동일하게 기초 조례 → 광역 조례 순으로 본다.
    /// </summary>
    private static async Task<object?> ResolveLandscapeAsync(AppSettings s, string province, string city)
    {
        if (s.MolegApiKey.Length == 0) return null;
        try
        {
            var moleg = new MolegClient(Http, s.MolegApiKey);
            foreach (var authority in Municipality.OrdinanceHierarchy(province, city))
            {
                var name = $"{authority} 건축 조례";
                var hits = await moleg.SearchAsync(name, LawTarget.Ordinance);
                var best = ReviewEngine.PickBestMatch(hits, name);
                if (best is null) continue;

                var text = await moleg.GetLawTextAsync(best.SerialNo, LawTarget.Ordinance);
                var std = LandscapeRatioResolver.Resolve(text);
                if (std.Rules.Count == 0) continue;

                return new
                {
                    basis = std.Basis,
                    rules = std.Rules.Select(r => new
                    {
                        minGrossArea = r.MinGrossArea,
                        maxGrossArea = r.MaxGrossArea,
                        ratio = r.Ratio,
                        text = r.Text,
                    }),
                };
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    /// <summary>조례 별표 첨부(HWP)를 받아 본문 줄을 뽑는다. 실패하면 빈 목록(모법으로 넘어간다).</summary>
    private static async Task<IReadOnlyList<string>> DownloadAnnexTextAsync(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return Array.Empty<string>();
        try
        {
            var bytes = await Http.GetByteArrayAsync(link);
            // HWP가 아니면(안내 HTML 등) 조용히 넘긴다 — 모법 기준으로 떨어진다.
            if (!HwpTextExtractor.LooksLikeHwp(bytes)) return Array.Empty<string>();
            return HwpTextExtractor.ExtractLines(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>검토 결과의 판정은 유지하고 프로젝트 정보만 최신 입력으로 바꾼다.</summary>
    private static ReviewResult CloneWithProject(ReviewResult src, ProjectInput project)
    {
        var copy = new ReviewResult { Project = project, Overview = QuantitativeCalculator.Calculate(project) };
        foreach (var row in src.Rows) copy.Rows.Add(row);
        foreach (var (k, v) in src.ReviewedLaws) copy.ReviewedLaws[k] = v;
        return copy;
    }

    /// <summary>
    /// 지자체 도시계획조례에서 용도지역별 법정 건폐율·용적률을 찾는다.
    /// 법제처 키가 없거나 조회에 실패하면 조용히 빈 결과를 돌려준다(자동조회 자체는 살린다).
    /// </summary>
    private static async Task<ZoningLimitLookup> ResolveZoningLimitsAsync(
        AppSettings s, string province, string city, IReadOnlyList<string> zones)
    {
        if (s.MolegApiKey.Length == 0 || province.Length == 0 || zones.Count == 0)
            return new ZoningLimitLookup();

        try
        {
            var moleg = new MolegClient(Http, s.MolegApiKey);
            // 도시계획조례를 제정하는 지자체를 골라야 한다(도 산하는 시·군이 제정).
            var name = $"{Municipality.OrdinanceAuthority(province, city)} 도시계획 조례";
            var hits = await moleg.SearchAsync(name, LawTarget.Ordinance);
            var best = ReviewEngine.PickBestMatch(hits, name);
            if (best is null) return new ZoningLimitLookup();

            var text = await moleg.GetLawTextAsync(best.SerialNo, LawTarget.Ordinance);
            return ZoningLimitResolver.Resolve(text, zones);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return new ZoningLimitLookup();
        }
    }
}

public sealed record SettingsDto(string? MolegApiKey, string? ClaudeApiKey, string? VworldApiKey,
    string? ClaudeModel, string? VworldDomain);

public sealed record LookupDto(string? Address, string? PrimaryUse);

/// <summary>검토서 저장 요청. Overrides는 화면에서 사람이 고친 판정·사유다(없으면 AI 판정 그대로).</summary>
public sealed record ReportRequest(ProjectInput? Project, string? Folder, string? FileName,
    List<JudgmentOverride>? Overrides);

public sealed record OpenRequest(string Path);
