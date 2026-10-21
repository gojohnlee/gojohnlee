using ValheimHelper.Core.Chat;
using Xunit;

public class ClaudePromptTests
{
    [Fact]
    public void IncludesNpcsStockAndPlayer()
    {
        string s = ClaudePrompt.System(new[] { "Claude: 놀고 있음" }, new[] { "나무 50" }, "te4st41");
        Assert.Contains("te4st41", s);
        Assert.Contains("- Claude: 놀고 있음", s);
        Assert.Contains("- 나무 50", s);
        Assert.Contains("setsave", s);
    }

    [Fact]
    public void EmptyStockSaysSo()
    {
        Assert.Contains("(비어 있음)", ClaudePrompt.System(new string[0], new string[0], "a"));
    }
}
