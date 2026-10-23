using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 밤사이 정산 가) 아침 정리(요청 R3): 잠자기 넘김이 끝난 순간 호스트에서 한 번, 넘김이 시작될 때 거점(깃발 30m 안)에 있던
    /// 쉬는 일꾼(Idle·Home)만. 보는 사람이 없으니 걷지 않고 바로 한다. 일꾼마다 그 아침 날짜(vh_settled)를 ZDO에 남겨 두 번 안 함.
    /// 짐 → 상자(Unload를 걷기 없이), 제련소류(Smelter) 결과물 → 상자·상자 광석·연료로 다시 채움. 다음 조각: 발효기, 밭.
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
                Machines(npc, out int taken, out int filled);
                StoreBag(npc); // 땅에서 주운 결과물
                if (taken > 0) parts.Add(npc.Line("settle.out", new Dictionary<string, string> { ["n"] = taken.ToString() }));
                if (filled > 0) parts.Add(npc.Line("settle.fill", new Dictionary<string, string> { ["n"] = filled.ToString() }));
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
        static void Machines(WorkerNpc npc, out int taken, out int filled)
        {
            taken = filled = 0;
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
