using System.Text.Json;
using LawReview.Core;
using LawReview.Core.LandUse;
using LawReview.Core.Models;
using LawReview.Core.Review;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LawReview.Web;

/// <summary>화면(HTML)이 호출하는 엔진 API. 계산·조회는 전부 LawReview.Core가 수행한다.</summary>
public static class ApiEndpoints
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

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

        app.MapGet("/api/ping", () => Results.Ok(new { ok = true }));
    }
}

public sealed record SettingsDto(string? MolegApiKey, string? ClaudeApiKey, string? VworldApiKey,
    string? ClaudeModel, string? VworldDomain);

public sealed record LookupDto(string? Address);
