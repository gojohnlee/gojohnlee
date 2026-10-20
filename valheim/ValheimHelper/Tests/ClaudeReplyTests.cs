using System.Collections.Generic;
using ValheimHelper.Core.Chat;
using Xunit;

public class ClaudeReplyTests
{
    static readonly string[] Npcs = { "Claude", "Alarak" };
    static string Resolve(string s) => s == "사슴 스튜" ? "DeerStew" : s == "Honey" || s == "꿀" ? "Honey" : null;

    static ClaudeResult P(string json) => ClaudeReply.Parse(json, Resolve, Npcs);

    [Fact]
    public void ParsesTasksAndReply()
    {
        var r = P("{\"reply\":\"목재 50, 갑니다!\",\"tasks\":[{\"kind\":\"item\",\"item\":\"Wood\",\"amount\":50,\"npc\":\"claude\",\"urgent\":true}]}");
        Assert.False(r.Invalid);
        Assert.Equal("목재 50, 갑니다!", r.Reply);
        var c = Assert.Single(r.Tasks).Command;
        Assert.Equal(CommandKind.Item, c.Kind);
        Assert.Equal("Wood", c.Item);
        Assert.Equal(50, c.Amount);
        Assert.Equal("Claude", c.Target);
        Assert.True(c.Urgent);
    }

    [Fact]
    public void ToleratesProseAroundJson()
    {
        var r = P("Here you go:\n```json\n{\"chatter\":false,\"reply\":\"네\",\"tasks\":[]}\n```");
        Assert.False(r.Invalid);
        Assert.Equal("네", r.Reply);
    }

    [Fact]
    public void ChatterIsSilent()
    {
        var r = P("{\"chatter\":true,\"reply\":\"ㅋㅋ\",\"tasks\":[{\"kind\":\"stop\"}]}");
        Assert.True(r.Chatter);
        Assert.Null(r.Reply);
        Assert.Empty(r.Tasks);
    }

    [Fact]
    public void RejectsUnknownAndCapsAmount()
    {
        var r = P("{\"tasks\":[{\"kind\":\"build\",\"item\":\"Wood\"},{\"kind\":\"item\",\"item\":\"Unobtanium\",\"amount\":5}," +
                  "{\"kind\":\"item\",\"item\":\"Wood\",\"amount\":5000},{\"kind\":\"item\",\"item\":\"Wood\",\"amount\":2.5},{\"kind\":\"escort\",\"npc\":\"Bob\"}]}");
        var t = Assert.Single(r.Tasks);
        Assert.Equal(999, t.Command.Amount);
        Assert.True(t.Command.Clamped);
        Assert.Equal(4, r.Rejected.Count);
    }

    [Fact]
    public void LimitsTaskCountAndReplyLength()
    {
        var tasks = new List<string>();
        for (int i = 0; i < 8; i++) tasks.Add("{\"kind\":\"status\"}");
        var r = P("{\"reply\":\"" + new string('가', 300) + "\\n<color=red>x</color>\",\"tasks\":[" + string.Join(",", tasks) + "]}");
        Assert.Equal(ClaudeReply.MaxTasks, r.Tasks.Count);
        Assert.True(r.Reply.Length <= ClaudeReply.MaxReply + 1);
        Assert.DoesNotContain("<", r.Reply);
        Assert.DoesNotContain("\n", r.Reply);
    }

    [Fact]
    public void SetSaveAndEdit()
    {
        var r = P("{\"tasks\":[{\"kind\":\"setsave\",\"name\":\"늪 세트\",\"items\":[\"사슴 스튜\",\"꿀\"]},{\"kind\":\"setedit\",\"number\":1,\"remove\":[\"꿀\"],\"add\":[\"사슴 스튜\"]}]}");
        Assert.Equal(2, r.Tasks.Count);
        Assert.Equal(CommandKind.SetSave, r.Tasks[0].Command.Kind);
        Assert.Equal(0, r.Tasks[0].Command.Number);
        Assert.Equal(new[] { "DeerStew", "Honey" }, r.Tasks[0].Items);
        Assert.Equal("늪 세트", r.Tasks[0].Name);
        Assert.Equal(new[] { "Honey" }, r.Tasks[1].Remove);
        var bad = P("{\"tasks\":[{\"kind\":\"setsave\",\"items\":[\"돌멩이빵\"]},{\"kind\":\"setmake\",\"number\":11,\"amount\":2}]}");
        Assert.Empty(bad.Tasks);
    }

    [Fact]
    public void ExploreArgs()
    {
        var r = P("{\"tasks\":[{\"kind\":\"explore\",\"arg\":\"동쪽\"},{\"kind\":\"explore\",\"arg\":\"검은숲\"},{\"kind\":\"explore\",\"arg\":\"달\"}]}");
        Assert.Equal(2, r.Tasks.Count);
        Assert.Equal("east", r.Tasks[0].Command.Arg);
        Assert.NotNull(r.Tasks[1].Command.Biome);
        Assert.Single(r.Rejected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"reply\": }")]
    [InlineData("{\"a\":[1,2}")]
    public void InvalidJson(string s) => Assert.True(P(s).Invalid);

    [Fact]
    public void RateLimiterSixPerMinute()
    {
        var l = new RateLimiter(6, 60);
        for (int i = 0; i < 6; i++) Assert.True(l.TryTake(i));
        Assert.False(l.TryTake(10));
        Assert.Equal(60, l.NextFree(10));
        Assert.True(l.TryTake(60));
    }

    [Fact]
    public void MiniJsonBasics()
    {
        var o = (Dictionary<string, object>)MiniJson.Parse("{\"a\":[1,-2.5e1,\"x\\u0041\\\"\",true,null,{}]}");
        var a = (List<object>)o["a"];
        Assert.Equal(1.0, a[0]);
        Assert.Equal(-25.0, a[1]);
        Assert.Equal("xA\"", a[2]);
        Assert.Equal(true, a[3]);
        Assert.Null(a[4]);
    }
}
