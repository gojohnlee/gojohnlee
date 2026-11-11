using ValheimHelper.Core.Work;
using Xunit;

public class StuckClockTests
{
    static bool Run(StuckClock c, ref double t, double secs, double remaining, bool seen = false, bool paused = false)
    {
        bool stuck = false;
        for (int i = 0; i < (int)(secs / 0.1 + 0.5); i++) { t += 0.1; stuck = c.Tick(t, 0.1, remaining, seen, paused); if (stuck) break; }
        return stuck;
    }

    [Fact]
    public void UnseenStuckAfterOneSecond()
    {
        var c = new StuckClock(); double t = 0;
        Assert.False(c.Tick(t, 0, 10, false, false));
        Assert.False(Run(c, ref t, 0.8, 10));
        Assert.True(Run(c, ref t, 0.4, 10));
    }

    [Fact]
    public void SeenStuckAfterThreeSecondsAndGraceTwoSeconds()
    {
        var c = new StuckClock(); double t = 0;
        c.Tick(t, 0, 10, true, false);
        Assert.False(Run(c, ref t, 2.8, 10, seen: true));
        Assert.True(Run(c, ref t, 0.4, 10, seen: true));
        // 안 보게 된 뒤 2초 동안은 여전히 3초 기준
        c.Reset(); t = 100; c.Tick(t, 0, 10, true, false);
        Assert.False(Run(c, ref t, 1.5, 10)); // 안 봄 1.5초 — 아직 '봄'
        Assert.True(c.Seen(t));
        Assert.True(Run(c, ref t, 0.7, 10)); // 2초 지나 '안 봄' → 1초 기준을 이미 넘김
    }

    [Fact]
    public void ProgressResetsAndSmallWobbleDoesNot()
    {
        var c = new StuckClock(); double t = 0;
        c.Tick(t, 0, 10, false, false);
        Assert.False(Run(c, ref t, 0.8, 10));
        Assert.False(Run(c, ref t, 0.1, 9)); // 1m 줄어 진척
        Assert.Equal(0, c.NoGain, 6);
        Assert.False(Run(c, ref t, 0.5, 8.9));
        Assert.True(Run(c, ref t, 0.6, 9.5)); // 늘었다 줄었다 0.3m 안: 진척 아님
    }

    [Fact]
    public void PausedStopsClock()
    {
        var c = new StuckClock(); double t = 0;
        c.Tick(t, 0, 10, false, false);
        Assert.False(Run(c, ref t, 5, 10, paused: true));
        Assert.Equal(0, c.NoGain, 6);
        Assert.True(Run(c, ref t, 1.1, 10));
    }

    [Fact]
    public void TwoHopsSpendTarget()
    {
        var c = new StuckClock(); double t = 0;
        c.Tick(t, 0, 10, false, false);
        Assert.True(Run(c, ref t, 1.2, 10));
        c.NoteHop(); Assert.False(c.HopsSpent);
        c.Tick(t, 0, 4, false, false);
        Assert.True(Run(c, ref t, 1.2, 4));
        c.NoteHop(); Assert.True(c.HopsSpent);
        c.Reset(); Assert.False(c.HopsSpent);
    }

    [Fact]
    public void WatchingRules()
    {
        Assert.True(StuckClock.Watching(15, false, false));
        Assert.True(StuckClock.Watching(50, true, true));
        Assert.False(StuckClock.Watching(50, true, false));
        Assert.False(StuckClock.Watching(50, false, true));
        Assert.False(StuckClock.Watching(150, true, true));
        Assert.True(StuckClock.InFront(1, 0, 5, 3));
        Assert.False(StuckClock.InFront(1, 0, -5, 3));
    }
}

public class StuckClockReplanTests
{
    [Fact]
    public void NewLongerPathRebaselinesUpToLimit()
    {
        var c = new StuckClock(); double t = 0;
        c.Tick(t, 0, 10, false, false);
        t += 0.8; Assert.False(c.Tick(t, 0.8, 10, false, false));
        t += 0.1; Assert.False(c.Tick(t, 0.1, 14, false, false, newPath: true)); // 돌아가는 길
        Assert.Equal(0, c.NoGain, 6);
        t += 0.5; Assert.False(c.Tick(t, 0.5, 13, false, false)); // 새 길 따라 진척
        for (int i = 0; i < StuckClock.MaxReplans; i++) { t += 0.5; Assert.False(c.Tick(t, 0.5, 20 + i, false, false, newPath: true)); }
        t += 0.5; c.Tick(t, 0.5, 30, false, false, newPath: true); // 한도 넘음: 기준 안 바꿈
        t += 0.6; Assert.True(c.Tick(t, 0.6, 30, false, false));
    }
}
