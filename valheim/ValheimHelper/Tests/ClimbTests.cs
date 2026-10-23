using ValheimHelper.Core.Work;
using Xunit;

public class ClimbTests
{
    [Fact]
    public void PitWhenWallsAllAround()
    {
        Assert.True(Climb.IsPit(new[] { 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0 }));
        // 두 방향이 트여 있어도 구덩이(벽에 붙어 서면 반대쪽 재는 곳이 구덩이 안일 때가 있다)
        Assert.True(Climb.IsPit(new[] { 2.0, 2.0, 2.0, 0.1, 0.0, 2.0, 2.0, 2.0 }));
    }

    [Fact]
    public void NotPitOnSlopeOrLowStep()
    {
        // 언덕 밑: 앞쪽만 높다 → 구덩이 아님(길 만들기는 시킬 때만)
        Assert.False(Climb.IsPit(new[] { 3.0, 3.0, 2.0, 0.5, -1.0, -1.0, 0.5, 2.0 }));
        // 낮은 턱으로 둘러싸임 → 뛰어 넘는다
        Assert.False(Climb.IsPit(new[] { 0.8, 0.8, 0.8, 0.8, 0.8, 0.8, 0.8, 0.8 }));
        Assert.False(Climb.IsPit(null));
        Assert.False(Climb.IsPit(new[] { 2.0, 2.0 }));
    }

    [Fact]
    public void CarvesLowestWallInFront()
    {
        Assert.Equal(0, Climb.CarveDir(new[] { 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0 }));
        Assert.Equal(7, Climb.CarveDir(new[] { 2.0, 2.5, 2.0, 2.0, 2.0, 2.0, 2.0, 1.5 }));
        Assert.Equal(2, Climb.CarveDir(new[] { 2.0, 2.5, 1.2, 2.0, 2.0, 2.0, 1.4, 2.5 }));
        // 뒤쪽(3·4·5)이 더 낮아도 앞쪽 반원에서만 고른다
        Assert.Equal(0, Climb.CarveDir(new[] { 2.0, 2.0, 2.0, 0.5, 0.5, 0.5, 2.0, 2.0 }));
        // 거의 같으면 가려는 쪽
        Assert.Equal(0, Climb.CarveDir(new[] { 2.0, 1.98, 2.0, 2.0, 2.0, 2.0, 2.0, 1.97 }));
    }

    [Fact]
    public void FleeingInPitLongEnoughIsRescued()
    {
        Assert.Equal("fleePit", Climb.TrapRescue(true, true, false, Climb.FleeTrapSeconds));
        Assert.Null(Climb.TrapRescue(true, true, false, Climb.FleeTrapSeconds - 1));
    }

    [Fact]
    public void WalkingInPitIsLeftToJumpAndCarve()
    {
        Assert.Null(Climb.TrapRescue(false, true, false, 100));
        Assert.Null(Climb.TrapRescue(true, false, false, 100));
    }

    [Fact]
    public void SwimmingInPlaceIsRescued()
    {
        Assert.Equal("water", Climb.TrapRescue(false, false, true, Climb.SwimTrapSeconds));
        Assert.Null(Climb.TrapRescue(false, false, true, Climb.SwimTrapSeconds - 1));
    }

    static double[] Ring(params double[] h) => h;

    [Fact]
    public void EdgeSpotsNearestFlatTopFirst()
    {
        // 구덩이: 가운데에서 1m 0.5, 2m 2.0(벽), 3m 3.0, 4m 3.2(위, 평평), 5m 3.2
        var deep = Ring(0.5, 2.0, 3.0, 3.2, 3.2, 3.2, 3.2, 3.2);
        var near = Ring(0.5, 2.0, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1); // 2m에서 벌써 위
        var rise = new double[8][];
        for (int d = 0; d < 8; d++) rise[d] = deep;
        rise[3] = near;
        rise[5] = near;
        var e = Climb.EdgeSpots(rise);
        Assert.Equal(8, e.Count);
        // 가장 가까운 3·5번(칸 1) — 같은 거리면 가려는 쪽에 가까운 3번 먼저(3과 5는 둘 다 3번 돌림 → 번호 작은 것)
        Assert.Equal(3, e[0].Dir); Assert.Equal(1, e[0].Step);
        Assert.Equal(5, e[1].Dir);
        // 나머지는 칸 2(3.0 → 3.2 차 0.2): 0번 먼저, 그다음 1·7
        Assert.Equal(0, e[2].Dir); Assert.Equal(2, e[2].Step);
        Assert.Equal(1, e[3].Dir); Assert.Equal(7, e[4].Dir);
    }

    [Fact]
    public void EdgeSpotsSkipsLowAndUnknown()
    {
        var rise = new double[8][];
        for (int d = 0; d < 8; d++) rise[d] = Ring(0.2, 0.4, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5); // 벽이 아님(낮은 턱)
        rise[2] = Ring(double.NaN, 1.5, 1.6, 1.6, 1.6, 1.6, 1.6, 1.6);
        rise[6] = Ring(1.5, 3.0, 4.5, 6.0, 7.5, 9.0, 10.5, 12.0); // 계속 오르는 비탈은 위가 없음
        var e = Climb.EdgeSpots(rise);
        Assert.Single(e);
        Assert.Equal(2, e[0].Dir); Assert.Equal(1, e[0].Step);
        Assert.Empty(Climb.EdgeSpots(null));
    }

    [Fact]
    public void EdgeSpotsSkipsLedgeHalfwayUpWall()
    {
        var rise = new double[8][];
        for (int d = 0; d < 8; d++) rise[d] = Ring(0.5, 1.5, 1.6, 3.0, 3.1, 3.1, 3.1, 3.1); // 1.5m 턱은 벽 중간 → 3m 위
        var e = Climb.EdgeSpots(rise);
        Assert.Equal(8, e.Count);
        Assert.Equal(3, e[0].Step);
        // 비탈 아래쪽 둘레(바깥으로 갈수록 낮아짐)는 그 자리가 가장 높은 데
        rise[4] = Ring(0.5, 1.4, 1.3, 1.0, 0.8, 0.6, 0.4, 0.2);
        Assert.Equal(4, Climb.EdgeSpots(rise)[0].Dir);
    }
}
