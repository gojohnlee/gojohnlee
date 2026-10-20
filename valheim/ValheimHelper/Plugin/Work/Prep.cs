using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 출발 준비 (구상서 3장, 4장). 작업을 시작할 때와 거점에 짐을 넣을 때마다 한다.
    /// - 장비 상자(그 NPC 것)에 지금보다 좋은 무기·방패·투구·갑옷·바지·망토·벨트가 있거나 체력 미드가 모자라면
    ///   가서 챙겨 입고, 밀려난 헌 장비는 상자에 돌려놓는다(도끼·곡괭이는 가장 좋은 것 하나씩 들고 다닌다).
    /// - 음식 상자에 지금 먹을 수 있는 음식이 있으면 가서 먹는다(최대 3종, 바닐라 규칙).
    ///   먹은 것도 없고 음식 상자도 비었으면 한 번 알리고 그냥 간다.
    /// 갈 일이 없으면 바로 끝난다. 상자에 못 닿거나 누가 계속 열고 있으면 건너뛴다.
    /// </summary>
    public sealed class Prep
    {
        public const int MeadsToCarry = 3;
        const float WalkLimit = 60f, BusyLimit = 20f;

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        bool m_checkedEquip, m_checkedFood;
        Container m_chest;
        bool m_forFood;
        float m_time;

        public Prep(WorkerNpc npc)
        {
            m_npc = npc;
            m_ai = npc.Ai;
        }

        /// <returns>true = 준비 중(걷거나 기다림), false = 끝.</returns>
        public bool Update(float dt)
        {
            if (m_chest == null && !Next()) return false;
            m_time += dt;
            if (m_time > WalkLimit) { Plugin.Log.LogInfo($"{m_npc.NpcName} 준비: 상자에 못 닿아 건너뜀"); m_chest = null; return true; }
            Vector3 at = m_chest.transform.position;
            if (Utils.DistanceXZ(at, m_npc.transform.position) > 2.5f)
            {
                m_ai.MoveToPoint(dt, at, 1.8f, Utils.DistanceXZ(at, m_npc.transform.position) > 10f);
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(at);
            bool forFood = m_forFood;
            bool done = Chests.TryEdit(m_chest, inv =>
            {
                if (forFood)
                {
                    var eaten = m_npc.EatFrom(inv);
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 먹음: {string.Join(", ", eaten.ToArray())} → 최대 체력 {m_npc.Meals.MaxHealth:0}");
                }
                else TakeGear(inv);
            });
            if (!done && m_time < BusyLimit) return true; // 누가 열고 있다
            m_chest = null;
            return true;
        }

        /// <summary>다음에 갈 상자를 고른다. 갈 데가 없으면 false.</summary>
        bool Next()
        {
            m_time = 0f;
            if (!m_checkedEquip)
            {
                m_checkedEquip = true;
                WearPlan(m_npc, Plan(m_npc.GetInventory(), null)); // 이미 가진 건 걸어가지 않고 바로 입는다
                Container c = Chests.Assigned(Chests.KeyEquip((int)m_npc.Personality));
                if (c != null && NeedsGear(c.GetInventory())) { m_chest = c; m_forFood = false; return true; }
            }
            if (!m_checkedFood)
            {
                m_checkedFood = true;
                Container f = Chests.Assigned(Chests.KeyFood);
                if (f != null && m_npc.CanEatFrom(f.GetInventory())) { m_chest = f; m_forFood = true; return true; }
                if (m_npc.Meals.Count == 0 && (f == null || !WorkerNpc.HasFood(f.GetInventory()))) m_npc.SayNoFoodOnce();
            }
            return false;
        }

        /// <summary>NPC 소지품과 상자를 합쳐 고른 장비 목록(무기, 방패, 입을 것 칸마다 하나).</summary>
        static List<ItemDrop.ItemData> Plan(Inventory mine, Inventory chest)
        {
            var all = new List<ItemDrop.ItemData>(mine.GetAllItems());
            if (chest != null) all.AddRange(chest.GetAllItems());
            ItemDrop.ItemData shield = Gear.Best(all, Gear.IsShield, Gear.ShieldScore);
            ItemDrop.ItemData weapon = Gear.Best(all, it => Gear.IsMelee(it) && (shield == null || !it.IsTwoHanded()), Gear.WeaponScore)
                                       ?? Gear.Best(all, Gear.IsMelee, Gear.WeaponScore);
            if (weapon != null && weapon.IsTwoHanded()) shield = null;
            var plan = new List<ItemDrop.ItemData> { weapon, shield };
            foreach (var slot in Gear.WearSlots)
                plan.Add(Gear.Best(all, it => it.m_shared.m_itemType == slot, Gear.WearScore));
            plan.RemoveAll(it => it == null);
            return plan;
        }

        bool NeedsGear(Inventory chest)
        {
            Inventory mine = m_npc.GetInventory();
            foreach (var it in Plan(mine, chest))
                if (chest.ContainsItem(it)) return true;
            return CountMeads(mine) < MeadsToCarry && CountMeads(chest) > 0;
        }

        /// <summary>가진 것 중 가장 좋은 것을 입는다(묘비에서 되찾은 뒤 등).</summary>
        public static void WearBest(WorkerNpc npc) => WearPlan(npc, Plan(npc.GetInventory(), null));

        /// <summary>들고 다닐 것: 입을 것·무기·방패, 가장 좋은 도끼·곡괭이, 체력 미드. 나머지는 거점 상자에 둔다.</summary>
        public static HashSet<ItemDrop.ItemData> Kept(WorkerNpc npc)
        {
            Inventory mine = npc.GetInventory();
            var keep = new HashSet<ItemDrop.ItemData>(Plan(mine, null));
            keep.Add(WorkerNpc.BestTool(mine, ToolKind.Axe));
            keep.Add(WorkerNpc.BestTool(mine, ToolKind.Pickaxe));
            foreach (var it in mine.GetAllItems()) if (Gear.IsHealthMead(it)) keep.Add(it);
            keep.Remove(null);
            return keep;
        }

        /// <summary>장비(무기·방패·입을 것·연장)인지: 남는 장비는 장비 상자로 돌려놓는다.</summary>
        public static bool IsGear(ItemDrop.ItemData it) =>
            Gear.IsMelee(it) || Gear.IsShield(it) || System.Array.IndexOf(Gear.WearSlots, it.m_shared.m_itemType) >= 0
            || WorkerNpc.IsTool(it, ToolKind.Axe) || WorkerNpc.IsTool(it, ToolKind.Pickaxe);

        /// <summary>가진 것 중 입을 것(무기 말고 전부: 방패·투구·갑옷·바지·망토·벨트)을 입는다. 무기는 싸울 때 든다.</summary>
        static void WearPlan(WorkerNpc npc, List<ItemDrop.ItemData> plan) => npc.Wear(plan.FindAll(it => !Gear.IsMelee(it)));

        static int CountMeads(Inventory inv)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems()) if (Gear.IsHealthMead(it)) n += it.m_stack;
            return n;
        }

        void TakeGear(Inventory chest)
        {
            Inventory mine = m_npc.GetInventory();
            List<ItemDrop.ItemData> plan = Plan(mine, chest);
            var taken = new List<string>();
            foreach (var it in plan)
            {
                if (!chest.ContainsItem(it) || !mine.CanAddItem(it)) continue;
                mine.MoveItemToThis(chest, it);
                taken.Add(it.m_shared.m_name);
            }
            // 입을 것은 입고(무기는 싸울 때 든다), 밀려난 헌 장비는 돌려놓는다
            WearPlan(m_npc, plan);
            var keep = new HashSet<ItemDrop.ItemData>(plan);
            keep.Add(WorkerNpc.BestTool(mine, ToolKind.Axe));
            keep.Add(WorkerNpc.BestTool(mine, ToolKind.Pickaxe));
            foreach (var it in new List<ItemDrop.ItemData>(mine.GetAllItems()))
            {
                bool gear = Gear.IsMelee(it) || Gear.IsShield(it) || System.Array.IndexOf(Gear.WearSlots, it.m_shared.m_itemType) >= 0;
                if (!gear || keep.Contains(it) || !chest.CanAddItem(it)) continue;
                if (m_npc.IsWearing(it)) m_npc.UnequipItem(it, false);
                chest.MoveItemToThis(mine, it);
            }
            // 체력 미드
            int need = MeadsToCarry - CountMeads(mine);
            foreach (var it in new List<ItemDrop.ItemData>(chest.GetAllItems()))
            {
                if (need <= 0) break;
                if (!Gear.IsHealthMead(it)) continue;
                int n = Mathf.Min(need, it.m_stack);
                ItemDrop.ItemData copy = it.Clone();
                copy.m_stack = n;
                if (!mine.AddItem(copy)) break;
                chest.RemoveItem(it, n);
                need -= n;
                taken.Add($"{it.m_shared.m_name} x{n}");
            }
            Plugin.Log.LogInfo($"{m_npc.NpcName} 장비 챙김: {string.Join(", ", taken.ToArray())}, 방어력 {m_npc.GetBodyArmor():0}");
        }
    }
}
