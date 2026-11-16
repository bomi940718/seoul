using LawReview.Core.Models;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 조례 제정 주체 판별. 이걸 틀리면 조례를 못 찾아 법정 한도·조례 조문이 통째로 빈다
/// (실제로 강원특별자치도 속초시 검토에서 발생했던 버그).
/// </summary>
public class MunicipalityTests
{
    [Theory]
    // 광역시·특별시는 광역이 제정한다 (자치구는 조례 주체가 아니다)
    [InlineData("대전광역시", "유성구", "대전광역시")]
    [InlineData("서울특별시", "관악구", "서울특별시")]
    // 도 산하는 시·군이 제정한다
    [InlineData("강원특별자치도", "속초시", "속초시")]
    [InlineData("경기도", "안양시", "안양시")]
    [InlineData("경상남도", "창원시 의창구", "창원시")]   // 자치구는 떼고 시까지만
    [InlineData("경기도", "성남시 분당구", "성남시")]
    // 단층제
    [InlineData("세종특별자치시", "세종특별자치시", "세종특별자치시")]
    public void 조례_제정_지자체를_고른다(string province, string city, string expected) =>
        Assert.Equal(expected, Municipality.OrdinanceAuthority(province, city));

    [Fact]
    public void 조례_자리표시자가_제정_지자체로_치환된다()
    {
        // 도 산하 프로젝트는 "속초시 건축 조례"가 되어야 한다("강원특별자치도 건축 조례"가 아니라)
        var sokcho = new ProjectInput { Province = "강원특별자치도", City = "속초시" };
        Assert.Equal("속초시 건축 조례", ReviewEngine.ResolvePlaceholders("{시} 건축 조례", sokcho));
        Assert.Equal("속초시 도시계획 조례", ReviewEngine.ResolvePlaceholders("{시} 도시계획 조례", sokcho));

        // 광역시는 광역시 조례를 그대로 쓴다
        var daejeon = new ProjectInput { Province = "대전광역시", City = "유성구" };
        Assert.Equal("대전광역시 주차장 조례", ReviewEngine.ResolvePlaceholders("{시} 주차장 조례", daejeon));
        Assert.Equal("유성구 건축 조례", ReviewEngine.ResolvePlaceholders("{구} 건축 조례", daejeon));
    }
}
