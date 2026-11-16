using LawReview.Core.Models;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 인증 의무 등 대상 요건을 AI 전에 코드로 거른다.
/// 실제 사례(2026-11-16): 연면적 331㎡ 민간 근린생활시설 리모델링인데 에너지효율·녹색건축·BF가 "확인필요"로 나왔다.
/// </summary>
public class ItemScreenTests
{
    private static ProjectInput Remodel(double before, double after, bool isPublic = false) => new()
    {
        Mode = ProjectMode.리모델링,
        IsPublic = isPublic,
        Buildings = { new BuildingArea { Name = "주동", Floors = { new FloorArea { ExistingExclusiveArea = before, ExclusiveArea = after } } } },
    };

    private static ProjectInput New(double area, bool isPublic = false) => new()
    {
        Mode = ProjectMode.신축,
        IsPublic = isPublic,
        Buildings = { new BuildingArea { Name = "주동", Floors = { new FloorArea { ExclusiveArea = area } } } },
    };

    [Fact]
    public void 면적이_늘지_않는_리모델링은_신축_증축_대상_항목이_아니다()
    {
        var r = ReviewEngine.ScreenOut(new ItemScreen { NewWorkOnly = true, MinArea = 1000 }, Remodel(331.31, 331.31));
        Assert.NotNull(r);
        Assert.Contains("면적 증가가 없어", r!.Value.Reason);
    }

    [Fact]
    public void 연면적이_기준_미만이면_해당없음()
    {
        var r = ReviewEngine.ScreenOut(new ItemScreen { NewWorkOnly = true, MinArea = 1000 }, New(331.31));
        Assert.NotNull(r);
        Assert.Contains("1,000㎡ 미만", r!.Value.Reason);
        Assert.Equal("연면적 331.31 ㎡ < 1,000 ㎡", r.Value.Calculation);
    }

    [Fact]
    public void 증축은_증가분으로_본다()
    {
        // 기존 900 → 1,950 : 증가 1,050㎡ ≥ 1,000 → 거르지 않는다(AI가 본다)
        Assert.Null(ReviewEngine.ScreenOut(new ItemScreen { NewWorkOnly = true, MinArea = 1000 }, Remodel(900, 1950)));
        // 기존 900 → 1,200 : 증가 300㎡ → 해당없음
        Assert.NotNull(ReviewEngine.ScreenOut(new ItemScreen { NewWorkOnly = true, MinArea = 1000 }, Remodel(900, 1200)));
    }

    [Fact]
    public void 공공_전용_항목은_민간이면_해당없음()
    {
        var s = new ItemScreen { NewWorkOnly = true, MinArea = 3000, PublicOnly = true };
        Assert.Contains("민간 건축물", ReviewEngine.ScreenOut(s, New(5000))!.Value.Reason);
        Assert.Null(ReviewEngine.ScreenOut(s, New(5000, isPublic: true)));
    }
}
