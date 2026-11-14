using System.Collections.Generic;
using System.Linq;
using ValheimHelper.Core.Npc;
using Xunit;

public class FrameStatsTests
{
    static List<double> Frames(int n, double secs)
    {
        var l = new List<double>();
        for (int i = 0; i < n; i++) l.Add(secs);
        return l;
    }

    [Fact]
    public void EmptyIsAllZero()
    {
        var s = FrameStats.Summarize(new List<double>());
        Assert.Equal(0, s.Count);
        Assert.Equal(0, s.AvgMs);
        Assert.Equal(0, s.WorstMs);
        Assert.Equal(0, s.Worst1PctMs);
    }

    [Fact]
    public void BadFramesAreDropped()
    {
        var l = Frames(3, 0.02);
        l.Add(0);
        l.Add(-1);
        l.Add(double.NaN);
        var s = FrameStats.Summarize(l);
        Assert.Equal(3, s.Count);
        Assert.Equal(20.0, s.AvgMs, 6);
    }

    [Fact]
    public void AverageAndWorst()
    {
        var l = Frames(99, 0.010);
        l.Add(0.050);
        var s = FrameStats.Summarize(l);
        Assert.Equal(100, s.Count);
        Assert.Equal((99 * 10.0 + 50.0) / 100, s.AvgMs, 6);
        Assert.Equal(50.0, s.WorstMs, 6);
    }

    [Fact]
    public void WorstOnePercentIsAverageOfSlowestPercent()
    {
        // 200장이면 가장 느린 2장(1%)의 평균
        var l = Frames(198, 0.010);
        l.Add(0.030);
        l.Add(0.050);
        var s = FrameStats.Summarize(l);
        Assert.Equal(40.0, s.Worst1PctMs, 6);
    }

    [Fact]
    public void FewFramesUseTheSlowestOne()
    {
        // 장수가 100 밑이면 1%는 한 장(가장 느린 것)
        var s = FrameStats.Summarize(new List<double> { 0.010, 0.040, 0.020 });
        Assert.Equal(40.0, s.Worst1PctMs, 6);
        Assert.Equal(40.0, s.WorstMs, 6);
    }

    [Fact]
    public void ToTextFormatsOneLine()
    {
        var s = FrameStats.Summarize(new List<double> { 0.010, 0.030 });
        Assert.Equal("프레임 2장 · 평균 20.0 ms · 최악 30.0 ms · 1% 최악 30.0 ms", FrameStats.ToText(s));
    }
}
