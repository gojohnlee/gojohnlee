using System;
using System.Collections.Generic;
using P = ValheimHelper.Core.Work.PathMemory.P;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 길 기억에 넣을 걸은 자취(요청 11-15 ③ 첫 조각): 목표가 바뀌면 Start, 걷는 동안 Step(Spacing 넘게 움직일 때만 점),
    /// 닿으면 Finish가 점 목록을 준다. 한 번에 Jump 넘게 튀면(순간이동·구조) 또는 점이 MaxPoints 넘으면 그 자취는 버림.
    /// 출발~도착 직선이 MinLength 안이면 기억할 값어치 없음(null).
    /// 성공한 길만(사용자 11-15 "내렸는데 불타면 성공적인 길이 아님"): Spoil(피해·막힘·문 못 엶)·되돌아감·길/직선 1.3 넘음이면 버리고 SpoilReason. 게임 무관.
    /// </summary>
    public sealed class PathTrail
    {
        public const float Spacing = 0.5f, Jump = 3f, MinLength = 3f;
        public const float MaxRatio = 1.3f, FloorRise = 1.3f, BackTouch = 0.4f;
        public const int MaxPoints = 400, BackGap = 4;
        readonly List<P> m_points = new List<P>();
        bool m_bad;

        public bool Active { get; private set; }
        public P From { get; private set; }
        public int Count => m_points.Count;
        /// <summary>버린 까닭(Finish 뒤에도 남음, 다음 Start에 지움). null이면 버리지 않음.</summary>
        public string SpoilReason { get; private set; }

        public void Start(P from)
        {
            m_points.Clear();
            m_points.Add(from);
            From = from;
            m_bad = false;
            SpoilReason = null;
            Active = true;
        }

        public void Stop() { Active = false; m_points.Clear(); }

        /// <summary>걷는 동안 피해·막힘·멈칫·문 못 엶: 이 자취는 기억 안 함(처음 까닭만 남김).</summary>
        public void Spoil(string reason)
        {
            if (!Active || m_bad) return;
            m_bad = true;
            SpoilReason = reason;
        }

        public void Step(P pos)
        {
            if (!Active || m_bad) return;
            var last = m_points[m_points.Count - 1];
            float d = Dist(last, pos);
            if (d > Jump) { Spoil("순간이동"); return; }
            if (d < Spacing) return;
            if (m_points.Count >= MaxPoints) { Spoil("점 너무 많음"); return; }
            // 되돌아감(헛걸음): BackGap점(약 2m) 넘게 앞에 지난 자리 곁으로 다시 옴
            for (int i = m_points.Count - BackGap; i >= 0; i--)
                if (Dist(m_points[i], pos) < BackTouch) { Spoil("되돌아감"); return; }
            m_points.Add(pos);
        }

        /// <summary>닿음: 끝 점을 더해 자취를 주고 멈춤. 버린 자취·짧은 길은 null.</summary>
        public P[] Finish(P to)
        {
            if (!Active) return null;
            Step(to);
            float straight = Dist(From, to);
            bool ok = !m_bad && m_points.Count >= 2 && straight >= MinLength;
            // 길/직선이 집 걸음 기준(1.3) 넘음 — 다른 층(높이 FloorRise 넘게 차이)은 계단을 돌아 늘 길어 안 봄(WalkTrace도 직선 대신 남은 길)
            if (ok && Math.Abs(to.Y - From.Y) <= FloorRise)
            {
                float len = Dist(m_points[m_points.Count - 1], to);
                for (int i = 1; i < m_points.Count; i++) len += Dist(m_points[i - 1], m_points[i]);
                if (len > straight * MaxRatio) { SpoilReason = $"길/직선 {len / straight:0.00}"; ok = false; }
            }
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
