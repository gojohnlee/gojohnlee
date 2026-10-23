using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 멀리선 이동 생략 때 지도상 위치가 따라갈 거친 길(사용자 결정 2026-10-23 "멀리선 이동 생략 때 지도").
    /// 8m 칸 A*: 깊은 물(물 높이보다 WaterRule.DeepDepth 넘게 낮은 땅)과 너무 가파른 칸 사이(MaxSlope)는 못 가고,
    /// 안 밝혀진 칸은 UnexploredCost배로 비싸서 밝혀진 칸을 지나는 쪽을 고른다. 땅 높이·밝혀짐은 부르는 쪽(게임 생성 지형·지도)이 준다.
    /// 출발·도착 칸은 늘 갈 수 있다고 본다. 찾는 범위는 두 점을 감싼 네모 + Margin.
    /// 못 찾거나 maxExpand칸 넘게 펼치면 null → Route가 직선으로 대신한다(대체 동작).
    /// </summary>
    public static class RoughPath
    {
        public const double CellSize = 8;
        /// <summary>이웃 칸 중심 사이 높이 차 ÷ 거리의 최대(1 = 45°).</summary>
        public const double MaxSlope = 1.0;
        public const double UnexploredCost = 1.5;
        public const double Margin = 64;
        public const int DefaultMaxExpand = 20000;

        public struct P
        {
            public double X, Z;
            public P(double x, double z) { X = x; Z = z; }
            public override string ToString() => $"({X:0.#},{Z:0.#})";
        }

        /// <summary>
        /// 지도상 위치가 따라갈 길(출발점부터 도착점까지 점들). goalExplored가 아니면(탐험·찾기) 직선,
        /// 밝혀진 목적지면 Find, 못 찾으면 직선.
        /// </summary>
        public static List<P> Route(P start, P goal, bool goalExplored, Func<double, double, double> height, double waterLevel,
            Func<double, double, bool> explored, int maxExpand = DefaultMaxExpand)
        {
            if (goalExplored)
            {
                var path = Find(start, goal, height, waterLevel, explored, maxExpand);
                if (path != null) return path;
            }
            return new List<P> { start, goal };
        }

        public static List<P> Find(P start, P goal, Func<double, double, double> height, double waterLevel,
            Func<double, double, bool> explored, int maxExpand = DefaultMaxExpand)
        {
            var s = ToCell(start);
            var e = ToCell(goal);
            if (s.Equals(e)) return new List<P> { start, goal };
            int minX = ToCell(Math.Min(start.X, goal.X) - Margin), maxX = ToCell(Math.Max(start.X, goal.X) + Margin);
            int minZ = ToCell(Math.Min(start.Z, goal.Z) - Margin), maxZ = ToCell(Math.Max(start.Z, goal.Z) + Margin);

            var heights = new Dictionary<StepGrid.Cell, double?>();
            double? Height(StepGrid.Cell c)
            {
                if (heights.TryGetValue(c, out double? h)) return h;
                if (c.X < minX || c.X > maxX || c.Z < minZ || c.Z > maxZ) h = null;
                else
                {
                    double y = height(Center(c.X), Center(c.Z));
                    bool deep = waterLevel - y > WaterRule.DeepDepth;
                    h = deep && !c.Equals(s) && !c.Equals(e) ? (double?)null : y;
                }
                heights[c] = h;
                return h;
            }
            double Guess(StepGrid.Cell c)
            {
                int dx = Math.Abs(c.X - e.X), dz = Math.Abs(c.Z - e.Z);
                return CellSize * (Math.Max(dx, dz) + (Diagonal - 1.0) * Math.Min(dx, dz));
            }

            var g = new Dictionary<StepGrid.Cell, double> { [s] = 0.0 };
            var came = new Dictionary<StepGrid.Cell, StepGrid.Cell>();
            var closed = new HashSet<StepGrid.Cell>();
            var heap = new SortedSet<Node>(new NodeOrder());
            int seq = 0;
            heap.Add(new Node(s, Guess(s), seq++));
            int expanded = 0;
            while (heap.Count > 0)
            {
                var top = heap.Min;
                heap.Remove(top);
                var cur = top.Cell;
                if (!closed.Add(cur)) continue;
                if (cur.Equals(e)) return Points(came, s, e, start, goal);
                if (++expanded > maxExpand) return null;
                double? hc = Height(cur);
                if (hc == null) continue;
                for (int k = 0; k < 8; k++)
                {
                    var n = new StepGrid.Cell(cur.X + DX[k], cur.Z + DZ[k]);
                    if (closed.Contains(n)) continue;
                    double? hn = Height(n);
                    if (hn == null) continue;
                    double step = k >= 4 ? CellSize * Diagonal : CellSize;
                    if (Math.Abs(hn.Value - hc.Value) / step > MaxSlope) continue;
                    double cost = step * (explored(Center(n.X), Center(n.Z)) ? 1.0 : UnexploredCost);
                    double ng = g[cur] + cost;
                    if (g.TryGetValue(n, out double old) && old <= ng) continue;
                    g[n] = ng;
                    came[n] = cur;
                    heap.Add(new Node(n, ng + Guess(n), seq++));
                }
            }
            return null;
        }

        /// <summary>길 전체 길이(m, 수평).</summary>
        public static double Length(IList<P> path)
        {
            double d = 0;
            for (int i = 1; i < path.Count; i++) d += Dist(path[i - 1], path[i]);
            return d;
        }

        /// <summary>출발점에서 길을 따라 dist(m) 간 점. 0 이하면 출발점, 길이 넘으면 도착점.</summary>
        public static P PointAt(IList<P> path, double dist)
        {
            if (path.Count == 0) throw new ArgumentException("빈 길");
            if (dist <= 0) return path[0];
            for (int i = 1; i < path.Count; i++)
            {
                double seg = Dist(path[i - 1], path[i]);
                if (dist <= seg)
                {
                    double t = seg <= 0 ? 1 : dist / seg;
                    return new P(path[i - 1].X + (path[i].X - path[i - 1].X) * t, path[i - 1].Z + (path[i].Z - path[i - 1].Z) * t);
                }
                dist -= seg;
            }
            return path[path.Count - 1];
        }

        const double Diagonal = 1.41421356;
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        static int ToCell(double v) => (int)Math.Floor(v / CellSize);
        static StepGrid.Cell ToCell(P p) => new StepGrid.Cell(ToCell(p.X), ToCell(p.Z));
        static double Center(int c) => (c + 0.5) * CellSize;
        static double Dist(P a, P b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        /// <summary>출발점 → (출발·도착 칸 뺀) 칸 중심들 → 도착점.</summary>
        static List<P> Points(Dictionary<StepGrid.Cell, StepGrid.Cell> came, StepGrid.Cell s, StepGrid.Cell e, P start, P goal)
        {
            var cells = new List<StepGrid.Cell>();
            var c = e;
            while (!c.Equals(s)) { c = came[c]; if (!c.Equals(s)) cells.Add(c); }
            cells.Reverse();
            var pts = new List<P> { start };
            foreach (var cell in cells) pts.Add(new P(Center(cell.X), Center(cell.Z)));
            pts.Add(goal);
            return pts;
        }

        readonly struct Node
        {
            public readonly StepGrid.Cell Cell;
            public readonly double Key;
            public readonly int Seq;
            public Node(StepGrid.Cell cell, double key, int seq) { Cell = cell; Key = key; Seq = seq; }
        }

        sealed class NodeOrder : IComparer<Node>
        {
            public int Compare(Node a, Node b)
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : a.Seq.CompareTo(b.Seq);
            }
        }
    }
}
