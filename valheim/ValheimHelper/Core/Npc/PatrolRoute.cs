using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>순찰 후보 자리 판정(게임 쪽 probe가 땅을 보고 줌).</summary>
    public enum PatrolSpot { Ok, Water, Steep, Built, Roof, NoGround, Unreached }

    /// <summary>
    /// 호위병 순찰 점 고르기(사용자 11-12 23:22 "거점 범위 안을 크게 한 바퀴, 점은 그 거점 지형을 보고 코드가 고름"):
    /// 깃발 둘레 Dirs 방향(문 쪽이 0번)마다 Radii 순서(바깥 → 안)로 후보를 찍어 probe가 Ok인 첫 자리를 그 방향 점으로. 못 고른 방향은 버림(까닭 남김).
    /// 순찰 중 못 닿은 점은 Unreached로 그 (방향, 반경)을 막아 다음 고르기에서 안쪽 반경으로. 땅이 바뀌면(sig) 또는 RecheckEvery마다 막은 것을 지우고 다시 고름.
    /// 점이 하나도 없으면 Points가 비어 있음 — 부르는 쪽이 옛 집 둘레(Patrol.PointAt)로.
    /// </summary>
    public sealed class PatrolRoute
    {
        public const int Dirs = 8;
        public static readonly double[] Radii = { 120, 100, 80, 60 };
        /// <summary>땅이 안 바뀌어도 이만큼(초)마다 막은 것을 지우고 다시 고름 — 기본안 11-13.</summary>
        public const float RecheckEvery = 3600f;

        public struct Spot { public int Dir, Ring; public double X, Z; }

        readonly HashSet<int> m_banned = new HashSet<int>();
        readonly List<Spot> m_points = new List<Spot>();
        float m_age;
        long m_sig;
        bool m_built;

        public IReadOnlyList<Spot> Points => m_points;
        /// <summary>방향마다 버린 까닭(마지막 고르기) — 방향에 점이 있으면 null.</summary>
        public PatrolSpot?[] DroppedWhy { get; } = new PatrolSpot?[Dirs];

        /// <summary>다시 골라야 하나: 처음, 막은 점이 생김(m_built=false), 땅 표시 sig 바뀜, RecheckEvery 지남(그땐 막은 것도 지움).</summary>
        public bool Due(float dt, long groundSig)
        {
            m_age += dt;
            if (m_built && groundSig != m_sig) { m_banned.Clear(); m_built = false; }
            if (m_built && m_age >= RecheckEvery) { m_banned.Clear(); m_built = false; }
            m_sig = groundSig;
            return !m_built;
        }

        /// <summary>고르기: (cx,cz) 깃발, (dx,dz) 문 쪽. probe(x,z) = 그 자리 판정.</summary>
        public void Build(double cx, double cz, double dx, double dz, Func<double, double, PatrolSpot> probe)
        {
            m_points.Clear();
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) { dx = 1; dz = 0; len = 1; }
            double a0 = Math.Atan2(dz / len, dx / len);
            for (int d = 0; d < Dirs; d++)
            {
                double a = a0 + d * (2 * Math.PI / Dirs);
                PatrolSpot? why = null;
                for (int r = 0; r < Radii.Length; r++)
                {
                    if (m_banned.Contains(Key(d, r))) { why = why ?? PatrolSpot.Unreached; continue; }
                    double x = cx + Math.Cos(a) * Radii[r], z = cz + Math.Sin(a) * Radii[r];
                    var s = probe(x, z);
                    if (s == PatrolSpot.Ok) { m_points.Add(new Spot { Dir = d, Ring = r, X = x, Z = z }); why = null; break; }
                    why = why ?? s;
                }
                DroppedWhy[d] = why;
            }
            m_built = true; m_age = 0f;
        }

        /// <summary>순찰 중 Points[i]에 못 닿음 — 그 자리를 막고 다음 Due에서 다시 고름.</summary>
        public void Unreached(int i)
        {
            if (i < 0 || i >= m_points.Count) return;
            m_banned.Add(Key(m_points[i].Dir, m_points[i].Ring));
            m_built = false;
        }

        static int Key(int dir, int ring) => dir * 16 + ring;

        /// <summary>비탈 판정 도우미: 중심과 둘레 네 점 높이 차가 run(m) 거리에서 maxDeg를 넘으면 가파름.</summary>
        public static bool Steep(double h0, double hN, double hE, double hS, double hW, double run, double maxDeg)
        {
            double m = Math.Max(Math.Max(Math.Abs(hN - h0), Math.Abs(hE - h0)), Math.Max(Math.Abs(hS - h0), Math.Abs(hW - h0)));
            return Math.Atan2(m, run) * 180 / Math.PI > maxDeg;
        }
    }
}
