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
    }

    /// <summary>
    /// 일꾼 NPC 행동. 지금(① 단계)은 대기·따라가기·집에 가기만 한다.
    /// 이동·길찾기는 바닐라 BaseAI.MoveTo를 쓴다 (구상서 12장).
    /// </summary>
    public class WorkerAI : BaseAI
    {
        public enum Mode { Idle, Follow, Home, Work }

        /// <summary>Mode.Work일 때 하는 일(채집 GatherJob, 탐험 ExploreJob).</summary>
        public IWorkerJob Job
        {
            get => m_job;
            // 쉴 때 상자 정리(TidyJob)를 다른 일·명령·쉬기로 바꾸면 든 물건을 바로 상자에 되돌린다
            private set { if (m_job is TidyJob t && !ReferenceEquals(value, t)) t.PutBack(); m_job = value; }
        }
        IWorkerJob m_job;

        /// <summary>지금 일이 새 명령에 바로 자리를 내주는 일(묘비 회수·쉴 때 상자 정리) — 대기열에 안 넣고 "~부터 하고 갈게요"도 안 함.</summary>
        public bool Yields => Job is GraveJob || Job is TidyJob;
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
        public void Interrupt()
        {
            if (Current == Mode.Work && Job != null && !Yields)
                Queue.Insert(0, new QueuedWork { Label = Job.Label, Resume = Job, Work = WorkSaves.FromJob(Job) });
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

        public void StartJob(IWorkerJob job)
        {
            Job = job;
            m_escort = null; // 동행은 그 일에만(부르는 쪽이 StartJob 뒤 EscortBy)
            m_guard?.Reset();
            m_choresHeld = false;
            Current = Mode.Work;
        }

        public void Face(Vector3 point) => LookAt(point);

        Escort m_escort;
        ZDOID m_escortId = ZDOID.None;
        /// <summary>지금 일에 같이 가는 동료 일꾼 이름(앞장서는 중), 없으면 null. 시험·!상태용.</summary>
        public string EscortName => m_escort != null ? m_escortName : null;
        string m_escortName;
        /// <summary>시험용: 앞장서기 단계.</summary>
        public Escort.Phase? EscortPhase => m_escort?.Now;

        /// <summary>지금 일을 동료 일꾼과 같이 한다(Departures.GoWithMate): 기다렸다가 앞장서고 20m 안 유지. StartJob 뒤에 부른다.</summary>
        public void EscortBy(ZDOID who, string name, Escort escort = null)
        {
            if (Current != Mode.Work || Job == null || who.IsNone()) return;
            m_escort = escort ?? new Escort();
            m_escortId = who;
            m_escortName = name;
        }

        /// <summary>동행자를 기다려야 하면 true(이번 프레임 일은 쉼).</summary>
        bool EscortHolds(WorkerNpc npc, float dt)
        {
            foreach (var mate in WorkerNpc.All)
                if (mate != npc && mate.GetZDOID() == m_escortId && (mate.Ai.Current != Mode.Follow || mate.Ai.FollowTarget != npc.GetZDOID()))
                {
                    // 동료가 따라오기를 그만둠(다른 일을 받음·죽음 등): 기다리지 않고 혼자 감
                    Plugin.Log.LogInfo($"{npc.NpcName}: 동행 {m_escortName}이 따라오기를 그만둠({mate.Ai.Current}) → 혼자 감");
                    m_escort = null;
                    return false;
                }
            ZDO z = ZDOMan.instance.GetZDO(m_escortId);
            Vector3 pos = z != null ? z.GetPosition() : Vector3.zero;
            float d = z != null ? Vector3.Distance(pos, transform.position) : -1f;
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

        Guard m_guard;
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

        static readonly List<Vector3> s_diagPath = new List<Vector3>();
        /// <summary>진단(막힘 기록용): 여기서 그 점까지 바닐라 길찾기 — 완전/부분(끝이 목표에서 몇 m)/없음.</summary>
        public string PathDiag(Vector3 to)
        {
            if (Pathfinding.instance == null) return "길찾기 없음";
            Vector3 from = transform.position;
            if (!Pathfinding.instance.GetPath(from, to, s_diagPath, m_pathAgentType)) return "길 없음";
            if (s_diagPath.Count == 0) return "길 빈 것";
            Vector3 end = s_diagPath[s_diagPath.Count - 1];
            bool full = Pathfinding.instance.GetPath(from, to, null, m_pathAgentType, requireFullPath: true);
            return $"길 {(full ? "완전" : "부분")}(모퉁이 {s_diagPath.Count}, 끝이 목표에서 {Utils.DistanceXZ(end, to):0.0}m·높이 {end.y - to.y:0.0})";
        }

        /// <summary>시험 기록용: 그쪽으로 3m 안에 걸리는 것(발 0.4m·가슴 1.4m 높이).</summary>
        public string BlockerAhead(Vector3 point)
        {
            Vector3 pos = transform.position, dir = Flat(point - pos).normalized;
            string Hit(float h) => Physics.Raycast(pos + Vector3.up * h, dir, out RaycastHit hit, 3f, SolidMask, QueryTriggerInteraction.Ignore)
                ? $"{hit.collider.name}({LayerMask.LayerToName(hit.collider.gameObject.layer)}, {hit.distance:0.0}m, 위 {hit.collider.transform.root.name})" : "없음";
            return $"앞 발 {Hit(0.4f)} / 가슴 {Hit(1.4f)}, 땅 {pos.y - ZoneSystem.instance.GetGroundHeight(pos):0.0}m, 앞 1m 땅 {ZoneSystem.instance.GetGroundHeight(pos + dir) - pos.y:0.0}m, 둘레 {Around()}";
        }

        /// <summary>진단: 몸 둘레 1.3m 안 단단한 충돌체(이름·종류·루트와의 거리·몸 기준 높이 범위).</summary>
        string Around()
        {
            Vector3 pos = transform.position;
            var cols = Physics.OverlapSphere(pos + Vector3.up * 0.9f, 1.3f, SolidMask, QueryTriggerInteraction.Ignore);
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var c in cols)
            {
                if (c.transform.root == transform.root) continue;
                if (++n > 6) break;
                Bounds b = c.bounds;
                string kind = c is MeshCollider m ? (m.convex ? "메시(볼록)" : "메시") : c.GetType().Name.Replace("Collider", "");
                sb.Append($"[{c.name}:{kind} 루트 {c.transform.root.name} {Utils.DistanceXZ(c.transform.root.position, pos):0.0}m 높이 {b.min.y - pos.y:0.0}~{b.max.y - pos.y:0.0} 크기 {b.size.x:0.0}x{b.size.z:0.0}]");
            }
            return n == 0 ? "없음" : sb.ToString();
        }

        float m_sampleTimer;
        Vector3 m_samplePos;
        float m_detourTime;
        Vector3 m_detourDir;
        int m_detourCount;
        float m_detourSide = 1f;

        /// <summary>깊은 물 앞에서 선 채로 지난 시간. 작업은 이게 WaterGiveUp초를 넘으면 그 목표를 버리고 다른 데를 고른다.</summary>
        public float WaterBlockedTime { get; private set; }
        public const float WaterGiveUp = 3f;
        Vector3 m_moveTarget;
        Vector3 m_lastDry;
        bool m_hasDry;
        /// <summary>지금 앞의 물은 건너기로 했다(좁은 강, WaterRule). 물에서 나오면 풀린다.</summary>
        bool m_swimOk;
        /// <summary>물가에서 내린 판단(건넘/못 건넘)을 이때까지 다시 재지 않는다(앞 32m를 재는 건 비싸다).</summary>
        float m_waterDecidedUntil;

        List<Vector3> m_inPath;
        int m_inIdx;
        static readonly Vector3 NoGoal = new Vector3(1e9f, 0f, 1e9f);
        Vector3 m_inGoal = NoGoal;
        float m_inPlanAt = -100f, m_inBest, m_inProgressAt, m_inDist;

        bool m_inSwimLogged;

        void ForgetInteriorPath() { m_inPath = null; m_inGoal = NoGoal; }

        /// <summary>지하묘지 안 길(InteriorNav)을 따라 걷는 중이고 최근 3초 안에 다음 점에 다가갔다(막힘 판단이 길을 도는 걸음을 막힘으로 안 보게).</summary>
        public bool OnInteriorPath => m_inPath != null && Time.time - m_inProgressAt < 3f;

        /// <summary>지하묘지 안에서 그 점(2m 안)·닿을 거리(0.3m 안)로 마지막에 찾은 묘지 안 길이 없었다(바닐라 걷기로 헤매는 중).</summary>
        public bool InteriorNoPath(Vector3 point, float dist) =>
            m_inPath == null && Character.InInterior(transform.position) && Utils.DistanceXZ(point, m_inGoal) <= 2f && Mathf.Abs(dist - m_inDist) <= 0.3f;

        /// <summary>
        /// 지하묘지 안 걷기: InteriorNav 칸 길을 점마다 따라간다(바닐라 길 지도는 안에서 조각나 길이 늘 "부분"). 다음 점이 턱 위면 뛴다.
        /// 목표가 2m 넘게 바뀌었거나 3초 동안 다음 점에 못 다가가면 지금 자리에서 다시 찾는다(3초에 한 번까지). 길이 없으면 false(바닐라 걷기로).
        /// </summary>
        bool InteriorWalk(Vector3 pos, Vector3 point, float dist, bool run)
        {
            bool newGoal = Utils.DistanceXZ(point, m_inGoal) > 2f || Mathf.Abs(dist - m_inDist) > 0.3f; // 닿을 거리가 바뀌어도(줍기: 손 닿는 거리로) 새로 찾는다
            bool stalled = m_inPath != null && Time.time - m_inProgressAt > 3f;
            bool retry = m_inPath == null; // 못 찾았던 같은 목표: 3초마다 다시
            if (newGoal ? Time.time - m_inPlanAt > 0.5f : (stalled || retry) && Time.time - m_inPlanAt > 3f)
            {
                m_inGoal = point;
                m_inDist = dist;
                m_inPlanAt = Time.time;
                InteriorNav.UseBody(m_character);
                m_inPath = InteriorNav.Plan(pos, point, dist, out string why);
                m_inIdx = m_inPath != null && m_inPath.Count > 1 ? 1 : 0; // 첫 점은 지금 자리
                m_inBest = float.MaxValue;
                if (!stalled) m_inProgressAt = Time.time; // 멈춰서 다시 찾은 길은 실제로 다가가야 진척(막힘 판단이 계속 미뤄지지 않게)
                Plugin.Log.LogInfo($"{((WorkerNpc)m_character).NpcName}: 묘지 안 길 {(m_inPath != null ? "찾음" : "못 찾음")}{(stalled ? "(멈춰서 다시)" : "")} — {why}, 목표까지 {Utils.DistanceXZ(point, pos):0.0}m");
                if (m_inPath == null && newGoal) Plugin.Log.LogInfo($"DIAG 묘지 안 길 없음: {InteriorNav.Explain(pos, point, dist)}, 목표 ({point.x - pos.x:0.0},{point.y - pos.y:0.0},{point.z - pos.z:0.0})");
            }
            if (m_inPath == null) return false;
            while (m_inIdx < m_inPath.Count - 1 && Utils.DistanceXZ(m_inPath[m_inIdx], pos) < 0.5f) { m_inIdx++; m_inBest = float.MaxValue; m_inProgressAt = Time.time; }
            Vector3 wp = m_inPath[m_inIdx];
            float dw = Utils.DistanceXZ(wp, pos);
            if (m_inBest == float.MaxValue) m_inBest = dw; // 새 점: 지금 거리가 기준
            else if (dw < m_inBest - 0.3f) { m_inBest = dw; m_inProgressAt = Time.time; }
            bool last = m_inIdx == m_inPath.Count - 1 && dw < 0.5f;
            MoveTowards(Flat(last ? point - pos : wp - pos), run);
            if (wp.y - pos.y > (float)StepGrid.StepUp && dw < 1.4f) TryJump(); // 턱 위 점: 뛰어 오른다
            return true;
        }

        /// <summary>
        /// 걷기: 바닐라 길찾기(BaseAI.MoveTo, 물 뺀 길)를 쓰되
        /// - 지하묘지 안은 InteriorNav 칸 길(InteriorWalk).
        /// - 길을 못 찾으면(처음 가는 곳은 길 지도를 만드는 중일 때가 많다) 서 있지 말고 곧장 걸어간다.
        /// - 1.5초 동안 거의 못 움직였으면(앞을 막은 생물·장애물) 1.2초 옆으로 비켜 간다.
        /// - 앞이 깊은 물이면 좁은 강만 헤엄쳐 건너고, 늪·바다·긴 물이면 멈춘다(WaterBlockedTime). 모르고 빠졌으면 마른 땅으로 돌아간다.
        /// 도착하면 true.
        /// </summary>
        public bool MoveToPoint(float dt, Vector3 point, float dist, bool run)
        {
            Vector3 pos = transform.position;
            // 목표가 바뀌면 물가에서 막혀 있던 시간은 새로 센다(0.3.7: 물러날 때 막힌 시간이 남아 도망이 시작하자마자 끝남)
            if (Utils.DistanceXZ(point, m_moveTarget) > 2f) { WaterBlockedTime = 0f; m_moveTarget = point; m_detourCount = 0; }
            float d = Utils.DistanceXZ(point, pos);
            if (InteriorNav.Reached(pos, point, dist)) { StopMoving(); m_detourTime = 0f; m_detourCount = 0; WaterBlockedTime = 0f; m_carving = false; m_pitSince = 0f; ForgetInteriorPath(); return true; }
            if (EdgeRescueIfLong(dt, pos)) return false;
            if (m_carving && Time.time - m_carveSeen > 1f) m_carving = false; // 깎다가 다른 일로 넘어갔다
            if (m_carving && UpdateCarve(dt, pos)) return false;
            if (RescueIfTrapped(dt, pos)) return false;
            bool inPath = Character.InInterior(pos) && m_inPath != null; // 지하묘지 안 길(헤엄 칸 포함)을 따라가는 중
            if (m_character.IsSwimming() && Character.InInterior(pos) && !inPath) // 진단: 묘지 안 길 없이(바닐라 걷기로) 물에 빠짐
            {
                if (!m_inSwimLogged)
                {
                    m_inSwimLogged = true;
                    Plugin.Log.LogInfo($"DIAG 묘지 안 길 밖 헤엄: 수면 {m_character.GetLiquidLevel() - pos.y:+0.00}, 발밑 {InteriorNav.CellDiag(pos, pos.y)}, 마른 자리 {(m_hasDry ? Utils.DistanceXZ(m_lastDry, pos).ToString("0.0") + "m 높이 " + (m_lastDry.y - pos.y).ToString("+0.0;-0.0") : "없음")}");
                }
            }
            else m_inSwimLogged = false;
            if (!m_character.InLiquid()) { m_lastDry = pos; m_hasDry = true; if (Time.time > m_waterDecidedUntil) m_swimOk = false; }
            else if (m_character.IsSwimming() && !m_swimOk && m_hasDry && !inPath && !TarGoal(point)) // 줍는 타르는 웅덩이 안에 떠 있다(그때만 헤엄쳐 감). 묘지 안 길이 헤엄 칸을 지나면 그대로 헤엄쳐 간다(회차 136: 돌아가기를 되풀이해 20초 갇힘)
            {
                MoveTowards(Flat(m_lastDry - pos), true);
                return false;
            }
            if (TarStep(dt, pos, point, dist)) return false;
            if (m_detourTime > 0f)
            {
                m_detourTime -= dt;
                if (!WaterAhead(pos, m_detourDir) && !TarAhead(pos, m_detourDir, point)) MoveTowards(m_detourDir, true); // 걷기(1.6m/s)로는 큰 바위를 못 돌아감 → 달림
                else m_detourTime = 0f;
                return false;
            }
            bool inside = Character.InInterior(pos);
            if (!inside) ForgetInteriorPath();
            if (!inside || !InteriorWalk(pos, point, dist, run))
            {
                bool done = MoveTo(dt, point, dist, run);
                if (done || !FoundPath()) MoveTowards(Flat(point - pos), run);
            }
            if (WaterAhead(pos, m_character.GetMoveDir()))
            {
                StopMoving();
                if (WaterBlockedTime < WaterGiveUp && WaterBlockedTime + dt >= WaterGiveUp)
                    Brain.Places.AddFeature(Senses.ZoneOf(point), PlaceFeature.DeepWater, ZNet.instance.GetTimeSeconds()); // 장소 기억: 물에 막힘
                WaterBlockedTime += dt;
                return false;
            }
            WaterBlockedTime = 0f;
            if (TarAhead(pos, m_character.GetMoveDir(), point)) { StartTarDetour(pos, point); return false; }
            // 바위·통나무·구덩이 벽 같은 턱에 걸려 섰으면 뛴다(플레이어처럼)
            m_ledgeTimer -= dt;
            if (m_ledgeTimer <= 0f)
            {
                m_ledgeTimer = 0.25f;
                if (Flat(m_character.GetVelocity()).magnitude < 1f && LedgeAhead(pos, m_character.GetMoveDir())) TryJump();
            }
            m_sampleTimer += dt;
            if (m_sampleTimer >= 1.5f)
            {
                if (Utils.DistanceXZ(m_samplePos, pos) < 0.75f && d > dist + 1f)
                {
                    // 막혔다: 두 번까지 뛰어 보고, 구덩이에 갇혔으면 경사로를 깎고, 아니면 옆으로 비켜 간다
                    Vector3 fwd = Flat(point - pos).normalized;
                    if (m_pitSince <= 0f && Climb.IsPit(Rise8(pos, fwd))) MarkPit(pos);
                    if (m_stuckJumps < 2 && m_detourCount == 0 && TryJump()) m_stuckJumps++; // 이미 비켜 가 봤으면 점프는 건너뜀
                    else if (!StartCarve(pos, fwd))
                    {
                        m_stuckJumps = 0;
                        // 같은 목표로 또 막히면 같은 쪽으로 점점 길게 비켜 간다(큰 바위는 1.2초로 못 돌아감, 회차 72 coppertest).
                        // 네 번 해도 안 되면 반대쪽부터 다시.
                        if (m_detourCount >= 4) { m_detourSide = -m_detourSide; m_detourCount = 0; }
                        else if (m_detourCount == 0) m_detourSide = Random.value < 0.5f ? 1f : -1f;
                        m_detourCount++;
                        Vector3 side = Vector3.Cross(Vector3.up, fwd) * m_detourSide;
                        m_detourDir = (side + fwd * 0.3f).normalized;
                        m_detourTime = 1.2f * m_detourCount;
                        if (m_detourCount >= 2) Plugin.Log.LogInfo($"DIAG 비켜 가기 {m_detourCount}번째: 쪽 {m_detourSide:0} {m_detourTime:0.0}초, 목표까지 {d:0.0}m, 앞 {BlockerAhead(point)}");
                    }
                }
                else m_stuckJumps = 0;
                m_sampleTimer = 0f;
                m_samplePos = pos;
            }
            return false;
        }

        // ---- 점프·구덩이 (2026-10-21 사용자 관찰: 점프를 안 해서 돌 캐다 판 구덩이에서 못 나옴) ----

        /// <summary>시험 기록: 뛴 횟수, 경사로를 깎은 곡괭이질 수.</summary>
        public int Jumps { get; private set; }
        public int CarveSwings { get; private set; }
        /// <summary>시험용: 점프·경사로 깎기를 끄고 걷기만으로 나오는지 본다.</summary>
        public static bool TestNoJump, TestNoCarve;
        /// <summary>시험 기록: 길이 막혀 거점·상자로 순간이동한 횟수(임시 대책, 0이어야 좋다).</summary>
        public static int StuckTeleports;

        /// <summary>이때까지는 이 일꾼의 곡괭이질이 땅을 판다(광석 캐기·구덩이 탈출). 그 밖에는 안 판다(NpcPatches.NoDig).</summary>
        public float DigAllowedUntil;
        public bool DigAllowed => Time.time < DigAllowedUntil;
        /// <summary>이 일꾼이 판 횟수(NpcPatches.NoDig가 센다). 채집은 이것도 진척으로 본다.</summary>
        public int Digs;
        /// <summary>땅에 먼저 닿았는데 파기를 막은 횟수(그 휘두름은 바위에 안 들어감).</summary>
        public int BlockedDigs;

        float m_lastJump = -10f;
        float m_ledgeTimer;
        /// <summary>막혀서 뛴 횟수(진척이 있으면 0).</summary>
        int m_stuckJumps;
        static int s_solidMask;

        static int SolidMask => s_solidMask != 0 ? s_solidMask
            : (s_solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"));

        /// <summary>바닐라 점프(일꾼은 몸 앞쪽으로 뛴다). 땅에 서 있을 때만, 0.8초에 한 번.</summary>
        bool TryJump()
        {
            if (TestNoJump || Time.time - m_lastJump < 0.8f || !m_character.IsOnGround() || m_character.InAttack()) return false;
            m_lastJump = Time.time;
            m_character.Jump();
            Jumps++;
            return true;
        }

        /// <summary>앞 1m 안에 무릎 높이 턱(가파른 면)이 있고, 뛰면 닿는 높이(1.4m)는 비었는지.</summary>
        bool LedgeAhead(Vector3 pos, Vector3 dir)
        {
            dir = Flat(dir);
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();
            if (!Physics.Raycast(pos + Vector3.up * 0.4f, dir, out RaycastHit low, 1f, SolidMask, QueryTriggerInteraction.Ignore)) return false;
            if (low.normal.y > 0.7f) return false; // 완만한 비탈은 걸어 오른다
            return !Physics.Raycast(pos + Vector3.up * 1.4f, dir, 1.4f, SolidMask, QueryTriggerInteraction.Ignore);
        }

        bool m_carving;
        /// <summary>구덩이에서 경사로를 깎는 중(제자리라도 막힌 게 아니다).</summary>
        public bool Carving => m_carving;
        float m_carveSeen;
        Vector3 m_carveDir;
        bool m_carveSwung;
        float m_carveWalk;
        int m_carveCount;
        float m_carveStart;
        float m_noCarveUntil;
        const int CarveMax = 30;

        /// <summary>8방향(0 = toward, 45°씩) 둘레 땅이 발보다 얼마나 높은지: 1.5·3·4.5m 중 가장 높은 값.</summary>
        static double[] Rise8(Vector3 pos, Vector3 toward)
        {
            var rise = new double[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, 45f * i, 0f) * toward;
                float best = -99f;
                for (int s = 1; s <= 3; s++)
                    if (ZoneSystem.instance.GetGroundHeight(pos + dir * (1.5f * s), out float h)) best = Mathf.Max(best, h - pos.y);
                rise[i] = best;
            }
            return rise;
        }

        /// <summary>
        /// 구덩이에 갇혔으면(Climb.IsPit) 곡괭이로 가려는 쪽 가장 낮은 벽에 경사로를 깎기 시작한다.
        /// 곡괭이가 없거나 구덩이가 아니면 false(그냥 언덕·절벽은 길 만들기라 시킬 때만, 구상서 6-5).
        /// </summary>
        bool StartCarve(Vector3 pos, Vector3 toward)
        {
            if (TestNoCarve || Time.time < m_noCarveUntil || toward.sqrMagnitude < 0.01f) return false;
            if (WorkerNpc.BestTool(((WorkerNpc)m_character).GetInventory(), ToolKind.Pickaxe, true) == null) return false;
            double[] rise = Rise8(pos, toward);
            if (!Climb.IsPit(rise)) return false;
            int k = Climb.CarveDir(rise);
            m_carveDir = Quaternion.Euler(0f, 45f * k, 0f) * toward;
            m_carving = true;
            m_carveSwung = false;
            m_carveWalk = 0f;
            m_carveCount = 0;
            m_carveStart = m_carveSeen = Time.time;
            MarkPit(pos);
            Brain.Places.AddFeature(Senses.ZoneOf(pos), PlaceFeature.Pit, ZNet.instance.GetTimeSeconds()); // 장소 기억: 구덩이
            Plugin.Log.LogInfo($"{((WorkerNpc)m_character).NpcName}: 구덩이에 갇힘(둘레 {string.Join(" ", System.Array.ConvertAll(rise, r => r.ToString("0.0")))}) → 경사로 깎기");
            return true;
        }

        /// <summary>
        /// 경사로 깎기 한 번: 벽 쪽(발 위 1m)으로 곡괭이질(바닐라: 맞은 곳보다 조금 아래로 땅이 깎임) → 1.2초 올라가 본다 →
        /// 아직 구덩이면 다시. 다 나왔거나 30번이면 끝(끝나면 1분 동안은 다시 시작하지 않는다). true면 이번 프레임은 깎기가 맡았다.
        /// </summary>
        bool UpdateCarve(float dt, Vector3 pos)
        {
            var npc = (WorkerNpc)m_character;
            m_carveSeen = Time.time;
            if (m_carveWalk > 0f)
            {
                m_carveWalk -= dt;
                MoveTowards(m_carveDir, false);
                if (m_carveWalk > 0f) return true;
                double[] rise = Rise8(pos, m_carveDir);
                if (!Climb.IsPit(rise) || m_carveCount >= CarveMax)
                {
                    Plugin.Log.LogInfo($"{npc.NpcName}: 경사로 깎기 끝({(Climb.IsPit(rise) ? "못 나옴" : "나옴")}, 곡괭이질 {m_carveCount}번, {Time.time - m_carveStart:0}초)");
                    m_carving = false;
                    m_stuckJumps = 0;
                    if (Climb.IsPit(rise)) m_noCarveUntil = Time.time + 60f;
                    return false;
                }
                return true;
            }
            if (!npc.HoldTool(ToolKind.Pickaxe, true)) { m_carving = false; return false; }
            StopMoving();
            Vector3 aim = pos + m_carveDir * 1.2f + Vector3.up * 1f;
            LookAt(aim);
            bool attacking = npc.InAttack();
            if (m_carveSwung && !attacking)
            {
                m_carveSwung = false;
                m_carveCount++;
                CarveSwings++;
                m_carveWalk = 1.2f; // 깎인 데로 올라가 본다
                return true;
            }
            if (!attacking && IsLookingAt(aim, 20f))
            {
                DigAllowedUntil = Time.time + 1.5f;
                if (npc.StartAttack(null, false)) m_carveSwung = true;
            }
            return true;
        }


        Vector3 m_trapAnchor;
        float m_trapStill, m_trapCheck;
        /// <summary>시험용: 갇혀서(도망 중 구덩이·물 찬 구덩이) 깃발로 순간이동한 횟수.</summary>
        public static int TrapRescues;

        /// <summary>
        /// 갇힘 구조(사용자 결정 2026-10-22, Core Climb.TrapRescue): 도망 중 구덩이에 갇혀 6초, 또는 헤엄만 치며 20초 동안 3m 안을 못 벗어나면
        /// 깃발로 순간이동한다(체력·짐·장비 그대로, 도망은 그만둠). 그냥 걷다 빠진 구덩이는 점프·경사로 깎기가 맡는다. true면 옮겼다.
        /// </summary>
        bool RescueIfTrapped(float dt, Vector3 pos)
        {
            if (Utils.DistanceXZ(pos, m_trapAnchor) > (float)Climb.TrapRadius) { m_trapAnchor = pos; m_trapStill = 0f; return false; }
            m_trapStill += dt;
            if (m_trapStill < Mathf.Min((float)Climb.FleeTrapSeconds, (float)Climb.SwimTrapSeconds)) return false;
            m_trapCheck -= dt;
            if (m_trapCheck > 0f) return false;
            m_trapCheck = 0.5f;
            bool fleeing = m_guard != null && m_guard.Fleeing;
            bool swimming = m_character.IsSwimming();
            Vector3 toward = Flat(m_moveTarget - pos);
            if (toward.sqrMagnitude < 0.01f) toward = Flat(transform.forward);
            bool pit = fleeing && !swimming && Climb.IsPit(Rise8(pos, toward));
            string why = Climb.TrapRescue(fleeing, pit, swimming, m_trapStill);
            if (why == null) return false;
            var npc = (WorkerNpc)m_character;
            Plugin.Log.LogInfo($"{npc.NpcName}: {(why == "water" ? "물 찬 구덩이에서 헤엄만 함" : "도망 중 구덩이에 갇힘")} {m_trapStill:0}초 → 깃발로 순간이동");
            m_guard?.CancelFlee();
            m_carving = false;
            StopMoving();
            npc.TeleportTo(npc.Home, transform.rotation, false);
            m_trapAnchor = npc.Home;
            m_trapStill = 0f;
            m_pitSince = 0f; // 구덩이 시간도 새로(회차 122: 도망 구덩이 시간이 이어져 다음 구덩이에서 24초 만에 옮김)
            StuckTeleports++;
            TrapRescues++;
            npc.Say("stuck");
            return true;
        }

        float m_pitSince, m_pitSeen, m_pitCheck;
        Vector3 m_pitPos;
        static int s_edgeMask;
        /// <summary>시험용: 구덩이에서 30초 못 나와 가장자리 위로 옮긴 횟수.</summary>
        public static int PitEdgeRescues;
        /// <summary>시험용: 구덩이 가장자리 옮기기를 끈다(점프·경사로만 보는 칸).</summary>
        public static bool TestNoEdgeRescue;

        /// <summary>걷다 구덩이에 갇힌 것을 처음 본 때를 적는다(이미 적었으면 그대로).</summary>
        void MarkPit(Vector3 pos)
        {
            if (m_pitSince > 0f) return;
            m_pitSince = m_pitSeen = Time.time;
            m_pitPos = pos;
            m_pitCheck = 0f;
        }

        /// <summary>
        /// 구덩이 탈출 안전장치(사용자 결정 2026-10-23, E018): 걷다 빠진 구덩이에서 점프·경사로 깎기를 해도 15초(Climb.PitEscapeSeconds) 안에 못 나오면
        /// 가장 가까운 가장자리 위(Climb.EdgeSpots — 단단한 지형·물 아님·몸 자리 빔·위험 판단 통과)로 옮긴다. 플레이어 근처라도. 짐·체력·일은 그대로.
        /// 자리가 없으면 깃발로. 도망 구덩이(6초 → 깃발)는 RescueIfTrapped가 먼저 맡는다. true면 옮겼다.
        /// </summary>
        bool EdgeRescueIfLong(float dt, Vector3 pos)
        {
            if (m_pitSince <= 0f) return false;
            if (TestNoEdgeRescue) { m_pitSince = 0f; return false; } // 꺼 둔 동안 시간을 쌓지 않는다(켜자마자 옮기지 않게)
            if (Time.time - m_pitSeen > 5f || Utils.DistanceXZ(pos, m_pitPos) > (float)Climb.PitLeftRadius) { m_pitSince = 0f; return false; }
            m_pitSeen = Time.time;
            if (Time.time - m_pitSince < (float)Climb.PitEscapeSeconds || m_character.IsSwimming()) return false;
            m_pitCheck -= dt;
            if (m_pitCheck > 0f) return false;
            m_pitCheck = 0.5f;
            Vector3 toward = Flat(m_moveTarget - pos);
            if (toward.sqrMagnitude < 0.01f) toward = Flat(transform.forward);
            toward.Normalize();
            if (!Climb.IsPit(Rise8(pos, toward))) { m_pitSince = 0f; return false; } // 나왔다(경사로 위)
            var npc = (WorkerNpc)m_character;
            float stuck = Time.time - m_pitSince;
            var rise = new double[8][];
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, 45f * i, 0f) * toward;
                rise[i] = new double[Climb.EdgeSteps];
                for (int k = 0; k < Climb.EdgeSteps; k++)
                    rise[i][k] = ZoneSystem.instance.GetGroundHeight(pos + dir * ((k + 1) * (float)Climb.EdgeStep), out float h) ? h - pos.y : double.NaN;
            }
            if (s_edgeMask == 0) s_edgeMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
            string last = "후보 없음";
            int tried = 0;
            foreach (var e in Climb.EdgeSpots(rise))
            {
                tried++;
                Vector3 dir = Quaternion.Euler(0f, 45f * e.Dir, 0f) * toward;
                Vector3 p = pos + dir * ((e.Step + 1) * (float)Climb.EdgeStep + 0.5f); // 가장자리 끝에서 반 m 더 바깥
                if (!ZoneSystem.instance.GetSolidHeight(p, out float h, out Vector3 normal, out GameObject go) || go == null) { last = "땅 없음"; continue; }
                if (go.GetComponentInParent<Heightmap>() == null) { last = "지형 아님(" + go.name + ")"; continue; }
                if (normal.y < StuckHop.MinNormalY) { last = $"가파름({normal.y:0.00})"; continue; }
                p.y = h;
                if (h < pos.y + (float)Climb.Wall) { last = "낮음"; continue; }
                if (Climb.IsPit(Rise8(p, dir))) { last = "아직 구덩이 안"; continue; }
                if (h < ZoneSystem.instance.m_waterLevel + 0.3f || Floating.GetLiquidLevel(p) > h + 0.1f) { last = "물"; continue; }
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.45f, s_edgeMask, QueryTriggerInteraction.Ignore)) { last = "몸 자리 막힘"; continue; }
                if (m_guard != null && !m_guard.SafeAt(p)) { last = "위험"; continue; }
                m_carving = false;
                StopMoving();
                if (!npc.TeleportTo(p + Vector3.up * 0.2f, transform.rotation, false)) { last = "옮기기 실패"; continue; }
                Plugin.Log.LogInfo($"{npc.NpcName}: 구덩이에서 {stuck:0}초 못 나옴 → 가장자리 위로 {Utils.DistanceXZ(pos, p):0.0}m 옮김(위 {h - pos.y:0.0}m, 후보 {tried}번째)");
                AfterPitRescue(p);
                PitEdgeRescues++;
                npc.Say("rescue");
                return true;
            }
            Plugin.Log.LogInfo($"{npc.NpcName}: 구덩이에서 {stuck:0}초 못 나옴, 가장자리 자리 없음(후보 {tried}개, 마지막: {last}) → 깃발로 순간이동");
            m_carving = false;
            StopMoving();
            npc.TeleportTo(npc.Home, transform.rotation, false);
            AfterPitRescue(npc.Home);
            npc.Say("stuck");
            return true;
        }

        void AfterPitRescue(Vector3 at)
        {
            m_pitSince = 0f;
            m_trapAnchor = at;
            m_trapStill = 0f;
            m_stuckJumps = 0;
            m_detourTime = 0f;
            m_detourCount = 0;
            m_samplePos = at;
            m_sampleTimer = 0f;
            StuckTeleports++;
        }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ---- 타르 웅덩이 (⑫ 평원 바): 빠지면 Tared(느려짐)라 걸어 들어가지 않고, 빠졌으면 마지막 타르 밖 자리로 돌아간다 ----

        /// <summary>이보다 깊은 타르(m)는 걸어 들어가지 않는다(무릎 아래 가장자리는 지나감).</summary>
        public const float TarWade = 0.4f;
        /// <summary>시험 기록: 타르를 비켜 간 횟수, TarWade보다 깊은 타르에 있던 시간(초).</summary>
        public int TarDetours { get; private set; }
        public float TarTime { get; private set; }
        Vector3 m_lastNoTar;
        bool m_hasNoTar, m_tarLogged;
        Vector3 m_tarTarget;
        float m_tarSide = 1f;

        /// <summary>
        /// 그 자리(xz) 땅 위 타르 깊이(m), 없으면 0. 타르(LiquidVolume)는 평원에만 있다. 바닐라 Floating.GetLiquidLevel(타르만)
        /// — 웅덩이 충돌체는 볼록 껍질이라 땅 0.2m 위 점으로 물으면 웅덩이 안에서 잡히고, 타르가 없는 칸은 표면이 땅보다 낮게 나온다(LiquidVolume.GetSurface).
        /// </summary>
        public static float TarDepth(Vector3 p)
        {
            if (Character.InInterior(p) || WorldGenerator.instance == null || WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.Plains) return 0f;
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGroundHeight(p, out float g)) return 0f;
            float tar = Floating.GetLiquidLevel(new Vector3(p.x, g + 0.2f, p.z), 1f, LiquidType.Tar);
            return tar < -1000f ? 0f : Mathf.Max(0f, tar - g);
        }

        /// <summary>깊은 타르에 빠졌으면 마지막 타르 밖 자리로 달려 나온다(목표가 타르 안이거나 바로 옆이면 그대로). 나오는 중이면 true.</summary>
        bool TarStep(float dt, Vector3 pos, Vector3 point, float dist)
        {
            float here = TarDepth(pos);
            if (here < 0.1f) { m_lastNoTar = pos; m_hasNoTar = true; m_tarLogged = false; return false; }
            if (here <= TarWade) return false;
            TarTime += dt;
            if (!m_hasNoTar || TarGoal(point) || Utils.DistanceXZ(point, pos) <= dist + 1f) return false;
            if (!m_tarLogged)
            {
                m_tarLogged = true;
                Plugin.Log.LogInfo($"{(m_character as WorkerNpc)?.NpcName}: 타르 {here:0.0}m에 빠짐 → 타르 밖 {Utils.DistanceXZ(m_lastNoTar, pos):0.0}m로 나옴");
            }
            MoveTowards(Flat(m_lastNoTar - pos), true);
            return true;
        }

        /// <summary>그 방향 1.5m 앞이 깊은 타르인지(목표가 2m 안이면 안 봄 — 가장자리 타르를 주우러 감).</summary>
        bool TarAhead(Vector3 pos, Vector3 dir, Vector3 point)
        {
            dir = Flat(dir);
            if (dir.sqrMagnitude < 0.01f || Utils.DistanceXZ(point, pos) < 2f || TarGoal(point)) return false;
            return TarDepth(pos + dir.normalized * 1.5f) > TarWade;
        }

        Vector3 m_tarGoalAt = new Vector3(1e7f, 0f, 1e7f);
        bool m_tarGoal;

        /// <summary>
        /// 가는 곳 자체가 깊은 타르 안인지(줍는 타르 Pickable_Tar는 웅덩이 1.3~3.8m 타르 위에 떠 있다 — 회차 174 vh_tartest, 플레이어도 들어가서 줍는다).
        /// 그때만 타르에 들어가고, 지나가는 길이면 피한다. 목표가 0.5m 넘게 바뀔 때만 다시 잰다.
        /// </summary>
        bool TarGoal(Vector3 point)
        {
            if (Utils.DistanceXZ(point, m_tarGoalAt) > 0.5f) { m_tarGoalAt = point; m_tarGoal = TarDepth(point) > TarWade; }
            return m_tarGoal;
        }

        /// <summary>앞이 타르: 같은 목표면 같은 쪽으로(가장자리를 따라 돎) 비켜 간다. 처음엔 타르가 얕은 쪽을 고른다.</summary>
        void StartTarDetour(Vector3 pos, Vector3 point)
        {
            Vector3 fwd = Flat(point - pos).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            if (Utils.DistanceXZ(point, m_tarTarget) > 2f)
            {
                m_tarTarget = point;
                m_tarSide = TarDepth(pos + right * 3f + fwd * 1.5f) <= TarDepth(pos - right * 3f + fwd * 1.5f) ? 1f : -1f;
            }
            for (int flip = 0; flip < 2; flip++)
            {
                Vector3 side = right * m_tarSide;
                foreach (Vector3 dir in new[] { (side + fwd * 0.3f).normalized, side, (side - fwd * 0.5f).normalized })
                    if (!TarAhead(pos, dir, point)) { m_detourDir = dir; m_detourTime = 0.8f; TarDetours++; return; }
                m_tarSide = -m_tarSide;
            }
            m_detourDir = -fwd; // 앞뒤 옆이 다 타르: 뒤로
            m_detourTime = 0.8f;
            TarDetours++;
        }

        /// <summary>그 방향 2.5m 앞이 깊은 물이고 건너면 안 되는 물인지. 건널 물(좁은 강)이면 m_swimOk를 켠다.</summary>
        bool WaterAhead(Vector3 pos, Vector3 dir)
        {
            // Scout.WaterDepth는 바깥 지형·바다 높이로 잰다 — 지하묘지 안(y+5000)이면 그 자리 바깥 늪물을 앞 물로 본다. 안의 물은 InteriorNav가 헤엄 칸으로 다룬다.
            if (Character.InInterior(pos)) return false;
            dir = Flat(dir);
            if (m_swimOk || dir.sqrMagnitude < 0.01f) return false;
            if (Time.time < m_waterDecidedUntil) return true; // 방금 못 건넌다고 봤다
            dir.Normalize();
            if (Scout.WaterDepth(pos + dir * 2.5f) <= (float)WaterRule.DeepDepth) return false;
            // 물가: 앞으로 MaxSwim보다 조금 더 내다봐서 그 안에 땅이 나오면(좁은 강) 건넌다
            m_swimOk = Scout.CanWalk(pos, pos + dir * ((float)WaterRule.MaxSwim + 8f));
            m_waterDecidedUntil = Time.time + (m_swimOk ? 3f : 0.5f);
            return !m_swimOk;
        }

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
                if (ev != null && Utils.DistanceXZ(transform.position, ev.m_pos) < ev.m_eventRange && Utils.DistanceXZ(transform.position, npc.Home) > 30f)
                {
                    npc.Say("raid");
                    GoHome(false);
                    return true;
                }
            }
            GuardBusy = m_guard.Update(dt);
            if (GuardBusy) return true; // 싸움·물러나기·도망·미드 중: 일·따라가기는 쉰다
            switch (Current)
            {
                case Mode.Work:
                    if (Job != null && m_escort != null && EscortHolds(npc, dt)) break;
                    if (Job == null || !Job.Update(dt))
                    {
                        Job = null;
                        m_escort = null;
                        if (!NextQueued()) GoHome(false);
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
                        if (lead.Ai.Current == Mode.Idle || lead.Ai.Current == Mode.Home || lead.Ai.EscortName != npc.NpcName) { GoHome(false); break; }
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
                    StopMoving();
                    if (WorkerBanter.Facing(npc, out Vector3 faceTo)) LookAt(faceTo);
                    if (Queue.Count > 0 && NextQueued()) break; // 직접 명령 > 묘비 회수(구상서 11장 우선순위)
                    Chores(npc, dt);
                    break;
            }
            return true;
        }

        // ---- 죽은 뒤 (구상서 9장) ----

        /// <summary>!멈춰로 쉬라고 했다: 다음 명령이나 다음 죽음 전까지 묘비 회수를 스스로 시작하지 않는다.</summary>
        bool m_choresHeld;
        float m_choreTimer = 3f;
        bool m_saidGraveNight;

        public void HoldChores() => m_choresHeld = true;

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
