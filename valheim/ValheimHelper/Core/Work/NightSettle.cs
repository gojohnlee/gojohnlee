namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 밤사이 정산(요청 R3, 사용자 결정 2026-10-23) 규칙: 모두 자서 잠자기 넘김(EnvMan.IsTimeSkipping)이 끝난 순간 한 번.
    /// 넘김이 시작될 때 거점에 있던 일꾼만, 일꾼마다 그 아침 날짜를 저장해 같은 아침에 두 번 하지 않는다(다시 불러와도).
    /// </summary>
    public sealed class NightSettle
    {
        public enum Edge { None, Started, Ended }

        bool m_skipping;

        /// <summary>매 프레임 넘김 여부를 넣는다 → 막 시작한 프레임 Started, 막 끝난 프레임 Ended.</summary>
        public Edge Step(bool skipping)
        {
            Edge e = skipping == m_skipping ? Edge.None : skipping ? Edge.Started : Edge.Ended;
            m_skipping = skipping;
            return e;
        }

        /// <summary>이 아침(day)에 아직 정산 안 했으면 true. lastDay = 저장된 마지막 정산 날짜(없으면 -1).</summary>
        public static bool Due(int day, int lastDay) => day > lastDay;

        /// <summary>거점에 있는 것으로 치는 깃발과의 거리(m).</summary>
        public const float BaseRadius = 30f;

        public static bool AtBase(float distanceXZ) => distanceXZ <= BaseRadius;

        /// <summary>
        /// 아침에 거두는 밭: 밭 중심이 깃발에서 이 거리(m)+밭 반경 안(사용자 요청 2026-10-23 — 집이 30m를 거의 차지해 밭은 마당·더 먼 곳).
        /// 일꾼(먼 구역 유지)·호스트 둘레 2구역(≥128m)은 불러와져 있어 이 안의 작물은 보임. 안 불러와진 작물은 안 보여 그냥 건너뜀.
        /// </summary>
        public const float FarmRadius = 100f;

        public static bool FarmNear(float centerDistanceXZ, float fieldRadius) => centerDistanceXZ <= FarmRadius + fieldRadius;
    }
}
