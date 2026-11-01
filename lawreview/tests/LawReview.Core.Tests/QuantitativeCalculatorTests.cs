using LawReview.Core.Models;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 검증 기준: 실제 실무 검토서(대전 둔곡 세이퍼존 공장, 2023.10)의 수치.
/// 대지 6,030.10㎡ / 건축면적 1,453.22㎡ / 연면적 2,484.43㎡ / 건폐율 70% / 용적률 350% 한도.
/// </summary>
public class QuantitativeCalculatorTests
{
    private static ProjectInput DungokProject() => new()
    {
        ProjectName = "세이퍼존 둔곡 공장",
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

    [Fact]
    public void 연면적은_제외층을_빼고_합산한다()
    {
        var p = DungokProject();
        // A동 소계 2,465.63 (PIT 110.06 제외) + B동 18.80 = 2,484.43 — 검토서 수치와 일치해야 한다.
        Assert.Equal(2484.43, p.GrossFloorArea, 2);
        Assert.Equal(2465.63, p.Buildings[0].GrossFloorArea, 2);
    }

    [Fact]
    public void 건폐율_계산이_검토서와_일치한다()
    {
        var o = QuantitativeCalculator.Calculate(DungokProject());
        Assert.Equal(24.10, o.CoverageRatio, 2);
        Assert.Equal(4221.07, o.MaxBuildingArea!.Value, 2);   // 6,030.10 × 0.70
        Assert.True(o.CoverageCompliant);
        Assert.Contains("4,221.07", o.MaxBuildingAreaFormula);
    }

    [Fact]
    public void 용적률_계산이_검토서와_일치한다()
    {
        var o = QuantitativeCalculator.Calculate(DungokProject());
        Assert.Equal(41.20, o.FloorAreaRatio, 2);
        // 참고: 원본 검토서에는 법정 연면적이 21,150.35로 적혀 있으나 6,030.10×3.5 = 21,105.35가 맞다.
        // (사람이 만든 문서의 전기 오류 — 자동화가 잡아내는 사례)
        Assert.Equal(21105.35, o.MaxGrossFloorArea!.Value, 2);
        Assert.True(o.FloorAreaCompliant);
    }

    [Fact]
    public void 주차대수_산정이_검토서와_일치한다()
    {
        var o = QuantitativeCalculator.Calculate(DungokProject());
        // 2,484.43 / 200 = 12.42 → 12대 (0.5 미만 버림), 장애인 12×3% = 0.36 → 1대 (최소 1대)
        Assert.Equal(12, o.Parking.TotalSpaces);
        Assert.Equal(1, o.Parking.DisabledSpaces);
        Assert.Null(o.Parking.ExpandedSpaces);  // 50대 미만 → 확장형 의무 없음
        Assert.Contains("12.42", o.Parking.TotalFormula);
    }

    [Theory]
    [InlineData(12.42, 12)]    // 0.5 미만 버림
    [InlineData(105.75, 106)]  // 0.5 이상 올림
    [InlineData(0.4, 0)]
    [InlineData(12.5, 13)]
    public void 주차대수_단수처리_규칙(double raw, int expected) =>
        Assert.Equal(expected, QuantitativeCalculator.RoundHalfUpFloor(raw));

    [Fact]
    public void 건폐율_초과시_부적합_판정된다()
    {
        var p = DungokProject();
        p.PlannedBuildingArea = 4500.0;  // 한도 4,221.07 초과
        var o = QuantitativeCalculator.Calculate(p);
        Assert.False(o.CoverageCompliant);
    }

    [Fact]
    public void 확장형_친환경은_50대_미만이면_대상_아님을_표기한다()
    {
        // 실무 표준 서식의 표기: 계획 12대 → "주차대수 50대 이상 해당"
        var o = QuantitativeCalculator.Calculate(DungokProject());
        Assert.Null(o.Parking.ExpandedSpaces);
        Assert.Null(o.Parking.EcoSpaces);
        Assert.Equal("주차대수 50대 이상 해당", o.Parking.ExpandedFormula);
        Assert.Equal("주차대수 50대 이상 해당", o.Parking.EcoFormula);
    }

    [Fact]
    public void 법정_연면적_기준_주차는_확장형_친환경까지_산정한다()
    {
        // 법정 열은 법정 연면적(21,105.35)으로 계산 → 105.53 → 106대,
        // 106 x 3% = 3.18 → 3대 (장애인·확장형·친환경 동일)
        var o = QuantitativeCalculator.Calculate(DungokProject());
        var legal = o.ParkingAtLegalMax;
        Assert.NotNull(legal);
        Assert.Equal(106, legal!.TotalSpaces);
        Assert.Equal(3, legal.DisabledSpaces);
        Assert.Equal(3, legal.ExpandedSpaces);
        Assert.Equal(3, legal.EcoSpaces);
        Assert.Contains("3.18", legal.EcoFormula);
        // 법정 열의 전체 표기는 "이상"이 붙는다
        Assert.Equal("106 대 이상 (장애인 포함)", legal.TotalDisplay);
    }

    [Fact]
    public void 전체_주차_표기는_장애인_대상_여부를_반영한다()
    {
        var o = QuantitativeCalculator.Calculate(DungokProject());
        Assert.Equal("12 대 (장애인 포함)", o.Parking.TotalDisplay);   // 10대 이상이므로 포함 문구

        var small = DungokProject();
        small.Buildings.Clear();
        small.Buildings.Add(new BuildingArea
        {
            Name = "소규모",
            Floors = { new FloorArea { FloorLabel = "1층", Use = "공장", ExclusiveArea = 500 } },
        });
        var o2 = QuantitativeCalculator.Calculate(small);
        Assert.Equal(3, o2.Parking.TotalSpaces);        // 500/200 = 2.5 → 0.5 이상이므로 올림
        Assert.Equal("3 대", o2.Parking.TotalDisplay);  // 10대 미만이라 "(장애인 포함)" 없음
    }

    [Fact]
    public void 조경면적은_대지면적_비율로_산정된다()
    {
        var p = DungokProject();
        p.Zoning.LandscapeRatio = 0.05;      // 건축법 시행령 제27조 (5%)
        var o = QuantitativeCalculator.Calculate(p);
        Assert.True(Math.Abs(o.RequiredLandscapeArea!.Value - 301.505) < 0.001);
        Assert.Equal("301.51 m² 이상", o.LandscapeFormula);   // 표기는 소수 둘째 자리
    }

    [Fact]
    public void 건축면적_연면적_산정식이_표준_표기를_따른다()
    {
        // 실무 표준: "6,030.10 x 0.241 = 1,453.22 m²" / "6,030.10 x 0.412 = 2,484.43 m²"
        var o = QuantitativeCalculator.Calculate(DungokProject());
        Assert.Equal("6,030.10 x 0.241 = 1,453.22 m²", o.BuildingAreaFormula);
        Assert.Equal("6,030.10 x 0.412 = 2,484.43 m²", o.GrossAreaFormula);
    }
}
