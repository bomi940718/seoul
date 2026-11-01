using LawReview.Core.Models;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 리모델링·증축·용도변경 — 변경전/변경후 계산 검증.
///
/// 법정 한도(건폐율·용적률) 검토는 **변경후 전체** 기준이다. 기존 부분도 함께 적법해야 하기 때문이다.
/// 주차만 변경전·변경후를 각각 산정해 차이를 낸다 — 주차장법 시행령 제6조제4항이 용도변경에 대해
/// "변경 후 − 변경 전"을 추가 확보하도록 정하고 있고, 증축도 실무에서 같은 방식으로 본다.
/// **추가 확보가 실제로 필요한지는 판정하지 않는다**(같은 조 단서에 면제 사유가 여럿 있다).
/// </summary>
public class ExistingBuildingTests
{
    /// <summary>기존 1,850㎡(PIT 200 별도) 공장을 2,250㎡로 증축하는 경우.</summary>
    private static ProjectInput Extension() => new()
    {
        Mode = ProjectMode.증축,
        SiteArea = 6030.10,
        PrimaryUse = "공장",
        PlannedBuildingArea = 1800,
        ExistingBuildingArea = 1453.22,
        Buildings =
        {
            new BuildingArea
            {
                Name = "A",
                Floors =
                {
                    new FloorArea { FloorLabel = "1층", ExclusiveArea = 1200, CommonArea = 100,
                                    ExistingExclusiveArea = 1000, ExistingCommonArea = 100 },
                    new FloorArea { FloorLabel = "2층", ExclusiveArea = 900, CommonArea = 50,
                                    ExistingExclusiveArea = 700, ExistingCommonArea = 50 },
                    new FloorArea { FloorLabel = "PIT", ExclusiveArea = 200, ExistingExclusiveArea = 200,
                                    ExcludeFromGrossArea = true },
                },
            },
        },
        Zoning = new ZoningLimits { MaxCoverageRatio = 70.0, MaxFloorAreaRatio = 350.0 },
        Parking = new ParkingRule { AreaPerSpace = 200 },
    };

    [Fact]
    public void 연면적_제외_층은_변경전에서도_뺀다()
    {
        // 같은 기준이어야 증감이 맞다. PIT 200㎡는 양쪽 모두에서 빠진다.
        var p = Extension();
        Assert.Equal(2250, p.GrossFloorArea);
        Assert.Equal(1850, p.ExistingGrossFloorArea);
        Assert.Equal(400, p.GrossFloorAreaDelta);
    }

    [Fact]
    public void 증감_연면적_산정식을_만든다()
    {
        var o = QuantitativeCalculator.Calculate(Extension());
        Assert.Equal("기존 1,850.00 + 증가 400.00 = 2,250.00 m²", o.GrossAreaChangeFormula);
    }

    [Fact]
    public void 변경전_건폐율_용적률을_기존_값으로_계산한다()
    {
        var o = QuantitativeCalculator.Calculate(Extension());

        Assert.Equal(24.10, o.ExistingCoverageRatio!.Value, 2);      // 1,453.22 / 6,030.10
        Assert.Equal(30.68, o.ExistingFloorAreaRatio!.Value, 2);     // 1,850.00 / 6,030.10
        Assert.Equal("1,850.00 m²", o.ExistingGrossAreaFormula);
    }

    [Fact]
    public void 법정_한도_검토는_변경후_전체_기준이다()
    {
        // 기존 부분도 함께 적법해야 하므로 증축분만 보지 않는다.
        var o = QuantitativeCalculator.Calculate(Extension());

        Assert.Equal(37.31, o.FloorAreaRatio, 2);     // 2,250.00 / 6,030.10 — 변경후 전체
        Assert.True(o.FloorAreaCompliant);
        Assert.True(o.CoverageCompliant);
    }

    [Fact]
    public void 주차는_변경전_변경후를_각각_내고_차이를_알린다()
    {
        var o = QuantitativeCalculator.Calculate(Extension());

        Assert.Equal(11, o.Parking.TotalSpaces);              // 2,250 / 200 = 11.25 → 11
        Assert.Equal(9, o.ExistingParking!.TotalSpaces);      // 1,850 / 200 = 9.25 → 9
        Assert.Equal(2, o.AdditionalParkingSpaces);
        Assert.Contains("추가 확보 대상", o.AdditionalParkingFormula);
    }

    [Fact]
    public void 면적이_줄면_추가_확보_대상이_아니라고_적는다()
    {
        var p = Extension();
        p.Buildings[0].Floors[0].ExclusiveArea = 500;   // 변경후가 기존보다 작아진다
        var o = QuantitativeCalculator.Calculate(p);

        Assert.True(o.GrossFloorAreaDelta < 0);
        Assert.Contains("증가분 없음", o.AdditionalParkingFormula);
    }

    [Fact]
    public void 신축이면_변경전_값을_만들지_않는다()
    {
        var p = Extension();
        p.Mode = ProjectMode.신축;
        var o = QuantitativeCalculator.Calculate(p);

        Assert.Null(o.ExistingGrossFloorArea);
        Assert.Null(o.ExistingParking);
        Assert.Null(o.AdditionalParkingSpaces);
        Assert.Null(o.GrossAreaChangeFormula);
    }

    [Fact]
    public void 신축이_아니어도_기존_면적이_없으면_변경전_값을_만들지_않는다()
    {
        // 모드만 바꾸고 기존 면적을 아직 입력하지 않은 상태 — 0으로 계산해 버리면 안 된다.
        var p = Extension();
        foreach (var f in p.Buildings[0].Floors) { f.ExistingExclusiveArea = 0; f.ExistingCommonArea = 0; }
        var o = QuantitativeCalculator.Calculate(p);

        Assert.False(p.HasExisting);
        Assert.Null(o.AdditionalParkingSpaces);
    }

    [Fact]
    public void 용도변경도_같은_경로를_쓴다()
    {
        var p = Extension();
        p.Mode = ProjectMode.용도변경;
        var o = QuantitativeCalculator.Calculate(p);

        Assert.Equal(2, o.AdditionalParkingSpaces);
    }
}
