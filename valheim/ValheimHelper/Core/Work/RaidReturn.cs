using ValheimHelper.Core.Npc;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 습격 중 먼 일꾼(사용자 11-14 ⑧ 답): 일꾼이 거점에 도착할 때 습격 남은 시간이 전체의 절반 이상이면 귀환하고, 아니면 하던 일을 계속.
    /// 도착 예상 = 거리 ÷ 걷는 속도(Departure.WalkSpeed), 습격 남은 시간 = 게임 RandomEvent의 m_duration − m_time
    /// (1.0.21 디컴파일: Update가 dt를 m_time에 더하고 m_duration을 넘으면 끝, m_pauseIfNoPlayerInArea면 멈춤).
    /// </summary>
    public static class RaidReturn
    {
        /// <summary>전체 시간에서 도착 때 이 비율 이상 남아 있어야 귀환 [사용자 11-14 답].</summary>
        public const double HalfShare = 0.5;

        /// <summary>duration = 습격 전체 시간(초), elapsed = 지금까지 지난 시간(초), distanceToHome = 지금 거점까지 거리(m).</summary>
        public static bool GoHome(double duration, double elapsed, double distanceToHome)
        {
            double left = duration - elapsed;
            double arrive = distanceToHome / Departure.WalkSpeed;
            return left - arrive >= duration * HalfShare;
        }
    }
}
