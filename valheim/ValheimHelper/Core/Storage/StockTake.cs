using System.Collections.Generic;

namespace ValheimHelper.Core.Storage
{
    /// <summary>
    /// 상자 재료로 바로 제작·건축(상자 기능 ①, 사용자 결정 2026-10-23): 내 가방 먼저 → 가까운 상자부터.
    /// 게임 무관 계산만: 있는지(Enough)와 상자마다 몇 개 뺄지(Plan).
    /// </summary>
    public static class StockTake
    {
        /// <summary>가방 + 상자 합이 필요한 만큼 되나. need ≤ 0이면 늘 됨.</summary>
        public static bool Enough(int need, int bag, int chests) => need <= 0 || bag + chests >= need;

        /// <summary>
        /// 가방에 모자라는 몫을 상자(가까운 순)에서 몇 개씩 뺄지. 상자 합으로도 모자라면 null(아무것도 안 뺌 — 반만 빼서 재료가 사라지지 않게).
        /// </summary>
        public static int[] Plan(int need, int bag, IList<int> chests)
        {
            var take = new int[chests.Count];
            int left = need - (bag > 0 ? bag : 0);
            for (int i = 0; i < chests.Count && left > 0; i++)
            {
                int n = chests[i] < left ? chests[i] : left;
                if (n <= 0) continue;
                take[i] = n;
                left -= n;
            }
            return left > 0 ? null : take;
        }
    }
}
