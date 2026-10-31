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
        readonly DoorWay m_way = new DoorWay();
        readonly Core.Work.Recipe m_recipe;
        readonly int m_crafts;
        float m_time, m_busy;
        public static bool TestCheckRoof;
        public static int RoofFails; // 시험 확인용

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
                // 바닐라처럼 지붕(70%)·불이 필요한 설비는 그래야 씀(③-1 기본안). 시험 대부분은 들판 작업대라 TestCheckRoof일 때만
                if ((!TestRunner.Running || TestCheckRoof) && !Repair.Usable(cs))
                { Plugin.Log.LogInfo($"{m_npc.NpcName} 제작 실패: {Localization.instance.Localize(cs.m_name)} 지붕·불 없음"); RoofFails++; return CraftResult.Failed; }
                Vector3 at = cs.transform.position;
                if (Utils.DistanceXZ(at, m_npc.transform.position) > cs.m_rangeBuild)
                { m_npc.Ai.MoveToPoint(dt, at, Mathf.Max(1.5f, cs.m_rangeBuild * 0.6f), Utils.DistanceXZ(at, m_npc.transform.position) > 10f); return CraftResult.Working; }
                if (m_way.Steer(dt, m_npc, cs)) return CraftResult.Working; // 집 안 작업대: 벽이 가리면 문으로(회차 311)
                m_npc.Ai.StopMoving(); // 걷기 방향이 남으면 설비 한가운데로 계속 걷는다(가마솥 밑 불 등 — 회차 128 MachineStep과 같은 까닭)
            }
            var need = Needs();
            foreach (var kv in need)
                if (Have(home, kv.Key) < kv.Value) return CraftResult.Failed;
            var chests = Chests.NearBase(home, ChestRole.Mats);
            foreach (var c in chests)
                if (Chests.InUse(c)) { m_busy += dt; return m_busy > BusyLimit ? CraftResult.Failed : CraftResult.Working; }
            foreach (var kv in need) // 사람 제작 막대가 예약한 몫을 빼면 모자라면 막대가 끝날 때까지 기다림(① 라)
            {
                int r = BaseStock.Reserved(SharedName(kv.Key));
                if (r > 0 && Have(home, kv.Key) - r < kv.Value) { m_busy += dt; return m_busy > BusyLimit ? CraftResult.Failed : CraftResult.Working; }
            }
            if (!TakeAll(home, chests, need)) return CraftResult.Failed;
            int left = Put(home, m_recipe.Output, m_recipe.OutputAmount * m_crafts);
            if (left > 0) LeaveAtChest(m_npc, m_recipe.Output, left);
            return CraftResult.Done;
        }

        /// <summary>
        /// 상자에 못 넣은 만든 물건(재료는 이미 빠짐): 짐에 넣지 않고 그 역할 상자 중 가장 가까운 것 바로 앞 땅에 내려놓고(사라지지 않게),
        /// "상자 하나만 더 만들어 주세요"라고 한다(같은 상자는 5분에 한 번) — 사용자 결정 2026-10-22. 상자가 하나도 없으면 발밑.
        /// </summary>
        public static void LeaveAtChest(WorkerNpc npc, string prefab, int amount, ChestRole role = ChestRole.Mats)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null || amount <= 0) return;
            Container chest = null;
            float best = float.MaxValue;
            foreach (var c in Chests.NearBase(npc.Home, role))
            {
                float d = Vector3.Distance(c.transform.position, npc.transform.position);
                if (d < best) { best = d; chest = c; }
            }
            DropAt(chest, npc.transform.position, prefab, amount);
            Plugin.Log.LogInfo($"{npc.NpcName} 상자가 꽉 차 만든 것 {prefab} {amount}개를 {(chest != null ? "상자 앞" : "발밑")}에 둠");
            if (chest == null) return;
            ZDOID id = chest.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None;
            if (s_whined.TryGetValue(id, out float t) && Time.time - t < WhineEvery) return;
            s_whined[id] = Time.time;
            npc.Say("craftChestFull");
        }

        const float WhineEvery = 300f;
        static readonly Dictionary<ZDOID, float> s_whined = new Dictionary<ZDOID, float>();

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
            return name == null ? 0 : Chests.CountAll(home, inv => inv.CountItems(name), ChestRole.Mats);
        }

        /// <summary>상자들에서 뺀다. 실제로 뺀 개수를 돌려준다(상자가 그새 열렸거나 개수 확인 뒤 줄었거나 사람 제작이 예약했으면 amount보다 적음).</summary>
        internal static int Take(List<Container> chests, string prefab, int amount)
        {
            string name = SharedName(prefab);
            if (TestShortBy > 0) { amount = Mathf.Max(0, amount - TestShortBy); TestShortBy = 0; }
            int taken = 0;
            foreach (var c in chests)
            {
                if (taken >= amount) break;
                int left = BaseStock.Allow(c, name, amount - taken); // 사람 제작 막대가 예약한 몫은 안 가져감
                if (left <= 0) continue;
                Chests.TryEdit(c, inv =>
                {
                    int n = Mathf.Min(left, inv.CountItems(name));
                    if (n > 0) { inv.RemoveItem(name, n); taken += n; BaseStock.Changed(); }
                });
            }
            return taken;
        }

        /// <summary>시험용: 0보다 크면 다음 Take 한 번이 그만큼 덜 뺀다(상자 변동 흉내). 쓰면 0으로 돌아감.</summary>
        internal static int TestShortBy;

        /// <summary>
        /// 재료를 다 빼거나 하나도 안 뺀다: 하나라도 덜 빠지면 이미 뺀 것을 상자에 되돌리고 false(물건 복제·소실 막음, 요청 10-23 코드 정리).
        /// 되돌릴 상자 자리가 없으면 첫 상자 앞 땅에.
        /// </summary>
        internal static bool TakeAll(Vector3 home, List<Container> chests, IEnumerable<KeyValuePair<string, int>> need)
        {
            var taken = new List<KeyValuePair<string, int>>();
            string shortOf = null;
            foreach (var kv in need)
            {
                if (kv.Value <= 0) continue;
                int n = Take(chests, kv.Key, kv.Value);
                if (n > 0) taken.Add(new KeyValuePair<string, int>(kv.Key, n));
                if (n < kv.Value) { shortOf = $"{kv.Key} {n}/{kv.Value}"; break; }
            }
            if (shortOf == null) return true;
            foreach (var kv in taken)
            {
                int left = Put(home, kv.Key, kv.Value);
                if (left > 0) DropAt(chests.Count > 0 ? chests[0] : null, home, kv.Key, left);
            }
            Plugin.Log.LogWarning($"재료가 덜 빠짐({shortOf}) → 뺀 것 상자에 되돌리고 그만둠");
            return false;
        }

        /// <summary>상자 바로 앞 땅에(상자가 없으면 or 자리 위에) 내려놓는다 — 사라지지 않게.</summary>
        internal static void DropAt(Container chest, Vector3 or, string prefab, int amount)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return;
            Vector3 at = chest != null ? chest.transform.position + chest.transform.forward * 0.9f + Vector3.up * 0.3f : or + Vector3.up;
            ItemDrop.ItemData d0 = go.GetComponent<ItemDrop>().m_itemData.Clone();
            d0.m_dropPrefab = go;
            int max = Mathf.Max(1, d0.m_shared.m_maxStackSize);
            for (int left = amount; left > 0; left -= max)
                ItemDrop.DropItem(d0, Mathf.Min(left, max), at, Quaternion.identity);
        }

        /// <summary>결과를 상자에 넣는다. 못 넣은 개수를 돌려준다(0 = 다 넣음).</summary>
        internal static int Put(Vector3 home, string prefab, int amount)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return amount;
            ItemDrop.ItemData proto = go.GetComponent<ItemDrop>().m_itemData;
            int max = Mathf.Max(1, proto.m_shared.m_maxStackSize);
            while (amount > 0)
            {
                Container chest = Chests.ForItem(prefab, home, ChestRole.Mats);
                if (chest == null) return amount;
                int stack = Mathf.Min(amount, max);
                int added = 0;
                Chests.TryEdit(chest, inv =>
                {
                    ItemDrop.ItemData d = proto.Clone();
                    d.m_stack = stack;
                    d.m_dropPrefab = go;
                    added = inv.AddItem(d) ? stack : stack - d.m_stack; // 실패해도 기존 더미에 일부 들어갔을 수 있음
                });
                amount -= added;
                if (added < stack) return amount;
            }
            return 0;
        }
    }
}
