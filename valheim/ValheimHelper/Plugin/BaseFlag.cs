using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 거점 깃발 (구상서 3장): 망치로 짓는 싼 건축물. 세우면 그 자리에 NPC 2명이 나타나고 거기가 귀환 지점이 된다.
    /// 월드에서 하나만 쓴다: 새로 세우면 거점이 그리로 옮겨 가고, 옛 깃발은 "쓰지 않음"으로 남는다(부수지는 않는다).
    /// 겉모습은 바닐라 건축물을 복제해 쓴다(후보를 차례로 찾는다).
    /// </summary>
    public static class BaseFlag
    {
        public const string Name = "VH_BaseFlag";
        public const string KeyActive = "vh_active";

        static readonly string[] Sources = { "piece_banner01", "wood_pole2", "wood_pole" };
        public static string SourceUsed { get; private set; }
        public static GameObject Prefab => s_prefab;
        static GameObject s_prefab;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        static class ZNetSceneAwake
        {
            static void Prefix(ZNetScene __instance)
            {
                try
                {
                    if (s_prefab == null) s_prefab = Build(__instance.m_prefabs);
                    if (s_prefab != null && !__instance.m_prefabs.Contains(s_prefab)) __instance.m_prefabs.Add(s_prefab);
                    AddToHammer();
                }
                catch (System.Exception e) { Plugin.Log.LogError("거점 깃발 만들기 실패: " + e); }
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        static class ObjectDBAwake
        {
            static void Postfix() => AddToHammer();
        }

        static GameObject Build(List<GameObject> prefabs)
        {
            GameObject src = null;
            foreach (var name in Sources)
            {
                src = prefabs.Find(p => p != null && p.name == name);
                if (src != null) { SourceUsed = name; break; }
            }
            if (src == null) { Plugin.Log.LogError("거점 깃발: 복제할 바닐라 건축물을 못 찾음 (" + string.Join(", ", Sources) + ")"); return null; }
            GameObject go = Object.Instantiate(src, WorkerPrefab.Container(), false);
            go.name = Name;
            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "거점 깃발";
            piece.m_description = "일꾼 NPC(라그나·플로키)의 집. 세우면 둘이 여기 나타나고, 일이 끝나면 여기로 돌아온다. 새로 세우면 거점이 옮겨 간다.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_craftingStation = null; // 작업대 없이도 지을 수 있게
            piece.m_groundPiece = false;
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) wear.m_noSupportWear = false; // 받침이 없어도 무너지지 않게
            go.AddComponent<BaseFlagPiece>();
            Plugin.Log.LogInfo($"거점 깃발 프리팹 {Name}: {SourceUsed} 복제");
            return go;
        }

        /// <summary>망치 건축 목록에 넣고 재료(나무 10)를 정한다. ObjectDB·ZNetScene 어느 쪽이 먼저 깨어나도 되게 둘 다에서 부른다.</summary>
        static void AddToHammer()
        {
            ObjectDB db = ObjectDB.instance;
            if (s_prefab == null || db == null) return;
            GameObject hammer = db.GetItemPrefab("Hammer");
            GameObject wood = db.GetItemPrefab("Wood");
            if (hammer == null || wood == null) return;
            Piece piece = s_prefab.GetComponent<Piece>();
            piece.m_resources = new[] { new Piece.Requirement { m_resItem = wood.GetComponent<ItemDrop>(), m_amount = 10, m_recover = true } };
            PieceTable table = hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            if (table != null && !table.m_pieces.Contains(s_prefab)) table.m_pieces.Add(s_prefab);
        }

        public static bool InHammer()
        {
            GameObject hammer = ObjectDB.instance?.GetItemPrefab("Hammer");
            PieceTable table = hammer?.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            return table != null && s_prefab != null && table.m_pieces.Contains(s_prefab);
        }

        // ---- 호스트: 새 깃발 → 거점 옮기기 ----

        [HarmonyPatch(typeof(Game), "Start")]
        static class GameStart
        {
            static void Postfix() => ZRoutedRpc.instance.Register<ZDOID>("VH_BaseSet", RPC_BaseSet);
        }

        /// <summary>깃발을 세운 사람의 게임에서 부른다(누가 세워도 호스트가 처리).</summary>
        internal static void NotifyPlaced(ZDOID flag) => ZRoutedRpc.instance.InvokeRoutedRPC("VH_BaseSet", flag);

        static void RPC_BaseSet(long sender, ZDOID id)
        {
            if (!ZNet.instance.IsServer()) return;
            if (ZDOMan.instance.GetZDO(id) == null) { Plugin.Instance.StartCoroutine(WaitFlag(id)); return; }
            SetBase(id);
        }

        // 손님이 세우면 깃발 ZDO보다 이 RPC가 먼저 온다(바닐라 PlacePiece가 만들자마자 OnPlaced, ZDO는 다음 전송 때) → 10초까지 기다린다.
        static System.Collections.IEnumerator WaitFlag(ZDOID id)
        {
            for (int i = 0; i < 20 && ZDOMan.instance != null && ZDOMan.instance.GetZDO(id) == null; i++) yield return new WaitForSeconds(0.5f);
            if (ZDOMan.instance?.GetZDO(id) == null) { Plugin.Log.LogWarning($"거점 깃발 {id}가 10초 안에 안 옴"); yield break; }
            SetBase(id);
        }

        static void SetBase(ZDOID id)
        {
            ZDO flag = ZDOMan.instance.GetZDO(id);
            if (flag == null) return;
            foreach (var other in AllFlags())
                if (other.m_uid != id && other.GetBool(KeyActive)) other.Set(KeyActive, false);
            flag.Set(KeyActive, true);
            Workers.OnBaseMoved(flag.GetPosition(), flag.GetRotation());
        }

        public static List<ZDO> AllFlags()
        {
            var list = ZdoScan.All(Name);
            return list;
        }
    }

    /// <summary>깃발에 붙는 부품: 세웠을 때 호스트에 알리고, 쳐다보면 상태를 보여 준다.</summary>
    public class BaseFlagPiece : MonoBehaviour, Hoverable, IPlaced
    {
        ZNetView m_nview;
        Piece m_piece;

        void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_piece = GetComponent<Piece>();
        }

        public void OnPlaced()
        {
            if (m_nview != null && m_nview.IsValid()) BaseFlag.NotifyPlaced(m_nview.GetZDO().m_uid);
        }

        public string GetHoverName() => m_piece != null ? m_piece.m_name : "거점 깃발";

        public string GetHoverText()
        {
            bool active = m_nview != null && m_nview.IsValid() && m_nview.GetZDO().GetBool(BaseFlag.KeyActive);
            return GetHoverName() + "\n" + (active
                ? $"<color=yellow>{Workers.Name1}·{Workers.Name2}의 집</color>"
                : "<color=grey>옛 거점 (쓰지 않음). 새 깃발을 세우면 그쪽이 집이 됩니다.</color>");
        }

        public float GetHoverOffset() => 0f;
    }
}
