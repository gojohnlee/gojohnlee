using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 지원을 기다리는 일꾼이 쉴 편한 곳 고르기(사용자 11-13 00:30 "약한 적·덜 몰린·가까운" 순).
    /// 기준 순서: 적 세기(낮을수록) → 몰린 정도(적을수록) → 거리(가까울수록). 같으면 먼저 든 후보.
    /// 적 세기 3(강함) 이상 후보는 기다리는 곳으로 안 씀.
    /// </summary>
    public static class WaitSpot
    {
        /// <summary>이 세기 이상이면 기다리는 곳으로 안 씀.</summary>
        public const int TooStrong = 3;

        /// <summary>후보 한 곳. Threat: 0 없음 · 1 약함 · 2 보통 · 3 강함.</summary>
        public sealed class Cand
        {
            /// <summary>곳 이름(로그용).</summary>
            public string Name;
            /// <summary>가까운 적 세기 0~3.</summary>
            public int Threat;
            /// <summary>같은 곳에 있는 다른 일꾼·동물 수.</summary>
            public int Crowd;
            /// <summary>지금 자리에서 거리(m).</summary>
            public float Dist;
        }

        /// <summary>가장 나은 곳(없으면 null). 입력이 null이면 null.</summary>
        public static Cand Pick(IList<Cand> cands)
        {
            if (cands == null) return null;
            Cand best = null;
            foreach (var c in cands)
            {
                if (c == null || c.Threat >= TooStrong) continue;
                if (best == null || Better(c, best)) best = c;
            }
            return best;
        }

        private static bool Better(Cand x, Cand y)
        {
            if (x.Threat != y.Threat) return x.Threat < y.Threat;
            if (x.Crowd != y.Crowd) return x.Crowd < y.Crowd;
            return x.Dist < y.Dist;
        }
    }
}
