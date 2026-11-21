using Carryover.Core;
using Xunit;

public class PerkStateTests
{
    static int Idx(string id) => PerkState.IndexOf(id);

    [Fact]
    public void Award_Rules()
    {
        Assert.Equal(0, PerkState.Award(179f, 30, 10));    // 3분 안: 0
        Assert.Equal(0, PerkState.Award(599f, 2, 1));      // 10분 안, 능력치 2, 1층
        Assert.Equal(6, PerkState.Award(7200f, 30, 10));   // 상한 6
    }

    [Fact]
    public void Award_Example()
    {
        Assert.Equal(5, PerkState.Award(1800f, 3, 2));   // 30분 3 + 능력치 1 + 층 1
    }

    [Fact]
    public void RunEnd_Twice_AddsOnlyDifference()
    {
        var s = new PerkState();
        s.OnNewRun();
        Assert.Equal(3, s.OnRunEnd(1800f, 0, 0));
        Assert.Equal(1, s.OnRunEnd(2400f, 0, 0));   // 되살아나 더 버팀
        Assert.Equal(0, s.OnRunEnd(2400f, 0, 0));
        Assert.Equal(4, s.Points);
        s.OnNewRun();
        Assert.Equal(3, s.OnRunEnd(1800f, 0, 0));    // 새 회차는 다시 받음
        Assert.Equal(7, s.Points);
    }

    [Fact]
    public void Buy_CostsRise_AndCap()
    {
        var s = new PerkState { Points = 8 };
        int b = Idx("subspace");
        Assert.True(s.Buy(b)); Assert.Equal(6, s.Points);
        Assert.True(s.Buy(b)); Assert.Equal(4, s.Points);
        Assert.True(s.Buy(b)); Assert.Equal(1, s.Points);
        Assert.False(s.Buy(b));                       // 4단계 값 3 > 남은 1
        Assert.Equal(3, s.NextCost(b)); Assert.Equal(1, s.Points);
        Assert.Equal(3, s.SubspaceSlots);
    }

    [Fact]
    public void Buy_NotEnoughPoints_KeepsPoints()
    {
        var s = new PerkState { Points = 1 };
        Assert.False(s.Buy(Idx("xp")));
        Assert.Equal(1, s.Points);
        Assert.Equal(0, s.Level("xp"));
    }

    [Fact]
    public void FiveConfirmedPerks_Only()
    {
        Assert.Equal(new[] { "subspace", "lockpick", "crafting", "traps", "xp", "mining", "fall", "temp" }, System.Array.ConvertAll(PerkState.Perks, p => p.Id));
    }

    [Fact]
    public void MaxedEffects()
    {
        var s = new PerkState { Points = 999 };
        for (int i = 0; i < PerkState.Perks.Length; i++) while (s.Buy(i)) { }
        Assert.Equal(999 - 94, s.Points);             // 전부 94점
        Assert.Equal(0.4f, s.TempDriftMult, 3);
        Assert.Equal(1.75f, s.MiningMult, 3);
        Assert.Equal(0.4f, s.ImpactMult, 3);
        Assert.Equal(10, s.SubspaceSlots);
        Assert.Equal(1.75f, s.LockpickMult, 3);
        Assert.Equal(3, s.CraftIntBonus);
        Assert.Equal(15f, s.TrapRadius);
        Assert.Equal(1.3f, s.XpMult, 3);
    }

    [Fact]
    public void TrapRadius_ByLevel()
    {
        var s = new PerkState();
        int t = Idx("traps");
        Assert.Equal(0f, s.TrapRadius);
        s.Levels[t] = 1; Assert.Equal(6f, s.TrapRadius);
        s.Levels[t] = 2; Assert.Equal(10f, s.TrapRadius);
    }

    [Fact]
    public void NoPerks_NoEffect()
    {
        var s = new PerkState();
        Assert.Equal(0, s.SubspaceSlots); Assert.Equal(1f, s.LockpickMult); Assert.Equal(0, s.CraftIntBonus); Assert.Equal(1f, s.XpMult);
    }

    [Fact]
    public void SerializeRoundTrip_AndClampBadValues()
    {
        var s = new PerkState { Points = 7, RunAward = 2 };
        s.Levels[Idx("lockpick")] = 2;
        var t = PerkState.Parse(s.Serialize());
        Assert.Equal(7, t.Points); Assert.Equal(2, t.RunAward); Assert.Equal(2, t.Level("lockpick"));

        var bad = PerkState.Parse("points=-3\nperk.subspace=99\nperk.bleed=2\nperk.xp=x");   // 옛 퍽 이름은 무시
        Assert.Equal(0, bad.Points);
        Assert.Equal(10, bad.Level("subspace"));
        Assert.Equal(0, bad.Level("xp"));
    }
}
