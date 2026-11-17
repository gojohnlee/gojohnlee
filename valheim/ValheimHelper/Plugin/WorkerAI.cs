using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>WorkerAI가 Mode.Work에서 돌리는 일.</summary>
    public interface IWorkerJob
    {
        /// <returns>false면 작업 끝(성공이든 실패든). 알림은 작업이 한다.</returns>
        bool Update(float dt);
        string StepName { get; }
        /// <summary>!상태에 넣을 말("나무 모으는 중(3/10)").</summary>
        string StateLine();
        /// <summary>Guard: 거기(danger)는 위험하다(도망쳤거나 두 번 물러남). 그곳의 목표는 버리고 다른 데로.</summary>
        void AvoidHere(Vector3 danger);
        /// <summary>대기열·"~부터 하고 갈게요"에 쓰는 짧은 이름("나무 50").</summary>
        string Label { get; }
        /// <summary>
        /// 일을 놓을 때 WorkerAI.Job 한 곳에서 늘 부름(사용자 11-10): 끝났을 때·취소·쉬기·죽음 모두. pause면 대기열에 들어가 나중에 Resume.
        /// 일이 남긴 것(짐승 따라오기·든 물건·꺼낸 먹이)을 정리. 여러 번 불려도 안전하게.
        /// </summary>
        void Stop(bool pause);
        /// <summary>대기열에서 다시 시작할 때(Stop(true) 뒤). 멈출 때 푼 것을 다시 건다.</summary>
        void Resume();
        /// <summary>쉬는 일(상자 정리·묘비·보충 채집·쉴 때 계획): 새 명령에 바로 자리를 내준다(WorkerAI.Yields).</summary>
        bool Yields { get; }
        /// <summary>하던 일 → 이어 하기 칸(큰 단계만). 저장할 수 없는 일(묘비 회수 등)이면 null.</summary>
        WorkItem Save();
    }

    /// <summary>
    /// 대기열 한 칸 (설계 B-5): 멈춘 일을 이어서 하거나(Resume), 명령을 다시 판단해서 시작한다(Start).
    /// Ready가 false면(아침까지 기다림 등) 건너뛰고 다음 칸.
    /// </summary>
    public sealed class QueuedWork
    {
        public string Label;
        public IWorkerJob Resume;
        public System.Action Start;
        public System.Func<bool> Ready;
        /// <summary>저장용 데이터(WorkSaves → ZDO vh_queue). null이면 저장하지 않는 칸.</summary>
        public WorkItem Work;
        /// <summary>멈춘 까닭(예: 습격). 저장할 때 WorkItem.Why로 넘긴다(요청 멈췄다 이어 가기).</summary>
        public string Why;
    }

    /// <summary>
    /// 일꾼 NPC 행동. 지금(① 단계)은 대기·따라가기·집에 가기만 한다.
    /// 이동·길찾기는 바닐라 BaseAI.MoveTo를 쓴다 (구상서 12장).
    /// </summary>
    public partial class WorkerAI : BaseAI
    {
        public enum Mode { Idle, Follow, Home, Work }

        /// <summary>Mode.Work일 때 하는 일(채집 GatherJob, 탐험 ExploreJob).</summary>
        public IWorkerJob Job
        {
            get => m_job;
            // 일이 바뀌는 한 곳: 놓는 일은 Stop(대기열에 Resume으로 들어갔으면 잠깐 멈춤), 새 일은 Resume(멈췄던 일이면 다시 걸게)
            private set
            {
                IWorkerJob old = m_job;
                if (ReferenceEquals(old, value)) return;
                m_job = value;
                if (old != null) SafeJob(old, () => old.Stop(Queue.Exists(q => ReferenceEquals(q.Resume, old))), "Stop");
                if (value != null) SafeJob(value, value.Resume, "Resume");
            }
        }

        static void SafeJob(IWorkerJob job, System.Action a, string what)
        {
            try { a(); }
            catch (System.Exception e) { Plugin.Log.LogError($"{job.GetType().Name}.{what} 오류: {e}"); }
        }
        IWorkerJob m_job;

        /// <summary>
        /// 지금 일이 새 명령에 바로 자리를 내주는 일(묘비 회수·쉴 때 상자 정리, 스스로 한 일 — 자동 보충·쉴 때 먹거리·만들기·밭일·쓴 재료 다시 캐기)
        /// — 대기열에 안 넣고 "~부터 하고 갈게요"도 안 함(사용자 11-09 "시켰으면 바로 출발").
        /// </summary>
        public bool Yields => Job != null && Job.Yields;
        float m_senseTimer;

        public Mode Current { get; private set; } = Mode.Idle;
        public string FollowName { get; private set; }
        /// <summary>따라가는 대상(플레이어 또는 동료 일꾼), 없으면 None.</summary>
        public ZDOID FollowTarget => m_follow;

        /// <summary>일부러 서 있는 까닭(통나무 기다림·설비 기다림 등) — 매 프레임 Hold로 적고, 시험의 멈춤 재기(TestKit.Stalls)만 읽는다.</summary>
        public string HoldWhy { get; private set; }
        public float HoldAt { get; private set; } = -99f;
        public void Hold(string why) { HoldWhy = why; HoldAt = Time.time; }

        ZDOID m_follow = ZDOID.None;
        float m_farSayAt;
        float m_raidCheckAt;
        float m_stuckTimer;
        Vector3 m_lastPos;

        /// <summary>프리팹을 만들 때 한 번: 바닐라 기본값 중 일꾼에 안 맞는 것만 바꾼다.</summary>
        public void Setup()
        {
            m_viewRange = 40f;
            m_hearRange = 30f;
            // Humanoid는 물 위도 길로 친다(0.3.6에서 해협을 헤엄쳐 건넘). 물을 뺀 길만 찾고, 좁은 강은 MoveToPoint가 따로 판단한다.
            m_pathAgentType = Pathfinding.AgentType.HumanoidAvoidWater;
            m_canBeAlerted = false;
            m_avoidWater = true;
            m_idleSoundChance = 0f;
            // 몬스터 기본값(10°)이면 방향이 조금만 틀어져도 멈춰 서서 돈다 → 걷다 서다를 반복했다(0.3.1 사용자 관찰).
            // 90°면 돌면서 속도만 줄인다(플레이어처럼).
            m_moveMinAngle = 90f;
            m_smoothMovement = true;
        }

        /// <summary>대기열(설계 B-5, 사용자 확정): 급하지 않은 명령은 뒤에, 급한 명령에 밀린 일은 맨 앞에. [게임 시험 안 됨]</summary>
        public readonly List<QueuedWork> Queue = new List<QueuedWork>();

        /// <summary>급한 명령: 하던 일을 대기열 맨 앞에 넣고 비운다(새 일은 부르는 쪽이 StartJob).</summary>
        public void Interrupt(string why = null)
        {
            if (Current == Mode.Work && Job != null && !Yields)
                Queue.Insert(0, new QueuedWork { Label = Job.Label, Resume = Job, Work = WorkSaves.FromJob(Job), Why = why });
            Job = null;
        }

        /// <summary>대기열에서 할 수 있는 첫 칸을 꺼내 시작한다. 시작했으면 true.</summary>
        bool NextQueued()
        {
            if (Departures.AskedAt((WorkerNpc)m_character) >= 0) return false; // 질문하고 대답을 기다리는 중
            for (int i = 0; i < Queue.Count; i++)
            {
                var q = Queue[i];
                if (q.Ready != null && !q.Ready()) continue;
                Queue.RemoveAt(i);
                if (q.Resume != null) StartJob(q.Resume);
                else q.Start?.Invoke();
                return Current == Mode.Work;
            }
            return false;
        }

        /// <summary>근처 회복으로 간 일이 도착을 센 구역 — 집에 안 가고 이어 가면 다시 안 셈(요청 다침 ④, Core WorkerPortal.KeepVisits). 저장 안 함.</summary>
        System.Collections.Generic.List<ZoneXY> m_hurtVisited;

        public void StartJob(IWorkerJob job)
        {
            if (m_hurtVisited != null && job is GatherJob)
            {
                if (WorkerPortal.KeepVisits(Utils.DistanceXZ(transform.position, ((WorkerNpc)m_character).Home))) ((GatherJob)job).KeepVisited(m_hurtVisited);
                m_hurtVisited = null;
            }
            Job = job;
            m_escort = null; // 동행은 그 일에만(부르는 쪽이 StartJob 뒤 EscortBy)
            Risky = false;
            m_guard?.Reset();
            m_choresHeld = false;
            Current = Mode.Work;
        }

        public void Face(Vector3 point) => LookAt(point);

        Escort m_escort;
        ZDOID m_escortId = ZDOID.None;
        /// <summary>지금 일이 남은 곳이 다 위험한 데로 가는 일(출발 판단 GoTogether) — 동행이 없으면 쉬는 동료가 따라간다(Departures.JoinRisky, 사용자 11-13). StartJob마다 지움, 저장 안 함.</summary>
        public bool Risky { get; set; }
        /// <summary>지금 일에 같이 가는 동료 일꾼 이름(앞장서는 중), 없으면 null. 시험·!상태용.</summary>
        public string EscortName => m_escort != null ? m_escortName : null;
        string m_escortName;
        /// <summary>셋째로 같이 가는 동료 일꾼 이름(⑦ 셋이면 될 때), 없으면 null.</summary>
        public string Escort2Name => m_escort != null ? m_escort2Name : null;
        string m_escort2Name;
        /// <summary>시험용: 앞장서기 단계.</summary>
        public Escort.Phase? EscortPhase => m_escort?.Now;

        /// <summary>지금 일을 동료 일꾼과 같이 한다(Departures.GoWithMate): 기다렸다가 앞장서고 20m 안 유지. StartJob 뒤에 부른다.</summary>
        public void EscortBy(ZDOID who, string name, Escort escort = null)
        {
            if (Current != Mode.Work || Job == null || who.IsNone()) return;
            m_escort = escort ?? new Escort();
            m_escortId = who;
            m_escortName = name;
            m_escort2Name = null;
        }

        /// <summary>EscortBy 뒤에 셋째 동료를 붙인다(그 동료는 Follow로 이 일꾼을 따라옴, 기다림은 첫 동행만 — Departures.GoWithTeam).</summary>
        public void EscortAlso(string name)
        {
            if (m_escort != null) m_escort2Name = name;
        }

        /// <summary>지금 일의 동행을 끝낸다(제작 재료 채집 단계가 끝남 등) — 호위병은 이걸 보고 거점으로 돌아간다(Follow).</summary>
        public void EndEscort()
        {
            if (m_escort == null) return;
            Plugin.Log.LogInfo($"{((WorkerNpc)m_character).NpcName}: 동행 {m_escortName} 끝 → 돌려보냄");
            m_escort = null;
        }

        /// <summary>동행자를 기다려야 하면 true(이번 프레임 일은 쉼).</summary>
        bool EscortHolds(WorkerNpc npc, float dt)
        {
            WorkerNpc mate = WorkerNpc.FindById(m_escortId);
            if (mate != null && (mate.IsDead() || mate.Ai.Current != Mode.Follow || mate.Ai.FollowTarget != npc.GetZDOID()))
            {
                // 호위병이 따라오기를 그만둠(죽음·다른 일 등): 기다리지 않고 혼자 감(위험해도 거절 안 함 — 사용자 10-25), 알린다
                Plugin.Log.LogInfo($"{npc.NpcName}: 동행 {m_escortName}이 따라오기를 그만둠({mate.Ai.Current}, 죽음 {mate.IsDead()}) → 혼자 감");
                npc.Say("escort.giveup");
                m_escort = null;
                return false;
            }
            ZDO z = ZDOMan.instance.GetZDO(m_escortId);
            Vector3 pos = z != null ? z.GetPosition() : Vector3.zero;
            float d = z != null ? Vector3.Distance(pos, transform.position) : -1f;
            if (mate != null && mate.IsTeleporting()) d = Mathf.Min(d, Escort.Resume); // 곁으로 옮기는 중: 포기하지 않는다
            else if (mate != null && d > Escort.Far && BringMate(npc, mate, d)) d = Escort.Resume;
            if (m_escort2Name != null && d >= 0f)
            {
                // ⑦ 셋째 동료(Escort2Name)도 곁에 와야 셋으로 셈(Guard.MyHit 20m) — 먼 쪽 거리로 기다림, 따라오기를 그만두면 둘로
                WorkerNpc third = WorkerNpc.All.Find(o => o != null && o != npc && o.NpcName == m_escort2Name);
                if (third == null || third.IsDead() || third.Ai.Current != Mode.Follow || third.Ai.FollowTarget != npc.GetZDOID())
                {
                    Plugin.Log.LogInfo($"{npc.NpcName}: 셋째 동행 {m_escort2Name}이 따라오기를 그만둠 → 둘이 감");
                    m_escort2Name = null;
                }
                else if (!third.IsTeleporting())
                {
                    float d2 = Vector3.Distance(third.transform.position, transform.position);
                    if (d2 > Escort.Far && BringMate(npc, third, d2)) d2 = Escort.Resume;
                    d = Mathf.Max(d, d2);
                }
            }
            EscortAct act = m_escort.Tick(dt, d);
            if (m_escort.Say != null) npc.Say(m_escort.Say);
            if (act == EscortAct.GiveUp)
            {
                Plugin.Log.LogInfo($"{npc.NpcName}: 동행 {m_escortName} 포기(거리 {d:0}m) → 혼자 감");
                m_escort = null;
                return false;
            }
            if (act == EscortAct.Go) return false;
            StopMoving();
            LookAt(pos);
            return true;
        }

        /// <summary>
        /// 동행이 끊기지 않게(사용자 10-26 검토): 포탈·던전 문처럼 나만 건너갔거나(Gone 넘게 벌어짐), 둘 다 아무 플레이어에게서 64m 밖이거나
        /// (멀리선 이동 생략으로 나만 건너뜀), 출발 뒤 15초 넘게 못 따라오면(막힘) 동행자를 내 곁으로 옮긴다. 막힘이고 플레이어가 보면 구조 말.
        /// 출발 전(Waiting)엔 보이는 곳이면 걸어오게 둔다. 옮겼으면 true. 시험용 횟수 MateBrings.
        /// </summary>
        bool BringMate(WorkerNpc npc, WorkerNpc mate, float d)
        {
            bool jumped = d > Escort.Gone;
            bool unseen = GatherJob.FarFromPlayers(transform.position) && GatherJob.FarFromPlayers(mate.transform.position);
            bool stuck = m_escort.Now == Escort.Phase.Paused && m_escort.PausedFor > (float)StuckHop.NearStuckSeconds;
            if (!jumped && !unseen && !stuck) return false;
            Vector3 me = transform.position, at = me - Flat(transform.forward).normalized * 1.5f + Vector3.up * 0.3f;
            if (Character.InInterior(me) || !ZoneSystem.instance.GetSolidHeight(at, out float h) || Mathf.Abs(h - me.y) > 1.5f) at = me + Vector3.up * 0.3f;
            else at.y = h + 0.3f;
            if (!mate.TeleportTo(at, transform.rotation, false)) return false;
            MateBrings++;
            Plugin.Log.LogInfo($"{npc.NpcName}: 동행 {mate.NpcName}을 곁으로 옮김({d:0}m, {(jumped ? "건너감" : unseen ? "안 보는 곳" : "못 따라옴")})");
            if (stuck && !unseen) mate.Say("rescue");
            return true;
        }

        /// <summary>시험용: 동행자를 곁으로 옮긴 횟수.</summary>
        public static int MateBrings;

        Guard m_guard;
        internal ItemDrop StrayTarget => m_guard?.StrayTarget;
        /// <summary>위험 감지·싸움(구상서 8장). 시험·!상태용.</summary>
        public Guard Guard => m_guard;
        /// <summary>이번 프레임 싸움·물러나기·도망·미드 중(잡담은 쉼).</summary>
        public bool GuardBusy { get; private set; }

        /// <summary>Guard: 거기는 위험하다 → 하던 일이 그곳을 버린다.</summary>
        public void AvoidHere(Vector3 danger) => Job?.AvoidHere(danger);

        /// <summary>시험 기록용: 마지막 길찾기가 길을 찾았는지.</summary>
        public bool PathFound => FoundPath();

        /// <summary>시험 기록용: 걷기 판단 상태.</summary>
        public string MoveState => $"깎기 {m_carving}(곡괭이질 {m_carveCount}, 걷기 {m_carveWalk:0.0}), 막혀서 뜀 {m_stuckJumps}, 비켜 가기 {m_detourTime:0.0}, " +
            $"물 막힘 {WaterBlockedTime:0.0}, 길 {FoundPath()}, 경계 {m_guard?.StateName}, 공격 중 {m_character.InAttack()}";

        public void Follow(ZDOID target, string name)
        {
            Job = null;
            m_escort = null;
            m_follow = target;
            FollowName = name;
            m_choresHeld = false;
            Current = Mode.Follow;
        }

        bool m_announceHome = true;

        static bool RaidOver() => RandEventSystem.instance == null || RandEventSystem.instance.GetCurrentRandomEvent() == null;

        public void GoHome(bool announce = true)
        {
            m_announceHome = announce;
            Job = null;
            m_escort = null;
            m_follow = ZDOID.None;
            Current = Mode.Home;
            m_stuckTimer = 0f;
            m_homeBook.HomeReached();
        }

        /// <summary>집 가기(Mode.Home)의 귀환 막힘 횟수(StuckBook 귀환 칸만 씀). GoHome마다 새로.</summary>
        readonly StuckBook m_homeBook = new StuckBook();

        /// <summary>
        /// 막힘 복구 마지막 단계(사용자 결정 2026-10-23): 깃발로 순간이동. 소유자면 옮긴 뒤 깃발 StuckBook.HomeArrived m 안인지까지 보고,
        /// 아니면 false(순간이동 실패 — 부른 쪽이 RestHere). 소유자가 아니면 RPC만 보내고 true.
        /// </summary>
        public bool TeleportHome()
        {
            var npc = (WorkerNpc)m_character;
            Vector3 home = npc.Home;
            StopMoving();
            m_carving = false;
            bool sent = npc.TeleportTo(home, transform.rotation, false);
            Plugin.Log.LogInfo($"{npc.NpcName}: 순간이동 자리 집으로 귀환 (보냄 {sent})");
            StuckTeleports++;
            if (!sent) return false;
            ZNetView nv = npc.GetComponent<ZNetView>();
            return nv == null || !nv.IsOwner() || Utils.DistanceXZ(home, transform.position) <= (float)StuckBook.HomeArrived;
        }

        /// <summary>깃발 순간이동마저 안 됨(드묾): 하던 일은 버리고 그 자리에서 쉰다 + "집에 못 가요". 다시 걷기를 되풀이하지 않는다(다음 명령 대기).</summary>
        public void RestHere(WorkerNpc npc)
        {
            Plugin.Log.LogInfo($"{npc.NpcName}: 깃발로 순간이동도 안 됨(집에서 {Utils.DistanceXZ(npc.Home, transform.position):0.0}m) → 그 자리에서 쉼");
            StopMoving();
            Job = null;
            m_escort = null;
            m_follow = ZDOID.None;
            Current = Mode.Idle;
            HomeFails++;
            npc.Say("homeFail");
        }

        /// <summary>시험용: 깃발 순간이동도 안 돼 그 자리에서 쉰 횟수.</summary>
        public static int HomeFails;

        /// <summary>집에 가는데 지하묘지 안에 있었던 시간, 나가는 Teleport를 마지막으로 쓴 때.</summary>
        float m_homeInside, m_exitUsedAt = -10f;

        public override bool UpdateAI(float dt)
        {
            if (!base.UpdateAI(dt)) return false;
            if (TestHoldAll) { StopMoving(); return true; } // 시험용(E072): 불러온 그대로 멈춰 섬
            var npc = (WorkerNpc)m_character;
            npc.UpdateFood(dt);
            m_senseTimer -= dt;
            if (m_senseTimer <= 0f)
            {
                m_senseTimer = 2f;
                Senses.Look(transform.position);
            }
            if (m_guard == null) m_guard = new Guard(npc, this);
            if (Current == Mode.Work && Time.time >= m_raidCheckAt)
            {
                m_raidCheckAt = Time.time + 5f;
                var ev = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
                // 습격 구역(m_eventRange)에서 일하는 중이면 거점으로 귀환. 이미 거점 근처(30m)면 그대로 둔다.
                // 도착 때 습격 남은 시간이 절반 이상일 때만 귀환, 아니면 하던 일 계속(사용자 11-14 ⑧, Core RaidReturn).
                if (ev != null && Utils.DistanceXZ(transform.position, ev.m_pos) < ev.m_eventRange && Utils.DistanceXZ(transform.position, npc.Home) > 30f
                    && RaidReturn.GoHome(ev.m_duration, ev.GetTime(), Utils.DistanceXZ(transform.position, npc.Home)))
                {
                    npc.Say("raid");
                    // 하던 일은 대기열 맨 앞에 두고 습격이 끝나면 이어 감(사용자 11-13 "귀환해도 하던 일은 절대 안 잊음")
                    bool keep = Job != null && !Yields;
                    Interrupt("습격");
                    if (keep && Queue.Count > 0) Queue[0].Ready = RaidOver;
                    GoHome(false);
                    return true;
                }
            }
            (Job as GatherJob)?.TrackPrey(); // Guard가 사냥감과 싸우는 동안에도 사냥감 자리를 새로(떨굼 가로채기 E094)
            GuardBusy = m_guard.Update(dt);
            // 밤에 센 적에게서 도망쳤으면 일을 접고 거점으로, 하던 일은 대기열 맨 앞에서 아침에 이어 감(요청 밤 몫 ② 11-13)
            if (m_guard.TakeFleeEnded() && Current == Mode.Work && Job != null && EnvMan.IsNight())
            {
                npc.Say("nightRetreat");
                bool keep = !Yields;
                Interrupt("밤 위험");
                if (keep && Queue.Count > 0)
                {
                    QueuedWork q = Queue[0];
                    if (q.Work != null)
                    {
                        q.Work.Ready = ReadyWhen.Morning;
                        q.Work.NotBefore = 0; // 이미 밤이라 "밤이 아니면" 만으로 다음 아침(시험의 시각 고정에서도 맞음)
                        q.Ready = () => WorkState.IsReady(q.Work, ZNet.instance.GetTimeSeconds(), EnvMan.IsNight());
                    }
                    else q.Ready = () => !EnvMan.IsNight();
                }
                Plugin.Log.LogInfo($"{npc.NpcName} 밤 위험: 일 접고 귀환, 아침에 이어 감({(keep ? "대기열 맨 앞" : "남의 일 돕기라 안 남김")})");
                GoHome(false);
                return true;
            }
            if (m_patrol.Walking && m_guard.Engaged && !m_patrolFighting) m_roundFights++; // 순찰 중 싸움 셈(diag)
            m_patrolFighting = m_guard.Engaged;
            if (GuardBusy) { m_patrolTick = Time.time; return true; } // 싸움·물러나기·도망·미드 중: 일·따라가기는 쉰다(호위병 순찰은 싸운 뒤 이어 감)
            switch (Current)
            {
                case Mode.Work:
                    if (Job != null && m_escort != null && EscortHolds(npc, dt)) break;
                    if (Job == null || !Job.Update(dt))
                    {
                        bool ready = Job is GuardReadyJob; // 호위병 장비 챙기기 끝: 집(깃발)으로 가지 않고 쉬며 문 옆 경계 자리로(GuardPostTick)
                        if (Job is GatherJob nd && nd.NightDefer && !Yields)
                        {
                            // 밤이라 새 땅에 못 감: 시킨 일을 접지 않고 남은 몫을 아침에 이어 감(요청 밤 몫 ③, E090)
                            var w = WorkSaves.FromJob(nd);
                            w.Ready = ReadyWhen.Morning;
                            w.NotBefore = 0;
                            w.Why = "밤";
                            var dq = WorkSaves.ToQueued(npc, w);
                            dq.Why = "밤";
                            Queue.Insert(0, dq);
                            Plugin.Log.LogInfo($"{npc.NpcName} 작업 {nd.Item}: 밤이라 남은 {w.Amount}개는 아침에 이어 감");
                        }
                        else if (Job is GatherJob hd && hd.HurtDefer && !Yields)
                        {
                            // 다쳐서 돌아옴: 일을 접지 않고 체력이 찬 뒤 남은 몫을 이어 감(요청 다침 ③, Core Recovery)
                            var w = WorkSaves.FromJob(hd);
                            w.Why = "다침";
                            Queue.Insert(0, WorkSaves.ToQueued(npc, w)); // 체력이 찰 때까지 기다림은 ToQueued가 Why "다침"으로 붙임(재시작 뒤에도)
                            Plugin.Log.LogInfo($"{npc.NpcName} 작업 {hd.Item}: 다쳐서 남은 {w.Amount}개는 체력이 차면 이어 감");
                            if (hd.HurtNear) // 다친 자리에서 고른 근처 회복(GatherJob.UpdateHunt — Core Recovery.Choose)
                            {
                                Plugin.Log.LogInfo($"{npc.NpcName} 다쳐서 근처에서 줍기하며 회복");
                                m_hurtVisited = new System.Collections.Generic.List<ZoneXY>(hd.Visited);
                                Job = new RecoverNearJob(npc);
                                break;
                            }
                        }
                        else if (Job is GatherJob) npc.ClearHurt();
                        Job = null;
                        m_escort = null;
                        if (!NextQueued()) { if (ready) Current = Mode.Idle; else GoHome(false); }
                    }
                    break;
                case Mode.Follow:
                {
                    ZDO target = m_follow.IsNone() ? null : ZDOMan.instance.GetZDO(m_follow);
                    if (target == null) { Lost(npc); break; }
                    Vector3 pos = target.GetPosition();
                    float d = Vector3.Distance(pos, transform.position);
                    WorkerNpc lead = null;
                    foreach (var o in WorkerNpc.All)
                        if (o != npc && o.GetZDOID() == m_follow) { lead = o; break; }
                    if (lead != null)
                    {
                        // 같이 가는 동료 일꾼(사용자 결정 2026-10-26: 거리 무관 — 멀어도 따라감). 그쪽이 일을 마치고 쉬거나 나를 더는 안 기다리면(혼자 감) 끝
                        if (lead.Ai.Current == Mode.Idle || lead.Ai.Current == Mode.Home || lead.Ai.EscortName != npc.NpcName && lead.Ai.Escort2Name != npc.NpcName) { GoHome(false); break; }
                    }
                    else
                    {
                        if (d > 200f) { Lost(npc); break; } // 포탈 등으로 사라짐. 포탈 따라가기는 ⑤ 단계.
                        if (d > 20f && Time.time >= m_farSayAt) { m_farSayAt = Time.time + 30f; npc.Say("escort.far"); } // 따라가는 플레이어와 20m 유지
                    }
                    if (d < 3f) StopMoving();
                    else MoveToPoint(dt, pos, 2.5f, d > 8f);
                    break;
                }
                case Mode.Home:
                {
                    if (npc.InInterior())
                    {
                        // 지하묘지 안에서 집으로(안에서 일을 그만둠 등): 집 쪽으로 걸으면 묘지 벽에 막힌다 → 나가는 Teleport로 나간다
                        // (GatherJob.LeaveCrypt와 같게, 나가는 길이 없거나 2분 넘으면 깃발로).
                        m_homeInside += dt;
                        Teleport exit = Dungeons.Exit(transform.position);
                        if (exit == null || m_homeInside > 120f)
                        {
                            Plugin.Log.LogInfo($"{npc.NpcName}: 지하묘지 나가는 길 없음/시간 초과 → 깃발로");
                            m_homeInside = 0f;
                            if (!TeleportHome()) RestHere(npc);
                            break;
                        }
                        Vector3 ep = exit.transform.position;
                        if ((MoveToPoint(dt, ep, 1.5f, false) || Vector3.Distance(ep, transform.position) < 2.5f) && Time.time - m_exitUsedAt > 3f)
                        {
                            m_exitUsedAt = Time.time;
                            StopMoving();
                            Dungeons.Use(npc, exit);
                        }
                        break;
                    }
                    m_homeInside = 0f;
                    if (WorkerBanter.Facing(npc, out Vector3 talkTo))
                    {
                        // 잡담 중(몇 초): 멈춰 서로 바라본다. 서 있는 동안은 막힘으로 세지 않는다.
                        StopMoving();
                        LookAt(talkTo);
                        m_stuckTimer = 0f;
                        m_lastPos = transform.position;
                        break;
                    }
                    Vector3 home = npc.Home;
                    float d = Utils.DistanceXZ(home, transform.position);
                    if (d < 2f)
                    {
                        StopMoving();
                        m_homeBook.HomeReached();
                        Current = Mode.Idle;
                        if (m_announceHome) npc.Say("home");
                        break;
                    }
                    MoveToPoint(dt, home, 1.5f, d > 15f);
                    // 막힘 복구(사용자 결정 2026-10-23): 7.5초 동안 못 움직이면 막힘 1번(비켜 가기·점프는 걷기가 계속 함).
                    // StuckBook.HomeWalkTries번째면 더 걷지 않고 깃발로 순간이동, 그것도 안 되면 그 자리에서 쉰다("집에 못 가요").
                    m_stuckTimer = Vector3.Distance(m_lastPos, transform.position) < 0.5f * dt && !m_carving ? m_stuckTimer + dt : 0f;
                    m_lastPos = transform.position;
                    if (m_stuckTimer > 7.5f) // 집 가는 길 막힘 15 → 7.5초(막힘 시간 절반, 사용자 결정 2026-10-26)
                    {
                        m_stuckTimer = 0f;
                        if (!m_homeBook.HomeWalkFailed())
                        {
                            Plugin.Log.LogInfo($"{npc.NpcName}: 집에 가는 길이 막힘 {m_homeBook.HomeStuck}번째(집에서 {d:0.0}m) → 더 걸어 봄 / {MoveState}");
                            break;
                        }
                        Plugin.Log.LogInfo($"{npc.NpcName}: 집에 가는 길이 {m_homeBook.HomeStuck}번 막혀서 깃발로 순간이동");
                        if (!TeleportHome()) { RestHere(npc); break; }
                    }
                    break;
                }
                default:
                    if (!npc.IsGuard) StopMoving(); // 호위병은 GuardPostTick이 걷거나 멈춤
                    if (WorkerBanter.Facing(npc, out Vector3 faceTo)) LookAt(faceTo);
                    if (Queue.Count > 0 && NextQueued()) break; // 직접 명령 > 묘비 회수(구상서 11장 우선순위)
                    Chores(npc, dt);
                    if (npc.IsGuard && Current == Mode.Idle) { GuardReady(npc, dt); if (Current == Mode.Idle) GuardPostTick(npc, dt); }
                    break;
            }
            return true;
        }


        // ---- 죽은 뒤 (구상서 9장) ----

        /// <summary>!멈춰로 쉬라고 했다: 다음 명령이나 다음 죽음 전까지 묘비 회수를 스스로 시작하지 않는다.</summary>
        bool m_choresHeld;
        float m_choreTimer = 3f;
        float m_guardReadyTimer = 5f;

        /// <summary>쉬는 호위병: 1분마다 거점 상자에서 더 좋은 장비·음식·미드를 챙긴다(챙길 게 없으면 바로 끝남, 사용자 10-26).</summary>
        void GuardReady(WorkerNpc npc, float dt)
        {
            m_guardReadyTimer -= dt;
            if (m_guardReadyTimer > 0f) return;
            m_guardReadyTimer = 60f;
            StartJob(new GuardReadyJob(npc));
        }
        bool m_saidGraveNight;

        public void HoldChores() => m_choresHeld = true;

        /// <summary>"멈춰"로 쉬라고 한 뒤(다음 명령 전까지): 쉴 때 먹거리 같은 스스로 하는 일을 시작하지 않는다.</summary>
        public bool Resting => m_choresHeld;

        /// <summary>회수할 묘비가 있다(자동 보충보다 먼저, 구상서 11장).</summary>
        public bool HasChores => !m_choresHeld && ((WorkerNpc)m_character).Graves().Count > 0;

        /// <summary>
        /// 죽었다(WorkerNpc.OnDeath): 하던 일은 이어 하기 칸(큰 단계만, WorkSaves.FromJob)으로 대기열 맨 앞에 두고 묘비 회수가 끝난 뒤 다시 한다
        /// (짐·연장이 묘비에 있음, 오류 정리 꼼수 라-2). 묘비를 회수하다 죽었으면 그 묘비 시도 횟수를 올린다.
        /// </summary>
        public void OnDied()
        {
            var npc = (WorkerNpc)m_character;
            if (Job is GraveJob g) npc.SetGraves(WorkerNpc.AddTry(npc.Graves(), g.Grave, 1));
            else if (Current == Mode.Work && Job != null && WorkSaves.FromJob(Job) is WorkItem w)
            {
                // 계획(PlanJob)은 객체째 이어 한다: 다시 세우면 설비에 이미 올린 것을 몰라 재료를 또 모은다(회차 128). 저장(재시작)은 여전히 목표 데이터(w).
                QueuedWork q;
                if (Job is PlanJob plan) { plan.OnDied(); q = new QueuedWork { Label = plan.Label, Resume = plan, Work = w }; }
                else q = WorkSaves.ToQueued(npc, w);
                System.Func<bool> ready = q.Ready;
                q.Ready = () => !HasChores && (ready == null || ready());
                Queue.Insert(0, q);
                Plugin.Log.LogInfo($"{npc.NpcName}: 죽어서 하던 일({w.Label})은 묘비 회수 뒤 이어 함");
            }
            Job = null;
            m_follow = ZDOID.None;
            m_guard?.OnDied(); // 죽기 전 도망·물러날 곳으로 거점에서 다시 걸어가지 않게
            Current = Mode.Idle;
            StopMoving();
            m_choresHeld = false;
            m_choreTimer = 3f;
            m_saidGraveNight = false;
        }

        /// <summary>한가할 때 할 일: 남은 묘비 회수(밤이면 아침까지, 2번 실패한 묘비는 위치만 알리고 포기).</summary>
        void Chores(WorkerNpc npc, float dt)
        {
            if (m_choresHeld) return;
            m_choreTimer -= dt;
            if (m_choreTimer > 0f) return;
            m_choreTimer = 5f;
            var graves = npc.Graves();
            if (graves.Count == 0) return;
            foreach (var g in graves.ToArray())
            {
                ZDO zdo = ZDOMan.instance.GetZDO(g.Id);
                if (zdo == null) { npc.ForgetGrave(g.Id); continue; } // 누가 챙겼거나 사라짐
                if (g.Tries < GraveJob.MaxTries) continue;
                npc.ForgetGrave(g.Id);
                Scout.FromHome(npc.Home, zdo.GetPosition(), out string dir, out string dist);
                npc.Say("graveFail", new Dictionary<string, string> { ["dir"] = dir, ["dist"] = dist });
            }
            graves = npc.Graves();
            if (graves.Count == 0) return;
            if (EnvMan.IsNight())
            {
                if (!m_saidGraveNight) { m_saidGraveNight = true; npc.Say("graveNight"); }
                return;
            }
            StartJob(new GraveJob(npc, graves[0].Id));
        }
        void Lost(WorkerNpc npc)
        {
            npc.Say("lost");
            GoHome();
        }
    }
}
