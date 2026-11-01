using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 일조 등의 확보를 위한 높이 제한(건축법 제61조) 중 **조례가 정하는 값** 검증.
///
/// 정북방향 이격은 대체로 같지만 **인동거리 배수는 지자체마다 실제로 다르다**
/// (대전·속초 1배 / 안양 0.8배 / 서울 0.5배). 공동주택 배치에 바로 영향을 주므로
/// 모법 기본값으로 뭉뚱그리면 안 된다.
/// </summary>
public class SunlightHeightResolverTests
{
    [Theory]
    [InlineData("대전광역시 건축 조례", "43")]
    [InlineData("서울특별시 건축 조례", "35")]
    [InlineData("안양시 건축 조례", "35")]
    [InlineData("속초시 건축 조례", "43")]
    [InlineData("성남시 건축 조례", "27")]
    public void 정북방향_이격거리를_읽는다(string ordinance, string articleNo)
    {
        var std = SunlightHeightResolver.Resolve(OrdinanceArticleFixture.Ordinance(ordinance));

        Assert.Equal(2, std.NorthSetback.Count);   // 10미터 이하 / 초과 두 구간
        Assert.Contains(std.NorthSetback, s => s.Contains("1.5미터 이상"));
        Assert.Contains(std.NorthSetback, s => s.Contains("2분의 1"));
        Assert.Equal($"{ordinance} 제{articleNo}조", std.Basis);
    }

    [Theory]
    [InlineData("대전광역시 건축 조례", "1배")]
    [InlineData("서울특별시 건축 조례", "0.5배")]
    [InlineData("안양시 건축 조례", "0.8배")]
    [InlineData("속초시 건축 조례", "1배")]
    [InlineData("성남시 건축 조례", "0.8배")]
    public void 인동거리_배수는_지자체마다_다르다(string ordinance, string expected)
    {
        var std = SunlightHeightResolver.Resolve(OrdinanceArticleFixture.Ordinance(ordinance));

        Assert.NotEmpty(std.BetweenBuildings);
        Assert.Contains(expected, std.BetweenBuildings[0]);
    }

    [Fact]
    public void 삭제된_호는_버린다()
    {
        // 서울·안양 조례 제1항 제1호는 "삭제"다. 그대로 두면 기준 줄에 빈 항목이 섞인다.
        var std = SunlightHeightResolver.Resolve(OrdinanceArticleFixture.Ordinance("서울특별시 건축 조례"));
        Assert.DoesNotContain(std.NorthSetback, s => s.StartsWith("삭제"));
    }

    [Fact]
    public void 적용_제외_항을_기준으로_잘못_잡지_않는다()
    {
        // 안양 제2항은 "2층 이하로서 높이가 8미터 이하인 건축물은 제1항을 적용하지 아니한다"이다.
        var std = SunlightHeightResolver.Resolve(OrdinanceArticleFixture.Ordinance("안양시 건축 조례"));
        Assert.All(std.NorthSetback, s => Assert.DoesNotContain("적용하지 아니한다", s));
    }

    [Fact]
    public void 법적기준은_두_값을_모두_담고_출처를_밝힌다()
    {
        var text = SunlightHeightResolver
            .Resolve(OrdinanceArticleFixture.Ordinance("안양시 건축 조례")).CriterionText()!;

        Assert.Contains("정북방향 이격", text);
        Assert.Contains("인동거리", text);
        Assert.Contains("안양시 건축 조례 제35조", text);
    }

    [Fact]
    public void 조례를_못_찾으면_빈_결과다()
    {
        Assert.Null(SunlightHeightResolver.Resolve(null).CriterionText());
        Assert.Null(SunlightHeightResolver
            .Resolve(new LawText("○○시 건축 조례", "20260101", Array.Empty<Article>())).CriterionText());
    }
}
