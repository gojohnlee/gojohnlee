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
    public void VoicePerPersonality()
    {
        Assert.Contains("히히", ClaudePrompt.Voice(ValheimHelper.Core.Npc.Personality.Floki));
        Assert.Contains("~지", ClaudePrompt.Voice(ValheimHelper.Core.Npc.Personality.Ragnar));
        Assert.Contains("말투로", ClaudePrompt.System(new string[0], new string[0], "a"));
    }

    [Fact]
    public void RecentDialogueAndDefaultAmount()
    {
        string s = ClaudePrompt.System(new string[0], new string[0], "a", new[] { "a: 나무 캐", "플로키: 몇 개?" });
        Assert.Contains("최근 대화(오래된 것부터):\n- a: 나무 캐\n- 플로키: 몇 개?", s);
        Assert.Contains(ClaudePrompt.DefaultAmount + "개로 바로 시작", s);
        Assert.DoesNotContain("최근 대화(", ClaudePrompt.System(new string[0], new string[0], "a", new string[0]));
    }

    [Fact]
    public void EmptyStockSaysSo()
    {
        Assert.Contains("(비어 있음)", ClaudePrompt.System(new string[0], new string[0], "a"));
    }
}
