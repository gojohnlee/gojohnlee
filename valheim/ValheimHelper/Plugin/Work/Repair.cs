using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 장비 수리(구상서 4장, 결정 E): 일꾼 짐의 도구·무기·갑옷 중 반 넘게 닳았거나 부러진 것을 거점 제작대에서 고친다.
    /// 바닐라 InventoryGui.CanRepair·RepairOneItem(게임 1.0.20)과 같은 규칙: m_canBeReparied, 그 물건 레시피의 수리대·제작대와
    /// 이름이 같은 설비, 설비 레벨(최대 4) ≥ 레시피 최소 레벨, 설비를 쓸 수 있음(지붕 70%·불, CraftingStation.CheckUsable).
    /// 무료이고 재료는 안 든다. 건축물 수리는 안 함(사용자 결정).
    /// </summary>
    public static class Repair
    {
        /// <summary>이 물건을 지금 거점에서 고칠 설비. 고칠 필요가 없거나 못 고치면 null.</summary>
        public static CraftingStation StationFor(ItemDrop.ItemData it, Vector3 home)
        {
            if (it == null || !it.m_shared.m_canBeReparied) return null;
            if (!RepairRule.Needs(it.m_shared.m_useDurability, it.m_durability, it.GetMaxDurability())) return null;
            Recipe recipe = ObjectDB.instance != null ? ObjectDB.instance.GetRecipe(it) : null;
            if (recipe == null) return null;
            foreach (CraftingStation want in new[] { recipe.m_repairStation, recipe.m_craftingStation })
            {
                if (want == null) continue;
                CraftingStation cs = CraftingStation.FindClosestStationInRange(want.m_name, home, Recipes.BaseRange);
                if (cs != null && Mathf.Min(cs.GetLevel(), 4) >= recipe.m_minStationLevel && Usable(cs)) return cs;
            }
            return null;
        }

        /// <summary>짐에서 고칠 것이 있는 첫 설비(이미 들른 설비 빼고). 없으면 null.</summary>
        public static CraftingStation Next(WorkerNpc npc, HashSet<CraftingStation> done)
        {
            foreach (var it in npc.GetInventory().GetAllItems())
            {
                CraftingStation cs = StationFor(it, npc.Home);
                if (cs != null && !done.Contains(cs)) return cs;
            }
            return null;
        }

        /// <summary>그 설비에서 고칠 수 있는 것을 다 고친다(설비 곁에서 부름). 고친 개수.</summary>
        public static int At(WorkerNpc npc, CraftingStation station)
        {
            var names = new List<string>();
            foreach (var it in npc.GetInventory().GetAllItems())
            {
                CraftingStation cs = StationFor(it, npc.Home);
                if (cs == null || cs.m_name != station.m_name) continue;
                names.Add($"{it.m_dropPrefab?.name} {it.m_durability:0}→{it.GetMaxDurability():0}");
                it.m_durability = it.GetMaxDurability();
            }
            if (names.Count > 0)
            {
                station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
                Plugin.Log.LogInfo($"{npc.NpcName} 수리({Localization.instance.Localize(station.m_name)}): {string.Join(", ", names.ToArray())}");
            }
            return names.Count;
        }

        /// <summary>바닐라 CraftingStation.CheckUsable과 같은 판단(플레이어 인자 없이).</summary>
        static bool Usable(CraftingStation cs)
        {
            if (cs.m_craftRequireRoof && !(TestRunner.Running && MachineStep.TestIgnoreRoof))
            {
                Vector3 p = cs.m_roofCheckPoint != null ? cs.m_roofCheckPoint.position : cs.transform.position;
                Cover.GetCoverForPoint(p, out float cover, out bool underRoof);
                if (!underRoof || cover < 0.7f) return false;
            }
            if (cs.m_craftRequireFire && !EffectArea.IsPointPlus025InsideBurningArea(cs.transform.position)) return false;
            return true;
        }
    }
}
