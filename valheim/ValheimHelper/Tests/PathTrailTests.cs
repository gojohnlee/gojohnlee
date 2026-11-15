using ValheimHelper.Core.Work;
using Xunit;
using P = ValheimHelper.Core.Work.PathMemory.P;

public class PathTrailTests
{
    [Fact]
    public void Walk_KeepsPointsEveryHalfMeter_AndEndsAtTarget()
    {
        var t = new PathTrail();
        t.Start(new P(0, 0, 0));
        for (int i = 1; i <= 50; i++) t.Step(new P(i * 0.1f, 0, 0));   // 5m를 0.1m씩
        var r = t.Finish(new P(5.05f, 0, 0));
        Assert.NotNull(r);
        Assert.InRange(r.Length, 10, 12);
        Assert.Equal(5.05f, r[r.Length - 1].X, 2);
        Assert.False(t.Active);
    }

    [Fact]
    public void Teleport_DropsTrail()
    {
        var t = new PathTrail();
        t.Start(new P(0, 0, 0));
        t.Step(new P(1, 0, 0));
        t.Step(new P(6, 0, 0));   // 5m 튐
        Assert.Null(t.Finish(new P(7, 0, 0)));
    }

    [Fact]
    public void ShortRoute_NotWorthIt()
    {
        var t = new PathTrail();
        t.Start(new P(0, 0, 0));
        t.Step(new P(1, 0, 0));
        Assert.Null(t.Finish(new P(2, 0, 0)));
    }

    [Fact]
    public void TooManyPoints_DropsTrail()
    {
        var t = new PathTrail();
        t.Start(new P(0, 0, 0));
        for (int i = 1; i <= PathTrail.MaxPoints + 5; i++) t.Step(new P(i * 0.6f, 0, 0));
        Assert.Null(t.Finish(new P((PathTrail.MaxPoints + 5) * 0.6f, 0, 0)));
    }

    [Fact]
    public void NotStarted_Null()
    {
        Assert.Null(new PathTrail().Finish(new P(5, 0, 0)));
    }

    [Fact]
    public void Spoiled_DropsTrail_KeepsReason()
    {
        var t = new PathTrail();
        t.Start(new P(0, 0, 0));
        for (int i = 1; i <= 8; i++) t.Step(new P(i * 0.6f, 0, 0));
        t.Spoil("피해");
        t.Spoil("막힘");
        Assert.Null(t.Finish(new P(5, 0, 0)));
        Assert.Equal("피해", t.SpoilReason);
        t.Start(new P(0, 0, 0));
        Assert.Null(t.SpoilReason);
    }

    [Fact]
    public void BackTrack_DropsTrail()
    {
        var t = new PathTrail();
        t.Start(new P(0, 0, 0));
        for (int i = 1; i <= 6; i++) t.Step(new P(i * 0.6f, 0, 0));   // 3.6m 나갔다
        for (int i = 5; i >= 1; i--) t.Step(new P(i * 0.6f, 0, 0.1f)); // 되돌아옴
        for (int i = 1; i <= 10; i++) t.Step(new P(-i * 0.6f, 0, 0));
        Assert.Null(t.Finish(new P(-6, 0, 0)));
        Assert.Equal("되돌아감", t.SpoilReason);
    }

    [Fact]
    public void LongDetour_SameFloor_DropsTrail()
    {
        var t = new PathTrail();
        t.Start(new P(0, 0, 0));
        for (int i = 1; i <= 6; i++) t.Step(new P(0, 0, i * 0.6f));      // 옆으로 3.6m
        for (int i = 1; i <= 8; i++) t.Step(new P(i * 0.6f, 0, 3.6f));
        for (int i = 5; i >= 0; i--) t.Step(new P(4.8f, 0, i * 0.6f));
        Assert.Null(t.Finish(new P(5, 0, 0)));
        Assert.StartsWith("길/직선", t.SpoilReason);
    }

    [Fact]
    public void LongDetour_OtherFloor_Kept()
    {
        var t = new PathTrail();
        t.Start(new P(0, 0, 0));
        for (int i = 1; i <= 10; i++) t.Step(new P(0, i * 0.2f, i * 0.6f)); // 계단 돌아 오름
        for (int i = 1; i <= 8; i++) t.Step(new P(i * 0.6f, 2, 6));
        for (int i = 9; i >= 0; i--) t.Step(new P(4.8f, 3, i * 0.6f));
        Assert.NotNull(t.Finish(new P(5, 3, 0)));
        Assert.Null(t.SpoilReason);
    }
}
