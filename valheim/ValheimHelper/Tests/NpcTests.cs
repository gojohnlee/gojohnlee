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
            string line = s.Say(Personality.Scv, "escort");
            Assert.NotEqual(prev, line);
            prev = line;
        }
    }

    [Fact]
    public void UsesEveryOption()
    {
        var s = new Speech(new Random(2));
        var seen = new HashSet<string>();
        for (int i = 0; i < 200; i++) seen.Add(s.Say(Personality.Scv, "escort"));
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void FillsArguments()
    {
        var s = new Speech();
        string line = s.Say(Personality.Alarak, "status", new Dictionary<string, string> { ["state"] = "노는 중", ["hp"] = "25", ["max"] = "25" });
        Assert.Equal("노는 중! 체력 25/25, 히히.", line);
    }

    [Fact]
    public void UnknownKeyReturnsKey() => Assert.Equal("nope", new Speech().Say(Personality.Scv, "nope"));

    [Fact]
    public void BothPersonalitiesHaveSameKeys()
    {
        foreach (var key in new[] { "spawn", "escort", "escort.far", "raid", "stop", "home", "moveBase", "lost", "status", "notYet", "onlyBang", "state.idle", "state.follow", "state.home", "state.work", "gatherStart", "gatherKnown", "gatherDone", "dropJob", "searching", "noTool", "toolTooWeak", "toolBroke", "notFound", "noChest", "chestFull", "chestBusy", "chestAny", "bagFull" })
        {
            Assert.True(Speech.Has(Personality.Scv, key), key);
            Assert.True(Speech.Has(Personality.Alarak, key), key);
        }
    }
}

public class DispatchTests
{
    static List<NpcInfo> Npcs(bool claudeBusy = false, bool alarakBusy = false) => new List<NpcInfo>
    {
        new NpcInfo { Name = "Claude", Personality = Personality.Scv, Busy = claudeBusy },
        new NpcInfo { Name = "Alarak", Personality = Personality.Alarak, Busy = alarakBusy },
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
