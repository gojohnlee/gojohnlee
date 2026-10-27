using System;
using System.Collections.Generic;
using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;
using Cell = ValheimHelper.Core.Work.StepGrid.Cell;

public class StepGridTests
{
    // 10x10 칸, 높이는 표에서(null = 바닥 없음). 벽은 walls에 (a,b) 쌍으로.
    static List<Cell> Run(Func<int, int, double?> floor, Cell goal, HashSet<string> walls = null, int maxExpand = 1000)
    {
        bool Open(Cell a, Cell b) => walls == null || (!walls.Contains($"{a}{b}") && !walls.Contains($"{b}{a}"));
        double? Floor(int x, int z) => x < 0 || z < 0 || x > 9 || z > 9 ? null : floor(x, z);
        return StepGrid.Find(new Cell(0, 0), floor(0, 0) ?? 0, c => c.Equals(goal), goal, Floor, Open, maxExpand);
    }

    [Fact]
    public void FlatGoesStraight()
    {
        var path = Run((x, z) => 0, new Cell(5, 0));
        Assert.NotNull(path);
        Assert.Equal(6, path.Count);
        Assert.All(path, c => Assert.Equal(0, c.Z));
    }

    [Fact]
    public void WallMakesDetourThroughGap()
    {
        // x=2 과 x=3 사이 벽, z=4 에만 틈
        var walls = new HashSet<string>();
        for (int z = 0; z <= 9; z++)
            if (z != 4) for (int dz = -1; dz <= 1; dz++) walls.Add($"{new Cell(2, z)}{new Cell(3, z + dz)}");
        var path = Run((x, z) => 0, new Cell(5, 0), walls);
        Assert.NotNull(path);
        int i = path.FindIndex(c => c.X == 3);
        Assert.Equal(new Cell(2, 4), path[i - 1]); // 틈(z=4)으로만 건넌다
    }

    [Fact]
    public void JumpUpToMaxUpButNotHigher()
    {
        Assert.NotNull(Run((x, z) => x >= 3 ? 0.9 : 0, new Cell(5, 0)));   // 0.9m 턱: 뛰어 오름
        Assert.Null(Run((x, z) => x >= 3 ? 1.2 : 0, new Cell(5, 0)));      // 1.2m: 못 오름(둘러 갈 곳 없음)
    }

    [Fact]
    public void DropDownToMaxDropButNotDeeper()
    {
        Assert.NotNull(Run((x, z) => x >= 3 ? -2.5 : 0, new Cell(5, 0)));
        Assert.NotNull(Run((x, z) => x >= 3 ? -7 : 0, new Cell(5, 0)));    // 광산 7m 낭떠러지
        Assert.Null(Run((x, z) => x >= 3 ? -61 : 0, new Cell(5, 0)));
    }

    [Fact]
    public void RampOfSmallStepsClimbsHigh()
    {
        var path = Run((x, z) => x * 0.35, new Cell(9, 0)); // 칸마다 0.35m: 걸어 오름, 끝은 3.15m 위
        Assert.NotNull(path);
        Assert.Equal(10, path.Count);
    }

    [Fact]
    public void NoDiagonalCornerCut()
    {
        // (1,0)에 바닥 없음 → (0,0)에서 (1,1)로 대각선 금지, (0,1)을 거친다
        var path = Run((x, z) => x == 1 && z == 0 ? (double?)null : 0, new Cell(1, 1));
        Assert.NotNull(path);
        Assert.Equal(new[] { new Cell(0, 0), new Cell(0, 1), new Cell(1, 1) }, path);
    }

    [Fact]
    public void LedgesOnlyStraightNotDiagonal()
    {
        // (1,1)만 0.8m 높음: 대각선으로 바로 못 오르고 곧게 한 번 오른다
        var path = Run((x, z) => x == 1 && z == 1 ? 0.8 : 0, new Cell(1, 1));
        Assert.NotNull(path);
        Assert.Equal(3, path.Count);
    }

    [Fact]
    public void UnreachableAndExpandLimitGiveNull()
    {
        Assert.Null(Run((x, z) => x == 4 ? (double?)null : 0, new Cell(8, 8)));          // 바닥 없는 줄로 막힘
        Assert.Null(Run((x, z) => 0, new Cell(9, 9), maxExpand: 3));                       // 펼친 칸 한도
    }

    [Fact]
    public void GoalPredicateStopsAtFirstCellInReach()
    {
        var path = StepGrid.Find(new Cell(0, 0), 0, c => Math.Abs(c.X - 6) + Math.Abs(c.Z) <= 2, new Cell(6, 0),
            (x, z) => 0, (a, b) => true, 1000);
        Assert.NotNull(path);
        Assert.Equal(new Cell(4, 0), path.Last());
    }

    [Fact]
    public void FloorFromGetsHeightOfCellWeComeFrom()
    {
        // x 방향 계단(칸마다 0.5m 오름), z=0 줄만 바닥: 각 칸은 왼쪽 칸 높이를 기준으로 물어야 한다
        var asked = new Dictionary<int, double>();
        var path = StepGrid.Find(new Cell(0, 0), 0, c => c.X == 6, new Cell(6, 0),
            (x, z, from) => { if (z != 0 || x < 0 || x > 9) return null; asked[x] = from; return 0.5 * x; }, (a, b) => true, 1000);
        Assert.NotNull(path);
        Assert.Equal(new Cell(6, 0), path.Last());
        for (int x = 1; x <= 6; x++) Assert.Equal(0.5 * (x - 1), asked[x], 6);
    }

    [Fact]
    public void DoorwayUnderLintelIsFoundFromCorridorHeight()
    {
        // 2026-10-24 vh_crypttest: 묘지 입구 계단 위(1.5m)에서 잰 높이 기준으로는 문간(x=3)의 첫 면이 문틀 윗면(2.5m)이라 못 지나갔다.
        // 면 목록에서 기준 + 1m(뛰어 오르기) 아래 첫 면을 바닥으로 — 기준이 걷던 칸 높이면 문간 바닥(0)이 잡힌다.
        double[] Surfaces(int x) => x == 3 ? new[] { 2.5, 0.0 } : new[] { x == 0 ? 1.5 : 0.0 };
        double? Probe(int x, int z, double from)
        {
            if (z != 0 || x < 0 || x > 9) return null;
            foreach (double s in Surfaces(x)) if (s <= from + StepGrid.MaxUp) return s;
            return null;
        }
        var path = StepGrid.Find(new Cell(0, 0), 1.5, c => c.X == 6, new Cell(6, 0), Probe, (a, b) => true, 1000);
        Assert.NotNull(path);
        Assert.Contains(new Cell(3, 0), path);
        // 예전처럼 시작 높이 하나로 재면 문틀 윗면이 바닥 → 2.5m 턱이라 못 감
        Assert.Null(StepGrid.Find(new Cell(0, 0), 1.5, c => c.X == 6, new Cell(6, 0), (x, z) => Probe(x, z, 1.5), (a, b) => true, 1000));
    }

    [Fact]
    public void ReachNeedsSameFloor()
    {
        // E032: 서리 동굴 위층(수평 2.3m, 대상이 34.5m 아래)은 닿은 게 아니다
        Assert.False(StepGrid.Reached(2.3, 0, -34.5, 2.5));
        Assert.True(StepGrid.Reached(2.3, 0, -0.5, 2.5));
        Assert.True(StepGrid.Reached(1, 1, 2.5, 2.5)); // 벽에 붙은 수정
        Assert.False(StepGrid.Reached(3, 0, 0, 2.5));
    }
}
