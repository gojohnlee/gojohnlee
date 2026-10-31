using HarmonyLib;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 상자에서 계속채워 등록(사용자 10-30): 상자 창 칸을 Alt+우클릭 → 바닐라 묶음 나누기 창(SplitDialog)을 "계속 채우기" 제목으로 빌려 목표 수를 고름.
    /// 확인하면 "!계속채워 [개인] 프리팹 수"를 채팅 RPC(VH_Chat)로 보냄 — 손님도 같은 길, 호스트가 채팅 명령과 똑같이 처리.
    /// 개인 상자(내 것)면 개인, 남의 개인 상자는 안 됨. 창을 닫는 길(취소·Esc·인벤토리 닫기)은 모두 HideSplitDialog → 우리 상태 지움.
    /// </summary>
    internal static class RefillPick
    {
        static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        static readonly AccessTools.FieldRef<InventoryGui, Container> Current = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> SplitItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_splitItem");
        static readonly AccessTools.FieldRef<InventoryGui, Inventory> SplitInv = AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_splitInventory");
        const string Title = "계속 채우기";

        /// <summary>창이 우리 것으로 열려 있으면 그 물건 프리팹(시험용).</summary>
        internal static string Active { get; private set; }
        internal static bool Personal { get; private set; }
        internal static string LastSent { get; private set; }

        /// <summary>상자 창의 물건으로 목표 수 창을 연다. 열었으면 true.</summary>
        internal static bool Open(InventoryGui gui, ItemDrop.ItemData item, Container chest)
        {
            if (gui == null || item == null || chest == null || item.m_dropPrefab == null || Player.m_localPlayer == null) return false;
            ZNetView view = chest.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null) return false;
            bool priv = Chests.TagProvider != null && (Chests.TagProvider(zdo) & ChestTags.Private) != 0;
            long me = Player.m_localPlayer.GetPlayerID();
            if (priv && zdo.GetLong(ZDOVars.s_creator, 0L) != me)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "남의 개인 상자예요");
                return true;
            }
            string prefab = item.m_dropPrefab.name;
            int want = Target(prefab, priv, me);
            if (want <= 0) want = Mathf.Max(1, chest.GetInventory().CountItems(item.m_shared.m_name));
            int max = Mathf.Max(100, item.m_shared.m_maxStackSize * 10, want);

            Traverse.Create(gui).Method("ShowSplitDialog", item, chest.GetInventory()).GetValue();
            SplitDialog d = gui.m_splitDialog;
            d.m_splitSlider.minValue = 1f;
            d.m_splitSlider.maxValue = max;
            d.SliderValue = want;
            d.UpdateIcon(item.GetIcon(), Title + ": " + Localization.instance.Localize(item.m_shared.m_name) + (priv ? " (개인)" : ""));
            Active = prefab;
            Personal = priv;
            return true;
        }

        /// <summary>지금 목표(호스트만 앎 — 손님은 0이라 상자 개수로 시작).</summary>
        static int Target(string prefab, bool personal, long owner)
        {
            if (personal)
            {
                foreach (var e in Brain.Personal.Entries)
                    if (e.Owner == owner && e.Item == prefab) return e.Amount;
                return 0;
            }
            foreach (var kv in Brain.Refill.Items)
                if (kv.Key == prefab) return kv.Value;
            return 0;
        }

        static void Accept(InventoryGui gui)
        {
            int n = (int)gui.m_splitDialog.m_splitSlider.value;
            LastSent = "!계속채워 " + (Personal ? "개인 " : "") + Active + " " + n;
            Plugin.Log.LogInfo("상자에서 계속채워: " + LastSent);
            ChatNet.SendAsLocal(LastSent);
        }

        [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
        static class RightClickPatch
        {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item)
            {
                if (item == null || grid == null || grid != ContainerGrid(__instance)) return true;
                if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return true;
                return !Open(__instance, item, Current(__instance));
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "ShowSplitDialog")]
        static class ShowPatch
        {
            static void Prefix() => Active = null;
        }

        [HarmonyPatch(typeof(InventoryGui), "OnSplitOk")]
        static class OkPatch
        {
            static bool Prefix(InventoryGui __instance)
            {
                if (Active == null) return true;
                try { Accept(__instance); }
                catch (System.Exception e) { Plugin.Log.LogError("상자에서 계속채워 오류: " + e); }
                SplitItem(__instance) = null;
                SplitInv(__instance) = null;
                Traverse.Create(__instance).Method("HideSplitDialog").GetValue();
                return false;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "HideSplitDialog")]
        static class HidePatch
        {
            static void Postfix() => Active = null;
        }
    }
}
