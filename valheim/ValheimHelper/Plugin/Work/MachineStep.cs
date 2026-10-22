using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 계획의 Load·Unload 단계 하나(⑥ 실행기 조각): 거점의 제련소·숯가마 같은 Smelter나 발효기(Fermenter)에 거점 상자 재료·연료를 넣거나(Load),
    /// 다 된 것을 꺼내 상자에 넣는다(Unload). 화덕(CookingStation)은 아직 없음(Failed → 그에 기댄 단계는 Blocked).
    /// 넣을 때 재료·연료는 한 프레임에, 열린 상자가 없을 때만 뺀다. 설비 자리가 모자라면 들어가는 만큼만(연료는 빈자리만큼만 빼서 낭비 없음).
    /// 발효기는 빈 것에 하나만 넣고, 지붕이 없으면(바닐라: 덮임 70% 미만·지붕 없음이면 시간이 멈춤) 넣지 않는다.
    /// 꺼낼 땐 완성품 줍기(소유권 요청 후 다음 프레임) → 짐 정리(Unload)로 상자에 넣는다. 발효기는 아직 발효 중이면 조금 기다렸다 따른다.
    /// </summary>
    public sealed class MachineStep
    {
        const float Limit = 120f, BusyLimit = 30f;
        /// <summary>꺼내러 왔는데 발효가 이보다 오래 남았으면 실패(계획 시계와 너무 어긋남).</summary>
        const float MaxFermentWait = 900f;
        /// <summary>RPC_Tap 뒤 바닐라는 m_tapDelay(1.5초) 뒤에 내놓는다 → 그보다 넉넉히 줍기를 기다린다.</summary>
        const float TapWait = 3f;
        /// <summary>시험용(vh_crafttest): 시험 월드에 집을 짓지 않고 발효기 지붕 판단을 건너뜀. 시험 중에만 뜻이 있음.</summary>
        public static bool TestIgnoreRoof;

        readonly WorkerNpc m_npc;
        readonly PlanStep m_step;
        readonly bool m_load;
        Smelter m_smelter;
        Fermenter m_fermenter;
        Unload m_store;
        float m_time, m_busy, m_tappedAt = -1f, m_nextDiag;

        public MachineStep(WorkerNpc npc, PlanStep step)
        {
            m_npc = npc;
            m_step = step;
            m_load = step.Kind == StepKind.Load;
        }

        /// <summary>Smelter·Fermenter 프리팹이면 true(화덕은 아직).</summary>
        public static bool Supported(string station)
        {
            GameObject p = station != null ? ZNetScene.instance.GetPrefab(station) : null;
            return p != null && (p.GetComponent<Smelter>() != null || p.GetComponent<Fermenter>() != null);
        }

        Component Machine => m_smelter != null ? m_smelter : (Component)m_fermenter;

        public CraftResult Update(float dt)
        {
            m_time += dt;
            if (m_time > Limit) return CraftResult.Failed;
            if (m_store != null)
            {
                if (m_store.Update(dt)) return CraftResult.Working;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 꺼낸 것 정리 끝: 짐에 남은 {m_step.Item} {InBag()}"); // vh_crafttest 간헐 FAIL 진단(회차 35)
                return CraftResult.Done;
            }
            Vector3 home = m_npc.Home;
            if (Machine == null)
            {
                Find(home);
                if (Machine == null) return CraftResult.Failed;
            }
            if (!Machine.TryGetComponent(out ZNetView nview) || !nview.IsValid()) return CraftResult.Failed;
            Vector3 at = Machine.transform.position;
            if (Utils.DistanceXZ(at, m_npc.transform.position) > 2.5f)
            { m_npc.Ai.MoveToPoint(dt, at, 1.8f, false); return CraftResult.Working; }
            if (m_fermenter != null) return m_load ? FermentLoad(nview, home) : FermentUnload(nview, dt);
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
            if (!TakeFromChests(home, m_step.Item, ore, fuelPrefab, fuel, out CraftResult wait)) return wait;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 설비 넣기 {m_step.Item} {ore}, 연료 {fuel}");
            for (int i = 0; i < ore; i++) nview.InvokeRPC("RPC_AddOre", m_step.Item, false);
            for (int i = 0; i < fuel; i++) nview.InvokeRPC("RPC_AddFuel");
            return CraftResult.Done;
        }

        /// <summary>열린 상자가 있으면 기다리고(BusyLimit 넘으면 Failed) false, 없으면 한 프레임에 빼고 true.</summary>
        bool TakeFromChests(Vector3 home, string item, int amount, string fuelPrefab, int fuel, out CraftResult wait)
        {
            var chests = Chests.NearBase(home, ChestRole.Mats);
            foreach (var c in chests)
                if (Chests.InUse(c))
                {
                    m_busy += Time.deltaTime;
                    wait = m_busy > BusyLimit ? CraftResult.Failed : CraftResult.Working;
                    return false;
                }
            CraftStep.Take(chests, item, amount);
            if (fuel > 0) CraftStep.Take(chests, fuelPrefab, fuel);
            wait = CraftResult.Done;
            return true;
        }

        CraftResult FermentLoad(ZNetView nview, Vector3 home)
        {
            if (nview.GetZDO().GetInt(ZDOVars.s_content) != 0) return CraftResult.Failed; // 그새 누가 채움
            if (Exposed(m_fermenter))
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 발효기에 지붕이 없어 안 넣음");
                return CraftResult.Failed;
            }
            if (CraftStep.Have(home, m_step.Item) < 1) return CraftResult.Failed;
            if (!TakeFromChests(home, m_step.Item, 1, null, 0, out CraftResult wait)) return wait;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 발효기 넣기 {m_step.Item} 1");
            nview.InvokeRPC("RPC_AddItem", m_step.Item.GetStableHashCode(), false);
            return CraftResult.Done;
        }

        CraftResult FermentUnload(ZNetView nview, float dt)
        {
            ZDO zdo = nview.GetZDO();
            bool any = false;
            var drops = Drops();
            foreach (ItemDrop drop in drops)
            {
                any = true;
                if (drop.CanPickup()) m_npc.Pickup(drop.gameObject);
                else drop.RequestOwn();
            }
            if (any && m_time >= m_nextDiag)
            {
                m_nextDiag = m_time + 5f;
                var d0 = drops[0];
                Plugin.Log.LogInfo($"DIAG 발효기 줍기: 떨어진 것 {drops.Count}, 첫째 거리 {Vector3.Distance(d0.transform.position, m_npc.transform.position):0.0}m, CanPickup {d0.CanPickup()}, 주인 {d0.GetComponent<ZNetView>()?.IsOwner()}, 짐 빈칸 {m_npc.GetInventory().GetEmptySlots()}");
            }
            // 회차 57 한 번: 따른 뒤 60초 동안 6개를 못 주움(원인 못 찾음, 위 DIAG) → 오래 남으면 그 자리로 걸어간다
            if (any && m_tappedAt >= 0f && m_time - m_tappedAt > 8f) m_npc.Ai.MoveToPoint(dt, drops[0].transform.position, 0.5f, false);
            if (any) return CraftResult.Working;
            if (m_tappedAt < 0f && zdo.GetInt(ZDOVars.s_content) != 0)
            {
                double left = FermentLeft(zdo);
                if (left > 0)
                {
                    if (left > MaxFermentWait || Exposed(m_fermenter))
                    {
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 발효 덜 됨({left:0}초 남음) — 꺼내기 실패");
                        return CraftResult.Failed;
                    }
                    m_time -= dt; // 발효가 끝나길 기다리는 동안은 시간 제한을 쓰지 않음
                    return CraftResult.Working;
                }
                nview.InvokeRPC("RPC_Tap");
                m_tappedAt = m_time;
                return CraftResult.Working;
            }
            if (m_tappedAt >= 0f && m_time - m_tappedAt < TapWait) return CraftResult.Working;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 발효기 꺼냄 {m_step.Item}: 짐에 {InBag()}");
            m_store = new Unload(m_npc);
            return CraftResult.Working;
        }

        double FermentLeft(ZDO zdo)
        {
            long start = zdo.GetLong(ZDOVars.s_startTime, 0L);
            if (start == 0L) return m_fermenter.m_fermentationDuration;
            return m_fermenter.m_fermentationDuration - (ZNet.instance.GetTime() - new System.DateTime(start)).TotalSeconds;
        }

        /// <summary>바닐라 Fermenter.UpdateCover와 같은 판단: 덮임 70% 미만이거나 지붕 아래가 아니면 발효 시간이 계속 처음으로 돌아감.</summary>
        static bool Exposed(Fermenter f)
        {
            if (TestRunner.Running && TestIgnoreRoof) return false;
            Vector3 p = f.m_roofCheckPoint != null ? f.m_roofCheckPoint.position : f.transform.position;
            Cover.GetCoverForPoint(p, out float cover, out bool underRoof);
            return cover < 0.7f || !underRoof;
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
            Plugin.Log.LogInfo($"{m_npc.NpcName} 설비 꺼냄 {m_step.Item}: 짐에 {InBag()}");
            m_store = new Unload(m_npc);
            return CraftResult.Working;
        }

        int InBag()
        {
            string name = CraftStep.SharedName(m_step.Item);
            return name == null ? 0 : m_npc.GetInventory().CountItems(name);
        }

        List<ItemDrop> Drops()
        {
            var list = new List<ItemDrop>();
            Transform outPoint = m_smelter != null ? m_smelter.m_outputPoint : m_fermenter.m_outputPoint;
            Vector3 p = outPoint != null ? outPoint.position : Machine.transform.position;
            foreach (Collider col in Physics.OverlapSphere(p, 4f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d != null && !list.Contains(d) && Utils.GetPrefabName(d.gameObject) == m_step.Item) list.Add(d);
            }
            return list;
        }

        /// <summary>
        /// 거점 30m 안에서 이 설비(프리팹 이름 = Station)를 고른다. 제련소: 넣을 땐 자리가 가장 넉넉한 것, 꺼낼 땐 일하는·결과가 있는 것 먼저.
        /// 발효기: 넣을 땐 빈 것, 꺼낼 땐 이 결과(m_step.Item)가 들어 있는 것 중 가장 오래 발효한 것.
        /// </summary>
        void Find(Vector3 home)
        {
            int bestScore = int.MinValue;
            long bestStart = long.MaxValue;
            var seen = new HashSet<Component>();
            foreach (Collider col in Physics.OverlapSphere(home, Recipes.BaseRange, Recipes.MachineMask))
            {
                Smelter s = col.GetComponentInParent<Smelter>();
                Fermenter f = s == null ? col.GetComponentInParent<Fermenter>() : null;
                Component c = s != null ? s : (Component)f;
                if (c == null || !seen.Add(c) || Utils.GetPrefabName(c.gameObject) != m_step.Station) continue;
                if (!c.TryGetComponent(out ZNetView nv) || !nv.IsValid()) continue;
                ZDO z = nv.GetZDO();
                if (s != null)
                {
                    int queued = z.GetInt(ZDOVars.s_queued);
                    int score = m_load ? s.m_maxOre - queued : queued + z.GetInt(ZDOVars.s_spawnAmount);
                    if (score > bestScore) { bestScore = score; m_smelter = s; }
                    continue;
                }
                int content = z.GetInt(ZDOVars.s_content);
                if (m_load) { if (content == 0) { m_fermenter = f; return; } continue; }
                if (content == 0 || FermentOutput(f, content) != m_step.Item) continue;
                long start = z.GetLong(ZDOVars.s_startTime, 0L);
                if (start < bestStart) { bestStart = start; m_fermenter = f; }
            }
        }

        /// <summary>거점 30m 안에 이 발효기(station)가 있고 output이 다 익은 게 하나라도 있으면 true. 발효기가 아닌 설비면 false.</summary>
        public static bool FermentReady(Vector3 home, string station, string output)
        {
            foreach (Collider col in Physics.OverlapSphere(home, Recipes.BaseRange, Recipes.MachineMask))
            {
                Fermenter f = col.GetComponentInParent<Fermenter>();
                if (f == null || Utils.GetPrefabName(f.gameObject) != station) continue;
                if (!f.TryGetComponent(out ZNetView nv) || !nv.IsValid()) continue;
                ZDO z = nv.GetZDO();
                int content = z.GetInt(ZDOVars.s_content);
                long start = z.GetLong(ZDOVars.s_startTime, 0L);
                if (content == 0 || start == 0L || FermentOutput(f, content) != output) continue;
                if ((ZNet.instance.GetTime() - new System.DateTime(start)).TotalSeconds > f.m_fermentationDuration) return true;
            }
            return false;
        }

        static string FermentOutput(Fermenter f, int contentHash)
        {
            foreach (var c in f.m_conversion)
                if (c.m_from != null && c.m_to != null && c.m_from.gameObject.name.GetStableHashCode() == contentHash)
                    return Utils.GetPrefabName(c.m_to.gameObject);
            return null;
        }
    }
}
