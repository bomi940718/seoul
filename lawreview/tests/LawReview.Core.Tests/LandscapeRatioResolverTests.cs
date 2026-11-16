using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 건축조례에서 연면적 구간별 법정 조경면적 비율을 뽑는지 검증.
/// 본문은 실제 조례(대전광역시 제32조 / 안양시 제27조)에서 발췌했다 —
/// 지자체마다 "100분의 15"와 "15퍼센트"로 표기가 갈린다.
/// </summary>
public class LandscapeRatioResolverTests
{
    private static LawText Daejeon() => new(
        "대전광역시 건축 조례", "20260410", new[]
        {
            new Article("32", "대지의 조경",
                "제32조(대지의 조경)① 법 제42조제1항에 따른 조경에 필요한 면적은 다음 각 호의 기준에 따라야 한다." +
                "1. 연면적의 합계가 2천제곱미터 이상인 건축물: 대지면적의 100분의 15 이상" +
                "2. 연면적의 합계가 1천제곱미터 이상 2천제곱미터 미만인 건축물: 대지면적의 100분의 10 이상" +
                "3. 연면적의 합계가 1천제곱미터 미만인 건축물: 대지면적의 100분의 5 이상"),
        });

    private static LawText Anyang() => new(
        "안양시 건축 조례", "20260101", new[]
        {
            new Article("27", "대지의 조경",
                "제27조(대지의 조경)① 법 제42조제1항에서 규정한 기준이란 다음 각 호와 같다." +
                "1. 연면적이 2천제곱미터 이상인 건축물: 대지면적의 15퍼센트 이상" +
                "2. 연면적이 1천제곱미터 이상 2천제곱미터 미만인 건축물: 대지면적의 10퍼센트 이상" +
                "3. 연면적이 1천제곱미터 미만인 건축물: 대지면적의 5퍼센트 이상"),
        });

    [Fact]
    public void 연면적_구간별_비율을_읽는다()
    {
        var s = LandscapeRatioResolver.Resolve(Daejeon());

        Assert.Equal(3, s.Rules.Count);
        Assert.Equal("대전광역시 건축 조례 제32조", s.Basis);
        // 둔곡 연면적 2,484.43㎡ → 2천㎡ 이상 구간(15%)
        Assert.Equal(0.15, s.For(2484.43)!.Ratio, 3);
        Assert.Equal(0.10, s.For(1500)!.Ratio, 3);
        Assert.Equal(0.05, s.For(800)!.Ratio, 3);
    }

    [Fact]
    public void 퍼센트_표기_조례도_읽는다()
    {
        var s = LandscapeRatioResolver.Resolve(Anyang());
        Assert.Equal(0.15, s.For(2484.43)!.Ratio, 3);
        Assert.Equal(0.05, s.For(999)!.Ratio, 3);
    }

    [Fact]
    public void 구간_경계값이_정확하다()
    {
        var s = LandscapeRatioResolver.Resolve(Daejeon());
        Assert.Equal(0.10, s.For(1000)!.Ratio, 3);   // 1천 이상 2천 미만
        Assert.Equal(0.15, s.For(2000)!.Ratio, 3);   // 2천 이상
        Assert.Equal(0.05, s.For(999.99)!.Ratio, 3); // 1천 미만
    }

    [Fact]
    public void 조례를_못_받으면_빈_결과를_돌려준다()
    {
        var s = LandscapeRatioResolver.Resolve(null);
        Assert.Empty(s.Rules);
        Assert.Null(s.For(1000));
    }
}
