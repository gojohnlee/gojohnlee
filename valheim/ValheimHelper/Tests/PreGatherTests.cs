using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;

public class PreGatherTests
{
    static Dictionary<string, int> T(params (string, int)[] a) { var d = new Dictionary<string, int>(); foreach (var (k, n) in a) d[k] = n; return d; }

    [Fact]
    public void FirstObserveIsBaseline()
    {
        var b = new PreGatherBook();
        b.Observe(T(("Wood", 3)), 0);
        Assert.Empty(b.Wants(_ => 3, null, 10));
    }

    [Fact]
    public void UsedAndLowIsWanted()
    {
        var b = new PreGatherBook();
        b.Observe(T(("Wood", 30), ("Stone", 40)), 0);
        b.Observe(T(("Wood", 12), ("Stone", 38)), 60); // 나무 18 씀, 돌 2(MinUsed 밑)
        var w = b.Wants(i => i == "Wood" ? 12 : 38, null, 70);
        Assert.Single(w);
        Assert.Equal("Wood", w[0].Item);
        Assert.Equal(PreGatherBook.Target - 12, w[0].Amount);
    }

    [Fact]
    public void GainDoesNotCount()
    {
        var b = new PreGatherBook();
        b.Observe(T(("Wood", 0)), 0);
        b.Observe(T(("Wood", 50)), 60);
        Assert.Empty(b.Wants(_ => 0, null, 70));
    }

    [Fact]
    public void GoneItemCountsAsUsed()
    {
        var b = new PreGatherBook();
        b.Observe(T(("Flint", 8)), 0);
        b.Observe(T(), 60);
        Assert.Single(b.Wants(_ => 0, null, 70));
    }

    [Fact]
    public void OldUseAndFullStockAndSkip()
    {
        var b = new PreGatherBook();
        b.Observe(T(("Wood", 30), ("Resin", 30)), 0);
        b.Observe(T(("Wood", 10), ("Resin", 10)), 60);
        Assert.Empty(b.Wants(_ => 5, null, 60 + PreGatherBook.Window + 1)); // 오래됨
        Assert.Empty(b.Wants(_ => PreGatherBook.Low, null, 70));            // 재고 넉넉
        var w = b.Wants(_ => 5, i => i == "Wood", 70);                      // 계속채워 등은 뺌
        Assert.Single(w);
        Assert.Equal("Resin", w[0].Item);
    }

    [Fact]
    public void RoomNeedsSlack()
    {
        Assert.True(PreGatherBook.Room(3, 25, 50));   // 1묶음 + 2
        Assert.False(PreGatherBook.Room(2, 25, 50));
        Assert.False(PreGatherBook.Room(3, 25, 20));  // 2묶음 + 2
    }
}

public class PreGatherResetTests
{
    [Fact]
    public void ResetForgetsUseAndBaseline()
    {
        var b = new PreGatherBook();
        b.Observe(new Dictionary<string, int> { ["Wood"] = 30 }, 0);
        b.Observe(new Dictionary<string, int> { ["Wood"] = 10 }, 60);
        b.Reset();
        Assert.Empty(b.Wants(_ => 0, null, 70));
        b.Observe(new Dictionary<string, int>(), 80); // 다시 기준만
        Assert.Empty(b.Wants(_ => 0, null, 90));
    }
}
