using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 위험 감지와 싸움 (구상서 8장: NPC가 스스로, 0.5초마다 규칙으로). WorkerAI가 매 프레임 먼저 부르고,
    /// 경계가 몸을 쓰는 동안(싸움·물러나기·도망·미드)은 하던 일(Job)을 멈춰 둔다.
    /// - 적: 30m 안의 적대 생물(사슴 같은 순한 동물·잠든 것 제외). 싸울 상대는 나·동행을 노리거나 경계 중이거나 8m 안에 온 것.
    /// - 판단(Core Danger): 보스·너무 센 적 → 도망(그 목적지는 버림), 체력 낮음 → 미드 → 없으면 물러남,
    ///   잃을 체력 ≤ 60%(알라라크 80%) → 가까운 적부터 싸움, 그 이상 → 온 길로 40m 물러나 30초 뒤 다시(2번 실패하면 그곳은 버림).
    /// - 처음 보는 적은 겉모습(크기·별)으로 짐작하고, 싸워 보면(맞거나 때리면) 게임 데이터의 실제 체력·공격력을 배운다(Brain.Foes).
    /// </summary>
    public sealed class Guard
    {
        public const float SenseRange = 30f, CloseRange = 8f, AllyRange = 20f, HelpRange = 60f, EngageRange = 15f;
        const float JudgeEvery = 0.5f, BackDistance = 40f, BackWait = 30f;
        public const int MaxBacks = 2;
        /// <summary>[기본안] 내가 한 번 휘두를 때 적이 치는 횟수(바닐라 몬스터의 공격 간격 반영, 시험으로 조정).</summary>
        public const float FoeHitsPerSwing = 0.5f;

        enum State { Calm, Fight, Back, Flee }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        State m_state;
        float m_judgeTimer;
        Character m_target;
        Vector3 m_backTo;
        float m_backTimer;
        int m_backs;
        bool m_saidFight;
        readonly List<Character> m_foes = new List<Character>();
        readonly List<Foe> m_views = new List<Foe>();

        public Guard(WorkerNpc npc, WorkerAI ai)
        {
            m_npc = npc;
            m_ai = ai;
        }

        /// <summary>시험·!상태용.</summary>
        public string StateName => m_state.ToString();
        public bool Fleeing => m_state == State.Flee;

        /// <summary>도망을 그만둔다(갇혀서 깃발로 순간이동했을 때).</summary>
        /// <summary>시험용: 적 없이 to로 도망을 시작한다(적이 없어도 도망은 끝까지 감).</summary>
        internal void TestFlee(Vector3 to)
        {
            m_backTo = to;
            m_target = null;
            m_fleeTime = 0f;
            m_state = State.Flee;
        }

        public void CancelFlee()
        {
            if (m_state != State.Flee) return;
            m_state = State.Calm;
            m_target = null;
        }
        public Character Target => m_target;
        public float LastRatio { get; private set; }
        public Verdict LastVerdict { get; private set; }
        /// <summary>시험 기록용: 마지막 판단에 쓴 수치.</summary>
        public string LastDetail { get; private set; } = "";

        /// <summary>새 일을 받으면 물러난 횟수를 다시 센다.</summary>
        public void Reset()
        {
            m_backs = 0;
            m_state = State.Calm;
            m_target = null;
        }

        /// <returns>true면 이번 프레임은 경계가 몸을 썼다(일·따라가기는 쉰다).</returns>
        public bool Update(float dt)
        {
            m_judgeTimer -= dt;
            if (m_judgeTimer <= 0f)
            {
                m_judgeTimer = JudgeEvery;
                Judge();
            }
            switch (m_state)
            {
                case State.Fight: return UpdateFight(dt);
                case State.Back: return UpdateBack(dt);
                case State.Flee: return UpdateFlee(dt);
            }
            return false;
        }

        // ---- 판단 ----

        void Judge()
        {
            Vector3 pos = m_npc.transform.position;
            float myHit = MyHit(pos);
            float armor = m_npc.GetBodyArmor();
            float scale = Game.instance.GetDifficultyDamageScalePlayer(pos) * Game.m_enemyDamageRate;
            System.Func<float, float> taken = d => HitData.DamageTypes.ApplyArmor(d * scale, armor);
            FindFoes(pos, myHit, taken);
            if (m_foes.Count == 0)
            {
                if (m_state == State.Fight) EndFight();
                m_saidFight = false;
                // 물러나 기다리는 중이거나 도망 중이면 마저 한다(0.3.7: 적이 30m 경계를 넘나들 때마다 도망이 취소됐다 다시 시작됨)
                if (m_state != State.Back && m_state != State.Flee) m_state = State.Calm;
                if (m_state == State.Calm) CloseEncounter();
                return;
            }
            Tally(pos);
            bool mead = m_npc.CanDrinkHealthMead();
            Verdict v = Danger.Judge(m_npc.Personality, m_views, m_npc.GetHealth(), m_npc.GetMaxHealth(), myHit, taken, mead);
            LastVerdict = v;
            LastRatio = Danger.Ratio(m_views, m_npc.GetHealth(), myHit, taken);
            LastDetail = $"내 한 방 {myHit:0.0}, 갑옷 {armor:0}, 배율 {scale:0.00}, 체력 {m_npc.GetHealth():0}/{m_npc.GetMaxHealth():0}, 적 [" + string.Join("; ", m_views.ConvertAll(f => $"{f.Kind} {f.Health:0}/{f.Damage:0.0}→{taken(f.Damage):0.0} {f.Distance:0}m{(Brain.Foes.Knows(f.Kind) ? "" : " 짐작")}").ToArray()) + "]";
            Character nearest = Nearest(pos);
            string foeName = Localization.instance.Localize(nearest.m_name);
            switch (v)
            {
                case Verdict.Flee:
                    if (m_state != State.Flee) StartFlee(pos, nearest, foeName);
                    else if (Chased(10f)) m_backTo = AwayPoint(pos, BackDistance * 1.5f); // 쫓아온다: 더 간다
                    break;
                case Verdict.Drink:
                    if (m_npc.DrinkHealthMead()) m_npc.Say("drink");
                    m_judgeTimer = 0f; // 마셨으니 바로 다시 판단(싸울지 물러날지)
                    break;
                case Verdict.Back:
                    if (m_state == State.Back)
                    {
                        // 물러났는데 쫓아온다: 다시 물러나고, 세 번째면 도망(그곳은 버린다)
                        if (!Chased(10f)) break;
                        if (++m_chased >= 3) { StartFlee(pos, nearest, foeName); break; }
                        m_backTo = AwayPoint(pos, BackDistance);
                        m_backTimer = BackWait;
                        m_backWalking = true;
                        m_backWalkTime = 0f;
                        break;
                    }
                    if (m_state == State.Flee) break;
                    m_chased = 0;
                    {
                        m_backs++;
                        Worse(Outcome.Back);
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 물러남 {m_backs}번째: {foeName} 등 {m_foes.Count}, 비율 {LastRatio:0.00} ({LastDetail})");
                        if (m_backs > MaxBacks)
                        {
                            m_npc.Say("dangerSkip", Args(foeName));
                            m_ai.AvoidHere(nearest.transform.position);
                            m_backs = 0;
                        }
                        else if (!CallHelp(pos, foeName)) m_npc.Say("backOff", Args(foeName));
                        m_backTo = AwayPoint(pos, BackDistance);
                        m_backTimer = BackWait;
                        m_backWalking = true;
                        m_backWalkTime = 0f;
                        m_state = State.Back;
                    }
                    break;
                case Verdict.Fight:
                    if (m_state == State.Back && m_backTimer > 0f && !Aggressive(nearest)) break; // 물러나 기다리는 중: 쫓아오지 않으면 그대로
                    Character t = FightTarget(pos);
                    if (t == null) break;
                    if (t != m_target) { DrinkResist(pos, t); m_missStreak = 0; m_diagSwings = 0; }
                    if (!m_saidFight) { m_saidFight = true; m_npc.Say("fightStart", Args(Localization.instance.Localize(t.m_name))); }
                    m_target = t;
                    m_state = State.Fight;
                    break;
            }
        }

        int m_chased;

        float m_helpAt = -999f;

        /// <summary>위급한데 근처(HelpRange)에 플레이어가 있으면 "도와줘요!!"+지도 핑(구상서 8-1). 60초에 한 번. 불렀으면 true.</summary>
        bool CallHelp(Vector3 pos, string foeName)
        {
            if (Time.time - m_helpAt < 60f) return false;
            bool near = false;
            foreach (var p in Player.GetAllPlayers())
                if (p != null && !p.IsDead() && !p.InGhostMode() && Vector3.Distance(p.transform.position, pos) <= HelpRange) { near = true; break; }
            if (!near) return false;
            m_helpAt = Time.time;
            m_npc.Say("help", Args(foeName));
            try { if (Chat.instance != null) Chat.instance.SendPing(pos); }
            catch (System.Exception e) { Plugin.Log.LogWarning("핑 실패: " + e.Message); }
            return true;
        }

        void StartFlee(Vector3 pos, Character nearest, string foeName)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 도망: {foeName} 등 {m_foes.Count}, 잃을 체력 비율 {LastRatio:0.00} ({LastDetail})");
            if (!CallHelp(pos, foeName)) m_npc.Say("flee", Args(foeName));
            Worse(Outcome.Fled);
            m_ai.AvoidHere(nearest.transform.position);
            m_backTo = AwayPoint(pos, BackDistance * 1.5f);
            m_target = null;
            m_armed = false;
            m_fleeTime = 0f;
            m_state = State.Flee;
        }

        /// <summary>싸울 생각인 적이 dist 안까지 따라왔다.</summary>
        bool Chased(float dist)
        {
            foreach (var c in m_foes)
                if (Aggressive(c) && Vector3.Distance(c.transform.position, m_npc.transform.position) < dist) return true;
            return false;
        }

        static Dictionary<string, string> Args(string foe) => new Dictionary<string, string> { ["foe"] = foe };

        /// <summary>
        /// 저항 미드(사용자 결정 2026-10-22): 싸우기 직전, 그 적이 주는 속성 피해(독·냉기·불)의 저항이 아직 없고 미드가 있으면 마신다.
        /// 적 피해 종류를 게임 데이터로 모르면 바이옴 기준.
        /// </summary>
        void DrinkResist(Vector3 pos, Character foe)
        {
            string kind = Resist.ToDrink(FoeElements(foe), ((Heightmap.Biome)Scout.BiomeAt(pos)).ToString(), m_npc.Resists, m_npc.HasResistMead);
            if (kind != null && m_npc.DrinkResistMead(kind)) m_npc.Say("resist");
        }

        static readonly Dictionary<string, Dictionary<string, float>> s_elements = new Dictionary<string, Dictionary<string, float>>();

        /// <summary>
        /// 적이 주는 속성 피해 종류와 그 한 방 크기: Humanoid의 공격 물건(m_defaultItems·m_randomWeapon·m_randomSets)의 m_damages 독·냉기·불(가장 큰 값).
        /// 공격 물건이 하나도 없으면(모름) null. 프리팹 이름별로 한 번만 센다.
        /// </summary>
        static Dictionary<string, float> FoeElementDamage(Character foe)
        {
            if (foe == null) return null;
            string key = Utils.GetPrefabName(foe.gameObject);
            if (s_elements.TryGetValue(key, out var cached)) return cached;
            Dictionary<string, float> kinds = null;
            if (foe is Humanoid h)
            {
                var items = new List<GameObject>();
                if (h.m_defaultItems != null) items.AddRange(h.m_defaultItems);
                if (h.m_randomWeapon != null) items.AddRange(h.m_randomWeapon);
                if (h.m_randomSets != null) foreach (var set in h.m_randomSets) if (set?.m_items != null) items.AddRange(set.m_items);
                foreach (var go in items)
                {
                    var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (drop == null) continue;
                    var shared = drop.m_itemData.m_shared;
                    if (shared.m_itemType == ItemDrop.ItemData.ItemType.Shield || shared.m_itemType == ItemDrop.ItemData.ItemType.Chest
                        || shared.m_itemType == ItemDrop.ItemData.ItemType.Legs || shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet
                        || shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder || shared.m_itemType == ItemDrop.ItemData.ItemType.Utility) continue;
                    if (kinds == null) kinds = new Dictionary<string, float>();
                    var d = shared.m_damages;
                    void Add(string k, float v) { if (v > 0f) kinds[k] = Mathf.Max(v, kinds.TryGetValue(k, out float o) ? o : 0f); }
                    Add("Poison", d.m_poison);
                    Add("Frost", d.m_frost);
                    Add("Fire", d.m_fire);
                }
            }
            s_elements[key] = kinds;
            Plugin.Log.LogInfo($"적 속성 피해 {key}: {(kinds == null ? "모름(바이옴 기준)" : kinds.Count == 0 ? "없음" : string.Join(",", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(kinds, p => p.Key + " " + p.Value.ToString("0"))))) }");
            return kinds;
        }

        static IEnumerable<string> FoeElements(Character foe) => FoeElementDamage(foe)?.Keys;

        /// <summary>
        /// 셈에 넣을 적: 30m 안의 적대 생물 중 나·동행을 노리거나 15m 안에 왔거나, 보스이거나, 혼자서도 못 이길 만큼 센 것(8장 a: 보이면 피함).
        /// 아직 나를 모르는 먼 잔챙이는 넣지 않는다(0.3.7: 거점 둘레를 어슬렁거리는 멧돼지·회색난쟁이 때문에 탐험을 못 함).
        /// </summary>
        void FindFoes(Vector3 pos, float myHit, System.Func<float, float> taken)
        {
            m_foes.Clear();
            m_views.Clear();
            var one = new List<Foe>(1);
            foreach (Character c in Character.GetAllCharacters())
            {
                if (!IsFoe(c) || Ignored(c) || Vector3.Distance(c.transform.position, pos) > SenseRange) continue;
                Foe view = View(c, pos);
                if (!c.IsBoss() && !Aggressive(c) && view.Distance > EngageRange)
                {
                    one.Clear();
                    one.Add(view);
                    if (Danger.Ratio(one, m_npc.GetMaxHealth(), myHit, taken) <= Danger.AvoidRatio) continue;
                }
                m_foes.Add(c);
                m_views.Add(view);
            }
        }

        bool IsFoe(Character c)
        {
            if (c == null || c == m_npc || c.IsDead() || c.IsPlayer() || c is WorkerNpc || c.IsTamed()) return false;
            if (c.GetFaction() == Character.Faction.AnimalsVeg) return false; // 멧돼지 등: 사냥은 ⑧
            if (c.GetBaseAI() is AnimalAI) return false; // 사슴·토끼: AnimalAI는 도망만 하고 공격 없음(사슴 진영이 AnimalsVeg가 아니라 도망치던 것)
            if (!BaseAI.IsEnemy(m_npc, c)) return false;
            BaseAI ai = c.GetBaseAI();
            return ai == null || !ai.IsSleeping();
        }

        /// <summary>사냥감으로 삼아도 되나(⑧): 그 한 마리만 놓고 보면 싸우는 쪽으로 판단될 때(체력 낮으면 안 됨).</summary>
        public bool SafePrey(Character c)
        {
            Vector3 pos = m_npc.transform.position;
            float armor = m_npc.GetBodyArmor();
            float scale = Game.instance.GetDifficultyDamageScalePlayer(pos) * Game.m_enemyDamageRate;
            var one = new List<Foe>(1) { View(c, pos) };
            return Danger.Judge(m_npc.Personality, one, m_npc.GetHealth(), m_npc.GetMaxHealth(), MyHit(pos),
                d => HitData.DamageTypes.ApplyArmor(d * scale, armor), false) == Verdict.Fight;
        }

        /// <summary>나·동행을 노리거나, 경계 중이거나, 가까이 왔거나, 방금 나를 때렸다.</summary>
        bool Aggressive(Character c)
        {
            if (c == null) return false;
            if (m_npc.HitBy(c, 10f)) return true;
            float d = Vector3.Distance(c.transform.position, m_npc.transform.position);
            if (d < CloseRange) return true;
            BaseAI ai = c.GetBaseAI();
            if (ai == null) return false;
            Character t = ai.GetTargetCreature();
            if (t == m_npc || t != null && (t.IsPlayer() || t is WorkerNpc) && Vector3.Distance(t.transform.position, m_npc.transform.position) < AllyRange) return true;
            return ai.IsAlerted() && d < AllyRange;
        }

        Character FightTarget(Vector3 pos)
        {
            Character best = null;
            float bestD = float.MaxValue;
            foreach (var c in m_foes)
            {
                float dc = Vector3.Distance(c.transform.position, pos);
                if (!Aggressive(c) && dc > EngageRange) continue; // 가까운 적은 못 알아챘어도 먼저 친다(0.3.7: 8.1m에서 서로 쳐다만 봄)
                if (dc < bestD) { bestD = dc; best = c; }
            }
            return best;
        }

        Character Nearest(Vector3 pos)
        {
            Character best = m_foes[0];
            foreach (var c in m_foes)
                if (Vector3.Distance(c.transform.position, pos) < Vector3.Distance(best.transform.position, pos)) best = c;
            return best;
        }

        /// <summary>적 하나의 판단용 수치: 체력은 보이는 값(체력바), 한 방은 아는 적이면 배운 값(별 레벨 반영), 처음 보는 적이면 몸집 짐작.</summary>
        Foe View(Character c, Vector3 pos)
        {
            string kind = Utils.GetPrefabName(c.gameObject);
            int level = c.GetLevel();
            float health = c.GetHealth();
            float damage = Brain.Foes.TryGet(kind, out _, out float baseDmg)
                ? baseDmg * (1f + Mathf.Max(0, level - 1) * 0.5f)
                : Danger.GuessDamage(c.GetHeight(), c.GetRadius(), level);
            // 저항 미드(사용자 결정 2026-10-22): 그 속성 저항이 있거나 미드를 들고 있으면(싸우기 직전에 마심) 속성 몫을 반(Resistant)으로 센다. 배운 적만.
            if (Brain.Foes.Knows(kind) && FoeElementDamage(c) is Dictionary<string, float> el)
                foreach (var e in el)
                    if (m_npc.Resists(e.Key) || m_npc.HasResistMead(e.Key))
                        damage = Mathf.Max(0f, damage - e.Value * 0.5f * (1f + Mathf.Max(0, level - 1) * 0.5f));
            // 판단식은 "내가 한 번 칠 때 맞는 피해"로 센다. 바닐라 몬스터는 공격 사이 쉬는 시간이 있어 대략 내가 두 번 칠 때 한 번 친다.
            damage *= FoeHitsPerSwing;
            return new Foe { Kind = kind, Health = health, Damage = damage, Boss = c.IsBoss(), Distance = Vector3.Distance(c.transform.position, pos) };
        }

        /// <summary>내 한 방(가장 좋은 무기, 없으면 맨손) + 20m 안 플레이어들의 한 방(같이 싸운다).</summary>
        float MyHit(Vector3 pos)
        {
            float hit = HitOf(m_npc) / 0.9f; // 아래에서 플레이어 몫과 같이 0.9를 곱한다
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null || p.IsDead() || p.InGhostMode() || p.InDebugFlyMode() || Vector3.Distance(p.transform.position, pos) > AllyRange) continue; // 유령·날기 모드는 싸우지 않는다
                ItemDrop.ItemData pw = p.GetCurrentWeapon();
                hit += pw != null ? Gear.WeaponScore(pw) : 5f;
            }
            return hit * 0.9f; // 바닐라 무작위 피해 계수(0.75~1의 제곱근) 평균쯤
        }

        // ---- 행동 ----

        bool m_armed;
        float m_fightIdle;
        float m_diagTimer;
        int m_diagSwings;
        readonly Dictionary<Character, float> m_ignore = new Dictionary<Character, float>();
        const float FightIdleLimit = 20f;

        bool UpdateFight(float dt)
        {
            if (m_target == null || m_target.IsDead() || !IsFoe(m_target) || Ignored(m_target))
            {
                if (m_target != null) Learn(m_target);
                EndFight();
                m_judgeTimer = 0f; // 다음 적을 바로 고른다
                return true;
            }
            if (!m_armed || m_npc.HoldingBow()) { m_armed = true; m_fightIdle = 0f; m_npc.StopDraw(); m_npc.HoldWeapon(); } // 없으면 맨손으로. 활은 사냥에만
            ItemDrop.ItemData weapon = m_npc.GetCurrentWeapon();
            float range = weapon != null ? Mathf.Clamp(weapon.m_shared.m_attack.m_attackRange, 1.5f, 3f) : 1.5f;
            Vector3 tp = m_target.transform.position;
            float d = Utils.DistanceXZ(tp, m_npc.transform.position) - m_target.GetRadius();
            m_fightIdle += dt;
            if (m_npc.HitBy(m_target, 1f)) m_fightIdle = 0f;
            if (d > range * 0.9f) m_ai.MoveToPoint(dt, tp, range * 0.8f + m_target.GetRadius(), true);
            else
            {
                m_ai.StopMoving();
                m_ai.Face(m_target.GetCenterPoint());
                if (!m_npc.InAttack() && m_ai.IsLookingAt(tp, 25f) && m_npc.StartAttack(m_target, false))
                {
                    m_fightIdle = 0f;
                    m_diagSwings++;
                    Learn(m_target);
                    // 앞 휘두름이 안 맞았나(적 체력 그대로). 적이 바위 위·턱 위에 있으면 수평 휘두름이 계속 빗나간다
                    // (회차 72 coppertest: 회색 난쟁이가 2.4m 높은 광맥 위, 7번 휘둘러 0번 맞고 일꾼만 죽음) → 3번 잇달아 빗나가면 직접 피해.
                    float hp = m_target.GetHealth();
                    m_missStreak = m_diagSwings > 1 && hp >= m_lastFoeHealth ? m_missStreak + 1 : 0;
                    m_lastFoeHealth = hp;
                    if (m_missStreak >= 3 && Mathf.Abs(tp.y - m_npc.transform.position.y) < 3f) DirectHit(weapon);
                }
            }
            // 간헐 목록(회차 66 hunttest): 싸움이 오래 안 끝나면 닿지 못하는지·때리는데 안 맞는지 가른다
            m_diagTimer += dt;
            if (m_diagTimer > 5f)
            {
                m_diagTimer = 0f;
                Plugin.Log.LogInfo($"DIAG 싸움 {m_npc.NpcName}→{m_target.m_name}: 거리 {d:0.0}/{range:0.0} 휘두름 {m_diagSwings} 적 체력 {m_target.GetHealth():0}/{m_target.GetMaxHealth():0} 공격 중 {m_npc.InAttack()} 봄 {m_ai.IsLookingAt(tp, 25f)} 높이차 {tp.y - m_npc.transform.position.y:0.0} 물 {m_target.InWater()} 쉼 {m_fightIdle:0}");
            }
            // 닿지 않는 곳(절벽 위·물 건너)의 적: 20초 동안 서로 못 때리면 1분 동안 무시한다
            if (m_fightIdle > FightIdleLimit)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_target.m_name}에 닿지 않아 잠시 무시");
                m_ignore[m_target] = Time.time + 60f;
                EndFight();
            }
            return true;
        }

        int m_missStreak;
        float m_lastFoeHealth;

        /// <summary>바닐라 Attack 판정이 못 닿을 때 무기 피해를 직접 준다(GatherJob.DirectHit와 같은 방식, 스킬 반영).</summary>
        void DirectHit(ItemDrop.ItemData weapon)
        {
            if (weapon == null) return;
            Vector3 me = m_npc.GetCenterPoint();
            Vector3 point = m_target.GetCenterPoint();
            var hit = new HitData
            {
                m_damage = weapon.GetDamage(),
                m_point = point,
                m_dir = (point - me).normalized,
                m_skill = weapon.m_shared.m_skillType,
                m_pushForce = weapon.m_shared.m_attackForce,
            };
            hit.m_damage.Modify(m_npc.GetRandomSkillFactor(weapon.m_shared.m_skillType));
            hit.SetAttacker(m_npc);
            m_target.Damage(hit);
            m_npc.RaiseSkill(weapon.m_shared.m_skillType);
            Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_target.m_name}에 휘두름이 {m_missStreak}번 빗나가 직접 피해 {hit.GetTotalDamage():0}");
        }

        void EndFight()
        {
            m_missStreak = 0;
            m_lastFoeHealth = 0f;
            m_target = null;
            m_armed = false;
            m_diagSwings = 0;
            m_state = State.Calm;
        }

        bool Ignored(Character c) => m_ignore.TryGetValue(c, out float until) && Time.time < until;

        bool m_backWalking;
        float m_backWalkTime;

        bool UpdateBack(float dt)
        {
            if (m_backWalking)
            {
                m_backWalkTime += dt;
                bool there = Utils.DistanceXZ(m_backTo, m_npc.transform.position) < 3f;
                if (!there && m_backWalkTime < 30f && m_ai.WaterBlockedTime <= WorkerAI.WaterGiveUp)
                {
                    m_ai.MoveToPoint(dt, m_backTo, 2f, true);
                    return true;
                }
                m_backWalking = false; // 다 왔거나 막혔다: 여기서 기다린다
            }
            m_ai.StopMoving();
            m_backTimer -= dt;
            if (m_backTimer <= 0f) { m_state = State.Calm; return false; } // 다시 해 본다
            return true;
        }

        float m_fleeTime;

        /// <summary>
        /// 도망은 목적지(60m)에 닿거나 40초가 지나거나 물에 막힐 때까지 한다. 적이 감지 거리(30m) 밖으로 잠깐 나갔다고 멈추면
        /// 경계에서 멈췄다 도망쳤다를 되풀이한다(0.3.7 시험: 트롤 30m 언저리에서 맴돎).
        /// </summary>
        bool UpdateFlee(float dt)
        {
            m_fleeTime += dt;
            bool there = m_ai.MoveToPoint(dt, m_backTo, 2f, true) || Utils.DistanceXZ(m_backTo, m_npc.transform.position) < 3f;
            if (there || m_fleeTime > 40f || m_ai.WaterBlockedTime > WorkerAI.WaterGiveUp)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 도망 끝: {(there ? "도착" : m_fleeTime > 40f ? "40초" : "물에 막힘")}");
                m_ai.StopMoving();
                m_state = State.Calm;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 물러날 곳: 거점이 멀고 적 반대쪽이면 거점 쪽(온 길)으로 dist만큼(거점이 더 가까우면 거점), 거점 근처이거나
        /// 거점 쪽에 적이 있으면 적 반대쪽으로. (0.3.7: 거점에 있는데 "거점으로 도망"이라 제자리에서 되풀이했다.)
        /// </summary>
        Vector3 AwayPoint(Vector3 pos, float dist)
        {
            if (Character.InInterior(pos))
            {
                // 지하묘지 안: 거점 쪽/적 반대쪽이 아니라 나가는 Teleport 쪽으로(안은 y+5000이라 거점 방향이 뜻 없음)
                Teleport exit = Dungeons.Exit(pos);
                if (exit != null) return exit.transform.position;
            }
            Vector3 toHome = m_npc.Home - pos;
            toHome.y = 0f;
            Vector3 fromFoes = Vector3.zero;
            foreach (var c in m_foes) { Vector3 v = pos - c.transform.position; v.y = 0f; fromFoes += v.normalized; }
            if (fromFoes.sqrMagnitude < 0.01f) fromFoes = -m_npc.transform.forward;
            fromFoes.Normalize();
            bool homeFar = toHome.magnitude > 10f;
            if (homeFar && Vector3.Dot(toHome.normalized, fromFoes) > -0.3f)
                return toHome.magnitude < dist ? m_npc.Home : pos + toHome.normalized * dist;
            return pos + fromFoes * dist;
        }

        /// <summary>싸워 본 적: 게임 데이터의 실제 기본 체력·가장 센 한 방을 배운다(두 NPC가 같이 쓴다).</summary>
        public static void Learn(Character c)
        {
            if (c == null) return;
            string kind = Utils.GetPrefabName(c.gameObject);
            if (Brain.Foes.Knows(kind)) return;
            float dmg = 0f;
            if (c is Humanoid h)
            {
                foreach (var go in Concat(h.m_defaultItems, h.m_randomWeapon))
                {
                    ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (drop != null) dmg = Mathf.Max(dmg, Gear.WeaponScore(drop.m_itemData));
                }
                ItemDrop.ItemData cur = h.GetCurrentWeapon();
                if (cur != null) dmg = Mathf.Max(dmg, Gear.WeaponScore(cur));
            }
            Brain.Foes.Learn(kind, c.m_health, dmg);
            Plugin.Log.LogInfo($"적 배움: {kind} 체력 {c.m_health:0}, 한 방 {dmg:0}");
        }

        static IEnumerable<GameObject> Concat(GameObject[] a, GameObject[] b)
        {
            if (a != null) foreach (var x in a) yield return x;
            if (b != null) foreach (var x in b) yield return x;
        }

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
        /// 장소 기억의 적 한 종류(레벨 포함) → 판단용 수치. 배운 적이면 배운 값, 아니면 프리팹의 기본 체력과 몸집 짐작.
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
                dmg = Danger.GuessDamage(col != null ? col.height : 1.8f, col != null ? col.radius : 0.4f, 1);
            }
            int level = Mathf.Max(1, g.Level);
            return new Foe { Kind = g.Kind, Health = hp * level, Damage = dmg * (1f + (level - 1) * 0.5f) * FoeHitsPerSwing, Boss = g.Boss };
        }

        /// <summary>출발 전 판단용 내 한 방(가장 좋은 무기, 없으면 맨손; 플레이어는 빼고).</summary>
        public static float HitOf(WorkerNpc npc)
        {
            ItemDrop.ItemData w = Gear.Best(npc.GetInventory().GetAllItems(), Gear.IsMelee, Gear.WeaponScore);
            // 스킬 반영(3-1장): 평균 피해 계수(스킬 10이면 0.46, 70이면 0.82)
            return (w != null ? Gear.WeaponScore(w) * npc.MeanSkillFactor(w.m_shared.m_skillType) : 5f * npc.MeanSkillFactor(Skills.SkillType.Unarmed));
        }

        /// <summary>출발 전 판단용: 적의 한 방이 내 갑옷을 거쳐 들어오는 피해(거점 기준 난이도 배율).</summary>
        public static System.Func<float, float> TakenFor(WorkerNpc npc)
        {
            float armor = npc.GetBodyArmor();
            float scale = Game.instance.GetDifficultyDamageScalePlayer(npc.Home) * Game.m_enemyDamageRate;
            return d => HitData.DamageTypes.ApplyArmor(d * scale, armor);
        }
    }
}
