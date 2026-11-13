using ValheimHelper.Core.Work;
using Xunit;

public class AmmoRuleTests
{    [Fact]
    public void FeatherArrowsNeedMoreThanFiftyFeathers()
    {
        Assert.False(AmmoRule.MayTakeFeatherArrows(0));
        Assert.False(AmmoRule.MayTakeFeatherArrows(50));
        Assert.True(AmmoRule.MayTakeFeatherArrows(51));
    }
}
