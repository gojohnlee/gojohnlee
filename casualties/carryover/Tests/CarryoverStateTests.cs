using Carryover.Core;
using Xunit;

public class CarryoverStateTests
{
    static readonly int[] Base = { 9, 8, 7 };

    [Fact]
    public void FirstRun_NoBonus()
    {
        var s = new CarryoverState();
        Assert.Equal(new[] { 9, 8, 7 }, s.OnNewRun(Base));
    }

    [Fact]
    public void UserExample_Gain333_ThenDieAtOnce_Gives000()
    {
        var s = new CarryoverState();
        s.OnNewRun(Base);                                                    // 1회차: 9 8 7
        Assert.Equal(new[] { 3, 3, 3 }, s.OnRunEnd(new[] { 12, 11, 10 }));
        Assert.Equal(new[] { 12, 11, 10 }, s.OnNewRun(Base));                // 2회차: +3 +3 +3
        Assert.Equal(new[] { 0, 0, 0 }, s.OnRunEnd(new[] { 12, 11, 10 }));  // 바로 죽음: 물려받은 건 얻은 양 아님
        Assert.Equal(new[] { 9, 8, 7 }, s.OnNewRun(Base));                   // 3회차: +0
    }

    [Fact]
    public void NotCumulative_OnlyLastRunGain()
    {
        var s = new CarryoverState();
        s.OnNewRun(Base); s.OnRunEnd(new[] { 12, 11, 10 });   // +3 +3 +3
        s.OnNewRun(Base); s.OnRunEnd(new[] { 13, 11, 10 });   // 12→13: +1 0 0
        Assert.Equal(new[] { 10, 8, 7 }, s.OnNewRun(Base));
    }

    [Fact]
    public void Loss_IsClampedToZero()
    {
        var s = new CarryoverState();
        s.OnNewRun(Base);
        Assert.Equal(new[] { 0, 2, 0 }, s.OnRunEnd(new[] { 9, 10, 0 }));
    }

    [Fact]
    public void EndCalledTwice_UsesSameStart()
    {
        var s = new CarryoverState();
        s.OnNewRun(Base);
        s.OnRunEnd(new[] { 10, 8, 7 });
        Assert.Equal(new[] { 2, 0, 0 }, s.OnRunEnd(new[] { 11, 8, 7 }));   // 되살아나 더 얻고 다시 죽음
        Assert.Equal(new[] { 2, 0, 0 }, s.Pending);
    }

    [Fact]
    public void UnfinishedRun_KeepsPreviousBonus()
    {
        var s = new CarryoverState();
        s.OnNewRun(Base); s.OnRunEnd(new[] { 12, 8, 7 });
        s.OnNewRun(Base);                                      // 끝내지 않고 새로 시작
        Assert.Equal(new[] { 12, 8, 7 }, s.OnNewRun(Base));
    }

    [Fact]
    public void Tutorial_NotCounted()
    {
        var s = new CarryoverState();
        s.OnNewRun(Base); s.OnRunEnd(new[] { 12, 8, 7 });
        s.OnUncountedRun();
        Assert.Null(s.OnRunEnd(new[] { 0, 0, 0 }));
        Assert.Equal(new[] { 3, 0, 0 }, s.Pending);
    }

    [Fact]
    public void LoadedRun_KeepsStart_OrAdoptsCurrent()
    {
        var s = new CarryoverState();
        s.OnNewRun(Base);
        s.OnLoadedRun(new[] { 11, 8, 7 });                     // 이어하기: 시작 값 9 그대로
        Assert.Equal(new[] { 4, 0, 0 }, s.OnRunEnd(new[] { 13, 8, 7 }));

        var fresh = new CarryoverState();                      // 모드를 회차 도중에 깖
        fresh.OnLoadedRun(new[] { 11, 8, 7 });
        Assert.Equal(new[] { 2, 0, 0 }, fresh.OnRunEnd(new[] { 13, 8, 7 }));
    }

    [Fact]
    public void SerializeRoundTrip_AndBadInputIgnored()
    {
        var s = new CarryoverState();
        s.OnNewRun(Base); s.OnRunEnd(new[] { 12, 9, 7 });
        var t = CarryoverState.Parse(s.Serialize());
        Assert.True(t.RunActive);
        Assert.Equal(new[] { 9, 8, 7 }, t.Start);
        Assert.Equal(new[] { 3, 1, 0 }, t.Pending);

        var bad = CarryoverState.Parse("pending=1,x,3\nstart=1,2\nrunActive=1\r\ngarbage");
        Assert.Equal(new[] { 0, 0, 0 }, bad.Pending);
        Assert.Equal(new[] { 0, 0, 0 }, bad.Start);
        Assert.True(bad.RunActive);
        Assert.False(CarryoverState.Parse(null).RunActive);
    }
}
