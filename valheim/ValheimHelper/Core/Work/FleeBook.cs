using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 도망 뒤 목표(사용자 결정 2026-10-24, decisions.txt "더 공격적으로" 다): 거점 범위(BaseArea) 안 목표는 도망이 끝나면 다시 하고,
    /// 같은 목표에서 두 번 도망치면 그 일 동안 뺀다. 거점 밖 목표는 첫 도망에 뺀다(구상서 8장 a).
    /// 거점 안 위험 구역도 처음엔 피하지 않는다 — 다만 거점 안 도망이 BaseFleeLimit번 쌓이면(목표가 달라도 같은 적에게 되풀이) 거점 안도 피한다.
    /// </summary>
    public sealed class FleeBook<T>
    {
        public const int BaseFleeLimit = 3;

        readonly Dictionary<T, int> m_counts = new Dictionary<T, int>();
        int m_baseFlees;

        /// <summary>목표 target에서 도망쳤다(target 없음이면 hasTarget false). 그 목표를 이번 일에서 뺄지.</summary>
        public bool Fled(T target, bool hasTarget, bool inBase)
        {
            if (inBase) m_baseFlees++;
            if (!hasTarget) return false;
            if (!inBase) return true;
            m_counts.TryGetValue(target, out int n);
            m_counts[target] = ++n;
            return n >= 2;
        }

        /// <summary>거점 안 자리도 위험 구역 피하기를 적용할지.</summary>
        public bool AvoidInBase => m_baseFlees >= BaseFleeLimit;

        public int BaseFlees => m_baseFlees;
    }
}
