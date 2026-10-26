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
    ///   잃을 체력 ≤ 60%(플로키 80%) → 가까운 적부터 싸움, 그 이상 → 온 길로 40m 물러나 30초 뒤 다시(2번 실패하면 그곳은 버림).
    /// - 처음 보는 적은 겉모습(크기·별)으로 짐작하고, 싸워 보면(맞거나 때리면) 게임 데이터의 실제 체력·공격력을 배운다(Brain.Foes).
    /// </summary>
    public sealed class Guard
    {
        public const float SenseRange = 30f, CloseRange = 8f, AllyRange = 20f, HelpRange = 60f, EngageRange = 15f;
        const float JudgeEvery = 0.5f, BackDistance = 40f, BackWait = 30f;
        public const int MaxBacks = 2;
        /// <summary>동료 부르기(사용자 결정 2026-10-24 나): 이 거리 안 동료를 부르고, 20m 안에 올 때까지 최대 MateWait초 기다린다.</summary>
        /// <summary>동료 부르기: 일꾼끼리는 서로 일을 끊고 돕지 않는다 — 50m 안만(사용자 결정 2026-10-26, 전 100m).</summary>
        public const float MateCallRange = 50f, MateWait = 30f, MateCallEvery = 60f, ComeLimit = 40f;
        /// <summary>[기본안] 내가 한 번 휘두를 때 적이 치는 횟수(바닐라 몬스터의 공격 간격 반영, 시험으로 조정).</summary>
        public const float FoeHitsPerSwing = 0.5f;

        enum State { Calm, Fight, Back, Flee, Come }

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
        /// <summary>싸우거나 물러나거나 도망치는 중(시험이 이 동안 시간을 멈춤).</summary>
        public bool Engaged => m_state != State.Calm;
        /// <summary>시험용: 불러 놓고 동료를 기다리는 중.</summary>
        public bool WaitingMate => m_waitMate != null;

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
        /// <summary>시험용: 동료 부르기 60초 쉬는 시간을 없앤다.</summary>
        internal void TestForgetMateCall() { m_mateAt = -999f; m_waitMate = null; }
        public float LastRatio { get; private set; }
        public Verdict LastVerdict { get; private set; }
        /// <summary>시험 기록용: 마지막 판단에 쓴 수치.</summary>
        public string LastDetail { get; private set; } = "";

        /// <summary>
        /// 죽어서 거점에서 다시 일어났다(WorkerAI.OnDied): 싸움·물러나기·도망을 끝낸다. 안 그러면 죽기 전에 고른 물러날 곳·도망 목적지
        /// (죽은 곳 둘레)로 거점에서 다시 걸어간다(회차 141 검은숲 vh_hoptest: 도망 하나가 죽음 4~10번을 넘어 이어지다 "도망 끝: 40초").
        /// </summary>
        public void OnDied()
        {
            EndFight();
            m_backWalking = false;
            m_backs = 0;
            m_aroundSide = 0;
            m_waitMate = null;
            m_caller = null;
        }

        /// <summary>새 일을 받으면 물러난 횟수를 다시 센다.</summary>
        public void Reset()
        {
            m_backs = 0;
            m_state = State.Calm;
            m_target = null;
            m_waitMate = null;
            m_caller = null;
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
                case State.Come: return UpdateCome(dt);
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
            Shield? shield = m_npc.BlockStats();
            System.Func<float, float> taken = Taken(scale, armor, shield, 1);
            FindFoes(pos, myHit, taken);
            if (m_foes.Count == 0)
            {
                if (m_state == State.Fight) EndFight();
                m_saidFight = false;
                // 물러나 기다리는 중이거나 도망 중이면 마저 한다(0.3.7: 적이 30m 경계를 넘나들 때마다 도망이 취소됐다 다시 시작됨)
                if (m_state != State.Back && m_state != State.Flee && m_state != State.Come) m_state = State.Calm;
                if (m_state == State.Calm) { CloseEncounter(); m_aroundSide = 0; }
                return;
            }
            Tally(pos);
            taken = Taken(scale, armor, shield, m_views.Count); // 막기는 적 수에 따라(Block.MaxFoes)
            bool mead = m_npc.CanDrinkHealthMead();
            Verdict v = Danger.Judge(m_npc.Personality, m_views, m_npc.GetHealth(), m_npc.GetMaxHealth(), myHit, taken, mead);
            Character nearest = Nearest(pos);
            // 나보다 빠른 적이 덤벼 오면 물러나도 등 뒤로 맞기만 한다 → 물러남 대신 싸움(도망 판단은 그대로).
            // (⑪ 5 기본안 10-24. 회차 166 vh_resisttest에서 철 갑옷 라그나가 늑대에게 한순간에 죽은 건 물러남 탓이 아니라 일꾼이 받던 몬스터 뒤치기 4배 — NpcPatches.LikePlayerHit로 고침)
            if (v == Verdict.Back && Aggressive(nearest) && ChaseSpeed(nearest) > m_npc.m_runSpeed * 1.05f)
            {
                if (m_state != State.Fight) Plugin.Log.LogInfo($"{m_npc.NpcName} 물러나도 못 떨침({Utils.GetPrefabName(nearest.gameObject)} 달리기 {ChaseSpeed(nearest):0.0} > 나 {m_npc.m_runSpeed:0.0}) → 싸움");
                v = Verdict.Fight;
            }
            LastVerdict = v;
            LastRatio = Danger.Ratio(m_views, m_npc.GetHealth(), myHit, taken);
            LastDetail = $"내 한 방 {myHit:0.0}, 갑옷 {armor:0}{(shield != null ? $", 막기 {shield.Value.Power:0}" : "")}, 배율 {scale:0.00}, 체력 {m_npc.GetHealth():0}/{m_npc.GetMaxHealth():0}, 적 [" + string.Join("; ", m_views.ConvertAll(f => $"{f.Kind} {f.Health:0}/{f.Damage:0.0}→{taken(f.Damage):0.0} {f.Distance:0}m{(Brain.Foes.Knows(f.Kind) ? "" : " 짐작")}").ToArray()) + "]";
            string foeName = Localization.instance.Localize(nearest.m_name);
            // 쫓아오는 속성 적에게서 물러나거나 도망칠 때도 그 저항 미드를 마신다(⑪ 5 기본안 10-24: 드레이크 냉기 침은 날아서 못 때리고 도망치는 동안 맞음)
            if ((v == Verdict.Flee || v == Verdict.Back) && Aggressive(nearest)) DrinkResist(pos, nearest);
            if (TryCallMate(pos, v, myHit, taken, mead, foeName)) return;
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
                        if (m_waitMate != null && !m_waitMate.IsDead())
                        {
                            // 부른 동료를 기다리다 쫓긴다: 동료 쪽으로 물러난다(회차 147 vh_matetest: 멀리 물러나다 세 번째에 도망 → 온 동료가 혼자 싸움)
                            m_backTo = m_waitMate.transform.position;
                            m_backWalking = true;
                            m_backWalkTime = 0f;
                            break;
                        }
                        if (++m_chased >= 3) { StartFlee(pos, nearest, foeName); break; }
                        m_backTo = AwayPoint(pos, BackDistance);
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 쫓겨 다시 물러남 {m_chased}번째 → {m_lastAway}");
                        m_backTimer = BackWait;
                        m_backWalking = true;
                        m_backWalkTime = 0f;
                        break;
                    }
                    if (m_state == State.Flee || ComingAlone()) break;
                    m_chased = 0;
                    {
                        m_backs++;
                        Worse(Outcome.Back);
                        m_backTo = AwayPoint(pos, BackDistance);
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 물러남 {m_backs}번째: {foeName} 등 {m_foes.Count}, 비율 {LastRatio:0.00} ({LastDetail}) → {m_lastAway}");
                        if (m_backs > MaxBacks)
                        {
                            m_npc.Say("dangerSkip", Args(foeName));
                            m_ai.AvoidHere(nearest.transform.position);
                            m_backs = 0;
                        }
                        else if (!CallHelp(pos, foeName)) m_npc.Say("backOff", Args(foeName));
                        m_backTimer = BackWait;
                        m_backWalking = true;
                        m_backWalkTime = 0f;
                        m_state = State.Back;
                    }
                    break;
                case Verdict.Fight:
                    // 도망은 끝까지 한다(도착·40초·물 막힘 — 구상서 8장 a "그 지역을 벗어남", 물러남 판단·적 없음도 도망을 안 끊음).
                    // (늪 회귀 basetest: 30m 경계를 넘나드는 회색난쟁이 주술사 때문에 판단이 도망↔싸움으로 바뀌어, 싸우러 무리 쪽으로 갔다가
                    // 다시 도망을 수십 초마다 새로 시작 — 그때마다 목표·구역을 버리고 장소 기억에 도망을 더 쌓음.)
                    if (m_state == State.Flee || ComingAlone()) break;
                    // 물러나 기다리는 중: 쫓아오지 않으면 그대로(부른 동료가 와서 싸움이 된 것이면 싸운다)
                    if (m_state == State.Back && m_backTimer > 0f && !Aggressive(nearest) && m_waitMate == null) break;
                    Character t = FightTarget(pos);
                    if (t == null) break;
                    // 기다리는 동료가 아직 곁(AllyRange)에 없으면 판단이 잠깐 싸움으로 흔들려도 바로 덤비는 적이 없는 한 마저 기다린다
                    // (회차 147 vh_matetest C: 발 묶인 동료 63m인데 1초 만에 "옴 → 같이 싸움"으로 기다림을 접음)
                    if (m_waitMate != null && m_state == State.Back && !m_waitMate.IsDead()
                        && Vector3.Distance(m_waitMate.transform.position, pos) > AllyRange && !Chased(CloseRange)) break;
                    if (m_waitMate != null) { Plugin.Log.LogInfo($"{m_npc.NpcName} 동료 {m_waitMate.NpcName} 옴 → 같이 싸움"); m_waitMate = null; }
                    if (t != m_target) { DrinkResist(pos, t); m_missStreak = 0; m_diagSwings = 0; }
                    if (!m_saidFight) { m_saidFight = true; m_npc.Say("fightStart", Args(Localization.instance.Localize(t.m_name))); }
                    m_target = t;
                    m_state = State.Fight;
                    break;
            }
        }

        int m_chased;

        // ---- 동료 부르기(사용자 결정 2026-10-24 "더 공격적으로" 나) ----

        float m_mateAt = -999f;
        /// <summary>불러 놓고 물러나 기다리는 동료(오면 싸움, 못 오면 MateWait 뒤 원래 판단).</summary>
        WorkerNpc m_waitMate;
        /// <summary>나를 부른 동료(State.Come 동안 그쪽으로 달려감).</summary>
        WorkerNpc m_caller;
        float m_comeTime;
        /// <summary>마지막 동료 부르기(시험용): 부른 쪽·온 쪽 이름과 Wait/Join.</summary>
        public static string LastMateCall { get; internal set; } = "";

        /// <summary>
        /// 20m 안에 동료가 없고, 부를 수 있는 동료(MateCallRange 안·쉬거나 일하는 중 — 싸움·물러남·도망·오는 중·죽음·순간이동 아님)가 있으면
        /// 그 한 방을 합쳐 다시 판단해 Danger.ShouldCallMate대로 부른다. Wait이면 물러나 기다리고 true(이번 판단은 끝), Join이면 부르고 false(그대로 싸움).
        /// </summary>
        bool TryCallMate(Vector3 pos, Verdict alone, float myHit, System.Func<float, float> taken, bool mead, string foeName)
        {
            if (m_state == State.Flee || m_state == State.Come || m_waitMate != null || Time.time - m_mateAt < MateCallEvery) return false;
            if (m_npc.IsGuard) return false; // 호위병은 일꾼을 부르지 않는다(일꾼의 일을 끊지 않게)
            if (alone != Verdict.Fight && alone != Verdict.Back && alone != Verdict.Flee) return false;
            WorkerNpc mate = null;
            float best = float.MaxValue;
            foreach (var n in WorkerNpc.All)
            {
                if (n == null || n == m_npc || n.IsDead() || n.Ai == null) continue;
                float d = Vector3.Distance(n.transform.position, pos);
                if (d <= AllyRange) return false; // 이미 곁에 있다(MyHit에 들어 있음)
                if (d > MateCallRange || n.IsTeleporting() || (n.Ai.Guard != null && n.Ai.Guard.Engaged)) continue;
                if (d < best) { best = d; mate = n; }
            }
            if (mate == null) return false;
            Verdict together = Danger.Judge(m_npc.Personality, m_views, m_npc.GetHealth(), m_npc.GetMaxHealth(), myHit + HitOf(mate) * 0.9f, taken, mead);
            MateCall call = Danger.ShouldCallMate(alone, together, LastRatio, m_views.Count);
            if (call == MateCall.None) return false;
            // 동료 쪽에서 봐도(그 체력·갑옷) 같이면 싸움이어야 부른다(회차 147 vh_fighttest: 맨몸 체력 25 동료를 불러, 와서 혼자 물러나고 부른 쪽이 죽음)
            float scale = Game.instance.GetDifficultyDamageScalePlayer(pos) * Game.m_enemyDamageRate;
            Verdict theirs = Danger.Judge(mate.Personality, m_views, mate.GetHealth(), mate.GetMaxHealth(), HitOf(mate) + myHit * 0.9f,
                Taken(scale, mate.GetBodyArmor(), mate.BlockStats(), m_views.Count), mate.CanDrinkHealthMead());
            if (theirs != Verdict.Fight)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 동료 안 부름: {mate.NpcName} 쪽 판단 {theirs} (체력 {mate.GetHealth():0}/{mate.GetMaxHealth():0}, 갑옷 {mate.GetBodyArmor():0})");
                m_mateAt = Time.time; // 60초 동안 다시 셈하지 않음
                return false;
            }
            if (mate.Ai.Guard == null || !mate.Ai.Guard.Summon(m_npc)) return false;
            m_mateAt = Time.time;
            LastMateCall = $"{m_npc.NpcName}→{mate.NpcName} {call}";
            Plugin.Log.LogInfo($"{m_npc.NpcName} 동료 부름({call}): {mate.NpcName} {best:0}m, 혼자 {alone} 비율 {LastRatio:0.00} → 같이 {together}, {foeName} 등 {m_foes.Count}");
            m_npc.Say("callMate", Args(foeName));
            if (call == MateCall.Join) return false;
            m_waitMate = mate;
            m_target = null;
            m_backTo = AwayPoint(pos, BackDistance);
            m_backTimer = MateWait;
            m_backWalking = true;
            m_backWalkTime = 0f;
            m_chased = 0;
            m_state = State.Back;
            return true;
        }

        /// <summary>동료가 불렀다: 하던 일은 그대로 두고(경계가 일보다 먼저) 그쪽으로 달려가 같은 적과 싸운다. 바쁘면 false.</summary>
        public bool Summon(WorkerNpc caller)
        {
            if (m_state != State.Calm || m_npc.IsDead()) return false;
            m_caller = caller;
            m_comeTime = 0f;
            m_state = State.Come;
            m_judgeTimer = JudgeEvery;
            m_npc.Say("mateComing", new Dictionary<string, string> { ["name"] = caller.NpcName });
            return true;
        }

        /// <summary>
        /// 부름을 받고 가는 중인데 아직 부른 쪽 곁(AllyRange)이 아니고 바로 덤비는 적(CloseRange)도 없다: 혼자 판단으로 싸우거나 물러나지 않고 마저 간다
        /// (회차 147 vh_matetest: 온 동료가 혼자 싸움 판단으로 먼저 덤벼 체력 71→26).
        /// </summary>
        bool ComingAlone()
        {
            if (m_state != State.Come || m_caller == null || m_caller.IsDead()) return false;
            return Vector3.Distance(m_caller.transform.position, m_npc.transform.position) > AllyRange && !Chased(CloseRange);
        }

        bool UpdateCome(float dt)
        {
            m_comeTime += dt;
            WorkerNpc c = m_caller;
            float d = c != null && !c.IsDead() ? Vector3.Distance(c.transform.position, m_npc.transform.position) : -1f;
            // 부른 쪽이 죽었거나 없어졌거나, 다 왔는데 적이 없거나, 너무 오래 걸리면 하던 일로
            if (d < 0f || m_comeTime > ComeLimit || (d < 8f && m_foes.Count == 0))
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 동료에게 가기 끝: {(d < 0f ? "부른 쪽 없음" : m_comeTime > ComeLimit ? "시간" : "적 없음")}");
                m_ai.StopMoving();
                m_caller = null;
                m_state = State.Calm;
                return false;
            }
            if (d < 4f) m_ai.StopMoving();
            else m_ai.MoveToPoint(dt, c.transform.position, 3f, true);
            return true;
        }

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
            m_backTo = AwayPoint(pos, BackDistance * 1.5f);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 도망: {foeName} 등 {m_foes.Count}, 잃을 체력 비율 {LastRatio:0.00} ({LastDetail}) → {m_lastAway}");
            if (!CallHelp(pos, foeName)) m_npc.Say("flee", Args(foeName));
            Worse(Outcome.Fled);
            m_ai.AvoidHere(nearest.transform.position);
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
                foreach (var it in AttackItems(h))
                {
                    if (kinds == null) kinds = new Dictionary<string, float>();
                    var d = it.m_shared.m_damages;
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

        /// <summary>Humanoid의 공격 물건(m_defaultItems·m_randomWeapon·m_randomSets, 방패·갑옷 빼고).</summary>
        static IEnumerable<ItemDrop.ItemData> AttackItems(Humanoid h)
        {
            var gos = new List<GameObject>();
            if (h.m_defaultItems != null) gos.AddRange(h.m_defaultItems);
            if (h.m_randomWeapon != null) gos.AddRange(h.m_randomWeapon);
            if (h.m_randomSets != null) foreach (var set in h.m_randomSets) if (set?.m_items != null) gos.AddRange(set.m_items);
            foreach (var go in gos)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                var t = drop.m_itemData.m_shared.m_itemType;
                if (t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs
                    || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility) continue;
                yield return drop.m_itemData;
            }
        }

        /// <summary>쫓아오는 속도: 날기 적(데스킷 등)은 바닐라가 m_runSpeed 대신 m_flyFastSpeed로 난다(Character 이동, ⑫ 라).</summary>
        public static float ChaseSpeed(Character c) => c.m_flying ? c.m_flyFastSpeed : c.m_runSpeed;

        /// <summary>
        /// 게임 데이터의 별 없는 가장 센 한 방: 공격 물건 피해 합 × 그 공격의 m_damageMultiplier(바닐라 Attack.ModifyDamage, 별 배율은 따로).
        /// 공격 물건이 없으면 0(→ 몸집 짐작). 장소 기억의 프리팹에도 쓴다.
        /// </summary>
        public static float DataHit(Character c)
        {
            if (!(c is Humanoid h)) return 0f;
            float dmg = 0f;
            foreach (var it in AttackItems(h))
                dmg = Mathf.Max(dmg, Gear.WeaponScore(it) * it.m_shared.m_attack.m_damageMultiplier);
            ItemDrop.ItemData cur = h.GetCurrentWeapon();
            if (cur != null) dmg = Mathf.Max(dmg, Gear.WeaponScore(cur) * cur.m_shared.m_attack.m_damageMultiplier);
            return dmg;
        }

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

        /// <summary>사냥감 둘레 이만큼 안의 적은 같이 덤빈다고 본다(⑫ 풀링 마을 무리·주술사).</summary>
        public const float PackRange = 15f;

        /// <summary>
        /// 사냥감으로 삼아도 되나(⑧): 그 한 마리와 둘레 PackRange 안 적(자는 것 포함 — 치면 깬다)을 같이 놓고 싸우는 쪽으로 판단될 때(체력 낮으면 안 됨).
        /// pack = 혼자면 되는데 무리 때문에 안 될 때 그 무리에서 가장 센 적(대사용), 아니면 null.
        /// </summary>
        public bool SafePrey(Character c, out Character pack, float extraHit = 0f)
        {
            pack = null;
            Vector3 pos = m_npc.transform.position, at = c.transform.position;
            float armor = m_npc.GetBodyArmor();
            float scale = Game.instance.GetDifficultyDamageScalePlayer(pos) * Game.m_enemyDamageRate;
            float hit = MyHit(pos) + extraHit; // extraHit = 아직 곁에 없는 동료 일꾼 한 방(부르면 올 때, GatherJob.CallMateForPrey)
            Shield? shield = m_npc.BlockStats();
            var taken = Taken(scale, armor, shield, 1);
            var one = new List<Foe>(1) { View(c, pos) };
            if (Danger.Judge(m_npc.Personality, one, m_npc.GetHealth(), m_npc.GetMaxHealth(), hit, taken, false) != Verdict.Fight) return false;
            var all = new List<Foe>(one);
            Character strongest = null;
            float most = 0f;
            foreach (Character o in Character.GetAllCharacters())
            {
                if (o == null || o == c || o == m_npc || o.IsDead() || o.IsPlayer() || o is WorkerNpc || o.IsTamed()) continue;
                if (o.GetFaction() == Character.Faction.AnimalsVeg || o.GetBaseAI() is AnimalAI || !BaseAI.IsEnemy(m_npc, o)) continue;
                if (Vector3.Distance(o.transform.position, at) > PackRange) continue;
                Foe f = View(o, pos);
                all.Add(f);
                if (f.Health * f.Damage > most) { most = f.Health * f.Damage; strongest = o; }
            }
            if (all.Count == 1) return true;
            if (Danger.Judge(m_npc.Personality, all, m_npc.GetHealth(), m_npc.GetMaxHealth(), hit, Taken(scale, armor, shield, all.Count), false) == Verdict.Fight) return true;
            pack = strongest;
            return false;
        }

        /// <summary>
        /// 막힘 복구 순간이동(StuckHop)의 내릴 자리가 안전한가: 그 자리 SenseRange 안 적 전부(자거나 모르는 것 포함, 안 보고 내리니 넉넉히)를
        /// 지금 체력으로 셌을 때 싸우는 쪽(Fight)이거나 적이 없으면 true. 미드는 셈에 넣지 않는다.
        /// </summary>
        public bool SafeAt(Vector3 at)
        {
            var views = new List<Foe>();
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == m_npc || c.IsDead() || c.IsPlayer() || c is WorkerNpc || c.IsTamed()) continue;
                if (c.GetFaction() == Character.Faction.AnimalsVeg || c.GetBaseAI() is AnimalAI || !BaseAI.IsEnemy(m_npc, c)) continue;
                if (Vector3.Distance(c.transform.position, at) > SenseRange) continue;
                views.Add(View(c, at));
            }
            if (views.Count == 0) return true;
            float armor = m_npc.GetBodyArmor();
            float scale = Game.instance.GetDifficultyDamageScalePlayer(at) * Game.m_enemyDamageRate;
            return Danger.Judge(m_npc.Personality, views, m_npc.GetHealth(), m_npc.GetMaxHealth(), MyHit(at),
                Taken(scale, armor, m_npc.BlockStats(), views.Count), false) == Verdict.Fight;
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

        /// <summary>적 하나의 판단용 수치: 체력은 보이는 값(체력바), 한 방은 아는 적이면 배운 값, 처음 보는 적이면 게임 데이터(DataHit)에 별 레벨 반영, 데이터도 없으면 몸집 짐작.</summary>
        Foe View(Character c, Vector3 pos)
        {
            string kind = Utils.GetPrefabName(c.gameObject);
            int level = c.GetLevel();
            float health = c.GetHealth();
            // 회차 170: 안 배운 적도 공격 물건 데이터로(몸집 짐작은 데스킷을 4.7로 봄)
            float baseDmg = Brain.Foes.TryGet(kind, out _, out float learned) ? learned : DataHit(c);
            float damage = baseDmg > 0f
                ? baseDmg * (1f + Mathf.Max(0, level - 1) * 0.5f)
                : Danger.GuessDamage(c.GetHeight(), c.GetRadius(), level);
            // 저항 미드(사용자 결정 2026-10-22): 그 속성 저항이 있거나 미드를 들고 있으면(싸우기 직전에 마심) 속성 몫을 반(Resistant)으로 센다. 데이터로 센 한 방만.
            if (baseDmg > 0f && FoeElementDamage(c) is Dictionary<string, float> el)
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
            // 20m 안 동료 일꾼·호위병도 같이 싸운다(도망 중·죽은 동료는 빼고, 사용자 결정 2026-10-24 나·10-26 호위병)
            foreach (var list in new[] { WorkerNpc.All, WorkerNpc.Guards })
                foreach (var n in list)
                    if (n != null && n != m_npc && !n.IsDead() && !(n.Ai?.Guard?.Fleeing ?? false) && Vector3.Distance(n.transform.position, pos) <= AllyRange)
                        hit += HitOf(n) / 0.9f;
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
            // 멀리선 싸움 단순화(사용자 결정 2026-10-23 "멀리선 단순하게"): 모든 플레이어 64m 밖이면 휘두름 대신 한 번 칠 시간마다 직접 피해
            // (비행·높은 곳 적 포함 — 수평 거리만 본다). 도망 판단은 그대로, 근처에선 아래 바닐라 휘두름(3번 빗나가면 직접 피해).
            m_farCheck -= dt;
            if (m_farCheck <= 0f) { m_farCheck = 1f; m_far = GatherJob.FarFromPlayers(m_npc.transform.position); }
            if (m_far && d <= FarHitRange)
            {
                m_farHitTimer += dt;
                if (m_farHitTimer >= FarHitEvery)
                {
                    m_farHitTimer = 0f;
                    m_fightIdle = 0f;
                    m_diagSwings++;
                    Learn(m_target);
                    DirectHit(weapon, "멀리서(플레이어 64m 밖)");
                }
            }
            if (d > range * 0.9f && !(m_far && d <= FarHitRange)) { m_npc.DropBlock(); m_ai.MoveToPoint(dt, tp, range * 0.8f + m_target.GetRadius(), true); } // 방패를 들면 못 달림
            else
            {
                m_ai.StopMoving();
                m_ai.Face(m_target.GetCenterPoint());
                // 방패 막기(전투 보강 가): 붙어 싸울 땐 방패를 들고 있다(휘두르는 동안은 바닐라가 저절로 안 막음),
                // 적이 휘두르는 중이면 새로 치지 않고 막는다. 앞에서 온 것만 막히니 적을 본다(위 Face).
                bool shieldUp = m_npc.HoldingShield;
                if (shieldUp) m_npc.HoldBlock();
                bool foeSwings = shieldUp && m_target.InAttack();
                if (!m_far && !foeSwings && !m_npc.InAttack() && m_ai.IsLookingAt(tp, 25f) && m_npc.StartAttack(m_target, false))
                {
                    m_fightIdle = 0f;
                    m_diagSwings++;
                    Learn(m_target);
                    // 앞 휘두름이 안 맞았나(적 체력 그대로). 적이 바위 위·턱 위에 있으면 수평 휘두름이 계속 빗나간다
                    // (회차 72 coppertest: 회색 난쟁이가 2.4m 높은 광맥 위, 7번 휘둘러 0번 맞고 일꾼만 죽음) → 3번 잇달아 빗나가면 직접 피해.
                    float hp = m_target.GetHealth();
                    m_missStreak = m_diagSwings > 1 && hp >= m_lastFoeHealth ? m_missStreak + 1 : 0;
                    m_lastFoeHealth = hp;
                    if (m_missStreak >= 3 && Mathf.Abs(tp.y - m_npc.transform.position.y) < 3f) DirectHit(weapon, $"휘두름이 {m_missStreak}번 빗나가");
                }
            }
            // 간헐 목록(회차 66 hunttest): 싸움이 오래 안 끝나면 닿지 못하는지·때리는데 안 맞는지 가른다
            m_diagTimer += dt;
            if (m_diagTimer > 5f)
            {
                m_diagTimer = 0f;
                Plugin.Log.LogInfo($"DIAG 싸움 {m_npc.NpcName}→{m_target.m_name}: 거리 {d:0.0}/{range:0.0} 휘두름 {m_diagSwings} 적 체력 {m_target.GetHealth():0}/{m_target.GetMaxHealth():0} 공격 중 {m_npc.InAttack()} 봄 {m_ai.IsLookingAt(tp, 25f)} 높이차 {tp.y - m_npc.transform.position.y:0.0} 물 {m_target.InWater()} 쉼 {m_fightIdle:0} 막음 {m_npc.Blocks}");
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
        bool m_far;
        float m_farCheck, m_farHitTimer;
        const float FarHitRange = 6f, FarHitEvery = 1.5f;
        float m_lastFoeHealth;

        /// <summary>바닐라 Attack 판정이 못 닿을 때 무기 피해를 직접 준다(GatherJob.DirectHit와 같은 방식, 스킬 반영).</summary>
        void DirectHit(ItemDrop.ItemData weapon, string why)
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
            Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_target.m_name}에 {why} 직접 피해 {hit.GetTotalDamage():0}");
        }

        void EndFight()
        {
            m_missStreak = 0;
            m_lastFoeHealth = 0f;
            m_farHitTimer = 0f;
            m_target = null;
            m_armed = false;
            m_diagSwings = 0;
            m_npc.DropBlock();
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
                // 동료 기다림(MateWait)은 걷는 동안에도 센다(회차 147 vh_matetest C: 걷기 30초 + 기다림 30초 = 60초 기다림)
                if (m_waitMate != null && (m_backTimer -= dt) <= 0f) { m_ai.StopMoving(); m_backWalking = false; m_state = State.Calm; m_waitMate = null; return false; }
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
            if (m_backTimer <= 0f) { m_state = State.Calm; m_waitMate = null; return false; } // 다시 해 본다
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

        /// <summary>돌아가는 쪽(Core Retreat.Way side) — 마주침이 끝나면 0.</summary>
        int m_aroundSide;
        /// <summary>마지막으로 고른 물러날 곳(로그용): 길 종류·거점까지 지금 → 그곳.</summary>
        string m_lastAway = "";

        /// <summary>
        /// 물러날 곳(Core Retreat.Way): 거점이 멀고 적 반대쪽과 크게 어긋나지 않으면 거점 쪽(온 길)으로 dist만큼(거점이 더 가까우면 거점),
        /// 거점 근처면 적 반대쪽, 거점 쪽에 적이 있으면 거점 쪽 옆으로 돌아서. 쫓기는 중이면 덜 꺾는다.
        /// (0.3.7: 거점에 있는데 "거점으로 도망"이라 제자리에서 되풀이했다. 회차 138: 거점 쪽 적을 피해 곧장 반대로 가니
        /// 쫓아오는 회색난쟁이가 늘 거점 쪽에 남아 귀환하다 거점에서 682m까지 멀어졌다.)
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
            fromFoes.y = 0f;
            fromFoes.Normalize();
            bool chased = Chased(10f);
            RetreatWay way = Retreat.Way(toHome.x, toHome.z, fromFoes.x, fromFoes.z, chased, ref m_aroundSide, out double dx, out double dz);
            Vector3 to = way == RetreatWay.Home && toHome.magnitude < dist ? m_npc.Home : pos + new Vector3((float)dx, 0f, (float)dz) * dist;
            m_lastAway = $"{way}{(chased ? " 쫓김" : "")}, 거점 {toHome.magnitude:0}m → {Utils.DistanceXZ(to, m_npc.Home):0}m";
            return to;
        }

        /// <summary>싸워 본 적: 게임 데이터의 실제 기본 체력·가장 센 한 방을 배운다(두 NPC가 같이 쓴다).</summary>
        public static void Learn(Character c)
        {
            if (c == null) return;
            string kind = Utils.GetPrefabName(c.gameObject);
            if (Brain.Foes.Knows(kind)) return;
            float dmg = DataHit(c);
            Brain.Foes.Learn(kind, c.m_health, dmg);
            Plugin.Log.LogInfo($"적 배움: {kind} 체력 {c.m_health:0}, 한 방 {dmg:0}");
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
        public static float HitWith(WorkerNpc npc, ItemDrop.ItemData w)
        {
            // 스킬 반영(3-1장): 평균 피해 계수(스킬 10이면 0.46, 70이면 0.82)
            return (w != null ? Gear.WeaponScore(w) * npc.MeanSkillFactor(w.m_shared.m_skillType) : 5f * npc.MeanSkillFactor(Skills.SkillType.Unarmed));
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
