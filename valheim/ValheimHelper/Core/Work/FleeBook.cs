namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 도망 뒤 목표(사용자 10-28, decisions.txt "도망 뒤 목표" — 10-24 "거점 안은 두 번째 도망부터"·BaseFleeLimit 3번 규칙을 바꿈):
    /// 도망친 목표는 거점 안팎 없이 그 일 동안 뺀다(거점 안이면 플레이어가 그 적과 싸울 때만 Guard가 가서 돕고, 아니면 다른 일).
    /// 거점 안 위험 구역은 처음엔 피하지 않고, 거점 안에서 한 번 도망친 뒤부터 피한다(그 적 곁 다른 목표로 되돌아가 또 도망치지 않게).
    /// </summary>
    public sealed class FleeBook<T>
    {
        int m_baseFlees;

        /// <summary>목표 target에서 도망쳤다(target 없음이면 hasTarget false). 그 목표를 이번 일에서 뺄지.</summary>
        public bool Fled(T target, bool hasTarget, bool inBase)
        {
            if (inBase) m_baseFlees++;
            return hasTarget;
        }

        /// <summary>거점 안 자리도 위험 구역 피하기를 적용할지.</summary>
        public bool AvoidInBase => m_baseFlees > 0;

        public int BaseFlees => m_baseFlees;
    }
}
