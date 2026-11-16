using LawReview.Core.LawApi;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 조례 본문(줄바꿈 없는 평문)을 항·호·목·세목마다 줄을 나눠 읽히게 한다.
/// 표본: 서울특별시 건축 조례 제7조 (2026-11-16 법제처 원문 앞부분).
/// </summary>
public class ArticleDisplayTests
{
    private const string Seoul7 =
        "제7조(기능 및 절차 등) ① 영 제5조의5제1항에 따른 위원회의 심의사항은 다음 각 호와 같이 구분한다. " +
        "<개정 2010.1.7, 2025.5.19, 2026.1.5>1. 시 위원회 심의사항가. 「서울특별시 건축 조례」의 제정ㆍ개정에 관한 사항" +
        "나. 법 제5조에 따른 건축법령의 적용 완화 여부 및 적용 범위에 관한 사항(허가권자가 시장인 경우를 말한다)" +
        "다. 영 제5조의5제1항제4호 또는 같은 항 제8호에 따른 심의대상건축물로 심의사항은 다음과 같다." +
        "1) 연면적의 합계가 10만제곱미터 이상이거나 21층 이상 건축물의 건축에 관한 사항" +
        "2) 시 또는 시가 설립한 공사가 시행하는 건축물의 건축에 관한 사항3) 삭제 <2025.5.19>" +
        "2. 구 위원회 심의사항가. 법 제46조제2항에 따른 건축선의 지정에 관한 사항";

    [Fact]
    public void 항_호_목_세목마다_줄을_나눈다()
    {
        var lines = ArticleText.ForDisplay(Seoul7).Split('\n').Select(l => l.TrimEnd()).ToList();

        Assert.StartsWith("제7조(기능 및 절차 등)", lines[0]);
        Assert.StartsWith("① 영 제5조의5제1항", lines[1]);
        Assert.Contains(lines, l => l.StartsWith("  1. 시 위원회 심의사항"));
        Assert.Contains(lines, l => l.StartsWith("    가. 「서울특별시 건축 조례」"));
        Assert.Contains(lines, l => l.StartsWith("    나. 법 제5조에 따른"));
        Assert.Contains(lines, l => l.StartsWith("    다. 영 제5조의5제1항제4호"));
        Assert.Contains(lines, l => l.StartsWith("      1) 연면적의 합계가"));
        Assert.Contains(lines, l => l.StartsWith("      3) 삭제"));
        Assert.Contains(lines, l => l.StartsWith("  2. 구 위원회 심의사항"));
        // 2호 아래 목은 다시 가.부터
        Assert.Equal("    가. 법 제46조제2항에 따른 건축선의 지정에 관한 사항", lines[^1]);
    }

    [Fact]
    public void 날짜와_문장끝은_자르지_않는다()
    {
        var text = ArticleText.ForDisplay("① 이 조례는 2026.1.5. 부터 시행한다. 다만, 다음은 예외로 한다.1. 첫째");
        Assert.Contains("2026.1.5. 부터 시행한다. 다만", text);   // "1." · "다." 로 자르지 않음
        Assert.Contains("\n  1. 첫째", text);
    }

    [Fact]
    public void 이미_줄바꿈된_법령_본문은_줄을_더_늘리지_않는다()
    {
        const string law = "제5조의5(지방건축위원회)\n① 다음 각 호의 사항에 대한 심의등을 한다.\n1. 건축선의 지정에 관한 사항\n2. 조례의 제정";
        var text = ArticleText.ForDisplay(law);
        Assert.DoesNotContain("\n\n", text);
        Assert.Contains("\n  1. 건축선의 지정", text);
    }
}
