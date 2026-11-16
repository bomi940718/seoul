using LawReview.Core.LandUse;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 건축물대장(건축HUB) 응답 해석. 표본은 2026-11-16 실제 응답(강남구 역삼동 737)을 줄인 것.
/// </summary>
public class BuildingRegisterTests
{
    [Fact]
    public void PNU를_건축HUB_필지_파라미터로_바꾼다()
    {
        // PNU 11번째 자리: 1=대지, 2=산 → 건축HUB platGbCd: 0=대지, 1=산
        Assert.Equal("sigunguCd=11680&bjdongCd=10100&platGbCd=0&bun=0737&ji=0000",
            BuildingRegisterClient.ParcelQuery("1168010100107370000"));
        Assert.Equal("sigunguCd=41173&bjdongCd=10200&platGbCd=1&bun=0012&ji=0003",
            BuildingRegisterClient.ParcelQuery("4117310200200120003"));
        Assert.Throws<ArgumentException>(() => BuildingRegisterClient.ParcelQuery("1168010100"));
    }

    private const string TitleJson = """
        {"response":{"header":{"resultCode":"00","resultMsg":"NORMAL SERVICE"},"body":{"items":{"item":[
        {"dongNm":"주건축물제1동","mainAtchGbCd":"0","mainAtchGbCdNm":"주건축물","regstrKindCdNm":"일반건축물",
         "platArea":13156.7,"archArea":5600.51,"bcRat":42.5677,"totArea":212615.29,"vlRatEstmTotArea":130933.99,
         "vlRat":995.1887,"strctCdNm":"철골철근콘크리트구조","mainPurpsCdNm":"업무시설","etcPurps":"업무시설,근린생활시설",
         "heit":202.65,"grndFlrCnt":45,"ugrndFlrCnt":8,"indrMechUtcnt":0,"oudrMechUtcnt":0,"indrAutoUtcnt":1300,
         "oudrAutoUtcnt":12,"useAprDay":"20010731","pmsDay":"19950504","pmsnoYear":" "}]},
         "numOfRows":"2","pageNo":"1","totalCount":"1"}}}
        """;

    [Fact]
    public void 표제부를_읽는다()
    {
        var (items, total) = BuildingRegisterClient.ParsePage(TitleJson);
        Assert.Equal(1, total);   // totalCount가 문자열로 온다
        var b = BuildingRegisterClient.ParseTitle(items[0]);

        Assert.Equal("주건축물제1동", b.DisplayName);
        Assert.False(b.IsAnnex);
        Assert.Equal(5600.51, b.BuildingArea);
        Assert.Equal(212615.29, b.GrossFloorArea);
        Assert.Equal(45, b.FloorsAbove);
        Assert.Equal(8, b.FloorsBelow);
        Assert.Equal(202.65, b.Height);
        Assert.Equal(1300, b.ParkingIndoor);
        Assert.Equal(12, b.ParkingOutdoor);
        Assert.Equal("2001-07-31", b.ApprovalDate);
    }

    [Fact]
    public void 층별개요는_기타용도를_대장_용도로_쓰고_아래층부터_정렬한다()
    {
        const string json = """
            {"response":{"header":{"resultCode":"00"},"body":{"items":{"item":[
            {"dongNm":"주건축물제1동","mainAtchGbCd":"0","flrGbCd":"20","flrGbCdNm":"지상","flrNo":3,"flrNoNm":"3층",
             "mainPurpsCdNm":"의원","etcPurps":"제1종근린생활시설(의원)","area":178.49,"areaExctYn":"0"},
            {"dongNm":"주건축물제1동","mainAtchGbCd":"0","flrGbCd":"30","flrGbCdNm":"옥탑","flrNo":1,"flrNoNm":"옥탑1층",
             "mainPurpsCdNm":"계단실","etcPurps":" ","area":20.0,"areaExctYn":"1"},
            {"dongNm":"주건축물제1동","mainAtchGbCd":"0","flrGbCd":"10","flrGbCdNm":"지하","flrNo":2,"flrNoNm":"지2층",
             "mainPurpsCdNm":"주차장","etcPurps":"주차장","area":500.0,"areaExctYn":"0"},
            {"dongNm":"주건축물제1동","mainAtchGbCd":"0","flrGbCd":"20","flrGbCdNm":"지상","flrNo":1,"flrNoNm":"1층",
             "mainPurpsCdNm":"골프연습장","etcPurps":"운동시설(골프연습장)","area":491.753,"areaExctYn":"0"}]},
             "totalCount":"4"}}}
            """;
        var floors = BuildingRegisterClient.ParsePage(json).Items.Select(BuildingRegisterClient.ParseFloor)
            .OrderBy(f => f.SortKey).ToList();

        Assert.Equal(new[] { "지2층", "1층", "3층", "옥탑1층" }, floors.Select(f => f.Label));
        Assert.Equal("제1종근린생활시설(의원)", floors[2].Use);
        Assert.Equal("계단실", floors[3].Use);          // 기타용도가 비면 주용도
        Assert.True(floors[3].ExcludedFromGross);
        Assert.Equal(491.753, floors[1].Area);
    }

    [Fact]
    public void 한_건이면_배열이_아니라_객체로_와도_읽는다()
    {
        const string json = """{"response":{"header":{"resultCode":"00"},"body":{"items":{"item":{"dongNm":"A동","archArea":"120.5"}},"totalCount":"1"}}}""";
        var (items, _) = BuildingRegisterClient.ParsePage(json);
        Assert.Single(items);
        Assert.Equal(120.5, BuildingRegisterClient.ParseTitle(items[0]).BuildingArea);
    }

    [Fact]
    public void 건축물이_없으면_빈_목록()
    {
        const string json = """{"response":{"header":{"resultCode":"00"},"body":{"items":"","totalCount":"0"}}}""";
        Assert.Empty(BuildingRegisterClient.ParsePage(json).Items);
    }

    [Fact]
    public void 키_오류는_고칠_수_있는_말로_알린다()
    {
        const string xml = """
            <OpenAPI_ServiceResponse><cmmMsgHeader><errMsg>SERVICE ERROR</errMsg>
            <returnAuthMsg>SERVICE_KEY_IS_NOT_REGISTERED_ERROR</returnAuthMsg><returnReasonCode>30</returnReasonCode>
            </cmmMsgHeader></OpenAPI_ServiceResponse>
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => BuildingRegisterClient.ParsePage(xml));
        Assert.Contains("활용신청", ex.Message);
    }

    [Fact]
    public void 게이트웨이_연결실패_JSON은_빈_결과가_아니라_오류다()
    {
        // 2026-11-16 실측: 쉬었다 들어온 첫 요청에 503과 함께 오는 형식. 빈 결과로 넘기면 "건축물 없음"이 된다.
        const string json = """
            {"OpenAPI_ServiceResponse":{"cmmMsgHeader":{"errMsg":"SERVICETIMEOUT_ERROR",
             "returnAuthMsg":"서비스 연결실패 에러","returnReasonCode":"05"}}}
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => BuildingRegisterClient.ParsePage(json));
        Assert.Contains("응답하지 않습니다", ex.Message);
    }

    [Fact]
    public void 총괄표제부가_없으면_동별_합으로_필지_합계를_낸다()
    {
        var reg = new BuildingRegister("1168010100107370000", null, new()
        {
            new RegisterBuilding { DongName = "A동", BuildingArea = 100, GrossFloorArea = 300 },
            new RegisterBuilding { DongName = "B동", BuildingArea = 50, GrossFloorArea = 80 },
        });
        Assert.Equal(150, reg.TotalBuildingArea);
        Assert.Equal(380, reg.TotalGrossFloorArea);

        var withRecap = reg with { Recap = new RegisterRecap { BuildingArea = 140, GrossFloorArea = 370 } };
        Assert.Equal(140, withRecap.TotalBuildingArea);
    }
}
