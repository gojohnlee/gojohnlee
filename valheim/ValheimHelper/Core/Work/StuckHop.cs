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

        /// <summary>근처 막힘 건너뛰기(사용자 결정 2026-10-23 "근처 막힘 건너뛰기"): 플레이어 근처에서 이 초 넘게 진척이 없으면 막힌 곳 너머로 옮긴다.</summary>
        public const double NearStuckSeconds = 30.0;
        /// <summary>막힌 곳 너머 내릴 거리(m, 일꾼에서 목표 쪽으로), 가까운 것부터. 몸 반지름 + 바위·나무 하나를 넘는 4m부터.</summary>
        public static readonly double[] NearSteps = { 4.0, 6.0, 8.0 };
        /// <summary>목표 쪽 방향에서 벌리는 각(도), 곧장부터.</summary>
        public static readonly double[] NearAngles = { 0.0, 30.0, -30.0, 60.0, -60.0 };

        /// <summary>
        /// 근처 막힘 건너뛰기 자리 후보: 일꾼에서 목표 쪽으로 NearSteps × NearAngles(가까운 거리부터, 같은 거리에선 곧장부터).
        /// 목표를 1m 넘게 지나치거나 목표에 2m도 안 다가가는 자리는 뺀다. 목표가 가까워 그런 자리가 없으면 목표 둘레(Candidates) 중 일꾼에서 1.5~10m.
        /// </summary>
        public static IEnumerable<XZ> NearCandidates(double npcX, double npcZ, double destX, double destZ)
        {
            double dx = destX - npcX, dz = destZ - npcZ;
            double d = Math.Sqrt(dx * dx + dz * dz);
            bool any = false;
            if (d > 1e-6)
            {
                double baseAngle = Math.Atan2(dz, dx);
                foreach (double s in NearSteps)
                {
                    if (s > d - 1.0) break;
                    foreach (double deg in NearAngles)
                    {
                        double a = baseAngle + deg * Math.PI / 180.0;
                        double x = npcX + Math.Cos(a) * s, z = npcZ + Math.Sin(a) * s;
                        double left = Math.Sqrt((destX - x) * (destX - x) + (destZ - z) * (destZ - z));
                        if (left > d - 2.0) continue;
                        any = true;
                        yield return new XZ(x, z);
                    }
                }
            }
            if (any) yield break;
            foreach (var c in Candidates(npcX, npcZ, destX, destZ))
            {
                double ox = c.X - npcX, oz = c.Z - npcZ;
                double o2 = ox * ox + oz * oz;
                if (o2 >= 1.5 * 1.5 && o2 <= 100.0) yield return c; // 제자리·먼 곳 빼고
            }
        }
    }
}
