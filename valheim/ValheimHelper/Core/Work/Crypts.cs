using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>늪 지하묘지 하나(설계 ⑦): 입구 자리와 문이 열렸는지. 위험해서 물러난 곳은 Avoid로 한동안 고르지 않는다.</summary>
    public struct CryptSpot
    {
        public float X, Z;
        public bool Open;
        public bool Avoid;
        /// <summary>안에 고철 더미가 하나도 안 남았다(세어 봄). 다 캔 곳은 고르지 않는다.</summary>
        public bool Empty;
        /// <summary>문은 닫혔지만 일꾼이 열 수 있다(플레이어가 그 열쇠를 얻었다 — Unlocks).</summary>
        public bool CanOpen;
    }

    /// <summary>
    /// 철(고철)을 캐러 갈 지하묘지 고르기(사용자 결정 2026-10-22): 갈 수 있는 거리 안에서 이미 열린 곳(가까운 곳부터)을 다 캔 뒤,
    /// 남은 게 없을 때만 닫힌 곳을 연다(플레이어가 늪 열쇠를 얻었을 때만). 열쇠가 없으면 열린 곳만.
    /// </summary>
    public static class Crypts
    {
        /// <summary>이보다 먼 지하묘지는 고르지 않는다(걸어서 다녀오기엔 너무 멂, 포탈은 이동 단계가 따로 고름).</summary>
        public const float MaxDistance = 3000f;

        /// <summary>(fx,fz)에서 갈 지하묘지 번호, 없으면 -1. 열린 곳이 먼저, 없으면 열 수 있는 닫힌 곳. 다 캔 곳·피할 곳·너무 먼 곳은 뺀다.</summary>
        public static int Choose(float fx, float fz, IList<CryptSpot> spots)
        {
            int pick = Nearest(fx, fz, spots, true);
            return pick >= 0 ? pick : Nearest(fx, fz, spots, false);
        }

        /// <summary>갈 곳이 없을 때 까닭: 다 캔 곳이 있으면 true(새 지하묘지가 필요), 아니면 false(문이 닫혀서 — 열어 달라).</summary>
        public static bool MinedOut(IList<CryptSpot> spots)
        {
            foreach (var s in spots)
                if (s.Empty) return true;
            return false;
        }

        static int Nearest(float fx, float fz, IList<CryptSpot> spots, bool open)
        {
            int pick = -1;
            double best = MaxDistance;
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                if (s.Avoid || s.Empty) continue;
                if (open ? !s.Open : s.Open || !s.CanOpen) continue;
                double dx = s.X - fx, dz = s.Z - fz;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d <= best) { best = d; pick = i; }
            }
            return pick;
        }
    }
}
