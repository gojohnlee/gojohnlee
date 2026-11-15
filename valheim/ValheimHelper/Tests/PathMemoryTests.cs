using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;
using P = ValheimHelper.Core.Work.PathMemory.P;

public class PathMemoryTests
{
    static List<P> Line(float x0, float x1, float y = 0) => new List<P> { new P(x0, y, 0), new P((x0 + x1) / 2, y, 0), new P(x1, y, 0) };

    [Fact]
    public void Find_SameCellPair_EvenIfSlightlyOff()
    {
        var m = new PathMemory();
        m.Remember(new P(0.1f, 0, 0.1f), new P(10.1f, 0, 0.1f), Line(0, 10), 1);
        Assert.NotNull(m.Find(new P(0.4f, 0.5f, 0.4f), new P(10.3f, 0.2f, 0.2f), 2));   // 같은 0.5m 칸·같은 층
        Assert.Null(m.Find(new P(0.6f, 0, 0.1f), new P(10.1f, 0, 0.1f), 2));           // 출발 칸 다름
        Assert.Null(m.Find(new P(0.1f, 4.2f, 0.1f), new P(10.1f, 0, 0.1f), 2));         // 위층 같은 자리
    }

    [Fact]
    public void ShortPath_NotRemembered_ForgetRemoves()
    {
        var m = new PathMemory();
        m.Remember(new P(0, 0, 0), new P(5, 0, 0), new List<P> { new P(0, 0, 0) }, 1);
        Assert.Equal(0, m.Count);
        m.Remember(new P(0, 0, 0), new P(5, 0, 0), Line(0, 5), 1);
        Assert.True(m.Forget(new P(0.2f, 0, 0.2f), new P(5.2f, 0, 0.2f)));
        Assert.Equal(0, m.Count);
    }

    [Fact]
    public void OverCap_EvictsLeastRecentlyUsed()
    {
        var m = new PathMemory(2);
        m.Remember(new P(0, 0, 0), new P(10, 0, 0), Line(0, 10), 1);
        m.Remember(new P(20, 0, 0), new P(30, 0, 0), Line(20, 30), 2);
        m.Find(new P(0, 0, 0), new P(10, 0, 0), 3);                                    // 첫 길을 최근에 씀
        m.Remember(new P(40, 0, 0), new P(50, 0, 0), Line(40, 50), 4);
        Assert.Equal(2, m.Count);
        Assert.NotNull(m.Find(new P(0, 0, 0), new P(10, 0, 0), 5));
        Assert.Null(m.Find(new P(20, 0, 0), new P(30, 0, 0), 5));
    }

    [Fact]
    public void ForgetNear_RemovesRoutesPassingBuildChange_SameFloorOnly()
    {
        var m = new PathMemory();
        m.Remember(new P(0, 0, 0), new P(10, 0, 0), Line(0, 10), 1);                   // (5,0,0)을 지남
        m.Remember(new P(0, 0, 20), new P(10, 0, 20), new List<P> { new P(0, 0, 20), new P(10, 0, 20) }, 1);
        m.Remember(new P(0, 8, 0), new P(10, 8, 0), Line(0, 10, 8), 1);                // 위층
        Assert.Equal(1, m.ForgetNear(new P(5, 0, 1), 2));
        Assert.Equal(2, m.Count);
    }

    [Fact]
    public void Prune_RemovesOld()
    {
        var m = new PathMemory();
        m.Remember(new P(0, 0, 0), new P(10, 0, 0), Line(0, 10), 0);
        m.Remember(new P(20, 0, 0), new P(30, 0, 0), Line(20, 30), 900);
        Assert.Equal(1, m.Prune(1000, 500));
        Assert.Equal(1, m.Count);
    }

    [Fact]
    public void SaveLoad_RoundTrips_SkipsBadLines()
    {
        var m = new PathMemory();
        m.Remember(new P(-3959.2f, 31.4f, -5829.7f), new P(-3950.1f, 35.2f, -5820.3f),
            new List<P> { new P(-3959.2f, 31.4f, -5829.7f), new P(-3955f, 33f, -5825f), new P(-3950.1f, 35.2f, -5820.3f) }, 12.5);
        string text = m.Save() + "망가진 줄\n1;2;3\n";
        var back = new PathMemory();
        Assert.Equal(1, back.Load(text));
        var pts = back.Find(new P(-3959.2f, 31.4f, -5829.7f), new P(-3950.1f, 35.2f, -5820.3f), 13);
        Assert.NotNull(pts);
        Assert.Equal(3, pts.Length);
        Assert.Equal(-3955f, pts[1].X, 2);
    }
}
