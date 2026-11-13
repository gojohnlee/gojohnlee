using System.Collections.Generic;
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

    [Fact]
    public void CheapestFirstKeepsChestOrderForTies()
    {
        var costs = new Dictionary<string, int> { ["BoltIron"] = 5, ["BoltBone"] = 3, ["BoltSilver"] = 5, ["BoltBlackmetal"] = 9 };
        var order = AmmoRule.CheapestFirst(new[] { "BoltIron", "BoltBlackmetal", "BoltBone", "BoltSilver" }, n => costs[n]);
        Assert.Equal(new[] { "BoltBone", "BoltIron", "BoltSilver", "BoltBlackmetal" }, order);
    }
}
