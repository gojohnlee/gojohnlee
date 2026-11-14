using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 모드 부하 재기(사용자 11-13 00:15 "모드 부하 재기"): 한 판에서 잰 프레임 시간(초)을 요약한다 — 평균·최악·1% 최악(가장 느린 1%의 평균).
    /// 게임 쪽은 Time.unscaledDeltaTime을 모아 여기에 넘긴다. 같은 판을 모드 끔·켬으로 두 번 재서 비교한다.
    /// </summary>
    public static class FrameStats
    {
        public readonly struct Summary
        {
            public readonly int Count;
            public readonly double AvgMs, WorstMs, Worst1PctMs;

            public Summary(int count, double avgMs, double worstMs, double worst1PctMs)
            {
                Count = count;
                AvgMs = avgMs;
                WorstMs = worstMs;
                Worst1PctMs = worst1PctMs;
            }
        }

        /// <summary>frameSecs: 프레임마다 걸린 시간(초). 0 이하·NaN 값은 버린다(일시정지 같은 빈 프레임).</summary>
        public static Summary Summarize(IEnumerable<double> frameSecs)
        {
            var ms = frameSecs.Where(s => s > 0 && !double.IsNaN(s)).Select(s => s * 1000.0).OrderByDescending(v => v).ToList();
            if (ms.Count == 0) return new Summary(0, 0, 0, 0);
            int top = Math.Max(1, (int)Math.Ceiling(ms.Count * 0.01));
            return new Summary(ms.Count, ms.Average(), ms[0], ms.Take(top).Average());
        }

        /// <summary>history·diag 한 줄: "프레임 n장 · 평균 a.a ms · 최악 b.b ms · 1% 최악 c.c ms".</summary>
        public static string ToText(Summary s) =>
            string.Format(CultureInfo.InvariantCulture, "프레임 {0}장 · 평균 {1:0.0} ms · 최악 {2:0.0} ms · 1% 최악 {3:0.0} ms",
                s.Count, s.AvgMs, s.WorstMs, s.Worst1PctMs);
    }
}
