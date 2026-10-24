namespace ValheimHelper.Core.Storage
{
    /// <summary>
    /// 일꾼 상자 정리(상자 기능 ②, 사용자 결정 2026-10-23, decisions.txt "상자 기능 확정"): 같은 물건은 가장 많이 든 상자로 모은다
    /// (역할 이름표 상자가 있으면 그쪽 — 고를 수 있는 상자는 Plugin이 ChestNames.Select로 정해 줌). 개인·보물·열린 상자는 Plugin이 아예 뺀다.
    /// </summary>
    public static class ChestTidy
    {
        /// <summary>
        /// 그 물건을 모을 상자 번호, 옮길 게 없으면 -1. counts[i] = 상자 i의 그 물건 수, canTarget[i] = 모으는 상자가 될 수 있음.
        /// 고를 수 있는 상자 중 가장 많이 든 것(같으면 앞 번호 = 가까운 것). 모은 뒤 다시 불러도 같은 상자 → 왔다 갔다 안 함.
        /// 그 물건이 든 상자가 모으는 상자 하나뿐이면 -1.
        /// </summary>
        public static int Target(int[] counts, bool[] canTarget)
        {
            int best = -1;
            for (int i = 0; i < counts.Length; i++)
                if (canTarget[i] && (best < 0 || counts[i] > counts[best])) best = i;
            if (best < 0) return -1;
            for (int i = 0; i < counts.Length; i++)
                if (i != best && counts[i] > 0) return best;
            return -1;
        }
    }
}
