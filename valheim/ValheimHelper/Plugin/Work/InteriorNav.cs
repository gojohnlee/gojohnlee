using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 지하묘지 안(y&gt;3000) 걷기 길. 바닐라 길 지도는 안에서 1m 턱(흙바닥)·진흙 더미·벽으로 작은 섬들로 쪼개져 길이 늘 "부분"이고,
    /// 끝에서 목표로 곧장 걸으면 벽에 막혔다(2026-10-23 vh_crypttest 진단: 도착 자리 섬이 2칸, 둘레 칸은 길 지도가 있어도 안 이어짐).
    /// 1m 칸마다 물리로 바닥·몸 자리·벽을 재서 Core StepGrid로 길을 찾는다(걸어 넘기 0.4m, 뛰어 오르기 1m, 뛰어내리기 3m).
    /// </summary>
    public static class InteriorNav
    {
        public const float Cell = 1f;
        const int MaxExpand = 6000;
        /// <summary>바닥을 찾는 범위: 지금 높이에서 위로 이만큼(묘지 지붕 위 면을 바닥으로 잡지 않게 낮게), 아래로 DownRange.</summary>
        const float UpRange = 2.6f, DownRange = 9f;
        const float BodyRadius = 0.3f;
        /// <summary>몸 자리 검사 아래 끝(바닥 위 m).</summary>
        const float BodyBottom = 0.35f;
        /// <summary>
        /// 바닥 위 물 깊이가 이보다 깊으면 헤엄 칸(뜬 발 높이로 잼, 한 길에 WaterRule.MaxSwim칸까지). 바닐라 Character.InLiquidSwimDepth: 깊이 > m_swimDepth − 0.4면 헤엄 —
        /// 일꾼(플레이어 프리팹) m_swimDepth 1.5 → 1.1m, 0.1m 여유(UseBody). 예전 1.2는 1.1~1.2m 물을 설 수 있다고 봤다(회차 136).
        /// </summary>
        public static float SwimDepth = 1.0f;
        /// <summary>헤엄칠 때 발 높이 = 수면 − 이 값(바닐라 Character: 발이 수면 − m_swimDepth보다 낮으면 떠오른다). 일꾼 1.5.</summary>
        static float s_floatDepth = 1.5f;

        /// <summary>걷는 몸의 헤엄 깊이로 SwimDepth·뜨는 높이를 맞춘다.</summary>
        public static void UseBody(Character c)
        {
            SwimDepth = Mathf.Max(0f, c.m_swimDepth - 0.4f) - 0.1f;
            s_floatDepth = c.m_swimDepth;
        }

        static readonly Collider[] s_water = new Collider[8];
        static int s_waterMask;

        /// <summary>
        /// 그 바닥에 선 몸이 받는 물 깊이(수면 − 바닥, 물 없으면 아주 작은 값). 바닐라는 몸 충돌체가 물 트리거(WaterVolume·LiquidSurface)에 들어가면
        /// 그 수면을 받고(OnTriggerEnter·UpdateFloaters) 깊이 = 수면 − 발 높이로 잰다 — 바닥 한 점(Floating.GetLiquidLevel)만 보면
        /// 바닥보다 위에 뜬 물 상자(늪 지하묘지 물은 수면 쪽 1m 두께)를 놓친다. 그래서 바닥에서 1.85m(일꾼 몸 캡슐 높이)까지 몸 기둥으로 겹치는 물을 본다.
        /// </summary>
        public static float WaterDepth(Vector3 floor)
        {
            if (s_waterMask == 0) s_waterMask = LayerMask.GetMask("WaterVolume");
            int n = Physics.OverlapCapsuleNonAlloc(floor + Vector3.up * (0.05f + BodyRadius), floor + Vector3.up * (1.85f - BodyRadius), BodyRadius, s_water, s_waterMask, QueryTriggerInteraction.Collide);
            float level = -10000f;
            for (int i = 0; i < n; i++)
            {
                if (s_water[i].TryGetComponent(out WaterVolume wv)) level = Mathf.Max(level, wv.GetWaterSurface(floor));
                else if (s_water[i].TryGetComponent(out LiquidSurface ls)) level = Mathf.Max(level, ls.GetSurface(floor));
            }
            return level - floor.y;
        }

        static readonly RaycastHit[] s_hits = new RaycastHit[16];
        static readonly RaycastHit[] s_line = new RaycastHit[16];
        static readonly Collider[] s_cols = new Collider[16];
        static int s_mask;
        static int Mask => s_mask != 0 ? s_mask : (s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"));

        /// <summary>
        /// 닿았는가: 수평 거리 dist 안 + 던전 안이면 높이 차 StepGrid.ReachDy 안(E032 — 서리 동굴 위층에서 아래층 수정을 "닿음"으로 봤다). 밖은 수평 거리만(비탈·나무 뿌리).
        /// </summary>
        public static bool Reached(Vector3 me, Vector3 to, float dist) =>
            Utils.DistanceXZ(me, to) <= dist && (!Character.InInterior(me) || Mathf.Abs(to.y - me.y) <= (float)StepGrid.ReachDy);

        /// <summary>
        /// from에서 to의 dist m 안(XZ)·높이 차 StepGrid.ReachDy 안 칸까지의 길(세계 좌표, 바닥 높이, 첫 점 = from). 못 찾으면 null과 까닭.
        /// 같은 높이로 곧게 트인 구간은 끝점만 남긴다(턱 앞뒤 점은 남음 — 걷는 쪽이 높이로 뛸지 정한다).
        /// </summary>
        public static List<Vector3> Plan(Vector3 from, Vector3 to, float dist, out string why)
        {
            var g = new Grid(from, to, dist, false);
            var cells = g.Find();
            if (cells == null) { why = $"길 없음(칸 {g.Seen}개 봄)"; return null; }
            int swims = cells.Count(g.IsSwim);
            // 헤엄은 좁은 물만(사용자 결정 2026-10-20: 깊은 물 24m 이하, WaterRule.MaxSwim) — 묘지 안 물 찬 구덩이·통로
            if (swims > WaterRule.MaxSwim) { why = $"길에 헤엄 칸 {swims}개(상한 {WaterRule.MaxSwim:0})"; return null; }

            var pts = g.Points(cells);
            var path = new List<Vector3> { pts[0] };
            int i = 0;
            while (i < pts.Count - 1)
            {
                int j = i + 1;
                for (int k = pts.Count - 1; k > i + 1; k--)
                    if (Flat(pts, i, k) && Clear(pts[i], pts[k]) && g.FloorAlong(pts[i], pts[k])) { j = k; break; }
                path.Add(pts[j]);
                i = j;
            }
            why = $"칸 {cells.Count}개·점 {path.Count}개{(swims > 0 ? $"·헤엄 칸 {swims}개" : "")}(칸 {g.Seen}개 봄)";
            return path;
        }

        /// <summary>
        /// 진단(길 없음일 때): from에서 갈 수 있는 칸을 다 펼쳐(StepGrid와 같은 규칙) 목표에 가장 가까이 가는 거리, 경계에서 막은 것(까닭별 수),
        /// 진흙 더미·광맥을 지나가도 된다고 치면 길이 있는지와 그 길에 걸리는 더미를 적는다.
        /// </summary>
        public static string Explain(Vector3 from, Vector3 to, float dist)
        {
            var g = new Grid(from, to, dist, false);
            var why = new Dictionary<string, int>();
            var seen = Flood(g, why);
            float best = seen.Min(c => Utils.DistanceXZ(g.At(c, 0), to));
            string walls = string.Join(", ", why.OrderByDescending(p => p.Value).Take(6).Select(p => p.Key + " ×" + p.Value));
            var piles = PilesOnWay(from, to, dist, out string passWhy);
            string pass = piles == null ? $"더미를 지나가도 길 없음({passWhy})"
                : $"더미를 지나가면 길 있음({passWhy}, 걸리는 더미 {piles.Count}개: {string.Join(", ", piles.Select(p => $"{Utils.GetPrefabName(p.gameObject)} {Utils.DistanceXZ(p.transform.position, from):0}m"))})";
            return $"닿는 칸 {seen.Count}개, 목표에 가장 가까이 {best:0.0}m(닿을 거리 {dist:0.0}), 경계 [{walls}], {pass}, 목표 칸 {CellDiag(to, from.y)}";
        }

        /// <summary>진단: p 칸(yRef 기준으로 잰 바닥)의 바닥 높이(yRef 기준)·못 서는 까닭·물 깊이(바닥 한 점 / 몸 기둥).</summary>
        public static string CellDiag(Vector3 p, float yRef)
        {
            double? h = Probe(p.x, p.z, yRef, true, false, out string why, out float fy, out bool swim);
            float y = !float.IsNaN(fy) ? fy : h != null ? (float)h.Value : float.NaN;
            if (float.IsNaN(y)) return $"바닥 없음({why})";
            var f = new Vector3(p.x, y, p.z);
            return $"바닥 {y - yRef:+0.0;-0.0}m {(h == null ? why : swim ? $"헤엄(뜬 발 {h.Value - yRef:+0.0;-0.0}m)" : "설 수 있음")}, 물 깊이 점 {Floating.GetLiquidLevel(f + Vector3.up * 0.05f) - y:0.00}·몸 {WaterDepth(f):0.00}(기준 {SwimDepth:0.00})";
        }

        /// <summary>진단: here 둘레 range m 안 지하묘지 안 물(WaterVolume) — 상자 높이·수면(here 기준)·크기, 가운데 바닥에서 한 점·몸 기둥 물 깊이.</summary>
        public static string WaterDiag(Vector3 here, float range)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var wv in Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None))
            {
                Vector3 c = wv.transform.position;
                if (!Character.InInterior(c) || Utils.DistanceXZ(c, here) > range) continue;
                if (++n > 8) continue;
                var col = wv.GetComponent<Collider>();
                Bounds b = col != null ? col.bounds : new Bounds(c, Vector3.zero);
                sb.Append($"\n  물 {wv.name} {Utils.DistanceXZ(b.center, here):0}m: 상자 높이 {b.min.y - here.y:+0.0;-0.0}~{b.max.y - here.y:+0.0;-0.0} 크기 {b.size.x:0.0}x{b.size.z:0.0}, 수면 {wv.GetWaterSurface(b.center) - here.y:+0.00;-0.00}, 바람 {wv.m_useGlobalWind}·forceDepth {wv.m_forceDepth:0.0} / 가운데 {CellDiag(b.center, b.max.y + 0.5f)}");
            }
            return $"물 {n}개" + sb;
        }

        /// <summary>시작 칸에서 갈 수 있는 칸 전부(StepGrid와 같은 규칙 — 대각선은 양옆으로도 갈 때만이라 네 방향으로 같다). why = 경계에서 막은 까닭별 수.</summary>
        static HashSet<StepGrid.Cell> Flood(Grid g, Dictionary<string, int> why)
        {
            var seen = new HashSet<StepGrid.Cell> { new StepGrid.Cell(0, 0) };
            var queue = new Queue<StepGrid.Cell>();
            queue.Enqueue(new StepGrid.Cell(0, 0));
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            while (queue.Count > 0 && seen.Count < MaxExpand)
            {
                var cur = queue.Dequeue();
                double hc = g.Height(cur).Value;
                for (int k = 0; k < 4; k++)
                {
                    var n = new StepGrid.Cell(cur.X + dx[k], cur.Z + dz[k]);
                    if (seen.Contains(n)) continue;
                    string r;
                    double? hn = g.Height(n, hc);
                    if (hn == null) r = g.FloorWhy(n);
                    else if (!StepGrid.CanStep(hn.Value - hc)) r = hn.Value > hc ? "턱 오름 1m 넘음" : "턱 내림 3m 넘음";
                    else if (!g.Open(cur, n, out Collider hit)) r = "벽 " + Name(hit);
                    else { seen.Add(n); queue.Enqueue(n); continue; }
                    if (why != null) why[r] = why.TryGetValue(r, out int c) ? c + 1 : 1;
                }
            }
            return seen;
        }

        /// <summary>진단: 길이 있는가. fixedRef면 예전처럼 모든 칸을 시작 높이 기준으로 잰다(회차 136 전 — 문틀 윗면을 바닥으로 잡던 것과 견줌).</summary>
        public static bool CanReach(Vector3 from, Vector3 to, float dist, bool fixedRef) => new Grid(from, to, dist, false, fixedRef).Find() != null;

        /// <summary>
        /// 진단 지도(from 둘레 ±r칸, 위가 +z): 왼쪽 = 칸 종류(@ 시작, . 갈 수 있음, o 설 수 있지만 못 감, ~ 헤엄 칸(w 못 감), = 몸 자리가 1.05m 아래만 막힘(턱·계단 옆),
        /// # 몸 자리 막힘(벽), m 더미, r 강체, 빈칸 바닥 없음), 오른쪽 = 바닥 높이(시작 기준 0.5m 단위: 0, 1~9 위, a~i 아래, ^ v 더).
        /// </summary>
        public static string Map(Vector3 from, int r)
        {
            var g = new Grid(from, from, 0f, false);
            var reach = Flood(g, null);
            var sb = new System.Text.StringBuilder();
            for (int z = r; z >= -r; z--)
            {
                var kinds = new System.Text.StringBuilder();
                var heights = new System.Text.StringBuilder();
                for (int x = -r; x <= r; x++)
                {
                    var c = new StepGrid.Cell(x, z);
                    double? h = g.Height(c);
                    float fy = float.NaN;
                    if (h != null) fy = (float)h.Value;
                    else if (g.BlockedFloor(c, out float by)) fy = by;
                    char k;
                    if (x == 0 && z == 0) k = '@';
                    else if (h != null) k = g.IsSwim(c) ? (reach.Contains(c) ? '~' : 'w') : reach.Contains(c) ? '.' : 'o';
                    else
                    {
                        string w = g.FloorWhy(c);
                        if (w.Contains("더미")) k = 'm';
                        else if (w.StartsWith("강체")) k = 'r';
                        else if (w.StartsWith("몸 자리")) k = HighClear(g.At(c, fy)) ? '=' : '#';
                        else k = ' ';
                    }
                    kinds.Append(k);
                    heights.Append(float.IsNaN(fy) ? ' ' : HeightChar(fy - from.y));
                }
                sb.Append('\n').Append(kinds).Append("   ").Append(heights);
            }
            return sb.ToString();
        }

        static char HeightChar(float dy)
        {
            int d = Mathf.RoundToInt(dy / 0.5f);
            if (d == 0) return '0';
            if (d > 9) return '^';
            if (d < -9) return 'v';
            return d > 0 ? (char)('0' + d) : (char)('a' - d - 1);
        }

        /// <summary>바닥 p 위 1.05~2m 몸 자리가 비었나(진단: 무릎 아래만 걸리는 턱·계단 옆인지).</summary>
        static bool HighClear(Vector3 p) =>
            !Physics.CheckCapsule(p + Vector3.up * (1.05f + BodyRadius), p + Vector3.up * (2f - BodyRadius), BodyRadius, Mask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// 진흙 더미·광맥을 지나가도 된다고 치고 찾은 길에 걸리는 더미들(가까운 순, 길이 지나는 차례). 길이 없으면 null.
        /// 늪 지하묘지는 방 사이 통로를 진흙 더미가 막는 일이 있다 — 그 뒤 더미에 가려면 막은 더미부터 캐야 한다.
        /// </summary>
        public static List<Component> PilesOnWay(Vector3 from, Vector3 to, float dist, out string why)
        {
            var g = new Grid(from, to, dist, true);
            var cells = g.Find();
            if (cells == null) { why = $"칸 {g.Seen}개 봄"; return null; }
            var piles = new List<Component>();
            foreach (var p in g.Points(cells))
            {
                int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (0.35f + BodyRadius), p + Vector3.up * (2f - BodyRadius), BodyRadius + 0.2f, s_cols, Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    Component pile = PileOf(s_cols[i]);
                    if (pile != null && !piles.Contains(pile)) piles.Add(pile);
                }
            }
            why = $"칸 {cells.Count}개";
            return piles;
        }

        /// <summary>한 번 길 찾기의 칸 지도(바닥 높이 기억). passPiles면 진흙 더미·광맥을 없는 것처럼 본다.</summary>
        sealed class Grid
        {
            readonly float m_x0, m_z0, m_yRef, m_reach;
            readonly Vector3 m_to;
            readonly bool m_passPiles;
            readonly bool m_fixedRef;
            readonly StepGrid.Cell m_aim;
            readonly Dictionary<StepGrid.Cell, double?> m_floors = new Dictionary<StepGrid.Cell, double?>();
            readonly Dictionary<StepGrid.Cell, string> m_why = new Dictionary<StepGrid.Cell, string>();
            /// <summary>몸 자리가 막혀 못 서는 칸의 바닥 높이(진단 지도).</summary>
            readonly Dictionary<StepGrid.Cell, float> m_blockedFloor = new Dictionary<StepGrid.Cell, float>();
            public bool BlockedFloor(StepGrid.Cell c, out float y) { Height(c); return m_blockedFloor.TryGetValue(c, out y); }
            /// <summary>헤엄 칸(깊은 물 — 높이는 뜬 발 높이).</summary>
            readonly HashSet<StepGrid.Cell> m_swim = new HashSet<StepGrid.Cell>();
            public bool IsSwim(StepGrid.Cell c) => m_swim.Contains(c);

            public Grid(Vector3 from, Vector3 to, float dist, bool passPiles, bool fixedRef = false)
            {
                m_x0 = from.x; m_z0 = from.z; m_yRef = from.y; m_to = to; m_passPiles = passPiles; m_fixedRef = fixedRef;
                m_reach = Mathf.Max(dist + 0.3f, 0.8f);
                m_aim = new StepGrid.Cell(Mathf.RoundToInt((to.x - m_x0) / Cell), Mathf.RoundToInt((to.z - m_z0) / Cell));
            }

            public int Seen => m_floors.Count;

            public Vector3 At(StepGrid.Cell c, double y) => new Vector3(m_x0 + c.X * Cell, (float)y, m_z0 + c.Z * Cell);

            double? Floor(int x, int z, double from)
            {
                var c = new StepGrid.Cell(x, z);
                if (!m_floors.TryGetValue(c, out double? h))
                {
                    m_floors[c] = h = Probe(m_x0 + x * Cell, m_z0 + z * Cell, (float)(m_fixedRef ? m_yRef : from), !m_fixedRef, m_passPiles, out string why, out float fy, out bool swim);
                    if (h == null && !float.IsNaN(fy)) m_blockedFloor[c] = fy;
                    if (why != null) m_why[c] = why;
                    if (swim) m_swim.Add(c);
                }
                return h;
            }

            /// <summary>칸의 바닥 높이(시작 칸은 지금 선 높이). 처음 물을 때 from = 건너오는 이웃 칸 높이(그 높이 기준으로 바닥을 고른다), 그다음은 기억한 값.</summary>
            public double? Height(StepGrid.Cell c, double from) => c.X == 0 && c.Z == 0 ? m_yRef : Floor(c.X, c.Z, from);

            /// <summary>이미 잰 칸의 높이(안 잰 칸이면 시작 높이 기준으로 잰다 — 진단 지도).</summary>
            public double? Height(StepGrid.Cell c) => Height(c, m_yRef);

            public string FloorWhy(StepGrid.Cell c) { Height(c); return m_why.TryGetValue(c, out string w) ? w : "?"; }

            public bool Open(StepGrid.Cell a, StepGrid.Cell b) => Open(a, b, out _);

            public bool Open(StepGrid.Cell a, StepGrid.Cell b, out Collider hit)
            {
                double ha = Height(a) ?? m_yRef;
                double hb = Height(b) ?? m_yRef;
                float top = (float)System.Math.Max(ha, hb);
                Vector3 pa = At(a, top), pb = At(b, top);
                hit = Blocked(pa + Vector3.up * 0.9f, pb + Vector3.up * 0.9f, m_passPiles) ?? Blocked(pa + Vector3.up * 1.6f, pb + Vector3.up * 1.6f, m_passPiles);
                return hit == null;
            }

            /// <summary>목표 칸: 수평 m_reach 안이고 바닥이 대상 높이에서 StepGrid.ReachDy 안(E032 — 위층 칸은 아래층 대상의 목표가 아니다).</summary>
            bool Goal(StepGrid.Cell c) => Utils.DistanceXZ(At(c, 0), m_to) <= m_reach && System.Math.Abs((Height(c) ?? m_yRef) - m_to.y) <= StepGrid.ReachDy;

            public List<StepGrid.Cell> Find() => StepGrid.Find(new StepGrid.Cell(0, 0), m_yRef, Goal, m_aim, Floor, Open, MaxExpand);

            /// <summary>
            /// a→b 곧은 줄 아래가 0.5m마다 설 수 있는 칸이고 a와 같은 높이(걸어 넘는 턱 안)인가. 칸 길을 곧게 펴면 길에 없던 칸을 지나는데,
            /// Clear는 무릎·가슴 높이 장애물만 봐서 모퉁이를 질러 물웅덩이·구덩이 위로 가는 줄도 통과시켰다(회차 136).
            /// </summary>
            public bool FloorAlong(Vector3 a, Vector3 b)
            {
                float len = Utils.DistanceXZ(a, b);
                int steps = Mathf.CeilToInt(len / 0.5f);
                for (int s = 1; s < steps; s++)
                {
                    Vector3 q = Vector3.Lerp(a, b, s / (float)steps);
                    var c = new StepGrid.Cell(Mathf.RoundToInt((q.x - m_x0) / Cell), Mathf.RoundToInt((q.z - m_z0) / Cell));
                    double? h = Height(c, a.y);
                    if (h == null || System.Math.Abs(h.Value - a.y) > StepGrid.StepUp) return false;
                }
                return true;
            }

            public List<Vector3> Points(List<StepGrid.Cell> cells)
            {
                var pts = new List<Vector3>(cells.Count);
                foreach (var c in cells) pts.Add(At(c, Height(c).Value));
                return pts;
            }
        }

        /// <summary>진흙 더미(Destructible)·광맥이면 그 물체, 아니면 null.</summary>
        static Component PileOf(Collider c)
        {
            if (c == null) return null;
            return (Component)c.GetComponentInParent<Destructible>() ?? (Component)c.GetComponentInParent<MineRock5>() ?? c.GetComponentInParent<MineRock>();
        }

        static string Name(Collider c)
        {
            if (c == null) return "?";
            Component pile = PileOf(c);
            if (pile != null) return "더미 " + Utils.GetPrefabName(pile.gameObject);
            // 방 모델의 메시 충돌체는 이름이 "default"뿐이다 — 어느 물체인지 부모 이름을 붙인다(진단, E031)
            Transform up = c.transform.parent;
            return c.name == "default" && up != null ? $"{c.name}/{Utils.GetPrefabName(up.gameObject)}" : c.name;
        }

        /// <summary>a→b 선분을 막는 첫 물체(없으면 null). passPiles면 진흙 더미·광맥은 지나간다.</summary>
        static Collider Blocked(Vector3 a, Vector3 b, bool passPiles)
        {
            if (!passPiles) return Physics.Linecast(a, b, out RaycastHit h, Mask, QueryTriggerInteraction.Ignore) ? h.collider : null;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return null;
            int n = Physics.RaycastNonAlloc(a, d / len, s_line, len, Mask, QueryTriggerInteraction.Ignore);
            Collider first = null;
            float firstAt = float.MaxValue;
            for (int i = 0; i < n; i++)
                if (s_line[i].distance < firstAt && PileOf(s_line[i].collider) == null) { first = s_line[i].collider; firstAt = s_line[i].distance; }
            return first;
        }

        /// <summary>i~k 칸이 모두 i와 같은 높이(걸어 넘는 턱 안)인가.</summary>
        static bool Flat(List<Vector3> pts, int i, int k)
        {
            for (int n = i + 1; n <= k; n++) if (Mathf.Abs(pts[n].y - pts[i].y) > (float)StepGrid.StepUp) return false;
            return true;
        }

        /// <summary>a에서 b까지 몸(반지름 BodyRadius)이 무릎~가슴 높이에서 걸리지 않는가.</summary>
        static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            d /= len;
            return !Physics.SphereCast(a + Vector3.up * 0.9f, BodyRadius, d, out _, len, Mask, QueryTriggerInteraction.Ignore)
                && !Physics.Linecast(a + Vector3.up * 0.5f, b + Vector3.up * 0.5f, Mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// (x,z) 칸의 설 수 있는 바닥 높이: yRef(건너오는 이웃 칸 높이)+UpRange에서 아래로 쏴, yRef+뛰어 오르기(1m)보다 높지 않은 첫 완만한 면(법선 y ≥ 0.55).
        /// 그보다 높은 면(문간 위 문틀·벽 꼭대기·선반)은 지나 아래를 본다 — 입구 계단 위(1.5m)를 기준으로 재면 문틀 윗면을 바닥으로 잡아
        /// 문간이 다 막힌 것으로 보였다(2026-10-24 vh_crypttest 회차 136). 그 위 몸 자리(0.35~2m)가 비어야 한다.
        /// 진흙 더미·광맥·움직이는 물체 위는 바닥으로 안 친다(장애물; passPiles면 더미·광맥은 없는 것처럼 그 아래를 본다).
        /// 벽·기둥 속에서 쏘면 그 아래 바닥은 몸 자리가 막혀 null. why = 못 설 때 까닭(진단).
        /// </summary>
        static double? Probe(float x, float z, float yRef, bool skipHigh, bool passPiles, out string why, out float floorY, out bool swim)
        {
            why = null;
            floorY = float.NaN;
            swim = false;
            var origin = new Vector3(x, yRef + UpRange, z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, UpRange + DownRange, Mask, QueryTriggerInteraction.Ignore);
            for (int a = 1; a < n; a++)
            {
                RaycastHit h = s_hits[a];
                int b = a - 1;
                while (b >= 0 && s_hits[b].distance > h.distance) { s_hits[b + 1] = s_hits[b]; b--; }
                s_hits[b + 1] = h;
            }
            for (int a = 0; a < n; a++)
            {
                RaycastHit h = s_hits[a];
                if (h.normal.y < 0.55f) continue; // 벽면·가파른 면은 지나 아래를 본다
                if (skipHigh && h.point.y > yRef + (float)StepGrid.MaxUp + 0.1f) continue; // 못 오르는 높은 면(문틀·벽 꼭대기)은 지나 아래를 본다
                Collider c = h.collider;
                Component pile = PileOf(c);
                if (pile != null)
                {
                    if (passPiles) continue;
                    why = "더미 위 " + Utils.GetPrefabName(pile.gameObject);
                    return null;
                }
                if (c.attachedRigidbody != null) { why = "강체 위 " + c.name; return null; }
                Vector3 p = h.point;
                int m = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (BodyBottom + BodyRadius), p + Vector3.up * (2f - BodyRadius), BodyRadius, s_cols, Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < m; i++)
                {
                    if (passPiles && PileOf(s_cols[i]) != null) continue;
                    why = "몸 자리 " + Name(s_cols[i]);
                    floorY = p.y;
                    return null;
                }
                float depth = WaterDepth(p);
                if (depth > SwimDepth)
                {
                    // 깊은 물(묘지 안 물 찬 구덩이·통로): 헤엄 칸 — 몸은 수면 − m_swimDepth까지 떠서 간다(그 높이로 이웃 턱을 잰다).
                    // 회차 136·137: 바닥 한 점만 봐 물을 놓쳐 마른 칸으로 보고 들어갔다가, 걷기가 "헤엄 → 마른 자리로 돌아감"을 되풀이해 20초 갇힘.
                    swim = true;
                    floorY = p.y;
                    return System.Math.Max(p.y, p.y + depth - s_floatDepth);
                }
                return p.y;
            }
            why = "바닥 없음";
            return null;
        }
    }
}
