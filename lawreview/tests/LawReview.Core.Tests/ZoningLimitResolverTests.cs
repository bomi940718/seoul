using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 도시계획조례에서 용도지역별 법정 건폐율·용적률을 뽑는지 검증.
/// 본문은 대전광역시 도시계획 조례(실제 조문, 2026-02-13 시행)에서 발췌.
/// </summary>
public class ZoningLimitResolverTests
{
    private static LawText DaejeonOrdinance() => new(
        "대전광역시 도시계획 조례", "20260213", new[]
        {
            // 완화 조문 — 제목에 "건폐율"이 들어가지만 용도지역 목록이 아니다(오탐 방지 대상)
            new Article("19", "지구단위계획구역 안에서의 건폐율 등의 완화적용",
                "제19조① 삭제 ② 지구단위계획구역 안에서 건축물을 건축하려는 자가…"),
            new Article("45", "용도지역 안에서의 건폐율",
                "제45조(용도지역 안에서의 건폐율)① 법 제77조 및 영 제84조에 따른 용도지역 안에서의 건폐율은 다음 각 호와 같다. " +
                "1. 제1종전용주거지역: 50퍼센트 이하2. 제2종전용주거지역: 40퍼센트 이하8. 일반상업지역: 70퍼센트 이하" +
                "11. 전용공업지역: 70퍼센트 이하12. 일반공업지역: 70퍼센트 이하13. 준공업지역: 70퍼센트 이하" +
                "16. 자연녹지지역: 20퍼센트 이하"),
            new Article("50", "용도지역 안에서의 용적률",
                "제50조(용도지역 안에서의 용적률)① 법 제78조제1항 및 영 제85조에 따른 용도지역 안에서의 용적률은 다음 각 호와 같다." +
                "7. 중심상업지역: 1,300퍼센트 이하8. 일반상업지역: 1,100퍼센트 이하" +
                "11. 전용공업지역: 300퍼센트 이하12. 일반공업지역: 350퍼센트 이하13. 준공업지역: 400퍼센트 이하"),
        });

    [Fact]
    public void 용도지역으로_건폐율_용적률을_찾는다()
    {
        // 둔곡 프로젝트의 실제 용도지역 목록(자동조회 결과와 같은 형태)
        var zones = new[] { "도시지역", "산업육성구역", "가축사육제한구역", "연구개발특구", "지구단위계획구역", "일반공업지역" };

        var r = ZoningLimitResolver.Resolve(DaejeonOrdinance(), zones);

        // 실무 검토서의 법정 한도와 일치해야 한다
        Assert.Equal(70, r.CoverageRatio);
        Assert.Equal(350, r.FloorAreaRatio);
        Assert.Equal("일반공업지역", r.MatchedZone);
        // 근거는 완화 조문(제19조)이 아니라 용도지역 조문이어야 한다
        Assert.Equal("대전광역시 도시계획 조례 제45조", r.CoverageBasis);
        Assert.Equal("대전광역시 도시계획 조례 제50조", r.FloorAreaBasis);
    }

    [Fact]
    public void 천단위_구분이_있는_용적률도_읽는다()
    {
        var r = ZoningLimitResolver.Resolve(DaejeonOrdinance(), new[] { "도시지역", "일반상업지역" });
        Assert.Equal(70, r.CoverageRatio);
        Assert.Equal(1100, r.FloorAreaRatio);
    }

    [Fact]
    public void 부분_일치로_엉뚱한_용도지역을_잡지_않는다()
    {
        // "도시지역"만 있으면 조례 목록에 그 이름이 없으므로 아무것도 찾지 못해야 한다.
        // (예전 방식으로 부분 일치를 허용하면 "자연녹지지역" 등에 잘못 걸린다)
        var r = ZoningLimitResolver.Resolve(DaejeonOrdinance(), new[] { "도시지역", "지구단위계획구역" });
        Assert.Null(r.CoverageRatio);
        Assert.Null(r.FloorAreaRatio);
    }

    [Fact]
    public void 조례를_못_받아오면_빈_결과를_돌려준다()
    {
        var r = ZoningLimitResolver.Resolve(null, new[] { "일반공업지역" });
        Assert.Null(r.CoverageRatio);
        Assert.Null(r.MatchedZone);
    }
}
