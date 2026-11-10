using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 쉴 때 벌통 꿀 거두기(사용자 11-09 요청 가): 거점(일꾼 집 Chests.BaseRange) 안 벌통 중 꿀이 MinHoney 이상인 것마다 걸어가
    /// 바닐라 RPC_Extract(벌통 주인이 꿀 단계만큼 Honey를 떨구고 0으로)를 부르고 떨군 꿀을 주운 뒤 돌아와 상자에 넣는다(Unload).
    /// Beehive.Interact는 플레이어 통계·PrivateArea를 보므로 쓰지 않고 RPC를 바로 부른다(게임 사실: codemap).
    /// </summary>
    public sealed class HoneyJob : IWorkerJob
    {
        const float TotalLimit = 5f * 60f, SpotLimit = 15f, HomeLimit = 180f, PickWait = 4f;
        /// <summary>이만큼 찬 벌통만 간다(최대 4, 바닐라 m_maxHoney).</summary>
        internal const int MinHoney = 2;

        enum Step { Hive, Home, Store }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        readonly List<Beehive> m_hives;
        Step m_step = Step.Hive;
        float m_time, m_stepTime, m_pickTime = -1f;
        Vector3 m_pickAt;
        Unload m_unload;
        bool m_abort, m_told;

        /// <summary>주운 꿀 수.</summary>
        public int Got { get; private set; }
        public string StepName => "Honey" + m_step;
        public string Label => m_npc.Line("job.honey");
        public string StateLine() => m_npc.Line("state.honey", new Dictionary<string, string> { ["n"] = Got.ToString() });
        public void Stop(bool pause) { } // 남기는 것 없음(11-10 확인)
        public void Resume() { }
        public void AvoidHere(Vector3 danger) => m_abort = true;

        public HoneyJob(WorkerNpc npc, List<Beehive> hives)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_hives = hives;
        }

        internal static int Level(Beehive b)
        {
            ZNetView v = b != null ? b.GetComponent<ZNetView>() : null;
            return v != null && v.IsValid() ? v.GetZDO().GetInt(ZDOVars.s_level) : 0;
        }

        /// <summary>집 기준 거점 안, 꿀이 MinHoney 이상인 벌통.</summary>
        internal static List<Beehive> Ready(Vector3 home)
        {
            var list = new List<Beehive>();
            foreach (var b in Object.FindObjectsByType<Beehive>(FindObjectsSortMode.None))
                if (b != null && Utils.DistanceXZ(b.transform.position, home) < Chests.BaseRange && Level(b) >= MinHoney) list.Add(b);
            return list;
        }

        void Go(Step s)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 꿀: {m_step} → {s} (꿀 {Got})");
            m_step = s;
            m_stepTime = 0f;
            if (s == Step.Store) m_unload = new Unload(m_npc);
            m_ai.StopMoving();
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_stepTime += dt;
            if ((m_time > TotalLimit || m_abort) && m_step < Step.Home) Go(Step.Home);
            switch (m_step)
            {
                case Step.Hive: return UpdateHive(dt);
                case Step.Home: return UpdateHome(dt);
                case Step.Store:
                    if (m_unload.Update(dt)) return true;
                    Tell();
                    return false;
            }
            return false;
        }

        bool UpdateHive(float dt)
        {
            if (m_pickTime >= 0f) return Collect(dt);
            m_hives.RemoveAll(x => x == null || Level(x) == 0);
            if (m_hives.Count == 0) { Go(Step.Home); return true; }
            Vector3 me = m_npc.transform.position;
            m_hives.Sort((a, b) => Utils.DistanceXZ(a.transform.position, me).CompareTo(Utils.DistanceXZ(b.transform.position, me)));
            Beehive h = m_hives[0];
            Vector3 at = h.transform.position;
            if (!m_ai.MoveToPoint(dt, at, 1.5f, false) && Utils.DistanceXZ(at, me) > 2f)
            {
                if (m_stepTime > SpotLimit)
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 벌통 건너뜀: 못 닿음 {at}");
                    m_hives.Remove(h);
                    m_stepTime = 0f;
                }
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(at);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 벌통 꿀 {Level(h)} 거둠 {at}");
            h.GetComponent<ZNetView>().InvokeRPC("RPC_Extract");
            m_hives.Remove(h);
            m_pickAt = h.m_spawnPoint != null ? h.m_spawnPoint.position : at;
            m_pickTime = 0f;
            return true;
        }

        /// <summary>떨군 꿀을 줍는다(FarmJob.Collect와 같은 꼴). 못 주운 건 땅에 남음(로그).</summary>
        bool Collect(float dt)
        {
            m_pickTime += dt;
            if (m_pickTime < 0.6f) return true;
            int left = 0;
            foreach (Collider col in Physics.OverlapSphere(m_pickAt, 3f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d == null || !d.TryGetComponent(out ZNetView v) || !v.IsValid() || Utils.GetPrefabName(d.gameObject) != "Honey") continue;
                int n = d.m_itemData.m_stack;
                if (m_npc.PickDrop(d)) Got += n;
                else left++;
            }
            if (left == 0 && m_pickTime > 1.2f || m_pickTime > PickWait)
            {
                if (left > 0) Plugin.Log.LogInfo($"{m_npc.NpcName} 꿀 {left}개 못 주움(짐 빈칸 {m_npc.GetInventory().GetEmptySlots()})");
                m_pickTime = -1f;
                m_stepTime = 0f;
                if (left > 0 && m_npc.GetInventory().GetEmptySlots() == 0) m_hives.Clear();
            }
            return true;
        }

        bool UpdateHome(float dt)
        {
            Vector3 home = m_npc.Home;
            if (m_ai.MoveToPoint(dt, home, 2.5f, true) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) { Go(Step.Store); return true; }
            if (m_stepTime > HomeLimit)
            {
                if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return true; }
                Go(Step.Store);
            }
            return true;
        }

        void Tell()
        {
            if (m_told || Got == 0) return;
            m_told = true;
            m_npc.Say("honeyDone", new Dictionary<string, string> { ["n"] = Got.ToString() });
        }
    }
}
