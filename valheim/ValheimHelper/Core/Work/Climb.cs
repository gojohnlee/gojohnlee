using System;
using System.Collections.Generic;

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

        /// <summary>걷다 빠진 구덩이에서 점프·경사로 깎기를 해도 이만큼(초) 못 나오면 가장 가까운 가장자리 위로 옮긴다(사용자 결정 2026-10-23, E018).</summary>
        public const double PitEscapeSeconds = 30;
        /// <summary>구덩이 기록 자리에서 이만큼(m) 넘게 벗어나면 나온 것으로 본다.</summary>
        public const double PitLeftRadius = 6;
        /// <summary>가장자리 찾기: 8방향으로 EdgeStep(m)마다 EdgeSteps번 잰다.</summary>
        public const double EdgeStep = 1;
        public const int EdgeSteps = 8;
        /// <summary>가장자리 위 = 발보다 Wall 넘게 높고 바로 바깥 한 칸과 높이 차가 이 값(m) 안(더 오르지 않는 평평한 땅).</summary>
        public const double EdgeFlat = 0.5;

        /// <summary>
        /// 구덩이 가장자리 위 후보를 가까운 순으로(같은 거리면 가려는 쪽 0번에 가까운 방향 먼저).
        /// rise[dir][k] = 방향 dir(0 = 가려는 쪽, 45°씩)으로 (k+1)·EdgeStep m 떨어진 땅이 발보다 얼마나 높은지(못 재면 NaN).
        /// 방향마다 처음으로 "벽 위이고 바깥 칸과 평평하고 그 방향 남은 칸 중 가장 높은 데쯤(원뿔 벽 중간 턱이 아님)"인 칸 하나.
        /// </summary>
        public static List<Edge> EdgeSpots(double[][] rise)
        {
            var found = new List<Edge>();
            if (rise == null || rise.Length != 8) return found;
            for (int d = 0; d < 8; d++)
            {
                var r = rise[d];
                if (r == null) continue;
                for (int k = 0; k + 1 < r.Length; k++)
                {
                    if (double.IsNaN(r[k]) || double.IsNaN(r[k + 1])) continue;
                    if (r[k] > Wall && Math.Abs(r[k + 1] - r[k]) <= EdgeFlat && r[k] >= Top(r, k) - EdgeFlat) { found.Add(new Edge(d, k)); break; }
                }
            }
            int Turn(int d) => Math.Min(d, 8 - d);
            found.Sort((a, b) => a.Step != b.Step ? a.Step.CompareTo(b.Step)
                : Turn(a.Dir) != Turn(b.Dir) ? Turn(a.Dir).CompareTo(Turn(b.Dir)) : a.Dir.CompareTo(b.Dir));
            return found;
        }

        static double Top(double[] r, int from)
        {
            double top = double.MinValue;
            for (int i = from; i < r.Length; i++) if (!double.IsNaN(r[i]) && r[i] > top) top = r[i];
            return top;
        }
    }

    /// <summary>구덩이 가장자리 후보: 방향 Dir(0 = 가려는 쪽, 45°씩), 칸 Step(거리 (Step+1)·EdgeStep m).</summary>
    public readonly struct Edge
    {
        public readonly int Dir, Step;
        public Edge(int dir, int step) { Dir = dir; Step = step; }
    }
}
