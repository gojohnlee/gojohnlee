using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;

public class StuckBookTests
{
    [Fact]
    public void NoHopExcludesOnFirstFailure()
    {
        var b = new StuckBook();
        Assert.Equal(StuckNext.Exclude, b.WalkFailed(10, 10, false)); // 2) 불가도 실패로 셈
        Assert.True(b.Excluded(12, 13)); // 같은 8m 칸
        Assert.False(b.Excluded(30, 10));
    }

    [Fact]
    public void HopFirstThenExclude()
    {
        var b = new StuckBook();
        Assert.Equal(StuckNext.Hop, b.WalkFailed(0, 0, true));
        Assert.False(b.Excluded(0, 0));
        Assert.Equal(StuckNext.Exclude, b.WalkFailed(0, 0, true)); // 순간이동 뒤 또 막힘 = 두 번째
        Assert.True(b.Excluded(0, 0));
    }

    [Fact]
    public void HopFailedCountsAsFailure()
    {
        var b = new StuckBook();
        Assert.Equal(StuckNext.Hop, b.WalkFailed(0, 0, true));
        Assert.Equal(StuckNext.Exclude, b.HopFailed(0, 0));
        Assert.True(b.Excluded(0, 0));
    }

    [Fact]
    public void GivesUpAfterManyExcluded()
    {
        var b = new StuckBook();
        for (int i = 0; i < StuckBook.GiveUpAt - 1; i++) Assert.Equal(StuckNext.Exclude, b.WalkFailed(i * 20, 0, false));
        Assert.Equal(StuckNext.GiveUp, b.WalkFailed(500, 0, false));
    }

    [Fact]
    public void NegativeCoordsAreSeparateCells()
    {
        var b = new StuckBook();
        b.WalkFailed(-1, -1, false);
        Assert.True(b.Excluded(-7.9, -0.1));
        Assert.False(b.Excluded(1, 1));
    }

    [Fact]
    public void HomeWalkLimitThenTeleport()
    {
        var b = new StuckBook();
        Assert.False(b.HomeWalkFailed());
        Assert.True(b.HomeWalkFailed());
        b.HomeReached();
        Assert.False(b.HomeWalkFailed());
    }

    [Fact]
    public void SaveLoadKeepsFailures()
    {
        var b = new StuckBook();
        b.WalkFailed(-100, 250, false);
        Assert.Equal(StuckNext.Hop, b.WalkFailed(40, 40, true));
        b.HomeWalkFailed();
        var c = StuckBook.Load(b.Save());
        Assert.True(c.Excluded(-100, 250));
        Assert.Equal(1, c.Fails(40, 40));
        Assert.Equal(1, c.HomeStuck);
        Assert.Equal(StuckNext.Exclude, c.WalkFailed(40, 40, true)); // 불러온 뒤에도 두 번째로 셈
    }

    [Fact]
    public void LoadIgnoresJunk() => Assert.Equal(0, StuckBook.Load("x|y|z;1|2;h|-3;;5|5|0").ExcludedCount);

    [Fact]
    public void WorkItemCarriesStuckBook()
    {
        var b = new StuckBook();
        b.WalkFailed(64, -64, false);
        var w = new WorkItem { Kind = WorkKind.Gather, Item = "Stone", Amount = 5, Resume = true, Stuck = b.Save() };
        var back = WorkState.LoadQueue(WorkState.SaveQueue(new[] { w })).Single();
        Assert.True(StuckBook.Load(back.Stuck).Excluded(64, -64));
    }
}
