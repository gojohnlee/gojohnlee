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
    public void InBaseSkipsOnSecondFleeFromSameTarget()
    {
        var b = new FleeBook<int>();
        Assert.False(b.Fled(1, true, true));
        Assert.False(b.Fled(2, true, true)); // 다른 목표는 따로 셈
        Assert.True(b.Fled(1, true, true));
    }

    [Fact]
    public void RepeatedBaseFleesAvoidBaseToo()
    {
        var b = new FleeBook<int>();
        b.Fled(1, true, true);
        b.Fled(2, true, true);
        Assert.False(b.AvoidInBase);
        b.Fled(0, false, true); // 목표 없이 거점 안에서 도망도 셈
        Assert.True(b.AvoidInBase);
        Assert.False(b.Fled(0, false, true));
    }
}
