using LawReview.Core.LawApi;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 조문 본문의 항·호·목 분해 검증. 자치법규 본문은 줄바꿈 없는 평문 한 덩어리로 오고
/// **번호가 값에 바로 붙어** 있어서, 여기가 틀리면 조례 값이 통째로 사라진다.
/// </summary>
public class ArticleTextTests
{
    [Fact]
    public void 항을_동그라미_번호로_나눈다()
    {
        var paragraphs = ArticleText.Paragraphs("제34조(공개 공지)① 첫째 항이다.② 둘째 항이다.③ 셋째다.");

        Assert.Equal(3, paragraphs.Count);
        Assert.Equal("첫째 항이다.", paragraphs[0]);
        Assert.Equal("셋째다.", paragraphs[2]);
    }

    [Fact]
    public void 항_표시가_없으면_본문_전체를_한_덩어리로_본다()
    {
        Assert.Single(ArticleText.Paragraphs("제40조(대지 안의 공지) 기준은 별표 3과 같다."));
    }

    [Fact]
    public void 호_번호가_값에_바로_붙어도_값을_잃지_않는다()
    {
        // 실제 조례 표기: "…: 100분의 82. 업무시설…" — 앞 호의 값은 8이고 "2."가 다음 호다.
        var clauses = ArticleText.Clauses(
            "다음 각 호와 같다.1. 문화 및 집회시설, 판매시설: 100분의 82. 업무시설, 숙박시설: 100분의 5");

        Assert.Equal(2, clauses.Count);
        Assert.Equal(0.08, ArticleText.Ratio(clauses[0]));
        Assert.Equal(0.05, ArticleText.Ratio(clauses[1]));
    }

    [Fact]
    public void 날짜의_마침표를_호_번호로_읽지_않는다()
    {
        var text = ArticleText.StripTags("다음과 같다. <개정 2020.12.29.>1. 첫째2. 둘째");
        var clauses = ArticleText.Clauses(text);

        Assert.Equal(2, clauses.Count);
        Assert.Equal("첫째", clauses[0]);
    }

    [Fact]
    public void 목을_가나다로_나눈다()
    {
        var items = ArticleText.SubItems(
            "다음 각 목의 비율 이상으로 한다.가. 5천제곱미터 이상 1만제곱미터 미만 : 5퍼센트" +
            "나. 1만제곱미터 이상 : 7퍼센트");

        Assert.Equal(2, items.Count);
        Assert.Equal(0.05, ArticleText.Ratio(items[0]));
        Assert.Equal(0.07, ArticleText.Ratio(items[1]));
    }

    [Theory]
    [InlineData("100분의 15", 0.15)]
    [InlineData("15퍼센트", 0.15)]
    [InlineData("10%", 0.10)]
    [InlineData("100분의 8", 0.08)]
    public void 비율_표기를_소수로_읽는다(string text, double expected)
        => Assert.Equal(expected, ArticleText.Ratio(text));

    [Fact]
    public void 비율이_없으면_null이다() => Assert.Null(ArticleText.Ratio("2미터 이상"));

    [Fact]
    public void 용도_목록에서_및을_가려서_쪼갠다()
    {
        // "문화 및 집회시설"은 하나, "판매시설 및 운수시설"은 둘이다.
        var uses = ArticleText.SplitUses("문화 및 집회시설, 종교시설, 판매시설 및 운수시설");

        Assert.Equal(new[] { "문화 및 집회시설", "종교시설", "판매시설", "운수시설" }, uses);
    }

    [Fact]
    public void 괄호_안_단서는_용도명에서_떼어낸다()
    {
        var uses = ArticleText.SplitUses("판매시설(농수산물유통시설은 제외한다), 업무시설");
        Assert.Equal(new[] { "판매시설", "업무시설" }, uses);
    }
}
