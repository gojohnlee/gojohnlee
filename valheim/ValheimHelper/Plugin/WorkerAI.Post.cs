using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>WorkerAI 호위병 경계: 문 옆 서기·순찰·경례(사용자 11-09).</summary>
    public partial class WorkerAI
    {
        // ---- 호위병 경계(사용자 11-09) ----

        readonly Patrol m_patrol = new Patrol();
        float m_patrolTick, m_postFindAt;
        bool m_hasPost;
        Door m_postDoor;
        readonly HashSet<Door> m_badPostDoors = new HashSet<Door>();
        Vector3 m_post, m_postOut, m_postHome;
        /// <summary>시험: 끝까지 돈 순찰 수(호위병들 합).</summary>
        public static int PatrolRounds;
        readonly Salute m_salute = new Salute();
        /// <summary>시험: 경례 수(호위병들 합)와 마지막 경례 꼴.</summary>
        public static int Salutes;
        public static string LastSalute;
        public Patrol GuardPatrol => m_patrol;
        public bool HasPost => m_hasPost;
        /// <summary>호위병이 거점 둘레 순찰을 걷는 중(이땐 쉴 때 집안일을 안 함 — 순찰 먼저, 11-14).</summary>
        public bool Patrolling => m_patrol.Walking;
        public Vector3 Post => m_post;

        /// <summary>
        /// 쉬는 호위병: 거점 집 문 옆에 서서 바깥을 보고, 10분마다 거점 둘레를 크게(PatrolRoute 점, 달림 — 점이 없으면 옛 20m 4점) 돌고 문 옆으로(Core Patrol). 시간 안에 못 닿은 점은 다음 고르기에서 안쪽.
        /// 보이는 적은 경계(Guard)가 싸우고 그 뒤 순찰을 이어 감, 다른 일(따라가기·장비 챙기기)로 끊긴 뒤 다시 쉬면 문 옆부터. 25m 안 문이 없으면 거점(깃발 옆)을 자리로.
        /// </summary>
        void GuardPostTick(WorkerNpc npc, float dt)
        {
            if (m_choresHeld) { StopMoving(); return; } // "멈춰"로 쉬라 함
            if (Time.time - m_patrolTick > 2f) m_patrol.Reset();
            m_patrolTick = Time.time;
            if (Time.time >= m_postFindAt || Utils.DistanceXZ(m_postHome, npc.Home) > 1f)
            {
                m_postFindAt = Time.time + 30f;
                if (Utils.DistanceXZ(m_postHome, npc.Home) > 1f) m_badPostDoors.Clear(); // 거점이 바뀌면 못 닿던 문 목록도 새로
                m_postHome = npc.Home;
                bool had = m_hasPost; Door prevDoor = m_postDoor; Vector3 old = m_post;
                m_badPostDoors.RemoveWhere(d => d == null);
                m_hasPost = DoorWay.PostBy(m_postHome, 25f, m_badPostDoors, out m_post, out m_postOut, out m_postDoor);
                if (!m_hasPost) { m_post = m_postHome; m_postOut = transform.forward; m_postOut.y = 0f; }
                if (m_hasPost && (!had || prevDoor != m_postDoor || Utils.DistanceXZ(old, m_post) > 1f))
                    Plugin.Log.LogInfo($"{npc.NpcName}: 경계 자리 = {(m_postDoor != null ? "문 옆" : "문 없음 → 지붕 없는 곳")} {m_post:F1}(거점에서 {Utils.DistanceXZ(m_post, m_postHome):0.0}m, 못 닿은 문 {m_badPostDoors.Count})");
            }
            Vector3 pos = transform.position;
            if (!m_patrol.Walking) RouteTick(npc, dt);
            int count = m_route.Points.Count > 0 ? m_route.Points.Count : Patrol.Points;
            Vector3 goal = m_patrol.Walking ? PatrolPoint(m_patrol.Point) : m_post;
            bool waterSkip = m_patrol.Walking && WaterBlockedTime >= WaterGiveUp && Utils.DistanceXZ(m_moveTarget, goal) <= 2f; // 이 점 가다 물에 3초 막힘(E099: 47초 한도까지 물가에 서 있었음) — 막힌 시간은 그 목표 것만
            bool skip = m_patrol.Walking && (!PointOk(goal) || waterSkip);
            if (waterSkip) Plugin.Log.LogInfo($"{npc.NpcName}: 순찰 점 {m_patrol.Point} 가다 물에 막힘 {WaterBlockedTime:0.0}초 → 건너뜀, 자리 {pos:F1}, {Utils.DistanceXZ(pos, goal):0}m 남음");
            int was = m_patrol.Point;
            bool reached = Utils.DistanceXZ(pos, goal) < (m_patrol.Walking ? 2f : 1.2f);
            float limit = m_patrol.Walking ? Mathf.Max(Patrol.PointLimit, 20f + Utils.DistanceXZ(was > 0 ? PatrolPoint(was - 1) : m_post, goal) / 4f) : Patrol.PointLimit;
            if (m_patrol.Walking) // 순찰 점 못 닿음 diag(E099·회차 620): 이 점 가는 동안 가장 가까이 간 거리, 선·물 막힘·헤엄·길 못 찾음 시간
            {
                m_legBest = Mathf.Min(m_legBest, Utils.DistanceXZ(pos, goal));
                if (Flat(m_character.GetVelocity()).magnitude < 0.5f) m_legStill += dt;
                if (WaterBlockedTime > 0f) m_legWater += dt;
                if (m_character.IsSwimming()) m_legSwim += dt;
                if (!FoundPath()) m_legNoPath += dt;
            }
            bool gaveUp = m_patrol.GaveUp;
            PatrolGoal g = m_patrol.Tick(dt, reached, skip, count, limit);
            if (!gaveUp && m_patrol.GaveUp && m_postDoor != null && Utils.DistanceXZ(pos, m_post) > 1.2f)
            {
                // 11-14: 이 문 옆엔 못 닿음(문 안쪽 설비에 막힘 등) → 그 문은 빼고 곧 다시 고름(집 안 그 자리에 계속 서 있지 않게)
                m_badPostDoors.Add(m_postDoor);
                m_postFindAt = 0f;
                m_patrol.Reset();
                Plugin.Log.LogInfo($"{npc.NpcName}: 경계 자리 문 {m_postDoor.name} 옆에 못 닿음 → 그 문 빼고 다시 고름");
            }
            if (!gaveUp && m_patrol.GaveUp) // E099 diag: 문 옆으로 가다 시간 한도 — 어디서(물속?) 왜(길 부분·곧장 걷기·헤엄 막힘) 멈췄나
                Diag.Log($"{npc.NpcName}: DIAG 문 옆 {Patrol.PostLimit:0}초 안에 못 감 → 그 자리에 섬 {pos:F1}, 문 옆까지 {Utils.DistanceXZ(pos, m_post):0.0}m, 헤엄 {m_character.IsSwimming()}·물속 {m_character.InLiquid()}·물 깊이 {Scout.WaterDepth(pos):0.0}·건넘 {m_swimOk}·물 막힘 {WaterBlockedTime:0.0}초, 마른 자리 {(m_hasDry ? Utils.DistanceXZ(m_lastDry, pos).ToString("0.0") + "m" : "없음")}, {m_moveDiag}, {PathDiag(m_post)}, 곧은 줄 걸어감 {Scout.CanWalk(pos, m_post)}");
            bool swimNow = m_character.IsSwimming();
            if (g == PatrolGoal.Post && !m_patrol.GaveUp && swimNow != m_postSwim) // E099 diag: 문 옆 가는 길에 물에 듦·나옴
                Diag.Log($"{npc.NpcName}: DIAG 문 옆 가다 물에 {(swimNow ? "듦" : "나옴")} {pos:F1}, 문 옆까지 {Utils.DistanceXZ(pos, m_post):0.0}m, 건넘 {m_swimOk}, {m_moveDiag}, {PathDiag(m_post)}");
            m_postSwim = swimNow;
            if (was >= 0 && m_patrol.Point != was && !reached && !skip && m_route.Points.Count > 0)
            {
                m_roundMissed++;
                m_route.Unreached(was); // 시간 안에 못 닿음 — 다음 고르기에서 그 방향은 안쪽 반경으로
                Plugin.Log.LogInfo($"{npc.NpcName}: 순찰 점 {was} 못 닿음({Utils.DistanceXZ(pos, goal):0}m 남음) → 다음엔 안쪽 — 한도 {limit:0}초, 가장 가까이 {m_legBest:0}m, 선 {m_legStill:0}초·물 막힘 {m_legWater:0}초·헤엄 {m_legSwim:0}초·길 못 찾음 {m_legNoPath:0}초, 자리 {pos:F1}, 헤엄 {m_character.IsSwimming()}·물 깊이 {Scout.WaterDepth(pos):0.0}·건넘 {m_swimOk}, {m_moveDiag}, {PathDiag(goal)}, 곧은 줄 걸어감 {Scout.CanWalk(pos, goal)}");
            }
            if (m_patrol.Point != was) { m_legBest = float.MaxValue; m_legStill = m_legWater = m_legSwim = m_legNoPath = 0f; }
            if (g == PatrolGoal.Point && was < 0)
            {
                m_roundStart = Time.time; m_roundFights = 0; m_roundMissed = 0;
                Plugin.Log.LogInfo($"{npc.NpcName}: 거점 둘레 순찰 시작(점 {count})");
            }
            if (g == PatrolGoal.Post && was >= 0 && !m_patrol.Walking)
            {
                PatrolRounds++;
                LastRound = $"한 바퀴 {Time.time - m_roundStart:0}초·점 {count}·못 닿음 {m_roundMissed}·싸움 {m_roundFights}";
                Plugin.Log.LogInfo($"{npc.NpcName}: 순찰 끝({LastRound}) → 문 옆으로 — {pos:F1}에서 {Utils.DistanceXZ(pos, m_post):0}m, {PathDiag(m_post)}, 곧은 줄 걸어감 {Scout.CanWalk(pos, m_post)}"); // E099 diag: 돌아가는 길
            }
            switch (g)
            {
                case PatrolGoal.Stand:
                    StopMoving();
                    if (!SaluteTick(npc)) LookAt(m_post + m_postOut * 10f + Vector3.up * 1.5f);
                    break;
                case PatrolGoal.Post:
                    MoveToPoint(dt, m_post, 1f, Utils.DistanceXZ(pos, m_post) > 15f);
                    if (m_moveToDone && !m_deadEnd && WaterBlockedTime < WaterGiveUp && Utils.DistanceXZ(pos, m_post) > 15f && Scout.CanWalk(pos, m_post)) // 길 없음·부분 길 끝인데 곧은 줄은 걸을 만함(E099)
                    {
                        StartDeadEnd(Utils.DistanceXZ(pos, m_post));
                        Plugin.Log.LogInfo($"{npc.NpcName}: 문 옆 길이 막다름 {pos:F1}, {Utils.DistanceXZ(pos, m_post):0}m, {PathDiag(m_post)} → 곧장");
                    }
                    break;
                default:
                    MoveToPoint(dt, PatrolPoint(m_patrol.Point), 1.5f, m_route.Points.Count > 0); // 큰 바퀴는 달림(요청 11-12)
                    break;
            }
        }

        // ---- 순찰 점 고르기(요청 11-12 23:22: 거점 범위를 크게 한 바퀴, 점은 지형을 보고 Core PatrolRoute) ----
        readonly PatrolRoute m_route = new PatrolRoute();
        float m_routeSigAt, m_roundStart;
        long m_routeSig;
        int m_roundFights, m_roundMissed;
        bool m_patrolFighting, m_postSwim;
        float m_legBest = float.MaxValue, m_legStill, m_legWater, m_legSwim, m_legNoPath;
        static readonly List<Piece> s_routePieces = new List<Piece>();
        /// <summary>시험·diag: 마지막 바퀴 요약, 마지막 고르기(점·버린 방향과 까닭).</summary>
        public static string LastRound, LastRoute;
        public IReadOnlyList<PatrolRoute.Spot> RoutePoints => m_route.Points;
        /// <summary>시험 상태 줄(TestKit.Snapshot): 쉬는 호위병이 순찰 몇 번 점으로 가는지·남은 거리, 아니면 문 옆까지 거리.</summary>
        public string PostDiag => m_patrol.Walking ? $"순찰 점 {m_patrol.Point} 남음 {Utils.DistanceXZ(transform.position, PatrolPoint(m_patrol.Point)):0}m"
            : m_patrol.GaveUp ? "문 옆 포기" : $"문 옆 {Utils.DistanceXZ(transform.position, m_post):0}m";

        /// <summary>문 옆에 있을 때: 30초마다 거점 130m 안 조각 수·자리로 땅 표시를 만들고, 바뀌었거나 막은 점·1시간이면 다시 고름.</summary>
        void RouteTick(WorkerNpc npc, float dt)
        {
            if (ZoneSystem.instance == null) return;
            if (Time.time >= m_routeSigAt)
            {
                m_routeSigAt = Time.time + 30f;
                s_routePieces.Clear();
                Piece.GetAllPiecesInRadius(m_postHome, 130f, s_routePieces);
                long sig = s_routePieces.Count * 1000003L + Mathf.RoundToInt(m_postHome.x) * 31L + Mathf.RoundToInt(m_postHome.z);
                foreach (var pc in s_routePieces) if (pc != null) sig += Mathf.RoundToInt(pc.transform.position.x) * 7L + Mathf.RoundToInt(pc.transform.position.z) * 13L;
                m_routeSig = sig;
            }
            if (!m_route.Due(dt, m_routeSig)) return;
            Vector3 dir = m_post - m_postHome;
            m_route.Build(m_postHome.x, m_postHome.z, dir.x, dir.z, RouteProbe);
            var why = new List<string>();
            for (int d = 0; d < PatrolRoute.Dirs; d++) if (m_route.DroppedWhy[d] != null) why.Add($"{d}:{m_route.DroppedWhy[d]}");
            var rings = new List<string>();
            foreach (var s in m_route.Points) rings.Add($"{s.Dir}:{PatrolRoute.Radii[s.Ring]:0}");
            LastRoute = $"점 {m_route.Points.Count} [{string.Join(" ", rings)}] 버림 [{string.Join(" ", why)}]";
            Plugin.Log.LogInfo($"{npc.NpcName}: 순찰 점 고름 — {LastRoute}");
        }

        static PatrolSpot RouteProbe(double x, double z)
        {
            var p = new Vector3((float)x, 0f, (float)z);
            if (!ZoneSystem.instance.GetGroundHeight(p, out float h)) return PatrolSpot.NoGround;
            if (h < ZoneSystem.instance.m_waterLevel + 0.3f) return PatrolSpot.Water;
            float H(float ox, float oz) => ZoneSystem.instance.GetGroundHeight(p + new Vector3(ox, 0f, oz), out float y) ? y : h;
            if (PatrolRoute.Steep(h, H(0f, 2f), H(2f, 0f), H(0f, -2f), H(-2f, 0f), 2.0, 40.0)) return PatrolSpot.Steep;
            p.y = h;
            s_routePieces.Clear();
            Piece.GetAllPiecesInRadius(p, 4f, s_routePieces);
            if (s_routePieces.Count > 0) return PatrolSpot.Built;
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.up, 30f, LayerMask.GetMask("piece"))) return PatrolSpot.Roof;
            return PatrolSpot.Ok;
        }

        /// <summary>문 옆에 선 호위병: 플레이어가 4m 안을 지나가면 그쪽을 보고 경례(같은 사람 60초 한 번, 적이 보이면 안 함, Core Salute). 경례 중이면 참.</summary>
        bool SaluteTick(WorkerNpc npc)
        {
            bool foe = m_guard != null && (m_guard.Engaged || m_guard.Target != null);
            if (foe) m_salute.Stop();
            Player who = null;
            if (m_salute.Busy(Time.time))
            {
                foreach (var pl in Player.GetAllPlayers()) if (pl.GetPlayerID() == m_salute.Who) who = pl;
            }
            else if (!foe)
            {
                foreach (var pl in Player.GetAllPlayers())
                {
                    if (pl.IsDead() || !m_salute.Try(pl.GetPlayerID(), Utils.DistanceXZ(pl.transform.position, transform.position), Time.time, false)) continue;
                    who = pl;
                    LookAt(pl.GetHeadPoint());
                    string how = npc.Salute();
                    Salutes++;
                    LastSalute = how;
                    Plugin.Log.LogInfo($"{npc.NpcName}: {pl.GetPlayerName()}에게 경례({how ?? "고개만"})");
                    break;
                }
            }
            if (who == null) return false;
            LookAt(who.GetHeadPoint());
            return true;
        }

        Vector3 PatrolPoint(int i)
        {
            if (m_route.Points.Count > 0)
            {
                var s = m_route.Points[Mathf.Clamp(i, 0, m_route.Points.Count - 1)];
                var q = new Vector3((float)s.X, m_postHome.y, (float)s.Z);
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(q, out float qh)) q.y = qh;
                return q;
            }
            Vector3 dir = m_post - m_postHome;
            float r = Mathf.Max(Patrol.Radius, Utils.DistanceXZ(m_post, m_postHome) + 8f);
            Patrol.PointAt(m_postHome.x, m_postHome.z, dir.x, dir.z, i, r, out double x, out double z);
            var p = new Vector3((float)x, m_postHome.y, (float)z);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out float h)) p.y = h;
            return p;
        }

        static bool PointOk(Vector3 p) => ZoneSystem.instance == null || p.y > ZoneSystem.instance.m_waterLevel - 0.5f; // 물속 점은 건너뜀
    }
}
