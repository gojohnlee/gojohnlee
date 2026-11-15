using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>경계: 장소 기억(만난 적 셈)·적 약점·저항·받는 피해 셈.</summary>
    public sealed partial class Guard
    {
        // ---- 장소 기억 (설계 A-5) [게임 시험 안 됨] ----

        readonly Dictionary<string, GroupFoe> m_encounter = new Dictionary<string, GroupFoe>();
        ZoneXY m_encounterZone;
        Outcome m_encounterOutcome;
        bool m_inEncounter;

        /// <summary>마주침 동안 셈에 든 적: 종류마다 동시에 본 최대 수·가장 높은 레벨.</summary>
        void Tally(Vector3 pos)
        {
            if (!m_inEncounter)
            {
                m_inEncounter = true;
                m_encounter.Clear();
                m_encounterZone = Senses.ZoneOf(pos);
                m_encounterOutcome = Outcome.Won;
            }
            var counts = new Dictionary<string, int>();
            foreach (var c in m_foes)
            {
                string kind = Utils.GetPrefabName(c.gameObject);
                counts.TryGetValue(kind, out int n);
                counts[kind] = n + 1;
                if (!m_encounter.TryGetValue(kind, out var g)) m_encounter[kind] = g = new GroupFoe { Kind = kind };
                g.Level = Mathf.Max(g.Level, c.GetLevel());
                g.Boss |= c.IsBoss();
            }
            foreach (var kv in counts) m_encounter[kv.Key].Count = Mathf.Max(m_encounter[kv.Key].Count, kv.Value);
        }

        void Worse(Outcome o)
        {
            if (m_inEncounter && o > m_encounterOutcome) m_encounterOutcome = o;
        }

        /// <summary>마주침이 끝났다(경계가 Calm으로 돌아옴, 또는 죽음). died면 마주침이 없어도 죽은 곳을 적는다.</summary>
        public void CloseEncounter(bool died = false, string killer = null)
        {
            if (!m_inEncounter && !died) return;
            ZoneXY z = m_inEncounter ? m_encounterZone : Senses.ZoneOf(m_npc.transform.position);
            Outcome o = died ? Outcome.Died : m_encounterOutcome;
            var group = new List<GroupFoe>(m_encounter.Values);
            Brain.Places.Record(z, group, o, ZNet.instance.GetTimeSeconds(), Resolve, killer);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 장소 기억 {z}: {o}, 무리 [" + string.Join(", ", group.ConvertAll(g => $"{g.Kind}×{g.Count}★{g.Level - 1}").ToArray()) + "]");
            m_inEncounter = false;
            m_encounter.Clear();
        }

        /// <summary>
        /// 장소 기억의 적 한 종류(레벨 포함) → 판단용 수치. 배운 적이면 배운 값, 아니면 프리팹의 기본 체력과 게임 데이터 한 방(DataHit, 없으면 몸집 짐작).
        /// 체력은 바닐라처럼 기본 × 레벨(Character.SetupMaxHealth), 한 방은 별마다 ×1.5, 공격 간격 반영(FoeHitsPerSwing).
        /// </summary>
        public static Foe Resolve(GroupFoe g)
        {
            if (!Brain.Foes.TryGet(g.Kind, out float hp, out float dmg))
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(g.Kind) : null;
                Character c = prefab != null ? prefab.GetComponent<Character>() : null;
                CapsuleCollider col = prefab != null ? prefab.GetComponent<CapsuleCollider>() : null;
                hp = c != null ? c.m_health : 100f;
                dmg = c != null ? DataHit(c) : 0f;
                if (dmg <= 0f) dmg = Danger.GuessDamage(col != null ? col.height : 1.8f, col != null ? col.radius : 0.4f, 1);
            }
            int level = Mathf.Max(1, g.Level);
            return new Foe { Kind = g.Kind, Health = hp * level, Damage = dmg * (1f + (level - 1) * 0.5f) * FoeHitsPerSwing, Boss = g.Boss };
        }

        /// <summary>출발 전 판단용 내 한 방(가장 좋은 무기, 없으면 맨손; 플레이어는 빼고).</summary>
        public static float HitOf(WorkerNpc npc) => HitWith(npc, Gear.Best(npc.GetInventory().GetAllItems(), Gear.IsMelee, Gear.WeaponScore));

        /// <summary>그 무기(null이면 맨손)를 들었을 때 내 한 방(Prep.Ready — 상자의 더 좋은 무기까지).</summary>
        public static float HitWith(WorkerNpc npc, ItemDrop.ItemData w) => HitWith(npc, w, null);

        /// <summary>그 무기(null이면 맨손 = 둔기 5)로 그 적(mults = Mults, null이면 배율 없이)에게 들어가는 한 방.</summary>
        public static float HitWith(WorkerNpc npc, ItemDrop.ItemData w, float[] mults)
        {
            // 스킬 반영(3-1장): 평균 피해 계수(스킬 10이면 0.46, 70이면 0.82)
            if (w == null) return 5f * (mults != null ? mults[Weakness.Blunt] : 1f) * npc.MeanSkillFactor(Skills.SkillType.Unarmed);
            HitData.DamageTypes d = w.GetDamage();
            return Weakness.Hit(d.m_damage, Damages(d), mults) * npc.MeanSkillFactor(w.m_shared.m_skillType);
        }

        // ---- 적 약점·저항 (전투 보강 나, 바닐라 HitData.ApplyResistance) ----

        /// <summary>적의 피해 종류별 배율(Weakness.Kinds 순서). 살아 있는 적은 GetDamageModifiers(상태 효과 포함), 프리팹은 m_damageModifiers.</summary>
        public static float[] Mults(HitData.DamageModifiers m) => new[]
        {
            W(m.m_blunt), W(m.m_slash), W(m.m_pierce), W(m.m_fire), W(m.m_frost), W(m.m_lightning), W(m.m_poison), W(m.m_spirit),
        };

        static float W(HitData.DamageModifier d) => Weakness.Mult((int)d);

        static float[] Damages(HitData.DamageTypes d) => new[] { d.m_blunt, d.m_slash, d.m_pierce, d.m_fire, d.m_frost, d.m_lightning, d.m_poison, d.m_spirit };

        /// <summary>그 적에게 가장 세게 들어가는 근접 무기(스킬 반영). oneHanded면(방패가 있으면) 한손 무기 먼저. 없으면 null.</summary>
        public static ItemDrop.ItemData BestVs(WorkerNpc npc, IEnumerable<ItemDrop.ItemData> items, bool oneHanded, float[] mults)
        {
            System.Func<ItemDrop.ItemData, float> score = it => HitWith(npc, it, mults);
            return Gear.Best(items, it => Gear.IsMelee(it) && (!oneHanded || !it.IsTwoHanded()), score) ?? Gear.Best(items, Gear.IsMelee, score);
        }

        /// <summary>
        /// 위험 판단의 적 체력 보정(Weakness.HealthScale): 판단식은 배율 없는 내 한 방(HitOf)으로 세니, 내 무기 중 그 적에게 가장 세게 들어가는 한 방과의 비만큼 체력을 바꾼다.
        /// (함께 싸우는 플레이어·동료 몫도 같은 비로 본다 — 짐작.)
        /// </summary>
        public static float WeakScale(WorkerNpc npc, IEnumerable<ItemDrop.ItemData> items, float[] mults)
        {
            var list = new List<ItemDrop.ItemData>(items);
            float raw = HitWith(npc, Gear.Best(list, Gear.IsMelee, Gear.WeaponScore));
            float eff = HitWith(npc, BestVs(npc, list, false, mults), mults);
            return Weakness.HealthScale(raw, eff);
        }

        /// <summary>출발 판단용 Resolve: 장소 기억의 적 체력을 그 적 프리팹의 약점·저항과 내 무기(items, 상자 장비까지)로 보정.</summary>
        public static System.Func<GroupFoe, Foe> ResolveFor(WorkerNpc npc, List<ItemDrop.ItemData> items)
        {
            var scales = new Dictionary<string, float>();
            return g =>
            {
                Foe f = Resolve(g);
                if (!scales.TryGetValue(g.Kind, out float k))
                {
                    GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(g.Kind) : null;
                    Character c = prefab != null ? prefab.GetComponent<Character>() : null;
                    scales[g.Kind] = k = c != null ? WeakScale(npc, items, Mults(c.m_damageModifiers)) : 1f;
                }
                f.Health *= k;
                return f;
            };
        }

        /// <summary>출발 전 판단용: 적의 한 방이 내 갑옷을 거쳐 들어오는 피해(거점 기준 난이도 배율). 막기는 안 셈(!기억·시험용).</summary>
        public static System.Func<float, float> TakenFor(WorkerNpc npc) => TakenFor(npc, npc.GetBodyArmor());

        public static System.Func<float, float> TakenFor(WorkerNpc npc, float armor, Shield? shield = null, int foes = 1)
        {
            float scale = Game.instance.GetDifficultyDamageScalePlayer(npc.Home) * Game.m_enemyDamageRate;
            return Taken(scale, armor, shield, foes);
        }

        /// <summary>
        /// 판단식의 적 피해(한 방 × FoeHitsPerSwing)가 갑옷을 거쳐 들어오는 몫. 갑옷은 실제 한 방에 매기고 나서 공격 간격을 곱한다
        /// (회차 146 matetest: 줄인 피해 7에 갑옷 6을 매겨 2.0으로 셌는데 실제 14 → 8의 반 4 — 체력 32에서 회색난쟁이 둘과 싸우다 죽음).
        /// </summary>
        /// 방패가 있으면(shield, 전투 보강 가) 실제 한 방을 먼저 막기로 줄이고 갑옷을 매긴다 — 적이 Block.MaxFoes보다 많으면 안 셈(Core Block.Taken).
        static System.Func<float, float> Taken(float scale, float armor, Shield? shield, int foes) =>
            d => Block.Taken(d / FoeHitsPerSwing * scale, armor, shield, foes) * FoeHitsPerSwing;
    }
}
