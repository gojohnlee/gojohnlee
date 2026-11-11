using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 막힘 1초·3초(사용자 11-11, decisions 10절): 진척(= 남은 길 길이, 가장 짧았던 값 기준) 없이
    /// 플레이어가 안 보면 UnseenSeconds·보면 SeenSeconds 지나면 막힘. 공격·문·헤엄·넉백 중(paused)엔 시계 멈춤.
    /// 안 보게 된 뒤 UnseenGrace 초 지나야 '안 봄'. 같은 목표로 순간이동 MaxHops 번 하고도 못 닿으면 건너뜀(HopsSpent).
    /// 보는지(Watching)는 플러그인이 카메라 시야·레이캐스트·거리로 재서 넘긴다. 목표가 바뀌면 Reset.
    /// </summary>
    public sealed class StuckClock
    {
        public const double UnseenSeconds = 1.0;
        public const double SeenSeconds = 3.0;
        public const double UnseenGrace = 2.0;
        /// <summary>이만큼(m) 남은 길이 줄어야 진척으로 친다(제자리 흔들림·길 다시 찾기 오차 무시).</summary>
        public const double MinGain = 0.3;
        public const int MaxHops = 2;
        /// <summary>이 거리(m) 안 플레이어는 시야·가림과 상관없이 본다고 친다.</summary>
        public const double NearSee = 20.0;
        /// <summary>이 거리(m) 밖은 시야 안이어도 안 본다고 친다 [기본안·확인 필요].</summary>
        public const double FarSee = 100.0;

        double m_best = double.PositiveInfinity;
        double m_noGain;
        double m_lastSeen = double.NegativeInfinity;
        int m_replans;
        /// <summary>진척 없는 동안 새 길로 기준을 다시 잡는 횟수 한도(길만 자꾸 바뀌며 제자리면 막힘).</summary>
        public const int MaxReplans = 3;

        /// <summary>진척 없이 흐른 시간(멈춘 시간 뺌).</summary>
        public double NoGain => m_noGain;
        /// <summary>지금까지 가장 짧았던 남은 길(m).</summary>
        public double Best => m_best;
        /// <summary>이 목표로 한 순간이동 수.</summary>
        public int Hops { get; private set; }
        public bool HopsSpent => Hops >= MaxHops;

        /// <summary>새 목표: 시계·순간이동 수 모두 처음부터(본 시각은 그대로 — 사람 쪽 일).</summary>
        public void Reset()
        {
            m_best = double.PositiveInfinity;
            m_noGain = 0; m_replans = 0;
            Hops = 0;
        }

        /// <summary>같은 목표로 순간이동했다: 시계만 처음부터, 횟수는 셈.</summary>
        public void NoteHop()
        {
            Hops++;
            m_best = double.PositiveInfinity;
            m_noGain = 0; m_replans = 0;
        }

        /// <summary>지금 '보는 중'인가(마지막으로 본 뒤 UnseenGrace 안).</summary>
        public bool Seen(double now) => now - m_lastSeen < UnseenGrace;

        public double Limit(double now) => Seen(now) ? SeenSeconds : UnseenSeconds;

        /// <summary>매 틱. remaining = 남은 길 길이(m, 길 없으면 직선), seenNow = 지금 플레이어가 보는지,
        /// paused = 공격·문·헤엄·넉백 중. 막혔으면 true(부른 쪽이 복구 후 NoteHop 또는 Reset).
        /// newPath = 이번 틱에 길을 새로 받음(돌아가는 길이면 남은 길이 늘어남) → 기준을 다시 잡음(진척 없이 MaxReplans번까지).</summary>
        public bool Tick(double now, double dt, double remaining, bool seenNow, bool paused, bool newPath = false)
        {
            if (seenNow) m_lastSeen = now;
            if (double.IsNaN(remaining)) return false;
            if (newPath && remaining > m_best && m_replans < MaxReplans) { m_replans++; m_best = remaining; m_noGain = 0; return false; }
            if (remaining < m_best - MinGain || double.IsPositiveInfinity(m_best))
            {
                m_best = remaining;
                m_noGain = 0; m_replans = 0;
                return false;
            }
            if (remaining < m_best) m_best = remaining; // 조금씩 줄어도 기준은 따라감(시계는 그대로)
            if (!paused && dt > 0) m_noGain += dt;
            return m_noGain >= Limit(now);
        }

        /// <summary>한 플레이어가 일꾼을 보는지: NearSee 안은 봄, FarSee 밖은 안 봄, 그 사이는 시야 안이고 가리지 않을 때.</summary>
        public static bool Watching(double dist, bool inView, bool lineClear)
        {
            if (dist <= NearSee) return true;
            if (dist > FarSee) return false;
            return inView && lineClear;
        }

        /// <summary>원격 플레이어(카메라 없음)의 시야: 몸 앞 방향(fx,fz)에서 ±90° 안인가(뒤는 안 봄).</summary>
        public static bool InFront(double fx, double fz, double dx, double dz) => fx * dx + fz * dz >= 0;
    }
}
