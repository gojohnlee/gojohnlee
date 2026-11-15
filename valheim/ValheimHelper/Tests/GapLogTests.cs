using System.Linq;
using ValheimHelper.Core.Npc;
using Xunit;

public class GapLogTests
{
    [Fact]
    public void StandingSpanIsCountedWhenMovingAgain()
    {
        var g = new GapLog();
        g.Tick("라그나", "Deposit", true, 10.0);
        g.Tick("라그나", "Deposit", true, 11.0);
        g.Tick("라그나", "Deposit", false, 12.5);
        var r = Assert.Single(g.Rows());
        Assert.Equal("라그나", r.Worker);
        Assert.Equal("Deposit", r.Step);
        Assert.Equal(1, r.Count);
        Assert.Equal(2.5, r.Worst, 3);
        Assert.Equal(2.5, r.Avg, 3);
    }

    [Fact]
    public void StepChangeSplitsTheSpan()
    {
        var g = new GapLog();
        g.Tick("라그나", "Pick", true, 0.0);
        g.Tick("라그나", "Deposit", true, 1.0);
        g.Tick("라그나", "Deposit", false, 4.0);
        var rows = g.Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Deposit", rows[0].Step); // 최악이 긴 순
        Assert.Equal(3.0, rows[0].Worst, 3);
        Assert.Equal(1.0, rows[1].Worst, 3);
    }

    [Fact]
    public void ShortStopsAreIgnored()
    {
        var g = new GapLog();
        g.Tick("라그나", "Walk", true, 0.0);
        g.Tick("라그나", "Walk", false, 0.2);
        Assert.Empty(g.Rows());
    }

    [Fact]
    public void CountAverageWorstPerWorkerAndStep()
    {
        var g = new GapLog();
        g.Tick("라그나", "Pick", true, 0.0);
        g.Tick("라그나", "Pick", false, 1.0);
        g.Tick("라그나", "Pick", true, 5.0);
        g.Tick("라그나", "Pick", false, 8.0);
        g.Tick("플로키", "Pick", true, 0.0);
        g.Tick("플로키", "Pick", false, 0.5);
        var ragna = g.Rows().Single(r => r.Worker == "라그나");
        Assert.Equal(2, ragna.Count);
        Assert.Equal(2.0, ragna.Avg, 3);
        Assert.Equal(3.0, ragna.Worst, 3);
        Assert.Equal(4.0, ragna.Total, 3);
        Assert.Equal(1, g.Rows().Single(r => r.Worker == "플로키").Count);
    }

    [Fact]
    public void CloseAllEndsOpenSpans()
    {
        var g = new GapLog();
        g.Tick("라그나", "Idle", true, 0.0);
        Assert.Empty(g.Rows());
        g.CloseAll(7.0);
        Assert.Equal(7.0, Assert.Single(g.Rows()).Worst, 3);
        g.CloseAll(9.0); // 두 번 닫아도 안 늘어남
        Assert.Equal(1, Assert.Single(g.Rows()).Count);
    }

    [Fact]
    public void TooManyKindsGoToOther()
    {
        var g = new GapLog();
        for (int i = 0; i < GapLog.MaxKeys + 5; i++)
        {
            g.Tick("라그나", "S" + i, true, i * 10.0);
            g.Tick("라그나", "S" + i, false, i * 10.0 + 1.0);
        }
        var rows = g.Rows();
        Assert.Equal(GapLog.MaxKeys + 1, rows.Count);
        Assert.Equal(5, rows.Single(r => r.Step == GapLog.Other).Count);
    }

    [Fact]
    public void TableHidesRowsBelowMinWorst()
    {
        var g = new GapLog();
        g.Tick("라그나", "Pick", true, 0.0);
        g.Tick("라그나", "Pick", false, 0.5);
        g.Tick("라그나", "Deposit", true, 1.0);
        g.Tick("라그나", "Deposit", false, 3.6);
        var t = GapLog.Table(g.Rows(), 1.0);
        Assert.Equal(3, t.Count);
        Assert.Equal("| 라그나 | Deposit | 1 | 2.6 | 2.6 | 3 |", t[2]);
        Assert.Equal(4, GapLog.Table(g.Rows()).Count);
    }
}
