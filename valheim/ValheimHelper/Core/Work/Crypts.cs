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
    }

    /// <summary>철(고철)을 캐러 갈 지하묘지 고르기: 플레이어가 이미 문을 연 곳 중 가장 가까운 곳(구상서 7장 기본안).</summary>
    public static class Crypts
    {
        /// <summary>이보다 먼 지하묘지는 고르지 않는다(걸어서 다녀오기엔 너무 멂, 포탈은 이동 단계가 따로 고름).</summary>
        public const float MaxDistance = 3000f;

        /// <summary>(fx,fz)에서 갈 지하묘지 번호, 없으면 -1. 닫힌 곳·피할 곳·너무 먼 곳은 뺀다.</summary>
        public static int Choose(float fx, float fz, IList<CryptSpot> spots)
        {
            int pick = -1;
            double best = MaxDistance;
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                if (!s.Open || s.Avoid) continue;
                double dx = s.X - fx, dz = s.Z - fz;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d <= best) { best = d; pick = i; }
            }
            return pick;
        }
    }
}
