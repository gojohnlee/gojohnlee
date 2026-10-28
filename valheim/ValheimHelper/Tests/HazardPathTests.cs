using System;
using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;
using P = ValheimHelper.Core.Work.RoughPath.P;

public class HazardPathTests
{
    static bool Pool(double x, double z) => (x - 30) * (x - 30) + z * z < 15 * 15; // (30,0) 둘레 15m 용암

    static void AssertClear(System.Collections.Generic.List<P> path, Func<double, double, bool> blocked)
    {
        for (int i = 0; i + 1 < path.Count; i++) Assert.True(HazardPath.Clear(path[i], path[i + 1], blocked), $"{i}번째 구간이 막힘");
    }

    [Fact]
    public void OpenGroundIsStraight()
    {
        var path = HazardPath.Find(new P(0, 0), new P(60, 0), (x, z) => false);
        Assert.NotNull(path);
        Assert.Equal(2, path.Count);
    }

    [Fact]
    public void GoesAroundPool()
    {
        var path = HazardPath.Find(new P(0, 1), new P(60, 1), Pool);
        Assert.NotNull(path);
        AssertClear(path, Pool);
        Assert.Equal(60, path[path.Count - 1].X);
        double len = 0;
        for (int i = 0; i + 1 < path.Count; i++) len += Math.Sqrt(Math.Pow(path[i + 1].X - path[i].X, 2) + Math.Pow(path[i + 1].Z - path[i].Z, 2));
        Assert.InRange(len, 60, 85); // 반원(약 47m + 30m)쯤, 가장자리를 더듬지 않음
    }

    [Fact]
    public void BlockedGoalIsNull()
    {
        Assert.Null(HazardPath.Find(new P(0, 0), new P(30, 0), Pool));
    }

    [Fact]
    public void StartOnEdgeStillLeaves()
    {
        var path = HazardPath.Find(new P(15.5, 0), new P(-10, 0), Pool);
        Assert.NotNull(path);
    }

    [Fact]
    public void WalledInIsNull()
    {
        // 둘레를 다 막은 고리 안에서 시작
        bool Ring(double x, double z) { double r = Math.Sqrt(x * x + z * z); return r > 8 && r < 14; }
        Assert.Null(HazardPath.Find(new P(0, 0), new P(40, 0), Ring));
    }

    [Fact]
    public void NoCornerCutting()
    {
        // 얇은 벽 x=10 (z<20), 대각선으로 벽 끝을 스치지 않음
        bool Wall(double x, double z) => x >= 9 && x < 11 && z < 20;
        var path = HazardPath.Find(new P(0, 0), new P(20, 0), Wall);
        Assert.NotNull(path);
        AssertClear(path, Wall);
        Assert.Contains(path, p => p.Z >= 20);
    }
}
