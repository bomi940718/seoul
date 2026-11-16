using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 용도지역 허용·불허 용도. 표본은 2026-11-16 법제처 원문(국토계획법 시행령 별표 5·7,
/// 서울특별시 도시계획 조례 별표 5·7)을 줄인 것 — 줄 꺾임까지 원문 그대로다.
/// </summary>
public class ZoneUseResolverTests
{
    private const string Decree5 = """
        ■ 국토의 계획 및 이용에 관한 법률 시행령 [별표 5] <개정 2023. 5. 15.>
          제2종일반주거지역안에서 건축할 수 있는
          건축물(제71조제1항제4호관련)
          1. 건축할 수 있는 건축물(경관관리 등을 위하여 도시ㆍ군계획조례로 건축물의
          층수를 제한하는 경우에는 그 층수 이하의 건축물로 한정한다)
            가. 「건축법 시행령」 별표 1 제1호의 단독주택
            나. 「건축법 시행령」 별표 1 제2호의 공동주택
            다. 「건축법 시행령」 별표 1 제3호의 제1종 근린생활시설
            마. 「건축법 시행령」 별표 1 제10호의 교육연구시설 중 유치원ㆍ초등학
          교·중학교 및 고등학교
          2. 도시ㆍ군계획조례가 정하는 바에 따라 건축할 수 있는 건축물
            가. 「건축법 시행령」 별표 1 제4호의 제2종 근린생활시설(단란주점 및 안
          마시술소를 제외한다)
            아. 「건축법 시행령」 별표 1 제14호의 업무시설 중 오피스텔·금융업소·
          사무소 및 동호 가목에 해당하는 것으로서 해당용도에 쓰이는 바닥면적의
          합계가 3천제곱미터 미만인 것
        """;

    private static readonly string[] Ord5 =
    {
        "[별표 5] <개정 2026. 1. 5.>",
        "제2종일반주거지역 안에서 건축할 수 있는 건축물(제33조제4호 관련)",
        "1. 영 별표 5 제1호와 제2호에 따라 제2종일반주거지역 안에서 건축할 수 있는 건축물의 층수는 다음 각 목과 같다.",
        "가. 「건축법 시행령」 별표 1 제2호가목에 따른 아파트를 건축하는 경우: 평균층수 13층 이하",
        "3. 제2종일반주거지역 안에서는 영 별표 5 제1호의 각 목의 건축물과 영 별표 5 제2호에 따라 다음 각 목의 건축물을 건축할 수 있다.",
        "가. 「건축법 시행령」 별표 1 제4호의 제2종근린생활시설 중 다음의 어느 하나에 해당하는 건축에 해당하는 건축물",
        "1) 공연장(너비 12미터 이상인 도로에 접한 대지에 건축하는 것에 한정한다)",
        "9) 일반음식점",
        "14) 금융업소, 사무소, 부동산중개사무소, 결혼상담소 등 소개업소, 출판사 등 일반업무시설",
        "아. 「건축법 시행령」 별표 1 제14호의 업무시설 중 공공업무시설·금융업소 및 사무소로서 해당 용도에 쓰이는 바닥면적의 합계가 3천제곱미터 미만인 것",
    };

    private static ZoneUseRules Rules2General() =>
        ZoneUseResolver.Build("제2종일반주거지역",
            new LawText("국토의 계획 및 이용에 관한 법률 시행령", "", Array.Empty<Article>(),
                new[] { new Annex("5", "제2종일반주거지역안에서 건축할 수 있는 건축물(제71조제1항제4호관련)", "별표", "", Decree5) }),
            "서울특별시 도시계획 조례", "[별표 5] <개정 2026. 1. 5.>", Ord5)!;

    [Fact]
    public void 지역지구_목록에서_용도지역을_고른다()
    {
        Assert.Equal("제2종일반주거지역",
            ZoneUseResolver.PickUseZone(new[] { "도시지역", "제2종일반주거지역", "가로구역별 최고높이 제한지역" }));
        Assert.Null(ZoneUseResolver.PickUseZone(new[] { "도시지역", "지구단위계획구역" }));
    }

    [Fact]
    public void 조례_세목에_있는_용도는_허용_근거는_세목까지()
    {
        var j = ZoneUseResolver.Judge(Rules2General(), "일반음식점", "4");
        Assert.Equal("허용", j.Verdict);
        Assert.Equal("서울특별시 도시계획 조례 별표 5 제3호가목 9)", j.Citation);

        var office = ZoneUseResolver.Judge(Rules2General(), "제2종근린생활시설(사무소)");
        Assert.Equal("허용", office.Verdict);
        Assert.EndsWith("제3호가목 14)", office.Citation);
    }

    [Fact]
    public void 층수_호는_용도_목록으로_쓰지_않는다()
    {
        // 조례 별표 5 제1호(층수)의 "아파트" 목은 허용 용도 목록이 아니다
        Assert.DoesNotContain(Rules2General().OrdinanceClauses, c => c.Section == 1);
    }

    [Fact]
    public void 법령이_바로_허용하는_용도()
    {
        var j = ZoneUseResolver.Judge(Rules2General(), "제1종근린생활시설(의원)");
        Assert.Equal("허용", j.Verdict);
        Assert.EndsWith("별표 5 제1호다목", j.Citation);
    }

    [Fact]
    public void 조례로_허용할_수_있지만_조례에_없으면_불허()
    {
        var j = ZoneUseResolver.Judge(Rules2General(), "단란주점");
        Assert.Equal("불허", j.Verdict);
    }

    [Fact]
    public void 목록에_없는_분류는_불허()
    {
        Assert.Equal("불허", ZoneUseResolver.Judge(Rules2General(), "숙박시설").Verdict);
    }

    [Fact]
    public void 면적으로_1종_2종이_갈리는_용도는_확인_필요()
    {
        // "사무소"만으로는 1종(30㎡ 미만 등)인지 2종인지 모른다 — 대장 용도코드가 있으면 그걸 쓴다
        Assert.Equal("확인 필요", ZoneUseResolver.Judge(Rules2General(), "사무소").Verdict);
        Assert.Equal("허용", ZoneUseResolver.Judge(Rules2General(), "사무소", "4").Verdict);
    }

    [Fact]
    public void 세부_용도를_모르면_조건부()
    {
        Assert.Equal("조건부 허용", ZoneUseResolver.Judge(Rules2General(), "업무시설").Verdict);
    }

    [Fact]
    public void 호_자체가_용도인_조례_별표도_읽는다()
    {
        // 서울 별표 7(준주거): 번호 없는 머리말 + "1. 「건축법 시행령」 별표 1 제5호의 …" 형식
        var lines = new[]
        {
            "[별표 7]",
            "준주거지역 안에서 건축할 수 없는 건축물 (제33조제6호 관련)",
            "준주거지역 안에서는 영 별표 7 제1호의 각 목의 건축물과 영 별표 7 제2호에 따라 다음 각 호의 건축물을 건축할 수 없다.",
            "2. 「건축법 시행령」 별표 1 제8호의 운수시설(철도시설은 제외한다)",
            "5. 「건축법 시행령」 별표 1 제19호의 위험물저장 및 처리시설 중 다음 각 목의 건축물",
            "나. 위험물 제조소‧저장소‧취급소",
        };
        const string decree7 = """
            ■ 국토의 계획 및 이용에 관한 법률 시행령 [별표 7]
              1. 건축할 수 없는 건축물
                가. 「건축법 시행령」 별표 1 제4호의 제2종 근린생활시설 중 단란주점
              2. 지역 여건 등을 고려하여 도시·군계획조례로 정하는 바에 따라 건축할 수
              없는 건축물
                라. 「건축법 시행령」 별표 1 제8호의 운수시설
            """;
        var r = ZoneUseResolver.Build("준주거지역",
            new LawText("", "", Array.Empty<Article>(),
                new[] { new Annex("7", "준주거지역안에서 건축할 수 없는 건축물(제71조제1항제6호 관련)", "별표", "", decree7) }),
            "서울특별시 도시계획 조례", "[별표 7]", lines)!;

        Assert.False(r.Permissive);
        Assert.Equal("불허", ZoneUseResolver.Judge(r, "운수시설").Verdict);
        Assert.Equal("서울특별시 도시계획 조례 별표 7 제2호", ZoneUseResolver.Judge(r, "운수시설").Citation);
        Assert.Equal("불허", ZoneUseResolver.Judge(r, "단란주점").Verdict);
        Assert.Equal("허용", ZoneUseResolver.Judge(r, "일반음식점").Verdict);
        // 제2종근린 중 단란주점만 금지 — 세부를 모르면 단정하지 않는다
        Assert.Equal("조건부 허용", ZoneUseResolver.Judge(r, "제2종근린생활시설").Verdict);
        Assert.Equal("허용", ZoneUseResolver.Judge(r, "위험물저장및처리시설(주유소)").Verdict);
    }
}
