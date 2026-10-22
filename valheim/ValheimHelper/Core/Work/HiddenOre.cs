namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 땅에 묻힌 광맥(산 은, 바닐라 Beacon이 붙은 것 — 위시본이 찾아 주는 것) 고르기(구상서 7-1 산, 사용자 결정 2026-10-22):
    /// 드러난(땅 위로 나왔거나 누가 이미 깨기 시작한) 광맥은 늘 보이고 먼저 캔다. 묻힌 광맥은 플레이어가 찾는 물건(위시본)을 얻었을 때만 알고,
    /// 드러난 게 시야에 없을 때만 고른다. 일꾼은 위시본을 들고 다니지 않는다(Unlocks).
    /// </summary>
    public static class HiddenOre
    {
        /// <summary>윗면이 땅보다 이만큼 넘게 나와 있으면 드러난 것(m). GatherJob.RockAim의 "땅 위 조각" 기준과 같다.</summary>
        public const float ExposedMargin = 0.3f;

        /// <summary>묻힌 광맥에 주는 점수 덧셈(m): 시야(44m) 안의 드러난 것보다 늘 뒤.</summary>
        public const float BuriedPenalty = 1000f;

        /// <summary>윗면 높이 top이 땅 높이 ground보다 나와 있나.</summary>
        public static bool Exposed(float top, float ground) => top > ground + ExposedMargin;

        /// <summary>일꾼이 이 광맥을 아는가: 숨는 종류가 아니거나, 드러났거나, 찾는 물건을 얻었다.</summary>
        public static bool Known(bool hidden, bool exposed, bool haveFinder) => !hidden || exposed || haveFinder;

        /// <summary>고르기 점수(작을수록 먼저): 거리 − 가산점, 묻힌 숨는 광맥은 BuriedPenalty를 더한다.</summary>
        public static float Score(float dist, float priority, bool hidden, bool exposed) =>
            dist - priority + (hidden && !exposed ? BuriedPenalty : 0f);
    }
}
