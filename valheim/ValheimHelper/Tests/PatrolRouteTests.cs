using System;
using ValheimHelper.Core.Npc;
using Xunit;

public class PatrolRouteTests
{
    static double R(double x, double z) => Math.Sqrt(x * x + z * z);

    [Fact]
    public void OpenGround_EightPointsAtOuterRing_DoorFirst()
    {
        var r = new PatrolRoute();
        Assert.True(r.Due(0f, 1));
        r.Build(0, 0, 1, 0, (x, z) => PatrolSpot.Ok);
        Assert.Equal(PatrolRoute.Dirs, r.Points.Count);
        Assert.Equal(120, r.Points[0].X, 3);
        Assert.Equal(0, r.Points[0].Z, 3);
        for (int i = 0; i < r.Points.Count; i++) Assert.Equal(i, r.Points[i].Dir);
        Assert.False(r.Due(1f, 1));
    }

    [Fact]
    public void Water_PullsInward_ThenDropsDirection()
    {
        var r = new PatrolRoute();
        r.Due(0f, 1);
        // 동쪽(x>0) 70m 넘으면 물: 0번 방향은 60m, 서쪽 반은 모두 바다
        r.Build(0, 0, 1, 0, (x, z) => x < -10 ? PatrolSpot.Water : (R(x, z) > 70 && Math.Abs(z) < 1 ? PatrolSpot.Water : PatrolSpot.Ok));
        Assert.Equal(60, r.Points[0].X, 3);
        Assert.Equal(PatrolSpot.Water, r.DroppedWhy[4]);
        Assert.Null(r.DroppedWhy[0]);
        Assert.True(r.Points.Count < PatrolRoute.Dirs);
    }

    [Fact]
    public void Unreached_BansSpot_NextBuildGoesInward_RecheckClears()
    {
        var r = new PatrolRoute();
        r.Due(0f, 1);
        r.Build(0, 0, 1, 0, (x, z) => PatrolSpot.Ok);
        r.Unreached(0);
        Assert.True(r.Due(1f, 1));
        r.Build(0, 0, 1, 0, (x, z) => PatrolSpot.Ok);
        Assert.Equal(100, r.Points[0].X, 3);
        Assert.False(r.Due(1f, 1));
        Assert.True(r.Due(PatrolRoute.RecheckEvery, 1));
        r.Build(0, 0, 1, 0, (x, z) => PatrolSpot.Ok);
        Assert.Equal(120, r.Points[0].X, 3);
    }

    [Fact]
    public void AllBanned_DirectionDroppedAsUnreached_GroundChangeClears()
    {
        var r = new PatrolRoute();
        r.Due(0f, 1);
        for (int k = 0; k < PatrolRoute.Radii.Length; k++)
        {
            r.Build(0, 0, 1, 0, (x, z) => PatrolSpot.Ok);
            r.Unreached(0);
            r.Due(1f, 1);
        }
        r.Build(0, 0, 1, 0, (x, z) => PatrolSpot.Ok);
        Assert.Equal(PatrolSpot.Unreached, r.DroppedWhy[0]);
        Assert.Equal(PatrolRoute.Dirs - 1, r.Points.Count);
        Assert.True(r.Due(1f, 2)); // 땅 바뀜
        r.Build(0, 0, 1, 0, (x, z) => PatrolSpot.Ok);
        Assert.Equal(PatrolRoute.Dirs, r.Points.Count);
    }

    [Fact]
    public void NothingOk_EmptyPoints()
    {
        var r = new PatrolRoute();
        r.Due(0f, 1);
        r.Build(0, 0, 0, 0, (x, z) => PatrolSpot.Built);
        Assert.Empty(r.Points);
        Assert.Equal(PatrolSpot.Built, r.DroppedWhy[3]);
    }

    [Fact]
    public void Steep_ByAngle()
    {
        Assert.False(PatrolRoute.Steep(0, 1, 0, 0, 0, 4, 30));
        Assert.True(PatrolRoute.Steep(0, 0, 3, 0, 0, 4, 30));
    }
}
