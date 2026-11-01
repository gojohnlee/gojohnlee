using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 싸워서 잡은 몬스터의 떨굼 줍기(사용자 11-01): 무슨 일 중이든 싸움이 끝나면 그 몬스터가 떨굴 수 있는 물건(CharacterDrop)만
    /// 죽은 자리 LootRange 안에서 주워 가방에 — 상자에는 일 끝난 뒤 Unload가 넣는다. 가방에 안 들어가면 둔다.
    /// 떨굼은 쓰러진 몸이 사라질 때 생기니(Ragdoll.SpawnLoot) 보일 때까지는 일을 계속하고, 보이면 그때만 걸어가 줍는다.
    /// </summary>
    public sealed partial class Guard
    {
        const float LootRange = 8f, LootLimit = 25f, LootLeave = 40f;

        sealed class Loot { public Vector3 At; public HashSet<string> Names; public float Age; }

        readonly List<Loot> m_loots = new List<Loot>();
        HashSet<string> m_targetDrops;
        Vector3 m_targetAt;

        /// <summary>시험용: 아직 줍기를 기다리는 잡은 몬스터 수.</summary>
        public int LootsWaiting => m_loots.Count;

        void RememberDrops(Character t)
        {
            m_targetDrops = null;
            var cd = t.GetComponent<CharacterDrop>();
            if (cd == null || cd.m_drops == null) return;
            foreach (var d in cd.m_drops)
                if (d.m_prefab != null) (m_targetDrops ??= new HashSet<string>()).Add(d.m_prefab.name);
            m_targetAt = t.transform.position;
        }

        /// <summary>싸움 끝 — 대상이 죽었으면(이미 지워진 것 포함, 무시·놓침은 뺌) 그 자리를 줍기 목록에.</summary>
        void NoteKill()
        {
            bool gone = m_target == null && (object)m_target != null; // 죽어 지워진 Unity 물체
            if (m_target != null && m_target.IsDead() == false) { m_targetDrops = null; return; }
            if (m_target != null) m_targetAt = m_target.transform.position;
            if ((gone || m_target != null) && m_targetDrops != null)
            {
                m_loots.Add(new Loot { At = m_targetAt, Names = m_targetDrops });
                if (m_loots.Count > 6) m_loots.RemoveAt(0);
            }
            m_targetDrops = null;
        }

        /// <summary>싸움 없을 때: 보이는 떨굼이 있으면 걸어가 줍는다(true = 이번 프레임 몸을 씀).</summary>
        bool UpdateLoot(float dt)
        {
            if (m_loots.Count == 0 || m_npc.IsDead()) return false;
            Vector3 me = m_npc.transform.position;
            Inventory inv = m_npc.GetInventory();
            for (int i = m_loots.Count - 1; i >= 0; i--)
            {
                Loot l = m_loots[i];
                l.Age += dt;
                if (l.Age > LootLimit || Utils.DistanceXZ(l.At, me) > LootLeave) { m_loots.RemoveAt(i); continue; }
                ItemDrop best = null;
                float bestD = float.MaxValue;
                foreach (var zdo in Senses.Nearby(l.At, LootRange))
                {
                    ZNetView view = ZNetScene.instance.FindInstance(zdo);
                    ItemDrop drop = view != null ? view.GetComponent<ItemDrop>() : null;
                    if (drop == null || !l.Names.Contains(Utils.GetPrefabName(drop.gameObject))) continue;
                    if (!inv.CanAddItem(drop.m_itemData)) continue; // 가방 꽉 차면 둠
                    float d = Vector3.Distance(drop.transform.position, me);
                    if (d < bestD) { bestD = d; best = drop; }
                }
                if (best == null) continue; // 아직 몸이 안 사라졌거나 다 주움 — 일은 계속
                if (bestD <= 1.8f)
                {
                    if (m_npc.PickDrop(best)) Plugin.Log.LogInfo($"{m_npc.NpcName} 잡은 몬스터 떨굼 주움 {best.m_itemData.m_shared.m_name} x{best.m_itemData.m_stack}");
                }
                else m_ai.MoveToPoint(dt, best.transform.position, 1f, true);
                return true;
            }
            return false;
        }
    }
}
