using ValheimHelper.Core.Work;
using Xunit;

public class PathLegTests
{
    [Fact]
    public void NearGoalNeverUsesLeg()
    {
        var l = new PathLeg();
        Assert.False(l.DirectFailed(0, 0, 0, PathLeg.MinDistance - 1, 0));
        Assert.False(l.Active);
        Assert.False(l.Keep(0.1, 0, 0, PathLeg.MinDistance - 1, 0));
    }

    [Fact]
    public void FarGoalLegPointsTowardGoalAtLength()
    {
        var l = new PathLeg();
        Assert.True(l.DirectFailed(0, 10, 20, 10, 220));
        Assert.True(l.Active);
        Assert.Equal(10, l.X, 3);
        Assert.Equal(20 + PathLeg.Length, l.Z, 3);
        Assert.Equal(1, l.Picks);
    }

    [Fact]
    public void LegStaysFixedUntilCloseThenAdvances()
    {
        var l = new PathLeg();
        l.DirectFailed(0, 0, 0, 300, 0);
        // 옆으로 돌아가는 중(문 쪽): 토막 점 그대로 — 남은 길(토막까지 + 토막~목표)이 길 따라 줄어들게
        Assert.True(l.Keep(1, -5, 10, 300, 0));
        Assert.Equal(PathLeg.Length, l.X, 3);
        Assert.Equal(1, l.Picks);
        // 토막 점 Next 안에 오면 다음 토막
        Assert.True(l.Keep(2, PathLeg.Length - PathLeg.Next + 1, 0, 300, 0));
        Assert.Equal(2, l.Picks);
        Assert.Equal(PathLeg.Length - PathLeg.Next + 1 + PathLeg.Length, l.X, 3);
    }

    [Fact]
    public void RepicksAfterTeleportOrPassingLeg()
    {
        var l = new PathLeg();
        l.DirectFailed(0, 0, 0, 300, 0);
        // 순간이동으로 토막 점보다 앞(목표 쪽)으로 옮겨짐 → 뒤로 돌아가지 않고 새 토막
        Assert.True(l.Keep(1, 40, 0, 300, 0));
        Assert.Equal(2, l.Picks);
        Assert.Equal(40 + PathLeg.Length, l.X, 3);
        // 옆으로 멀리 옮겨짐(토막 점에서 Length + 2×Next 밖) → 새 토막
        Assert.True(l.Keep(2, 40, 60, 300, 0));
        Assert.Equal(3, l.Picks);
    }

    [Fact]
    public void RetriesDirectPathAfterRetrySeconds()
    {
        var l = new PathLeg();
        l.DirectFailed(10, 0, 0, 300, 0);
        Assert.True(l.Keep(10 + PathLeg.Retry - 0.1, 3, 0, 300, 0));
        Assert.False(l.Keep(10 + PathLeg.Retry, 3, 0, 300, 0));
        Assert.False(l.Active);
        // 다시 실패하면 그 자리에서 새 토막
        Assert.True(l.DirectFailed(15, 3, 0, 300, 0));
        Assert.Equal(3 + PathLeg.Length, l.X, 3);
    }

    [Fact]
    public void EndsWhenGoalGetsClose()
    {
        var l = new PathLeg();
        l.DirectFailed(0, 0, 0, 100, 0);
        Assert.False(l.Keep(1, 100 - PathLeg.MinDistance, 0, 100, 0));
        Assert.False(l.Active);
    }
}
