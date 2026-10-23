using ValheimHelper.Core.Work;
using Xunit;

public class NightSettleTests
{
    [Fact]
    public void EdgesOnlyOnChange()
    {
        var s = new NightSettle();
        Assert.Equal(NightSettle.Edge.None, s.Step(false));
        Assert.Equal(NightSettle.Edge.Started, s.Step(true));
        Assert.Equal(NightSettle.Edge.None, s.Step(true));
        Assert.Equal(NightSettle.Edge.Ended, s.Step(false));
        Assert.Equal(NightSettle.Edge.None, s.Step(false));
    }

    [Fact]
    public void OncePerMorning()
    {
        Assert.True(NightSettle.Due(5, -1));
        Assert.True(NightSettle.Due(5, 4));
        Assert.False(NightSettle.Due(5, 5));
        Assert.False(NightSettle.Due(4, 5));
    }

    [Fact]
    public void BaseRadius()
    {
        Assert.True(NightSettle.AtBase(30f));
        Assert.False(NightSettle.AtBase(30.5f));
    }

    [Fact]
    public void FarmRange()
    {
        Assert.True(NightSettle.FarmNear(45f, 3f));
        Assert.True(NightSettle.FarmNear(110f, 10f));
        Assert.False(NightSettle.FarmNear(110.5f, 10f));
    }
}
