using System;

namespace ValheimHelper.Core.Npc
{
    public enum RetreatWay
    {
        /// <summary>거점(온 길) 쪽으로.</summary>
        Home,
        /// <summary>거점 근처: 적 반대쪽으로.</summary>
        Away,
        /// <summary>적이 거점 쪽에 있다: 적에게서 멀어지되 거점 쪽으로 돌아서.</summary>
        Around,
    }

    /// <summary>
    /// 물러남·도망 방향(구상서 8장 d "온 길로", 플러그인 Guard.AwayPoint). 수평 x,z만.
    /// 거점이 멀고 적 반대쪽과 크게 어긋나지 않으면 거점 쪽, 거점 근처면 적 반대쪽.
    /// 적이 거점 쪽에 있으면 적 반대쪽으로 곧장 가지 않고 거점 쪽 옆으로 돌아간다 — 곧장 가면 쫓아오는 적이 늘 거점 쪽에 남아
    /// 물러날 때마다 거점에서 40~60m씩 멀어진다(회차 138 검은숲 vh_woodtest: 귀환 중 회색난쟁이 하나에 쫓겨 거점에서 682m).
    /// 쫓기는 중(적이 바로 뒤)이면 적 반대쪽에서 ChasedMinDot 각도 안으로만 튼다: 곧장 쫓는 적과의 간격은
    /// 내 속도 × cos(꺾은 각) − 적 속도로 줄어드니 많이 꺾으면 따라잡힌다.
    /// </summary>
    public static class Retreat
    {
        /// <summary>거점이 이보다 가까우면 적 반대쪽.</summary>
        public const double HomeNear = 10.0;
        /// <summary>쫓기지 않을 때: 거점 방향과 적 반대쪽의 내적이 이보다 크면 거점 쪽(0.3.7부터 그대로).</summary>
        public const double HomeMinDot = -0.3;
        /// <summary>쫓기지 않을 때 돌아가는 방향의 적 반대쪽 몫(약 73°).</summary>
        public const double AroundMinDot = 0.3;
        /// <summary>쫓길 때 적 반대쪽 몫(약 41° 안 — 회색난쟁이 뛰는 속도가 일꾼의 4분의 3쯤이라 간격이 안 줄어듦).</summary>
        public const double ChasedMinDot = 0.75;
        /// <summary>거점이 적 바로 뒤(외적이 이보다 작음)면 지난번에 돈 쪽을 그대로(0.5초마다 다시 골라도 오락가락하지 않게).</summary>
        public const double SideKeep = 0.15;

        /// <param name="toHomeX">일꾼 → 거점(정규화 안 함).</param>
        /// <param name="awayX">적들 → 일꾼 방향(단위 벡터).</param>
        /// <param name="chased">싸울 생각인 적이 바로 뒤까지 따라왔다.</param>
        /// <param name="side">돌아가는 쪽(+1 반시계·−1 시계, 0 아직 없음). Around면 고른 쪽으로 바뀐다.</param>
        /// <param name="dirX">갈 방향(단위 벡터).</param>
        public static RetreatWay Way(double toHomeX, double toHomeZ, double awayX, double awayZ, bool chased, ref int side, out double dirX, out double dirZ)
        {
            double hd = Math.Sqrt(toHomeX * toHomeX + toHomeZ * toHomeZ);
            if (hd <= HomeNear) { dirX = awayX; dirZ = awayZ; return RetreatWay.Away; }
            double hx = toHomeX / hd, hz = toHomeZ / hd;
            double dot = hx * awayX + hz * awayZ;
            if (chased ? dot >= ChasedMinDot : dot > HomeMinDot) { dirX = hx; dirZ = hz; return RetreatWay.Home; }
            double cross = awayX * hz - awayZ * hx; // > 0: 거점이 적 반대쪽에서 반시계 쪽
            if (Math.Abs(cross) >= SideKeep || side == 0) side = cross >= 0 ? 1 : -1;
            double c = chased ? ChasedMinDot : AroundMinDot, s = Math.Sqrt(1 - c * c);
            double px = -awayZ * side, pz = awayX * side; // 적 반대쪽을 side 쪽으로 90° 돌린 것
            dirX = awayX * c + px * s;
            dirZ = awayZ * c + pz * s;
            return RetreatWay.Around;
        }
    }
}
