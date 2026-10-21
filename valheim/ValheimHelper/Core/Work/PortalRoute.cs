using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>포탈 한 쌍의 한쪽: 선 자리(입구)와 이 포탈로 들어가면 나오는 자리(짝 포탈 출구). 짝이 없으면 쓸 수 없다.</summary>
    public struct PortalLink
    {
        public float X, Z, Y;
        public float ExitX, ExitZ, ExitY;
    }

    /// <summary>포탈을 쓸지 걸어갈지 고르는 순수 계산(설계 ⑤). 직선 거리로 비교하고, 포탈 앞까지 걷는 길도 직선으로 본다.</summary>
    public static class PortalRoute
    {
        /// <summary>포탈 입구까지 걸어가는 거리가 이보다 멀면 포탈을 보지 않는다(불러온 구역 밖이라 길찾기가 안 됨).</summary>
        public const float MaxWalkToPortal = 150f;
        /// <summary>걸어서 가는 직선 거리보다 이만큼 이상 짧아야 포탈을 쓴다(포탈 기다림·막힘 여유).</summary>
        public const float Margin = 60f;

        /// <summary>
        /// 출발 (fx,fz)에서 도착 (tx,tz)까지 포탈을 타는 편이 나으면 그 포탈 번호(portals 안), 아니면 -1.
        /// 비용 = 입구까지 걷기 + 출구에서 도착까지 걷기. 가장 싼 포탈이 걸어가는 것보다 Margin 넘게 싸야 한다.
        /// </summary>
        public static int Choose(float fx, float fz, float tx, float tz, IList<PortalLink> portals)
        {
            double walk = Dist(fx, fz, tx, tz);
            double best = walk - Margin;
            int pick = -1;
            for (int i = 0; i < portals.Count; i++)
            {
                var p = portals[i];
                double toPortal = Dist(fx, fz, p.X, p.Z);
                if (toPortal > MaxWalkToPortal) continue;
                double cost = toPortal + Dist(p.ExitX, p.ExitZ, tx, tz);
                if (cost < best) { best = cost; pick = i; }
            }
            return pick;
        }

        static double Dist(float ax, float az, float bx, float bz)
        {
            double dx = ax - bx, dz = az - bz;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
