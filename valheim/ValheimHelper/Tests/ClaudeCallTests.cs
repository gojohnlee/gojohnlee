using ValheimHelper.Core.Chat;
using Xunit;

public class ClaudeCallTests
{
    [Fact]
    public void BangUsesHaikuElseSonnet()
    {
        Assert.Equal("haiku", ClaudeCall.Model(true));
        Assert.Equal("sonnet", ClaudeCall.Model(false));
    }

    [Fact]
    public void ArgsTurnEverythingOff()
    {
        var a = ClaudeCall.Args("haiku", "sys");
        Assert.Equal("", a[a.IndexOf("--tools") + 1]);
        Assert.Contains("--no-session-persistence", a);
        Assert.Contains("--disable-slash-commands", a);
        Assert.Equal("", a[a.IndexOf("--setting-sources") + 1]);
    }

    [Fact]
    public void ExtractsResult()
    {
        Assert.Equal("{\"a\":1}", ClaudeCall.ExtractResult("{\"is_error\":false,\"result\":\"{\\\"a\\\":1}\"}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"is_error\":true,\"result\":\"limit\"}")]
    [InlineData("{\"result\":\"  \"}")]
    [InlineData("{\"x\":1}")]
    public void BadOutputIsNull(string s) => Assert.Null(ClaudeCall.ExtractResult(s));

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a b\\", "\"a b\\\\\"")]
    public void QuotesForWindows(string a, string q) => Assert.Equal(q, ClaudeCall.Quote(a));
}
