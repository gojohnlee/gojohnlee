using System;
using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;
using P = ValheimHelper.Core.Work.RoughPath.P;

public class FarSkipTests
{
    static FarSkip Straight(double len, double speed) => new FarSkip(new List<P> { new P(0, 0), new P(len, 0) }, speed);

    [Fact]
    public void WaitsLengthOverRunSpeed()
    {
        var s = Straight(300, 6);
        Assert.Equal(50, s.Seconds, 3);
        for (int i = 0; i < 49; i++) s.Tick(1);
        Assert.True(s.Waiting);
        s.Tick(1);
        Assert.False(s.Waiting);
    }

    [Fact]
    public void MapPosMovesAlongPath()
    {
        var s = new FarSkip(new List<P> { new P(0, 0), new P(100, 0), new P(100, 100) }, 10);
        Assert.Equal(20, s.Seconds, 3);
        for (int i = 0; i < 15; i++) s.Tick(1);
        Assert.Equal(100, s.MapPos.X, 3);
        Assert.Equal(50, s.MapPos.Z, 3);
        for (int i = 0; i < 10; i++) s.Tick(1);
        Assert.Equal(100, s.MapPos.Z, 3); // 다 기다리면 도착점에 머묾
    }

    [Fact]
    public void RevealsEveryTwoSecondsAndAtEnd()
    {
        var s = Straight(50, 10); // 5초
        int reveals = 0;
        for (int i = 0; i < 50; i++) if (s.Tick(0.1)) reveals++;
        Assert.Equal(3, reveals); // 2초·4초·끝(5초)
        Assert.False(s.Tick(0.1)); // 다 기다린 뒤엔 안 밝힘
    }

    [Fact]
    public void LandTimesOutAfterLimit()
    {
        var s = Straight(60, 60);
        s.Tick(1);
        Assert.False(s.Waiting);
        Assert.False(s.LandTimedOut);
        s.Tick(FarSkip.LandLimit + 1);
        Assert.True(s.LandTimedOut);
    }

    [Fact]
    public void HopAimStepsTowardDestAndStopsNear()
    {
        var from = new P(0, 0);
        var dest = new P(200, 0);
        int hops = 0;
        while (FarSkip.HopAim(from, dest, FarSkip.HopStep, out P aim))
        {
            Assert.InRange(Math.Sqrt((aim.X - from.X) * (aim.X - from.X) + (aim.Z - from.Z) * (aim.Z - from.Z)), 1, FarSkip.HopStep + 1e-6);
            from = aim;
            Assert.True(++hops < 10);
        }
        Assert.Equal(3, hops); // 60·120·180(남은 20m는 걷기)
        Assert.True(200 - from.X <= FarSkip.ArriveNear);
        Assert.True(200 - from.X > 0); // 목적지를 넘지 않음
    }

    [Fact]
    public void ShortWalksAreNotSkipped()
    {
        Assert.False(FarSkip.Worth(FarSkip.MinDistance - 1));
        Assert.True(FarSkip.Worth(FarSkip.MinDistance));
        Assert.False(FarSkip.HopAim(new P(0, 0), new P(15, 0), 60, out _));
    }
}
