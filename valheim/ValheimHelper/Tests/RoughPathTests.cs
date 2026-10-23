using System;
using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;
using P = ValheimHelper.Core.Work.RoughPath.P;

public class RoughPathTests
{
    const double Water = 30;
    static bool All(double x, double z) => true;

    [Fact]
    public void FlatGoesNearlyStraight()
    {
        var path = RoughPath.Find(new P(0, 0), new P(200, 0), (x, z) => 40, Water, All);
        Assert.NotNull(path);
        Assert.Equal(0, path[0].X);
        Assert.Equal(200, path[path.Count - 1].X);
        Assert.InRange(RoughPath.Length(path), 200, 215);
    }

    [Fact]
    public void GoesAroundDeepLake()
    {
        // x 80~120, z -100~60 호수(물 아래 5m) → 둘러 간다
        double H(double x, double z) => x > 80 && x < 120 && z > -100 && z < 60 ? 25 : 40;
        var path = RoughPath.Find(new P(0, 0), new P(200, 0), H, Water, All);
        Assert.NotNull(path);
        Assert.DoesNotContain(path.Skip(1).Take(path.Count - 2), p => H(p.X, p.Z) < Water - WaterRule.DeepDepth);
        Assert.True(path.Any(p => p.Z >= 60), "북쪽으로 돌아감");
    }

    [Fact]
    public void NarrowChannelBetweenCellCentersIsNotCrossed()
    {
        // x 101~103 좁은 깊은 물길(z < 60): 칸 중심(x 100·108)은 마른 땅이라 중심만 보면 건너 버림 → 칸 사이 점도 봐야 둘러 감
        double H(double x, double z) => x > 101 && x < 103 && z < 60 ? 20 : 40;
        var path = RoughPath.Find(new P(4, 4), new P(196, 4), H, Water, All);
        Assert.NotNull(path);
        double len = RoughPath.Length(path);
        for (double t = 0; t <= len; t += 1)
        {
            var q = RoughPath.PointAt(path, t);
            Assert.False(H(q.X, q.Z) < Water - WaterRule.DeepDepth, $"깊은 물 지남 {q}");
        }
    }

    [Fact]
    public void LeavesShoreWhoseCellCenterIsDeep()
    {
        // 출발점(6.5,4)은 마른 물가지만 그 칸 중심(4,4)은 깊은 물(x < 5) → 그래도 떠난다
        double H(double x, double z) => x < 5 ? 20 : 40;
        var path = RoughPath.Find(new P(6.5, 4), new P(150, 4), H, Water, All);
        Assert.NotNull(path);
        Assert.InRange(RoughPath.Length(path), 143, 160);
    }

    [Fact]
    public void ShallowWaterIsFine()
    {
        double H(double x, double z) => x > 80 && x < 120 ? 29.5 : 40; // 얕은 여울
        var path = RoughPath.Find(new P(0, 0), new P(200, 0), H, Water, All);
        Assert.InRange(RoughPath.Length(path), 200, 215);
    }

    [Fact]
    public void AvoidsCliff()
    {
        // x=100에서 40m 절벽, z>=40에만 완만한 비탈(찾는 범위 64m 안)
        double H(double x, double z) => z >= 40 ? Math.Min(80, Math.Max(40, 40 + (x - 80) * 0.5)) : (x < 100 ? 40 : 80);
        var path = RoughPath.Find(new P(0, 0), new P(200, 0), H, Water, All);
        Assert.NotNull(path);
        Assert.True(path.Any(p => p.Z >= 40), "비탈 쪽으로 돌아감");
    }

    [Fact]
    public void PrefersExplored()
    {
        // 곧은 길은 안 밝혀짐, z 40~56 띠만 밝혀짐 → 조금 돌아도 밝혀진 띠로
        var path = RoughPath.Find(new P(0, 0), new P(400, 0), (x, z) => 40, Water, (x, z) => z > 40 && z < 56);
        Assert.NotNull(path);
        Assert.True(path.Count(p => p.Z > 40 && p.Z < 56) > path.Count / 2);
    }

    [Fact]
    public void NoPathGivesNullAndRouteFallsBackToStraight()
    {
        // 도착점이 깊은 바다 한가운데 섬 → 길 없음
        double H(double x, double z) => x > 50 && x < 180 ? 0 : 40;
        Assert.Null(RoughPath.Find(new P(0, 0), new P(200, 0), H, Water, All));
        var route = RoughPath.Route(new P(0, 0), new P(200, 0), true, H, Water, All);
        Assert.Equal(2, route.Count);
        Assert.Equal(200, RoughPath.Length(route), 3);
    }

    [Fact]
    public void ExpandLimitGivesNull()
    {
        Assert.Null(RoughPath.Find(new P(0, 0), new P(2000, 0), (x, z) => 40, Water, (x, z) => false, maxExpand: 10));
    }

    [Fact]
    public void UnexploredGoalIsStraight()
    {
        double H(double x, double z) => x > 80 && x < 120 && z > -100 && z < 60 ? 25 : 40;
        var route = RoughPath.Route(new P(0, 0), new P(200, 0), false, H, Water, All);
        Assert.Equal(2, route.Count);
    }

    [Fact]
    public void PointAtWalksAlongPath()
    {
        var path = new[] { new P(0, 0), new P(10, 0), new P(10, 10) }.ToList();
        Assert.Equal(20, RoughPath.Length(path), 6);
        var a = RoughPath.PointAt(path, 5);
        Assert.Equal(5, a.X, 6); Assert.Equal(0, a.Z, 6);
        var b = RoughPath.PointAt(path, 15);
        Assert.Equal(10, b.X, 6); Assert.Equal(5, b.Z, 6);
        Assert.Equal(10, RoughPath.PointAt(path, 99).Z, 6);
        Assert.Equal(0, RoughPath.PointAt(path, -1).X, 6);
    }
}
