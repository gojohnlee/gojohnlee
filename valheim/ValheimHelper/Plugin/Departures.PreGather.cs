using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>미리 채집(사용자 11-18, Core PreGatherBook): 쉬는 일꾼 맨 끝 — 최근 꺼내 쓴 캘 수 있는 물건을 25개 안팎까지.</summary>
    public static partial class Departures
    {
        static readonly PreGatherBook s_pre = new PreGatherBook();

        /// <summary>시험 중엔 이것이 켜졌을 때만 셈(vh_chorestest 7) — 앞 단계의 시험 준비가 상자를 비우는 것을 쓴 것으로 안 봄.</summary>
        public static bool TestPreGather;

        /// <summary>자동 보충 틱(1분)마다: 거점 상자 + 일꾼 가방의 캘 수 있는 물건 합을 적는다(줄어든 것 = 쓴 것).</summary>
        static void ObservePre()
        {
            WorkerNpc any = WorkerNpc.All.Find(n => n != null && !n.IsDead());
            if (!ChoresOn || !Features.IdleChores || TestHook.Running && !TestPreGather) { s_pre.Reset(); return; }
            if (any == null || ObjectDB.instance == null) return;
            var totals = new Dictionary<string, int>();
            void Count(Inventory inv)
            {
                foreach (var it in inv.GetAllItems())
                {
                    if (it.m_dropPrefab == null) continue;
                    string name = it.m_dropPrefab.name;
                    if (!totals.ContainsKey(name) && (ResourceCatalog.WaysFor(name).Count == 0 || !Material(name))) continue;
                    totals.TryGetValue(name, out int n);
                    totals[name] = n + it.m_stack;
                }
            }
            foreach (var c in Chests.NearBase(any.Home)) Count(c.GetInventory());
            foreach (var n in WorkerNpc.All) if (n != null && !n.IsDead()) Count(n.GetInventory());
            s_pre.Observe(totals, Time.time);
        }

        static HashSet<string> s_materials;

        /// <summary>제작법·건축(망치 등 m_buildPieces) 재료인지 — 동전처럼 캐도 안 쓰는 것은 미리 안 캠.</summary>
        static bool Material(string name)
        {
            if (s_materials == null)
            {
                s_materials = new HashSet<string>();
                void Add(Piece.Requirement[] reqs) { if (reqs != null) foreach (var r in reqs) if (r?.m_resItem != null) s_materials.Add(r.m_resItem.gameObject.name); }
                foreach (Recipe rc in ObjectDB.instance.m_recipes) if (rc != null) Add(rc.m_resources);
                foreach (GameObject go in ObjectDB.instance.m_items)
                {
                    var pt = go != null ? go.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
                    if (pt == null) continue;
                    foreach (GameObject p in pt.m_pieces) { Piece pc = p != null ? p.GetComponent<Piece>() : null; if (pc != null) Add(pc.m_resources); }
                }
                Plugin.Log.LogInfo($"미리 채집 재료 후보 {s_materials.Count}개");
            }
            return s_materials.Contains(name);
        }

        static bool Listed(IEnumerable<KeyValuePair<string, int>> items, string item)
        {
            foreach (var kv in items) if (kv.Key == item) return true;
            return false;
        }

        /// <summary>쉬는 일꾼 맨 끝: 채울 것 첫 하나를 같은 출발 판단으로(위험하면 10분 쉼, 호위병 안 데려감). 상자 칸이 빠듯하면 안 함. 시작했으면 true.</summary>
        internal static bool PreGather(WorkerNpc npc)
        {
            Vector3 home = npc.Home;
            List<Ingredient> wants = s_pre.Wants(item => Stock(item, home),
                item => Listed(Brain.Refill.Items, item) || Listed(Brain.Debt.Items, item) || RefillSkipped(item) || NightSkipped(item) || RefillBusy(item) || FarmOnly(item), Time.time);
            if (wants.Count == 0) return false;
            int empty = 0;
            foreach (var c in Chests.NearBase(home, ChestRole.Mats)) empty += c.GetInventory().GetEmptySlots();
            foreach (Ingredient w in wants)
            {
                GameObject go = ObjectDB.instance.GetItemPrefab(w.Item);
                ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                if (!PreGatherBook.Room(empty, w.Amount, drop.m_itemData.m_shared.m_maxStackSize)) { Plugin.Log.LogInfo($"{npc.NpcName} 미리 채집 {w.Item} {w.Amount}: 상자 빈칸 {empty} 빠듯 → 안 함"); continue; }
                HashSet<ToolKind> ways = GatherJob.WaysFor(npc, w.Item);
                if (ways.Count == 0 || ways.Contains(ToolKind.Hunt) && HuntWeak(npc, w.Item)) { s_refillSkipUntil[w.Item] = Time.time + 600f; continue; }
                var cmd = new ChatCommand { Kind = CommandKind.Item, Item = w.Item, Amount = w.Amount };
                DepartureInput input = Input(npc, cmd, ways, true);
                input.Stock = 0;
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{npc.NpcName} 미리 채집 {w.Item} {w.Amount}: {r.Action} {r.Question} ({r.Reason})");
                if (r.Action != DepartAction.Go) { s_refillSkipUntil[w.Item] = Time.time + 600f; continue; }
                var job = new GatherJob(npc, w.Item, w.Amount, ways) { FromRefill = true };
                foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
                npc.Ai.StartJob(job);
                npc.Say("refillGo", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(w.Item), ["n"] = w.Amount.ToString() });
                return true;
            }
            return false;
        }
    }
}
