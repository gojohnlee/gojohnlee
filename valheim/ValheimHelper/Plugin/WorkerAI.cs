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
        public IWorkerJob Job { get; private set; }
        float m_senseTimer;

        public Mode Current { get; private set; } = Mode.Idle;
        public string FollowName { get; private set; }

        ZDOID m_follow = ZDOID.None;
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
            if (Current == Mode.Work && Job != null && !(Job is GraveJob))
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
            m_guard?.Reset();
            m_choresHeld = false;
            Current = Mode.Work;
        }

        public void Face(Vector3 point) => LookAt(point);

        Guard m_guard;
        /// <summary>위험 감지·싸움(구상서 8장). 시험·!상태용.</summary>
        public Guard Guard => m_guard;

        /// <summary>Guard: 거기는 위험하다 → 하던 일이 그곳을 버린다.</summary>
        public void AvoidHere(Vector3 danger) => Job?.AvoidHere(danger);

        /// <summary>시험 기록용: 마지막 길찾기가 길을 찾았는지.</summary>
        public bool PathFound => FoundPath();

        /// <summary>시험 기록용: 걷기 판단 상태.</summary>
        public string MoveState => $"깎기 {m_carving}(곡괭이질 {m_carveCount}, 걷기 {m_carveWalk:0.0}), 막혀서 뜀 {m_stuckJumps}, 비켜 가기 {m_detourTime:0.0}, " +
            $"물 막힘 {WaterBlockedTime:0.0}, 길 {FoundPath()}, 경계 {m_guard?.StateName}, 공격 중 {m_character.InAttack()}";

        float m_sampleTimer;
        Vector3 m_samplePos;
        float m_detourTime;
        Vector3 m_detourDir;

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

        /// <summary>
        /// 걷기: 바닐라 길찾기(BaseAI.MoveTo, 물 뺀 길)를 쓰되
        /// - 길을 못 찾으면(처음 가는 곳은 길 지도를 만드는 중일 때가 많다) 서 있지 말고 곧장 걸어간다.
        /// - 1.5초 동안 거의 못 움직였으면(앞을 막은 생물·장애물) 1.2초 옆으로 비켜 간다.
        /// - 앞이 깊은 물이면 좁은 강만 헤엄쳐 건너고, 늪·바다·긴 물이면 멈춘다(WaterBlockedTime). 모르고 빠졌으면 마른 땅으로 돌아간다.
        /// 도착하면 true.
        /// </summary>
        public bool MoveToPoint(float dt, Vector3 point, float dist, bool run)
        {
            Vector3 pos = transform.position;
            // 목표가 바뀌면 물가에서 막혀 있던 시간은 새로 센다(0.3.7: 물러날 때 막힌 시간이 남아 도망이 시작하자마자 끝남)
            if (Utils.DistanceXZ(point, m_moveTarget) > 2f) { WaterBlockedTime = 0f; m_moveTarget = point; }
            float d = Utils.DistanceXZ(point, pos);
            if (d <= dist) { StopMoving(); m_detourTime = 0f; WaterBlockedTime = 0f; m_carving = false; return true; }
            if (m_carving && Time.time - m_carveSeen > 1f) m_carving = false; // 깎다가 다른 일로 넘어갔다
            if (m_carving && UpdateCarve(dt, pos)) return false;
            if (!m_character.InLiquid()) { m_lastDry = pos; m_hasDry = true; if (Time.time > m_waterDecidedUntil) m_swimOk = false; }
            else if (m_character.IsSwimming() && !m_swimOk && m_hasDry)
            {
                MoveTowards(Flat(m_lastDry - pos), true);
                return false;
            }
            if (m_detourTime > 0f)
            {
                m_detourTime -= dt;
                if (!WaterAhead(pos, m_detourDir)) MoveTowards(m_detourDir, run);
                else m_detourTime = 0f;
                return false;
            }
            bool done = MoveTo(dt, point, dist, run);
            if (done || !FoundPath()) MoveTowards(Flat(point - pos), run);
            if (WaterAhead(pos, m_character.GetMoveDir()))
            {
                StopMoving();
                if (WaterBlockedTime < WaterGiveUp && WaterBlockedTime + dt >= WaterGiveUp)
                    Brain.Places.AddFeature(Senses.ZoneOf(point), PlaceFeature.DeepWater, ZNet.instance.GetTimeSeconds()); // 장소 기억: 물에 막힘
                WaterBlockedTime += dt;
                return false;
            }
            WaterBlockedTime = 0f;
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
                    if (m_stuckJumps < 2 && TryJump()) m_stuckJumps++;
                    else if (!StartCarve(pos, fwd))
                    {
                        m_stuckJumps = 0;
                        Vector3 side = Vector3.Cross(Vector3.up, fwd) * (Random.value < 0.5f ? 1f : -1f);
                        m_detourDir = (side + fwd * 0.3f).normalized;
                        m_detourTime = 1.2f;
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
            if (WorkerNpc.BestTool(((WorkerNpc)m_character).GetInventory(), ToolKind.Pickaxe) == null) return false;
            double[] rise = Rise8(pos, toward);
            if (!Climb.IsPit(rise)) return false;
            int k = Climb.CarveDir(rise);
            m_carveDir = Quaternion.Euler(0f, 45f * k, 0f) * toward;
            m_carving = true;
            m_carveSwung = false;
            m_carveWalk = 0f;
            m_carveCount = 0;
            m_carveStart = m_carveSeen = Time.time;
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
            if (!npc.HoldTool(ToolKind.Pickaxe)) { m_carving = false; return false; }
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

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>그 방향 2.5m 앞이 깊은 물이고 건너면 안 되는 물인지. 건널 물(좁은 강)이면 m_swimOk를 켠다.</summary>
        bool WaterAhead(Vector3 pos, Vector3 dir)
        {
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
            m_follow = ZDOID.None;
            Current = Mode.Home;
            m_stuckTimer = 0f;
        }

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
            if (m_guard.Update(dt)) return true; // 싸움·물러나기·도망·미드 중: 일·따라가기는 쉰다
            switch (Current)
            {
                case Mode.Work:
                    if (Job == null || !Job.Update(dt))
                    {
                        Job = null;
                        if (!NextQueued()) GoHome(false);
                    }
                    break;
                case Mode.Follow:
                {
                    ZDO target = m_follow.IsNone() ? null : ZDOMan.instance.GetZDO(m_follow);
                    if (target == null) { Lost(npc); break; }
                    Vector3 pos = target.GetPosition();
                    float d = Vector3.Distance(pos, transform.position);
                    if (d > 200f) { Lost(npc); break; } // 포탈 등으로 사라짐. 포탈 따라가기는 ⑤ 단계.
                    // 같이 간 동료(3-2장)가 일을 마치고 쉬면 따라가기도 끝
                    foreach (var lead in WorkerNpc.All)
                        if (lead != npc && lead.GetZDOID() == m_follow && (lead.Ai.Current == Mode.Idle || lead.Ai.Current == Mode.Home)) { GoHome(false); break; }
                    if (Current != Mode.Follow) break;
                    if (d < 3f) StopMoving();
                    else MoveToPoint(dt, pos, 2.5f, d > 8f);
                    break;
                }
                case Mode.Home:
                {
                    Vector3 home = npc.Home;
                    float d = Utils.DistanceXZ(home, transform.position);
                    if (d < 2f)
                    {
                        StopMoving();
                        Current = Mode.Idle;
                        if (m_announceHome) npc.Say("home");
                        break;
                    }
                    MoveToPoint(dt, home, 1.5f, d > 15f);
                    // 길이 없거나 15초 동안 못 움직이면 거점으로 옮긴다 (임시: 길 막힘 처리는 ② 단계).
                    m_stuckTimer = Vector3.Distance(m_lastPos, transform.position) < 0.5f * dt && !m_carving ? m_stuckTimer + dt : 0f;
                    m_lastPos = transform.position;
                    if (m_stuckTimer > 15f)
                    {
                        Plugin.Log.LogInfo($"{npc.NpcName}: 집에 가는 길이 막혀서 거점으로 옮김");
                        m_character.TeleportTo(home, transform.rotation, false);
                        StuckTeleports++;
                        m_stuckTimer = 0f;
                    }
                    break;
                }
                default:
                    StopMoving();
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

        /// <summary>죽었다(WorkerNpc.OnDeath): 하던 일을 버린다. 묘비를 회수하다 죽었으면 그 묘비 시도 횟수를 올린다.</summary>
        public void OnDied()
        {
            var npc = (WorkerNpc)m_character;
            if (Job is GraveJob g) npc.SetGraves(WorkerNpc.AddTry(npc.Graves(), g.Grave, 1));
            Job = null;
            m_follow = ZDOID.None;
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
