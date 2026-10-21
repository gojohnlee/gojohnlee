using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 계획의 Load·Unload 단계 하나(⑥ 실행기 조각): 거점의 제련소·숯가마 같은 Smelter에 거점 상자 재료·연료를 넣거나(Load),
    /// 다 된 것을 꺼내 상자에 넣는다(Unload). Smelter만 한다(Fermenter·화덕은 Failed → 그에 기댄 단계는 Blocked).
    /// 넣을 때 재료·연료는 한 프레임에, 열린 상자가 없을 때만 뺀다. 설비 자리가 모자라면 들어가는 만큼만(연료는 빈자리만큼만 빼서 낭비 없음).
    /// 꺼낼 땐 완성품 줍기(소유권 요청 후 다음 프레임) → 짐 정리(Unload)로 상자에 넣는다.
    /// </summary>
    public sealed class MachineStep
    {
        const float Limit = 120f, BusyLimit = 30f;

        readonly WorkerNpc m_npc;
        readonly PlanStep m_step;
        readonly bool m_load;
        Smelter m_smelter;
        Unload m_store;
        float m_time, m_busy;

        public MachineStep(WorkerNpc npc, PlanStep step)
        {
            m_npc = npc;
            m_step = step;
            m_load = step.Kind == StepKind.Load;
        }

        /// <summary>Smelter 프리팹이면 true(Fermenter·화덕은 아직).</summary>
        public static bool Supported(string station)
        {
            GameObject p = station != null ? ZNetScene.instance.GetPrefab(station) : null;
            return p != null && p.GetComponent<Smelter>() != null;
        }

        public CraftResult Update(float dt)
        {
            m_time += dt;
            if (m_time > Limit) return CraftResult.Failed;
            if (m_store != null) return m_store.Update(dt) ? CraftResult.Working : CraftResult.Done;
            Vector3 home = m_npc.Home;
            if (m_smelter == null)
            {
                m_smelter = Find(home);
                if (m_smelter == null) return CraftResult.Failed;
            }
            if (!m_smelter.TryGetComponent(out ZNetView nview) || !nview.IsValid()) return CraftResult.Failed;
            Vector3 at = m_smelter.transform.position;
            if (Utils.DistanceXZ(at, m_npc.transform.position) > 2.5f)
            { m_npc.Ai.MoveToPoint(dt, at, 1.8f, false); return CraftResult.Working; }
            return m_load ? DoLoad(nview, home) : DoUnload(nview);
        }

        CraftResult DoLoad(ZNetView nview, Vector3 home)
        {
            ZDO zdo = nview.GetZDO();
            int room = m_smelter.m_maxOre - zdo.GetInt(ZDOVars.s_queued);
            int ore = Mathf.Min(m_step.Amount, room);
            if (ore <= 0) return CraftResult.Failed;
            int fuel = 0;
            if (m_smelter.m_fuelItem != null && m_step.FuelAmount > 0)
                fuel = Mathf.Min(m_step.FuelAmount, Mathf.FloorToInt(m_smelter.m_maxFuel - 1 - zdo.GetFloat(ZDOVars.s_fuel)) + 1);
            if (CraftStep.Have(home, m_step.Item) < ore) return CraftResult.Failed;
            string fuelPrefab = fuel > 0 ? Utils.GetPrefabName(m_smelter.m_fuelItem.gameObject) : null;
            if (fuel > 0 && CraftStep.Have(home, fuelPrefab) < fuel) return CraftResult.Failed;
            var chests = Chests.NearBase(home);
            foreach (var c in chests)
                if (Chests.InUse(c)) { m_busy += Time.deltaTime; return m_busy > BusyLimit ? CraftResult.Failed : CraftResult.Working; }
            CraftStep.Take(chests, m_step.Item, ore);
            if (fuel > 0) CraftStep.Take(chests, fuelPrefab, fuel);
            for (int i = 0; i < ore; i++) nview.InvokeRPC("RPC_AddOre", m_step.Item, false);
            for (int i = 0; i < fuel; i++) nview.InvokeRPC("RPC_AddFuel");
            return CraftResult.Done;
        }

        CraftResult DoUnload(ZNetView nview)
        {
            ZDO zdo = nview.GetZDO();
            if (zdo.GetInt(ZDOVars.s_spawnAmount) > 0) nview.InvokeRPC("RPC_EmptyProcessed");
            bool any = false;
            foreach (ItemDrop drop in Drops())
            {
                any = true;
                if (drop.CanPickup()) m_npc.Pickup(drop.gameObject);
                else drop.RequestOwn();
            }
            if (any) return CraftResult.Working;
            if (zdo.GetInt(ZDOVars.s_spawnAmount) > 0 || zdo.GetInt(ZDOVars.s_queued) > 0) return CraftResult.Working; // 아직 도는 중·내보내는 중
            m_store = new Unload(m_npc);
            return CraftResult.Working;
        }

        List<ItemDrop> Drops()
        {
            var list = new List<ItemDrop>();
            Vector3 p = m_smelter.m_outputPoint != null ? m_smelter.m_outputPoint.position : m_smelter.transform.position;
            foreach (Collider col in Physics.OverlapSphere(p, 4f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d != null && !list.Contains(d) && Utils.GetPrefabName(d.gameObject) == m_step.Item) list.Add(d);
            }
            return list;
        }

        /// <summary>거점 30m 안에서 이 설비(프리팹 이름 = Station)를 고른다. 넣을 땐 자리가 가장 넉넉한 것, 꺼낼 땐 일하는·결과가 있는 것 먼저.</summary>
        Smelter Find(Vector3 home)
        {
            Smelter best = null;
            int bestScore = int.MinValue;
            var seen = new HashSet<Smelter>();
            foreach (Collider col in Physics.OverlapSphere(home, Recipes.BaseRange, LayerMask.GetMask("piece", "piece_nonsolid")))
            {
                Smelter s = col.GetComponentInParent<Smelter>();
                if (s == null || !seen.Add(s) || Utils.GetPrefabName(s.gameObject) != m_step.Station) continue;
                if (!s.TryGetComponent(out ZNetView nv) || !nv.IsValid()) continue;
                ZDO z = nv.GetZDO();
                int queued = z.GetInt(ZDOVars.s_queued);
                int score = m_load ? s.m_maxOre - queued : queued + z.GetInt(ZDOVars.s_spawnAmount);
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }
    }
}
