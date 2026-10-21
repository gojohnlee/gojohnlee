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
            if (m_time > Limit) return false;
            if (m_chest == null)
            {
                ItemDrop.ItemData next = NextToStore();
                if (next == null) return false; // 다 넣었다
                m_chest = ChestFor(next);
                if (m_chest == null) { m_skip.Add(next); return true; }
            }
            var at = m_chest.transform.position;
            if (Utils.DistanceXZ(at, m_npc.transform.position) > 2.5f) { m_ai.MoveToPoint(dt, at, 1.8f, false); return true; }
            Container chest = m_chest;
            HashSet<ItemDrop.ItemData> keep = Prep.Kept(m_npc);
            Inventory mine = m_npc.GetInventory();
            bool done = Chests.TryEdit(chest, inv =>
            {
                foreach (var it in new List<ItemDrop.ItemData>(mine.GetAllItems()))
                {
                    if (!Storable(it, keep) || ChestFor(it) != chest || !inv.CanAddItem(it)) continue;
                    if (m_npc.IsWearing(it)) m_npc.UnequipItem(it, false);
                    inv.MoveItemToThis(mine, it);
                }
            });
            if (!done && m_time < BusyLimit) return true; // 누가 열고 있다
            // 이 상자에 못 넣은 건 건너뛴다(자리가 없음)
            foreach (var it in mine.GetAllItems()) if (Storable(it, keep) && ChestFor(it) == chest) m_skip.Add(it);
            m_chest = null;
            return true;
        }

        Container ChestFor(ItemDrop.ItemData it) => Chests.ForItem(it.m_dropPrefab.name, m_npc.Home, Chests.RoleFor(it));

        bool Storable(ItemDrop.ItemData it, HashSet<ItemDrop.ItemData> keep) =>
            it.m_dropPrefab != null && !keep.Contains(it) && !m_skip.Contains(it) && it.m_dropPrefab.name != m_except;

        ItemDrop.ItemData NextToStore()
        {
            HashSet<ItemDrop.ItemData> keep = Prep.Kept(m_npc);
            foreach (var it in m_npc.GetInventory().GetAllItems())
                if (Storable(it, keep)) return it;
            return null;
        }
    }
}
