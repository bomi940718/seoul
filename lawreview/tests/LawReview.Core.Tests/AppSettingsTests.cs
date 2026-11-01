using Xunit;

namespace LawReview.Core.Tests;

/// <summary>모델 기본값을 바꿨을 때 이미 키를 저장한 PC도 새 기본값을 쓰는지 검증.</summary>
public class AppSettingsTests
{
    [Fact]
    public void 옛_기본값으로_저장된_모델은_현재_기본값으로_올린다()
    {
        // 설정 창은 모델 칸까지 늘 저장하므로, 옛 기본값이 들어 있으면 사용자가 고른 값이 아니다.
        var s = AppSettings.Parse("""{"ClaudeApiKey":"k","ClaudeModel":"claude-sonnet-5"}""");

        Assert.Equal(AppSettings.DefaultClaudeModel, s.ClaudeModel);
        Assert.Equal("k", s.ClaudeApiKey);
    }

    [Fact]
    public void 사용자가_고른_다른_모델은_그대로_둔다()
    {
        var s = AppSettings.Parse("""{"ClaudeModel":"claude-opus-5-5"}""");

        Assert.Equal("claude-opus-5-5", s.ClaudeModel);
    }

    [Fact]
    public void 모델이_없거나_파일이_깨졌으면_기본값()
    {
        Assert.Equal(AppSettings.DefaultClaudeModel, AppSettings.Parse("{}").ClaudeModel);
        Assert.Equal(AppSettings.DefaultClaudeModel, AppSettings.Parse("{ 깨진").ClaudeModel);
    }
}
