using UnityEngine;
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

        public void StartJob(IWorkerJob job)
        {
            Job = job;
            Current = Mode.Work;
        }

        public void Face(Vector3 point) => LookAt(point);

        /// <summary>시험 기록용: 마지막 길찾기가 길을 찾았는지.</summary>
        public bool PathFound => FoundPath();

        float m_sampleTimer;
        Vector3 m_samplePos;
        float m_detourTime;
        Vector3 m_detourDir;

        /// <summary>깊은 물 앞에서 선 채로 지난 시간. 작업은 이게 WaterGiveUp초를 넘으면 그 목표를 버리고 다른 데를 고른다.</summary>
        public float WaterBlockedTime { get; private set; }
        public const float WaterGiveUp = 3f;
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
            float d = Utils.DistanceXZ(point, pos);
            if (d <= dist) { StopMoving(); m_detourTime = 0f; WaterBlockedTime = 0f; return true; }
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
                WaterBlockedTime += dt;
                return false;
            }
            WaterBlockedTime = 0f;
            m_sampleTimer += dt;
            if (m_sampleTimer >= 1.5f)
            {
                if (Utils.DistanceXZ(m_samplePos, pos) < 0.75f && d > dist + 1f)
                {
                    Vector3 fwd = point - pos;
                    fwd.y = 0f;
                    Vector3 side = Vector3.Cross(Vector3.up, fwd.normalized) * (Random.value < 0.5f ? 1f : -1f);
                    m_detourDir = (side + fwd.normalized * 0.3f).normalized;
                    m_detourTime = 1.2f;
                }
                m_sampleTimer = 0f;
                m_samplePos = pos;
            }
            return false;
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
            m_senseTimer -= dt;
            if (m_senseTimer <= 0f)
            {
                m_senseTimer = 2f;
                Senses.Look(transform.position);
            }
            switch (Current)
            {
                case Mode.Work:
                    if (Job == null || !Job.Update(dt))
                    {
                        GoHome(false);
                    }
                    break;
                case Mode.Follow:
                {
                    ZDO target = m_follow.IsNone() ? null : ZDOMan.instance.GetZDO(m_follow);
                    if (target == null) { Lost(npc); break; }
                    Vector3 pos = target.GetPosition();
                    float d = Vector3.Distance(pos, transform.position);
                    if (d > 200f) { Lost(npc); break; } // 포탈 등으로 사라짐. 포탈 따라가기는 ⑤ 단계.
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
                    m_stuckTimer = Vector3.Distance(m_lastPos, transform.position) < 0.5f * dt ? m_stuckTimer + dt : 0f;
                    m_lastPos = transform.position;
                    if (m_stuckTimer > 15f)
                    {
                        Plugin.Log.LogInfo($"{npc.NpcName}: 집에 가는 길이 막혀서 거점으로 옮김");
                        m_character.TeleportTo(home, transform.rotation, false);
                        m_stuckTimer = 0f;
                    }
                    break;
                }
                default:
                    StopMoving();
                    break;
            }
            return true;
        }

        void Lost(WorkerNpc npc)
        {
            npc.Say("lost");
            GoHome();
        }
    }
}
