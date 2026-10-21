using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;

public class ExploredSweepTests
{
    [Fact]
    public void NearestFirstWithinBudget()
    {
        var s = new ExploredSweep(3);
        var homes = new List<ZoneXY> { new ZoneXY(10, 10) };
        var absorbed = new List<ZoneXY>();
        int n = s.Step(homes, z => true, z => { absorbed.Add(z); return true; }, 100, 5);
        Assert.Equal(5, n);
        Assert.Equal(new ZoneXY(10, 10), absorbed[0]); // 거점 구역부터
        for (int i = 1; i < 5; i++) Assert.Equal(1, absorbed[i].DistSq(new ZoneXY(10, 10))); // 그다음 바로 옆
        Assert.Equal(5, s.Done);
    }

    [Fact]
    public void SkipsUnexploredAndRetriesUngenerated()
    {
        var s = new ExploredSweep(2);
        var homes = new List<ZoneXY> { new ZoneXY(0, 0) };
        var explored = new HashSet<ZoneXY> { new ZoneXY(1, 0), new ZoneXY(-2, 2) };
        var generated = new HashSet<ZoneXY> { new ZoneXY(1, 0) };
        int n = s.Step(homes, explored.Contains, generated.Contains, 1000, 10);
        Assert.Equal(1, n);
        Assert.True(s.IsDone(new ZoneXY(1, 0)));
        Assert.False(s.IsDone(new ZoneXY(-2, 2))); // 밝혀졌지만 생성 안 됨 → 모름 그대로
        Assert.False(s.IsDone(new ZoneXY(0, 0))); // 안 밝혀짐
        generated.Add(new ZoneXY(-2, 2)); // 나중에 생성되면 다음 바퀴에 넣는다
        Assert.Equal(1, s.Step(homes, explored.Contains, generated.Contains, 1000, 10));
        Assert.True(s.IsDone(new ZoneXY(-2, 2)));
        Assert.Equal(0, s.Step(homes, explored.Contains, generated.Contains, 1000, 10)); // 넣은 건 다시 안 넣음
    }

    [Fact]
    public void CheckBudgetSpreadsOverSteps()
    {
        var s = new ExploredSweep(2); // 5×5 = 25구역
        var homes = new List<ZoneXY> { new ZoneXY(0, 0) };
        int total = 0, steps = 0;
        while (total < 25 && steps < 100) { total += s.Step(homes, z => true, z => true, 4, 100); steps++; }
        Assert.Equal(25, total);
        Assert.Equal(7, steps); // 4개씩 확인 → 7걸음
    }

    [Fact]
    public void MovingHomeRebuildsAndKeepsDone()
    {
        var s = new ExploredSweep(1);
        s.Step(new List<ZoneXY> { new ZoneXY(0, 0) }, z => true, z => true, 100, 100);
        Assert.Equal(9, s.Done);
        int n = s.Step(new List<ZoneXY> { new ZoneXY(2, 0) }, z => true, z => true, 100, 100);
        Assert.Equal(6, n); // 겹치는 3구역은 이미 넣음
        s.Reset();
        Assert.Equal(0, s.Done);
        Assert.Equal(0, s.Step(new List<ZoneXY>(), z => true, z => true, 100, 100));
    }

    [Fact]
    public void SamplePointsInsideZone()
    {
        var pts = ExploredSweep.SamplePoints(64f);
        Assert.Equal(5, pts.Length);
        Assert.Equal(new[] { 0f, 0f }, pts[0]);
        foreach (var p in pts) { Assert.InRange(p[0], -31.9f, 31.9f); Assert.InRange(p[1], -31.9f, 31.9f); }
    }
}
