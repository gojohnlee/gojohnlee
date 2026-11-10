using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 우리 솎기 수(사용자 11-10 가축 1): 종별로 세고, 다 자란 것(성체) 최소 MinAdults마리(짝)는 늘 남긴다.
    /// 성체 2 + 새끼 6이면 지금은 안 잡고 새끼가 자라 성체가 늘면 그때 잡는다.
    /// </summary>
    public static class Herd
    {
        public const int MinAdults = 2;

        /// <param name="total">그 우리에 있는 그 종 수(새끼 포함).</param>
        /// <param name="adults">그 가운데 잡아도 되는 성체 수(이름 없음·따라가지 않음).</param>
        /// <param name="max">한 종에 둘 수(새끼 포함).</param>
        public static int ToCull(int total, int adults, int max) => Math.Max(0, Math.Min(total - max, adults - MinAdults));
    }
}
