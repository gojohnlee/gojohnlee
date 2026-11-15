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

            /// <summary>33ms(30fps 아래) 넘는 프레임이 분당 몇 장(사용자 11-15 끊김 판정).</summary>

            public readonly double SlowPerMin;

            public Summary(int count, double avgMs, double worstMs, double worst1PctMs, double slowPerMin = 0)
            {
                SlowPerMin = slowPerMin;
                Count = count;
                AvgMs = avgMs;
                WorstMs = worstMs;
                Worst1PctMs = worst1PctMs;
            }
        }

        /// <summary>끊김으로 치는 한 프레임 길이(33ms = 30fps 아래).</summary>
        public const double SlowMs = 33.0;
        /// <summary>
        /// 끊김 판정(사용자 11-15 "부하 재기 끝내는 조건"): 모드 켬이 끔보다 1% 최악이 +5ms 넘게 나쁘거나,
        /// 33ms 넘는 프레임이 끔보다 분당 1장 넘게 많으면 끊김. 그때만 기능별 몫을 재 줄인다.
        /// </summary>
        public static bool Stutters(Summary off, Summary on) =>
            on.Worst1PctMs - off.Worst1PctMs > 5.0 || on.SlowPerMin - off.SlowPerMin > 1.0;
        /// <summary>frameSecs: 프레임마다 걸린 시간(초). 0 이하·NaN 값은 버린다(일시정지 같은 빈 프레임).</summary>
        public static Summary Summarize(IEnumerable<double> frameSecs)
        {
            var ms = frameSecs.Where(s => s > 0 && !double.IsNaN(s)).Select(s => s * 1000.0).OrderByDescending(v => v).ToList();
            if (ms.Count == 0) return new Summary(0, 0, 0, 0);
            int top = Math.Max(1, (int)Math.Ceiling(ms.Count * 0.01));
            double mins = ms.Sum() / 60000.0;
            int slow = ms.Count(v => v > SlowMs);
            return new Summary(ms.Count, ms.Average(), ms[0], ms.Take(top).Average(), mins > 0 ? slow / mins : 0);
        }

        /// <summary>같은 판을 여러 번 잰 요약들의 칸별 중간값(장수도 중간값). 짝수 개면 가운데 둘의 평균, 비면 0.</summary>
        public static Summary Median(IEnumerable<Summary> runs)
        {
            var l = runs.Where(r => r.Count > 0).ToList();
            if (l.Count == 0) return new Summary(0, 0, 0, 0);
            double Mid(Func<Summary, double> f)
            {
                var v = l.Select(f).OrderBy(x => x).ToList();
                int h = v.Count / 2;
                return v.Count % 2 == 1 ? v[h] : (v[h - 1] + v[h]) / 2.0;
            }
            return new Summary((int)Math.Round(Mid(r => r.Count)), Mid(r => r.AvgMs), Mid(r => r.WorstMs), Mid(r => r.Worst1PctMs), Mid(r => r.SlowPerMin));
        }

        /// <summary>history·diag 한 줄: "프레임 n장 · 평균 a.a ms · 최악 b.b ms · 1% 최악 c.c ms".</summary>
        public static string ToText(Summary s) =>
            string.Format(CultureInfo.InvariantCulture, "프레임 {0}장 · 평균 {1:0.0} ms · 최악 {2:0.0} ms · 1% 최악 {3:0.0} ms · 33ms 넘음 분당 {4:0.0}",
                s.Count, s.AvgMs, s.WorstMs, s.Worst1PctMs, s.SlowPerMin);
    }
}
