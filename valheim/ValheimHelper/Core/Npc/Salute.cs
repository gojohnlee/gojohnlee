using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 호위병 경례(사용자 11-09 요청): 문 옆 경계로 서 있을 때 플레이어가 Range m 안을 지나가면 그쪽을 보고 경례(Hold초).
    /// 같은 사람은 Again초에 한 번, 적이 보이면 안 함.
    /// </summary>
    public sealed class Salute
    {
        public const float Range = 4f, Again = 60f, Hold = 2f;

        readonly Dictionary<long, float> m_last = new Dictionary<long, float>();
        float m_until = float.MinValue;

        /// <summary>지금 경례하는 상대.</summary>
        public long Who { get; private set; }
        public bool Busy(float now) => now < m_until;

        /// <summary>who가 dist m에 있다(foe = 적이 보임). 경례를 시작하면 참.</summary>
        public bool Try(long who, float dist, float now, bool foe)
        {
            if (foe || dist > Range || Busy(now)) return false;
            if (m_last.TryGetValue(who, out float t) && now - t < Again) return false;
            m_last[who] = now;
            m_until = now + Hold;
            Who = who;
            return true;
        }

        /// <summary>적이 나타남: 하던 경례를 멈춤.</summary>
        public void Stop() => m_until = float.MinValue;
    }
}
