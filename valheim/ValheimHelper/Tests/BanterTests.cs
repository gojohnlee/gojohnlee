using System;
using System.Collections.Generic;
using System.Linq;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;
using Xunit;

public class BanterTests
{
    static readonly string[] Names = { "라그나", "플로키" };

    static BanterFacts Full()
    {
        var f = new BanterFacts
        {
            DeathFoe = "트롤", DeathDir = "북동쪽", Deaths = 3, FledFoe = "트롤", BeatenFoe = "회색난쟁이",
            Biome = "늪", BiomeDir = "남쪽", Item = "구리", ItemDir = "서쪽", Crop = "당근", PitDir = "동쪽", Night = true,
        };
        f.Skill[0] = "벌목"; f.Level[0] = 22; f.Skill[1] = "채굴"; f.Level[1] = 15;
        return f;
    }

    static string Render(BanterLine l) => new Speech(new Random(1)).Say((Personality)l.Npc, l.Key, l.Args);

    [Fact]
    public void EveryBanterKeyRendersForBoth()
    {
        var keys = new List<string>();
        foreach (var t in Banter.MemoryTopics.Concat(Banter.AnyTopics)) { keys.Add("bt." + t + ".open"); keys.Add("bt." + t + ".reply"); }
        foreach (BanterEvent e in Enum.GetValues(typeof(BanterEvent)))
            foreach (var part in new[] { ".react", ".self", ".news" }) keys.Add(Banter.EventKey(e) + part);
        keys.Add("bt.solo");
        var args = new Dictionary<string, string>
        {
            ["partner"] = "플로키", ["who"] = "플로키", ["foe"] = "트롤", ["dir"] = "북쪽", ["deaths"] = "2", ["biome"] = "늪", ["item"] = "구리", ["n"] = "30",
            ["skill"] = "벌목", ["level"] = "12", ["pskill"] = "채굴", ["plevel"] = "20", ["crop"] = "당근",
        };
        foreach (var p in new[] { Personality.Ragnar, Personality.Floki })
            foreach (var k in keys)
            {
                Assert.True(Speech.Has(p, k), p + " " + k);
                for (int i = 0; i < 12; i++)
                {
                    string line = new Speech(new Random(i)).Say(p, k, args);
                    Assert.DoesNotContain("{", line);
                    Assert.DoesNotContain("쪽쪽", line);
                }
            }
    }

    [Fact]
    public void ChatsAboutEveryTwoMinutesOnlyWhenTogether()
    {
        var b = new Banter(new Random(3));
        Assert.True(b.Due(0));
        Assert.Empty(b.Tick(0, true, -1, Full(), Names));
        Assert.False(b.Due(Banter.IntervalMin - 1));
        Assert.Empty(b.Tick(Banter.IntervalMin - 1, true, -1, Full(), Names));
        Assert.Empty(b.Tick(Banter.IntervalMax + 1, false, -1, Full(), Names));
        var lines = b.Tick(Banter.IntervalMax + 2, true, -1, Full(), Names);
        Assert.Equal(2, lines.Count);
        Assert.NotEqual(lines[0].Npc, lines[1].Npc);
        Assert.EndsWith(".open", lines[0].Key);
        Assert.EndsWith(".reply", lines[1].Key);
        Assert.True(lines[1].Delay > 0);
        foreach (var l in lines) Assert.DoesNotContain("{", Render(l));
        Assert.Empty(b.Tick(Banter.IntervalMax + 3, true, -1, Full(), Names));
    }

    [Fact]
    public void AloneMuses()
    {
        var b = new Banter(new Random(4));
        b.Tick(0, false, 1, null, Names);
        var lines = b.Tick(Banter.IntervalMax + 1, false, 1, null, Names);
        Assert.Single(lines);
        Assert.Equal(1, lines[0].Npc);
        Assert.Equal("bt.solo", lines[0].Key);
        Assert.Equal("라그나", lines[0].Args["partner"]);
    }

    [Fact]
    public void NoMemoryMeansOnlyAnyTopicsAndNoRepeatsInARow()
    {
        var b = new Banter(new Random(5));
        double now = 0;
        b.Tick(now, true, -1, new BanterFacts(), Names);
        var seen = new List<string>();
        for (int i = 0; i < 20; i++)
        {
            now += Banter.IntervalMax + 1;
            var lines = b.Tick(now, true, -1, new BanterFacts(), Names);
            string topic = lines[0].Key.Split('.')[1];
            Assert.Contains(topic, Banter.AnyTopics);
            seen.Add(topic);
        }
        for (int i = 1; i < seen.Count; i++) Assert.NotEqual(seen[i - 1], seen[i]);
    }

    [Fact]
    public void MemoryTopicsNeedTheirFacts()
    {
        var f = new BanterFacts();
        Assert.False(Banter.Has("death", f));
        Assert.False(Banter.Has("skill", f));
        Assert.True(Banter.Has("gods", f));
        Assert.True(Banter.Has("death", Full()));
        Assert.True(Banter.Has("skill", Full()));
    }

    [Fact]
    public void EventWithPartnerNearReactsAtOnceThenCoolsDown()
    {
        var b = new Banter(new Random(6));
        var lines = b.OnEvent(10, 0, BanterEvent.Died, null, true, Names);
        Assert.Equal(2, lines.Count);
        Assert.Equal(1, lines[0].Npc);
        Assert.Equal("bt.ev.died.react", lines[0].Key);
        Assert.Equal("라그나", lines[0].Args["who"]);
        Assert.Equal(0, lines[1].Npc);
        Assert.Equal("bt.ev.died.self", lines[1].Key);
        Assert.Equal("플로키", lines[1].Args["partner"]);
        Assert.Empty(b.OnEvent(10 + Banter.EventCooldown - 1, 1, BanterEvent.Died, null, true, Names));
        Assert.NotEmpty(b.OnEvent(10 + Banter.EventCooldown + 1, 1, BanterEvent.Died, null, true, Names));
    }

    [Fact]
    public void EventAwayBecomesNewsForNextChat()
    {
        var b = new Banter(new Random(7));
        b.Tick(0, true, -1, Full(), Names);
        Assert.Empty(b.OnEvent(5, 1, BanterEvent.Haul, new Dictionary<string, string> { ["item"] = "나무", ["n"] = "50" }, false, Names));
        var lines = b.Tick(Banter.IntervalMax + 1, true, -1, Full(), Names);
        Assert.Equal("bt.ev.haul.news", lines[0].Key);
        Assert.Equal(0, lines[0].Npc);
        Assert.Equal("플로키", lines[0].Args["who"]);
        Assert.Equal("50", lines[0].Args["n"]);
        Assert.Equal("bt.ev.haul.self", lines[1].Key);
        foreach (var l in lines) Assert.DoesNotContain("{", Render(l));
        var next = b.Tick(2 * Banter.IntervalMax + 2, true, -1, Full(), Names);
        Assert.EndsWith(".open", next[0].Key);
    }

    [Fact]
    public void OldNewsIsDropped()
    {
        var b = new Banter(new Random(8));
        b.Tick(0, true, -1, Full(), Names);
        b.OnEvent(1, 0, BanterEvent.Fled, new Dictionary<string, string> { ["foe"] = "트롤" }, false, Names);
        var lines = b.Tick(Banter.NewsKeep + 10, true, -1, Full(), Names);
        Assert.EndsWith(".open", lines[0].Key);
    }

    [Fact]
    public void FactsFromPlaces()
    {
        var a = new PlaceMemory { Zone = new ZoneXY(2, 2), Deaths = 1, Killer = "Troll", Wins = 5 };
        a.Group.Add(new GroupFoe { Kind = "Greydwarf", Count = 3 });
        var c = new PlaceMemory { Zone = new ZoneXY(0, -3), Deaths = 4, Killer = "Troll", Flees = 2, Features = PlaceFeature.Pit };
        c.Group.Add(new GroupFoe { Kind = "Troll", Count = 1 });
        var f = new BanterFacts();
        f.FromPlaces(new[] { a, c }, new ZoneXY(0, 0), k => k == "Troll" ? "트롤" : "회색난쟁이");
        Assert.Equal("트롤", f.DeathFoe);
        Assert.Equal("남쪽", f.DeathDir);
        Assert.Equal(4, f.Deaths);
        Assert.Equal("트롤", f.FledFoe);
        Assert.Equal("회색난쟁이", f.BeatenFoe);
        Assert.Equal("남쪽", f.PitDir);
    }
}
