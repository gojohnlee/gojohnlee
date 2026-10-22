using ValheimHelper.Core.Work;
using Xunit;

public class HiddenOreTests
{
    [Fact]
    public void TopAboveGroundIsExposed()
    {
        Assert.True(HiddenOre.Exposed(10.5f, 10f));
        Assert.False(HiddenOre.Exposed(10.2f, 10f));
        Assert.False(HiddenOre.Exposed(8f, 10f));
    }

    [Fact]
    public void BuriedHiddenNeedsFinder()
    {
        Assert.False(HiddenOre.Known(hidden: true, exposed: false, haveFinder: false));
        Assert.True(HiddenOre.Known(hidden: true, exposed: false, haveFinder: true));
        Assert.True(HiddenOre.Known(hidden: true, exposed: true, haveFinder: false));
        Assert.True(HiddenOre.Known(hidden: false, exposed: false, haveFinder: false));
    }

    [Fact]
    public void ExposedComesBeforeNearerBuried()
    {
        float buried = HiddenOre.Score(5f, 10f, hidden: true, exposed: false);
        float exposed = HiddenOre.Score(40f, 10f, hidden: true, exposed: true);
        Assert.True(exposed < buried);
    }

    [Fact]
    public void OrdinaryOreScoreIsDistanceMinusPriority()
    {
        Assert.Equal(20f, HiddenOre.Score(30f, 10f, hidden: false, exposed: false));
    }
}
