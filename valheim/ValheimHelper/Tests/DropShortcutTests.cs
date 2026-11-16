using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;
using P = ValheimHelper.Core.Work.PathMemory.P;
using Spot = ValheimHelper.Core.Work.DropShortcut.Spot;

public class DropShortcutTests
{
    [Fact]
    public void Ok_HeightWallLanding()
    {
        Assert.True(DropShortcut.Ok(48.7f, 42.6f, false, true));   // 메인 집 2층 → 1층 6.1m
        Assert.False(DropShortcut.Ok(48.7f, 39f, false, true));    // 9.7m — 8m 넘음
        Assert.False(DropShortcut.Ok(43.5f, 42.6f, false, true));  // 턱
        Assert.False(DropShortcut.Ok(48.7f, 42.6f, true, true));   // 벽
        Assert.False(DropShortcut.Ok(48.7f, 42.6f, false, false)); // 착지 자리에 기계·틈
    }

    [Fact]
    public void Pick_FasterThanStairs_OnlyGoingDown()
    {
        var spots = new List<Spot> { new Spot(new P(5, 48.7f, 0), new P(6, 42.6f, 0)) };
        P bed = new P(0, 48.7f, 0), chest = new P(10, 42.6f, 0);
        // 계단 길 40m, 6m/s → 6.7초. 지름길 5m + 4m 걷기 1.5초 + 낙하 1.1초 + 0.5초 ≈ 3.1초
        Assert.True(DropShortcut.Pick(spots, bed, chest, 40f, 6f, out Spot s));
        Assert.Equal(5f, s.Edge.X);
        Assert.False(DropShortcut.Pick(spots, chest, bed, 40f, 6f, out _));   // 올라갈 땐 계단
        Assert.False(DropShortcut.Pick(spots, bed, chest, 18f, 6f, out _));   // 계단 3초 — 1초 넘게 안 빠름
    }

    [Fact]
    public void Pick_FloorsMustMatch_PicksFastest()
    {
        var spots = new List<Spot>
        {
            new Spot(new P(5, 52f, 0), new P(6, 48.7f, 0)),     // 다른 층(3층 → 2층)
            new Spot(new P(-20, 48.7f, 0), new P(-20, 42.6f, 0)),
            new Spot(new P(4, 48.7f, 1), new P(5, 42.6f, 1)),
        };
        Assert.True(DropShortcut.Pick(spots, new P(0, 48.7f, 0), new P(10, 42.6f, 0), 60f, 6f, out Spot s));
        Assert.Equal(4f, s.Edge.X);
    }

    [Fact]
    public void Add_DedupeCapForgetNear()
    {
        var d = new DropShortcut(2);
        Assert.True(d.Add(new Spot(new P(0, 48, 0), new P(1, 42, 0))));
        Assert.False(d.Add(new Spot(new P(0.5f, 48, 0), new P(1, 42, 0))));  // 1m 안 같은 층
        Assert.True(d.Add(new Spot(new P(0.5f, 52, 0), new P(1, 48, 0))));   // 위층은 따로
        Assert.True(d.Add(new Spot(new P(10, 48, 0), new P(11, 42, 0))));
        Assert.Equal(2, d.Count);                                             // 상한 2 — 처음 것 지움
        Assert.Equal(1, d.ForgetNear(new P(11, 42, 0), 2f));                   // 착지 근처 건축 바뀜
        Assert.Equal(1, d.Count);
    }
}
