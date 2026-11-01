using System.Text.Json;
using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 법령해석례(참고 자료) 처리 검증.
///
/// 해석례는 **조문이 아니다.** 검토서에 인용되는 조문은 법제처 현행 원문만이라는 원칙(2절 1번)이
/// 있으므로, 해석례는 "사람이 더 확인할 거리"로만 싣고 AI 판정 근거로는 넘기지 않는다.
/// 픽스처는 "대지 안의 공지" 검색 실응답(2026-11 캡처).
/// </summary>
public class InterpretationTests
{
    private static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(
        ChecklistTests.FindRepoFile(Path.Combine(
            "tests", "LawReview.Core.Tests", "Fixtures", "search_expc.json"))));

    [Fact]
    public void 해석례_검색_응답을_파싱한다()
    {
        using var doc = Fixture();
        var hits = MolegClient.ParseInterpretations(doc.RootElement);

        Assert.Equal(3, hits.Count);
        Assert.All(hits, h => Assert.Contains("대지 안의 공지", h.Title));
        Assert.All(hits, h => Assert.Equal("법제처", h.Organ));
        // 검토서는 협력사로 나간다 — 응답의 상세링크에 박힌 OC 키가 실리면 안 된다.
        Assert.All(hits, h => Assert.StartsWith("https://www.law.go.kr/expcInfoP.do?expcSeq=", h.Link));
        Assert.All(hits, h => Assert.DoesNotContain("OC=", h.Link));
        Assert.Contains(hits, h => h.ReplyDate.Length > 0);
    }

    [Theory]
    // 괄호 안이 더 구체적이면 그쪽을 검색어로 쓴다.
    [InlineData("건축물의 높이 (일조 등의 확보)", "일조 등의 확보")]
    [InlineData("장애물 없는 생활환경(BF) 인증", "장애물 없는 생활환경")]   // 괄호가 짧으면 앞부분
    [InlineData("대지 안의 공지", "대지 안의 공지")]
    [InlineData("부설주차장", "부설주차장")]
    public void 항목명을_해석례_검색어로_다듬는다(string title, string expected)
        => Assert.Equal(expected, ReviewEngine.SearchableTitle(title));

    [Fact]
    public void 참고_자료는_조문_인용과_이름으로_구분된다()
    {
        // ReviewEngine이 AI에게 넘길 인용을 고를 때 이 이름으로 걸러낸다.
        Assert.Equal("법령해석례 (참고)", ReviewEngine.ReferenceLawName);
    }
}
