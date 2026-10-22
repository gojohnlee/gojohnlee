using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 늪 지하묘지 찾기·드나들기(설계 ⑦). 지하묘지는 위치 물체(LocationProxy ZDO, s_location = 위치 이름 해시)로 찾고,
    /// 입구 쇠문(열쇠 문 Door, ZDO s_state ≠ 0 = 열림, 열쇠 문은 다시 못 닫음)이 열린 곳을 먼저 다 캔다. 플레이어가 그 열쇠를 얻었으면(Unlocks)
    /// 남은 게 없을 때 닫힌 곳을 일꾼이 연다(사용자 결정 2026-10-22). 남은 고철 더미는 안(y &gt; 3000)의 ZDO로 센다 — 안의 물체는 구역이
    /// 처음 생길 때(DungeonGenerator Full·Ghost) 함께 생기고 진흙 더미는 다시 안 생긴다.
    /// 입구·안은 바닐라 Teleport(구역이 불러와졌을 때만 있음): 들어가면 짝 Teleport 위치 + forward − up(Teleport.GetTeleportPoint와 같음),
    /// 안은 y ≈ 지면 + 5000(Character.InInterior = y &gt; 3000). 호스트에서만 부른다.
    /// </summary>
    public static class Dungeons
    {
        public const string ProxyPrefab = "LocationProxy";
        /// <summary>지하묘지 진흙 더미에서 나오는 물건(고철).</summary>
        public const string ScrapItem = "IronScrap";
        const float DoorRange = 30f;
        /// <summary>안의 물체를 이 지하묘지 것으로 세는 거리(xz). 안은 묘지 구역 한 칸(64m) 상자 안에 생긴다(DungeonGenerator m_zoneSize).</summary>
        const float InteriorRange = 100f;

        public struct Crypt
        {
            public Vector3 Pos;
            public string Name;
            public bool Open;
            public ZDO Door;
            /// <summary>안에 남은 고철 더미 수(ZDO).</summary>
            public int Left;
            /// <summary>닫혔지만 일꾼이 열 수 있다(그 열쇠를 플레이어가 얻음, 와드 밖).</summary>
            public bool CanOpen;
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
                c.Open = IsOpen(c.Door);
                c.CanOpen = !c.Open && CanOpen(c.Door);
                result.Add(c);
            }
            for (int i = 0; i < result.Count; i++)
            {
                var c = result[i];
                c.Left = ScrapLeft(c.Pos, result);
                result[i] = c;
            }
            return result;
        }

        public static bool IsOpen(ZDO door) => door != null && door.GetInt(ZDOVars.s_state) != 0;

        /// <summary>그 열쇠 문을 일꾼이 열 수 있나: 플레이어가 열쇠를 얻었고(Unlocks) 와드가 막지 않는다(바닐라 Door.Interact와 같은 확인).</summary>
        static bool CanOpen(ZDO doorZdo)
        {
            if (doorZdo == null) return false;
            GameObject prefab = ZNetScene.instance.GetPrefab(doorZdo.GetPrefab());
            Door door = prefab != null ? prefab.GetComponent<Door>() : null;
            if (door == null || !Unlocks.Have(door.m_keyItem)) return false;
            return !door.m_checkGuardStone || PrivateArea.CheckAccess(doorZdo.GetPosition(), 0f, flash: false);
        }

        /// <summary>at 지하묘지 안에 남은 고철 더미(ResourceCatalog가 고철을 낸다고 보는 것) 수. 둘레 다른 묘지에 더 가까운 것은 뺀다.</summary>
        static int ScrapLeft(Vector3 at, List<Crypt> all)
        {
            var near = new List<ZDO>();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), new SimulationDistance(1, 0), near);
            int n = 0;
            foreach (var z in near)
            {
                Vector3 p = z.GetPosition();
                if (!Character.InInterior(p)) continue;
                float d = Utils.DistanceXZ(p, at);
                if (d > InteriorRange) continue;
                var info = ResourceCatalog.Get(z.GetPrefab());
                if (info == null || !info.Produces.Contains(ScrapItem)) continue;
                bool mine = true;
                foreach (var o in all)
                    if (Utils.DistanceXZ(p, o.Pos) < d) { mine = false; break; }
                if (mine) n++;
            }
            return n;
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

        /// <summary>from에서 갈 지하묘지(Core.Crypts.Choose: 열린 곳 먼저, 없으면 열 수 있는 닫힌 곳). avoid 안의 자리(5m)·다 캔 곳은 뺀다.</summary>
        public static bool TryChoose(Vector3 from, out Crypt crypt, ICollection<Vector3> avoid = null) => TryChoose(from, out crypt, out _, avoid);

        /// <summary>minedOut: 못 골랐을 때 까닭이 다 캔 묘지다(Core.Crypts.MinedOut).</summary>
        public static bool TryChoose(Vector3 from, out Crypt crypt, out bool minedOut, ICollection<Vector3> avoid = null)
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
                spots.Add(new CryptSpot { X = c.Pos.x, Z = c.Pos.z, Open = c.Open, Avoid = skip, Empty = c.Left == 0, CanOpen = c.CanOpen });
            }
            int i = Crypts.Choose(from.x, from.z, spots);
            minedOut = i < 0 && Crypts.MinedOut(spots);
            if (i < 0) return false;
            crypt = all[i];
            return true;
        }

        /// <summary>불러와진 열쇠 문. 구역이 안 불러와졌으면 null.</summary>
        public static Door DoorOf(ZDO door)
        {
            ZNetView v = door != null ? ZNetScene.instance.FindInstance(door) : null;
            return v != null && v.IsValid() ? v.GetComponent<Door>() : null;
        }

        /// <summary>
        /// 일꾼이 열쇠 문을 연다: 바닐라 Door.Interact가 열쇠를 확인한 뒤 하는 것과 같다(선 쪽으로 방향을 정해 RPC UseDoor, 문 주인이 상태를 바꿈).
        /// 열쇠는 일꾼 짐이 아니라 Unlocks(플레이어가 얻었나)로 본다. 이미 열렸거나 열 수 없으면 false.
        /// </summary>
        public static bool Open(Character who, Door door)
        {
            if (who == null || door == null) return false;
            ZNetView v = door.GetComponent<ZNetView>();
            if (v == null || !v.IsValid() || IsOpen(v.GetZDO()) || !CanOpen(v.GetZDO())) return false;
            Vector3 dir = (who.transform.position - door.transform.position).normalized;
            v.InvokeRPC("UseDoor", Vector3.Dot(door.transform.forward, dir) < 0f);
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
