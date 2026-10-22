using System.Collections.Generic;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 장비·음식·미드 고르기 (구상서 3장, 4장). 무엇이 무엇인지는 게임 데이터(ItemDrop.SharedData)로 판단한다
    /// → 새 바이옴 장비도 손대지 않아도 된다.
    /// </summary>
    public static class Gear
    {
        static bool Broken(ItemDrop.ItemData it) => it.m_shared.m_useDurability && it.m_durability <= 0f;

        /// <summary>싸울 때 드는 근접 무기(곡괭이·활·지팡이 제외). 도끼도 무기다.</summary>
        public static bool IsMelee(ItemDrop.ItemData it)
        {
            var t = it.m_shared.m_itemType;
            if (t != ItemDrop.ItemData.ItemType.OneHandedWeapon && t != ItemDrop.ItemData.ItemType.TwoHandedWeapon
                && t != ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft) return false;
            switch (it.m_shared.m_skillType)
            {
                case Skills.SkillType.Swords: case Skills.SkillType.Axes: case Skills.SkillType.Clubs:
                case Skills.SkillType.Spears: case Skills.SkillType.Knives: case Skills.SkillType.Polearms:
                    return it.HavePrimaryAttack();
            }
            return false;
        }

        /// <summary>무기 점수: 싸움 피해(물리+속성+독+영혼). 벌목·채굴 피해는 뺀다.</summary>
        public static float WeaponScore(ItemDrop.ItemData it)
        {
            HitData.DamageTypes d = it.GetDamage();
            return d.m_damage + d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
        }

        /// <summary>활(당겨 쏘는 것, 화살을 씀). 활은 사냥에만 쓴다(구상서 6-2) → 싸울 무기 고르기(IsMelee)에는 안 들어간다.</summary>
        public static bool IsBow(ItemDrop.ItemData it) =>
            it != null && it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && it.m_shared.m_attack.m_bowDraw
            && !string.IsNullOrEmpty(it.m_shared.m_ammoType);

        /// <summary>그 활에 맞는 화살.</summary>
        public static bool IsArrowFor(ItemDrop.ItemData arrow, ItemDrop.ItemData bow) =>
            arrow != null && bow != null && arrow.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && arrow.m_shared.m_ammoType == bow.m_shared.m_ammoType;

        public static ItemDrop.ItemData BestBow(IEnumerable<ItemDrop.ItemData> items) => Best(items, IsBow, WeaponScore);

        public static int CountArrows(Inventory inv, ItemDrop.ItemData bow)
        {
            int n = 0;
            if (bow != null) foreach (var it in inv.GetAllItems()) if (IsArrowFor(it, bow)) n += it.m_stack;
            return n;
        }

        public static bool IsShield(ItemDrop.ItemData it) => it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;

        /// <summary>몸에 입는 칸(투구·갑옷·바지·망토·벨트). 망토·벨트도 방어구 칸으로 친다.</summary>
        public static readonly ItemDrop.ItemData.ItemType[] WearSlots =
        {
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility,
        };

        /// <summary>입는 것 점수: 방어력, 같으면 이동 속도 벌칙이 적은 것. 벨트는 방어력이 없으니 무엇이든 0점.</summary>
        public static float WearScore(ItemDrop.ItemData it) => it.GetArmor() * 10f + it.m_shared.m_movementModifier * 10f;

        /// <summary>체력 미드: 먹으면 체력을 채우는 상태 효과가 붙는 소모품(음식 아님).</summary>
        public static bool IsHealthMead(ItemDrop.ItemData it) =>
            it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && it.m_shared.m_food <= 0f
            && it.m_shared.m_consumeStatusEffect is SE_Stats s && (s.m_healthUpFront > 0f || s.m_healthOverTime > 0f);

        /// <summary>마실 체력 미드(가장 많이 채우는 것). 없으면 null.</summary>
        public static ItemDrop.ItemData FindHealthMead(Inventory inv) =>
            Best(inv.GetAllItems(), IsHealthMead, it => it.m_shared.m_consumeStatusEffect is SE_Stats s ? s.m_healthUpFront + s.m_healthOverTime : 0f);

        /// <summary>
        /// 저항 미드면 막는 피해 종류("Poison"·"Frost"·"Fire", Core.Npc.Resist.Kinds), 아니면 null.
        /// 먹으면 그 피해에 저항(약간·보통·매우)을 주는 상태 효과가 붙는 소모품(음식 아님).
        /// </summary>
        public static string ResistOf(ItemDrop.ItemData it)
        {
            if (it.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable || it.m_shared.m_food > 0f) return null;
            if (!(it.m_shared.m_consumeStatusEffect is SE_Stats s)) return null;
            foreach (var m in s.m_mods)
            {
                if (m.m_modifier != HitData.DamageModifier.Resistant && m.m_modifier != HitData.DamageModifier.VeryResistant
                    && m.m_modifier != HitData.DamageModifier.SlightlyResistant) continue;
                if (m.m_type == HitData.DamageType.Poison) return "Poison";
                if (m.m_type == HitData.DamageType.Frost) return "Frost";
                if (m.m_type == HitData.DamageType.Fire) return "Fire";
            }
            return null;
        }

        public static HitData.DamageType ResistType(string kind) =>
            kind == "Poison" ? HitData.DamageType.Poison : kind == "Frost" ? HitData.DamageType.Frost : HitData.DamageType.Fire;

        public static bool IsFood(ItemDrop.ItemData it) =>
            it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && (it.m_shared.m_food > 0f || it.m_shared.m_foodStamina > 0f);

        public static FoodInfo FoodOf(ItemDrop.ItemData it) => new FoodInfo
        {
            Name = it.m_dropPrefab != null ? it.m_dropPrefab.name : it.m_shared.m_name,
            Health = it.m_shared.m_food, Stamina = it.m_shared.m_foodStamina, Eitr = it.m_shared.m_foodEitr,
            Regen = it.m_shared.m_foodRegen, BurnTime = it.m_shared.m_foodBurnTime,
        };

        /// <summary>프리팹 이름 → 음식 수치(저장된 음식 불러오기). 음식이 아니면 null.</summary>
        public static FoodInfo FoodOf(string prefab)
        {
            var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null || !IsFood(drop.m_itemData)) return null;
            var f = FoodOf(drop.m_itemData);
            f.Name = prefab;
            return f;
        }

        /// <summary>그 칸에 가장 좋은 것(망가진 것 제외). 없으면 null.</summary>
        public static ItemDrop.ItemData Best(IEnumerable<ItemDrop.ItemData> items, System.Func<ItemDrop.ItemData, bool> fits, System.Func<ItemDrop.ItemData, float> score)
        {
            ItemDrop.ItemData best = null;
            foreach (var it in items)
            {
                if (it == null || Broken(it) || !fits(it)) continue;
                if (best == null || score(it) > score(best)) best = it;
            }
            return best;
        }

        public static float ShieldScore(ItemDrop.ItemData it) => it.GetBaseBlockPower();
    }
}
