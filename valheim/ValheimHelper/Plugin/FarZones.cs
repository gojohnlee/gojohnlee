using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 먼 구역 유지 (구상서 12장, 16장). 호스트가 추적 중인 물체(NPC) 주변도 자기 주변처럼
    /// 구역을 만들고, 물체를 불러오고, 소유해서 계산한다. 서버(호스트)에서만 동작한다.
    ///
    /// 게임 1.0.20 코드 기준:
    /// - ZoneSystem.Update → CreateLocalZones(호스트 위치): 0.1초마다 구역을 하나씩 만든다.
    /// - ZNetScene.CreateDestroyObjects: 호스트 위치 주변 ZDO만 남기고 나머지 인스턴스를 지운다.
    /// - ZDOMan.ReleaseNearbyZDOS: 2초마다 호스트 활동 영역 밖 물체의 소유권을 놓는다.
    /// </summary>
    public static class FarZones
    {
        static readonly List<ZDOID> s_tracked = new List<ZDOID>();
        static readonly List<Vector3> s_anchors = new List<Vector3>();

        public static void Track(ZDOID id) { if (!s_tracked.Contains(id)) s_tracked.Add(id); s_anchorFrame = -1; }
        public static void Untrack(ZDOID id) { s_tracked.Remove(id); s_anchorFrame = -1; }

        static bool s_enabled = true;
        public static bool Enabled { get => s_enabled; set { s_enabled = value; s_anchorFrame = -1; } }

        static int s_anchorFrame = -1;

        /// <summary>호스트 주변 밖에 있는 추적 물체 위치. 프레임마다 한 번만 계산한다(반환 목록을 순회 중에 다시 불러도 안전).</summary>
        static List<Vector3> Anchors()
        {
            if (s_anchorFrame == Time.frameCount) return s_anchors;
            s_anchorFrame = Time.frameCount;
            s_anchors.Clear();
            if (!Enabled || s_tracked.Count == 0 || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
                return s_anchors;
            Vector2s hostZone = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
            for (int i = s_tracked.Count - 1; i >= 0; i--)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(s_tracked[i]);
                if (zdo == null) { s_tracked.RemoveAt(i); continue; }
                Vector3 pos = zdo.GetPosition();
                if (!ZNetScene.InActiveArea(pos, hostZone)) s_anchors.Add(pos);
            }
            return s_anchors;
        }

        static bool InAnchorArea(Vector3 point, List<Vector3> anchors)
        {
            foreach (var a in anchors)
                if (ZNetScene.InActiveArea(point, a)) return true;
            return false;
        }

        // ---- 1) 구역 생성 ----

        static readonly Func<ZoneSystem, Vector3, bool> CreateLocalZones =
            AccessTools.MethodDelegate<Func<ZoneSystem, Vector3, bool>>(AccessTools.Method(typeof(ZoneSystem), "CreateLocalZones"));
        static bool s_creating;

        [HarmonyPatch(typeof(ZoneSystem), "CreateLocalZones")]
        static class CreateLocalZonesPatch
        {
            // 추적 물체 주변 구역도 매번 건드려(수명 갱신) 지워지지 않게 하고, 없으면 만든다(물체마다 한 번에 하나까지).
            // 호스트가 이동 중이라 구역을 만드는 중이어도 건너뛰지 않는다: 건너뛰면 NPC 발밑 구역이 수명 만료로 지워질 수 있다.
            static void Postfix(ZoneSystem __instance, ref bool __result)
            {
                if (s_creating) return;
                var anchors = Anchors();
                if (anchors.Count == 0) return;
                s_creating = true;
                try
                {
                    foreach (var a in anchors)
                        if (CreateLocalZones(__instance, a)) __result = true;
                }
                finally { s_creating = false; }
            }
        }

        // ---- 2) 물체 불러오기·지우기 ----

        static readonly Action<ZNetScene, List<ZDO>, List<ZDO>> CreateObjects =
            AccessTools.MethodDelegate<Action<ZNetScene, List<ZDO>, List<ZDO>>>(AccessTools.Method(typeof(ZNetScene), "CreateObjects"));
        static readonly Action<ZNetScene, List<ZDO>, List<ZDO>> RemoveObjects =
            AccessTools.MethodDelegate<Action<ZNetScene, List<ZDO>, List<ZDO>>>(AccessTools.Method(typeof(ZNetScene), "RemoveObjects"));

        static readonly List<ZDO> s_near = new List<ZDO>();
        static readonly List<ZDO> s_distant = new List<ZDO>();
        static readonly List<ZDO> s_temp = new List<ZDO>();
        static readonly HashSet<ZDO> s_seen = new HashSet<ZDO>();

        /// <summary>추적 물체 주변(근거리만) ZDO를 중복 없이 list에 더한다. s_seen에 이미 list 내용이 들어 있어야 한다.</summary>
        static void AddNear(List<ZDO> list, List<Vector3> anchors)
        {
            SimulationDistance sim = ZNet.instance.GetSyncedSimulationDistance();
            var near = new SimulationDistance(sim.NearSimulationDistance, 0, sim.IsClassic);
            foreach (var a in anchors)
            {
                s_temp.Clear();
                ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(a), near, s_temp);
                foreach (var zdo in s_temp)
                    if (s_seen.Add(zdo)) list.Add(zdo);
            }
            s_temp.Clear();
        }

        [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
        static class CreateDestroyObjectsPatch
        {
            // 원본과 같은 순서로 하되, 근거리 목록에 추적 물체 주변을 더한다.
            static bool Prefix(ZNetScene __instance)
            {
                var anchors = Anchors();
                if (anchors.Count == 0) return true;
                s_near.Clear();
                s_distant.Clear();
                s_seen.Clear();
                ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(ZNet.instance.GetReferencePosition()),
                    ZNet.instance.GetSyncedSimulationDistance(), s_near, s_distant);
                s_seen.UnionWith(s_near);
                AddNear(s_near, anchors);
                CreateObjects(__instance, s_near, s_distant);
                RemoveObjects(__instance, s_near, s_distant);
                s_seen.Clear();
                return false;
            }
        }

        // ---- 3) 소유권 ----

        static readonly Func<ZDOMan, Vector3, long, bool> IsInPeerActiveArea =
            AccessTools.MethodDelegate<Func<ZDOMan, Vector3, long, bool>>(AccessTools.Method(typeof(ZDOMan), "IsInPeerActiveArea"));

        [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
        static class ReleaseNearbyZDOSPatch
        {
            // 호스트 몫만 바꾼다: 원본 규칙에서 "호스트 활동 영역"을 "호스트 주변 + 추적 물체 주변"으로 넓힌다.
            static bool Prefix(ZDOMan __instance, Vector3 refPosition, long uid)
            {
                if (uid != ZDOMan.GetSessionID()) return true;
                var anchors = Anchors();
                if (anchors.Count == 0) return true;

                Vector2s zone = ZoneSystem.GetZone(refPosition);
                SimulationDistance sim = ZNet.instance.GetSyncedSimulationDistance();
                s_near.Clear();
                s_seen.Clear();
                __instance.FindSectorObjects(zone, new SimulationDistance(sim.NearSimulationDistance, 0, sim.IsClassic), s_near);
                s_seen.UnionWith(s_near);
                AddNear(s_near, anchors);
                foreach (ZDO zdo in s_near)
                {
                    if (!zdo.Persistent) continue;
                    Vector3 pos = zdo.GetPosition();
                    bool mine = ZNetScene.InActiveArea(pos, zone) || InAnchorArea(pos, anchors);
                    if (zdo.GetOwner() == uid)
                    {
                        if (!mine) zdo.SetOwner(0L);
                    }
                    else if ((!zdo.HasOwner() || !IsInPeerActiveArea(__instance, pos, zdo.GetOwner())) && mine)
                    {
                        zdo.SetOwner(uid);
                    }
                }
                s_near.Clear();
                s_seen.Clear();
                return false;
            }
        }

        [HarmonyPatch(typeof(ZDOMan), "IsInPeerActiveArea")]
        static class IsInPeerActiveAreaPatch
        {
            // 추적 물체 주변은 호스트 영역으로 본다 → 지나가는 플레이어가 NPC 계산을 가져가지 않는다.
            static void Postfix(Vector3 point, long uid, ref bool __result)
            {
                if (__result || uid != ZDOMan.GetSessionID()) return;
                var anchors = Anchors();
                if (anchors.Count > 0 && InAnchorArea(point, anchors)) __result = true;
            }
        }
    }
}
