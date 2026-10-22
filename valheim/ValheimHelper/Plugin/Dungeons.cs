using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 늪 지하묘지 찾기·드나들기(설계 ⑦). 지하묘지는 위치 물체(LocationProxy ZDO, s_location = 위치 이름 해시)로 찾고,
    /// 입구 쇠문(열쇠 문 Door, ZDO s_state ≠ 0 = 열림, 열쇠 문은 다시 못 닫음)이 열린 곳만 쓴다.
    /// 입구·안은 바닐라 Teleport(구역이 불러와졌을 때만 있음): 들어가면 짝 Teleport 위치 + forward − up(Teleport.GetTeleportPoint와 같음),
    /// 안은 y ≈ 지면 + 5000(Character.InInterior = y &gt; 3000). 호스트에서만 부른다.
    /// </summary>
    public static class Dungeons
    {
        public const string ProxyPrefab = "LocationProxy";
        const float DoorRange = 30f;

        public struct Crypt
        {
            public Vector3 Pos;
            public string Name;
            public bool Open;
            public ZDO Door;
        }

        static Dictionary<int, string> s_names;

        /// <summary>늪 지하묘지 위치 이름 해시 → 이름("SunkenCrypt…" 또는 늪에 놓이는 "…Crypt…").</summary>
        static Dictionary<int, string> CryptNames()
        {
            if (s_names != null) return s_names;
            var map = new Dictionary<int, string>();
            foreach (var loc in ZoneSystem.instance.m_locations)
            {
                string n = loc.m_prefabName;
                if (string.IsNullOrEmpty(n)) continue;
                if (n.Contains("SunkenCrypt") || (n.Contains("Crypt") && loc.m_biome == Heightmap.Biome.Swamp))
                    map[n.GetStableHashCode()] = n;
            }
            if (map.Count > 0) s_names = map;
            return map;
        }

        /// <summary>월드의 늪 지하묘지 전부(생성된 구역만). 문을 못 찾으면 닫힌 것으로 본다.</summary>
        public static List<Crypt> All()
        {
            var result = new List<Crypt>();
            if (ZDOMan.instance == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null) return result;
            var names = CryptNames();
            if (names.Count == 0) return result;
            var list = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(ProxyPrefab, list, ref index)) { }
            foreach (var zdo in list)
            {
                if (!names.TryGetValue(zdo.GetInt(ZDOVars.s_location), out string name)) continue;
                var c = new Crypt { Pos = zdo.GetPosition(), Name = name };
                c.Door = KeyDoor(c.Pos);
                c.Open = c.Door != null && c.Door.GetInt(ZDOVars.s_state) != 0;
                result.Add(c);
            }
            return result;
        }

        /// <summary>지하묘지 둘레(바깥, 안 말고) 열쇠 문 ZDO.</summary>
        public static ZDO KeyDoor(Vector3 at)
        {
            var near = new List<ZDO>();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), new SimulationDistance(1, 0), near);
            ZDO best = null;
            float bestD = DoorRange;
            foreach (var z in near)
            {
                Vector3 p = z.GetPosition();
                if (Character.InInterior(p)) continue;
                float d = Utils.DistanceXZ(p, at);
                if (d > bestD) continue;
                GameObject prefab = ZNetScene.instance.GetPrefab(z.GetPrefab());
                Door door = prefab != null ? prefab.GetComponent<Door>() : null;
                if (door == null || door.m_keyItem == null) continue;
                best = z;
                bestD = d;
            }
            return best;
        }

        /// <summary>from에서 갈 열린 지하묘지(Core.Crypts.Choose). avoid 안의 자리(5m)는 뺀다.</summary>
        public static bool TryChoose(Vector3 from, out Crypt crypt, ICollection<Vector3> avoid = null)
        {
            crypt = default;
            var all = All();
            var spots = new List<CryptSpot>();
            foreach (var c in all)
            {
                bool skip = false;
                if (avoid != null)
                    foreach (var a in avoid)
                        if (Utils.DistanceXZ(a, c.Pos) < 5f) skip = true;
                spots.Add(new CryptSpot { X = c.Pos.x, Z = c.Pos.z, Open = c.Open, Avoid = skip });
            }
            int i = Crypts.Choose(from.x, from.z, spots);
            if (i < 0) return false;
            crypt = all[i];
            return true;
        }

        /// <summary>불러와진 지하묘지의 바깥 입구 Teleport(안으로 이어진 것). 구역이 안 불러와졌으면 null.</summary>
        public static Teleport Entrance(Vector3 crypt)
        {
            Teleport best = null;
            float bestD = DoorRange * 2f;
            foreach (var t in Object.FindObjectsByType<Teleport>(FindObjectsSortMode.None))
            {
                if (t.m_targetPoint == null || Character.InInterior(t.transform.position)) continue;
                if (!Character.InInterior(t.m_targetPoint.transform.position)) continue;
                float d = Utils.DistanceXZ(t.transform.position, crypt);
                if (d < bestD) { best = t; bestD = d; }
            }
            return best;
        }

        /// <summary>안에서 나가는 Teleport(바깥으로 이어진 것) 중 at에 가장 가까운 것.</summary>
        public static Teleport Exit(Vector3 at)
        {
            Teleport best = null;
            float bestD = float.MaxValue;
            foreach (var t in Object.FindObjectsByType<Teleport>(FindObjectsSortMode.None))
            {
                if (t.m_targetPoint == null || !Character.InInterior(t.transform.position)) continue;
                if (Character.InInterior(t.m_targetPoint.transform.position)) continue;
                float d = Vector3.Distance(t.transform.position, at);
                if (d < bestD) { best = t; bestD = d; }
            }
            return best;
        }

        /// <summary>t를 쓰면 나오는 자리(바닐라 Teleport.Interact: 짝 위치 + forward − up, 짝 회전).</summary>
        public static Vector3 ArrivePoint(Teleport t, out Quaternion rot)
        {
            Transform x = t.m_targetPoint.transform;
            rot = x.rotation;
            return x.position + x.forward - x.up;
        }

        /// <summary>일꾼을 t로 건너보낸다(바닐라와 같은 TeleportTo, 거리 순간이동 아님).</summary>
        public static bool Use(Character who, Teleport t)
        {
            if (who == null || t == null || t.m_targetPoint == null) return false;
            Vector3 p = ArrivePoint(t, out Quaternion rot);
            return who.TeleportTo(p, rot, distantTeleport: false);
        }
    }
}
