using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 밤사이 정산 가) 아침 정리(요청 R3): 잠자기 넘김이 끝난 순간 호스트에서 한 번, 넘김이 시작될 때 거점(깃발 30m 안)에 있던
    /// 쉬는 일꾼(Idle·Home)만. 보는 사람이 없으니 걷지 않고 바로 한다. 일꾼마다 그 아침 날짜(vh_settled)를 ZDO에 남겨 두 번 안 함.
    /// 짐 → 상자(Unload를 걷기 없이), 제련소류(Smelter) 결과물 → 상자·상자 광석·연료로 다시 채움, 발효기 따르고 다시 채움, 거점 밭의 다 자란 작물 → 상자·같은 자리 다시 심기.
    /// </summary>
    public static class MorningSettle
    {
        public const string KeyDay = "vh_settled";

        static readonly NightSettle s_skip = new NightSettle();
        static readonly HashSet<ZDOID> s_atBase = new HashSet<ZDOID>();
        /// <summary>설비 정리는 거점 것이라 한 아침에 첫 일꾼 한 명만 한다.</summary>
        static bool s_machinesDone;

        /// <summary>시험이 넘김 없이 정산을 부를 때 쓴 마지막 결과(일꾼 이름 → 요약). 시험만 읽음.</summary>
        public static readonly Dictionary<string, string> LastResult = new Dictionary<string, string>();

        public static void Tick()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || EnvMan.instance == null) return;
            switch (s_skip.Step(EnvMan.instance.IsTimeSkipping()))
            {
                case NightSettle.Edge.Started: Snapshot(); break;
                case NightSettle.Edge.Ended: SettleAll(EnvMan.instance.GetDay()); break;
            }
        }

        /// <summary>넘김이 시작될 때 거점에 있는 일꾼을 기억한다(밤에 밖에 나간 일꾼은 빠짐).</summary>
        public static void Snapshot()
        {
            s_atBase.Clear();
            foreach (var npc in WorkerNpc.All)
                if (npc != null && npc.Zdo != null && NightSettle.AtBase(Utils.DistanceXZ(npc.transform.position, npc.Home))) s_atBase.Add(npc.Zdo.m_uid);
            Plugin.Log.LogInfo($"밤사이 정산: 넘김 시작, 거점에 있는 일꾼 {s_atBase.Count}명");
        }

        public static void SettleAll(int day)
        {
            LastResult.Clear();
            s_machinesDone = false;
            foreach (var npc in new List<WorkerNpc>(WorkerNpc.All))
            {
                if (npc == null || npc.Zdo == null || !s_atBase.Contains(npc.Zdo.m_uid)) continue;
                try { Settle(npc, day); }
                catch (System.Exception e) { Plugin.Log.LogError($"밤사이 정산 오류 {npc.NpcName}: {e}"); }
            }
            s_atBase.Clear();
        }

        static void Settle(WorkerNpc npc, int day)
        {
            ZDO zdo = npc.Zdo;
            if (!NightSettle.Due(day, zdo.GetInt(KeyDay, -1))) { Plugin.Log.LogInfo($"밤사이 정산: {npc.NpcName} {day}일 아침은 이미 함"); return; }
            if (!NightSettle.AtBase(Utils.DistanceXZ(npc.transform.position, npc.Home))) { Plugin.Log.LogInfo($"밤사이 정산: {npc.NpcName} 거점 밖이라 건너뜀"); return; }
            var mode = npc.Ai.Current;
            if (mode != WorkerAI.Mode.Idle && mode != WorkerAI.Mode.Home) { Plugin.Log.LogInfo($"밤사이 정산: {npc.NpcName} 일하는 중({mode})이라 건너뜀"); return; }
            zdo.Set(KeyDay, day); // 먼저 적는다: 정리 도중 오류가 나도 같은 아침에 되풀이하지 않음
            var parts = new List<string>();
            int stored = StoreBag(npc);
            if (stored > 0) parts.Add(npc.Line("settle.bag", new Dictionary<string, string> { ["n"] = stored.ToString() }));
            if (!s_machinesDone)
            {
                s_machinesDone = true;
                Machines(npc, out int taken, out int filled, out int tapped, out int brewed, out int picked, out int planted);
                StoreBag(npc); // 땅에서 주운 결과물
                if (taken > 0) parts.Add(npc.Line("settle.out", new Dictionary<string, string> { ["n"] = taken.ToString() }));
                if (filled > 0) parts.Add(npc.Line("settle.fill", new Dictionary<string, string> { ["n"] = filled.ToString() }));
                if (tapped > 0) parts.Add(npc.Line("settle.tap", new Dictionary<string, string> { ["n"] = tapped.ToString() }));
                if (brewed > 0) parts.Add(npc.Line("settle.brew", new Dictionary<string, string> { ["n"] = brewed.ToString() }));
                if (picked > 0) parts.Add(npc.Line("settle.harvest", new Dictionary<string, string> { ["n"] = picked.ToString() }));
                if (planted > 0) parts.Add(npc.Line("settle.plant", new Dictionary<string, string> { ["n"] = planted.ToString() }));
            }
            string summary = string.Join(", ", parts);
            LastResult[npc.NpcName] = summary;
            Plugin.Log.LogInfo($"밤사이 정산: {npc.NpcName} {day}일 아침 — {(parts.Count == 0 ? "할 것 없음" : summary)}");
            if (parts.Count > 0) npc.Say("settle", new Dictionary<string, string> { ["what"] = summary });
        }

        /// <summary>
        /// 거점 30m 안 제련소류(Smelter: 제련소·용광로·숯가마·풍차·물레 등): 아직 안 내놓은 결과물(ZDO s_spawnAmount)은 상자에 바로,
        /// 밤새 내놓아 땅에 떨어진 결과물은 주워 짐에(뒤에 StoreBag이 상자로). 다시 채우기는 연료를 쓰는 것만 — 숯가마·풍차·물레는
        /// 나무·보리·아마를 시키지 않은 데 쓰게 돼 꺼내기만(기본안). 넣은 광석은 다시 못 꺼내니 연료로 다 녹일 수 있는 만큼만 넣는다.
        /// 다른 일꾼이 일하는 중이면(설비를 쓰고 있을 수 있음) 안 건드리고, 설비 ZDO가 이 컴퓨터 것이 아니면 그 설비는 건너뜀.
        /// </summary>
        static void Machines(WorkerNpc npc, out int taken, out int filled, out int tapped, out int brewed, out int picked, out int planted)
        {
            taken = filled = tapped = brewed = picked = planted = 0;
            foreach (var w in WorkerNpc.All)
                if (w != null && w.Ai != null && w.Ai.Current == WorkerAI.Mode.Work)
                { Plugin.Log.LogInfo($"밤사이 정산: {w.NpcName}이 일하는 중이라 설비는 안 건드림"); return; }
            Vector3 home = npc.Home;
            var seen = new HashSet<Smelter>();
            foreach (Collider col in Physics.OverlapSphere(home, Recipes.BaseRange, Recipes.MachineMask))
            {
                Smelter s = col.GetComponentInParent<Smelter>();
                if (s == null || !seen.Add(s) || !s.TryGetComponent(out ZNetView nv) || !nv.IsValid()) continue;
                if (!nv.IsOwner()) { Plugin.Log.LogInfo($"밤사이 정산: {s.name} 주인이 다른 컴퓨터라 건너뜀"); continue; }
                int got = TakeOut(npc, s, nv.GetZDO());
                int put = s.m_maxFuel > 0 && s.m_maxOre > 0 && s.m_fuelItem != null ? Refill(home, s, nv) : 0;
                if (got > 0 || put > 0) Plugin.Log.LogInfo($"밤사이 정산: {Utils.GetPrefabName(s.gameObject)} 꺼냄 {got}, 넣음 {put}");
                taken += got;
                filled += put;
            }
            Fermenters(home, ref tapped, ref brewed);
            Farms(home, ref picked, ref planted);
        }

        /// <summary>
        /// 발효기(가3): 다 익은 것은 바닐라 RPC_Tap처럼 기록(s_content·s_startTime·s_cheatedQueued)을 지우고 결과물을 땅 대신 상자에.
        /// 빈 발효기는 지붕이 있으면(MachineStep.Exposed가 아니면) 상자 재료로 다시 채움 — 방금 딴 것과 같은 재료 먼저, 없으면 발효기가 받는 아무 재료.
        /// 익는 중인 것은 안 건드림. 꺼낸 결과물 수(tapped)와 넣은 재료 수(put)를 더한다.
        /// </summary>
        static void Fermenters(Vector3 home, ref int tapped, ref int put)
        {
            var seen = new HashSet<Fermenter>();
            foreach (Collider col in Physics.OverlapSphere(home, Recipes.BaseRange, Recipes.MachineMask))
            {
                Fermenter f = col.GetComponentInParent<Fermenter>();
                if (f == null || !seen.Add(f) || !f.TryGetComponent(out ZNetView nv) || !nv.IsValid()) continue;
                if (!nv.IsOwner()) { Plugin.Log.LogInfo($"밤사이 정산: {f.name} 주인이 다른 컴퓨터라 건너뜀"); continue; }
                ZDO zdo = nv.GetZDO();
                int content = zdo.GetInt(ZDOVars.s_content);
                string last = null;
                if (content != 0)
                {
                    long start = zdo.GetLong(ZDOVars.s_startTime, 0L);
                    if (start == 0L || (ZNet.instance.GetTime() - new System.DateTime(start)).TotalSeconds <= f.m_fermentationDuration) continue; // 익는 중
                    Fermenter.ItemConversion conv = f.m_conversion.Find(c => c.m_from != null && c.m_to != null && c.m_from.gameObject.name.GetStableHashCode() == content);
                    if (conv == null) continue; // 모르는 내용물은 안 건드림
                    zdo.Set(ZDOVars.s_content, 0);
                    zdo.Set(ZDOVars.s_startTime, 0);
                    zdo.Set(ZDOVars.s_cheatedQueued, false);
                    string made = Utils.GetPrefabName(conv.m_to.gameObject);
                    int rest = CraftStep.Put(home, made, conv.m_producedItems);
                    if (rest > 0)
                    {
                        var mats = Chests.NearBase(home, ChestRole.Mats);
                        CraftStep.DropAt(mats.Count > 0 ? mats[0] : null, home, made, rest);
                    }
                    tapped += conv.m_producedItems;
                    last = Utils.GetPrefabName(conv.m_from.gameObject);
                    Plugin.Log.LogInfo($"밤사이 정산: 발효기 따름 {made} {conv.m_producedItems}");
                }
                if (MachineStep.Exposed(f)) { Plugin.Log.LogInfo("밤사이 정산: 발효기에 지붕이 없어 안 채움"); continue; }
                var chests = Chests.NearBase(home, ChestRole.Mats);
                if (chests.Exists(Chests.InUse)) { Plugin.Log.LogInfo("밤사이 정산: 열린 상자가 있어 발효기 채우기는 건너뜀"); continue; }
                string from = last != null && CraftStep.Have(home, last) > 0 ? last : null;
                if (from == null)
                    foreach (var c in f.m_conversion)
                        if (c.m_from != null && CraftStep.Have(home, Utils.GetPrefabName(c.m_from.gameObject)) > 0) { from = Utils.GetPrefabName(c.m_from.gameObject); break; }
                if (from == null) continue;
                if (!CraftStep.TakeAll(home, chests, new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(from, 1) })) continue;
                nv.InvokeRPC("RPC_AddItem", from.GetStableHashCode(), false);
                put++;
                Plugin.Log.LogInfo($"밤사이 정산: 발효기 채움 {from}");
            }
        }

        /// <summary>
        /// 밭(가4): 거점 가까운 밭(중심이 깃발 NightSettle.FarmRadius(100m)+반경 안 — 먼 밭은 안 함)의 다 자란 작물을 바닐라 RPC_Pick처럼 셈하되
        /// 땅에 떨구지 않고 상자에(자리가 없으면 상자 앞 땅), 기록은 RPC_SetPicked(작물은 사라짐). 그 자리에 같은 작물을 상자 씨앗으로 다시 심음
        /// (빈 자리 새로 심기는 안 함 — 기본안, 갈린 땅이 풀렸으면 다시 갈고). 씨앗이 없거나 자리가 안 맞으면(지붕·바이옴 등) 거두기만.
        /// </summary>
        static void Farms(Vector3 home, ref int picked, ref int planted)
        {
            foreach (var f in Brain.Farms.Fields)
            {
                if (!NightSettle.FarmNear(Utils.DistanceXZ(new Vector3(f.X, 0f, f.Z), home), f.Radius)) continue;
                var chests = Chests.NearBase(home, ChestRole.Mats);
                if (chests.Exists(Chests.InUse)) { Plugin.Log.LogInfo("밤사이 정산: 열린 상자가 있어 밭은 건너뜀"); return; }
                foreach (Pickable p in FarmJob.Ripe(f))
                {
                    if (!p.TryGetComponent(out ZNetView nv) || !nv.IsValid()) continue;
                    if (!nv.IsOwner()) { Plugin.Log.LogInfo($"밤사이 정산: {p.name} 주인이 다른 컴퓨터라 건너뜀"); continue; }
                    string grown = Utils.GetPrefabName(p.gameObject);
                    Crop crop = Crops.All.Find(c => c.Grown == grown);
                    Vector3 at = p.transform.position;
                    var got = new List<KeyValuePair<string, int>>
                    {
                        new KeyValuePair<string, int>(p.m_itemPrefab.name,
                            p.m_dontScale ? p.m_amount : Mathf.Max(p.m_minAmountScaled, Game.instance.ScaleDrops(p.m_itemPrefab, p.m_amount))),
                    };
                    if (!p.m_extraDrops.IsEmpty())
                        foreach (ItemDrop.ItemData d in p.m_extraDrops.GetDropListItems())
                            if (d.m_dropPrefab != null) got.Add(new KeyValuePair<string, int>(Utils.GetPrefabName(d.m_dropPrefab), d.m_stack));
                    nv.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true); // 먼저 거둔 것으로: 도중 오류가 나도 두 번 안 나옴
                    foreach (var kv in got)
                    {
                        int rest = CraftStep.Put(home, kv.Key, kv.Value);
                        if (rest > 0) CraftStep.DropAt(chests.Count > 0 ? chests[0] : null, home, kv.Key, rest);
                        picked += kv.Value;
                    }
                    bool again = crop != null && Replant(home, chests, crop, at);
                    if (again) planted++;
                    Plugin.Log.LogInfo($"밤사이 정산: 밭 거둠 {grown} {got[0].Value}{(again ? ", 다시 심음" : "")}");
                }
            }
        }

        static bool Replant(Vector3 home, List<Container> chests, Crop crop, Vector3 at)
        {
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            GameObject sapling = ZNetScene.instance.GetPrefab(crop.Sapling);
            // 자리 참은 안 봄: 방금 거둔 작물이 이 프레임 끝에야 지워져 충돌체가 남아 있음(원래 심었던 자리)
            string why = sapling == null ? "새싹 없음" : FarmJob.Unusable(at, crop, space: false);
            Heightmap hm = Heightmap.FindHeightmap(at);
            bool till = hm == null || !hm.IsCultivated(at);
            if (why == null && till && Crops.CultivatePrefab == null) why = "갈기 조각 없음";
            int cost = Mathf.Max(1, crop.SeedCost);
            if (why == null && CraftStep.Have(home, crop.Seed) < cost) why = "씨앗 없음";
            if (why == null && !CraftStep.TakeAll(home, chests, new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(crop.Seed, cost) })) why = "씨앗 못 꺼냄";
            if (why != null) { Plugin.Log.LogInfo($"밤사이 정산: {crop.Sapling} 다시 안 심음({why})"); return false; }
            if (till) Object.Instantiate(Crops.CultivatePrefab, at, Quaternion.identity);
            Object.Instantiate(sapling, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            return true;
        }

        static int TakeOut(WorkerNpc npc, Smelter s, ZDO zdo)
        {
            int n = 0;
            int left = zdo.GetInt(ZDOVars.s_spawnAmount);
            string made = OutputOf(s, zdo.GetString(ZDOVars.s_spawnOre));
            if (left > 0 && made != null)
            {
                // 바닐라 SpawnProcessed처럼 기록을 지우고 내놓되, 땅 대신 상자에(자리가 없으면 상자 앞 땅)
                zdo.Set(ZDOVars.s_spawnOre, "");
                zdo.Set(ZDOVars.s_spawnAmount, 0);
                int rest = CraftStep.Put(npc.Home, made, left);
                if (rest > 0)
                {
                    var chests = Chests.NearBase(npc.Home, ChestRole.Mats);
                    CraftStep.DropAt(chests.Count > 0 ? chests[0] : null, npc.Home, made, rest);
                }
                n += left;
            }
            var outs = new HashSet<string>();
            foreach (var c in s.m_conversion) if (c.m_to != null) outs.Add(Utils.GetPrefabName(c.m_to.gameObject));
            Vector3 p = s.m_outputPoint != null ? s.m_outputPoint.position : s.transform.position;
            var drops = new HashSet<ItemDrop>();
            foreach (Collider col in Physics.OverlapSphere(p, 3f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d == null || !drops.Add(d) || !outs.Contains(Utils.GetPrefabName(d.gameObject))) continue;
                int stack = d.m_itemData.m_stack;
                if (npc.PickDrop(d)) n += stack;
            }
            return n;
        }

        static string OutputOf(Smelter s, string ore)
        {
            if (string.IsNullOrEmpty(ore)) return null;
            foreach (var c in s.m_conversion)
                if (c.m_from != null && c.m_to != null && Utils.GetPrefabName(c.m_from.gameObject) == ore) return Utils.GetPrefabName(c.m_to.gameObject);
            return null;
        }

        /// <summary>상자 광석을 빈자리만큼, 연료는 넣은 광석을 다 녹일 만큼(설비 연료 상한 안). 넣은 광석 수를 돌려준다.</summary>
        static int Refill(Vector3 home, Smelter s, ZNetView nv)
        {
            ZDO zdo = nv.GetZDO();
            var chests = Chests.NearBase(home, ChestRole.Mats);
            foreach (var c in chests)
                if (Chests.InUse(c)) { Plugin.Log.LogInfo("밤사이 정산: 열린 상자가 있어 설비 채우기는 건너뜀"); return 0; }
            string fuelPrefab = Utils.GetPrefabName(s.m_fuelItem.gameObject);
            float fuel = zdo.GetFloat(ZDOVars.s_fuel);
            int queued = zdo.GetInt(ZDOVars.s_queued);
            // RPC_AddFuel은 상한을 안 본다(사람 손 OnAddFuel만 봄) → 넘치지 않게 여기서 자름
            int fuelCan = Mathf.Min(CraftStep.Have(home, fuelPrefab), Mathf.Max(0, Mathf.FloorToInt(s.m_maxFuel - fuel)));
            int room = s.m_maxOre - queued;
            if (s.m_fuelPerProduct > 0) room = Mathf.Min(room, Mathf.FloorToInt((fuel + fuelCan) / s.m_fuelPerProduct) - queued);
            var need = new List<KeyValuePair<string, int>>();
            int ore = 0;
            foreach (var c in s.m_conversion)
            {
                if (room - ore <= 0) break;
                if (c.m_from == null) continue;
                string from = Utils.GetPrefabName(c.m_from.gameObject);
                if (need.Exists(kv => kv.Key == from)) continue;
                int n = Mathf.Min(room - ore, CraftStep.Have(home, from));
                if (n > 0) { need.Add(new KeyValuePair<string, int>(from, n)); ore += n; }
            }
            if (ore <= 0) return 0;
            int fuelAdd = Mathf.Clamp(Mathf.CeilToInt((queued + ore) * s.m_fuelPerProduct - fuel), 0, fuelCan);
            var take = new List<KeyValuePair<string, int>>(need);
            if (fuelAdd > 0) take.Add(new KeyValuePair<string, int>(fuelPrefab, fuelAdd));
            if (!CraftStep.TakeAll(home, chests, take)) return 0; // 덜 빠지면 되돌리고 안 넣음
            foreach (var kv in need)
                for (int i = 0; i < kv.Value; i++) nv.InvokeRPC("RPC_AddOre", kv.Key, false);
            for (int i = 0; i < fuelAdd; i++) nv.InvokeRPC("RPC_AddFuel");
            return ore;
        }

        /// <summary>짐 정리를 걷지 않고 바로 한다. 넣은 물건 수를 돌려준다.</summary>
        static int StoreBag(WorkerNpc npc)
        {
            int before = Count(npc);
            var u = new Unload(npc) { Instant = true };
            for (int i = 0; i < 200 && u.Update(1f); i++) { }
            return Mathf.Max(0, before - Count(npc));
        }

        static int Count(WorkerNpc npc)
        {
            int n = 0;
            foreach (var it in npc.GetInventory().GetAllItems()) n += it.m_stack;
            return n;
        }
    }
}
