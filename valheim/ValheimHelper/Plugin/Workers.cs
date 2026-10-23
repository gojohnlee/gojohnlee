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
        public const string Default1 = "라그나", Default2 = "플로키";
        public static string Name1 => s_name1?.Value ?? Default1;
        public static string Name2 => s_name2?.Value ?? Default2;

        public static void Init(ConfigFile cfg)
        {
            s_name1 = cfg.Bind("NPC", "Name1", Default1, "1호(라그나 — 침착·신중) 이름");
            s_name2 = cfg.Bind("NPC", "Name2", Default2, "2호(플로키 — 들뜬 장난꾸러기·공격적) 이름");
            // 옛 기본값(Claude·Alarak)이 남은 설정은 한 번 새 이름으로. 사용자가 고른 다른 이름은 그대로.
            if (s_name1.Value == Old1) s_name1.Value = Default1;
            if (s_name2.Value == Old2) s_name2.Value = Default2;
            new Terminal.ConsoleCommand("vh_spawn", "[Valheim Helper] 일꾼 NPC 2명을 내 옆에 부른다(없는 쪽만)", args => args.Context.AddString(Spawn(out _)));
            new Terminal.ConsoleCommand("vh_despawn", "[Valheim Helper] 일꾼 NPC를 모두 없앤다", args => args.Context.AddString($"NPC {DespawnAll()}명 없앰"));
        }

        static readonly HashSet<ZDOID> s_known = new HashSet<ZDOID>();

        /// <summary>
        /// 아는 NPC ZDO들(자주 부르는 곳용, 빠름). 월드를 불러올 때 한 번 찾고, 새로 만들 때 더한다. 없어진 것은 뺀다.
        /// </summary>
        public static List<ZDO> Zdos()
        {
            var list = new List<ZDO>();
            if (ZDOMan.instance == null) return list;
            s_known.RemoveWhere(id => ZDOMan.instance.GetZDO(id) == null);
            foreach (var id in s_known) list.Add(ZDOMan.instance.GetZDO(id));
            return list;
        }

        internal static void Know(ZDOID id) { if (!id.IsNone()) s_known.Add(id); }

        const string Old1 = "Claude", Old2 = "Alarak";

        /// <summary>옛 기본 이름(Claude·Alarak)으로 저장된 NPC는 지금 설정 이름으로 바꾼다(소유자가 Start 때 한 번).</summary>
        public static void RenameOld(ZDO zdo)
        {
            if (zdo == null) return;
            int role = zdo.GetInt(WorkerNpc.KeyRole);
            string name = zdo.GetString(WorkerNpc.KeyName, "");
            if (role == 0 && name == Old1 && Name1 != Old1) zdo.Set(WorkerNpc.KeyName, Name1);
            else if (role == 1 && name == Old2 && Name2 != Old2) zdo.Set(WorkerNpc.KeyName, Name2);
        }

        /// <summary>월드에 있는 NPC ZDO 전부를 직접 찾는다(느림: 소환·삭제 같은 드문 일에만). 호스트에서만 의미 있다.</summary>
        public static List<ZDO> AllZdos()
        {
            var list = ZdoScan.All(WorkerPrefab.Name);
            foreach (var z in list) s_known.Add(z.m_uid);
            return list;
        }

        /// <summary>콘솔 vh_spawn: 없는 NPC만 플레이어 옆에 부른다(거점도 거기).</summary>
        public static string Spawn(out List<ZDO> spawned)
        {
            spawned = new List<ZDO>();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer()) return "월드 호스트(싱글 또는 서버 시작)에서만 부를 수 있어요.";
            spawned = SpawnMissing(player.transform.position, player.transform.rotation);
            return spawned.Count == 0 ? "두 명 다 이미 있어요." : $"NPC {spawned.Count}명 부름";
        }

        /// <summary>거점 둘레의 각자 자리: 1호는 왼쪽 앞, 2호는 오른쪽 앞.</summary>
        public static Vector3 HomeSpot(Vector3 center, Quaternion rot, int role)
        {
            Vector3 p = center + rot * new Vector3(role == 0 ? -1.5f : 1.5f, 0f, 2f);
            // 높이는 지형 기준(깃발이 바위 위에 있어도 NPC 집은 땅): 구역이 안 불러와졌으면 깃발 높이 그대로.
            if (ZoneSystem.instance.GetGroundHeight(p, out float h)) p.y = h;
            return p;
        }

        /// <summary>
        /// 없는 역할의 NPC를 거점 둘레에 만든다. 인스턴스가 아니라 ZDO로 만들어서, 호스트에게서 먼 곳(구역이 안 불러와진 곳)이어도
        /// 추적(FarZones)이 구역을 만든 뒤 게임이 알아서 불러온다. 인사는 처음 나타날 때 한다(WorkerNpc.Start).
        /// </summary>
        public static List<ZDO> SpawnMissing(Vector3 center, Quaternion rot)
        {
            var created = new List<ZDO>();
            var existing = new HashSet<int>();
            foreach (var zdo in AllZdos()) existing.Add(zdo.GetInt(WorkerNpc.KeyRole));
            GameObject prefab = ZNetScene.instance.GetPrefab(WorkerPrefab.Name);
            ZNetView view = prefab.GetComponent<ZNetView>();
            int hash = WorkerPrefab.Name.GetStableHashCode();
            for (int role = 0; role < 2; role++)
            {
                if (existing.Contains(role)) continue;
                Vector3 pos = HomeSpot(center, rot, role) + Vector3.up * 0.5f;
                ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
                zdo.Persistent = true;
                zdo.Type = view.m_type;
                zdo.Distant = view.m_distant;
                zdo.SetPrefab(hash);
                zdo.SetRotation(rot);
                zdo.Set(WorkerNpc.KeyName, role == 0 ? Name1 : Name2);
                zdo.Set(WorkerNpc.KeyRole, role);
                zdo.Set(WorkerNpc.KeyHome, HomeSpot(center, rot, role));
                zdo.Set(WorkerNpc.KeyGreet, true);
                FarZones.Track(zdo.m_uid);
                Know(zdo.m_uid);
                created.Add(zdo);
            }
            return created;
        }

        /// <summary>새 거점: 집을 옮기고(한가하면 바로 이사), 없는 NPC는 거기에 만든다.</summary>
        public static void OnBaseMoved(Vector3 center, Quaternion rot)
        {
            foreach (var zdo in AllZdos())
            {
                int role = zdo.GetInt(WorkerNpc.KeyRole);
                zdo.Set(WorkerNpc.KeyHome, HomeSpot(center, rot, role));
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                WorkerNpc npc = view != null ? view.GetComponent<WorkerNpc>() : null;
                if (npc != null && npc.Ai.Current == WorkerAI.Mode.Idle)
                {
                    npc.Say("moveBase");
                    npc.Ai.GoHome();
                }
            }
            int n = SpawnMissing(center, rot).Count;
            Plugin.Log.LogInfo($"거점 이동 {center}, 새 NPC {n}명");
        }

        /// <summary>ZDO의 NPC 인스턴스(불러와져 있으면).</summary>
        public static WorkerNpc Find(ZDO zdo)
        {
            ZNetView view = zdo != null ? ZNetScene.instance.FindInstance(zdo) : null;
            return view != null ? view.GetComponent<WorkerNpc>() : null;
        }

        public static int DespawnAll() => DespawnNear(null, 0f);

        /// <summary>center 둘레 radius(수평) 안의 일꾼만 없앤다(동시 시험: 자기 자리 일꾼만 정리). center가 null이면 전부.</summary>
        public static int DespawnNear(Vector3? center, float radius)
        {
            int n = 0;
            foreach (var zdo in AllZdos())
            {
                if (center.HasValue)
                {
                    Vector3 d = zdo.GetPosition() - center.Value;
                    d.y = 0f;
                    if (d.magnitude > radius) continue;
                }
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

        static float s_nextReclaim;

        /// <summary>
        /// 호스트가 1초마다 NPC ZDO 주인이 자기가 아니면 되찾는다. 바닐라 2초 정리는 "주인이 근처면 그대로"라
        /// 친구가 NPC 근처에서 주인이 되면 안 돌려주기 때문(risks.txt 5).
        /// </summary>
        static void Reclaim()
        {
            if (Time.time < s_nextReclaim) return;
            s_nextReclaim = Time.time + 1f;
            long me = ZDOMan.GetSessionID();
            foreach (var zdo in Zdos())
                if (zdo.HasOwner() && zdo.GetOwner() != me) zdo.SetOwner(me);
        }

        public static void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            if (s_scanFor != ZDOMan.instance)
            {
                s_scanFor = ZDOMan.instance;
                s_known.Clear();
                s_scanList.Clear();
                s_scanIndex = 0;
                s_scanDone = false;
            }
            if (s_scanDone) { Reclaim(); return; }
            if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(WorkerPrefab.Name, s_scanList, ref s_scanIndex)) return;
            s_scanDone = true;
            foreach (var zdo in s_scanList) { FarZones.Track(zdo.m_uid); Know(zdo.m_uid); }
            Plugin.Log.LogInfo($"저장된 NPC {s_scanList.Count}명 추적 시작");
            s_scanList.Clear();
        }
    }
}
