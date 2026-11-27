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
        Assert.Equal(27, PerkState.Award(7200f, 30, 10));  // 상한 없음: 시간 12 + 능력치 10 + 층 5
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
        Assert.False(s.Buy(Idx("traps")));
        Assert.Equal(1, s.Points);
        Assert.Equal(0, s.Level("traps"));
    }

    [Fact]
    public void FiveConfirmedPerks_Only()
    {
        Assert.Equal(new[] { "subspace", "lockpick", "traps", "mining", "vision", "temp" }, System.Array.ConvertAll(PerkState.Perks, p => p.Id));
    }

    [Fact]
    public void MaxedEffects()
    {
        var s = new PerkState { Points = 999 };
        for (int i = 0; i < PerkState.Perks.Length; i++) while (s.Buy(i)) { }
        Assert.Equal(999 - 199, s.Points);            // 전부 199점
        Assert.Equal(0.2f, s.TempDriftMult, 3);
        Assert.Equal(2.5f, s.MiningMult, 3);
        Assert.Equal(9f, s.VisionRadius);
        Assert.Equal(20, s.SubspaceSlots); Assert.Equal(400f, s.SubspaceMaxWeight);
        Assert.Equal(2.25f, s.LockpickMult, 3);
        Assert.Equal(25f, s.TrapRadius);
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
        Assert.Equal(0, s.SubspaceSlots); Assert.Equal(1f, s.LockpickMult);
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
        Assert.Equal(20, bad.Level("subspace"));
        Assert.Equal(0, bad.Refunded);   // perk.xp=x는 숫자가 아니라 무시
    }

    [Fact]
    public void RemovedPerks_RefundSpentPoints()
    {
        var s = PerkState.Parse("points=3\nperk.crafting=2\nperk.xp=3\nperk.mining=1\nperk.fall=3");   // 숙련 제작 1+2, 경험치 2+3+4, 튼튼한 다리 2+3+4 → 21점 돌려줌
        Assert.Equal(21, s.Refunded); Assert.Equal(24, s.Points); Assert.Equal(1, s.Level("mining"));
        var again = PerkState.Parse(s.Serialize());
        Assert.Equal(0, again.Refunded); Assert.Equal(24, again.Points);
    }

    [Fact]
    public void TempPerk_OnlySlowsDriftAwayFrom37()
    {
        Assert.True(PerkState.DriftAwayFromNormal(36f, 0f));    // 추운 곳에서 더 식음
        Assert.True(PerkState.DriftAwayFromNormal(38f, 45f));   // 더운 곳에서 더 더워짐
        Assert.True(PerkState.DriftAwayFromNormal(37f, 10f));
        Assert.False(PerkState.DriftAwayFromNormal(39f, 20f));  // 운동 뒤 식기(회복)
        Assert.False(PerkState.DriftAwayFromNormal(33f, 50f));  // 불 쬐기(회복)
        Assert.False(PerkState.DriftAwayFromNormal(30f, 30f));
    }
}
