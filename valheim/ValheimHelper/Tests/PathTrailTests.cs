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
}
