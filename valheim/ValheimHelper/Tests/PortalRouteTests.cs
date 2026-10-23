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

    // 바다: x 200~400 사이는 걸어서 못 건넘(사용자 결정 2026-10-23 "바다 건너 일터는 포탈로만")
    static bool Sea(float ax, float az, float bx, float bz) => !((ax < 200 && bx > 400) || (bx < 200 && ax > 400));

    [Fact]
    public void AcrossSeaUsesPortalEvenIfNotShorter()
    {
        // 바로 걷기 500m, 포탈은 입구 100 + 출구에서 403 = 503 → 걸을 수 있으면 안 탐, 바다면 탐
        var list = new List<PortalLink> { P(0, 100, 450, 400) };
        Assert.Equal(-1, PortalRoute.Choose(0, 0, 500, 0, list));
        Assert.Equal(0, PortalRoute.Choose(0, 0, 500, 0, list, Sea));
    }

    [Fact]
    public void AcrossSeaWithoutGoodPortalWalksNowhere()
    {
        // 출구도 바다 이쪽이면 못 씀 → -1(부르는 쪽이 "바다 건너라 포탈이 있어야" 말)
        var list = new List<PortalLink> { P(10, 0, 150, 0) };
        Assert.Equal(-1, PortalRoute.Choose(0, 0, 500, 0, list, Sea));
        Assert.Equal(-1, PortalRoute.Choose(0, 0, 500, 0, new List<PortalLink>(), Sea));
    }

    [Fact]
    public void PortalWhoseEntranceIsAcrossSeaIsSkipped()
    {
        // 좁은 바다 200~260: 싼 포탈 A는 입구(270)가 바다 건너라 못 걸어감 → 비싼 B
        static bool Sea2(float ax, float az, float bx, float bz) => !((ax < 200 && bx > 260) || (bx < 200 && ax > 260));
        var list = new List<PortalLink> { P(270, 0, 500, 0), P(20, 0, 380, 0) };
        Assert.Equal(0, PortalRoute.Choose(150, 0, 500, 0, list));
        Assert.Equal(1, PortalRoute.Choose(150, 0, 500, 0, list, Sea2));
    }
}
