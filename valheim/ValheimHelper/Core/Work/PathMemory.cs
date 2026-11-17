using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 집 안 길 기억(요청 11-15 "조건 없이" ①): 끝까지 걸어 닿은 길(점 목록)을 출발·도착 칸으로 기억해 다음에 같은 길 찾기를 건너뛴다.
    /// 칸 = 가로 0.5m 격자 + 높이 1m 층(2층 집에서 위·아래층 같은 자리를 가름). 상한 1만 개, 넘으면 가장 오래 안 쓴 것부터 지운다.
    /// 건축이 바뀌면 그 근처를 지나는 길을 지운다(ForgetNear). 시간은 부르는 쪽 시계(초)를 받는다. 게임 무관 — 저장은 Save/Load 문자열.
    /// </summary>
    public sealed class PathMemory
    {
        public const float Cell = 0.5f;
        public const float Floor = 1f;
        public const int Max = 10000;

        public readonly struct P
        {
            public readonly float X, Y, Z;
            public P(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        sealed class Route { internal P[] Points; internal double Used; internal int Uses; internal float Length; }

        // 요청 11-17(길 기억 쌓임): 기억한 길만 따르면 더 나은 길을 영영 못 찾음 → 가끔 Find가 null(바닐라로 다시 걸어 봄), Remember는 더 짧은 길만 받음.
        // 수상한 길(길/직선이 같은 층 SameFloorRatio·다른 층 CrossFloorRatio 넘음)은 하나 걸러, 나머지는 ExploreEvery번째마다.
        public const float SameFloorRatio = 1.3f, CrossFloorRatio = 2f, KeepSlack = 1.05f;
        public const int ExploreEvery = 8;

        readonly struct K : IEquatable<K>
        {
            internal readonly int Ax, Ay, Az, Bx, By, Bz;
            internal K(int ax, int ay, int az, int bx, int by, int bz) { Ax = ax; Ay = ay; Az = az; Bx = bx; By = by; Bz = bz; }
            public bool Equals(K o) => Ax == o.Ax && Ay == o.Ay && Az == o.Az && Bx == o.Bx && By == o.By && Bz == o.Bz;
            public override bool Equals(object o) => o is K k && Equals(k);
            public override int GetHashCode() { unchecked { int h = Ax; h = h * 397 ^ Ay; h = h * 397 ^ Az; h = h * 397 ^ Bx; h = h * 397 ^ By; return h * 397 ^ Bz; } }
        }

        readonly Dictionary<K, Route> m_routes = new Dictionary<K, Route>();
        readonly int m_max;

        public PathMemory(int max = Max) { m_max = Math.Max(1, max); }

        public int Count => m_routes.Count;

        static int G(float v, float size) => (int)Math.Floor(v / size);

        static K Key(P a, P b) =>
            new K(G(a.X, Cell), G(a.Y, Floor), G(a.Z, Cell), G(b.X, Cell), G(b.Y, Floor), G(b.Z, Cell));

        /// <summary>닿은 길 기억(점 2개 미만은 안 함). 같은 칸 쌍이면 새 길이 옛 길보다 KeepSlack배 넘게 길지 않을 때만 바꾼다(force면 늘). 받았으면 true.
        /// 방금 걸어 본 길이라 다음 Find는 따른다(Uses 1). 불러온 길은 Uses 0 — 수상하면 첫 Find에 다시 걸어 봄.</summary>
        public bool Remember(P from, P to, IList<P> points, double now, bool force = false) => Put(from, to, points, now, force, 1);

        bool Put(P from, P to, IList<P> points, double now, bool force, int uses)
        {
            if (points == null || points.Count < 2) return false;
            var key = Key(from, to);
            float len = Len(points);
            if (!force && m_routes.TryGetValue(key, out var old) && len > old.Length * KeepSlack) { old.Used = now; return false; }
            if (!m_routes.ContainsKey(key) && m_routes.Count >= m_max) Evict();
            m_routes[key] = new Route { Points = points.ToArray(), Used = now, Uses = uses, Length = len };
            return true;
        }

        /// <summary>같은 출발·도착 칸의 기억한 길(없거나 다시 걸어 볼 차례면 null — 그때 explore true). 쓰면 시각을 새로 해 덜 지워지게.</summary>
        public P[] Find(P from, P to, double now) => Find(from, to, now, out _);

        public P[] Find(P from, P to, double now, out bool explore)
        {
            explore = false;
            if (!m_routes.TryGetValue(Key(from, to), out var r)) return null;
            r.Used = now;
            r.Uses++;
            explore = Suspicious(r) ? r.Uses % 2 == 1 : r.Uses % ExploreEvery == 0;
            return explore ? null : r.Points;
        }

        /// <summary>시험·진단용: 셈 없이 기억한 길 길이(없으면 -1).</summary>
        public float LengthOf(P from, P to) => m_routes.TryGetValue(Key(from, to), out var r) ? r.Length : -1f;

        static float Len(IList<P> p) { float s = 0; for (int i = 1; i < p.Count; i++) s += Dist(p[i - 1], p[i]); return s; }
        static float Dist(P a, P b) { float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z; return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); }

        /// <summary>길/직선이 기준을 넘는 수상한 길. 다른 층(높이 Floor*1.3 넘게 차이)은 계단을 돌아 늘 기니 CrossFloorRatio.</summary>
        static bool Suspicious(Route r)
        {
            P a = r.Points[0], b = r.Points[r.Points.Length - 1];
            float straight = Math.Max(0.5f, Dist(a, b));
            return r.Length > straight * (Math.Abs(a.Y - b.Y) > Floor * 1.3f ? CrossFloorRatio : SameFloorRatio);
        }

        /// <summary>기억한 길로 갔는데 막힘 — 그 길을 지운다.</summary>
        public bool Forget(P from, P to) => m_routes.Remove(Key(from, to));

        /// <summary>건축이 바뀐 자리 반경 안(가로 거리, 높이 차 Floor*2 안)을 지나는 길을 모두 지운다. 지운 수.</summary>
        public int ForgetNear(P at, float radius)
        {
            float r2 = radius * radius;
            var gone = m_routes.Where(kv => kv.Value.Points.Any(p => Near(p, at, r2))).Select(kv => kv.Key).ToList();
            foreach (var k in gone) m_routes.Remove(k);
            return gone.Count;
        }

        static bool Near(P p, P at, float r2)
        {
            float dx = p.X - at.X, dz = p.Z - at.Z;
            return dx * dx + dz * dz <= r2 && Math.Abs(p.Y - at.Y) <= Floor * 2;
        }

        /// <summary>maxAge초 넘게 안 쓴 길을 지운다. 지운 수.</summary>
        public int Prune(double now, double maxAge)
        {
            var gone = m_routes.Where(kv => now - kv.Value.Used > maxAge).Select(kv => kv.Key).ToList();
            foreach (var k in gone) m_routes.Remove(k);
            return gone.Count;
        }

        void Evict()
        {
            var oldest = m_routes.OrderBy(kv => kv.Value.Used).First().Key;
            m_routes.Remove(oldest);
        }

        // 저장 꼴: 한 길 한 줄 "used;fx,fy,fz;tx,ty,tz;x,y,z x,y,z …" — 출발·도착은 첫 점·끝 점이 아니라 기억한 칸의 대표(칸 중심)
        public string Save()
        {
            var sb = new StringBuilder();
            foreach (var kv in m_routes)
            {
                var k = kv.Key;
                sb.Append(kv.Value.Used.ToString("R", CultureInfo.InvariantCulture)).Append(';')
                  .Append(Fmt(Center(k.Ax, k.Ay, k.Az))).Append(';')
                  .Append(Fmt(Center(k.Bx, k.By, k.Bz))).Append(';')
                  .Append(string.Join(" ", kv.Value.Points.Select(Fmt))).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Save 문자열을 읽어 더한다(틀린 줄은 건너뜀). 읽은 길 수.</summary>
        public int Load(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0;
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split(';');
                if (parts.Length != 4) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double used)) continue;
                if (!TryP(parts[1], out var from) || !TryP(parts[2], out var to)) continue;
                var pts = new List<P>();
                bool ok = true;
                foreach (var s in parts[3].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!TryP(s, out var p)) { ok = false; break; }
                    pts.Add(p);
                }
                if (!ok || pts.Count < 2) continue;
                Put(from, to, pts, used, false, 0);
                n++;
            }
            return n;
        }

        static P Center(int x, int y, int z) => new P((x + 0.5f) * Cell, (y + 0.5f) * Floor, (z + 0.5f) * Cell);

        static string Fmt(P p) => string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##}", p.X, p.Y, p.Z);

        static bool TryP(string s, out P p)
        {
            p = default;
            var c = s.Split(',');
            if (c.Length != 3) return false;
            if (!float.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
            p = new P(x, y, z);
            return true;
        }
    }
}
