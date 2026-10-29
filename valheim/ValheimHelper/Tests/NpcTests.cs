using System;
using System.Collections.Generic;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using Xunit;

public class SpeechTests
{
    [Fact]
    public void NeverRepeatsLastLine()
    {
        var s = new Speech(new Random(1));
        string prev = null;
        for (int i = 0; i < 200; i++)
        {
            string line = s.Say(Personality.Ragnar, "escort");
            Assert.NotEqual(prev, line);
            prev = line;
        }
    }

    [Fact]
    public void UsesEveryOption()
    {
        var s = new Speech(new Random(2));
        var seen = new HashSet<string>();
        for (int i = 0; i < 200; i++) seen.Add(s.Say(Personality.Ragnar, "escort"));
        Assert.Equal(15, seen.Count);
    }

    [Fact]
    public void FillsArguments()
    {
        var s = new Speech();
        string line = s.Say(Personality.Floki, "status", new Dictionary<string, string> { ["state"] = "노는 중", ["hp"] = "25", ["max"] = "25" });
        Assert.Equal("노는 중! 체력 25/25, 히히.", line);
    }

    [Fact]
    public void DealsEveryLineBeforeRepeating()
    {
        var s = new Speech(new Random(3));
        int n = Speech.Options(Personality.Floki, "bt.solo").Length;
        string prev = null;
        for (int round = 0; round < 5; round++)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                string line = s.Say(Personality.Floki, "bt.solo", new Dictionary<string, string> { ["partner"] = "R" });
                Assert.NotEqual(prev, line);
                seen.Add(line);
                prev = line;
            }
            Assert.Equal(n, seen.Count);
        }
    }

    [Fact]
    public void UnknownKeyReturnsKey() => Assert.Equal("nope", new Speech().Say(Personality.Ragnar, "nope"));

    [Fact]
    public void WorkLinesHaveThreeWays()
    {
        // 대사 늘리기 가)(회차 256): 자주 듣는 일 대사는 셋 이상으로 돌려 말한다
        var keys = new[] { "bagFull", "craft.missingGather", "craft.missingDone", "setEmpty", "prepOnce", "prepKeep", "prepOff", "prepNoChest", "prepShort",
            "craft.farm", "farmNoField", "preyWaitGuard", "depart.noCrypt", "escort.noMate" };
        foreach (var p in new[] { Personality.Ragnar, Personality.Floki })
            foreach (var k in keys) Assert.True(Speech.Options(p, k).Length >= 3, p + " " + k);
    }

    [Fact]
    public void BothPersonalitiesHaveSameKeys()
    {
        foreach (var key in new[] { "spawn", "escort", "escort.far", "raid", "stop", "home", "moveBase", "lost", "status", "notYet", "onlyBang", "state.idle", "state.follow", "state.home", "state.work", "gatherStart", "gatherKnown", "gatherDone", "dropJob", "searching", "noTool", "toolTooWeak", "toolBroke", "notFound", "noChest", "chestFull", "chestBusy", "chestAny", "bagFull" })
        {
            Assert.True(Speech.Has(Personality.Ragnar, key), key);
            Assert.True(Speech.Has(Personality.Floki, key), key);
        }
    }
}

public class DispatchTests
{
    static List<NpcInfo> Npcs(bool claudeBusy = false, bool alarakBusy = false) => new List<NpcInfo>
    {
        new NpcInfo { Name = "Claude", Personality = Personality.Ragnar, Busy = claudeBusy },
        new NpcInfo { Name = "Alarak", Personality = Personality.Floki, Busy = alarakBusy },
    };

    [Fact]
    public void NamedTarget() => Assert.Equal(new[] { 1 }, Dispatch.Choose(CommandKind.Escort, "alarak", Npcs()));

    [Fact]
    public void UnknownTarget() => Assert.Empty(Dispatch.Choose(CommandKind.Escort, "Bob", Npcs()));

    [Theory]
    [InlineData(CommandKind.Status)]
    [InlineData(CommandKind.Stop)]
    public void Everyone(CommandKind kind) => Assert.Equal(new[] { 0, 1 }, Dispatch.Choose(kind, null, Npcs(true, true)));

    [Fact]
    public void PrefersScvWhenBothIdle() => Assert.Equal(new[] { 0 }, Dispatch.Choose(CommandKind.Escort, null, Npcs()));

    [Fact]
    public void PrefersIdle() => Assert.Equal(new[] { 1 }, Dispatch.Choose(CommandKind.Escort, null, Npcs(claudeBusy: true)));

    [Fact]
    public void NoNpcs() => Assert.Empty(Dispatch.Choose(CommandKind.Escort, null, new List<NpcInfo>()));
}
