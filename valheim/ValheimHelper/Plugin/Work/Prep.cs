using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 출발 준비 (구상서 3장, 4장). 작업을 시작할 때와 거점에 짐을 넣을 때마다 한다.
    /// - 거점 상자(깃발 125m 안 전부, 가까운 순)에 지금보다 좋은 무기·방패·투구·갑옷·바지·망토·벨트가 있거나 체력 미드가 모자라면
    ///   가서 챙겨 입고, 밀려난 헌 장비는 상자에 돌려놓는다(도끼·곡괭이는 가장 좋은 것 하나씩 들고 다닌다).
    /// - 거점 상자에 지금 먹을 수 있는 음식이 있으면 가서 먹는다(최대 3종, 바닐라 규칙).
    ///   먹은 것도 없고 거점 상자에 음식도 없으면 한 번 알리고 그냥 간다.
    /// 갈 일이 없으면 바로 끝난다. 상자에 못 닿거나 누가 계속 열고 있으면 건너뛴다.
    /// </summary>
    public sealed class Prep
    {
        public const int MeadsToCarry = 3;
        const float WalkLimit = 60f, BusyLimit = 20f, RepairReach = 3.5f;

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        bool m_wore;
        readonly HashSet<Container> m_gearDone = new HashSet<Container>(), m_foodDone = new HashSet<Container>();
        Container m_chest;
        bool m_forFood;
        /// <summary>이번 상자에서 챙길 것: 장비(장비 상자) / 미드(음식·미드 상자). 이름 없는 상자는 둘 다.</summary>
        bool m_takeGear, m_takeMeads;
        float m_time;
        /// <summary>장비 수리(Repair): 지금 가는 설비, 이미 들른 설비.</summary>
        CraftingStation m_station;
        readonly HashSet<CraftingStation> m_repairDone = new HashSet<CraftingStation>();

        public Prep(WorkerNpc npc)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_noBow = npc.IsGuard; // 호위병은 활(사냥)을 안 챙긴다 — 일꾼 몫(사용자 10-26)
        }

        readonly bool m_noBow;

        /// <returns>true = 준비 중(걷거나 기다림), false = 끝.</returns>
        public bool Update(float dt)
        {
            if (m_chest == null && m_station == null && !Next()) return false;
            m_time += dt;
            if (m_station != null) return UpdateRepair(dt);
            if (m_time > WalkLimit) { Plugin.Log.LogInfo($"{m_npc.NpcName} 준비: 상자에 못 닿아 건너뜀"); m_chest = null; return true; }
            Vector3 at = m_chest.transform.position;
            if (Utils.DistanceXZ(at, m_npc.transform.position) > 2.5f)
            {
                m_ai.MoveToPoint(dt, at, 1.8f, Utils.DistanceXZ(at, m_npc.transform.position) > 10f);
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(at);
            bool forFood = m_forFood, takeGear = m_takeGear, takeMeads = m_takeMeads;
            Container from = m_chest;
            bool done = Chests.TryEdit(from, inv =>
            {
                if (forFood)
                {
                    var eaten = m_npc.EatFrom(inv, from);
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 먹음: {string.Join(", ", eaten.ToArray())} → 최대 체력 {m_npc.Meals.MaxHealth:0}");
                }
                else TakeGear(from, inv, takeGear, takeMeads);
            });
            if (!done && m_time < BusyLimit) return true; // 누가 열고 있다
            m_chest = null;
            return true;
        }

        /// <summary>거점 설비까지 걸어가 짐에서 그 설비로 고칠 것을 다 고친다. 못 닿으면 건너뜀.</summary>
        bool UpdateRepair(float dt)
        {
            CraftingStation cs = m_station;
            Vector3 at = cs.transform.position;
            float d = Utils.DistanceXZ(at, m_npc.transform.position);
            if (m_time > WalkLimit) Plugin.Log.LogInfo($"{m_npc.NpcName} 수리: 설비에 못 닿아 건너뜀");
            else if (d > Mathf.Min(RepairReach, cs.m_rangeBuild))
            {
                m_ai.MoveToPoint(dt, at, RepairReach - 1f, d > 10f);
                return true;
            }
            else
            {
                m_ai.StopMoving();
                m_ai.Face(at);
                Repair.At(m_npc, cs);
            }
            m_repairDone.Add(cs);
            m_station = null;
            return true;
        }

        /// <summary>다음에 갈 상자를 고른다. 갈 데가 없으면 false.</summary>
        bool Next()
        {
            m_time = 0f;
            if (!m_wore)
            {
                // 입기 전에 고친다: 부러진 도구·갑옷은 고쳐야 고르기에 들어간다
                m_station = Repair.Next(m_npc, m_repairDone);
                if (m_station != null) return true;
                m_wore = true;
                WearPlan(m_npc, Plan(m_npc.GetInventory(), null)); // 이미 가진 건 걸어가지 않고 바로 입는다
            }
            // 거점 상자 전부(가까운 순)를 한 번씩: 더 좋은 장비·모자란 미드 → 먹을 음식
            List<Container> gearChests = Chests.NearBase(m_npc.Home, ChestRole.Gear);
            List<Container> chests = Chests.NearBase(m_npc.Home, ChestRole.Food);
            // 미드는 짐 정리가 음식·미드 상자(ChestRole.Food)에 넣으므로 거기서도 챙긴다(이름 붙은 "미드 상자"에서 못 꺼내던 것)
            var takeChests = new List<Container>(gearChests);
            foreach (var c in chests) if (!takeChests.Contains(c)) takeChests.Add(c);
            foreach (var c in takeChests)
            {
                bool gear = gearChests.Contains(c), meads = chests.Contains(c);
                if (m_gearDone.Add(c) && NeedsGear(c.GetInventory(), gear, meads))
                { m_chest = c; m_forFood = false; m_takeGear = gear; m_takeMeads = meads; return true; }
            }
            foreach (var c in chests)
                if (m_foodDone.Add(c) && m_npc.CanEatFrom(c.GetInventory())) { m_chest = c; m_forFood = true; return true; }
            if (m_npc.Meals.Count == 0 && !chests.Exists(c => WorkerNpc.HasFood(c.GetInventory()))) m_npc.SayNoFoodOnce();
            return false;
        }

        /// <summary>NPC 소지품과 상자를 합쳐 고른 장비 목록(무기, 방패, 입을 것 칸마다 하나).</summary>
        static List<ItemDrop.ItemData> Plan(Inventory mine, Inventory chest)
        {
            var all = new List<ItemDrop.ItemData>(mine.GetAllItems());
            if (chest != null) all.AddRange(chest.GetAllItems());
            return Plan(all);
        }

        /// <summary>
        /// 출발 준비를 마치면 입고 들 것: 짐 + 거점 장비 상자 전부에서 고른 갑옷 합(GetBodyArmor처럼 입는 칸)과 가장 센 근접 무기(Guard.HitOf처럼).
        /// 출발 판단이 상자에 넣어 둔 더 좋은 장비까지 셈(⑫ 다 "이 장비론 평원 무리예요" — 갑옷을 넣으면 다시 보고 출발).
        /// </summary>
        public static void Ready(WorkerNpc npc, out float armor, out ItemDrop.ItemData weapon) => Ready(npc, out armor, out weapon, out _);

        /// <summary>Ready + 싸울 때 들 방패(전투 보강 가: 출발 판단이 막기를 셈).</summary>
        public static void Ready(WorkerNpc npc, out float armor, out ItemDrop.ItemData weapon, out ItemDrop.ItemData shield)
        {
            var all = new List<ItemDrop.ItemData>(npc.GetInventory().GetAllItems());
            foreach (var c in Chests.NearBase(npc.Home, ChestRole.Gear)) all.AddRange(c.GetInventory().GetAllItems());
            armor = 0f;
            foreach (var it in Plan(all))
                if (System.Array.IndexOf(Gear.WearSlots, it.m_shared.m_itemType) >= 0) armor += it.GetArmor();
            weapon = Gear.Best(all, Gear.IsMelee, Gear.WeaponScore);
            shield = WorkerNpc.FightShield(all);
        }

        static List<ItemDrop.ItemData> Plan(List<ItemDrop.ItemData> all)
        {
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

        bool NeedsGear(Inventory chest, bool gear, bool meads)
        {
            Inventory mine = m_npc.GetInventory();
            if (gear)
                foreach (var it in Plan(mine, chest))
                    if (chest.ContainsItem(it)) return true;
            if (gear && !m_noBow && NeedsBow(mine, chest)) return true;
            if (!meads) return false;
            if (CountMeads(mine) < MeadsToCarry && CountMeads(chest) > 0) return true;
            foreach (var k in Resist.Kinds)
                if (CountResist(mine, k) < Resist.ToCarry && CountResist(chest, k) > 0) return true;
            return false;
        }

        static int CountResist(Inventory inv, string kind)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems()) if (Gear.ResistOf(it) == kind) n += it.m_stack;
            return n;
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
            foreach (var it in mine.GetAllItems()) if (Gear.IsHealthMead(it) || Gear.ResistOf(it) != null) keep.Add(it);
            ItemDrop.ItemData bow = Gear.BestBow(mine.GetAllItems());
            keep.Add(bow);
            foreach (var it in mine.GetAllItems()) if (Gear.IsArrowFor(it, bow)) keep.Add(it);
            keep.Remove(null);
            return keep;
        }

        /// <summary>
        /// 사냥용 활(⑧, 사슴·토끼): 상자에 더 좋은 활이 있거나, 활이 있는데 화살이 몫(Archery.ArrowsToCarry)보다 적고 상자에 맞는 화살이 있다.
        /// 활은 싸움에 안 쓰고 사냥에만 쓴다(구상서 6-2).
        /// </summary>
        static bool NeedsBow(Inventory mine, Inventory chest)
        {
            ItemDrop.ItemData have = Gear.BestBow(mine.GetAllItems()), there = Gear.BestBow(chest.GetAllItems());
            if (there != null && (have == null || Gear.WeaponScore(there) > Gear.WeaponScore(have))) return true;
            ItemDrop.ItemData bow = have ?? there;
            return bow != null && Gear.CountArrows(mine, bow) < Archery.ArrowsToCarry && Gear.CountArrows(chest, bow) > 0;
        }

        void TakeBow(Container from, Inventory mine, Inventory chest, List<string> taken)
        {
            ItemDrop.ItemData have = Gear.BestBow(mine.GetAllItems()), there = Gear.BestBow(chest.GetAllItems());
            if (there != null && (have == null || Gear.WeaponScore(there) > Gear.WeaponScore(have)) && mine.CanAddItem(there)
                && BaseStock.Allow(from, there.m_shared.m_name, 1) > 0)
            {
                mine.MoveItemToThis(chest, there);
                if (!chest.ContainsItem(there))
                {
                    taken.Add(there.m_shared.m_name);
                    if (have != null && chest.CanAddItem(have)) // 헌 활과 맞지 않게 된 화살은 돌려놓는다
                    {
                        if (m_npc.IsWearing(have)) m_npc.UnequipItem(have, false);
                        chest.MoveItemToThis(mine, have);
                        foreach (var a in new List<ItemDrop.ItemData>(mine.GetAllItems()))
                            if (Gear.IsArrowFor(a, have) && !Gear.IsArrowFor(a, there) && chest.CanAddItem(a)) chest.MoveItemToThis(mine, a);
                    }
                }
            }
            ItemDrop.ItemData bow = Gear.BestBow(mine.GetAllItems());
            if (bow != null)
                TakeMeads(from, mine, chest, Archery.ArrowsToCarry - Gear.CountArrows(mine, bow), it => Gear.IsArrowFor(it, bow), taken);
        }

        /// <summary>
        /// 이 미드 더미에서 들고 다닐 몫(체력 3·저항 종류마다 2)을 넘는 개수. 미드가 아니거나 넘지 않으면 0.
        /// Kept는 더미 단위라 발효로 6개를 꺼내면 6개를 다 지니게 됨 → 짐 정리가 넘는 만큼은 상자에 넣는다.
        /// </summary>
        public static int ExtraMeads(WorkerNpc npc, ItemDrop.ItemData it)
        {
            Inventory mine = npc.GetInventory();
            string kind = Gear.ResistOf(it);
            int total, carry;
            if (kind != null) { total = CountResist(mine, kind); carry = Resist.ToCarry; }
            else if (Gear.IsHealthMead(it)) { total = CountMeads(mine); carry = MeadsToCarry; }
            else return 0;
            return Mathf.Clamp(total - carry, 0, it.m_stack);
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

        void TakeGear(Container from, Inventory chest, bool takeGear, bool takeMeads)
        {
            Inventory mine = m_npc.GetInventory();
            List<ItemDrop.ItemData> plan = takeGear ? Plan(mine, chest) : new List<ItemDrop.ItemData>(); // 미드 상자에서는 장비를 안 꺼냄
            var taken = new List<string>();
            foreach (var it in plan)
            {
                if (!chest.ContainsItem(it)) continue;
                if (BaseStock.Allow(from, it.m_shared.m_name, it.m_stack) < it.m_stack) continue; // 사람 제작 막대가 예약한 것
                if (!mine.CanAddItem(it) && !MakeRoom(mine, chest, it, plan)) continue; // 못 바꾸면 헌 것을 그대로 입고 간다
                mine.MoveItemToThis(chest, it);
                if (chest.ContainsItem(it)) continue;
                BaseStock.Changed();
                taken.Add(it.m_shared.m_name);
            }
            // 실제로 가진 것 기준으로(ChatGPT-01: 새 갑옷을 못 가져왔는데 헌 갑옷만 벗고 감) 입고, 밀려난 헌 장비는 돌려놓는다
            plan = Plan(mine, null);
            WearPlan(m_npc, plan);
            var keep = new HashSet<ItemDrop.ItemData>(plan);
            keep.Add(WorkerNpc.BestTool(mine, ToolKind.Axe));
            keep.Add(WorkerNpc.BestTool(mine, ToolKind.Pickaxe));
            foreach (var it in new List<ItemDrop.ItemData>(mine.GetAllItems()))
            {
                bool gear = Gear.IsMelee(it) || Gear.IsShield(it) || System.Array.IndexOf(Gear.WearSlots, it.m_shared.m_itemType) >= 0;
                if (!takeGear || !gear || keep.Contains(it) || !chest.CanAddItem(it)) continue; // 헌 장비는 장비 상자에만
                if (m_npc.IsWearing(it)) m_npc.UnequipItem(it, false);
                chest.MoveItemToThis(mine, it);
            }
            if (takeGear && !m_noBow) TakeBow(from, mine, chest, taken);
            if (takeMeads)
            {
                // 체력 미드
                TakeMeads(from, mine, chest, MeadsToCarry - CountMeads(mine), Gear.IsHealthMead, taken);
                // 저항 미드(결정 D): 종류마다 둘
                foreach (var k in Resist.Kinds)
                    TakeMeads(from, mine, chest, Resist.ToCarry - CountResist(mine, k), it => Gear.ResistOf(it) == k, taken);
            }
            Plugin.Log.LogInfo($"{m_npc.NpcName} 장비 챙김: {string.Join(", ", taken.ToArray())}, 방어력 {m_npc.GetBodyArmor():0}");
        }

        static void TakeMeads(Container from, Inventory mine, Inventory chest, int need, System.Predicate<ItemDrop.ItemData> kind, List<string> taken)
        {
            foreach (var it in new List<ItemDrop.ItemData>(chest.GetAllItems()))
            {
                if (need <= 0) break;
                if (!kind(it)) continue;
                int n = BaseStock.Allow(from, it.m_shared.m_name, Mathf.Min(need, it.m_stack)); // 사람 제작 막대가 예약한 몫은 안 가져감
                if (n <= 0) continue;
                ItemDrop.ItemData copy = it.Clone();
                copy.m_stack = n;
                if (!mine.AddItem(copy)) break;
                chest.RemoveItem(it, n);
                BaseStock.Changed();
                need -= n;
                taken.Add($"{it.m_shared.m_name} x{n}");
            }
        }

        /// <summary>
        /// 가방이 차서 새 장비를 못 받을 때: 같은 칸의 헌 장비(계획에 없는 것, 들고 다닐 연장 제외)를 먼저 상자에 넣어 자리를 만든다.
        /// 상자에도 자리가 없으면 false(아무것도 안 옮김).
        /// </summary>
        bool MakeRoom(Inventory mine, Inventory chest, ItemDrop.ItemData it, List<ItemDrop.ItemData> plan)
        {
            ItemDrop.ItemData axe = WorkerNpc.BestTool(mine, ToolKind.Axe), pick = WorkerNpc.BestTool(mine, ToolKind.Pickaxe);
            foreach (var old in new List<ItemDrop.ItemData>(mine.GetAllItems()))
            {
                if (plan.Contains(old) || old == axe || old == pick || !SameSlot(old, it) || !chest.CanAddItem(old)) continue;
                bool wearing = m_npc.IsWearing(old);
                if (wearing) m_npc.UnequipItem(old, false);
                chest.MoveItemToThis(mine, old);
                if (mine.CanAddItem(it)) return true;
                mine.MoveItemToThis(chest, old); // 그래도 자리가 안 났으면 되돌린다
                if (wearing) m_npc.Wear(new[] { old });
            }
            return false;
        }

        static bool SameSlot(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            if (Gear.IsMelee(a) || Gear.IsMelee(b)) return Gear.IsMelee(a) && Gear.IsMelee(b);
            return a.m_shared.m_itemType == b.m_shared.m_itemType && IsGear(a);
        }
    }
}
