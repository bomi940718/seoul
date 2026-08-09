using System.Globalization;
using LawReview.Core.Models;

namespace LawReview.Core.Review;

/// <summary>
/// 설계개요의 정량 항목(건폐율·용적률·주차대수 등)을 계산한다.
/// 원칙: 숫자 계산은 전부 여기서 결정적으로 수행하며 AI에 맡기지 않는다.
/// 산정식 문자열은 검토서의 표기(예: "6,030.10 x 0.70 = 4,221.07 m²")를 그대로 따른다.
/// </summary>
public static class QuantitativeCalculator
{
    public static DesignOverview Calculate(ProjectInput p)
    {
        var o = new DesignOverview();

        // 건폐율: 건축면적 / 대지면적
        o.CoverageRatio = p.SiteArea > 0 ? p.PlannedBuildingArea / p.SiteArea * 100.0 : 0;
        o.CoverageFormula =
            $"{N(p.PlannedBuildingArea)} m² / {N(p.SiteArea)} m² x 100 = {N(o.CoverageRatio)} %";

        // 계획 건축면적은 검토서에 "대지면적 x 실제 건폐율 = 건축면적"으로 적는다
        // (실무 표준: "6,030.10 x 0.241 = 1,453.22 m²").
        if (p.SiteArea > 0)
            o.BuildingAreaFormula =
                $"{N(p.SiteArea)} x {(o.CoverageRatio / 100.0).ToString("0.000", Ci)} = {N(p.PlannedBuildingArea)} m²";

        if (p.Zoning.MaxCoverageRatio is double maxCov)
        {
            o.MaxBuildingArea = p.SiteArea * maxCov / 100.0;
            o.MaxBuildingAreaFormula =
                $"{N(p.SiteArea)} x {(maxCov / 100.0).ToString("0.00", Ci)} = {N(o.MaxBuildingArea.Value)} m²";
            o.CoverageCompliant = p.PlannedBuildingArea <= o.MaxBuildingArea + Epsilon;
        }

        // 용적률: 연면적 / 대지면적
        o.GrossFloorArea = p.GrossFloorArea;
        o.FloorAreaRatio = p.SiteArea > 0 ? o.GrossFloorArea / p.SiteArea * 100.0 : 0;
        o.FloorAreaRatioFormula =
            $"{N(o.GrossFloorArea)} m² / {N(p.SiteArea)} m² x 100 = {N(o.FloorAreaRatio)} %";

        if (p.SiteArea > 0)
            o.GrossAreaFormula =
                $"{N(p.SiteArea)} x {(o.FloorAreaRatio / 100.0).ToString("0.000", Ci)} = {N(o.GrossFloorArea)} m²";

        if (p.Zoning.MaxFloorAreaRatio is double maxFar)
        {
            o.MaxGrossFloorArea = p.SiteArea * maxFar / 100.0;
            o.MaxGrossFloorAreaFormula =
                $"{N(p.SiteArea)} x {(maxFar / 100.0).ToString("0.00", Ci)} = {N(o.MaxGrossFloorArea.Value)} m²";
            o.FloorAreaCompliant = o.GrossFloorArea <= o.MaxGrossFloorArea + Epsilon;
        }

        if (p.Zoning.MaxFloors is int maxFloors)
            o.FloorsCompliant = p.PlannedFloorsAbove <= maxFloors;

        // 조경면적: 법정은 "대지면적 x 비율 이상"으로 표기한다 (실무 표준: "301.51 m² 이상").
        if (p.Zoning.LandscapeRatio is double landRatio && p.SiteArea > 0)
        {
            o.RequiredLandscapeArea = p.SiteArea * landRatio;
            o.LandscapeFormula = $"{N(o.RequiredLandscapeArea.Value)} m² 이상";
        }

        o.Parking = CalculateParking(p.GrossFloorArea, p.Parking);
        if (o.MaxGrossFloorArea is double legalGfa)
            o.ParkingAtLegalMax = CalculateParking(legalGfa, p.Parking, isLegalColumn: true);

        return o;
    }

    /// <summary>
    /// 부설주차장 대수 산정.
    /// 총 대수: 소수점 이하 0.5 이상이면 올림, 미만이면 버림 (주차장법 시행령 별표1 비고).
    /// 장애인전용: 총 대수의 일정 비율 이상, 반올림하되 최소 1대.
    /// </summary>
    public static ParkingResult CalculateParking(double grossFloorArea, ParkingRule rule, bool isLegalColumn = false)
    {
        var r = new ParkingResult();
        r.RawSpaces = rule.AreaPerSpace > 0 ? grossFloorArea / rule.AreaPerSpace : 0;
        r.TotalSpaces = RoundHalfUpFloor(r.RawSpaces);
        r.TotalFormula =
            $"{N(grossFloorArea)} m² / {N(rule.AreaPerSpace)} m² = {N(r.RawSpaces)} 대 ({r.TotalSpaces} 대)";

        // 전체 행 표기: 장애인 의무 대상이면 "(장애인 포함)"을 붙이고, 법정 열은 "이상"을 덧붙인다.
        var includeNote = r.TotalSpaces >= rule.DisabledMinTotal ? " (장애인 포함)" : "";
        r.TotalDisplay = isLegalColumn
            ? $"{r.TotalSpaces} 대 이상{includeNote}"
            : $"{r.TotalSpaces} 대{includeNote}";

        if (r.TotalSpaces >= rule.DisabledMinTotal)
        {
            r.RawDisabled = r.TotalSpaces * rule.DisabledRatio;
            r.DisabledSpaces = Math.Max(1, (int)Math.Round(r.RawDisabled.Value, MidpointRounding.AwayFromZero));
            r.DisabledFormula =
                $"{r.TotalSpaces} 대 x {(rule.DisabledRatio * 100).ToString("0.00", Ci)} % = {N(r.RawDisabled.Value)} 대 ({r.DisabledSpaces} 대)";
        }

        if (r.TotalSpaces >= rule.ExpandedMinTotal)
        {
            r.RawExpanded = r.TotalSpaces * rule.ExpandedRatio;
            r.ExpandedSpaces = Math.Max(1, (int)Math.Round(r.RawExpanded.Value, MidpointRounding.AwayFromZero));
            r.ExpandedFormula =
                $"{r.TotalSpaces} 대 x {(rule.ExpandedRatio * 100).ToString("0.00", Ci)} % = {N(r.RawExpanded.Value)} 대 ({r.ExpandedSpaces} 대)";
        }
        else
        {
            // 대상이 아니면 검토서에 그 사실을 적는다 (빈칸으로 두지 않는다).
            r.ExpandedFormula = $"주차대수 {rule.ExpandedMinTotal}대 이상 해당";
        }

        if (r.TotalSpaces >= rule.EcoMinTotal)
        {
            r.RawEco = r.TotalSpaces * rule.EcoRatio;
            r.EcoSpaces = Math.Max(1, (int)Math.Round(r.RawEco.Value, MidpointRounding.AwayFromZero));
            r.EcoFormula =
                $"{r.TotalSpaces} 대 x {(rule.EcoRatio * 100).ToString("0.00", Ci)} % = {N(r.RawEco.Value)} 대 ({r.EcoSpaces} 대)";
        }
        else
        {
            r.EcoFormula = $"주차대수 {rule.EcoMinTotal}대 이상 해당";
        }

        return r;
    }

    /// <summary>단수가 0.5 이상이면 올림, 미만이면 버림.</summary>
    internal static int RoundHalfUpFloor(double value)
    {
        var floor = Math.Floor(value);
        return (int)(value - floor >= 0.5 ? floor + 1 : floor);
    }

    private const double Epsilon = 0.005;
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>검토서 표기용 숫자 형식: 천단위 구분, 소수 둘째 자리.</summary>
    internal static string N(double v) => v.ToString("#,##0.00", Ci);
}

/// <summary>설계개요 표의 계산 결과. 계획값·법정값·산정식을 모두 담는다.</summary>
public sealed class DesignOverview
{
    public double CoverageRatio { get; set; }
    public string CoverageFormula { get; set; } = "";
    /// <summary>계획 건축면적 표기 ("대지면적 x 건폐율 = 건축면적").</summary>
    public string BuildingAreaFormula { get; set; } = "";
    public double? MaxBuildingArea { get; set; }
    public string? MaxBuildingAreaFormula { get; set; }
    public bool? CoverageCompliant { get; set; }

    public double GrossFloorArea { get; set; }
    public double FloorAreaRatio { get; set; }
    public string FloorAreaRatioFormula { get; set; } = "";
    /// <summary>계획 연면적 표기 ("대지면적 x 용적률 = 연면적").</summary>
    public string GrossAreaFormula { get; set; } = "";
    public double? MaxGrossFloorArea { get; set; }
    public string? MaxGrossFloorAreaFormula { get; set; }
    public bool? FloorAreaCompliant { get; set; }

    public bool? FloorsCompliant { get; set; }

    /// <summary>법정 조경면적 (대지면적 x 비율).</summary>
    public double? RequiredLandscapeArea { get; set; }
    public string? LandscapeFormula { get; set; }

    public ParkingResult Parking { get; set; } = new();
    public ParkingResult? ParkingAtLegalMax { get; set; }
}

public sealed class ParkingResult
{
    public double RawSpaces { get; set; }
    public int TotalSpaces { get; set; }
    public string TotalFormula { get; set; } = "";
    /// <summary>"전체" 행 표기 (예: "12 대 (장애인 포함)", 법정 열은 "106 대 이상 (장애인 포함)").</summary>
    public string TotalDisplay { get; set; } = "";

    public double? RawDisabled { get; set; }
    public int? DisabledSpaces { get; set; }
    public string? DisabledFormula { get; set; }

    public double? RawExpanded { get; set; }
    public int? ExpandedSpaces { get; set; }
    public string? ExpandedFormula { get; set; }

    public double? RawEco { get; set; }
    public int? EcoSpaces { get; set; }
    public string? EcoFormula { get; set; }
}
