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
        }

        public void StartJob(GatherJob job)
        {
            Job = job;
            Current = Mode.Work;
        }

        // GatherJob이 바닐라 BaseAI 이동·시선을 쓰게 열어 준다.
        public bool MoveToPoint(float dt, Vector3 point, float dist, bool run) => MoveTo(dt, point, dist, run);
        public void Face(Vector3 point) => LookAt(point);

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
                    else MoveTo(dt, pos, 2.5f, d > 8f);
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
                    MoveTo(dt, home, 1.5f, d > 15f);
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
