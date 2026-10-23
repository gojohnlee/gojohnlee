using System.Collections.Generic;
using ValheimHelper.Core.Chat;
using Xunit;

public class ChatParserTests
{
    static readonly Dictionary<string, string> Names = new Dictionary<string, string>
    {
        ["Claude"] = "Claude", ["클로드"] = "Claude",
        ["Alarak"] = "Alarak", ["알라라크"] = "Alarak",
    };

    static string GameItems(string name) => name switch
    {
        "구리주괴" => "Copper",
        "청동곡괭이" => "PickaxeBronze",
        "bronze pickaxe" => "PickaxeBronze",
        _ => null,
    };

    static readonly ChatParser P = new ChatParser(Names, GameItems);

    static ChatCommand Cmd(string line)
    {
        var r = P.Parse(line);
        Assert.Equal(ChatRoute.Command, r.Route);
        return r.Command;
    }

    [Theory]
    [InlineData("!나무 50", "Wood", 50)]
    [InlineData("!목재 50", "Wood", 50)]
    [InlineData("!wood 50", "Wood", 50)]
    [InlineData("!Wood 50개", "Wood", 50)]
    [InlineData("!구리 주괴 4", "Copper", 4)]
    [InlineData("!청동주괴 5", "Bronze", 5)]
    [InlineData("!주석 주괴 3", "Tin", 3)]
    [InlineData("!고급목재 20", "FineWood", 20)]
    [InlineData("!고급 목재 20", "FineWood", 20)]
    [InlineData("!코어목재 20", "RoundLog", 20)]
    [InlineData("!돌 30", "Stone", 30)]
    [InlineData("!부싯돌 10", "Flint", 10)]
    [InlineData("!구리 20", "CopperOre", 20)]
    [InlineData("!은 5", "SilverOre", 5)]
    [InlineData("!은 주괴 3", "Silver", 3)]
    [InlineData("!주석 20", "TinOre", 20)]
    [InlineData("!철 30", "IronScrap", 30)]
    [InlineData("！철 30", "IronScrap", 30)]
    [InlineData("!구리주괴 20", "Copper", 20)]
    [InlineData("!청동 곡괭이 1", "PickaxeBronze", 1)]
    [InlineData("!bronze pickaxe 1", "PickaxeBronze", 1)]
    public void Item(string line, string prefab, int amount)
    {
        var c = Cmd(line);
        Assert.Equal(CommandKind.Item, c.Kind);
        Assert.Equal(prefab, c.Item);
        Assert.Equal(amount, c.Amount);
        Assert.Null(c.Target);
        Assert.False(c.Clamped);
    }

    [Fact]
    public void AmountIsClamped()
    {
        var c = Cmd("!나무 5000");
        Assert.Equal(999, c.Amount);
        Assert.True(c.Clamped);
    }

    [Theory]
    [InlineData("!목재 50개 내놔")]
    [InlineData("!철 좀 넉넉히")]
    [InlineData("!나무 0")]
    [InlineData("!나무 -5")]
    [InlineData("!나무")]
    [InlineData("!모르는물건 3")]
    [InlineData("!상태 보여줘")]
    [InlineData("!탐험 저기")]
    [InlineData("!세트 11 20")]
    [InlineData("!밭 크게")]
    [InlineData("!Claude")]
    public void MalformedBangGoesToHaiku(string line)
    {
        var r = P.Parse(line);
        Assert.Equal(ChatRoute.Haiku, r.Route);
        Assert.Equal(line, r.Text);
    }

    [Fact]
    public void HaikuKeepsTarget()
    {
        var r = P.Parse("!Alarak 철 좀 넉넉히");
        Assert.Equal(ChatRoute.Haiku, r.Route);
        Assert.Equal("Alarak", r.Target);
    }

    [Theory]
    [InlineData("!상태", CommandKind.Status)]
    [InlineData("!status", CommandKind.Status)]
    [InlineData("!스킬", CommandKind.Skills)]
    [InlineData("!기억", CommandKind.Memory)]
    [InlineData("!지식", CommandKind.Knowledge)]
    [InlineData("!같이 가자", CommandKind.Escort)]
    [InlineData("!같이가자", CommandKind.Escort)]
    [InlineData("!그냥 가", CommandKind.GoAnyway)]
    [InlineData("!Go Anyway", CommandKind.GoAnyway)]
    [InlineData("!멈춰", CommandKind.Stop)]
    [InlineData("!취소 전부", CommandKind.ClearQueue)]
    [InlineData("!길", CommandKind.Path)]
    [InlineData("!세트", CommandKind.SetList)]
    [InlineData("!채워 끔", CommandKind.KeepOff)]
    [InlineData("!탐험", CommandKind.Explore)]
    public void Simple(string line, CommandKind kind) => Assert.Equal(kind, Cmd(line).Kind);

    [Theory]
    [InlineData("!Alarak 철 30", "Alarak")]
    [InlineData("!alarak 철 30", "Alarak")]
    [InlineData("!알라라크 철 30", "Alarak")]
    [InlineData("!클로드 철 30", "Claude")]
    public void NamedTarget(string line, string target)
    {
        var c = Cmd(line);
        Assert.Equal(target, c.Target);
        Assert.Equal("IronScrap", c.Item);
    }

    [Fact]
    public void NamedSimpleCommand()
    {
        var c = Cmd("!Claude 멈춰");
        Assert.Equal(CommandKind.Stop, c.Kind);
        Assert.Equal("Claude", c.Target);
    }

    [Theory]
    [InlineData("!탐험 동쪽", "east", null)]
    [InlineData("!탐험 북동", "northeast", null)]
    [InlineData("!explore West", "west", null)]
    [InlineData("!탐험 늪", null, "Swamp")]
    [InlineData("!탐험 검은 숲", null, "BlackForest")]
    [InlineData("!explore black forest", null, "BlackForest")]
    [InlineData("!탐험 딥노스", null, "DeepNorth")]
    public void Explore(string line, string dir, string biome)
    {
        var c = Cmd(line);
        Assert.Equal(CommandKind.Explore, c.Kind);
        Assert.Equal(dir, c.Arg);
        Assert.Equal(biome, c.Biome);
    }

    [Theory]
    [InlineData("!상자 장비 Claude", "equipment", "Claude", null)]
    [InlineData("!상자 장비 alarak", "equipment", "Alarak", null)]
    [InlineData("!상자 장비", "equipment", null, null)]
    [InlineData("!상자 음식", "food", null, null)]
    [InlineData("!상자 씨앗", "seeds", null, null)]
    [InlineData("!상자 나무", "item", null, "Wood")]
    [InlineData("!chest copper", "item", null, "CopperOre")]
    [InlineData("!상자 구리주괴", "item", null, "Copper")]
    public void Chest(string line, string arg, string owner, string item)
    {
        var c = Cmd(line);
        Assert.Equal(CommandKind.SetChest, c.Kind);
        Assert.Equal(arg, c.Arg);
        Assert.Equal(owner, c.Target);
        Assert.Equal(item, c.Item);
    }

    [Theory]
    [InlineData("!상자")]
    [InlineData("!상자 나무 Claude")] // 자원 상자는 같이 쓴다
    [InlineData("!상자 모르는것")]
    public void BadChestGoesToHaiku(string line) => Assert.Equal(ChatRoute.Haiku, P.Parse(line).Route);

    [Fact]
    public void Keep()
    {
        var c = Cmd("!채워 나무 200");
        Assert.Equal(CommandKind.Keep, c.Kind);
        Assert.Equal("Wood", c.Item);
        Assert.Equal(200, c.Amount);

        c = Cmd("!채워 구리주괴 20");
        Assert.Equal("Copper", c.Item);

        c = Cmd("!채워 세트 1 10");
        Assert.Equal(CommandKind.KeepSet, c.Kind);
        Assert.Equal(1, c.Number);
        Assert.Equal(10, c.Amount);

        c = Cmd("!keep off wood");
        Assert.Equal(CommandKind.KeepOff, c.Kind);
        Assert.Equal("Wood", c.Item);
    }

    [Fact]
    public void SetMake()
    {
        var c = Cmd("!세트 1 20");
        Assert.Equal(CommandKind.SetMake, c.Kind);
        Assert.Equal(1, c.Number);
        Assert.Equal(20, c.Amount);
        Assert.Equal(10, Cmd("!set 10 5").Number);
    }

    [Theory]
    [InlineData("!밭", CommandKind.Farm, 10, false)]
    [InlineData("!밭 10", CommandKind.Farm, 10, false)]
    [InlineData("!farm 5m", CommandKind.Farm, 5, false)]
    [InlineData("!평탄화 30", CommandKind.Level, 20, true)]
    [InlineData("!level 20", CommandKind.Level, 20, false)]
    public void Radius(string line, CommandKind kind, int r, bool clamped)
    {
        var c = Cmd(line);
        Assert.Equal(kind, c.Kind);
        Assert.Equal(r, c.Number);
        Assert.Equal(clamped, c.Clamped);
    }

    [Theory]
    [InlineData("야 목재 50개 가져와")]
    [InlineData("청동 곡괭이 만들 재료 모아줘")]
    [InlineData("구리는 검은 숲 바위에 있어")]
    public void PlainChatGoesToSonnet(string line)
    {
        var r = P.Parse(line);
        Assert.Equal(ChatRoute.Sonnet, r.Route);
        Assert.Equal(line, r.Text);
        Assert.Null(r.Target);
    }

    [Theory]
    [InlineData("Claude야 나무 좀", "Claude")]
    [InlineData("claude, 나무 좀", "Claude")]
    [InlineData("알라라크 철 좀 캐 와", "Alarak")]
    [InlineData("Claudette 안녕하세요", null)]
    public void SonnetTarget(string line, string target) => Assert.Equal(target, P.Parse(line).Target);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!")]
    [InlineData("!!!")]
    [InlineData("ㅋㅋ")]
    [InlineData("ㅋㅋㅋㅋㅋㅋ")]
    [InlineData("ㅎㅎㅎ")]
    [InlineData("ㅠㅠㅠ")]
    [InlineData("ㅋㅋㅋ!!??")]
    [InlineData("😂😂😂")]
    [InlineData("👍")]
    [InlineData("hahaha")]
    [InlineData("LOL")]
    [InlineData("오케")]
    [InlineData("응")]
    [InlineData("???")]
    public void Chatter(string line) => Assert.Equal(ChatRoute.Ignore, P.Parse(line).Route);

    [Theory]
    [InlineData("응", AnswerKind.Yes)]
    [InlineData("ㅇㅇ", AnswerKind.Yes)]
    [InlineData("가", AnswerKind.Yes)]
    [InlineData("그래!", AnswerKind.Yes)]
    [InlineData("OK", AnswerKind.Yes)]
    [InlineData("아니", AnswerKind.No)]
    [InlineData("됐어", AnswerKind.No)]
    [InlineData("ㄴㄴ", AnswerKind.No)]
    [InlineData("no", AnswerKind.No)]
    public void ShortAnswer(string line, AnswerKind kind)
    {
        var r = P.Parse(line, awaitingAnswer: true);
        Assert.Equal(ChatRoute.Answer, r.Route);
        Assert.Equal(kind, r.Answer);
    }

    [Fact]
    public void LongAnswerGoesToSonnet() =>
        Assert.Equal(ChatRoute.Sonnet, P.Parse("미드 넣었어", awaitingAnswer: true).Route);

    [Fact]
    public void BangCommandWinsOverAnswer() =>
        Assert.Equal(CommandKind.GoAnyway, P.Parse("!그냥 가", awaitingAnswer: true).Command.Kind);
}
