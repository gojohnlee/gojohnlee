using ValheimHelper.Core.Work;
using Xunit;

public class FleeBookTests
{
    [Fact]
    public void OutsideBaseSkipsOnFirstFlee()
    {
        var b = new FleeBook<int>();
        Assert.True(b.Fled(1, true, false));
        Assert.False(b.AvoidInBase);
    }

    [Fact]
    public void InBaseSkipsOnFirstFleeAndAvoidsBaseAfter()
    {
        var b = new FleeBook<int>();
        Assert.False(b.AvoidInBase);
        Assert.True(b.Fled(1, true, true)); // 사용자 10-28: 거점 안도 첫 도망에 그 목표는 둔다
        Assert.True(b.AvoidInBase);
    }

    [Fact]
    public void NoTargetFleeInBaseStillCounts()
    {
        var b = new FleeBook<int>();
        Assert.False(b.Fled(0, false, true));
        Assert.True(b.AvoidInBase);
    }
}
