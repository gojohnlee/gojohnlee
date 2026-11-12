using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 막힘 1초·3초(사용자 11-11, Core StuckClock)의 게임 쪽 재기: 남은 길(BaseAI.m_path 합 + 길 끝~목표)과 플레이어가 보는지.
    /// 보는지 = 이 PC 카메라(시야 안·가림 없음) 또는 다른 플레이어 몸(앞 ±90°·눈에서 가림 없음), 20m 안은 봄·100m 밖은 안 봄.
    /// </summary>
    public static class StuckWatch
    {
        static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, List<Vector3>> PathOf = HarmonyLib.AccessTools.FieldRefAccess<BaseAI, List<Vector3>>("m_path");
        static int s_mask;
        static int Mask => s_mask != 0 ? s_mask : (s_mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain"));

        /// <summary>남은 길(m): m_path가 있으면 그 합 + 끝에서 목표까지, 없으면 직선(수평).</summary>
        public static float PathLeft(BaseAI ai, Vector3 from, Vector3 goal)
        {
            var path = ai != null ? PathOf(ai) : null;
            if (path == null || path.Count == 0) return Utils.DistanceXZ(from, goal);
            float sum = Utils.DistanceXZ(from, path[0]);
            for (int i = 1; i < path.Count; i++) sum += Utils.DistanceXZ(path[i - 1], path[i]);
            return sum + Utils.DistanceXZ(path[path.Count - 1], goal);
        }

        /// <summary>m_path가 이 목표로 받은 길인가(끝이 목표 within m 안) — 목표를 바꾼 첫 틱엔 앞 목표 길이 남아 있음.</summary>
        public static bool PathEndsNear(BaseAI ai, Vector3 goal, float within)
        {
            var path = ai != null ? PathOf(ai) : null;
            return path != null && path.Count > 0 && Utils.DistanceXZ(path[path.Count - 1], goal) < within;
        }

        /// <summary>길을 새로 받았는지(점 수가 늘었거나 끝점이 1m 넘게 바뀜 — 지나간 점 지우기는 아님). 일마다 하나씩.</summary>
        public sealed class PathMark
        {
            int m_count;
            Vector3 m_end;
            public bool Changed(BaseAI ai)
            {
                var path = ai != null ? PathOf(ai) : null;
                int n = path != null ? path.Count : 0;
                Vector3 end = n > 0 ? path[n - 1] : Vector3.zero;
                bool changed = n > 0 && (n > m_count || Vector3.Distance(end, m_end) > 1f);
                m_count = n; m_end = end;
                return changed;
            }
        }

        /// <summary>누구든 p(발)의 일꾼을 보는가.</summary>
        /// <summary>시험용: 있으면 Seen 대신 이것(보는 중·안 보는 중 강제, 있을 법한 자리 시험). 평소 null.</summary>
        public static System.Func<Vector3, bool> TestSeen;

        public static bool Seen(Vector3 p)
        {
            if (TestSeen != null) return TestSeen(p);
            Vector3 body = p + Vector3.up;
            var cam = Utils.GetMainCamera();
            if (cam != null)
            {
                Vector3 c = cam.transform.position;
                float dist = Vector3.Distance(c, body);
                Vector3 v = cam.WorldToViewportPoint(body);
                bool inView = v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
                if (StuckClock.Watching(dist, inView, inView && dist <= StuckClock.FarSee && !Physics.Linecast(c, body, Mask))) return true;
            }
            foreach (var pl in Player.GetAllPlayers())
            {
                if (pl == null || pl == Player.m_localPlayer || pl.IsDead()) continue;
                Vector3 eye = pl.GetEyePoint();
                float dist = Vector3.Distance(eye, body);
                Vector3 f = pl.transform.forward, d = body - eye;
                bool inView = StuckClock.InFront(f.x, f.z, d.x, d.z);
                if (StuckClock.Watching(dist, inView, inView && dist <= StuckClock.FarSee && !Physics.Linecast(eye, body, Mask))) return true;
            }
            return false;
        }
    }
}
