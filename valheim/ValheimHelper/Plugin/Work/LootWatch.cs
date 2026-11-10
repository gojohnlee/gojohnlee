using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 잡은 짐승의 떨굼 기다림(사용자 11-11, 사냥 GatherJob·솎기 CullJob). 떨굼은 죽을 때 생긴 쓰러진 몸(Ragdoll)이
    /// 죽은 뒤 m_ttl초에 사라지며 그 몸의 평균 자리에 같은 호출로 생긴다(Ragdoll.Awake InvokeRepeating DestroyNow → SpawnLoot, 1.0.21).
    /// 그래서 시간은 죽은 순간부터 재고, 몸이 있는 동안은 몸을 본 때 + m_ttl + Late까지, 몸이 사라진 뒤엔 Late까지 기다린다.
    /// 몸은 굴러 미끄러지므로 Pos(찾을 자리)를 몸 자리로 고쳐 둔다.
    /// </summary>
    public sealed class LootWatch
    {
        /// <summary>몸이 사라진 뒤 떨굼이 보일 때까지 여유(같은 호출에 생김 — 주인이 손님이면 ZDO가 오는 데 걸림).</summary>
        public const float Late = 2f;
        /// <summary>죽은 자리 둘레에 몸을 못 찾으면(이미 사라졌거나 몸 없는 짐승) 이만큼만.</summary>
        public const float NoBody = 5f; // 회차 383 hunttest: 사냥은 죽음을 알아챈 뒤 1.3초에야 몸이 보임

        public Vector3 Pos;
        readonly string m_who;
        readonly float m_deadAt;
        float m_ttl = -1f, m_foundAt, m_goneAt = -1f, m_checkAt, m_seenAt = -1f;
        ZDOID m_body = ZDOID.None;

        public LootWatch(string who, Vector3 at)
        {
            m_who = who;
            Pos = at;
            m_deadAt = Time.time;
        }

        public float SinceDeath => Time.time - m_deadAt;

        /// <summary>떨굼이 아직 나올 수 있으면 true. 0.5초마다 몸을 본다.</summary>
        public bool Waiting()
        {
            if (Time.time >= m_checkAt) { m_checkAt = Time.time + 0.5f; Check(); }
            if (m_goneAt >= 0f) return Time.time - m_goneAt < Late;
            // 몸을 본 때부터: 몸은 죽고 조금 뒤 생기고, 주인이 아니면 1초마다 다시 해 본다(회차 383 사냥: m_ttl 2초인데 죽고 4.4초에 사라짐)
            if (m_ttl >= 0f) return Time.time - m_foundAt < m_ttl + Late;
            return SinceDeath < NoBody;
        }

        /// <summary>떨굼을 볼 때마다 부른다 — 처음이면 잰 값 로그 + true.</summary>
        public bool Seen(Vector3 drop)
        {
            if (m_seenAt >= 0f) return false;
            m_seenAt = Time.time;
            Plugin.Log.LogInfo($"{m_who} 떨굼 잼: 죽고 {SinceDeath:0.0}초에 보임(m_ttl {m_ttl:0.0}, 몸 사라지고 {(m_goneAt >= 0f ? Time.time - m_goneAt : -1f):0.0}초), 찾는 자리에서 {Vector3.Distance(drop, Pos):0.0}m");
            return true;
        }

        void Check()
        {
            if (m_goneAt >= 0f) return;
            if (m_body.IsNone())
            {
                // 죽을 때 생긴 몸: 죽은 자리 8m 안 가장 가까운 Ragdoll
                float best = float.MaxValue;
                foreach (var zdo in Senses.Nearby(Pos, 8f))
                {
                    ZNetView view = ZNetScene.instance.FindInstance(zdo);
                    if (view == null || !view.TryGetComponent(out Ragdoll r)) continue;
                    float d = Vector3.Distance(r.GetAverageBodyPosition(), Pos);
                    if (d < best) { best = d; m_body = zdo.m_uid; m_ttl = r.m_ttl; }
                }
                if (!m_body.IsNone()) { m_foundAt = Time.time; Plugin.Log.LogInfo($"{m_who} 떨굼 잼: 몸 찾음 m_ttl {m_ttl:0.0}초(죽고 {SinceDeath:0.0}초)"); }
                return;
            }
            ZDO body = ZDOMan.instance.GetZDO(m_body);
            ZNetView bv = body != null ? ZNetScene.instance.FindInstance(body) : null;
            if (bv != null && bv.TryGetComponent(out Ragdoll rag)) { Pos = rag.GetAverageBodyPosition(); return; }
            m_goneAt = Time.time;
            Plugin.Log.LogInfo($"{m_who} 떨굼 잼: 몸 사라짐 죽고 {SinceDeath:0.0}초(m_ttl {m_ttl:0.0}), 자리 {Pos}");
        }
    }
}
