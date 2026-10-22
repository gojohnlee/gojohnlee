using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;

public class WorkerPortalTests
{
    static readonly List<PortalCost> Cost = new List<PortalCost>
    {
        new PortalCost("FineWood", 20), new PortalCost("GreydwarfEye", 10), new PortalCost("SurtlingCore", 2),
    };

    [Fact]
    public void TagHasPrefixAndIsCut()
    {
        Assert.Equal("일꾼-구리", WorkerPortal.Tag(" 구리 "));
        Assert.Equal("일꾼-일터", WorkerPortal.Tag(""));
        Assert.Equal(WorkerPortal.MaxTagLength, WorkerPortal.Tag(new string('가', 50)).Length);
        Assert.True(WorkerPortal.IsWorkerTag("일꾼-구리"));
        Assert.False(WorkerPortal.IsWorkerTag("집"));
        Assert.False(WorkerPortal.IsWorkerTag(null));
    }

    [Fact]
    public void PairsCountDistinctWorkerTags()
    {
        Assert.Equal(2, WorkerPortal.Pairs(new[] { "일꾼-a", "일꾼-a", "일꾼-b", "집", "" }));
        Assert.True(WorkerPortal.CanAddPair(2));
        Assert.False(WorkerPortal.CanAddPair(3));
    }

    [Fact]
    public void ShouldPlaceOnlyFarSecondVisitWithoutLink()
    {
        Assert.False(WorkerPortal.ShouldPlace(250, 5, false));
        Assert.False(WorkerPortal.ShouldPlace(400, 1, false));
        Assert.False(WorkerPortal.ShouldPlace(400, 2, true));
        Assert.True(WorkerPortal.ShouldPlace(400, 2, false));
    }

    [Fact]
    public void IdleAfterDays()
    {
        Assert.False(WorkerPortal.IsIdle(10, 14));
        Assert.True(WorkerPortal.IsIdle(10, 15));
    }

    [Fact]
    public void NeedDoublesForPairAndMergesLines()
    {
        var cost = new List<PortalCost>(Cost) { new PortalCost("FineWood", 1), new PortalCost("", 3), new PortalCost("Stone", 0) };
        var need = WorkerPortal.Need(cost, 2);
        Assert.Equal(42, need["FineWood"]);
        Assert.Equal(20, need["GreydwarfEye"]);
        Assert.Equal(4, need["SurtlingCore"]);
        Assert.Equal(3, need.Count);
    }

    [Fact]
    public void MissingListsShortItemsOnly()
    {
        var have = new Dictionary<string, int> { ["FineWood"] = 50, ["GreydwarfEye"] = 20, ["SurtlingCore"] = 1 };
        var miss = WorkerPortal.Missing(Cost, k => have.TryGetValue(k, out int n) ? n : 0, 2);
        Assert.Single(miss);
        Assert.Equal(3, miss["SurtlingCore"]);
        Assert.Empty(WorkerPortal.Missing(Cost, k => 999, 2));
    }
}
