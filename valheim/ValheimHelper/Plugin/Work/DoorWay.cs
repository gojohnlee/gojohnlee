using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 집 안 상자·작업대(회차 311): 손 높이에서 대상까지 건축물(piece 층)이 가리면 가까운 문으로 가서 열고 들어간다.
    /// 닫힌 문은 navmesh에서 벽이고 연 뒤 5초쯤 지나야 길이 나므로(codemap "집·벽·문 길찾기") 문틀은 곧장 걸어 지난다.
    /// 각 단계 8초 제한, 못 하면 옛 동작(그 자리에서 손 닿음)으로.
    /// </summary>
    public sealed class DoorWay
    {
        const float PhaseLimit = 8f, DoorSearch = 15f, ExitSearch = 10f, Step = 1.3f;
        static int s_mask;
        enum Phase { None, ToDoor, Through, GaveUp }
        Phase m_phase;
        Door m_door;
        Vector3 m_out, m_in, m_mouth; bool m_hall, m_lined; Vector3 m_hallN; float m_hallEnd; float m_lineCheck; // m_mouth: 문 앞이 복도면 복도 입구(들어갈 때 거기부터 곧장, E059)
        float m_t, m_limit, m_check, m_swingNext, m_diagNext, m_outT; Door m_outDoor; Vector3 m_outN;
        int m_swing, m_orig; // m_swing 1 = 문짝이 내 쪽으로 열려 있어 닫았음 → 반대로 다시 엶(E064). m_orig = 그때 남이 열어 둔 문이면 원래 s_state
        float m_best, m_stall, m_side; Vector3 m_sideDir; bool m_near; // 나갈 때 문 앞 가는 길이 설비에 막히면 옆으로 비킴, 1.6m 안에서 막히면 거기서 엶(E061)
        Vector3 m_for;
        bool m_walking, m_exiting; // Walk 안의 MoveToPoint가 Exit를 다시 부름
        public static int Opened, Entered, Exited, Closed;

        /// <summary>일꾼이 연 문(회차 313): 그 일꾼이 지붕 밖으로 나와 문에서 3m 넘게(지붕 아래여도 15m 넘게) 멀어지고 문틀에 아무도 없으면 닫는다. 플레이어가 연 문은 안 건드림.</summary>
        sealed class OpenedDoor { public Door Door; public WorkerNpc By; public Vector3 Mid; public float Sent = -99f; public int Reopen; public bool Shut, Inside; } // Reopen: 남이 열어 둔 문(s_state) — 지난 뒤 닫지 않고 그 쪽으로 되돌려 엶
        static readonly System.Collections.Generic.List<OpenedDoor> s_opened = new System.Collections.Generic.List<OpenedDoor>();
        static float s_next;

        /// <summary>일꾼이 연 문으로 적어 둠 — 나중에 닫는다(상자 가기에서 바로 연 문도, E059·회차 334).</summary>
        public static void Track(Door door, WorkerNpc npc, int reopen = 0)
        {
            if (door == null || s_opened.Exists(o => o.Door == door)) return;
            s_opened.Add(new OpenedDoor { Door = door, By = npc, Mid = Middle(door), Reopen = reopen });
        }

        /// <summary>문 쪽으로 가는 중 — 부른 쪽은 "대상까지 멀면 걷기"를 건너뛴다(문으로 돌다 대상 2.5m 밖으로 나가면 다시 대상 쪽으로 당겨 오락가락, 회차 314).</summary>
        public bool Busy => m_phase == Phase.ToDoor || m_phase == Phase.Through;
        /// <summary>문으로 가는 중 남은 길(문 앞 → 문틀 너머 → 목표 직선). 걸음 기록용 — 문 쪽 걷기는 m_path를 안 써서 옛 m_path로 재면 멀어지는 것처럼 보였음(회차 452: 나가며 1초 멈칫으로 셈).</summary>
        public float LeftVia(Vector3 me) => (m_phase == Phase.ToDoor ? Utils.DistanceXZ(me, m_out) + Utils.DistanceXZ(m_out, m_in) : Utils.DistanceXZ(me, m_in)) + Utils.DistanceXZ(m_in, m_for);

        static int Mask =>s_mask != 0 ? s_mask : (s_mask = LayerMask.GetMask("piece"));

        /// <summary>사이를 건축물이 가리나(대상 자신·대상의 자식 충돌체는 뺌). 가운데 줄이 문 옆 좁은 틈(사람 못 지남)을 지나도
        /// 좌우 0.35m 두 줄이 다 막히면 가림(회차 357: 뒷문 옆 틈으로 안 가린다고 봐 앞문으로 돌아감).</summary>
        public static bool Blocked(Character me, Component target)
        {
            if (me == null || target == null) return false;
            Vector3 from = me.transform.position + Vector3.up * 1.2f, to = target.transform.position + Vector3.up * 0.6f;
            if (Ray(from, to, target)) return true;
            Vector3 side = Vector3.Cross(Vector3.up, to - from); side.y = 0f;
            if (side.sqrMagnitude < 0.01f) return false;
            side = side.normalized * SideRay;
            return Ray(from + side, to + side, target) && Ray(from - side, to - side, target);
        }

        const float SideRay = 0.35f;

        static bool Ray(Vector3 from, Vector3 to, Component target)
        {
            foreach (var hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), Mask))
                if (hit.collider != null && !hit.collider.transform.IsChildOf(target.transform)) return true;
            return false;
        }

        /// <returns>true = 이번 틀은 문 쪽으로 움직였음(부른 쪽은 일하지 않음), false = 가리지 않거나 포기 → 하던 대로.</returns>
        public bool Steer(float dt, WorkerNpc npc, Component target)
        {
            if (m_phase == Phase.GaveUp) return false;
            Vector3 me = npc.transform.position;
            if (m_phase == Phase.None)
            {
                if (!Blocked(npc, target)) return false;
                m_door = Nearest(target.transform.position, DoorSearch, me);
                if (m_door == null) { GiveUp(npc, target.name, "곁에 문 없음"); return false; }
                Vector3 n = m_door.transform.forward; n.y = 0f; n.Normalize();
                Vector3 mid = Middle(m_door);
                if (Vector3.Dot(n, target.transform.position - mid) > 0f) n = -n; // n = 대상 반대쪽(일꾼이 벽 옆줄에 서면 일꾼 쪽은 모호)
                m_out = mid + n * Step;
                m_in = mid - n * Step;
                m_limit = PhaseLimit + Utils.DistanceXZ(me, m_out) / 1.5f;
                m_phase = Phase.ToDoor; m_t = 0f; m_diagNext = 0f; m_lineCheck = 0f;
                m_hall = Mouth(mid, n, out m_mouth); m_lined = false; m_hallN = n; m_hallEnd = Vector3.Dot(m_mouth - mid, n) - 0.8f;
                if (m_hall) m_limit = PhaseLimit + (Utils.DistanceXZ(me, m_mouth) + Utils.DistanceXZ(m_mouth, m_out)) / 1.5f;
                Plugin.Log.LogInfo($"DIAG {npc.NpcName} {target.name} 앞 벽 → 문 {m_door.name}({Utils.DistanceXZ(me, m_door.transform.position):0.0}m)으로");
            }
            int r = Walk(dt, npc, target.name);
            if (r != 2) return r == 1;
            Entered++;
            // 회차 450: 들어온 뒤 내가 연 문은 몇 걸음 지나 닫음(CloseBehind) — 열어 두면 문짝이 안쪽에 있어 나갈 때 문 앞에 서서 닫고 반대로 다시 열며 1.2~2.8초 섰음
            var mine = s_opened.Find(o => o.Door == m_door);
            if (mine != null && mine.By == npc && mine.Reopen == 0) mine.Inside = true;
            Plugin.Log.LogInfo($"DIAG {npc.NpcName} 문 지나 안으로 ({m_t:0.0}초, 나 {me:F1}·문틀 {Middle(m_door):F1}·문 {m_door.transform.position:F1}·바깥 {m_out:F1}·{target.name}까지 {Utils.DistanceXZ(me, target.transform.position):0.0}m)");
            m_phase = Phase.None;
            return Blocked(npc, target) ? Retry(npc, target.name) : false;
        }

        /// <summary>
        /// 안에서 나가기(회차 315, ③-5): 지붕 아래에서 지붕 밖 목표로 걷는데 사이를 건축물이 가리면 10m 안 문으로 가서(닫혔으면 열고 — 플레이어가 닫음) 나간다.
        /// WorkerAI.MoveToPoint가 부름. 목표가 2m 넘게 바뀌면 포기를 잊는다.
        /// </summary>
        /// <returns>true = 이번 틀은 문 쪽으로 움직였음.</returns>
        public bool Exit(float dt, WorkerNpc npc, Vector3 point)
        {
            if (npc == null || m_walking) return false;
            if (Utils.DistanceXZ(point, m_for) > 2f)
            {
                m_for = point;
                if (m_phase == Phase.GaveUp) m_phase = Phase.None;
                // 회차 415 사6: 집 밖 목표(일 끝나 깃발로)로 문을 향하던 중 새 목표가 집 안이면 그 문으로 계속 가다 막혀 8~13초 섰음(2층 문, 욕조에 막힘) → 문틀 들어서기 전이면 접고 새 목표로 다시 판단
                else if (m_phase == Phase.ToDoor) { Plugin.Log.LogInfo($"DIAG {npc.NpcName} 나가기 접음: 목표 바뀜 {point:F1} ({m_t:0.0}초)"); m_phase = Phase.None; m_check = 0f; }
            }
            if (m_phase == Phase.GaveUp) return false;
            Vector3 me = npc.transform.position;
            if (m_phase == Phase.None)
            {
                if (Time.time < m_check) return false;
                m_check = Time.time + 0.5f;
                if (Utils.DistanceXZ(me, point) < 3f || !Roofed(me) || Roofed(point)) return false;
                Vector3 from = me + Vector3.up * 1.2f, to = point + Vector3.up * 1.2f;
                if (!Physics.Raycast(from, to - from, Vector3.Distance(from, to), Mask)) return false;
                Door d = Nearest(me, ExitSearch, point);
                if (d != null && d == m_outDoor && Time.time - m_outT < 60f && (Vector3.Dot(me - Middle(d), m_outN) > 0f || Hidden(from, Middle(d) - m_outN * Step + Vector3.up * 0.2f, d))) return false; // 회차 348: 방금 이 문으로 나와 아직 문 바깥 — 처마·옆 건물에 지붕·벽 판정이 "안"으로 나와도 안 돌아감
                ZNetView dv = d != null ? d.GetComponent<ZNetView>() : null;
                if (dv == null || !dv.IsValid()) return false; // 열린 문도 곧장 지나감 — 연 뒤 navmesh가 늦게 바뀌어 바닐라 길은 벽에 박힘(회차 315 시험)
                m_door = d;
                Vector3 n = d.transform.forward; n.y = 0f; n.Normalize();
                if (!Walled(me, n)) { m_check = Time.time + 1f; return false; } // 회차 348: 막 나온 집 옆 처마 밑(지붕 True)을 안으로 읽어 문으로 되돌아감 → 사방 벽까지 봄
                Vector3 mid = Middle(d), a = mid + n * Step, b = mid - n * Step;
                bool ra = Roofed(a), rb = Roofed(b);
                bool aIn = ra != rb ? ra : Utils.DistanceXZ(me, a) < Utils.DistanceXZ(me, b); // 지붕 아래 쪽이 안, 모호하면 가까운 쪽
                m_out = aIn ? a : b; m_in = aIn ? b : a; // 이름은 들어갈 때 기준: m_out = 다가갈 곳, m_in = 지나갈 곳
                m_limit = PhaseLimit + Utils.DistanceXZ(me, m_out) / 1.5f;
                m_phase = Phase.ToDoor; m_t = 0f;
                m_best = float.MaxValue; m_stall = m_side = 0f; m_sideDir = Vector3.zero; m_near = false;
                Plugin.Log.LogInfo($"DIAG {npc.NpcName} 안에서 문 {d.name}(열림 {Dungeons.IsOpen(dv.GetZDO())})({Utils.DistanceXZ(me, mid):0.0}m)으로 나감 (나 {me:F1}·벽 {WallHits(me, n)}/4·방금 나온 문 {d == m_outDoor}·목표 {point:F1})");
            }
            m_walking = m_exiting = true;
            int r;
            try { r = Walk(dt, npc, "나가기"); } finally { m_walking = false; }
            if (r != 2) return r == 1;
            Exited++;
            npc.Ai.ForgetPath();
            CloseIfSwungAtMe(npc, me);
            m_outDoor = m_door; m_outT = Time.time; m_outN = m_in - Middle(m_door); m_outN.y = 0f; m_outN.Normalize();
            Plugin.Log.LogInfo($"DIAG {npc.NpcName} 문 지나 밖으로 ({m_t:0.0}초, 지붕 {Roofed(me)}, 나 {me:F1}·목표 {m_for:F1})");
            m_phase = Phase.None;
            return false;
        }

        /// <summary>문 앞 → 열기 → 문틀 지나기. 0 = 포기(하던 대로), 1 = 움직이는 중, 2 = 지나감.</summary>
        int Walk(float dt, WorkerNpc npc, string what)
        {
            Vector3 me = npc.transform.position;
            m_t += dt;
            if (m_t > (m_phase == Phase.ToDoor ? m_limit : PhaseLimit)) { GiveUp(npc, what, $"{m_phase} {m_t:0}초, 문 앞까지 {Utils.DistanceXZ(me, m_out):0.0}m·안쪽까지 {Utils.DistanceXZ(me, m_in):0.0}m"); return 0; }
            if (m_phase == Phase.ToDoor)
            {
                if (Utils.DistanceXZ(me, m_out) > 0.7f && !m_near)
                {
                    // 안에서 나갈 땐 곧장(닫힌 집 안은 바닐라 길찾기가 10초 제자리, 회차 315 시험)
                    if (m_exiting) ExitToward(dt, npc, me);
                    else
                    {
                        // E059: 문 앞이 좁은 복도면 바닐라 길(새 벽 navmesh 늦음)이 복도 바깥 벽 옆에서 섬 → 입구로 가서, 문 앞이 트이면 곧장
                        if (m_hall && Time.time >= m_lineCheck)
                        {
                            m_lineCheck = Time.time + 0.25f;
                            Vector3 to = m_out - me; to.y = 0f;
                            m_lined = !Physics.CapsuleCast(me + Vector3.up * 0.6f, me + Vector3.up * 1.0f, 0.4f, to.normalized, to.magnitude, Mask, QueryTriggerInteraction.Ignore);
                        }
                        if (!m_hall) npc.Ai.MoveToPoint(dt, m_out, 0.5f, false);
                        else if (m_lined) npc.Ai.MoveTowards(Vector3.ProjectOnPlane(m_out - me, Vector3.up), false);
                        else
                        {
                            // 복도 바깥 벽 옆이면 벽 따라 곧장 밖으로(바닐라 길은 벽 끝 모서리에 걸림, 회차 348 시험) → 지나면 입구로
                            Vector3 rel = me - Middle(m_door); rel.y = 0f;
                            float along = Vector3.Dot(rel, m_hallN), side = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, m_hallN)));
                            if (along > 0f && along < m_hallEnd + 0.4f && side < 2.4f) npc.Ai.MoveTowards(m_hallN, false);
                            else npc.Ai.MoveToPoint(dt, m_mouth, 0.5f, false);
                        }
                        if (m_t >= m_diagNext) { m_diagNext = m_t + 3f; Plugin.Log.LogInfo($"DIAG {npc.NpcName} {what} 문 앞으로 {m_t:0.0}초: 나 {me:F1}·문 앞 {m_out:F1}({Utils.DistanceXZ(me, m_out):0.0}m)·복도 {(m_hall ? $"입구 {m_mouth:F1} 트임 {m_lined}" : "없음")}·길 {npc.Ai.PathFound}·속도 {npc.GetVelocity().magnitude:0.0}"); }
                    }
                    return 1;
                }
                m_swing = 0;
                if (Dungeons.Open(npc, m_door))
                {
                    Opened++;
                    Track(m_door, npc);
                    m_swing = 2; // 방금 내가 엶 — 바꿔 열기 안 함(회차 347: 연 직후 s_state가 반대로 읽혀 들어갈 때도 닫음)
                }
                m_phase = Phase.Through; m_t = 0f;
            }
            ZNetView v = m_door != null ? m_door.GetComponent<ZNetView>() : null;
            if (v == null || !v.IsValid()) { GiveUp(npc, what, "문 사라짐"); return 0; }
            // E064: 반대쪽에서 연 문(들어갈 때 바깥에서 엶)은 문짝이 내 쪽에 있어 박힘 → 닫고 내 쪽에서 다시 엶(바닐라 RPC_UseDoor: forward면 s_state 1, 연 문이면 0)
            int state = v.GetZDO().GetInt(ZDOVars.s_state);
            if (m_swing == 0 && state != 0)
            {
                int mine = Vector3.Dot(m_door.transform.forward, me - m_door.transform.position) < 0f ? 1 : -1;
                if (state == -mine && SomeoneAt(Middle(m_door), npc)) { npc.Ai.StopMoving(); return 1; } // 회차 352: 문간에 플레이어가 서 있으면 닫지 않고 비킬 때까지 기다림(바꿔 열기가 문간의 플레이어 앞에서 문을 닫음)
                if (state == -mine)
                {
                    v.InvokeRPC("UseDoor", false);
                    var had = s_opened.Find(o => o.Door == m_door); m_orig = had != null ? had.Reopen : state; m_swing = 1; // 내가 연 문이면 had.Reopen 0 — 닫는 사이 CloseBehind가 항목을 지우므로 넘겨줌 m_t = 0f; m_swingNext = Time.time + 0.3f;
                    Plugin.Log.LogInfo($"DIAG {npc.NpcName} {what} 문짝이 내 쪽으로 열림(s_state {state}) → 닫고 반대로 다시 엶");
                    npc.Ai.StopMoving();
                    return 1;
                }
            }
            if (m_swing == 1 && state == 0 && Time.time >= m_swingNext)
            {
                m_swingNext = Time.time + 0.3f; // 닫히는 애니메이션 중엔 바닐라 CanInteract가 거절 → 될 때까지
                if (Dungeons.Open(npc, m_door)) Track(m_door, npc, m_orig);
            }
            if (!Dungeons.IsOpen(v.GetZDO())) { npc.Ai.StopMoving(); return 1; } // 열리는 중(주인 RPC 기다림)
            // 문틀 가운데를 지나기 전엔 가운데를 겨눔(회차 317: 문 앞 0.56m 옆에서 곧장 가다 열린 문짝에 8초 박힘)
            Vector3 mid = Middle(m_door), dirIn = m_in - mid; dirIn.y = 0f; dirIn.Normalize();
            Vector3 aim = Vector3.Dot(me - mid, dirIn) < -0.2f ? mid : m_in;
            Vector3 go = aim - me; go.y = 0f; // 높이 성분이 있으면 smooth 이동의 moveDir.y로 새어 거의 안 걸음(회차 311)
            if (Utils.DistanceXZ(me, m_in) > 0.5f)
            {
                npc.Ai.MoveTowards(go.normalized, false);
                return 1;
            }
            return 2;
        }

        /// <summary>
        /// 안에서 문 앞으로 곧장 걷기. 1초 동안 0.2m도 안 가까워지면(E061: 사이에 제련로) 옆 두 쪽 중 1.5m 빈 쪽으로 비킨다 —
        /// 문 앞 가는 곧은 길이 트이면(0.5초 뒤부터) 또는 2.5초면 그침. 지난번 비킴으로도 못 가까워졌으면 반대쪽부터(회차 346: 같은 쪽 1초 비킴 5번, 1.8m에 머묾).
        /// </summary>
        void ExitToward(float dt, WorkerNpc npc, Vector3 me)
        {
            Vector3 to = m_out - me; to.y = 0f;
            float d = to.magnitude;
            if (m_side > 0f)
            {
                m_side -= dt;
                if (m_side > 2f || !Physics.CapsuleCast(me + Vector3.up * 0.6f, me + Vector3.up * 1.0f, 0.4f, to.normalized, d, Mask, QueryTriggerInteraction.Ignore))
                { npc.Ai.MoveTowards(m_sideDir, false); return; }
                m_side = 0f;
            }
            if (d < m_best - 0.2f) { m_best = d; m_stall = 0f; }
            else if ((m_stall += dt) > 1f && d < 1.6f)
            {
                // 회차 346: 문 앞 1.0m(문틀 1.7m)에서 막은 것 없이 속도 0 — 문 쓰는 거리라 거기서 열고 문틀로
                m_near = true;
                Plugin.Log.LogInfo($"DIAG {npc.NpcName} 나가기 문 앞 {d:0.0}m에서 막힘 → 거기서 엶");
                return;
            }
            else if (m_stall > 1f)
            {
                Vector3 l = Vector3.Cross(Vector3.up, to).normalized, pick = Vector3.zero;
                float bestEnd = float.MaxValue;
                foreach (Vector3 s in new[] { l, -l })
                {
                    // 캡슐 위끝 1.4m — 다락 바닥(1.6m)에 걸리지 않게
                    if (Physics.CapsuleCast(me + Vector3.up * 0.6f, me + Vector3.up * 1.0f, 0.4f, s, 1.5f, Mask, QueryTriggerInteraction.Ignore)) continue;
                    float end = Utils.DistanceXZ(me + s * 1.5f, m_out);
                    if (m_sideDir != Vector3.zero && Vector3.Dot(s, m_sideDir) > 0.7f) end += 10f; // 지난 쪽은 효과 없었음 → 뒤로
                    if (end < bestEnd) { bestEnd = end; pick = s; }
                }
                m_stall = 0f; m_best = d;
                if (pick != Vector3.zero)
                {
                    string block = Physics.Raycast(me + Vector3.up * 0.8f, to.normalized, out RaycastHit h, d, Mask, QueryTriggerInteraction.Ignore) ? $"{h.collider.transform.root.name} {h.distance:0.0}m" : "없음";
                    m_sideDir = pick; m_side = 2.5f;
                    Plugin.Log.LogInfo($"DIAG {npc.NpcName} 나가기 문 앞 길 막힘({d:0.0}m, 나 {me:F1}·문 앞 {m_out:F1}·막은 것 {block}) → 옆으로 비킴 {pick:F1}");
                    npc.Ai.MoveTowards(pick, false);
                    return;
                }
            }
            npc.Ai.MoveTowards(to.normalized, false);
        }

        /// <summary>Workers.Update에서(서버만) 0.5초마다. UseDoor는 열린 문이면 닫는 토글이라 보낸 뒤 3초는 다시 안 보냄(두 번 가면 다시 열림).</summary>
        /// <summary>회차 348: 막 나왔는데 내가 연 문짝이 바깥(내 쪽)에 열려 있으면 바로 닫는다 — 문짝이 문 앞 1.5m를 막아 5초 제자리, 3m를 못 멀어져 CloseBehind도 안 닫음 → 비켜 가다 집 안으로 되돌아감.</summary>
        void CloseIfSwungAtMe(WorkerNpc npc, Vector3 me)
        {
            var o = s_opened.Find(x => x.Door == m_door);
            ZNetView v = m_door != null ? m_door.GetComponent<ZNetView>() : null;
            if (o == null || v == null || !v.IsValid()) return;
            int state = v.GetZDO().GetInt(ZDOVars.s_state), mine = Vector3.Dot(m_door.transform.forward, me - m_door.transform.position) < 0f ? 1 : -1;
            if (state == 0 || state != -mine) return;
            foreach (var c in Character.GetAllCharacters())
                if (c != null && c != npc && !c.IsDead() && Utils.DistanceXZ(c.transform.position, o.Mid) < 1.5f && Mathf.Abs(c.transform.position.y - o.Mid.y) < 2f) return;
            v.InvokeRPC("UseDoor", false);
            o.Sent = Time.time; o.Shut = true;
            Closed++;
            Plugin.Log.LogInfo($"DIAG {npc.NpcName} 나와서 문 {m_door.name} 바로 닫음(문짝이 내 쪽 s_state {state})");
        }

        public static void CloseBehind()
        {
            if (s_opened.Count == 0 || Time.time < s_next) return;
            s_next = Time.time + 0.5f;
            for (int i = s_opened.Count - 1; i >= 0; i--)
            {
                var o = s_opened[i];
                ZNetView v = o.Door != null ? o.Door.GetComponent<ZNetView>() : null;
                if (v == null || !v.IsValid()) { s_opened.RemoveAt(i); continue; }
                if (!Dungeons.IsOpen(v.GetZDO()))
                {
                    if (o.Reopen == 0 || !o.Shut) { s_opened.RemoveAt(i); continue; } // 남이 닫았으면 그대로
                    if (Time.time - o.Sent < 1.5f) continue; // 닫히는 중
                    v.InvokeRPC("UseDoor", o.Reopen == 1); // 바닐라 RPC_UseDoor: forward면 s_state 1
                    Plugin.Log.LogInfo($"DIAG {(o.By != null ? o.By.NpcName : "?")} 남이 열어 둔 문 {o.Door.name} 다시 엶(s_state {o.Reopen})");
                    s_opened.RemoveAt(i); continue;
                }
                if (Time.time - o.Sent < 3f) continue;
                if (o.By != null && !o.By.IsDead())
                {
                    Vector3 me = o.By.transform.position;
                    float d = Utils.DistanceXZ(me, o.Mid);
                    if (o.Inside ? d < 2.5f || o.By.Ai.ExitingDoor : d < 3f || (d < 15f && Roofed(me))) continue; // 들어와 문짝 반경을 지났으면 닫음(나가려고 문으로 가는 중이면 그대로), 아니면 문 곁이거나 아직 지붕 아래(안) — 문 평면 반쪽으로는 못 가름: 집 뒤로 돈 일꾼도 안쪽 반에 있음(회차 313)
                }
                if (SomeoneAt(o.Mid, null)) continue;
                v.InvokeRPC("UseDoor", false); // 열린 문이면 방향 무관하게 s_state = 0
                o.Sent = Time.time; o.Shut = true;
                Closed++;
                Plugin.Log.LogInfo($"DIAG {(o.By != null ? o.By.NpcName : "?")} 나와서 문 {o.Door.name} 닫음");
            }
        }

        /// <summary>문틀 가운데 1.5m 안에 누가 서 있나(except는 뺌).</summary>
        static bool SomeoneAt(Vector3 mid, Character except)
        {
            foreach (var c in Character.GetAllCharacters())
                if (c != null && c != except && !c.IsDead() && Utils.DistanceXZ(c.transform.position, mid) < 1.5f && Mathf.Abs(c.transform.position.y - mid.y) < 2f) return true;
            return false;
        }

        static bool Roofed(Vector3 at) => Physics.Raycast(at + Vector3.up * 1.8f, Vector3.up, 6f, Mask);
        /// <summary>두 점 사이를 건축물이 가리나(그 문 자신은 뺌 — 열린 문짝).</summary>
        static bool Hidden(Vector3 from, Vector3 to, Door d)
        {
            foreach (var hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), Mask, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && !hit.collider.transform.IsChildOf(d.transform)) return true;
            return false;
        }

        /// <summary>둘레를 벽이 감쌌나: 문 축 네 방향(1.2m 높이, 10m) 중 셋 이상이 건축물에 닿음 — 열린 문 쪽 하나는 빠질 수 있음.</summary>
        static bool Walled(Vector3 me, Vector3 n) => WallHits(me, n) >= 3;
        static int WallHits(Vector3 me, Vector3 n)
        {
            Vector3 l = Vector3.Cross(Vector3.up, n), from = me + Vector3.up * 1.2f;
            int hits = 0;
            foreach (Vector3 dir in new[] { n, -n, l, -l })
                if (Physics.Raycast(from, dir, 10f, Mask, QueryTriggerInteraction.Ignore)) hits++;
            return hits;
        }

        public static string OpenedDiag()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var o in s_opened)
            {
                string near = "";
                foreach (var c in Character.GetAllCharacters())
                    if (c != null && Utils.DistanceXZ(c.transform.position, o.Mid) < 1.5f) near += c.m_name + " ";
                Vector3 me = o.By != null ? o.By.transform.position : Vector3.zero;
                parts.Add($"{o.By?.NpcName} 지붕 {Roofed(me)}·거리 {Utils.DistanceXZ(me, o.Mid):0.0}m·곁 [{near}]·보냄 {Time.time - o.Sent:0}초 전");
            }
            return $"연 문 {s_opened.Count}개: {string.Join("; ", parts)}";
        }

        bool Retry(WorkerNpc npc, string what) { GiveUp(npc, what, "안에서도 가림"); return false; }

        void GiveUp(WorkerNpc npc, string what, string why)
        {
            m_phase = Phase.GaveUp;
            if (m_door != null)
            {
                Vector3 me = npc.transform.position, along = Vector3.Cross(Vector3.up, m_in - m_out).normalized;
                var hits = new System.Collections.Generic.List<string>();
                Vector3 flat = m_in - me; flat.y = 0f;
                foreach (var h in Physics.CapsuleCastAll(me + Vector3.up * 0.5f, me + Vector3.up * 1.4f, 0.45f, flat.normalized, 2f, ~0, QueryTriggerInteraction.Ignore)) if (!h.collider.transform.IsChildOf(npc.transform)) hits.Add($"{h.collider.name}/{h.collider.transform.root.name} {h.distance:0.0}m");
                why += $", 문틀 옆으로 {Vector3.Dot(me - Middle(m_door), along):0.00}m·문 옆으로 {Vector3.Dot(me - m_door.transform.position, along):0.00}m, 안쪽으로 [{string.Join(", ", hits)}], 문 s_state {m_door.GetComponent<ZNetView>()?.GetZDO()?.GetInt(ZDOVars.s_state)}·속도 {npc.GetVelocity().magnitude:0.00}·앞 {Vector3.Angle(npc.transform.forward, m_in - m_out):0}°·{npc.Ai.HoldWhy}";
            }
            Plugin.Log.LogInfo($"DIAG {npc.NpcName} {what} 문 길 포기: {why} → 그 자리에서");
        }

        /// <summary>문 앞(바깥 n쪽)이 양옆 벽 좁은 복도인지(E059). 그렇다면 입구 = 양옆이 트이는 첫 자리에서 0.8m 더 밖.</summary>
        static bool Mouth(Vector3 mid, Vector3 n, out Vector3 mouth)
        {
            Vector3 l = Vector3.Cross(Vector3.up, n).normalized;
            mouth = mid;
            for (float s = Step; s <= Step + 8f; s += 0.5f)
            {
                Vector3 p = mid + n * s;
                bool walls = Physics.Raycast(p, l, 1.6f, Mask, QueryTriggerInteraction.Ignore) && Physics.Raycast(p, -l, 1.6f, Mask, QueryTriggerInteraction.Ignore);
                if (walls) continue;
                mouth = mid + n * (s + 0.8f);
                return s > Step;
            }
            return false; // 8m 넘는 복도 — 바닐라 길에 맡김
        }

        /// <summary>문틀 가운데(문 transform은 경첩 쪽일 수 있어 닫힌 문 충돌체 가운데).</summary>
        static Vector3 Middle(Door d)
        {
            bool any = false; Bounds b = default;
            foreach (var c in d.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            Vector3 m = any ? b.center : d.transform.position;
            m.y = d.transform.position.y;
            return m;
        }


        /// <summary>
        /// 호위병 경계 자리(사용자 11-09): 거점 range 안 열쇠 없는 문 중 바깥문(한쪽만 지붕 — 집 안 방문은 걸리적거림, 11-09)을 먼저, 그중 거점에서 가장 가까운 것.
        /// 바깥문이 없으면 가장 가까운 문. 자리는 바깥(지붕 없는 쪽, 모호하면 거점 반대쪽) 1.8m·옆 1.2m, 지붕 아래면 4m까지 더 밖으로. 문이 없으면 false.
        /// </summary>
        public static bool PostBy(Vector3 home, float range, out Vector3 post, out Vector3 outward)
        {
            post = home; outward = Vector3.forward;
            Door best = null; float bd = float.MaxValue; bool bestOuter = false;
            foreach (var c in Physics.OverlapSphere(home, range, Mask))
            {
                Door d = c.GetComponentInParent<Door>();
                if (d == null || d.m_keyItem != null || d == best) continue;
                Vector3 m = Middle(d), dn = d.transform.forward; dn.y = 0f; dn.Normalize();
                bool outer = Roofed(m + dn * Step) != Roofed(m - dn * Step);
                float dd = Utils.DistanceXZ(home, d.transform.position);
                if (outer && !bestOuter || outer == bestOuter && dd < bd) { bd = dd; best = d; bestOuter = outer; }
            }
            if (best == null) return false;
            Vector3 mid = Middle(best), n = best.transform.forward; n.y = 0f; n.Normalize();
            bool ra = Roofed(mid + n * Step), rb = Roofed(mid - n * Step);
            Vector3 away = mid - home; away.y = 0f;
            outward = ra != rb ? (ra ? -n : n) : (Vector3.Dot(n, away) >= 0f ? n : -n);
            post = mid + outward * 1.8f + Vector3.Cross(Vector3.up, outward) * 1.2f;
            for (int i = 0; i < 4 && Roofed(post); i++) post += outward;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(post, out float h)) post.y = h;
            return true;
        }
        /// <summary>at에서 range 안 문 중 at·other까지 거리 합이 가장 짧은 것(문 둘이면 일꾼 쪽 문 — 회차 317, ④).</summary>
        static Door Nearest(Vector3 at, float range, Vector3 other)
        {
            Door best = null; float bd = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(at, range, Mask))
            {
                Door d = c.GetComponentInParent<Door>();
                if (d == null || d.m_keyItem != null) continue;
                float dd = Utils.DistanceXZ(at, d.transform.position) + Utils.DistanceXZ(other, d.transform.position);
                if (dd < bd) { bd = dd; best = d; }
            }
            return best;
        }
    }
}
