using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Storage;

namespace ValheimHelper
{
    /// <summary>
    /// 제작대에서 거점 상자 재료를 바로 씀(상자 기능 ① 첫 조각: 제작·업그레이드). 바닐라 Player.HaveRequirementItems(있나)·ConsumeResources(빼기),
    /// InventoryGui.DoCrafting(막대 끝 실제 제작) 기준(디컴파일 확인 2026-10-23). "하나만 있으면 되는" 제작법(m_requireOnlyOneIngredient,
    /// GetFirstRequiredItem로 가방에서 직접 뺌)은 손대지 않는다. 건축(HaveRequirements(Piece), 둘째 조각)·개수 표시(SetupRequirement, 셋째)도 여기. 예약은 다음 조각.
    /// </summary>
    static class CraftPatches
    {
        static bool Counts(CraftingStation station, Piece.Requirement r) =>
            r.m_resItem && (station == null ? !r.m_upgraderResource : station.m_upgrader == r.m_upgraderResource);

        /// <summary>가방만으로 모자라면 가방 + 상자로 다시 판단(바닐라와 같은 거르기·같은 가방 세기).</summary>
        [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
        static class HaveItems
        {
            static void Postfix(Player __instance, Recipe piece, bool discover, int qualityLevel, int amount, ref bool __result)
            {
                if (__result || discover || piece == null || piece.m_requireOnlyOneIngredient || !BaseStock.Active(__instance)) return;
                CraftingStation station = __instance.GetCurrentCraftingStation();
                Inventory bag = __instance.GetInventory();
                foreach (var r in piece.m_resources)
                {
                    if (!Counts(station, r)) continue;
                    int need = r.GetAmount(qualityLevel) * amount;
                    if (need <= 0) continue;
                    var shared = r.m_resItem.m_itemData.m_shared;
                    int have = 0;
                    for (int q = 1; q < shared.m_maxQuality + 1; q++) have = System.Math.Max(have, bag.CountItems(shared.m_name, q));
                    if (have >= need) continue;
                    if (!StockTake.Enough(need, have, BaseStock.Count(__instance, shared.m_name))) return;
                }
                __result = true;
            }
        }

        /// <summary>가방에 모자라는 몫을 먼저 상자에서 빼고, 나머지(가방 몫)는 바닐라가 뺀다.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
        static class Consume
        {
            static void Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
            {
                if (requirements == null || !BaseStock.Active(__instance)) return;
                CraftingStation station = __instance.GetCurrentCraftingStation();
                Inventory bag = __instance.GetInventory();
                foreach (var r in requirements)
                {
                    if (!Counts(station, r)) continue;
                    int need = r.GetAmount(qualityLevel) * multiplier;
                    if (need <= 0) continue;
                    string name = r.m_resItem.m_itemData.m_shared.m_name;
                    int shortBy = need - bag.CountItems(name, itemQuality);
                    if (shortBy > 0) BaseStock.Take(__instance, name, shortBy);
                }
            }
        }

        static readonly AccessTools.FieldRef<Player, System.Collections.Generic.Dictionary<string, int>> s_knownStations =
            AccessTools.FieldRefAccess<Player, System.Collections.Generic.Dictionary<string, int>>("m_knownStations");

        /// <summary>
        /// 건축(조각 2번째): 바닐라 HaveRequirements(Piece, 모드)가 재료 때문에 거짓이면 가방 + 상자로 다시 판단.
        /// CanBuild(놓기·조각 목록 색) = 가방 수 + 상자 수 ≥ m_amount, CanAlmostBuild(목록에 보이기) = 가방이나 상자에 하나라도.
        /// 재료 밖의 이유(제작대·DLC)는 바닐라와 같은 순서로 다시 봐서 그대로 거짓. 빼기는 위 Consume(PlacePiece 뒤 ConsumeResources(…, 0)).
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
        static class HavePiece
        {
            static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
            {
                if (__result || piece == null || mode == Player.RequirementMode.IsKnown || !BaseStock.Active(__instance)) return;
                if (piece.m_craftingStation)
                {
                    if (mode == Player.RequirementMode.CanAlmostBuild)
                    {
                        if (!s_knownStations(__instance).ContainsKey(piece.m_craftingStation.m_name)) return;
                    }
                    else if (!CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, __instance.transform.position)
                        && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench)) return;
                }
                if (piece.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(piece.m_dlc)) return;
                Inventory bag = __instance.GetInventory();
                foreach (var r in piece.m_resources)
                {
                    if (!r.m_resItem || r.m_amount <= 0) continue;
                    string name = r.m_resItem.m_itemData.m_shared.m_name;
                    if (mode == Player.RequirementMode.CanAlmostBuild)
                    {
                        if (!bag.HaveItem(name) && BaseStock.Count(__instance, name) <= 0) return;
                    }
                    else
                    {
                        int have = bag.CountItems(name);
                        if (have < r.m_amount && !StockTake.Enough(r.m_amount, have, BaseStock.Count(__instance, name))) return;
                    }
                }
                __result = true;
            }
        }

        /// <summary>놓기 클릭(확인 → TryPlacePiece → ConsumeResources가 같은 프레임) 동안은 상자 수를 캐시 없이 셈.</summary>
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        static class Place
        {
            static void Prefix() => BaseStock.Fresh++;
            static void Finalizer() => BaseStock.Fresh--;
        }

        /// <summary>
        /// 개수 표시(조각 3번째): 바닐라 SetupRequirement(제작 목록·망치 Hud 둘 다)는 가방 수 &lt; 필요 수면 숫자를 빨갛게 깜빡임 →
        /// 가방 + 상자(0.5초 캐시)로 채워지면 흰색. 여러 개 만들기는 고정 m_multiCraftAmount를 craftMultiplier로 받아 같은 셈(HaveItems도 amount로 셈).
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
        static class Show
        {
            static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, int quality, int craftMultiplier, bool __result)
            {
                if (!__result || req?.m_resItem == null || player == null || !BaseStock.Active(player)) return;
                int need = req.GetAmount(quality) * craftMultiplier;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int have = player.GetInventory().CountItems(name);
                if (have >= need || !StockTake.Enough(need, have, BaseStock.Count(player, name))) return;
                var amount = elementRoot.Find("res_amount")?.GetComponent<TMPro.TMP_Text>();
                if (amount != null) amount.color = UnityEngine.Color.white;
            }
        }

        /// <summary>막대 끝 실제 제작 동안은 상자 수를 캐시 없이 셈(확인과 빼기가 같은 프레임).</summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        static class Craft
        {
            static void Prefix() => BaseStock.Fresh++;
            static void Finalizer() => BaseStock.Fresh--;
        }
    }
}
