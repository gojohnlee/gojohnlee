using HarmonyLib;
using ValheimHelper.Core.Storage;

namespace ValheimHelper
{
    /// <summary>
    /// 제작대에서 거점 상자 재료를 바로 씀(상자 기능 ① 첫 조각: 제작·업그레이드). 바닐라 Player.HaveRequirementItems(있나)·ConsumeResources(빼기),
    /// InventoryGui.DoCrafting(막대 끝 실제 제작) 기준(디컴파일 확인 2026-10-23). "하나만 있으면 되는" 제작법(m_requireOnlyOneIngredient,
    /// GetFirstRequiredItem로 가방에서 직접 뺌)은 손대지 않는다. 건축(HaveRequirements(Piece))·개수 표시(SetupRequirement)·예약은 다음 조각.
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

        /// <summary>막대 끝 실제 제작 동안은 상자 수를 캐시 없이 셈(확인과 빼기가 같은 프레임).</summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        static class Craft
        {
            static void Prefix() => BaseStock.Fresh++;
            static void Finalizer() => BaseStock.Fresh--;
        }
    }
}
