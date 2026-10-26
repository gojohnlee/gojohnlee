using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 계획의 Load·Unload 단계 하나(⑥ 실행기 조각): 거점의 제련소·숯가마 같은 Smelter나 발효기(Fermenter)에 거점 상자 재료·연료를 넣거나(Load),
    /// 다 된 것을 꺼내 상자에 넣는다(Unload). 조리대·화덕(CookingStation, ⑧)은 두 배 시간이면 타므로 Load가 올리고 곁에서 기다렸다
    /// 익는 대로 꺼내 상자까지 넣는다(Unload는 남은 것만). 불이 꺼졌으면 거점 상자 연료로 한 번 지핀다(꺼 둔 불은 안 켬).
    /// 넣을 때 재료·연료는 한 프레임에, 열린 상자가 없을 때만 뺀다. 설비 자리가 모자라면 들어가는 만큼만(연료는 빈자리만큼만 빼서 낭비 없음).
    /// 발효기는 빈 것에 하나만 넣고, 지붕이 없으면(바닐라: 덮임 70% 미만·지붕 없음이면 시간이 멈춤) 넣지 않는다.
    /// 꺼낼 땐 완성품 줍기(소유권 요청 후 다음 프레임) → 짐 정리(Unload)로 상자에 넣는다. 발효기는 아직 발효 중이면 조금 기다렸다 따른다.
    /// </summary>
    public sealed class MachineStep
    {
        const float Limit = 120f, BusyLimit = 30f;
        /// <summary>조리대 한가운데에서 이만큼(수평 m)은 떨어져 선다: 밑 화로의 불 영역(FireBurn 구 반지름 0.45) + 몸 반지름(0.5) + 여유.</summary>
        const float CookKeepOff = 1.5f;
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
        CookingStation m_cook;
        bool m_cookLoaded;
        float m_fireWait = -1f;
        Unload m_store;
        float m_time, m_busy, m_tappedAt = -1f, m_nextDiag, m_nextChestCheck;
        int m_expect = -1, m_removed0;
        bool m_othersNear;

        public MachineStep(WorkerNpc npc, PlanStep step)
        {
            m_npc = npc;
            m_step = step;
            m_load = step.Kind == StepKind.Load;
        }

        /// <summary>Smelter·Fermenter·CookingStation 프리팹이면 true.</summary>
        public static bool Supported(string station)
        {
            GameObject p = station != null ? ZNetScene.instance.GetPrefab(station) : null;
            return p != null && (p.GetComponent<Smelter>() != null || p.GetComponent<Fermenter>() != null || p.GetComponent<CookingStation>() != null);
        }

        Component Machine => m_smelter != null ? m_smelter : m_fermenter != null ? m_fermenter : (Component)m_cook;

        public CraftResult Update(float dt)
        {
            m_time += dt;
            if (m_time > Limit + (m_cook != null ? CookSeconds() * 2f : 0f)) return CraftResult.Failed;
            WatchOthers();
            if (m_store != null)
            {
                if (m_store.Update(dt)) return CraftResult.Working;
                int left = LeaveSkipped();
                Plugin.Log.LogInfo($"{m_npc.NpcName} 꺼낸 것 정리 끝: 짐에 남은 {OutItem} {InBag()}"); // vh_crafttest 간헐 FAIL 진단(회차 35)
                Refill(left);
                return CraftResult.Done;
            }
            Vector3 home = m_npc.Home;
            if (Machine == null)
            {
                Find(home);
                if (Machine == null) return CraftResult.Failed;
            }
            if (!Machine.TryGetComponent(out ZNetView nview) || !nview.IsValid()) return CraftResult.Failed;
            if (m_cook != null && !m_load && CookSlotsOf(nview.GetZDO(), m_cook, false) == 0 && Drops().Count == 0) return CraftResult.Done; // 조리대는 Load가 다 꺼냄 — 남은 게 없으면 끝
            Vector3 at = Machine.transform.position;
            float d = Utils.DistanceXZ(at, m_npc.transform.position);
            if (d > 2.5f)
            { m_npc.Ai.MoveToPoint(dt, at, 1.8f, d > 10f); return CraftResult.Working; }
            // 바닐라 BaseAI.MoveTo가 정한 걷는 방향(Character.m_moveDir)은 StopMoving 전까지 남는다 → 안 멈추면 설비 한가운데로 계속 달려
            // 조리대 밑 화로 불(FireBurn) 속에서 불붙어 죽었다(회차 128 cooktest: 0.9m까지 들어감·불붙음).
            m_npc.Ai.StopMoving();
            m_npc.Ai.Hold("설비 앞 일");
            if (m_cook != null && d < CookKeepOff)
            {
                // 조리대 밑은 불: 처음부터 곁에 있었거나 밀려 너무 가까우면 먼저 물러선다
                Vector3 away = m_npc.transform.position - at;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -m_npc.transform.forward;
                m_npc.Ai.MoveTowards(away.normalized, false);
                return CraftResult.Working;
            }
            if (m_cook != null) return Cook(nview, home);
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
            var need = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(item, amount) };
            if (fuel > 0) need.Add(new KeyValuePair<string, int>(fuelPrefab, fuel));
            if (!CraftStep.TakeAll(home, chests, need)) { wait = CraftResult.Failed; return false; } // 덜 빠지면 되돌리고 설비에 안 넣음
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
                m_npc.PickDrop(drop);
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
            BeginStore();
            return CraftResult.Working;
        }

        double FermentLeft(ZDO zdo)
        {
            long start = zdo.GetLong(ZDOVars.s_startTime, 0L);
            if (start == 0L) return m_fermenter.m_fermentationDuration;
            return m_fermenter.m_fermentationDuration - (ZNet.instance.GetTime() - new System.DateTime(start)).TotalSeconds;
        }

        /// <summary>바닐라 Fermenter.UpdateCover와 같은 판단: 덮임 70% 미만이거나 지붕 아래가 아니면 발효 시간이 계속 처음으로 돌아감.</summary>
        internal static bool Exposed(Fermenter f)
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
                m_npc.PickDrop(drop);
            }
            if (any) return CraftResult.Working;
            if (zdo.GetInt(ZDOVars.s_spawnAmount) > 0 || zdo.GetInt(ZDOVars.s_queued) > 0) return CraftResult.Working; // 아직 도는 중·내보내는 중
            Plugin.Log.LogInfo($"{m_npc.NpcName} 설비 꺼냄 {m_step.Item}: 짐에 {InBag()}");
            BeginStore();
            return CraftResult.Working;
        }

        static readonly System.Func<CookingStation, bool> s_fireLit =
            HarmonyLib.AccessTools.MethodDelegate<System.Func<CookingStation, bool>>(HarmonyLib.AccessTools.Method(typeof(CookingStation), "IsFireLit"));

        /// <summary>조리대 한 번 굽는 시간(칸은 동시에 익음). 바닐라: 두 배를 넘기면 탄 것(m_overCookedItem)이 된다.</summary>
        float CookSeconds()
        {
            foreach (var c in m_cook.m_conversion)
                if (c.m_from != null && c.m_to != null && (c.m_from.gameObject.name == m_step.Item || c.m_to.gameObject.name == m_step.Item)) return c.m_cookTime;
            return 60f;
        }

        string CookedOf(string input)
        {
            foreach (var c in m_cook.m_conversion)
                if (c.m_from != null && c.m_to != null && c.m_from.gameObject.name == input) return c.m_to.gameObject.name;
            return input;
        }

        /// <summary>바닐라 ZDO 칸("slot"+i 이름, "slotstatus"+i: 0 덜 됨·1 익음·2 탐). done이면 꺼낼 수 있는(익거나 탄) 칸 수, 아니면 차 있는 칸 수.</summary>
        /// <summary>E014 진단: 화로 막힘(Fireplace.CheckUnderTerrain)의 연기 확인 — 연기 내는 곳 0.5m 안 단단한 물체 이름.</summary>
        static string SmokeDiag(Fireplace fp)
        {
            SmokeSpawner sp = fp.m_smokeSpawner;
            if (sp == null) return " 연기 없음";
            var names = new List<string>();
            foreach (Collider c in Physics.OverlapSphere(sp.transform.position, sp.m_testRadius, sp.m_testMask.value))
                names.Add(c.transform.root.name + "/" + LayerMask.LayerToName(c.gameObject.layer));
            return $" 연기 켜짐 {sp.gameObject.activeInHierarchy} 높이 {sp.transform.position.y - fp.transform.position.y:0.0} 걸림 [{string.Join(", ", names)}]";
        }

        static int CookSlotsOf(ZDO zdo, CookingStation cs, bool done)
        {
            int n = 0;
            for (int i = 0; i < cs.m_slots.Length; i++)
                if (zdo.GetString("slot" + i) != "" && (!done || zdo.GetInt("slotstatus" + i) != 0)) n++;
            return n;
        }

        /// <summary>
        /// 조리대: (Load면) 불을 확인하고 빈 칸만큼 올린 뒤 곁에서 기다려 익은(또는 탄) 것을 하나씩 꺼내 줍고, 칸이 비면 짐 정리로 상자에.
        /// 바닐라 RPC_RemoveDoneItem은 익거나 탄 첫 칸 하나를 사용자 쪽으로 내놓는다(NPC는 요리 스킬 덤 없음 = 1개).
        /// </summary>
        CraftResult Cook(ZNetView nview, Vector3 home)
        {
            ZDO zdo = nview.GetZDO();
            if (m_load && !m_cookLoaded)
            {
                if (!Burning(nview, home, out CraftResult wait)) return wait;
                int free = m_cook.m_slots.Length - CookSlotsOf(zdo, m_cook, false);
                int n = Mathf.Min(m_step.Amount, free);
                if (n <= 0 || CraftStep.Have(home, m_step.Item) < n) return CraftResult.Failed;
                if (!TakeFromChests(home, m_step.Item, n, null, 0, out wait)) return wait;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 조리대 올림 {m_step.Item} {n}" + CookSpotDiag());
                for (int i = 0; i < n; i++) nview.InvokeRPC("RPC_AddItem", m_step.Item, false);
                m_cookLoaded = true;
                return CraftResult.Working;
            }
            if (TestRunner.Running && m_time >= m_nextDiag) { m_nextDiag = m_time + 5f; Plugin.Log.LogInfo($"DIAG {m_npc.NpcName} 굽는 곁" + CookSpotDiag()); }
            bool any = false;
            foreach (ItemDrop drop in Drops())
            {
                any = true;
                m_npc.PickDrop(drop);
            }
            if (CookSlotsOf(zdo, m_cook, true) > 0) { nview.InvokeRPC("RPC_RemoveDoneItem", m_npc.transform.position, 1); return CraftResult.Working; }
            if (any) return CraftResult.Working;
            if (CookSlotsOf(zdo, m_cook, false) > 0) // 아직 굽는 중 — 불이 꺼지면 다시 지핀다
                return Burning(nview, home, out CraftResult w) ? CraftResult.Working : w;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 조리대 꺼냄 {OutItem}: 짐에 {InBag()}");
            BeginStore();
            return CraftResult.Working;
        }

        /// <summary>진단: 조리대 중심까지 거리·움직임 방향 크기·불붙음.</summary>
        string CookSpotDiag() =>
            $" (거리 {Utils.DistanceXZ(m_cook.transform.position, m_npc.transform.position):0.00}m, 움직임 {m_npc.GetMoveDir().magnitude:0.00}, 속도 {m_npc.GetVelocity().magnitude:0.00}, 불붙음 {m_npc.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning)})";

        /// <summary>
        /// 조리대가 굽고 있을 수 있으면 true. 아니면 한 번 지핀다(화덕 m_useFuel = 자기 연료, 조리대 = 3m 안 화로에 연료, 꺼 둔 불·연료 무한은 손대지 않음)
        /// — 거점 상자 연료로, 그래도 10초 안에 안 붙으면 Failed.
        /// </summary>
        bool Burning(ZNetView nview, Vector3 home, out CraftResult wait)
        {
            wait = CraftResult.Working;
            ZDO zdo = nview.GetZDO();
            if ((m_cook.m_requireFire && s_fireLit(m_cook)) || (m_cook.m_useFuel && zdo.GetFloat(ZDOVars.s_fuel) > 0f)) { m_fireWait = -1f; return true; }
            if (m_fireWait >= 0f)
            {
                if (m_time - m_fireWait < 10f) return false;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 조리대에 불이 없어 못 구움");
                foreach (Collider col in Physics.OverlapSphere(m_cook.transform.position, 3f))
                {
                    Fireplace fp = col.GetComponentInParent<Fireplace>();
                    if (fp == null || !fp.TryGetComponent(out ZNetView fv) || !fv.IsValid()) continue;
                    Plugin.Log.LogInfo($"DIAG 불 없음: {fp.name} 켬 {fv.GetZDO().GetInt(ZDOVars.s_state, 1)} 연료 {fv.GetZDO().GetFloat(ZDOVars.s_fuel):0.0} 탐 {fp.IsBurning()}"
                        + $" 날씨 {EnvMan.instance?.GetCurrentEnvironment()?.m_name} 젖음 {EnvMan.IsWet()} 바람 {EnvMan.instance?.GetWindIntensity():0.00}"
                        + $" 막힘 {HarmonyLib.Traverse.Create(fp).Field("m_blocked").GetValue<bool>()}" + SmokeDiag(fp));
                    // 바닐라: 비(지붕 없음)·센 바람(덮개 70% 미만)이면 화로가 젖어 약한 불만 남아 굽지 못한다(Fireplace.CheckWet) → 까닭을 말한다
                    if (fp.IsBurning() && (EnvMan.IsWet() || (EnvMan.instance != null && EnvMan.instance.GetWindIntensity() >= 0.8f))) m_npc.Say("cookWet");
                    break;
                }
                wait = CraftResult.Failed;
                return false;
            }
            m_fireWait = m_time;
            if (m_cook.m_useFuel && m_cook.m_fuelItem != null)
            {
                Feed(home, nview, m_cook.m_fuelItem, Mathf.Min(3, m_cook.m_maxFuel));
                return false;
            }
            foreach (Collider col in Physics.OverlapSphere(m_cook.transform.position, 3f))
            {
                Fireplace fp = col.GetComponentInParent<Fireplace>();
                if (fp == null || fp.m_infiniteFuel || !fp.m_canRefill || fp.m_fuelItem == null) continue;
                if (!fp.TryGetComponent(out ZNetView fv) || !fv.IsValid() || fv.GetZDO().GetInt(ZDOVars.s_state, 1) != 1) continue;
                if (fv.GetZDO().GetFloat(ZDOVars.s_fuel) >= 1f) continue;
                Feed(home, fv, fp.m_fuelItem, Mathf.Min(3, Mathf.FloorToInt(fp.m_maxFuel)));
                break;
            }
            return false;
        }

        void Feed(Vector3 home, ZNetView target, ItemDrop fuelItem, int n)
        {
            string fuel = Utils.GetPrefabName(fuelItem.gameObject);
            n = Mathf.Min(n, CraftStep.Have(home, fuel));
            if (n <= 0) return;
            var chests = Chests.NearBase(home, ChestRole.Mats);
            foreach (var c in chests) if (Chests.InUse(c)) return; // 열린 상자가 있으면 이번엔 안 지핌(10초 뒤 실패)
            n = CraftStep.Take(chests, fuel, n); // 그새 줄었으면 뺀 만큼만 지핌
            if (n <= 0) return;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 불 지핌 {fuel} {n}");
            for (int i = 0; i < n; i++) target.InvokeRPC("RPC_AddFuel");
        }

        /// <summary>꺼낼 것: 조리대 싣기 단계는 구운 것, 그 밖엔 단계 물건.</summary>
        string OutItem => m_cook != null && m_load ? CookedOf(m_step.Item) : m_step.Item;

        /// <summary>시험용: 이 실행 동안 소실 보충한 물건 수(vh_crafttest가 보존 칸에 보임).</summary>
        internal static int Refilled;

        /// <summary>
        /// 꺼낸 것을 상자에 정리하기 시작: 지금 (짐 + 거점 상자 전부)의 결과물 수를 기억한다(E001 소실 보충, 사용자 결정 10-23 "오류 정리 꼼수").
        /// </summary>
        void BeginStore()
        {
            m_store = new Unload(m_npc);
            string name = CraftStep.SharedName(OutItem);
            if (name == null) return;
            Chests.Watch(name);
            m_removed0 = Chests.Removed(name);
            m_expect = InBag() + Chests.CountAll(m_npc.Home, inv => inv.CountItems(name));
            if (TestLose > 0 && TestRunner.Running && InBag() >= TestLose)
            {
                m_npc.GetInventory().RemoveItem(name, TestLose); // 소실 흉내(vh_crafttest)
                Plugin.Log.LogInfo($"시험: {OutItem} {TestLose}개 짐에서 지움(소실 흉내)");
                TestLose = 0;
            }
        }

        /// <summary>일꾼이 죽었다(PlanJob.OnDied): 짐이 묘비로 가 정리 시작 때 센 수가 맞지 않으니 이 단계의 소실 보충은 하지 않는다(복제 방지).</summary>
        public void OnDied() => m_expect = -1;

        /// <summary>시험용: 0보다 크면 다음 정리 시작 때 짐의 결과물을 그만큼 지운다(소실 흉내). 쓰면 0.</summary>
        internal static int TestLose;

        /// <summary>
        /// 이 단계 동안 플레이어가 5m 안에 왔거나(주웠을 수 있음) 정리 중 거점 상자를 누가 열었으면 보충하지 않는다(복제 방지, [기본안] 10-23).
        /// 상자 확인은 1초마다.
        /// </summary>
        void WatchOthers()
        {
            if (m_othersNear) return;
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && Vector3.Distance(p.transform.position, m_npc.transform.position) < 5f) { m_othersNear = true; return; }
            if (m_store == null || m_time < m_nextChestCheck) return;
            m_nextChestCheck = m_time + 1f;
            foreach (var c in Chests.NearBase(m_npc.Home))
                if (c.IsInUse() || (c.TryGetComponent(out ZNetView v) && v.IsValid() && v.GetZDO().GetInt(ZDOVars.s_inUse) == 1)) { m_othersNear = true; return; }
        }

        /// <summary>
        /// 정리 끝: (짐 + 거점 상자 + 상자 앞에 둔 것 + 그새 다른 일이 상자에서 뺀 것)이 정리 시작 때보다 적으면 모자란 만큼 만들어 상자에 넣는다
        /// (못 넣으면 상자 앞 땅). 로그 "보충 N개"로 소실이 일어났음을 센다. 개수를 아는 설비 꺼내기만 — 캐기·벌목 드롭은 안 함.
        /// </summary>
        void Refill(int leftAtChest)
        {
            string name = CraftStep.SharedName(OutItem);
            if (m_expect < 0 || name == null) return;
            int now = InBag() + Chests.CountAll(m_npc.Home, inv => inv.CountItems(name)) + leftAtChest + (Chests.Removed(name) - m_removed0);
            int lost = m_expect - now;
            if (lost <= 0) return;
            if (m_othersNear)
            {
                Plugin.Log.LogWarning($"{m_npc.NpcName} {OutItem} {lost}개 모자람 — 플레이어가 곁에 있었거나 상자가 열려 보충 안 함");
                return;
            }
            int left = CraftStep.Put(m_npc.Home, OutItem, lost);
            if (left > 0) CraftStep.LeaveAtChest(m_npc, OutItem, left, ChestRole.Mats);
            Refilled += lost;
            Plugin.Log.LogWarning($"{m_npc.NpcName} 보충 {lost}개 {OutItem} (정리 시작 {m_expect}, 끝 {now}) — E001 소실");
        }

        /// <summary>꺼낸 것 중 상자가 꽉 차 못 넣은 것(들고 다닐 미드 몫은 빼고)은 짐에 두지 않고 상자 앞 땅에(사용자 결정 2026-10-22). 둔 수를 돌려준다.</summary>
        int LeaveSkipped()
        {
            int total = 0;
            HashSet<ItemDrop.ItemData> keep = Prep.Kept(m_npc);
            Inventory mine = m_npc.GetInventory();
            foreach (var it in new List<ItemDrop.ItemData>(m_store.Skipped))
            {
                if (it.m_dropPrefab == null || it.m_dropPrefab.name != OutItem || !mine.ContainsItem(it)) continue;
                ChestRole role = Chests.RoleFor(it);
                if (Chests.NearBase(m_npc.Home, role).Count == 0) continue; // 상자가 아예 없으면 들고 있음(예전대로)
                int n = keep.Contains(it) ? Prep.ExtraMeads(m_npc, it) : it.m_stack;
                if (n <= 0) continue;
                mine.RemoveItem(it, n);
                CraftStep.LeaveAtChest(m_npc, OutItem, n, role);
                total += n;
            }
            return total;
        }

        int InBag()
        {
            string name = CraftStep.SharedName(OutItem);
            return name == null ? 0 : m_npc.GetInventory().CountItems(name);
        }

        List<ItemDrop> Drops()
        {
            var list = new List<ItemDrop>();
            Transform outPoint = m_smelter != null ? m_smelter.m_outputPoint : m_fermenter != null ? m_fermenter.m_outputPoint : m_cook.m_spawnPoint;
            string burnt = m_cook != null && m_cook.m_overCookedItem != null ? m_cook.m_overCookedItem.name : null;
            string want = OutItem;
            Vector3 p = outPoint != null ? outPoint.position : Machine.transform.position;
            foreach (Collider col in Physics.OverlapSphere(p, 4f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d != null && !list.Contains(d) && d.TryGetComponent(out ZNetView v) && v.IsValid() && (Utils.GetPrefabName(d.gameObject) == want || Utils.GetPrefabName(d.gameObject) == burnt)) list.Add(d);
            }
            return list;
        }

        /// <summary>
        /// 거점 125m 안에서 이 설비(프리팹 이름 = Station)를 고른다. 제련소: 넣을 땐 자리가 가장 넉넉한 것, 꺼낼 땐 일하는·결과가 있는 것 먼저.
        /// 발효기: 넣을 땐 빈 것, 꺼낼 땐 이 결과(m_step.Item)가 들어 있는 것 중 가장 오래 발효한 것.
        /// 조리대: 넣을 땐 빈 칸이 가장 많은 것, 꺼낼 땐 차 있는 칸이 가장 많은 것.
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
                CookingStation k = s == null && f == null ? col.GetComponentInParent<CookingStation>() : null;
                Component c = s != null ? s : f != null ? f : (Component)k;
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
                if (k != null)
                {
                    int used = CookSlotsOf(z, k, false);
                    int score = m_load ? k.m_slots.Length - used : used;
                    if (score > bestScore) { bestScore = score; m_cook = k; }
                    continue;
                }
                int content = z.GetInt(ZDOVars.s_content);
                if (m_load) { if (content == 0) { m_fermenter = f; return; } continue; }
                if (content == 0 || FermentOutput(f, content) != m_step.Item) continue;
                long start = z.GetLong(ZDOVars.s_startTime, 0L);
                if (start < bestStart) { bestStart = start; m_fermenter = f; }
            }
        }

        /// <summary>거점 125m 안에 이 발효기(station)가 있고 output이 다 익은 게 하나라도 있으면 true. 발효기가 아닌 설비면 false.</summary>
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

        /// <summary>거점 125m 안에 이 제련소·숯가마(station)가 하나 이상 있고 모두 넣은 것이 0(다 돎)이면 true. Smelter가 아니거나 없으면 false.</summary>
        public static bool SmeltersIdle(Vector3 home, string station)
        {
            bool found = false;
            var seen = new HashSet<Smelter>();
            foreach (Collider col in Physics.OverlapSphere(home, Recipes.BaseRange, Recipes.MachineMask))
            {
                Smelter s = col.GetComponentInParent<Smelter>();
                if (s == null || !seen.Add(s) || Utils.GetPrefabName(s.gameObject) != station) continue;
                if (!s.TryGetComponent(out ZNetView nv) || !nv.IsValid()) continue;
                if (nv.GetZDO().GetInt(ZDOVars.s_queued) > 0) return false;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// 재시작 복원(PlanSave.Load): 곁에서 지키던 조리대 넣기가 진행 중이었을 때 이미 올렸는지 — 거점의 그 조리대 칸에 이 물건(날것·익은 것·탄 것)이
        /// 있으면 true(넣기는 끝으로 보고 꺼내기 단계가 마저 꺼냄). 짐의 익은 것은 안 본다(일꾼이 음식으로 들고 다님 — 잘못 끝으로 볼 수 있음).
        /// </summary>
        public static bool AlreadyCooking(Vector3 home, PlanStep step)
        {
            if (step.Kind != StepKind.Load || step.AttendSeconds <= 0f) return false;
            foreach (Collider col in Physics.OverlapSphere(home, Recipes.BaseRange, Recipes.MachineMask))
            {
                CookingStation k = col.GetComponentInParent<CookingStation>();
                if (k == null || Utils.GetPrefabName(k.gameObject) != step.Station) continue;
                if (!k.TryGetComponent(out ZNetView nv) || !nv.IsValid()) continue;
                string cooked = null;
                foreach (var c in k.m_conversion)
                    if (c.m_from != null && c.m_to != null && c.m_from.gameObject.name == step.Item) cooked = c.m_to.gameObject.name;
                string burnt = k.m_overCookedItem != null ? k.m_overCookedItem.name : null;
                ZDO z = nv.GetZDO();
                for (int i = 0; i < k.m_slots.Length; i++)
                {
                    string it = z.GetString("slot" + i);
                    if (it != "" && (it == step.Item || it == cooked || it == burnt)) return true;
                }
            }
            return false;
        }

        internal static string FermentOutput(Fermenter f, int contentHash)
        {
            foreach (var c in f.m_conversion)
                if (c.m_from != null && c.m_to != null && c.m_from.gameObject.name.GetStableHashCode() == contentHash)
                    return Utils.GetPrefabName(c.m_to.gameObject);
            return null;
        }
    }
}
