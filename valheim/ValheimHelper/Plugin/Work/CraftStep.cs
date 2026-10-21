using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public enum CraftResult { Working, Done, Failed }

    /// <summary>
    /// 계획의 Craft 단계 하나(⑥ 실행기 조각): 제작대 앞까지 걸어가서, 거점 상자 재료를 빼고 결과를 상자에 넣는다(제작은 순간).
    /// 재료가 모자라거나 제작대·레벨이 없거나 시간(Limit)이 넘으면 Failed. 누가 상자를 열고 있으면 잠시 기다린다.
    /// 재료를 빼는 건 한 프레임 안에, 열린 상자가 없을 때만(일부만 빠지는 일 방지).
    /// </summary>
    public sealed class CraftStep
    {
        const float Limit = 120f, BusyLimit = 30f;

        readonly WorkerNpc m_npc;
        readonly Core.Work.Recipe m_recipe;
        readonly int m_crafts;
        float m_time, m_busy;

        public CraftStep(WorkerNpc npc, PlanStep step)
        {
            m_npc = npc;
            m_crafts = Mathf.Max(1, step.Crafts);
            foreach (var r in Recipes.Book().RecipesFor(step.Item))
                if (m_recipe == null || r.Station == step.Station) m_recipe = r;
        }

        public CraftResult Update(float dt)
        {
            if (m_recipe == null) return CraftResult.Failed;
            m_time += dt;
            if (m_time > Limit) return CraftResult.Failed;
            Vector3 home = m_npc.Home;
            if (m_recipe.Station != null)
            {
                CraftingStation cs = CraftingStation.FindClosestStationInRange(m_recipe.Station, home, Recipes.BaseRange);
                if (cs == null || cs.GetLevel() < m_recipe.StationLevel) return CraftResult.Failed;
                Vector3 at = cs.transform.position;
                if (Utils.DistanceXZ(at, m_npc.transform.position) > cs.m_rangeBuild)
                { m_npc.Ai.MoveToPoint(dt, at, Mathf.Max(1.5f, cs.m_rangeBuild * 0.6f), false); return CraftResult.Working; }
            }
            var need = Needs();
            foreach (var kv in need)
                if (Have(home, kv.Key) < kv.Value) return CraftResult.Failed;
            var chests = Chests.NearBase(home);
            foreach (var c in chests)
                if (Chests.InUse(c)) { m_busy += dt; return m_busy > BusyLimit ? CraftResult.Failed : CraftResult.Working; }
            foreach (var kv in need) Take(chests, kv.Key, kv.Value);
            return Put(home, m_recipe.Output, m_recipe.OutputAmount * m_crafts) ? CraftResult.Done : CraftResult.Failed;
        }

        /// <summary>재료 → 필요한 수. OnlyOne이면 있는 재료 하나만(없으면 첫 재료).</summary>
        Dictionary<string, int> Needs()
        {
            var need = new Dictionary<string, int>();
            Ingredient pick = m_recipe.Inputs[0];
            if (m_recipe.OnlyOne)
                foreach (var i in m_recipe.Inputs) if (Have(m_npc.Home, i.Item) >= i.Amount * m_crafts) { pick = i; break; }
            if (m_recipe.OnlyOne) need[pick.Item] = pick.Amount * m_crafts;
            else foreach (var i in m_recipe.Inputs) need[i.Item] = i.Amount * m_crafts;
            return need;
        }

        internal static string SharedName(string prefab)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            return go != null ? go.GetComponent<ItemDrop>().m_itemData.m_shared.m_name : null;
        }

        internal static int Have(Vector3 home, string prefab)
        {
            string name = SharedName(prefab);
            return name == null ? 0 : Chests.CountAll(home, inv => inv.CountItems(name));
        }

        internal static void Take(List<Container> chests, string prefab, int amount)
        {
            string name = SharedName(prefab);
            foreach (var c in chests)
            {
                if (amount <= 0) return;
                int left = amount;
                Chests.TryEdit(c, inv =>
                {
                    int n = Mathf.Min(left, inv.CountItems(name));
                    if (n > 0) { inv.RemoveItem(name, n); left -= n; }
                });
                amount = left;
            }
        }

        /// <summary>결과를 상자에 넣는다. 자리가 없으면 false(재료는 이미 빠졌으니 호출 쪽이 알린다).</summary>
        static bool Put(Vector3 home, string prefab, int amount)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return false;
            ItemDrop.ItemData proto = go.GetComponent<ItemDrop>().m_itemData;
            int max = Mathf.Max(1, proto.m_shared.m_maxStackSize);
            while (amount > 0)
            {
                Container chest = Chests.ForItem(prefab, home);
                if (chest == null) return false;
                int stack = Mathf.Min(amount, max);
                bool added = false;
                Chests.TryEdit(chest, inv =>
                {
                    ItemDrop.ItemData d = proto.Clone();
                    d.m_stack = stack;
                    d.m_dropPrefab = go;
                    added = inv.AddItem(d);
                });
                if (!added) return false;
                amount -= stack;
            }
            return true;
        }
    }
}
