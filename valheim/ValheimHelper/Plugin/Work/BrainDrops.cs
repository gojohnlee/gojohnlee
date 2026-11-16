using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public static partial class Brain
    {
        // ---- 뛰어내리기 지름길 점 찾기(요청 11-11 ★ 나): 일꾼 집 둘레 바닥 끝 → 내려가기 점(Core DropShortcut) ----
        /// <summary>집에서 이 가로 반경 안 바닥만 봄.</summary>
        public const float DropScanRadius = PlayWalkLog.HouseRadius; // 일꾼이 집 안 걸음으로 보는 범위와 같게
        const float DropGrid = 1f;
        static DropShortcut s_drops;
        static Vector3 s_dropsHome;
        static bool s_dropsStale = true;
        /// <summary>시험용: 마지막 훑기 시간(ms)·건축 바뀜으로 지운 점 수.</summary>
        public static float DropsScanMs;
        public static int DropsForgotBuild;
        static readonly List<RaycastHit> s_dropHits = new List<RaycastHit>();
        static readonly Vector3[] s_dropDirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };

        /// <summary>home 둘레 내려가기 점. 집이 5m 넘게 옮겼거나 건축이 바뀌었으면 다시 훑음(점은 저장 안 함 — 불러온 뒤 처음 쓸 때 훑음).</summary>
        public static DropShortcut Drops(Vector3 home)
        {
            if (s_drops == null || Utils.DistanceXZ(home, s_dropsHome) > 5f) { s_drops = new DropShortcut(); s_dropsHome = home; s_dropsStale = true; }
            if (s_dropsStale) { ScanDrops(home, s_drops); s_dropsStale = false; }
            return s_drops;
        }

        /// <summary>건축 바뀜(BrainPaths.ForgetBuild 자리): 그 곁 점 지우고, 다음 Drops 때 새로 생긴 끝을 찾게 다시 훑기 표시.</summary>
        /// <summary>시험용(vh_droptest): 시험이 Instantiate로 놓은 조각은 SetCreator를 안 거침 → 다음 Drops 때 다시 훑기.</summary>
        internal static void DropsRescan() => s_dropsStale = true;

        static void ForgetDrops(Vector3 at)
        {
            if (s_drops == null || Utils.DistanceXZ(at, s_dropsHome) > DropScanRadius + 5f) return;
            DropsForgotBuild += s_drops.ForgetNear(new PathMemory.P(at.x, at.y, at.z), PathForgetRadius);
            s_dropsStale = true;
        }

        /// <summary>1m 칸마다 위에서 아래로 모든 평평한 조각 면을 보고, 이웃 칸 쪽에 같은 높이 바닥이 없으면 끝을 찾아 Ok 판정. 넣은 점 수.</summary>
        static int ScanDrops(Vector3 home, DropShortcut into)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int mask = WorkerAI.SolidMask, pieceMask = LayerMask.GetMask("piece"), added = 0;
            int n = Mathf.CeilToInt(DropScanRadius / DropGrid);
            for (int ix = -n; ix <= n; ix++)
                for (int iz = -n; iz <= n; iz++)
                {
                    var top = new Vector3(home.x + ix * DropGrid, home.y + 25f, home.z + iz * DropGrid);
                    if (Utils.DistanceXZ(top, home) > DropScanRadius) continue;
                    s_dropHits.Clear();
                    s_dropHits.AddRange(Physics.RaycastAll(top, Vector3.down, 50f, pieceMask, QueryTriggerInteraction.Ignore));
                    foreach (var h in s_dropHits)
                    {
                        if (h.normal.y < 0.9f) continue; // 지붕·비탈
                        Vector3 p = h.point;
                        if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.3f, mask, QueryTriggerInteraction.Ignore)) continue; // 벽 속·천장 낮음
                        foreach (var d in s_dropDirs)
                        {
                            if (Floor(p + d * DropGrid, p.y, pieceMask, out _)) continue; // 이웃 칸도 바닥 — 끝 아님
                            Vector3 edge = p;
                            for (float s = 0.1f; s < DropGrid; s += 0.1f) { if (!Floor(p + d * s, p.y, pieceMask, out Vector3 e)) break; edge = e; }
                            bool wall = Physics.Raycast(edge + Vector3.up * 0.5f, d, 1f, mask, QueryTriggerInteraction.Ignore)
                                || Physics.Raycast(edge + Vector3.up * 1.5f, d, 1f, mask, QueryTriggerInteraction.Ignore);
                            if (wall) continue;
                            if (!Physics.SphereCast(edge + d * 0.6f + Vector3.up * 0.4f, 0.3f, Vector3.down, out RaycastHit land, DropShortcut.MaxDrop + 1f, mask, QueryTriggerInteraction.Ignore)) continue;
                            Vector3 lp = land.point;
                            bool clear = land.normal.y >= 0.8f && land.collider.GetComponentInParent<Container>() == null
                                && lp.y > ZoneSystem.instance.m_waterLevel
                                && !Physics.CheckCapsule(lp + Vector3.up * 0.6f, lp + Vector3.up * 1.6f, 0.4f, mask, QueryTriggerInteraction.Ignore);
                            if (!DropShortcut.Ok(edge.y, lp.y, false, clear)) continue;
                            if (into.Add(new DropShortcut.Spot(new PathMemory.P(edge.x, edge.y, edge.z), new PathMemory.P(lp.x, lp.y, lp.z)))) added++;
                        }
                    }
                }
            DropsScanMs = (float)sw.Elapsed.TotalMilliseconds;
            Diag.Log($"내려가기 점 훑기: {home:F0} 둘레 {DropScanRadius:0}m, 새 {added}개(모두 {into.Count}), {DropsScanMs:0}ms");
            return added;
        }

        /// <summary>at 바로 밑(y 기준 ±0.3m)에 평평한 조각 바닥이 있는지.</summary>
        static bool Floor(Vector3 at, float y, int pieceMask, out Vector3 hitPoint)
        {
            hitPoint = default;
            if (!Physics.Raycast(new Vector3(at.x, y + 0.3f, at.z), Vector3.down, out RaycastHit h, 0.6f, pieceMask, QueryTriggerInteraction.Ignore) || h.normal.y < 0.9f) return false;
            hitPoint = h.point;
            return true;
        }
    }
}
