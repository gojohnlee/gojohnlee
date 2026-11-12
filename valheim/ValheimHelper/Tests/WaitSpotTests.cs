using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;

public class WaitSpotTests
{
    private static WaitSpot.Cand C(string name, int threat, int crowd, float dist) =>
        new WaitSpot.Cand { Name = name, Threat = threat, Crowd = crowd, Dist = dist };

    [Fact]
    public void NoCandidatesGivesNull()
    {
        Assert.Null(WaitSpot.Pick(new List<WaitSpot.Cand>()));
        Assert.Null(WaitSpot.Pick(null));
    }

    [Fact]
    public void WeakEnemyBeatsCloserCrowdedSpot()
    {
        var pick = WaitSpot.Pick(new List<WaitSpot.Cand>
        {
            C("가까운 곳", 2, 0, 5f),
            C("먼 곳", 1, 3, 80f),
        });
        Assert.Equal("먼 곳", pick.Name);
    }

    [Fact]
    public void LessCrowdedBeatsCloserWhenThreatSame()
    {
        var pick = WaitSpot.Pick(new List<WaitSpot.Cand>
        {
            C("붐빔", 1, 2, 3f),
            C("한가함", 1, 0, 40f),
        });
        Assert.Equal("한가함", pick.Name);
    }

    [Fact]
    public void CloserWinsWhenThreatAndCrowdSame()
    {
        var pick = WaitSpot.Pick(new List<WaitSpot.Cand>
        {
            C("멀리", 0, 1, 30f),
            C("가까이", 0, 1, 6f),
        });
        Assert.Equal("가까이", pick.Name);
    }

    [Fact]
    public void FirstInListWinsFullTie()
    {
        var pick = WaitSpot.Pick(new List<WaitSpot.Cand>
        {
            C("첫째", 0, 0, 10f),
            C("둘째", 0, 0, 10f),
        });
        Assert.Equal("첫째", pick.Name);
    }

    [Fact]
    public void StrongEnemySpotIsNeverPicked()
    {
        var pick = WaitSpot.Pick(new List<WaitSpot.Cand>
        {
            C("강한 적", WaitSpot.TooStrong, 0, 1f),
        });
        Assert.Null(pick);
    }

    [Fact]
    public void NullEntriesAreSkipped()
    {
        var pick = WaitSpot.Pick(new List<WaitSpot.Cand> { null, C("남은 곳", 2, 1, 9f) });
        Assert.Equal("남은 곳", pick.Name);
    }
}
