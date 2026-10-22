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
        Assert.Null(Run((x, z) => x >= 3 ? -3.5 : 0, new Cell(5, 0)));
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
}
