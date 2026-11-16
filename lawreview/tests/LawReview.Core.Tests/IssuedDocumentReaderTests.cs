using LawReview.Core.LandUse;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 발급본 PDF 글자에서 대조 항목 찾기. 표본은 서식의 글자 흐름을 흉내 낸 것 —
/// 실제 발급본으로 확인되면 그 문구로 바꾼다.
/// </summary>
public class IssuedDocumentReaderTests
{
    [Fact]
    public void 소유자는_성명과_주민번호_앞자리로_찾는다_2인_이상도()
    {
        var lines = new[]
        {
            "소유자 현황", "성명(명칭) 주민(법인)등록번호 주소 소유권지분",
            "홍길동 800101-1****** 서울특별시 마포구 1/2",
            "김 영희 820202-2****** 서울특별시 서대문구 1/2",
            "주식회사 한빛 110111-0******",
        };
        var info = IssuedDocumentReader.Parse(IssuedDocKind.BuildingRegister, lines);
        Assert.True(info.HasText);
        Assert.Contains(info.Owners, o => o.Name == "홍길동" && o.IdFront == "800101");
        Assert.Contains(info.Owners, o => o.IdFront == "820202");
        Assert.Contains(info.Owners, o => o.Name.Contains("한빛") && o.IdFront == "110111");
        Assert.DoesNotContain(info.Owners, o => o.Name.Contains("성명"));
    }

    [Fact]
    public void 위반건축물_표기를_찾는다()
    {
        var yes = IssuedDocumentReader.Parse(IssuedDocKind.BuildingRegister, new[] { "위 반 건 축 물", "일반건축물대장(갑)" });
        var no = IssuedDocumentReader.Parse(IssuedDocKind.BuildingRegister, new[] { "일반건축물대장(갑)", "대지면적 167.1㎡" });
        Assert.True(yes.Violation);
        Assert.False(no.Violation);
        Assert.Null(IssuedDocumentReader.Parse(IssuedDocKind.LandRegister, new[] { "토지대장 위반건축물" }).Violation);
    }

    [Fact]
    public void 지목_면적_용도지역()
    {
        var land = IssuedDocumentReader.Parse(IssuedDocKind.LandRegister, new[] { "토지대장", "지목 면적(㎡)", "(08) 대 167.1" });
        Assert.Equal("대", land.Category);

        var plan = IssuedDocumentReader.Parse(IssuedDocKind.LandUsePlan,
            new[] { "토지이용계획확인서", "지목 대 면적 167.1㎡", "도시지역 , 제2종일반주거지역 , 가축사육제한구역" });
        Assert.Equal("대", plan.Category);
        Assert.Equal(167.1, plan.Area);
        Assert.Contains("제2종일반주거지역", plan.Zones);
        Assert.Empty(plan.Owners);
    }

    [Fact]
    public void 글자_없는_PDF는_HasText_false()
    {
        var info = IssuedDocumentReader.Parse(IssuedDocKind.BuildingRegister, new[] { "  ", "12 34" });
        Assert.False(info.HasText);
    }
}
