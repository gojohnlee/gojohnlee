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

    [Fact]
    public void NearCandidatesAheadTowardTarget()
    {
        var c = StuckHop.NearCandidates(0, 0, 40, 0).ToList();
        Assert.InRange(c.Count, 10, StuckHop.NearSteps.Length * StuckHop.NearAngles.Length); // 4m 앞 ±60°는 2m도 안 다가가 빠짐
        // 첫 후보: 곧장 4m 앞
        Assert.Equal(4.0, c[0].X, 6);
        Assert.Equal(0.0, c[0].Z, 6);
        // 모두 일꾼에서 8m 안, 목표에 2m 넘게 다가감
        Assert.All(c, p => Assert.True(Math.Sqrt(p.X * p.X + p.Z * p.Z) <= 8.0 + 1e-6));
        Assert.All(c, p => Assert.True(Math.Sqrt((40 - p.X) * (40 - p.X) + p.Z * p.Z) <= 38.0));
    }

    [Fact]
    public void NearCandidatesDoNotOvershootTarget()
    {
        // 목표 6m: 4m 앞만(6m·8m는 목표를 지나침)
        var c = StuckHop.NearCandidates(0, 0, 6, 0).ToList();
        Assert.NotEmpty(c);
        Assert.All(c, p => Assert.True(Math.Sqrt(p.X * p.X + p.Z * p.Z) <= 4.0 + 1e-6));
        // 목표 3m: 앞쪽 자리 없음 → 목표 둘레(일꾼 10m 안)
        var ring = StuckHop.NearCandidates(0, 0, 3, 0).ToList();
        Assert.NotEmpty(ring);
        Assert.All(ring, p => Assert.InRange(p.X * p.X + p.Z * p.Z, 1.5 * 1.5, 100.0 + 1e-6));
    }
    [Fact]
    public void MachineSidePointsGoAroundThenBehind()
    {
        // 일꾼 (10,0), 설비 (0,0): 옆 둘(90°·-90°) 다음 뒤(180°), 모두 설비에서 2.2m
        var a = StuckHop.Side(10, 0, 0, 0, 2.2, 0);
        var b = StuckHop.Side(10, 0, 0, 0, 2.2, 1);
        var c = StuckHop.Side(10, 0, 0, 0, 2.2, 2);
        Assert.Equal(0, a.X, 6); Assert.Equal(2.2, a.Z, 6);
        Assert.Equal(0, b.X, 6); Assert.Equal(-2.2, b.Z, 6);
        Assert.Equal(-2.2, c.X, 6); Assert.Equal(0, c.Z, 6);
    }

    [Fact]
    public void MachineLandingStaysInsideWorkRange()
    {
        var ring = StuckHop.Candidates(10, 0, 0, 0, StuckHop.MachineRadii).ToList();
        Assert.Equal(StuckHop.MachineRadii.Length * StuckHop.Directions, ring.Count);
        Assert.Equal(2.2, ring[0].X, 6); // 오던 쪽부터
        Assert.All(ring, p => Assert.True(Math.Sqrt(p.X * p.X + p.Z * p.Z) < 2.5)); // MachineStep 설비 앞 일 거리 2.5m 안
        Assert.Equal(StuckHop.Radii.Length * StuckHop.Directions, StuckHop.Candidates(10, 0, 0, 0).Count()); // 기본은 그대로
    }

    [Fact]
    public void PlausibleDropsBehindAndPrefersUnseenInRange()
    {
        // 사용자 11-12 예: 플레이어 (0,0)이 +x를 봄, 일꾼은 (1,0)에서 보인 뒤 (-1,0)으로 감, 목표 (-6,0)
        var cands = new[] { new XZ(2, 0), new XZ(-3, 0), new XZ(-5, 1), new XZ(-30, 0) };
        Func<XZ, bool> seen = c => c.X > 0; // 앞(+x)만 보임
        var got = StuckHop.Plausible(cands, -1, 0, -6, 0, new XZ(1, 0), 1.0, 5.0, seen);
        Assert.DoesNotContain(got, c => c.X > 0); // 지나온 쪽·앞쪽 시야는 빠짐
        Assert.Equal(new[] { -3.0, -5.0, -30.0 }, got.Select(c => c.X)); // 범위(1초×5+4.5=9.5m) 안 먼저, 밖은 안 보이면 맨 뒤
        Assert.DoesNotContain(StuckHop.Plausible(new[] { new XZ(-30, 0) }, -1, 0, -6, 0, new XZ(1, 0), 1.0, 5.0, c => true), c => true); // 보이는 범위 밖은 뺌
    }

    [Fact]
    public void PlausibleOrdersUnseenBeforeSeenAndKeepsOrderWithoutSighting()
    {
        var cands = new[] { new XZ(4, 0), new XZ(4, 2), new XZ(6, 0) };
        var got = StuckHop.Plausible(cands, 0, 0, 10, 0, new XZ(0, 0), 0, 5, c => c.Z == 0);
        Assert.Equal(new[] { 2.0, 0.0 }, got.Select(c => c.Z)); // 보는 중(0초): 여유 4.5m 안, 안 보임 먼저, 6m는 보여서 빠짐
        Assert.Equal(3, StuckHop.Plausible(cands, 0, 0, 10, 0, null, 0, 5, null).Count); // 본 적 없음: 앞쪽 전부 그대로
    }
}
