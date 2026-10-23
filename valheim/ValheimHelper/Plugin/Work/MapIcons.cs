using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 지도에 일꾼 아이콘(사용자 결정 2026-10-23 "멀리선 이동 생략 때 지도"): 이름, 모든 플레이어 지도, 2초마다, 저장 안 함, 죽으면 없앰.
    /// 먼 일꾼 ZDO는 다른 컴퓨터에 안 간다(ZDOMan은 피어 둘레 구역만 보냄) → 호스트가 2초마다 위치 목록을 모두에게 보낸다(VH_MapIcons).
    /// 멀리선 이동 생략 중이면 몸 대신 지도상 위치(GatherJob.FarMapPos). 받는 쪽은 Minimap.AddPin(Player 모양, save false —
    /// 저장 안 되고 오른쪽 클릭 지우기(GetClosestPin은 m_save만)에도 안 잡힘)으로 핀을 만들고 m_pos만 고친다. 10초 소식 없으면 다 지움.
    /// </summary>
    public static class MapIcons
    {
        const float Every = 2f, Stale = 10f;
        static float s_sendTimer, s_lastHeard;
        static Minimap s_map;
        static readonly Dictionary<ZDOID, Minimap.PinData> s_pins = new Dictionary<ZDOID, Minimap.PinData>();
        static readonly AccessTools.FieldRef<Minimap, bool> PinUpdate = AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");

        /// <summary>시험용: 받은 쪽 지금 아이콘들과 받은 횟수.</summary>
        public static IEnumerable<Minimap.PinData> Pins => s_pins.Values;
        public static int Received;

        [HarmonyPatch(typeof(Game), "Start")]
        static class GameStart
        {
            static void Postfix() => ZRoutedRpc.instance.Register<ZPackage>("VH_MapIcons", RPC_MapIcons);
        }

        public static void Update(float dt)
        {
            if (s_map != Minimap.instance) { s_pins.Clear(); s_map = Minimap.instance; } // 새 지도(월드 다시 들어옴): 옛 핀은 같이 없어짐
            if (s_pins.Count > 0 && Time.time - s_lastHeard > Stale) Clear();
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null) return;
            s_sendTimer -= dt;
            if (s_sendTimer > 0f) return;
            s_sendTimer = Every;
            var ids = new List<ZDOID>();
            var names = new List<string>();
            var poss = new List<Vector3>();
            foreach (ZDO zdo in Workers.Zdos())
            {
                if (zdo == null) continue;
                WorkerNpc npc = Workers.Find(zdo);
                if (npc != null && npc.IsDead()) continue;
                ids.Add(zdo.m_uid);
                names.Add(zdo.GetString(WorkerNpc.KeyName, "Worker"));
                poss.Add(npc != null ? npc.FarJob?.FarMapPos ?? npc.transform.position : zdo.GetPosition());
            }
            var pkg = new ZPackage();
            pkg.Write(ids.Count);
            for (int i = 0; i < ids.Count; i++) { pkg.Write(ids[i]); pkg.Write(names[i]); pkg.Write(poss[i]); }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "VH_MapIcons", pkg);
        }

        static void RPC_MapIcons(long sender, ZPackage pkg)
        {
            Minimap map = Minimap.instance;
            if (map == null) return;
            if (s_map != map) { s_pins.Clear(); s_map = map; }
            s_lastHeard = Time.time;
            Received++;
            var seen = new HashSet<ZDOID>();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
            {
                ZDOID id = pkg.ReadZDOID();
                string name = pkg.ReadString();
                Vector3 pos = pkg.ReadVector3();
                seen.Add(id);
                if (s_pins.TryGetValue(id, out Minimap.PinData pin) && pin != null && pin.m_name == name) pin.m_pos = pos;
                else
                {
                    if (pin != null) map.RemovePin(pin); // 이름이 바뀜: 이름표는 만들 때 정해지니 새로
                    s_pins[id] = Scout.AddPin(pos, Minimap.PinType.Player, name, false);
                }
            }
            foreach (ZDOID id in new List<ZDOID>(s_pins.Keys))
                if (!seen.Contains(id)) { if (s_pins[id] != null) map.RemovePin(s_pins[id]); s_pins.Remove(id); } // 죽었거나 없어진 일꾼
            PinUpdate(map) = true;
        }

        static void Clear()
        {
            if (s_map != null) foreach (var pin in s_pins.Values) if (pin != null) s_map.RemovePin(pin);
            s_pins.Clear();
        }
    }
}
