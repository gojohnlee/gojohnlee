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
}
