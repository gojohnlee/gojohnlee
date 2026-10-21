using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;

public class PortalRouteTests
{
    static PortalLink P(float x, float z, float ex, float ez) => new PortalLink { X = x, Z = z, ExitX = ex, ExitZ = ez };

    [Fact]
    public void NoPortalsWalks()
    {
        Assert.Equal(-1, PortalRoute.Choose(0, 0, 1000, 0, new List<PortalLink>()));
    }

    [Fact]
    public void PortalNearStartAndExitNearGoalIsChosen()
    {
        var list = new List<PortalLink> { P(20, 0, 3000, 0) };
        Assert.Equal(0, PortalRoute.Choose(0, 0, 3010, 0, list));
    }

    [Fact]
    public void ShortTripWalksEvenWithPortal()
    {
        var list = new List<PortalLink> { P(20, 0, 3000, 0) };
        Assert.Equal(-1, PortalRoute.Choose(0, 0, 100, 0, list));
    }

    [Fact]
    public void PortalTooFarToWalkToIsIgnored()
    {
        var list = new List<PortalLink> { P(500, 0, 3000, 0) };
        Assert.Equal(-1, PortalRoute.Choose(0, 0, 3010, 0, list));
    }

    [Fact]
    public void SavingBelowMarginWalks()
    {
        // 걸으면 400, 포탈은 입구 30 + 출구 330 = 360 → 40m 절약뿐이라 걷는다
        var list = new List<PortalLink> { P(30, 0, 70, 0) };
        Assert.Equal(-1, PortalRoute.Choose(0, 0, 400, 0, list));
    }

    [Fact]
    public void CheapestPortalWins()
    {
        var list = new List<PortalLink> { P(100, 0, 1000, 0), P(10, 0, 2900, 0) };
        Assert.Equal(1, PortalRoute.Choose(0, 0, 3000, 0, list));
    }
}
