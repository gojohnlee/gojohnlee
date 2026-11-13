using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 다친 곳 근처에서 회복 대기(요청 다침 ③, Core Recovery.Nearby): 집까지 가지 않고 둘레의 적 없는 줍기(버섯·열매·돌·나뭇가지 Pickable)를 하며
    /// 체력이 찰 때까지 기다린다. 체력이 차면 끝(대기열 "다침" 칸이 이어 감), 또 맞거나 적이 오거나 짐 칸이 차거나 주울 게 없으면 끝(→ 집 대기).
    /// 밭 작물(Crops)은 안 건드린다.
    /// </summary>
    public sealed class RecoverNearJob : IWorkerJob
    {
        /// <summary>줍기를 찾는 거리(다친 곳에서, m)·그 둘레에 적이 없어야 하는 거리(m).</summary>
        public const float Reach = 25f, SafeRadius = 25f;
        const float TimeLimit = 300f, SpotLimit = 15f, PickWait = 3f;

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        readonly Vector3 m_center;
        readonly HashSet<Pickable> m_skip = new HashSet<Pickable>();
        Pickable m_target;
        Vector3 m_pickAt;
        float m_time, m_spotTime, m_pickTime = -1f, m_lastHealth;
        int m_picked;

        public RecoverNearJob(WorkerNpc npc)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_center = npc.transform.position;
            m_lastHealth = npc.GetHealth();
        }

        /// <summary>근처에 적 없는 줍기가 있는지(Recovery.Choose의 safeGatherNear).</summary>
        public static bool SafePickNear(WorkerNpc npc)
        {
            if (Find(npc, npc.transform.position, null) != null) return true;
            Character foe = Foe(npc, npc.transform.position);
            Plugin.Log.LogInfo($"{npc.NpcName} 근처 회복 못 함: {(foe != null ? $"적 {foe.m_name} {Utils.DistanceXZ(foe.transform.position, npc.transform.position):0}m" : $"{Reach:0}m 안 안전한 줍기 없음")}");
            return false;
        }

        static bool FoeNear(WorkerNpc npc, Vector3 at) => Foe(npc, at) != null;

        static Character Foe(WorkerNpc npc, Vector3 at)
        {
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == npc || c.IsDead() || c.IsTamed()) continue;
                if (c.GetFaction() == Character.Faction.AnimalsVeg || c.GetBaseAI() is AnimalAI || !BaseAI.IsEnemy(npc, c)) continue; // 짐승은 Guard처럼 적으로 안 셈
                if (Utils.DistanceXZ(c.transform.position, at) < SafeRadius) return c;
            }
            return null;
        }

        static Pickable Find(WorkerNpc npc, Vector3 center, HashSet<Pickable> skip)
        {
            if (FoeNear(npc, npc.transform.position)) return null;
            Pickable best = null;
            float bestD = float.MaxValue;
            Vector3 me = npc.transform.position;
            foreach (Pickable p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None)) // 찾을 때만(FarmJob.Ripe와 같은 방식)
            {
                if (Utils.DistanceXZ(p.transform.position, center) > Reach) continue;
                if ( (skip != null && skip.Contains(p)) || p.m_itemPrefab == null || !p.CanBePicked()) continue;
                if (Crops.IsProduce(p.m_itemPrefab.name) || Mathf.Abs(p.transform.position.y - me.y) > 4f) continue;
                float d = Utils.DistanceXZ(p.transform.position, me);
                if (d < bestD && !FoeNear(npc, p.transform.position)) { bestD = d; best = p; }
            }
            return best;
        }

        public bool Update(float dt)
        {
            m_time += dt;
            float hp = m_npc.GetHealth();
            if (Core.Work.Recovery.IsReady(hp, m_npc.GetMaxHealth())) return End($"체력 참({m_time:0}초, 주운 {m_picked}번)");
            if (hp < m_lastHealth - 0.5f) return End("또 맞음 → 집 대기");
            m_lastHealth = hp;
            if (FoeNear(m_npc, m_npc.transform.position)) return End("적이 옴 → 집 대기");
            if (m_time > TimeLimit) return End("시간 다 됨 → 집 대기");
            if (m_npc.GetInventory().GetEmptySlots() == 0) return End("짐 칸 참 → 집 대기");
            if (m_pickTime >= 0f) return Collect(dt);
            if (m_target == null || !m_target.CanBePicked())
            {
                m_target = Find(m_npc, m_center, m_skip);
                m_spotTime = 0f;
                if (m_target == null) return End("근처에 안전한 줍기 없음 → 집 대기");
            }
            m_spotTime += dt;
            Vector3 at = m_target.transform.position;
            if (!m_ai.MoveToPoint(dt, at, 1.2f, false) && Utils.DistanceXZ(at, m_npc.transform.position) > 1.5f)
            {
                if (m_spotTime > SpotLimit) { m_skip.Add(m_target); m_target = null; }
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(at);
            m_target.Interact(m_npc, false, false); // 주인이면 RPC_Pick이 바로 떨군다(FarmJob과 같음)
            m_skip.Add(m_target);
            m_target = null;
            m_picked++;
            m_pickAt = at;
            m_pickTime = 0f;
            return true;
        }

        bool Collect(float dt)
        {
            m_pickTime += dt;
            if (m_pickTime < 0.6f) return true;
            int left = 0;
            foreach (Collider col in Physics.OverlapSphere(m_pickAt, 3f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d == null || !d.TryGetComponent(out ZNetView v) || !v.IsValid()) continue;
                if (!m_npc.PickDrop(d)) left++;
            }
            if (left == 0 && m_pickTime > 1.2f || m_pickTime > PickWait) m_pickTime = -1f;
            return true;
        }

        bool End(string why)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 근처 회복 끝: {why}");
            return false;
        }

        public int Picked => m_picked;
        public string StepName => m_pickTime >= 0f ? "Collect" : "Pick";
        public string StateLine() => "다쳐서 근처에서 쉬며 줍는 중";
        public void AvoidHere(Vector3 danger) { m_target = null; m_skip.Clear(); }
        public void Stop(bool pause) { }
        public void Resume() { m_lastHealth = m_npc.GetHealth(); }
        public string Label => "근처 회복";
    }
}
