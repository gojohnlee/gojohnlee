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
        Vector3 m_out, m_in;
        float m_t, m_limit, m_check;
        Vector3 m_for;
        bool m_walking, m_exiting; // Walk 안의 MoveToPoint가 Exit를 다시 부름
        public static int Opened, Entered, Exited, Closed;

        /// <summary>일꾼이 연 문(회차 313): 그 일꾼이 지붕 밖으로 나와 문에서 3m 넘게(지붕 아래여도 15m 넘게) 멀어지고 문틀에 아무도 없으면 닫는다. 플레이어가 연 문은 안 건드림.</summary>
        sealed class OpenedDoor { public Door Door; public WorkerNpc By; public Vector3 Mid; public float Sent = -99f; }
        static readonly System.Collections.Generic.List<OpenedDoor> s_opened = new System.Collections.Generic.List<OpenedDoor>();
        static float s_next;

        /// <summary>문 쪽으로 가는 중 — 부른 쪽은 "대상까지 멀면 걷기"를 건너뛴다(문으로 돌다 대상 2.5m 밖으로 나가면 다시 대상 쪽으로 당겨 오락가락, 회차 314).</summary>
        public bool Busy => m_phase == Phase.ToDoor || m_phase == Phase.Through;

        static int Mask =>s_mask != 0 ? s_mask : (s_mask = LayerMask.GetMask("piece"));

        /// <summary>사이를 건축물이 가리나(대상 자신·대상의 자식 충돌체는 뺌).</summary>
        public static bool Blocked(Character me, Component target)
        {
            if (me == null || target == null) return false;
            Vector3 from = me.transform.position + Vector3.up * 1.2f, to = target.transform.position + Vector3.up * 0.6f;
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
                m_phase = Phase.ToDoor; m_t = 0f;
                Plugin.Log.LogInfo($"DIAG {npc.NpcName} {target.name} 앞 벽 → 문 {m_door.name}({Utils.DistanceXZ(me, m_door.transform.position):0.0}m)으로");
            }
            int r = Walk(dt, npc, target.name);
            if (r != 2) return r == 1;
            Entered++;
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
            if (Utils.DistanceXZ(point, m_for) > 2f) { m_for = point; if (m_phase == Phase.GaveUp) m_phase = Phase.None; }
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
                ZNetView dv = d != null ? d.GetComponent<ZNetView>() : null;
                if (dv == null || !dv.IsValid()) return false; // 열린 문도 곧장 지나감 — 연 뒤 navmesh가 늦게 바뀌어 바닐라 길은 벽에 박힘(회차 315 시험)
                m_door = d;
                Vector3 n = d.transform.forward; n.y = 0f; n.Normalize();
                Vector3 mid = Middle(d), a = mid + n * Step, b = mid - n * Step;
                bool ra = Roofed(a), rb = Roofed(b);
                bool aIn = ra != rb ? ra : Utils.DistanceXZ(me, a) < Utils.DistanceXZ(me, b); // 지붕 아래 쪽이 안, 모호하면 가까운 쪽
                m_out = aIn ? a : b; m_in = aIn ? b : a; // 이름은 들어갈 때 기준: m_out = 다가갈 곳, m_in = 지나갈 곳
                m_limit = PhaseLimit + Utils.DistanceXZ(me, m_out) / 1.5f;
                m_phase = Phase.ToDoor; m_t = 0f;
                Plugin.Log.LogInfo($"DIAG {npc.NpcName} 안에서 문 {d.name}(열림 {Dungeons.IsOpen(dv.GetZDO())})({Utils.DistanceXZ(me, mid):0.0}m)으로 나감");
            }
            m_walking = m_exiting = true;
            int r;
            try { r = Walk(dt, npc, "나가기"); } finally { m_walking = false; }
            if (r != 2) return r == 1;
            Exited++;
            Plugin.Log.LogInfo($"DIAG {npc.NpcName} 문 지나 밖으로 ({m_t:0.0}초, 지붕 {Roofed(me)})");
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
                if (Utils.DistanceXZ(me, m_out) > 0.7f)
                {
                    // 안에서 나갈 땐 곧장(닫힌 집 안은 바닐라 길찾기가 10초 제자리, 회차 315 시험)
                    if (m_exiting) { Vector3 to = m_out - me; to.y = 0f; npc.Ai.MoveTowards(to.normalized, false); }
                    else npc.Ai.MoveToPoint(dt, m_out, 0.5f, false);
                    return 1;
                }
                if (Dungeons.Open(npc, m_door))
                {
                    Opened++;
                    s_opened.Add(new OpenedDoor { Door = m_door, By = npc, Mid = Middle(m_door) });
                }
                m_phase = Phase.Through; m_t = 0f;
            }
            ZNetView v = m_door != null ? m_door.GetComponent<ZNetView>() : null;
            if (v == null || !v.IsValid()) { GiveUp(npc, what, "문 사라짐"); return 0; }
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

        /// <summary>Workers.Update에서(서버만) 0.5초마다. UseDoor는 열린 문이면 닫는 토글이라 보낸 뒤 3초는 다시 안 보냄(두 번 가면 다시 열림).</summary>
        public static void CloseBehind()
        {
            if (s_opened.Count == 0 || Time.time < s_next) return;
            s_next = Time.time + 0.5f;
            for (int i = s_opened.Count - 1; i >= 0; i--)
            {
                var o = s_opened[i];
                ZNetView v = o.Door != null ? o.Door.GetComponent<ZNetView>() : null;
                if (v == null || !v.IsValid() || !Dungeons.IsOpen(v.GetZDO())) { s_opened.RemoveAt(i); continue; }
                if (Time.time - o.Sent < 3f) continue;
                if (o.By != null && !o.By.IsDead())
                {
                    Vector3 me = o.By.transform.position;
                    float d = Utils.DistanceXZ(me, o.Mid);
                    if (d < 3f || (d < 15f && Roofed(me))) continue; // 문 곁이거나 아직 지붕 아래(안) — 문 평면 반쪽으로는 못 가름: 집 뒤로 돈 일꾼도 안쪽 반에 있음(회차 313)
                }
                bool someone = false;
                foreach (var c in Character.GetAllCharacters())
                    if (c != null && !c.IsDead() && Utils.DistanceXZ(c.transform.position, o.Mid) < 1.5f && Mathf.Abs(c.transform.position.y - o.Mid.y) < 2f) { someone = true; break; }
                if (someone) continue;
                v.InvokeRPC("UseDoor", false); // 열린 문이면 방향 무관하게 s_state = 0
                o.Sent = Time.time;
                Closed++;
                Plugin.Log.LogInfo($"DIAG {(o.By != null ? o.By.NpcName : "?")} 나와서 문 {o.Door.name} 닫음");
            }
        }

        static bool Roofed(Vector3 at) => Physics.Raycast(at + Vector3.up * 1.8f, Vector3.up, 6f, Mask);

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
