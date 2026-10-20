using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// NPC 프리팹: 게임의 Player 프리팹을 복제해 몸·애니메이션·장비 표시·동기화를 그대로 쓰고,
    /// Player 부품만 WorkerNpc(Humanoid) + WorkerAI(BaseAI)로 바꾼다 (구상서 12장).
    /// ZNetScene.Awake 앞에서 목록에 넣어 모든 접속자가 같은 프리팹을 알게 한다(모두 이 모드를 깐다).
    /// </summary>
    public static class WorkerPrefab
    {
        public const string Name = "VH_Worker";

        static GameObject s_prefab;
        static GameObject s_container;

        /// <summary>복제본에서 지운 부품 이름과 남은 부품 이름 (시험 보고용).</summary>
        public static readonly List<string> Removed = new List<string>();
        public static readonly List<string> Kept = new List<string>();

        // Player에 기대는 부품. Humanoid만으로는 필요 없다.
        static readonly HashSet<string> PlayerOnly = new HashSet<string> { "Player", "PlayerController", "Talker", "Skills" };

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        static class ZNetSceneAwake
        {
            static void Prefix(ZNetScene __instance)
            {
                try
                {
                    if (s_prefab == null) s_prefab = Build(__instance.m_prefabs.Find(p => p != null && p.name == "Player"));
                    if (s_prefab != null && !__instance.m_prefabs.Contains(s_prefab)) __instance.m_prefabs.Add(s_prefab);
                }
                catch (Exception e) { Plugin.Log.LogError("NPC 프리팹 만들기 실패: " + e); }
            }
        }

        static GameObject Build(GameObject player)
        {
            if (player == null) { Plugin.Log.LogError("Player 프리팹을 못 찾음"); return null; }
            GameObject go = UnityEngine.Object.Instantiate(player, Container(), false);
            go.name = Name;

            Player src = go.GetComponent<Player>();
            WorkerNpc npc = go.AddComponent<WorkerNpc>();
            CopyFields(src, npc, typeof(Character));
            CopyFields(src, npc, typeof(Humanoid));
            npc.m_name = "VH_Worker";
            npc.m_faction = Character.Faction.Players;

            Removed.Clear();
            Kept.Clear();
            // Player에 기대는 부품(RequireComponent)을 먼저 지워야 Player가 지워진다.
            foreach (var c in go.GetComponents<Component>())
            {
                string n = c.GetType().Name;
                if (n != "Player" && PlayerOnly.Contains(n)) { Removed.Add(n); UnityEngine.Object.DestroyImmediate(c); }
            }
            UnityEngine.Object.DestroyImmediate(src);
            if (go.GetComponent<Player>() != null)
            {
                Plugin.Log.LogError("NPC 프리팹: Player 부품을 지우지 못함. 다른 부품이 Player를 요구하는 듯함: "
                    + string.Join(", ", Array.ConvertAll(go.GetComponents<Component>(), c => c.GetType().Name)));
                UnityEngine.Object.DestroyImmediate(go);
                return null;
            }
            Removed.Add("Player");

            WorkerAI ai = go.AddComponent<WorkerAI>();
            ai.Setup();

            ZNetView nview = go.GetComponent<ZNetView>();
            nview.m_persistent = true; // 월드와 함께 저장
            foreach (var c in go.GetComponents<Component>()) Kept.Add(c.GetType().Name);
            Plugin.Log.LogInfo($"NPC 프리팹 {Name}: 지움 [{string.Join(", ", Removed)}], 남음 [{string.Join(", ", Kept)}]");
            return go;
        }

        /// <summary>복제 프리팹을 두는 꺼진 부모. 꺼진 부모 밑에 두면 복제본의 Awake가 돌지 않는다.</summary>
        internal static Transform Container()
        {
            if (s_container == null)
            {
                s_container = new GameObject("VH_Prefabs");
                s_container.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(s_container);
            }
            return s_container.transform;
        }

        /// <summary>declaring에 선언된 인스턴스 필드를 그대로 옮긴다(readonly 제외). 자식 Transform 참조도 복제본 안을 가리킨다.</summary>
        static void CopyFields(Component from, Component to, Type declaring)
        {
            foreach (FieldInfo f in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                f.SetValue(to, f.GetValue(from));
            }
        }
    }
}
