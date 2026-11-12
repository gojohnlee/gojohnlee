using System;

namespace ValheimHelper.Core.Npc
{
    public enum PatrolGoal { Post, Point, Stand }

    /// <summary>
    /// 호위병 경계(사용자 11-09 "집 문 옆에 서서 경계, 일정 시간마다 집 주변 돌며 몬스터 처리"): 쉬는 호위병은 문 옆 자리(Post)에 서 있다가
    /// 자리에 선 시간이 Every만큼 차면 집 둘레 Points개 점을 차례로 돌고(점마다 PointLimit초 — 못 닿으면 다음 점) 문 옆으로 돌아온다.
    /// 보이는 적과의 싸움은 경계(Guard)가 따로 하고 끝나면 순찰을 이어 감. 다른 일(따라가기 등)로 끊겼다 다시 쉬면 Reset으로 문 옆부터(기다린 시간은 그대로).
    /// 문 옆으로 PostLimit초 안에 못 가면 그 자리에 선다(다음 순찰까지).
    /// </summary>
    public sealed class Patrol
    {
        /// <summary>순찰 간격(초, 문 옆에 선 시간) — 기본안 11-09.</summary>
        public const float Every = 600f;
        /// <summary>집(거점) 둘레 반경(m) — 기본안 11-09.</summary>
        public const float Radius = 20f;
        public const float PointLimit = 40f, PostLimit = 60f;
        public const int Points = 4;

        float m_wait = Every, m_t;
        bool m_gaveUp;

        /// <summary>-1 = 문 옆, 0..Points-1 = 가는 순찰 점.</summary>
        public int Point { get; private set; } = -1;
        public bool Walking => Point >= 0;
        /// <summary>끝까지 돈 순찰 수.</summary>
        public int Rounds { get; private set; }
        /// <summary>다음 순찰까지 남은 초.</summary>
        public float Wait => m_wait;

        /// <summary>바로 순찰(시험).</summary>
        public void Now() => m_wait = 0f;

        /// <summary>끊겼다 다시 쉼: 문 옆부터. 기다림은 그대로.</summary>
        public void Reset() { Point = -1; m_t = 0f; m_gaveUp = false; }

        /// <summary>reached = 지금 목표(문 옆 또는 Point 점)에 닿음. skip = 그 점은 못 감(물 등) — 바로 다음 점.
        /// count = 이번 바퀴 점 수(PatrolRoute, 기본 Points), limit = 이 점까지 시간 한도(먼 점은 길게, 기본 PointLimit).</summary>
        public PatrolGoal Tick(float dt, bool reached, bool skip = false, int count = Points, float limit = PointLimit)
        {
            m_t += dt;
            if (Point < 0)
            {
                if (!reached && !m_gaveUp && m_t > PostLimit) m_gaveUp = true;
                if (!reached && !m_gaveUp) return PatrolGoal.Post;
                m_wait -= dt;
                if (m_wait > 0f) return PatrolGoal.Stand;
                m_wait = Every; Point = 0; m_t = 0f; m_gaveUp = false;
                return PatrolGoal.Point;
            }
            if (reached || skip || m_t > limit)
            {
                m_t = 0f;
                if (++Point >= Math.Max(1, count)) { Point = -1; Rounds++; return PatrolGoal.Post; }
            }
            return PatrolGoal.Point;
        }

        /// <summary>순찰 점 i: 거점(cx,cz)에서 문 쪽 방향(dx,dz)을 0번으로 90°씩 돈 반경 r 자리.</summary>
        public static void PointAt(double cx, double cz, double dx, double dz, int i, double r, out double x, out double z)
        {
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) { dx = 1; dz = 0; len = 1; }
            double a = Math.Atan2(dz / len, dx / len) + i * (2 * Math.PI / Points);
            x = cx + Math.Cos(a) * r;
            z = cz + Math.Sin(a) * r;
        }
    }
}
