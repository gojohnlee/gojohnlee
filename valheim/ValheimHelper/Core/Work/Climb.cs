namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 구덩이 판단 (2026-10-21 사용자 관찰: 일꾼이 점프를 안 해서 돌 캐다 판 구덩이에서 못 나옴).
    /// 막히면 먼저 뛰어 보고(바닐라 점프), 그래도 갇혔으면 곡괭이로 벽에 경사로를 깎아 나온다.
    /// 경사로 깎기는 자기가 갇힌 구덩이에서만 한다 — 그냥 가파른 언덕·절벽에 길을 내는 건 길 만들기(구상서 6-5, 시킬 때만).
    /// </summary>
    public static class Climb
    {
        /// <summary>이보다 높은 벽은 점프로 못 넘는다고 본다(m).</summary>
        public const double Wall = 1.0;

        /// <summary>
        /// 8방향 둘레 땅이 발보다 얼마나 높은지(m). 0번이 가려는 쪽, 45°씩 시계방향.
        /// 6방향 이상이 벽이면 구덩이에 갇혔다.
        /// </summary>
        public static bool IsPit(double[] rise)
        {
            if (rise == null || rise.Length != 8) return false;
            int walls = 0;
            foreach (var r in rise) if (r > Wall) walls++;
            return walls >= 6;
        }

        /// <summary>앞쪽 반원(가려는 쪽 ±90°) 중 가장 낮은 벽. 높이가 같으면 가려는 쪽에 가까운 것.</summary>
        public static int CarveDir(double[] rise)
        {
            int[] order = { 0, 1, 7, 2, 6 };
            int best = 0;
            foreach (int i in order)
                if (rise[i] < rise[best] - 0.05) best = i;
            return best;
        }

        /// <summary>도망 중 구덩이에 갇혀 이만큼(초) 3m 안을 못 벗어나면 깃발로 순간이동(사용자 결정 2026-10-22: 도망을 못 가니까).</summary>
        public const double FleeTrapSeconds = 6;
        /// <summary>헤엄치며 이만큼(초) 3m 안을 못 벗어나면(물 찬 구덩이에서 헤엄만 함) 깃발로 순간이동(사용자 결정 2026-10-22).</summary>
        public const double SwimTrapSeconds = 20;
        /// <summary>이 거리(m) 안에서만 맴돌면 못 움직인 것으로 센다.</summary>
        public const double TrapRadius = 3;

        /// <summary>
        /// 갇혀서 깃발로 순간이동할지: "water" = 물 찬 구덩이(헤엄만), "fleePit" = 도망 중 구덩이, null = 아님.
        /// still = 3m 안에 머문 시간. 그냥 걷다가 구덩이에 빠진 건 점프·경사로 깎기가 맡는다(여기 안 옴).
        /// </summary>
        public static string TrapRescue(bool fleeing, bool pit, bool swimming, double still)
        {
            if (swimming && still >= SwimTrapSeconds) return "water";
            if (fleeing && pit && still >= FleeTrapSeconds) return "fleePit";
            return null;
        }
    }
}
