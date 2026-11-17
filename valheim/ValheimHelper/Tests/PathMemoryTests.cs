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

    // 요청 11-17: 기억이 쌓여도 비효율적인 길은 스스로 바뀜
    static List<P> Detour(float x1) => new List<P> { new P(0, 0, 0), new P(0, 0, 6), new P(x1, 0, 6), new P(x1, 0, 0) };

    [Fact]
    public void Remember_KeepsShorter_ForceReplaces()
    {
        var m = new PathMemory();
        P a = new P(0, 0, 0), b = new P(10, 0, 0);
        Assert.True(m.Remember(a, b, Line(0, 10), 1));
        Assert.False(m.Remember(a, b, Detour(10), 2));              // 22m 돌아가는 길은 10m 길을 못 밀어냄
        Assert.Equal(10f, m.LengthOf(a, b), 1);
        Assert.True(m.Remember(a, b, Detour(10), 3, force: true));
        Assert.True(m.Remember(a, b, Line(0, 10), 4));              // 짧은 길이 다시 이김
        Assert.Equal(10f, m.LengthOf(a, b), 1);
    }

    [Fact]
    public void Find_Explores_SuspiciousEveryOther_NormalEveryEighth()
    {
        var m = new PathMemory();
        P a = new P(0, 0, 0), b = new P(10, 0, 0), c = new P(0, 0, 20), d = new P(10, 0, 20);
        m.Remember(a, b, Detour(10), 1);                            // 길/직선 2.2 — 수상
        m.Remember(c, d, new List<P> { c, new P(5, 0, 20), d }, 1);
        var bad = new List<bool>(); var ok = new List<bool>();
        for (int i = 0; i < 8; i++) { m.Find(a, b, 2, out bool e1); bad.Add(e1); m.Find(c, d, 2, out bool e2); ok.Add(e2); }
        Assert.Equal(new[] { false, true, false, true, false, true, false, true }, bad);   // 방금 걸은 길은 한 번 따르고 그다음 다시 걸어 봄
        Assert.Equal(1, ok.FindAll(x => x).Count);
        Assert.True(ok[6]);                                          // Uses 1에서 시작 → 8번째 = 일곱 번째 Find
    }

    [Fact]
    public void Loaded_SuspiciousRoute_ExploredFirst_ThenReplacedByShorterWalk()
    {
        var m = new PathMemory();
        P a = new P(0, 0, 0), b = new P(10, 0, 0);
        m.Remember(a, b, Detour(10), 1, force: true);
        var m2 = new PathMemory();
        m2.Load(m.Save());
        Assert.Null(m2.Find(a, b, 2, out bool explore));             // 쌓여 있던 수상한 길 — 첫 번에 바닐라로 다시 걸어 봄
        Assert.True(explore);
        Assert.True(m2.Remember(a, b, Line(0, 10), 3));              // 바닐라가 걸은 짧은 길로 바뀜
        Assert.NotNull(m2.Find(a, b, 4));
        Assert.Equal(10f, m2.LengthOf(a, b), 1);
    }

    [Fact]
    public void CrossFloor_StairRoute_NotSuspiciousUnderTwice()
    {
        var m = new PathMemory();
        P a = new P(0, 0, 0), b = new P(0, 3, 4);                   // 직선 5m, 계단 돌아 9m(1.8배)
        m.Remember(a, b, new List<P> { a, new P(2, 0, 0), new P(2, 3, 4), b }, 1);
        for (int i = 0; i < 6; i++) Assert.NotNull(m.Find(a, b, 2));
    }
}
