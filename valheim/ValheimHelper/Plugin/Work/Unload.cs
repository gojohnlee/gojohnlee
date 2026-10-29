using System.Collections.Generic;

namespace ValheimHelper
{
    /// <summary>
    /// 짐 정리: 들고 다닐 것(Prep.Kept) 말고는 거점 상자에 넣는다(장비는 장비 상자, 나머지는 자원 상자). 상자마다 걸어가서 한 번에.
    /// 넣을 상자가 없거나 자리가 없는 물건은 건너뛴다. 묘비 회수 뒤(GraveJob), 채집 중 짐을 넣을 때(GatherJob: 다른 짐으로 가방이 참).
    /// </summary>
    public sealed class Unload
    {
        const float Limit = 90f, BusyLimit = 60f;

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        /// <summary>이 물건은 넣지 않는다(채집 중인 물건: 그건 GatherJob이 세면서 넣는다).</summary>
        readonly string m_except;
        readonly HashSet<ItemDrop.ItemData> m_skip = new HashSet<ItemDrop.ItemData>();
        /// <summary>이 프리팹들도 넣지 않는다(GatherJob: 일터로 들고 가는 포탈 한 몫 — 불러온 뒤 첫 짐 정리에서 상자로 가지 않게).</summary>
        public HashSet<string> Hold;
        /// <summary>걷지 않고 바로 넣는다(밤사이 정산 — 보는 사람이 없을 때만).</summary>
        public bool Instant;
        /// <summary>넣을 상자가 꽉 차(또는 없어) 못 넣고 건너뛴 것.</summary>
        public IEnumerable<ItemDrop.ItemData> Skipped => m_skip;
        Container m_chest;
        float m_time;

        public Unload(WorkerNpc npc, string except = null)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_except = except;
        }

        /// <summary>넣을 게 있는지(상자가 있는지는 안 본다).</summary>
        public static bool Needed(WorkerNpc npc, string except = null) => new Unload(npc, except).NextToStore() != null;

        /// <returns>true = 넣는 중(걷거나 기다림), false = 끝.</returns>
        public bool Update(float dt)
        {
            m_time += dt;
            if (m_time > Limit) { Plugin.Log.LogInfo($"DIAG Unload 시간 초과 {m_time:0.0}"); return false; }
            if (m_chest == null)
            {
                ItemDrop.ItemData next = NextToStore();
                if (next == null) { Plugin.Log.LogInfo($"DIAG Unload 다 넣음/없음 ({m_time:0.0}초) 짐 [{BagDump()}] 지킴 {Prep.Kept(m_npc).Count} skip {m_skip.Count}"); return false; } // 다 넣었다
                m_chest = ChestFor(next);
                if (m_chest == null) { Plugin.Log.LogInfo($"DIAG Unload 넣을 상자 없음 {next.m_dropPrefab.name} (역할 {Chests.RoleFor(next)}, 상자 {Chests.NearBase(m_npc.Home, Chests.RoleFor(next)).Count})"); m_skip.Add(next); return true; }
            }
            var at = m_chest.transform.position;
            float d = Utils.DistanceXZ(at, m_npc.transform.position);
            if (!Instant && d > 2.5f) { m_ai.MoveToPoint(dt, at, 1.8f, d > 10f); return true; } // 거점 125m: 멀면 뜀(Prep과 같음)
            Container chest = m_chest;
            HashSet<ItemDrop.ItemData> keep = Prep.Kept(m_npc);
            Inventory mine = m_npc.GetInventory();
            int bagBefore = Stacks(mine);
            bool done = Chests.TryEdit(chest, inv =>
            {
                foreach (var it in new List<ItemDrop.ItemData>(mine.GetAllItems()))
                {
                    if (!Storable(it, keep) || ChestFor(it) != chest || !inv.CanAddItem(it)) continue;
                    if (keep.Contains(it)) // 들고 다닐 미드 더미: 몫을 넘는 만큼만
                    {
                        int extra = Prep.ExtraMeads(m_npc, it);
                        ItemDrop.ItemData part = it.Clone();
                        part.m_stack = extra;
                        // 바닐라 AddItem은 기존 더미에 일부만 채우고 false를 낼 수 있음(남은 수 = part.m_stack) → 들어간 만큼만 뺀다
                        int moved = extra <= 0 ? 0 : inv.AddItem(part) ? extra : extra - part.m_stack;
                        if (moved > 0) mine.RemoveItem(it, moved);
                        continue;
                    }
                    if (m_npc.IsWearing(it)) m_npc.UnequipItem(it, false);
                    inv.MoveItemToThis(mine, it);
                }
            });
            if (!done && m_time < BusyLimit) return true; // 누가 열고 있다
            int put = bagBefore - Stacks(mine);
            // 어느 상자에 넣었나(10-29 vh_bagtest: 깃발 125m 가장자리 옛 상자에 넣은 나무를 시험이 못 세 "사라짐"으로 보임)
            if (put > 0) Plugin.Log.LogInfo($"DIAG Unload 넣음 {put}개 → 상자 {chest.GetComponent<ZNetView>()?.GetZDO()?.m_uid} 집에서 {Utils.DistanceXZ(chest.transform.position, m_npc.Home):0.0}m");
            // 이 상자에 못 넣은 건 건너뛴다(자리가 없음)
            foreach (var it in mine.GetAllItems()) if (Storable(it, keep) && ChestFor(it) == chest) { m_skip.Add(it); Plugin.Log.LogInfo($"DIAG Unload 못 넣고 건너뜀 {it.m_dropPrefab.name}x{it.m_stack} (편집 {(done ? "됨" : "못 함")}, 상자 빈칸 {chest.GetInventory().GetEmptySlots()})"); }
            m_chest = null;
            return true;
        }

        static int Stacks(Inventory inv)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems()) n += it.m_stack;
            return n;
        }

        string BagDump()
        {
            var parts = new List<string>();
            foreach (var it in m_npc.GetInventory().GetAllItems())
                parts.Add((it.m_dropPrefab != null ? it.m_dropPrefab.name : "null-prefab:" + it.m_shared.m_name) + "x" + it.m_stack);
            return string.Join(", ", parts);
        }

        Container ChestFor(ItemDrop.ItemData it) => Chests.ForItem(it.m_dropPrefab.name, m_npc.Home, Chests.RoleFor(it));

        // 거점에서 고칠 수 있는 닳은 장비는 지닌다(다음 출발 준비가 고침, 상자에 넣으면 부러진 채 남던 것)
        bool Storable(ItemDrop.ItemData it, HashSet<ItemDrop.ItemData> keep) =>
            it.m_dropPrefab != null && (!keep.Contains(it) || Prep.ExtraMeads(m_npc, it) > 0) && !m_skip.Contains(it) && it.m_dropPrefab.name != m_except
            && (Hold == null || !Hold.Contains(it.m_dropPrefab.name))
            && Repair.StationFor(it, m_npc.Home) == null;

        ItemDrop.ItemData NextToStore()
        {
            HashSet<ItemDrop.ItemData> keep = Prep.Kept(m_npc);
            foreach (var it in m_npc.GetInventory().GetAllItems())
                if (Storable(it, keep)) return it;
            return null;
        }
    }
}
