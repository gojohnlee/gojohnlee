namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 사냥감 진척 제한(사용자 11-13 ⑤ — 2분 고정 대신): 사냥감 체력이 줄면 계속, NoHarmSeconds 동안 한 번도 못 깎으면
    /// 다른 놈(놓침·못 닿음 포함). 끝없이 쫓지 않게 MaxSeconds는 진척과 상관없이 끝(행동마다 시간 제한).
    /// LongSeconds(옛 2분)를 넘긴 사냥은 플러그인이 로그에 한 번 남긴다(2분 넘게 걸린 경우 찾기).
    /// </summary>
    public sealed class PreyProgress
    {
        /// <summary>이 시간(초) 동안 체력을 못 깎으면 포기 [기본안·확인 필요].</summary>
        public const double NoHarmSeconds = 60.0;
        /// <summary>진척이 있어도 이 시간(초)이면 끝 [기본안·확인 필요].</summary>
        public const double MaxSeconds = 600.0;
        /// <summary>옛 고정 제한 — 넘으면 기록만.</summary>
        public const double LongSeconds = 120.0;

        public enum Verdict { Keep, NoHarm, TooLong }

        double m_start, m_lastHarm, m_lowest;
        bool m_longNoted;

        /// <summary>새 사냥감(체력·지금 시각).</summary>
        public void Start(double health, double now)
        {
            m_start = m_lastHarm = now;
            m_lowest = health;
            m_longNoted = false;
        }

        /// <summary>걸린 시간(초).</summary>
        public double Elapsed(double now) => now - m_start;

        /// <summary>지금까지 가장 낮았던 체력보다 줄었으면 진척(되찬 체력은 무시).</summary>
        public Verdict Tick(double health, double now)
        {
            if (health < m_lowest - 0.01) { m_lowest = health; m_lastHarm = now; }
            if (now - m_start >= MaxSeconds) return Verdict.TooLong;
            if (now - m_lastHarm >= NoHarmSeconds) return Verdict.NoHarm;
            return Verdict.Keep;
        }

        /// <summary>LongSeconds를 처음 넘긴 때 한 번만 true(로그용).</summary>
        public bool NoteLong(double now)
        {
            if (m_longNoted || now - m_start < LongSeconds) return false;
            return m_longNoted = true;
        }
    }
}
