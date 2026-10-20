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
    }
}
