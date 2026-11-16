using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 법령 개정 감시 검증.
///
/// 검토서는 작성 후 몇 달씩 쓰이는데 그 사이 법이 바뀌면 인용 조문이 구법이 된다
/// — 검토서가 통째로 틀린 것이 되므로(원칙 1) 불러올 때 시행일자를 다시 맞춰 본다.
/// **못 봤다는 것과 안 바뀌었다는 것은 다르다** — 조회 실패를 "변경 없음"으로 삼키면
/// 구법에 근거한 검토서를 멀쩡한 것으로 오인한다.
/// </summary>
public class LawAmendmentWatcherTests
{
    [Theory]
    [InlineData("2026.07.28", "20260728")]
    [InlineData("2026-07-28", "20260728")]
    [InlineData("20260728", "20260728")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 시행일자_표기를_맞춰_비교한다(string? raw, string expected)
        => Assert.Equal(expected, LawAmendmentWatcher.Normalize(raw));

    [Fact]
    public void 시행일자가_다르면_개정이다()
    {
        var a = new LawAmendment("건축법 시행령", "20260301", "20260728");
        Assert.True(a.Changed);
        Assert.False(a.Unchecked);
    }

    [Fact]
    public void 시행일자가_같으면_개정이_아니다()
        => Assert.False(new LawAmendment("건축법", "20260728", "20260728").Changed);

    [Fact]
    public void 조회에_실패하면_개정이라고_단정하지_않는다()
    {
        var a = new LawAmendment("○○시 건축 조례", "20260301", "");
        Assert.False(a.Changed);
        Assert.True(a.Unchecked);   // 확인하지 못했다는 사실은 따로 남는다
    }

    [Fact]
    public void 검토_당시_날짜가_없으면_비교하지_않는다()
    {
        // 조회에 실패해 시행일자 없이 인용된 법령이 있다 — 이걸 개정으로 보면 거짓 경보다.
        Assert.False(new LawAmendment("자치법규", "", "20260728").Changed);
    }
}
