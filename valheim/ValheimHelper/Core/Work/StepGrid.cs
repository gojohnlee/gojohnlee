using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 실내(지하묘지) 걷기 길: 칸 지도 A*. 바닐라 길 지도(Pathfinding)는 묘지 안에서 1m 턱·진흙 더미·벽으로 작은 섬들로 쪼개져
    /// 길이 늘 "부분"이다(2026-10-23 vh_crypttest 진단). 칸마다 설 수 있는 바닥 높이(없으면 null)와 이웃 두 칸 사이가 트였는지(벽 없음)는
    /// 부르는 쪽(물리)이 준다. 걸어 넘는 턱 StepUp, 뛰어 오르기 MaxUp, 뛰어내리기 MaxDrop까지.
    /// 대각선은 평평할 때만, 양옆 두 칸으로도 갈 수 있을 때만(모서리에 안 끼게).
    /// (게임 Mono에 System.ValueTuple이 없을 수 있어 튜플 대신 Cell.)
    /// </summary>
    public static class StepGrid
    {
        /// <summary>걸어서 넘는 턱(m).</summary>
        public const double StepUp = 0.4;
        /// <summary>뛰어 오를 수 있는 턱(m). 일꾼 점프(바닐라 m_jumpForce 10)로 넉넉히 되는 높이.</summary>
        public const double MaxUp = 1.0;
        /// <summary>뛰어내려도 되는 높이(m). 일꾼은 떨어짐 피해가 없어서(게임은 IsPlayer만 깎음) 바닥을 찾는 깊이(InteriorNav DownRange)까지 다 됨(사용자 10-28).</summary>
        public const double MaxDrop = 60.0;
        const double JumpCost = 1.5, DropCost = 0.5, Diagonal = 1.41421356;

        public struct Cell : IEquatable<Cell>
        {
            public int X, Z;
            public Cell(int x, int z) { X = x; Z = z; }
            public bool Equals(Cell o) => X == o.X && Z == o.Z;
            public override bool Equals(object o) => o is Cell c && Equals(c);
            public override int GetHashCode() => unchecked(X * 73856093 ^ Z * 19349663);
            public override string ToString() => $"({X},{Z})";
        }

        /// <summary>높이 차 dy(갈 칸 − 지금 칸)를 넘을 수 있는가.</summary>
        public static bool CanStep(double dy) => dy <= MaxUp && dy >= -MaxDrop;

        /// <summary>
        /// 던전 안에서 닿았다고 보는 높이 차(m, 대상 뿌리 − 선 자리, 위아래 같음). 서리 동굴은 같은 xz에 바닥이 여럿이라
        /// 수평 거리만 재면 35m 위층에서 아래층 수정을 "닿음"으로 보고 바닥 너머로 캤다(E032). 벽에 붙은 수정·선반 위 더미(몸 1.8m+팔)까지는 덮는다.
        /// </summary>
        public const double ReachDy = 3.0;

        /// <summary>수평 거리(dx, dz)가 dist 안이고 높이 차 dy가 ReachDy 안인가(던전 안 닿음).</summary>
        public static bool Reached(double dx, double dz, double dy, double dist) => dx * dx + dz * dz <= dist * dist && Math.Abs(dy) <= ReachDy;

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// start에서 goal을 만족하는 칸까지 가장 싼 길(start부터 끝 칸까지). 못 찾거나 maxExpand칸 넘게 펼치면 null.
        /// startFloor = 지금 선 높이(start 칸은 floor를 묻지 않는다), aim = 거리 어림 기준 칸(목표 자리),
        /// floor(x,z) = 설 수 있는 바닥 높이 또는 null, open(a,b) = 이웃 두 칸 사이에 벽이 없는가.
        /// </summary>
        public static List<Cell> Find(Cell start, double startFloor, Func<Cell, bool> goal, Cell aim,
            Func<int, int, double?> floor, Func<Cell, Cell, bool> open, int maxExpand)
            => Find(start, startFloor, goal, aim, (x, z, from) => floor(x, z), open, maxExpand);

        /// <summary>
        /// floorFrom(x,z,from) = 높이 from인 이웃 칸에서 건너갈 때 (x,z) 칸의 설 수 있는 바닥 높이(처음 물은 값을 그 칸 높이로 기억).
        /// 칸 하나에 면이 여럿일 때(문틀 윗면·벽 꼭대기 아래 문간 바닥) 걷던 높이 기준으로 고르게 한다.
        /// </summary>
        public static List<Cell> Find(Cell start, double startFloor, Func<Cell, bool> goal, Cell aim,
            Func<int, int, double, double?> floorFrom, Func<Cell, Cell, bool> open, int maxExpand)
        {
            var heights = new Dictionary<Cell, double?> { [start] = startFloor };
            double? Height(Cell c, double from)
            {
                if (!heights.TryGetValue(c, out double? h)) heights[c] = h = floorFrom(c.X, c.Z, from);
                return h;
            }
            double Guess(Cell c)
            {
                int dx = Math.Abs(c.X - aim.X), dz = Math.Abs(c.Z - aim.Z);
                return Math.Max(dx, dz) + (Diagonal - 1.0) * Math.Min(dx, dz);
            }

            var g = new Dictionary<Cell, double> { [start] = 0.0 };
            var came = new Dictionary<Cell, Cell>();
            var closed = new HashSet<Cell>();
            var heap = new Heap();
            heap.Push(start, Guess(start));
            int expanded = 0;
            while (heap.Count > 0)
            {
                Cell cur = heap.Pop();
                if (!closed.Add(cur)) continue;
                if (goal(cur)) return Path(came, start, cur);
                if (++expanded > maxExpand) return null;
                double hc = Height(cur, startFloor).Value;
                for (int k = 0; k < 8; k++)
                {
                    var n = new Cell(cur.X + DX[k], cur.Z + DZ[k]);
                    if (closed.Contains(n)) continue;
                    double? hn = Height(n, hc);
                    if (hn == null) continue;
                    double dy = hn.Value - hc;
                    if (!CanStep(dy)) continue;
                    bool diag = k >= 4;
                    if (diag)
                    {
                        if (Math.Abs(dy) > StepUp) continue; // 턱은 곧게만 오르내린다
                        var o1 = new Cell(cur.X + DX[k], cur.Z);
                        var o2 = new Cell(cur.X, cur.Z + DZ[k]);
                        double? h1 = Height(o1, hc), h2 = Height(o2, hc);
                        if (h1 == null || h2 == null || Math.Abs(h1.Value - hc) > StepUp || Math.Abs(h2.Value - hc) > StepUp) continue;
                        if (!open(cur, o1) || !open(cur, o2)) continue;
                    }
                    if (!open(cur, n)) continue;
                    double cost = (diag ? Diagonal : 1.0) + (dy > StepUp ? JumpCost : 0.0) + (dy < -StepUp ? DropCost : 0.0);
                    double ng = g[cur] + cost;
                    if (g.TryGetValue(n, out double old) && old <= ng) continue;
                    g[n] = ng;
                    came[n] = cur;
                    heap.Push(n, ng + Guess(n));
                }
            }
            return null;
        }

        static List<Cell> Path(Dictionary<Cell, Cell> came, Cell start, Cell end)
        {
            var path = new List<Cell> { end };
            Cell c = end;
            while (!c.Equals(start)) { c = came[c]; path.Add(c); }
            path.Reverse();
            return path;
        }

        /// <summary>작은 이진 힙(값이 작은 것 먼저).</summary>
        sealed class Heap
        {
            readonly List<Cell> m_items = new List<Cell>();
            readonly List<double> m_keys = new List<double>();
            public int Count => m_items.Count;

            public void Push(Cell c, double key)
            {
                m_items.Add(c);
                m_keys.Add(key);
                int i = m_items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (m_keys[p] <= m_keys[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public Cell Pop()
            {
                Cell top = m_items[0];
                int last = m_items.Count - 1;
                m_items[0] = m_items[last];
                m_keys[0] = m_keys[last];
                m_items.RemoveAt(last);
                m_keys.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < m_items.Count && m_keys[l] < m_keys[m]) m = l;
                    if (r < m_items.Count && m_keys[r] < m_keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                Cell c = m_items[a]; m_items[a] = m_items[b]; m_items[b] = c;
                double k = m_keys[a]; m_keys[a] = m_keys[b]; m_keys[b] = k;
            }
        }
    }
}
