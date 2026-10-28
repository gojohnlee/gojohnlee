using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 용암·깊은 타르 웅덩이 둘레 길(회차 224, 다-2: 가장자리를 더듬어 돌면 60m에 134초).
    /// 2m 칸 A*: blocked(x,z)인 칸 중심과 칸 사이 가운데 점은 못 가고, 대각선은 양옆 칸이 다 열려야 감(모서리를 스치지 않게).
    /// 출발 칸은 늘 갈 수 있다고 본다(가장자리에 서 있어도 떠나게). 도착 칸이 막혔으면 null(부르는 쪽이 옛 비켜 가기로).
    /// 찾는 범위는 두 점을 감싼 네모 + Margin, maxExpand칸 넘게 펼치면 null. 결과는 1m 간격으로 막힘을 본 직선으로 줄인 점들(출발점 → … → 도착점).
    /// </summary>
    public static class HazardPath
    {
        public const double CellSize = 2;
        public const double Margin = 40;
        public const int DefaultMaxExpand = 8000;
        const double Diagonal = 1.4142135623730951;
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public static List<RoughPath.P> Find(RoughPath.P start, RoughPath.P goal, Func<double, double, bool> blocked, int maxExpand = DefaultMaxExpand)
        {
            var s = ToCell(start);
            var e = ToCell(goal);
            if (s.Equals(e)) return new List<RoughPath.P> { start, goal };
            int minX = ToCell(Math.Min(start.X, goal.X) - Margin), maxX = ToCell(Math.Max(start.X, goal.X) + Margin);
            int minZ = ToCell(Math.Min(start.Z, goal.Z) - Margin), maxZ = ToCell(Math.Max(start.Z, goal.Z) + Margin);
            var open = new Dictionary<StepGrid.Cell, bool>();
            bool Open(StepGrid.Cell c)
            {
                if (c.Equals(s)) return true;
                if (open.TryGetValue(c, out bool o)) return o;
                o = c.X >= minX && c.X <= maxX && c.Z >= minZ && c.Z <= maxZ && !blocked(Center(c.X), Center(c.Z));
                open[c] = o;
                return o;
            }
            if (!Open(e)) return null;
            double Guess(StepGrid.Cell c)
            {
                int dx = Math.Abs(c.X - e.X), dz = Math.Abs(c.Z - e.Z);
                return CellSize * (Math.Max(dx, dz) + (Diagonal - 1.0) * Math.Min(dx, dz));
            }

            var g = new Dictionary<StepGrid.Cell, double> { [s] = 0.0 };
            var came = new Dictionary<StepGrid.Cell, StepGrid.Cell>();
            var closed = new HashSet<StepGrid.Cell>();
            // 게임 Mono엔 System.ValueTuple이 없다(회차 224 FileNotFoundException) → 튜플 대신 Node
            var heap = new SortedSet<Node>(Comparer<Node>.Create((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : a.Seq.CompareTo(b.Seq);
            }));
            int seq = 0, expanded = 0;
            heap.Add(new Node(Guess(s), seq++, s));
            while (heap.Count > 0)
            {
                var top = heap.Min;
                heap.Remove(top);
                var cur = top.Cell;
                if (!closed.Add(cur)) continue;
                if (cur.Equals(e)) return Smooth(Cells(came, s, e, start, goal), blocked);
                if (++expanded > maxExpand) return null;
                for (int k = 0; k < 8; k++)
                {
                    var n = new StepGrid.Cell(cur.X + DX[k], cur.Z + DZ[k]);
                    if (closed.Contains(n) || !Open(n)) continue;
                    if (k >= 4 && (!Open(new StepGrid.Cell(cur.X + DX[k], cur.Z)) || !Open(new StepGrid.Cell(cur.X, cur.Z + DZ[k])))) continue;
                    if (!cur.Equals(s) && blocked(Center(cur.X) + DX[k] * CellSize / 2, Center(cur.Z) + DZ[k] * CellSize / 2)) continue;
                    double ng = g[cur] + (k >= 4 ? CellSize * Diagonal : CellSize);
                    if (g.TryGetValue(n, out double old) && old <= ng) continue;
                    g[n] = ng;
                    came[n] = cur;
                    heap.Add(new Node(ng + Guess(n), seq++, n));
                }
            }
            return null;
        }

        /// <summary>a → b 곧은 선을 1m 간격으로 봐서 막힌 데가 없는지(양 끝은 안 봄).</summary>
        public static bool Clear(RoughPath.P a, RoughPath.P b, Func<double, double, bool> blocked)
        {
            double len = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
            int n = (int)Math.Ceiling(len);
            for (int i = 1; i < n; i++)
            {
                double t = (double)i / n;
                if (blocked(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t)) return false;
            }
            return true;
        }

        /// <summary>점마다 막힘 없이 곧게 닿는 가장 먼 점으로 건너뛴다.</summary>
        static List<RoughPath.P> Smooth(List<RoughPath.P> pts, Func<double, double, bool> blocked)
        {
            var res = new List<RoughPath.P> { pts[0] };
            int i = 0;
            while (i < pts.Count - 1)
            {
                int j = pts.Count - 1;
                while (j > i + 1 && !Clear(pts[i], pts[j], blocked)) j--;
                res.Add(pts[j]);
                i = j;
            }
            return res;
        }

        static List<RoughPath.P> Cells(Dictionary<StepGrid.Cell, StepGrid.Cell> came, StepGrid.Cell s, StepGrid.Cell e, RoughPath.P start, RoughPath.P goal)
        {
            var cells = new List<StepGrid.Cell>();
            var c = e;
            while (!c.Equals(s)) { c = came[c]; if (!c.Equals(s)) cells.Add(c); }
            cells.Reverse();
            var pts = new List<RoughPath.P> { start };
            foreach (var cell in cells) pts.Add(new RoughPath.P(Center(cell.X), Center(cell.Z)));
            pts.Add(goal);
            return pts;
        }

        struct Node
        {
            public readonly double Key;
            public readonly int Seq;
            public readonly StepGrid.Cell Cell;
            public Node(double key, int seq, StepGrid.Cell cell) { Key = key; Seq = seq; Cell = cell; }
        }

        static int ToCell(double v) => (int)Math.Floor(v / CellSize);
        static StepGrid.Cell ToCell(RoughPath.P p) => new StepGrid.Cell(ToCell(p.X), ToCell(p.Z));
        static double Center(int c) => (c + 0.5) * CellSize;
    }
}
