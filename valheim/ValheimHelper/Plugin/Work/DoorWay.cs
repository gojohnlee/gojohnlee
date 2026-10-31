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
        const float PhaseLimit = 8f, DoorSearch = 15f, Step = 1.3f;
        static int s_mask;
        enum Phase { None, ToDoor, Through, GaveUp }
        Phase m_phase;
        Door m_door;
        Vector3 m_out, m_in;
        float m_t, m_limit;
        public static int Opened, Entered;

        static int Mask => s_mask != 0 ? s_mask : (s_mask = LayerMask.GetMask("piece"));

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
                m_door = Nearest(target.transform.position);
                if (m_door == null) { GiveUp(npc, target, "곁에 문 없음"); return false; }
                Vector3 n = m_door.transform.forward; n.y = 0f; n.Normalize();
                Vector3 mid = Middle(m_door);
                if (Vector3.Dot(n, target.transform.position - mid) > 0f) n = -n; // n = 대상 반대쪽(일꾼이 벽 옆줄에 서면 일꾼 쪽은 모호)
                m_out = mid + n * Step;
                m_in = mid - n * Step;
                m_limit = PhaseLimit + Utils.DistanceXZ(me, m_out) / 1.5f;
                m_phase = Phase.ToDoor; m_t = 0f;
                Plugin.Log.LogInfo($"DIAG {npc.NpcName} {target.name} 앞 벽 → 문 {m_door.name}({Utils.DistanceXZ(me, m_door.transform.position):0.0}m)으로");
            }
            m_t += dt;
            if (m_t > (m_phase == Phase.ToDoor ? m_limit : PhaseLimit)) { GiveUp(npc, target, $"{m_phase} {m_t:0}초, 문 앞까지 {Utils.DistanceXZ(me, m_out):0.0}m·안쪽까지 {Utils.DistanceXZ(me, m_in):0.0}m"); return false; }
            if (m_phase == Phase.ToDoor)
            {
                if (Utils.DistanceXZ(me, m_out) > 0.7f) { npc.Ai.MoveToPoint(dt, m_out, 0.5f, false); return true; }
                if (Dungeons.Open(npc, m_door)) Opened++;
                m_phase = Phase.Through; m_t = 0f;
            }
            ZNetView v = m_door != null ? m_door.GetComponent<ZNetView>() : null;
            if (v == null || !v.IsValid()) { GiveUp(npc, target, "문 사라짐"); return false; }
            if (!Dungeons.IsOpen(v.GetZDO())) { npc.Ai.StopMoving(); return true; } // 열리는 중(주인 RPC 기다림)
            Vector3 go = m_in - me; go.y = 0f; // 높이 성분이 있으면 smooth 이동의 moveDir.y로 새어 거의 안 걸음(회차 311)
            if (Utils.DistanceXZ(me, m_in) > 0.5f)
            {
                npc.Ai.MoveTowards(go.normalized, false);
                return true;
            }
            Entered++;
            Plugin.Log.LogInfo($"DIAG {npc.NpcName} 문 지나 안으로 ({m_t:0.0}초, 나 {me:F1}·문틀 {Middle(m_door):F1}·문 {m_door.transform.position:F1}·바깥 {m_out:F1}·{target.name}까지 {Utils.DistanceXZ(me, target.transform.position):0.0}m)");
            m_phase = Phase.None;
            return Blocked(npc, target) ? Retry(npc, target) : false;
        }

        bool Retry(WorkerNpc npc, Component target) { GiveUp(npc, target, "안에서도 가림"); return false; }

        void GiveUp(WorkerNpc npc, Component target, string why)
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
            Plugin.Log.LogInfo($"DIAG {npc.NpcName} {target.name} 문 길 포기: {why} → 그 자리에서");
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

        static Door Nearest(Vector3 at)
        {
            Door best = null; float bd = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(at, DoorSearch, Mask))
            {
                Door d = c.GetComponentInParent<Door>();
                if (d == null || d.m_keyItem != null) continue;
                float dd = Utils.DistanceXZ(at, d.transform.position);
                if (dd < bd) { bd = dd; best = d; }
            }
            return best;
        }
    }
}
