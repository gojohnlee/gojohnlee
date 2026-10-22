using System;
using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;
using XZ = ValheimHelper.Core.Work.StuckHop.XZ;

public class StuckHopTests
{
    [Fact]
    public void FarFromPlayersUses64m()
    {
        var players = new[] { new XZ(0, 0) };
        Assert.False(StuckHop.FarFromPlayers(63.9, 0, players));
        Assert.True(StuckHop.FarFromPlayers(64.1, 0, players));
        Assert.False(StuckHop.FarFromPlayers(200, 0, new[] { new XZ(300, 0), new XZ(150, 0) })); // 가장 가까운 플레이어 기준
        Assert.True(StuckHop.FarFromPlayers(0, 0, new XZ[0])); // 플레이어 없음
    }

    [Fact]
    public void CandidatesNearestRadiusFirstFacingWorker()
    {
        var c = StuckHop.Candidates(100, 0, 0, 0).ToList();
        Assert.Equal(StuckHop.Radii.Length * StuckHop.Directions, c.Count);
        // 첫 후보: 3m, 일꾼 쪽(+x)
        Assert.Equal(3.0, c[0].X, 6);
        Assert.Equal(0.0, c[0].Z, 6);
        double Dist(XZ p) => Math.Sqrt(p.X * p.X + p.Z * p.Z);
        Assert.All(c.Take(StuckHop.Directions), p => Assert.Equal(3.0, Dist(p), 6));
        Assert.All(c.Skip(2 * StuckHop.Directions), p => Assert.Equal(8.0, Dist(p), 6));
        // 같은 거리에선 일꾼 반대쪽(-x)이 마지막
        Assert.Equal(-3.0, c[StuckHop.Directions - 1].X, 6);
        // 한 거리 안 방향이 모두 다름
        Assert.Equal(StuckHop.Directions, c.Take(StuckHop.Directions).Select(p => (Math.Round(p.X, 3), Math.Round(p.Z, 3))).Distinct().Count());
    }

    [Fact]
    public void CandidatesWhenOnTopOfTarget()
    {
        var c = StuckHop.Candidates(5, 5, 5, 5).First();
        Assert.Equal(8.0, c.X, 6);
        Assert.Equal(5.0, c.Z, 6);
    }
}
