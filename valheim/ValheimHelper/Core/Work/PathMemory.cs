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

        sealed class Route { internal P[] Points; internal double Used; }

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

        /// <summary>닿은 길 기억(점 2개 미만은 안 함). 같은 칸 쌍이면 새 길로 바꾼다.</summary>
        public void Remember(P from, P to, IList<P> points, double now)
        {
            if (points == null || points.Count < 2) return;
            var key = Key(from, to);
            if (!m_routes.ContainsKey(key) && m_routes.Count >= m_max) Evict();
            m_routes[key] = new Route { Points = points.ToArray(), Used = now };
        }

        /// <summary>같은 출발·도착 칸의 기억한 길(없으면 null). 쓰면 시각을 새로 해 덜 지워지게.</summary>
        public P[] Find(P from, P to, double now)
        {
            if (!m_routes.TryGetValue(Key(from, to), out var r)) return null;
            r.Used = now;
            return r.Points;
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
                Remember(from, to, pts, used);
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
