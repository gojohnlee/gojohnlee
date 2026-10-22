using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 일꾼 NPC 몸 (구상서 3장). Player 프리팹을 복제해 Player 부품 대신 붙는다(WorkerPrefab).
    /// 이름·성향·거점은 ZDO에 저장해 월드와 함께 남는다. 계산은 호스트가 한다(FarZones로 소유 유지).
    /// </summary>
    public class WorkerNpc : Humanoid
    {
        public static readonly List<WorkerNpc> All = new List<WorkerNpc>();

        public const string KeyName = "vh_name";
        public const string KeyRole = "vh_role";
        public const string KeyHome = "vh_home";
        /// <summary>처음 나타날 때 인사할지(새로 만든 NPC).</summary>
        public const string KeyGreet = "vh_greet";

        internal WorkerAI Ai;

        /// <summary>
        /// 땅의 물건 줍기(플레이어 자동 줍기와 같은 순서): 내 것이면 줍고, 아니면 소유권을 요청하고 다음에 다시.
        /// 바닐라 CanPickup은 이미 지워진(ZDO 없는) 물건에 true를 돌려주고 GameObject는 프레임 끝에야 사라진다
        /// → 같은 프레임에 다른 일꾼이 주운 것을 또 주워 복제됐다(회차 74 vh_basetest 제련소 동시: 광석 4 → 주괴 6). 그래서 ZDO부터 본다.
        /// ItemDrop.Pickup(Humanoid)은 소유권 대기 중 줍는 쪽을 Player로 가정해 NPC면 오류(0.3.4) → Humanoid.Pickup을 쓴다.
        /// </summary>
        public bool PickDrop(ItemDrop drop)
        {
            if (drop == null || !drop.TryGetComponent(out ZNetView v) || !v.IsValid()) return false;
            if (drop.CanPickup()) return Pickup(drop.gameObject);
            drop.RequestOwn();
            return false;
        }

        public const string KeyInventory = "vh_inv";

        protected override void Awake()
        {
            base.Awake();
            Ai = GetComponent<WorkerAI>();
            All.Add(this);
            if (m_nview.IsValid()) Workers.Know(m_nview.GetZDO().m_uid);
            // 바닐라 몬스터는 소지품을 저장하지 않는다. 일꾼은 들고 있던 짐·도구를 ZDO에 저장해 둔다.
            if (m_nview.IsValid() && m_nview.IsOwner())
            {
                byte[] data = m_nview.GetZDO().GetByteArray(KeyInventory);
                if (data != null) m_inventory.Load(new ZPackage(data));
            }
            m_inventory.m_onChanged = (System.Action)System.Delegate.Combine(m_inventory.m_onChanged, new System.Action(SaveInventory));
        }

        void SaveInventory()
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner()) return;
            var pkg = new ZPackage();
            m_inventory.Save(pkg);
            m_nview.GetZDO().Set(KeyInventory, pkg.GetArray());
        }

        /// <summary>운반 한도: 플레이어 기본값과 같은 300, 벨트(힘의 벨트 등)를 차면 그만큼 더 (구상서 3장).</summary>
        public float MaxCarry => 300f + (m_utilityItem?.m_shared.m_equipStatusEffect is SE_Stats s ? s.m_addMaxCarryWeight : 0f);

        // ---- 갑옷 (Player와 같게: 바닐라 Humanoid는 입은 갑옷을 피해 계산에 넣지 않는다) ----

        public override float GetBodyArmor()
        {
            float armor = 0f;
            if (m_chestItem != null) armor += m_chestItem.GetArmor();
            if (m_legItem != null) armor += m_legItem.GetArmor();
            if (m_helmetItem != null) armor += m_helmetItem.GetArmor();
            if (m_shoulderItem != null) armor += m_shoulderItem.GetArmor();
            m_seman.ApplyArmorMods(ref armor);
            return armor;
        }

        protected override void ApplyArmorDamageMods(ref HitData.DamageModifiers mods)
        {
            if (m_chestItem != null) mods.Apply(m_chestItem.m_shared.m_damageModifiers);
            if (m_legItem != null) mods.Apply(m_legItem.m_shared.m_damageModifiers);
            if (m_helmetItem != null) mods.Apply(m_helmetItem.m_shared.m_damageModifiers);
            if (m_shoulderItem != null) mods.Apply(m_shoulderItem.m_shared.m_damageModifiers);
        }

        // 맞으면 입은 것 중 하나가 닳는다(Player.DamageArmorDurability와 같게, NpcPatches.ArmorCounts가 부른다)
        protected override void DamageArmorDurability(HitData hit)
        {
            var list = new List<ItemDrop.ItemData>();
            if (m_chestItem != null) list.Add(m_chestItem);
            if (m_legItem != null) list.Add(m_legItem);
            if (m_helmetItem != null) list.Add(m_helmetItem);
            if (m_shoulderItem != null) list.Add(m_shoulderItem);
            if (list.Count == 0) return;
            float dmg = hit.GetTotalPhysicalDamage() + hit.GetTotalElementalDamage();
            if (dmg <= 0f) return;
            ItemDrop.ItemData it = list[Random.Range(0, list.Count)];
            it.m_durability = Mathf.Max(0f, it.m_durability - dmg);
        }

        /// <summary>그 물건들을 입거나 든다(이미 입은 건 그대로).</summary>
        public void Wear(IEnumerable<ItemDrop.ItemData> items)
        {
            foreach (var it in items)
                if (it != null && m_inventory.ContainsItem(it) && !IsItemEquiped(it)) EquipItem(it);
        }

        /// <summary>싸울 무기와 방패를 든다(가진 것 중 가장 좋은 것, 방패가 있으면 한손 무기). 없으면 false.</summary>
        public bool HoldWeapon()
        {
            var items = m_inventory.GetAllItems();
            ItemDrop.ItemData shield = Gear.Best(items, Gear.IsShield, Gear.ShieldScore);
            ItemDrop.ItemData weapon = Gear.Best(items, it => Gear.IsMelee(it) && (shield == null || !it.IsTwoHanded()), Gear.WeaponScore)
                                       ?? Gear.Best(items, Gear.IsMelee, Gear.WeaponScore);
            if (weapon == null)
            {
                if (HoldingBow()) { StopDraw(); UnequipItem(GetCurrentWeapon(), false); } // 활은 사냥에만: 싸울 땐 맨손이 낫다
                return false;
            }
            if (weapon.IsTwoHanded()) shield = null;
            Wear(new[] { weapon, shield });
            return GetCurrentWeapon() == weapon;
        }

        public bool IsWearing(ItemDrop.ItemData it) => IsItemEquiped(it);

        // ---- 활 (⑧ 사냥: 사슴·토끼처럼 도망만 하는 짐승) ----

        /// <summary>쏠 활(가장 좋은 것, 맞는 화살이 있을 때만). 없으면 null.</summary>
        public ItemDrop.ItemData ShootingBow()
        {
            ItemDrop.ItemData bow = Gear.BestBow(m_inventory.GetAllItems());
            return bow != null && Gear.CountArrows(m_inventory, bow) > 0 ? bow : null;
        }

        /// <summary>활을 든다(이미 들었으면 그대로). 쏠 활이 없으면 false.</summary>
        public bool HoldBow()
        {
            ItemDrop.ItemData bow = ShootingBow();
            if (bow == null) return false;
            if (!IsItemEquiped(bow)) EquipItem(bow);
            return GetCurrentWeapon() == bow;
        }

        public bool HoldingBow() => Gear.IsBow(GetCurrentWeapon());

        /// <summary>진단용: 오른손·왼손에 든 것, 당긴 시간.</summary>
        public string HandsDiag() => $"{m_rightItem?.m_shared.m_name}/{m_leftItem?.m_shared.m_name} 당김 {m_attackDrawTime:0.0}";

        /// <summary>
        /// 활을 당긴다(바닐라 Player.UpdateAttackBowDraw와 같은 방식, 기력 소모는 뺌 — 바닐라는 당기기를 플레이어만 한다).
        /// 당긴 정도(0~1)를 돌려준다. 화살이 없으면 -1.
        /// </summary>
        public float DrawBow(float dt)
        {
            ItemDrop.ItemData bow = GetCurrentWeapon();
            if (!Gear.IsBow(bow)) return -1f;
            if (m_attackDrawTime <= 0f)
            {
                if (InAttack() || !bow.m_shared.m_attack.StartDraw(this, bow)) return -1f;
                m_attackDrawTime = 0f;
            }
            m_attackDrawTime += dt;
            string anim = bow.m_shared.m_attack.m_drawAnimationState;
            if (!string.IsNullOrEmpty(anim))
            {
                m_zanim.SetBool(anim, true);
                m_zanim.SetFloat("drawpercent", GetAttackDrawPercentage());
            }
            return GetAttackDrawPercentage();
        }

        /// <summary>당긴 활을 놓는다(쏨). 쐈으면 true. 쏘든 못 쏘든 당김은 풀린다.</summary>
        public bool ReleaseBow()
        {
            ItemDrop.ItemData bow = GetCurrentWeapon();
            bool shot = Gear.IsBow(bow) && m_attackDrawTime > 0f && StartAttack(null, false);
            StopDraw();
            return shot;
        }

        public void StopDraw()
        {
            ItemDrop.ItemData bow = GetCurrentWeapon();
            if (bow != null && !string.IsNullOrEmpty(bow.m_shared.m_attack.m_drawAnimationState))
                m_zanim.SetBool(bow.m_shared.m_attack.m_drawAnimationState, false);
            m_attackDrawTime = 0f;
        }

        // ---- 음식 (구상서 3장: 음식 상자에서 최대 3종, 바닐라 수치·지속시간) ----

        public const string KeyFoods = "vh_foods";
        public Meals Meals { get; private set; } = new Meals();
        float m_foodTimer, m_foodSaveTimer;
        bool m_saidNoFood;

        /// <summary>소유자(호스트)에서 매 프레임: 음식 시간이 흐르고, 최대 체력이 바뀌고, 10초마다 음식만큼 회복한다.</summary>
        public void UpdateFood(float dt)
        {
            m_foodTimer += dt;
            if (m_foodTimer < 1f) return;
            float heal = Meals.Tick(m_foodTimer, Game.m_foodRate);
            m_foodTimer = 0f;
            SetMaxHealth(Meals.MaxHealth);
            if (heal > 0f && GetHealth() < GetMaxHealth()) Heal(heal, false);
            if (++m_foodSaveTimer >= 10f) { m_foodSaveTimer = 0f; SaveFoods(); }
        }

        void SaveFoods()
        {
            if (m_nview != null && m_nview.IsValid() && m_nview.IsOwner()) m_nview.GetZDO().Set(KeyFoods, Meals.Save());
        }

        public bool CanEatFrom(Inventory inv)
        {
            foreach (var it in inv.GetAllItems())
                if (Gear.IsFood(it) && Meals.CanEat(Gear.FoodOf(it))) return true;
            return false;
        }

        public static bool HasFood(Inventory inv)
        {
            foreach (var it in inv.GetAllItems()) if (Gear.IsFood(it)) return true;
            return false;
        }

        /// <summary>그 상자(인벤토리)에서 지금 먹을 수 있는 가장 좋은 음식들을 하나씩 꺼내 먹는다. 먹은 프리팹 이름들.</summary>
        public List<string> EatFrom(Inventory inv)
        {
            var foods = new List<FoodInfo>();
            var byName = new Dictionary<string, ItemDrop.ItemData>();
            foreach (var it in inv.GetAllItems())
            {
                if (!Gear.IsFood(it)) continue;
                FoodInfo f = Gear.FoodOf(it);
                if (byName.ContainsKey(f.Name)) continue;
                byName[f.Name] = it;
                foods.Add(f);
            }
            List<string> eaten = Meals.EatBest(foods);
            foreach (var n in eaten) inv.RemoveOneItem(byName[n]);
            if (eaten.Count > 0)
            {
                m_saidNoFood = false;
                SetMaxHealth(Meals.MaxHealth);
                SaveFoods();
            }
            return eaten;
        }

        // ---- 싸움 (구상서 8장, Guard) ----

        readonly Dictionary<Character, float> m_hitBy = new Dictionary<Character, float>();

        protected override void OnDamaged(HitData hit)
        {
            base.OnDamaged(hit);
            Character attacker = hit.GetAttacker();
            if (attacker == null || attacker == this) return;
            if (m_hitBy.Count > 40) { var old = new List<Character>(); foreach (var kv in m_hitBy) if (kv.Key == null || Time.time - kv.Value > 60f) old.Add(kv.Key); foreach (var o in old) m_hitBy.Remove(o); }
            m_hitBy[attacker] = Time.time;
            Guard.Learn(attacker); // 맞아 봤으니 그 적을 안다
        }

        /// <summary>그 적에게 최근 seconds초 안에 맞았는지.</summary>
        public bool HitBy(Character c, float seconds) => c != null && m_hitBy.TryGetValue(c, out float t) && Time.time - t < seconds;

        /// <summary>지금 마실 수 있는 체력 미드가 있는지(가졌고, 같은 미드 효과가 아직 없음).</summary>
        public bool CanDrinkHealthMead()
        {
            ItemDrop.ItemData mead = Gear.FindHealthMead(m_inventory);
            if (mead == null) return false;
            StatusEffect se = mead.m_shared.m_consumeStatusEffect;
            return !m_seman.HaveStatusEffect(se.NameHash()) && (string.IsNullOrEmpty(se.m_category) || !m_seman.HaveStatusEffectCategory(se.m_category));
        }

        /// <summary>
        /// 체력 미드를 마신다(Player.ConsumeItem처럼 상태 효과를 붙이고 하나 줄인다). 같은 종류의 미드 효과가 아직 있으면
        /// 바닐라처럼 못 마신다(미드 대기시간). 마셨으면 true.
        /// </summary>
        public bool DrinkHealthMead()
        {
            if (!CanDrinkHealthMead()) return false;
            ItemDrop.ItemData mead = Gear.FindHealthMead(m_inventory);
            StatusEffect se = mead.m_shared.m_consumeStatusEffect;
            m_seman.AddStatusEffect(se, true);
            m_inventory.RemoveOneItem(mead);
            Plugin.Log.LogInfo($"{NpcName} 미드 마심: {mead.m_shared.m_name}, 체력 {GetHealth():0}/{GetMaxHealth():0}");
            return true;
        }

        /// <summary>그 피해(Core.Npc.Resist.Kinds)에 이미 저항이 있는지(미드 효과·장비 포함, 바닐라 추위 판정과 같은 기준).</summary>
        public bool Resists(string kind)
        {
            HitData.DamageModifier m = GetDamageModifiers().GetModifier(Gear.ResistType(kind));
            return m == HitData.DamageModifier.Resistant || m == HitData.DamageModifier.VeryResistant || m == HitData.DamageModifier.SlightlyResistant
                || m == HitData.DamageModifier.Immune || m == HitData.DamageModifier.Ignore;
        }

        /// <summary>그 저항 미드를 가졌는지.</summary>
        public bool HasResistMead(string kind) => m_inventory.GetAllItems().Exists(it => Gear.ResistOf(it) == kind);

        /// <summary>저항 미드를 마신다(같은 효과·같은 분류 효과가 있으면 바닐라처럼 못 마심). 마셨으면 true.</summary>
        public bool DrinkResistMead(string kind)
        {
            ItemDrop.ItemData mead = m_inventory.GetAllItems().Find(it => Gear.ResistOf(it) == kind);
            if (mead == null) return false;
            StatusEffect se = mead.m_shared.m_consumeStatusEffect;
            if (m_seman.HaveStatusEffect(se.NameHash()) || (!string.IsNullOrEmpty(se.m_category) && m_seman.HaveStatusEffectCategory(se.m_category))) return false;
            m_seman.AddStatusEffect(se, true);
            m_inventory.RemoveOneItem(mead);
            Plugin.Log.LogInfo($"{NpcName} 저항 미드 마심: {mead.m_shared.m_name} ({kind})");
            return true;
        }

        /// <summary>중독 중인지.</summary>
        public bool Poisoned => m_seman.HaveStatusEffect(SEMan.s_statusEffectPoison);

        // ---- 죽음 (구상서 9장: 묘비 + 거점 즉시 부활, 스킬은 안 깎임) ----

        /// <summary>회수할 묘비들: "userID/id/시도횟수;…".</summary>
        public const string KeyGraves = "vh_graves";
        /// <summary>이번 실행에서 죽은 횟수(시험용).</summary>
        public int Deaths { get; private set; }
        static readonly AccessTools.FieldRef<Character, bool> DeadField = AccessTools.FieldRefAccess<Character, bool>("m_isDead");

        /// <summary>
        /// 바닐라 몬스터처럼 사라지지 않고: 들고 있던 것은 플레이어 묘비에 남기고(세계 설정의 죽음 규칙 그대로),
        /// 음식·상태 효과를 잃고, 거점 깃발에서 바로 다시 일어난다. 묘비 회수는 WorkerAI가 한가할 때 GraveJob으로 한다.
        /// </summary>
        public override void OnDeath()
        {
            if (!m_nview.IsValid() || !m_nview.IsOwner()) return;
            Deaths++;
            Vector3 where = transform.position;
            Character killer = m_lastHit?.GetAttacker();
            string by = killer != null && killer != this ? Localization.instance.Localize(killer.m_name) : null;
            string killerKind = killer != null && killer != this ? Utils.GetPrefabName(killer.gameObject) : m_lastHit?.m_hitType.ToString();
            Ai.Guard?.CloseEncounter(true, killerKind); // 장소 기억: 여기서 죽었다
            ZDOID grave = CreateGrave();
            if (!grave.IsNone()) SetGraves(AddTry(Graves(), grave, 0));
            Ai.OnDied(); // 그 묘비를 회수하던 중이었으면 시도 횟수를 올린다
            Meals = new Meals();
            SaveFoods();
            m_seman.RemoveAllStatusEffects(true);
            DeadField(this) = false;
            SetMaxHealth(Meals.MaxHealth);
            SetHealth(GetMaxHealth());
            TeleportTo(Home, transform.rotation, false);
            Scout.FromHome(Home, where, out string dir, out string dist);
            var args = new Dictionary<string, string> { ["by"] = by != null ? by + "한테 " : "", ["dir"] = dir, ["dist"] = dist };
            Say(grave.IsNone() ? "diedNoGrave" : "died", args);
            if (!grave.IsNone()) Scout.DeathPin(where, NpcName);
            Plugin.Log.LogInfo($"{NpcName} 죽음: {where} ({by ?? m_lastHit?.m_hitType.ToString() ?? "?"}), 묘비 {grave}");
        }

        /// <summary>Player.CreateTombStone과 같은 규칙으로 묘비를 놓는다. 남길 게 없으면 None.</summary>
        ZDOID CreateGrave()
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (m_inventory.NrOfItems() == 0 || WorkerPrefab.Tombstone == null || zs.GetGlobalKey(GlobalKeys.DeathKeepInventory)) return ZDOID.None;
            if (!zs.GetGlobalKey(GlobalKeys.DeathKeepEquip) && !zs.GetGlobalKey(GlobalKeys.DeathDeleteUnequipped)) UnequipAllItems();
            if (zs.GetGlobalKey(GlobalKeys.DeathDeleteItems) || zs.GetGlobalKey(GlobalKeys.DeathDeleteUnequipped)) m_inventory.RemoveUnequipped();
            if (zs.GetGlobalKey(GlobalKeys.DeathDeleteUnequipped) && !zs.GetGlobalKey(GlobalKeys.DeathKeepEquip)) UnequipAllItems();
            if (m_inventory.NrOfItems() == 0) return ZDOID.None;
            GameObject go = Instantiate(WorkerPrefab.Tombstone, GetCenterPoint(), transform.rotation);
            go.GetComponent<Container>().GetInventory().MoveInventoryToGrave(m_inventory);
            go.GetComponent<TombStone>().Setup(NpcName, 0L);
            ZNetView view = go.GetComponent<ZNetView>();
            return view != null && view.IsValid() ? view.GetZDO().m_uid : ZDOID.None;
        }

        public sealed class GraveEntry
        {
            public ZDOID Id;
            public int Tries;
        }

        public List<GraveEntry> Graves()
        {
            var list = new List<GraveEntry>();
            if (m_nview == null || !m_nview.IsValid()) return list;
            foreach (var part in m_nview.GetZDO().GetString(KeyGraves).Split(';'))
            {
                string[] f = part.Split('/');
                if (f.Length == 3 && long.TryParse(f[0], out long user) && uint.TryParse(f[1], out uint id) && int.TryParse(f[2], out int tries))
                    list.Add(new GraveEntry { Id = new ZDOID(user, id), Tries = tries });
            }
            return list;
        }

        public void SetGraves(List<GraveEntry> list)
        {
            var parts = new List<string>();
            foreach (var g in list) parts.Add($"{g.Id.UserID}/{g.Id.ID}/{g.Tries}");
            m_nview.GetZDO().Set(KeyGraves, string.Join(";", parts.ToArray()));
        }

        /// <summary>그 묘비의 시도 횟수를 더한다(없으면 넣는다).</summary>
        public static List<GraveEntry> AddTry(List<GraveEntry> list, ZDOID id, int add)
        {
            GraveEntry e = list.Find(g => g.Id == id);
            if (e == null) list.Add(e = new GraveEntry { Id = id });
            e.Tries += add;
            return list;
        }

        public void ForgetGrave(ZDOID id)
        {
            var list = Graves();
            list.RemoveAll(g => g.Id == id);
            SetGraves(list);
        }

        /// <summary>먹을 게 없다고 한 번만 말한다(먹으면 다시 말할 수 있다).</summary>
        public void SayNoFoodOnce()
        {
            if (m_saidNoFood) return;
            m_saidNoFood = true;
            Say("noFood");
        }

        /// <summary>시험 기록용: 이동 입력 크기, 달리기 입력, 실제 속도 값.</summary>
        public string MoveDebug
        {
            get
            {
                Vector3 p = transform.position;
                bool hit = ZoneSystem.instance.GetGroundHeight(p, out float ground);
                float gen = WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight(p.x, p.z) : 0f;
                Collider under = GetLastGroundCollider();
                Vector3 v = GetVelocity();
                v.y = 0f;
                return $"입력 {m_moveDir.magnitude:0.00}, 달리기 입력 {m_run}, 수영 {IsSwimming()}, 땅닿음 {IsOnGround()}, 속도 {v.magnitude:0.0}, " +
                       $"발밑 {(under != null ? $"{under.name}({LayerMask.LayerToName(under.gameObject.layer)}, 위 {under.transform.root.name})" : "없음")}, " +
                       $"높이 {p.y:0.0}/땅 {(hit ? ground.ToString("0.0") : "없음")}/원래땅 {gen:0.0}/물 {GetLiquidLevel():0.0}, 구역 {ZoneSystem.instance.IsZoneLoaded(p)}";
            }
        }

        /// <summary>그 종류의 도구 중 가장 좋은 것(도구 단계 → 해당 피해 순).</summary>
        public static ItemDrop.ItemData BestTool(Inventory inv, ToolKind kind)
        {
            ItemDrop.ItemData best = null;
            foreach (var it in inv.GetAllItems())
            {
                if (!IsTool(it, kind) || it.m_durability <= 0f && it.m_shared.m_useDurability) continue;
                if (best == null || Score(it, kind) > Score(best, kind)) best = it;
            }
            return best;
        }

        public static bool IsTool(ItemDrop.ItemData it, ToolKind kind) =>
            it.IsWeapon() && (kind == ToolKind.Axe ? it.m_shared.m_skillType == Skills.SkillType.Axes
                            : kind == ToolKind.Pickaxe && it.m_shared.m_skillType == Skills.SkillType.Pickaxes);

        static float Score(ItemDrop.ItemData it, ToolKind kind)
        {
            var d = it.GetDamage();
            return it.m_shared.m_toolTier * 1000f + (kind == ToolKind.Axe ? d.m_chop : d.m_pickaxe);
        }

        /// <summary>도구를 손에 든다(이미 들고 있으면 그대로).</summary>
        public bool HoldTool(ToolKind kind)
        {
            ItemDrop.ItemData cur = GetCurrentWeapon();
            if (cur != null && IsTool(cur, kind) && !(cur.m_shared.m_useDurability && cur.m_durability <= 0f)) return true;
            ItemDrop.ItemData best = BestTool(m_inventory, kind);
            return best != null && EquipItem(best);
        }

        public int CountItem(string prefab)
        {
            int n = 0;
            foreach (var it in m_inventory.GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) n += it.m_stack;
            return n;
        }

        // Humanoid.Start는 몬스터용 기본 장비를 준다. 장비는 상자에서 받으므로(4장) 하지 않는다.
        protected override void Start()
        {
            if (!m_nview.IsValid() || !m_nview.IsOwner()) return;
            ApplyLook();
            // 저장된 소지품의 "입음" 표시대로 다시 입는다(Player.EquipInventoryItems와 같게)
            foreach (var it in m_inventory.GetEquippedItems())
                if (!EquipItem(it, false)) it.m_equipped = false;
            Meals = Meals.Load(m_nview.GetZDO().GetString(KeyFoods), Gear.FoodOf);
            SetMaxHealth(Meals.MaxHealth);
            if (m_nview.GetZDO().GetBool(KeyGreet))
            {
                m_nview.GetZDO().Set(KeyGreet, false);
                Say("spawn");
            }
        }

        protected override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public string NpcName => m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetString(KeyName, "Worker") : "Worker";
        public Personality Personality => m_nview != null && m_nview.IsValid() ? (Personality)m_nview.GetZDO().GetInt(KeyRole) : Personality.Scv;
        public Vector3 Home => m_nview.GetZDO().GetVec3(KeyHome, transform.position);

        public override string GetHoverName() => NpcName;

        // Character.TeleportTo는 비어 있다(플레이어만 순간이동). 막혔을 때 거점 복귀 등에 쓴다.
        public override bool TeleportTo(Vector3 pos, Quaternion rot, bool distantTeleport)
        {
            if (!m_nview.IsValid()) return false;
            if (!m_nview.IsOwner())
            {
                m_nview.InvokeRPC("RPC_TeleportTo", pos, rot, distantTeleport);
                return true;
            }
            // 땅 밑에 묻히지 않게 단단한 면 위로 올린다. GetSolidHeight는 1000m 위에서 내려 쏴 가장 위 면을 주는데,
            // 지하묘지 안(y>3000)은 위가 막힌 방이라 그게 묘지 지붕이다(도착점보다 6.5m 위에 서서 지붕 위를 걸었다, vh_crypttest 2026-10-23).
            // 안에서는 바닐라 Player.UpdateTeleport처럼 도착점 1m 위에서 아래로 바닥을 찾는다(ZoneSystem.FindFloor).
            bool floor = Character.InInterior(pos) ? ZoneSystem.instance.FindFloor(pos, out float h) : ZoneSystem.instance.GetSolidHeight(pos, out h);
            if (floor) pos.y = Mathf.Max(pos.y, h + 0.2f);
            transform.position = pos;
            m_body.position = pos; // 몸(Rigidbody)도 같이: 안 그러면 보간이 원래 자리로 되돌린다(0.3.7 시험: 40m 옮겼는데 그 자리에서 죽음)
            transform.rotation = rot;
            m_body.rotation = rot;
            m_body.linearVelocity = Vector3.zero;
            return true;
        }

        /// <summary>
        /// 외형: 둘 다 남자 바이킹, 1호는 갈색 머리·수염, 2호는 검은 머리·짧은 수염.
        /// 소유자 VisEquipment는 자기 필드를 쓰므로 생성될 때마다 다시 넣는다(바뀐 값만 ZDO에 기록된다).
        /// </summary>
        void ApplyLook()
        {
            VisEquipment vis = GetComponent<VisEquipment>();
            if (vis == null) return;
            bool scv = Personality == Personality.Scv;
            vis.SetModel(0);
            vis.SetHairItem((scv ? LookHair[0] : LookHair[1]).GetStableHashCode());
            vis.SetBeardItem((scv ? LookBeard[0] : LookBeard[1]).GetStableHashCode());
            vis.SetSkinColor(scv ? new Vector3(1f, 0.92f, 0.85f) : new Vector3(0.82f, 0.7f, 0.6f));
            vis.SetHairColor(scv ? new Vector3(0.45f, 0.35f, 0.25f) : new Vector3(0.08f, 0.08f, 0.08f));
        }

        public static readonly string[] LookHair = { "Hair2", "Hair1" };
        public static readonly string[] LookBeard = { "Beard5", "Beard2" };

        // ---- 스킬 (구상서 3-1장) [게임 시험 안 됨] ----

        public const string KeySkills = "vh_skills";
        SkillBook m_skillBook;
        static Dictionary<Skills.SkillType, float> s_skillSteps;

        /// <summary>NPC마다 따로(ZDO vh_skills). 10에서 시작, 70까지, 죽어도 안 깎임.</summary>
        public SkillBook SkillLevels
        {
            get
            {
                if (m_skillBook == null) m_skillBook = SkillBook.Load(m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetString(KeySkills, "") : "");
                return m_skillBook;
            }
        }

        /// <summary>바닐라 스킬 증가폭(플레이어 프리팹의 Skills.m_skills, 일꾼 프리팹에선 Skills를 뺐다).</summary>
        static float SkillStep(Skills.SkillType type)
        {
            if (s_skillSteps == null)
            {
                s_skillSteps = new Dictionary<Skills.SkillType, float>();
                Skills defs = Game.instance != null && Game.instance.m_playerPrefab != null ? Game.instance.m_playerPrefab.GetComponent<Skills>() : null;
                if (defs != null) foreach (var d in defs.m_skills) s_skillSteps[d.m_skill] = d.m_increseStep;
            }
            return s_skillSteps.TryGetValue(type, out float v) ? v : 1f;
        }

        public static string SkillName(Skills.SkillType type) => Localization.instance.Localize("$skill_" + type.ToString().ToLower());

        /// <summary>바닐라 Attack이 맞힐 때마다 부른다(Character 기본은 길들인 것만). 플레이어처럼 상태 효과 배율도 반영.</summary>
        public override void RaiseSkill(Skills.SkillType skill, float value = 1f)
        {
            if (skill == Skills.SkillType.None || !m_nview.IsValid() || !m_nview.IsOwner()) return;
            float multiplier = 1f;
            m_seman.ModifyRaiseSkill(skill, ref multiplier);
            int said = SkillLevels.Raise((int)skill, SkillStep(skill), value * multiplier, Game.m_skillGainRate);
            if (SkillLevels.Dirty) { m_nview.GetZDO().Set(KeySkills, SkillLevels.Save()); SkillLevels.Dirty = false; }
            if (said > 0) Say("skillUp", new Dictionary<string, string> { ["skill"] = SkillName(skill), ["level"] = said.ToString() });
        }

        // 피해·채집 속도는 지금 스킬의 플레이어와 같게(바닐라 Skills.GetSkillFactor / GetRandomSkillFactor)
        public override float GetSkillFactor(Skills.SkillType skill) => SkillLevels.Factor((int)skill);
        public override float GetRandomSkillFactor(Skills.SkillType skill) => SkillLevels.RandomFactor((int)skill, UnityEngine.Random.value);

        /// <summary>위험 판단용 평균 피해 계수.</summary>
        public float MeanSkillFactor(Skills.SkillType skill) => SkillLevels.MeanFactor((int)skill);

        // ---- 말하기 ----

        static readonly Speech s_speech = new Speech();

        /// <summary>스스로 하는 알림(명령에 대한 대답이 아닌 것): 같은 알림은 30초 안에 되풀이하지 않는다(구상서 10장 도배 방지).</summary>
        static readonly HashSet<string> Throttled = new HashSet<string> { "fightStart", "backOff", "flee", "help", "dangerSkip", "drink", "noFood", "graveRetry" };
        readonly Dictionary<string, float> m_lastSaid = new Dictionary<string, float>();

        public void Say(string key, IDictionary<string, string> args = null)
        {
            if (Throttled.Contains(key))
            {
                if (m_lastSaid.TryGetValue(key, out float t) && Time.time - t < 30f) return;
                m_lastSaid[key] = Time.time;
            }
            SayText(s_speech.Say(Personality, key, args));
        }

        public string Line(string key, IDictionary<string, string> args = null) => s_speech.Say(Personality, key, args);

        public void SayText(string text) => ChatNet.NpcSay(this, text);
    }
}
