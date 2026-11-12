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
        public static IEnumerable<XZ> Candidates(double npcX, double npcZ, double destX, double destZ, double[] radii = null)
        {
            double baseAngle = Math.Atan2(npcZ - destZ, npcX - destX);
            if (Math.Abs(npcX - destX) < 1e-6 && Math.Abs(npcZ - destZ) < 1e-6) baseAngle = 0;
            double step = 2 * Math.PI / Directions;
            foreach (double r in radii ?? Radii)
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

        /// <summary>설비 곁에 내릴 거리(m, 설비 가운데에서): 설비 앞 일 거리 2.5m 안(E035 설비 접근 막힘, 10-26).</summary>
        public static readonly double[] MachineRadii = { 2.2, 2.4 };
        /// <summary>설비에 곧장 못 다가갈 때 차례로 돌아 볼 쪽(도, 설비→일꾼 방향 기준): 옆 둘, 뒤(E035, 10-26).</summary>
        public static readonly double[] SideAngles = { 90.0, -90.0, 180.0 };

        /// <summary>설비 (cx,cz) 둘레 r m에서 side번째(SideAngles) 돌아 볼 접근점. 일꾼이 설비와 같은 자리면 +x 쪽 기준.</summary>
        public static XZ Side(double npcX, double npcZ, double cx, double cz, double r, int side)
        {
            double a = Math.Abs(npcX - cx) < 1e-6 && Math.Abs(npcZ - cz) < 1e-6 ? 0 : Math.Atan2(npcZ - cz, npcX - cx);
            a += SideAngles[side] * Math.PI / 180.0;
            return new XZ(cx + Math.Cos(a) * r, cz + Math.Sin(a) * r);
        }

        /// <summary>근처 막힘 건너뛰기(사용자 결정 2026-10-23 "근처 막힘 건너뛰기"): 플레이어 근처에서 이 초 넘게 진척이 없으면 막힌 곳 너머로 옮긴다(30 → 15초 10-26, → 5초 사용자 10-31).</summary>
        public const double NearStuckSeconds = 5.0;
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

        /// <summary>있을 법한 자리(사용자 11-12): 마지막으로 보인 뒤 걸어서 갈 수 있었던 거리 = 지난 초 × 달리기 속도 + 이 여유(m). 보는 중이면 여유만(NearSteps 4m는 들어감).</summary>
        public const double ReachSlack = 4.5;

        /// <summary>
        /// 근처 순간이동 후보를 '있을 법한 자리' 순으로 거른다(사용자 11-12): ② 가던 쪽(일꾼→목표 방향 앞, 지나온 쪽·반대편은 뺌)만,
        /// ① 마지막으로 보인 자리 seenAt에서 sinceSeen초 × speed + ReachSlack 안을 먼저, ③ 같은 무리에선 안 보이는 자리(seen=false) 먼저.
        /// 순서: 범위 안·안 보임 → 범위 안·보임 → 범위 밖·안 보임(순간이동을 막지 않으려고. 보이는 범위 밖은 뺌). seenAt이 없으면(본 적 없음) 범위 제한 없음.
        /// 같은 무리 안은 원래 후보 순서.
        /// </summary>
        public static List<XZ> Plausible(IEnumerable<XZ> candidates, double npcX, double npcZ, double destX, double destZ,
            XZ? seenAt, double sinceSeen, double speed, Func<XZ, bool> seen)
        {
            double fx = destX - npcX, fz = destZ - npcZ;
            bool hasDir = fx * fx + fz * fz > 1e-6;
            double reach = Math.Max(0, sinceSeen) * Math.Max(0, speed) + ReachSlack;
            var tiers = new[] { new List<XZ>(), new List<XZ>(), new List<XZ>() };
            foreach (var c in candidates)
            {
                if (hasDir && (c.X - npcX) * fx + (c.Z - npcZ) * fz < 0) continue; // 뒤(지나온 쪽)
                bool inRange = true;
                if (seenAt.HasValue)
                {
                    double sx = c.X - seenAt.Value.X, sz = c.Z - seenAt.Value.Z;
                    inRange = sx * sx + sz * sz <= reach * reach;
                }
                bool visible = seen != null && seen(c);
                if (inRange) tiers[visible ? 1 : 0].Add(c);
                else if (!visible) tiers[2].Add(c);
            }
            tiers[0].AddRange(tiers[1]);
            tiers[0].AddRange(tiers[2]);
            return tiers[0];
        }
    }
}
