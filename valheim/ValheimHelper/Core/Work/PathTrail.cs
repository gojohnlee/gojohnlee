using System;
using System.Collections.Generic;
using P = ValheimHelper.Core.Work.PathMemory.P;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 길 기억에 넣을 걸은 자취(요청 11-15 ③ 첫 조각): 목표가 바뀌면 Start, 걷는 동안 Step(Spacing 넘게 움직일 때만 점),
    /// 닿으면 Finish가 점 목록을 준다. 한 번에 Jump 넘게 튀면(순간이동·구조) 또는 점이 MaxPoints 넘으면 그 자취는 버림.
    /// 출발~도착 직선이 MinLength 안이면 기억할 값어치 없음(null). 게임 무관.
    /// </summary>
    public sealed class PathTrail
    {
        public const float Spacing = 0.5f, Jump = 3f, MinLength = 3f;
        public const int MaxPoints = 400;
        readonly List<P> m_points = new List<P>();
        bool m_bad;

        public bool Active { get; private set; }
        public P From { get; private set; }
        public int Count => m_points.Count;

        public void Start(P from)
        {
            m_points.Clear();
            m_points.Add(from);
            From = from;
            m_bad = false;
            Active = true;
        }

        public void Stop() { Active = false; m_points.Clear(); }

        public void Step(P pos)
        {
            if (!Active || m_bad) return;
            var last = m_points[m_points.Count - 1];
            float d = Dist(last, pos);
            if (d > Jump) { m_bad = true; return; }
            if (d < Spacing) return;
            if (m_points.Count >= MaxPoints) { m_bad = true; return; }
            m_points.Add(pos);
        }

        /// <summary>닿음: 끝 점을 더해 자취를 주고 멈춤. 버린 자취·짧은 길은 null.</summary>
        public P[] Finish(P to)
        {
            if (!Active) return null;
            Step(to);
            bool ok = !m_bad && m_points.Count >= 2 && Dist(From, to) >= MinLength;
            var r = ok ? m_points.ToArray() : null;
            if (r != null && Dist(r[r.Length - 1], to) > 0.01f) { Array.Resize(ref r, r.Length + 1); r[r.Length - 1] = to; }
            Stop();
            return r;
        }

        static float Dist(P a, P b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
