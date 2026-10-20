using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>NPC 2명 설정·소환·삭제·추적 (구상서 3장). 추적: 호스트가 모든 NPC 주변을 계속 돌린다(FarZones).</summary>
    public static class Workers
    {
        static ConfigEntry<string> s_name1, s_name2;
        public static string Name1 => s_name1?.Value ?? "Claude";
        public static string Name2 => s_name2?.Value ?? "Alarak";

        public static void Init(ConfigFile cfg)
        {
            s_name1 = cfg.Bind("NPC", "Name1", "Claude", "1호(SCV 성향) 이름");
            s_name2 = cfg.Bind("NPC", "Name2", "Alarak", "2호(알라라크 성향) 이름");
            new Terminal.ConsoleCommand("vh_spawn", "[Valheim Helper] 일꾼 NPC 2명을 내 옆에 부른다(없는 쪽만)", args => args.Context.AddString(Spawn(out _)));
            new Terminal.ConsoleCommand("vh_despawn", "[Valheim Helper] 일꾼 NPC를 모두 없앤다", args => args.Context.AddString($"NPC {DespawnAll()}명 없앰"));
        }

        /// <summary>월드에 있는 NPC ZDO 전부(멀리 있어 안 불러온 것 포함). 호스트에서만 의미 있다.</summary>
        public static List<ZDO> AllZdos()
        {
            var list = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(WorkerPrefab.Name, list, ref index)) { }
            return list;
        }

        /// <summary>없는 NPC만 플레이어 옆에 부른다. 거점은 지금은 각자 소환된 자리(깃발은 다음 단계).</summary>
        public static string Spawn(out List<WorkerNpc> spawned)
        {
            spawned = new List<WorkerNpc>();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer()) return "월드 호스트(싱글 또는 서버 시작)에서만 부를 수 있어요.";
            var existing = new HashSet<int>();
            foreach (var zdo in AllZdos()) existing.Add(zdo.GetInt(WorkerNpc.KeyRole));
            Vector3 home = player.transform.position;
            for (int role = 0; role < 2; role++)
            {
                if (existing.Contains(role)) continue;
                Vector3 pos = home + player.transform.right * (role == 0 ? -2f : 2f) + player.transform.forward * 2f;
                GameObject go = Object.Instantiate(ZNetScene.instance.GetPrefab(WorkerPrefab.Name), pos, player.transform.rotation);
                var npc = go.GetComponent<WorkerNpc>();
                npc.Init(role == 0 ? Name1 : Name2, (Personality)role, pos);
                FarZones.Track(npc.GetZDOID());
                spawned.Add(npc);
            }
            foreach (var npc in spawned) npc.Say("spawn");
            return spawned.Count == 0 ? "두 명 다 이미 있어요." : $"NPC {spawned.Count}명 부름";
        }

        public static int DespawnAll()
        {
            int n = 0;
            foreach (var zdo in AllZdos())
            {
                FarZones.Untrack(zdo.m_uid);
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view != null) ZNetScene.instance.Destroy(view.gameObject);
                else { zdo.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(zdo); }
                n++;
            }
            return n;
        }

        // ---- 추적: 월드를 불러오면 저장된 NPC를 한 번 찾아 추적한다(먼 곳에 있어도 계속 돌도록) ----
        // 섹터 400개씩 프레임마다 나눠 찾는다(큰 월드에서 끊김 방지). 그 뒤 새 NPC는 Spawn에서 추적한다.

        static ZDOMan s_scanFor;
        static readonly List<ZDO> s_scanList = new List<ZDO>();
        static int s_scanIndex;
        static bool s_scanDone;

        public static void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            if (s_scanFor != ZDOMan.instance)
            {
                s_scanFor = ZDOMan.instance;
                s_scanList.Clear();
                s_scanIndex = 0;
                s_scanDone = false;
            }
            if (s_scanDone || !ZDOMan.instance.GetAllZDOsWithPrefabIterative(WorkerPrefab.Name, s_scanList, ref s_scanIndex)) return;
            s_scanDone = true;
            foreach (var zdo in s_scanList) FarZones.Track(zdo.m_uid);
            Plugin.Log.LogInfo($"저장된 NPC {s_scanList.Count}명 추적 시작");
            s_scanList.Clear();
        }
    }
}
