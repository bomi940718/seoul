using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 공개 공지(건축법 제43조) 면적 비율 파싱 검증.
///
/// 시행령 제27조의2제2항은 "대지면적의 100분의 10 이하의 범위에서 건축조례로 정한다"라고만 해
/// **폴백할 값이 모법에 없다.** 조례를 못 읽으면 기준이 통째로 비므로 실제 문구로 고정한다.
/// </summary>
public class OpenSpaceRatioResolverTests
{
    [Fact]
    public void 대전_용도군별_비율을_구분해_읽는다()
    {
        // "1. 문화 및 집회시설, 종교시설, 판매시설 및 운수시설: 100분의 82. 업무시설…: 100분의 5"
        // — 호 번호가 값에 바로 붙어 있어 뭉뚱그려 끊으면 비율 8이 사라진다.
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("대전광역시 건축 조례"));

        Assert.Equal(0.08, std.For("판매시설")!.Ratio);
        Assert.Equal(0.05, std.For("업무시설")!.Ratio);
        Assert.Equal("대전광역시 건축 조례 제34조", std.Basis);
    }

    [Fact]
    public void 대전_문화_및_집회시설은_하나의_용도로_본다()
    {
        // "및"을 무조건 쪼개면 "문화"와 "집회시설"이 되어 매칭이 어긋난다.
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("대전광역시 건축 조례"));
        Assert.Equal(0.08, std.For("문화 및 집회시설")!.Ratio);
    }

    [Fact]
    public void 서울_용도_구분이_없으면_모든_용도에_같은_비율을_준다()
    {
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("서울특별시 건축 조례"));

        Assert.Equal(0.10, std.For("업무시설")!.Ratio);
        Assert.Equal(0.10, std.For("판매시설")!.Ratio);
    }

    [Fact]
    public void 속초_호마다_용도가_하나씩인_표기도_읽는다()
    {
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("속초시 건축 조례"));

        Assert.Equal(0.10, std.For("문화 및 집회시설")!.Ratio);
        Assert.Equal(0.05, std.For("업무시설")!.Ratio);
    }

    [Fact]
    public void 성남_바닥면적_구간별_비율을_모두_가져온다()
    {
        // 호 안이 목(가·나·다)으로 다시 갈린다. 첫 값만 읽으면 5%가 모든 규모에 적용되는 것처럼 된다.
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("성남시 건축 조례"));
        var rules = std.RulesFor("의료시설");

        Assert.Equal(new[] { 0.05, 0.07, 0.10 }, rules.Select(r => r.Ratio));
        Assert.Null(std.For("의료시설"));   // 하나로 정해지지 않는다
    }

    [Fact]
    public void 성남_법적기준은_구간을_모두_적고_구간별_면적을_계산한다()
    {
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("성남시 건축 조례"));
        var text = std.CriterionText("의료시설", 6030.10)!;

        Assert.Contains("5천제곱미터 이상 1만제곱미터 미만", text);
        Assert.Contains("301.51 m² 이상", text);   // 6,030.10 × 5%
        Assert.Contains("603.01 m² 이상", text);   // 6,030.10 × 10%
        Assert.Contains("성남시 건축 조례 제21조", text);
    }

    [Fact]
    public void 안양_표가_이미지면_값을_지어내지_않고_알린다()
    {
        // 조례 본문에 <img>만 있고 숫자가 없다. 자동 판독 불가를 정직하게 남긴다(원칙 5).
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("안양시 건축 조례"));

        Assert.Empty(std.Rules);
        Assert.Contains("이미지", std.Note);
        Assert.Equal("안양시 건축 조례 제30조", std.Basis);
    }

    [Fact]
    public void 의무면적은_대지면적에_비율을_곱해_계산한다()
    {
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("대전광역시 건축 조례"));
        var text = std.CriterionText("판매시설", 6030.10)!;

        Assert.Contains("대지면적의 8% 이상", text);
        Assert.Contains("6,030.10 x 0.08 = 482.41 m² 이상", text);
    }

    [Fact]
    public void 조례를_못_찾으면_빈_결과다()
    {
        Assert.Empty(OpenSpaceRatioResolver.Resolve(null).Rules);
        Assert.Empty(OpenSpaceRatioResolver
            .Resolve(new LawText("○○시 건축 조례", "20260101", Array.Empty<Article>())).Rules);
    }

    [Fact]
    public void 완화_산식의_비율을_면적_기준으로_잘못_읽지_않는다()
    {
        // 조문 뒤쪽 "건축기준의 완화" 항에는 용적률 산식과 "2분의 1" 같은 값이 섞여 있다.
        var std = OpenSpaceRatioResolver.Resolve(OrdinanceArticleFixture.Ordinance("대전광역시 건축 조례"));
        Assert.All(std.Rules, r => Assert.InRange(r.Ratio, 0.01, 0.20));
    }
}
