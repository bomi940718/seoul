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

        // ── 설계개요 계산 (검토서 p3 표기 문자열까지 생성) ────────────────
        app.MapPost("/api/overview", (ProjectInput project) =>
        {
            var o = QuantitativeCalculator.Calculate(project);
            return Results.Ok(new
            {
                coverage = new
                {
                    planned = o.CoverageFormula,
                    ratio = o.CoverageRatio,
                    legal = o.MaxBuildingAreaFormula,
                    legalArea = o.MaxBuildingArea,
                    compliant = o.CoverageCompliant,
                },
                floorArea = new
                {
                    gross = o.GrossFloorArea,
                    planned = o.FloorAreaRatioFormula,
                    ratio = o.FloorAreaRatio,
                    legal = o.MaxGrossFloorAreaFormula,
                    legalArea = o.MaxGrossFloorArea,
                    compliant = o.FloorAreaCompliant,
                },
                parking = new
                {
                    planned = new { total = o.Parking.TotalSpaces, formula = o.Parking.TotalFormula, disabled = o.Parking.DisabledFormula },
                    legal = o.ParkingAtLegalMax is null ? null : new
                    {
                        total = o.ParkingAtLegalMax.TotalSpaces,
                        formula = o.ParkingAtLegalMax.TotalFormula,
                        disabled = o.ParkingAtLegalMax.DisabledFormula,
                    },
                },
            });
        });

        app.MapGet("/api/ping", () => Results.Ok(new { ok = true }));
    }
}

public sealed record SettingsDto(string? MolegApiKey, string? ClaudeApiKey, string? VworldApiKey,
    string? ClaudeModel, string? VworldDomain);

public sealed record LookupDto(string? Address);
