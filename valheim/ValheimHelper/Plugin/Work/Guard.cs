using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;

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
        public const float SenseRange = 30f, CloseRange = 8f, AllyRange = 20f, EngageRange = 15f;
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
        public Character Target => m_target;
        public float LastRatio { get; private set; }
        public Verdict LastVerdict { get; private set; }

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
            FindFoes(pos);
            if (m_foes.Count == 0)
            {
                if (m_state == State.Fight) EndFight();
                m_saidFight = false;
                if (m_state != State.Back) m_state = State.Calm; // 물러난 뒤 기다리는 중이면 마저 기다린다
                return;
            }
            float myHit = MyHit(pos);
            float armor = m_npc.GetBodyArmor();
            float scale = Game.instance.GetDifficultyDamageScalePlayer(pos) * Game.m_enemyDamageRate;
            System.Func<float, float> taken = d => HitData.DamageTypes.ApplyArmor(d * scale, armor);
            bool mead = m_npc.CanDrinkHealthMead();
            Verdict v = Danger.Judge(m_npc.Personality, m_views, m_npc.GetHealth(), m_npc.GetMaxHealth(), myHit, taken, mead);
            LastVerdict = v;
            LastRatio = Danger.Ratio(m_views, m_npc.GetHealth(), myHit, taken);
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
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 물러남 {m_backs}번째: {foeName} 등 {m_foes.Count}, 비율 {LastRatio:0.00}");
                        if (m_backs > MaxBacks)
                        {
                            m_npc.Say("dangerSkip", Args(foeName));
                            m_ai.AvoidHere(nearest.transform.position);
                            m_backs = 0;
                        }
                        else m_npc.Say("backOff", Args(foeName));
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
                    if (!m_saidFight) { m_saidFight = true; m_npc.Say("fightStart", Args(Localization.instance.Localize(t.m_name))); }
                    m_target = t;
                    m_state = State.Fight;
                    break;
            }
        }

        int m_chased;

        void StartFlee(Vector3 pos, Character nearest, string foeName)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 도망: {foeName} 등 {m_foes.Count}, 잃을 체력 비율 {LastRatio:0.00}");
            m_npc.Say("flee", Args(foeName));
            m_ai.AvoidHere(nearest.transform.position);
            m_backTo = AwayPoint(pos, BackDistance * 1.5f);
            m_target = null;
            m_armed = false;
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

        void FindFoes(Vector3 pos)
        {
            m_foes.Clear();
            m_views.Clear();
            foreach (Character c in Character.GetAllCharacters())
            {
                if (!IsFoe(c) || Ignored(c) || Vector3.Distance(c.transform.position, pos) > SenseRange) continue;
                m_foes.Add(c);
                m_views.Add(View(c, pos));
            }
        }

        bool IsFoe(Character c)
        {
            if (c == null || c == m_npc || c.IsDead() || c.IsPlayer() || c is WorkerNpc || c.IsTamed()) return false;
            if (c.GetFaction() == Character.Faction.AnimalsVeg) return false; // 사슴·멧돼지 등: 사냥은 ⑧
            if (!BaseAI.IsEnemy(m_npc, c)) return false;
            BaseAI ai = c.GetBaseAI();
            return ai == null || !ai.IsSleeping();
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

        /// <summary>적 하나의 판단용 수치: 아는 적은 배운 값(별 레벨 반영), 처음 보는 적은 겉모습 짐작.</summary>
        Foe View(Character c, Vector3 pos)
        {
            string kind = Utils.GetPrefabName(c.gameObject);
            int level = c.GetLevel();
            float health, damage;
            if (Brain.Foes.TryGet(kind, out float baseHp, out float baseDmg))
            {
                health = c.GetHealth();
                damage = baseDmg * (1f + Mathf.Max(0, level - 1) * 0.5f);
            }
            else
            {
                Danger.Guess(c.GetHeight(), c.GetRadius(), level, out health, out damage);
                health *= c.GetHealthPercentage();
            }
            // 판단식은 "내가 한 번 칠 때 맞는 피해"로 센다. 바닐라 몬스터는 공격 사이 쉬는 시간이 있어 대략 내가 두 번 칠 때 한 번 친다.
            damage *= FoeHitsPerSwing;
            return new Foe { Kind = kind, Health = health, Damage = damage, Boss = c.IsBoss(), Distance = Vector3.Distance(c.transform.position, pos) };
        }

        /// <summary>내 한 방(가장 좋은 무기, 없으면 맨손) + 20m 안 플레이어들의 한 방(같이 싸운다).</summary>
        float MyHit(Vector3 pos)
        {
            ItemDrop.ItemData w = Gear.Best(m_npc.GetInventory().GetAllItems(), Gear.IsMelee, Gear.WeaponScore);
            float hit = w != null ? Gear.WeaponScore(w) : 5f;
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
            if (!m_armed) { m_armed = true; m_fightIdle = 0f; m_npc.HoldWeapon(); } // 없으면 맨손으로
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
                if (!m_npc.InAttack() && m_ai.IsLookingAt(tp, 25f) && m_npc.StartAttack(m_target, false)) { m_fightIdle = 0f; Learn(m_target); }
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

        void EndFight()
        {
            m_target = null;
            m_armed = false;
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

        bool UpdateFlee(float dt)
        {
            if (Utils.DistanceXZ(m_backTo, m_npc.transform.position) < 3f || m_foes.Count == 0)
            {
                m_state = State.Calm;
                return false;
            }
            m_ai.MoveToPoint(dt, m_backTo, 2f, true);
            return true;
        }

        /// <summary>온 길(거점 쪽)으로 물러날 곳. 적이 거점 쪽에 있으면 적 반대쪽.</summary>
        /// <summary>
        /// 물러날 곳: 거점이 멀고 적 반대쪽이면 거점 쪽(온 길)으로 dist만큼(거점이 더 가까우면 거점), 거점 근처이거나
        /// 거점 쪽에 적이 있으면 적 반대쪽으로. (0.3.7: 거점에 있는데 "거점으로 도망"이라 제자리에서 되풀이했다.)
        /// </summary>
        Vector3 AwayPoint(Vector3 pos, float dist)
        {
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
    }
}
