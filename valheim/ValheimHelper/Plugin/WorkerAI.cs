using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 일꾼 NPC 행동. 지금(① 단계)은 대기·따라가기·집에 가기만 한다.
    /// 이동·길찾기는 바닐라 BaseAI.MoveTo를 쓴다 (구상서 12장).
    /// </summary>
    public class WorkerAI : BaseAI
    {
        public enum Mode { Idle, Follow, Home, Work }

        /// <summary>Mode.Work일 때 하는 일.</summary>
        public GatherJob Job { get; private set; }
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
            m_pathAgentType = Pathfinding.AgentType.Humanoid;
            m_canBeAlerted = false;
            m_avoidWater = true;
            m_idleSoundChance = 0f;
            // 몬스터 기본값(10°)이면 방향이 조금만 틀어져도 멈춰 서서 돈다 → 걷다 서다를 반복했다(0.3.1 사용자 관찰).
            // 90°면 돌면서 속도만 줄인다(플레이어처럼).
            m_moveMinAngle = 90f;
            m_smoothMovement = true;
        }

        public void StartJob(GatherJob job)
        {
            Job = job;
            Current = Mode.Work;
        }

        public void Face(Vector3 point) => LookAt(point);

        float m_sampleTimer;
        Vector3 m_samplePos;
        float m_detourTime;
        Vector3 m_detourDir;

        /// <summary>
        /// 걷기: 바닐라 길찾기(BaseAI.MoveTo)를 쓰되
        /// - 길을 못 찾으면(처음 가는 곳은 길 지도를 만드는 중일 때가 많다) 서 있지 말고 곧장 걸어간다.
        /// - 1.5초 동안 거의 못 움직였으면(앞을 막은 생물·장애물) 1.2초 옆으로 비켜 간다.
        /// 도착하면 true.
        /// </summary>
        public bool MoveToPoint(float dt, Vector3 point, float dist, bool run)
        {
            Vector3 pos = transform.position;
            float d = Utils.DistanceXZ(point, pos);
            if (d <= dist) { StopMoving(); m_detourTime = 0f; return true; }
            if (m_detourTime > 0f)
            {
                m_detourTime -= dt;
                MoveTowards(m_detourDir, run);
                return false;
            }
            bool done = MoveTo(dt, point, dist, run);
            if (done || !FoundPath())
            {
                Vector3 dir = point - pos;
                dir.y = 0f;
                MoveTowards(dir, run);
            }
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
