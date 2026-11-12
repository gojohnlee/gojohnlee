using System;
using ValheimHelper.Core.Npc;
using Xunit;

public class PatrolTests
{
    [Fact]
    public void StandsAtPost_ThenWalksFourPoints_ThenBack()
    {
        var p = new Patrol();
        Assert.Equal(PatrolGoal.Post, p.Tick(1f, false));
        Assert.Equal(PatrolGoal.Stand, p.Tick(1f, true));
        Assert.Equal(PatrolGoal.Stand, p.Tick(Patrol.Every - 2f, true));
        Assert.Equal(PatrolGoal.Point, p.Tick(2f, true));
        Assert.Equal(0, p.Point);
        Assert.Equal(PatrolGoal.Point, p.Tick(1f, true));
        Assert.Equal(1, p.Point);
        Assert.Equal(PatrolGoal.Point, p.Tick(1f, false, skip: true)); // 물 등 — 다음 점
        Assert.Equal(2, p.Point);
        Assert.Equal(PatrolGoal.Point, p.Tick(Patrol.PointLimit + 1f, false)); // 못 닿음 — 다음 점
        Assert.Equal(3, p.Point);
        Assert.Equal(PatrolGoal.Post, p.Tick(1f, true));
        Assert.Equal(1, p.Rounds);
        Assert.False(p.Walking);
        Assert.Equal(Patrol.Every, p.Wait);
    }

    [Fact]
    public void PostUnreachable_StandsAndStillPatrols()
    {
        var p = new Patrol();
        Assert.Equal(PatrolGoal.Post, p.Tick(Patrol.PostLimit - 1f, false));
        Assert.Equal(PatrolGoal.Stand, p.Tick(2f, false));
        p.Now();
        Assert.Equal(PatrolGoal.Point, p.Tick(0.5f, false));
    }

    [Fact]
    public void Reset_GoesBackToPost_KeepsWait()
    {
        var p = new Patrol();
        p.Now();
        p.Tick(1f, true);
        Assert.True(p.Walking);
        p.Reset();
        Assert.False(p.Walking);
        Assert.Equal(PatrolGoal.Post, p.Tick(1f, false));
        Assert.Equal(Patrol.Every, p.Wait);
    }

    [Fact]
    public void Points_StartAtDoorSide_TurnQuarter()
    {
        Patrol.PointAt(10, 10, 0, 5, 0, 20, out double x, out double z);
        Assert.Equal(10, x, 3); Assert.Equal(30, z, 3);
        Patrol.PointAt(10, 10, 0, 5, 1, 20, out x, out z);
        Assert.Equal(-10, x, 3); Assert.Equal(10, z, 3);
        Patrol.PointAt(10, 10, 0, 5, 2, 20, out x, out z);
        Assert.Equal(10, x, 3); Assert.Equal(-10, z, 3);
    }

    [Fact]
    public void RouteCountAndLimit()
    {
        var p = new Patrol();
        p.Now();
        Assert.Equal(PatrolGoal.Point, p.Tick(0f, true, false, 8, 70f));
        Assert.Equal(PatrolGoal.Point, p.Tick(Patrol.PointLimit + 1f, false, false, 8, 70f)); // 먼 점은 한도가 길다 — 아직 같은 점
        Assert.Equal(0, p.Point);
        for (int i = 0; i < 7; i++) Assert.Equal(PatrolGoal.Point, p.Tick(0f, true, false, 8, 70f));
        Assert.Equal(7, p.Point);
        Assert.Equal(PatrolGoal.Post, p.Tick(0f, true, false, 8, 70f));
        Assert.Equal(1, p.Rounds);
    }
}
