using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 막힘 복구 2) 목표 쪽 순간이동의 자리 규칙(사용자 결정 2026-10-23, decisions.txt). 채집 일(GatherJob)만, 근처 아님:
    /// 떠날 자리와 내릴 자리 모두 가장 가까운 플레이어에서 NearPlayer m 밖(우리가 정한 근거리 품질 기준 — 엔진 구역 판정과 섞지 않음).
    /// 땅·물·위험·구역 불러와짐은 플러그인이 자리마다 본다. 여기는 후보 순서와 거리 규칙만.
    /// (게임 Mono에 System.ValueTuple이 없을 수 있어 튜플 대신 XZ.)
    /// </summary>
    public static class StuckHop
    {
        public struct XZ
        {
            public double X, Z;
            public XZ(double x, double z) { X = x; Z = z; }
        }

        /// <summary>"근처" 기준(m): 가장 가까운 플레이어에서 이 안이면 순간이동하지 않는다.</summary>
        public const double NearPlayer = 64.0;
        /// <summary>목표에서 내릴 거리(m), 가까운 것부터. 3m = 곡괭이·도끼가 닿는 거리 바로 밖.</summary>
        public static readonly double[] Radii = { 3.0, 5.0, 8.0 };
        /// <summary>한 거리에서 보는 방향 수.</summary>
        public const int Directions = 8;
        /// <summary>내릴 땅의 최소 법선 y(약 35도보다 가파르면 안 됨).</summary>
        public const double MinNormalY = 0.82;

        /// <summary>(x,z)가 모든 플레이어에서 NearPlayer m 밖인가. 플레이어가 없으면 true.</summary>
        public static bool FarFromPlayers(double x, double z, IEnumerable<XZ> players)
        {
            foreach (var p in players)
            {
                double dx = p.X - x, dz = p.Z - z;
                if (dx * dx + dz * dz < NearPlayer * NearPlayer) return false;
            }
            return true;
        }

        /// <summary>
        /// 내릴 자리 후보: 목표 둘레 Radii × Directions, 가까운 거리부터, 같은 거리에선 일꾼이 오던 쪽(목표→일꾼 방향)에 가까운 방향부터.
        /// 일꾼이 목표와 같은 자리면 +x 쪽부터.
        /// </summary>
        public static IEnumerable<XZ> Candidates(double npcX, double npcZ, double destX, double destZ)
        {
            double baseAngle = Math.Atan2(npcZ - destZ, npcX - destX);
            if (Math.Abs(npcX - destX) < 1e-6 && Math.Abs(npcZ - destZ) < 1e-6) baseAngle = 0;
            double step = 2 * Math.PI / Directions;
            foreach (double r in Radii)
            {
                // 0, +1, -1, +2, -2, ... 칸
                for (int i = 0; i < Directions; i++)
                {
                    int k = (i + 1) / 2 * (i % 2 == 1 ? 1 : -1);
                    double a = baseAngle + k * step;
                    yield return new XZ(destX + Math.Cos(a) * r, destZ + Math.Sin(a) * r);
                }
            }
        }
    }
}
