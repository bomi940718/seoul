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
// ── 안양시: 제목 표기가 다르고, 공장·지식산업센터 단서가 붙는다 ────────────
    // 본문은 안양시 도시계획 조례 제30·49·50조 실제 문구(2026-03-31 개정분 포함)에서 발췌.

    private static LawText AnyangOrdinance() => new(
        "안양시 도시계획 조례", "20260331", new[]
        {
            // 경관지구 조문 — 제목에 "건폐율"이 들어가고 조례 순서상 먼저 나온다(오탐 방지 대상)
            new Article("30", "경관지구에서의 건폐율",
                "제30조(경관지구에서의 건폐율)① 영 제72조제2항에 따라 경관지구에서 건축하는 건축물의 건폐율은 " +
                "다음 각 호의 비율 이하로 한다.1. 자연경관지구: 100분의 402. 특화경관지구: 100분의 50"),
            new Article("49", "용도지역에서의 건폐율",
                "제49조(용도지역에서의 건폐율)① 영 제84조제1항에 따라 도시계획조례로 정하는 용도지역에서의 건폐율은 " +
                "다음 각 호와 같다.7. 중심상업지역: 80퍼센트 이하8. 일반상업지역: 80퍼센트 이하" +
                "12. 일반공업지역: 70퍼센트 이하13. 준공업지역: 70퍼센트 이하"),
            new Article("50", "용도지역에서의 용적률",
                "제50조(용도지역에서의 용적률)① 영 제85조제1항에 따른 용도지역에서 도시계획조례로 정하는 용적률은 " +
                "다음 각 호와 같다.7. 중심상업지역: 1,000퍼센트 이하8. 일반상업지역: 800퍼센트 이하" +
                "12. 일반공업지역: 300퍼센트 이하(「건축법 시행령」 별표 1 제17호의 공장 및 「산업집적활성화 및 " +
                "공장설립에 관한 법률」 제2조제13호에 따른 지식산업센터의 경우에는 350퍼센트 이하로 한다)" +
                "13. 준공업지역: 300퍼센트 이하(「건축법 시행령」 별표 1 제17호의 공장 및 지식산업센터의 경우에는 " +
                "400퍼센트 이하, 「건축법 시행령」별표 1 제2호가목의 아파트의 경우에는 250퍼센트 이하로 한다)" +
                "14. 보전녹지지역: 80퍼센트 이하"),
        });

    [Fact]
    public void 안양_용도지역에서의_표기도_찾는다()
    {
        // 예전에는 "용도지역 안에서의 건폐율"만 찾아서 안양에서는 법정 한도가 통째로 비었다.
        var r = ZoningLimitResolver.Resolve(AnyangOrdinance(), new[] { "도시지역", "중심상업지역" });

        Assert.Equal(80, r.CoverageRatio);
        Assert.Equal(1000, r.FloorAreaRatio);   // "1,000"의 쉼표
        Assert.StartsWith("안양시 도시계획 조례 제49조", r.CoverageBasis);
    }

    [Fact]
    public void 경관지구_조문을_용도지역_한도로_오인하지_않는다()
    {
        var r = ZoningLimitResolver.Resolve(AnyangOrdinance(), new[] { "일반공업지역" });
        Assert.StartsWith("안양시 도시계획 조례 제49조", r.CoverageBasis);   // 제30조가 아니다
    }

    [Fact]
    public void 용도별_단서가_있으면_근거에_함께_적는다()
    {
        // 앞의 300만 집으면 공장 프로젝트의 법정 용적률이 틀린 값으로 채워진다.
        // 용도 판단은 단정하지 않고(원칙 5) 단서를 그대로 보여준다.
        var r = ZoningLimitResolver.Resolve(AnyangOrdinance(), new[] { "준공업지역" });

        Assert.Equal(300, r.FloorAreaRatio);
        Assert.Contains("※ 단서", r.FloorAreaBasis);
        Assert.Contains("400퍼센트", r.FloorAreaBasis);
        Assert.Contains("250퍼센트", r.FloorAreaBasis);
        Assert.DoesNotContain("보전녹지", r.FloorAreaBasis);   // 다음 호까지 끌려오지 않는다
    }

    [Fact]
    public void 단서가_없으면_근거는_조문만이다()
    {
        var r = ZoningLimitResolver.Resolve(AnyangOrdinance(), new[] { "중심상업지역" });
        Assert.Equal("안양시 도시계획 조례 제50조", r.FloorAreaBasis);
    }
}
