using System.Collections.Generic;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 쉴 때 우리 먹이 주기(사용자 11-09 요청 다-2): 거점 안 "가축" 이름 상자(ChestTags.Livestock) PenRange 둘레의 길들인 가축이 배고프면(Tameable.IsHungry — 600초)
    /// 상자에서 그 종 먹이(MonsterAI.m_consumeItems) 1개씩 꺼내(한 프레임) 가축 곁에 걸어가 떨군다. 가축은 둘레 10m 땅의 먹이를 스스로 먹음(게임 사실: codemap).
    /// 둘레에 먹을 것이 이미 떨어져 있으면 그 가축은 건너뜀. 남은 먹이는 그 상자에 되돌림.
    /// </summary>
    public sealed class LivestockJob : IWorkerJob
    {
        const float TotalLimit = 4f * 60f, SpotLimit = 20f, HomeLimit = 180f, BusyLimit = 10f;
        /// <summary>"가축" 상자에서 이 거리 안 가축이 그 우리 것.</summary>
        internal const float PenRange = 10f;

        /// <summary>그 우리 가축: 상자 PenRange 안이거나 순찰점(풀어 둔 자리)이 PenRange 안 — 멧돼지는 떠돌기 범위 10m라 순찰점 둘레 20m까지 떠돎(회차 378 E071).</summary>
        internal static bool InPen(Character a, Vector3 pen)
        {
            if (Utils.DistanceXZ(a.transform.position, pen) <= PenRange) return true;
            ZDO zdo = a.GetComponent<ZNetView>()?.GetZDO();
            return zdo != null && zdo.GetBool(ZDOVars.s_patrol) && Utils.DistanceXZ(zdo.GetVec3(ZDOVars.s_patrolPoint, a.transform.position), pen) <= PenRange;
        }

        internal sealed class Feed
        {
            public Character Animal;
            public Container Chest;
            public HashSet<string> Foods;
            public string Taken;
            public bool Over;
        }

        enum Step { Fetch, Feed, Home }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        readonly List<Feed> m_feeds;
        Step m_step = Step.Fetch;
        float m_time, m_stepTime;
        bool m_abort, m_told;

        /// <summary>먹인 가축 수.</summary>
        public int Fed { get; private set; }
        public string StepName => "Feed" + m_step;
        public string Label => m_npc.Line("job.feed");
        public string StateLine() => m_npc.Line("state.feed", new Dictionary<string, string> { ["n"] = Fed.ToString() });
        public void AvoidHere(Vector3 danger) => m_abort = true;

        LivestockJob(WorkerNpc npc, List<Feed> feeds)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_feeds = feeds;
        }

        internal static LivestockJob For(WorkerNpc npc) { var f = Plan(npc.Home); return f.Count > 0 ? new LivestockJob(npc, f) : null; }

        internal static HashSet<string> FoodsOf(Character c)
        {
            var set = new HashSet<string>();
            MonsterAI ai = c.GetComponent<MonsterAI>();
            if (ai != null && ai.m_consumeItems != null) foreach (var i in ai.m_consumeItems) if (i != null) set.Add(i.gameObject.name);
            return set;
        }

        internal static bool FoodOnGround(Vector3 at, HashSet<string> foods)
        {
            foreach (Collider col in Physics.OverlapSphere(at, 10f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d != null && foods.Contains(Utils.GetPrefabName(d.gameObject))) return true;
            }
            return false;
        }

        internal static ItemDrop.ItemData FoodIn(Inventory inv, HashSet<string> foods)
        {
            foreach (var it in inv.GetAllItems()) if (it.m_dropPrefab != null && foods.Contains(it.m_dropPrefab.name)) return it;
            return null;
        }

        /// <summary>집 기준 거점 안 "가축" 상자마다 둘레 배고픈 길들인 가축(상자에 그 먹이가 있고 땅에 먹을 것이 없는 것). 이유는 로그로.</summary>
        internal static List<Feed> Plan(Vector3 home)
        {
            var list = new List<Feed>();
            var provider = Chests.TagProvider;
            if (provider == null) return list;
            int pens = 0, tame = 0, hungry = 0;
            foreach (var zdo in Senses.Nearby(home, Chests.BaseRange, Chests.IsBaseChest))
            {
                if ((provider(zdo) & ChestTags.Livestock) == 0) continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                Container c = view != null ? view.GetComponent<Container>() : null;
                if (c == null) continue;
                pens++;
                foreach (var a in Character.GetAllCharacters())
                {
                    if (a == null || a.IsDead() || !a.IsTamed() || a is WorkerNpc || a.IsPlayer() || !InPen(a, c.transform.position)) continue;
                    Tameable t = a.GetComponent<Tameable>();
                    if (t == null || list.Exists(f => f.Animal == a)) continue;
                    tame++;
                    if (!t.IsHungry()) continue;
                    hungry++;
                    HashSet<string> foods = FoodsOf(a);
                    if (foods.Count == 0 || FoodOnGround(a.transform.position, foods) || FoodIn(c.GetInventory(), foods) == null) continue;
                    list.Add(new Feed { Animal = a, Chest = c, Foods = foods });
                }
            }
            if (pens > 0) Plugin.Log.LogInfo($"우리 먹이(집 {home}): 가축 상자 {pens}, 길들인 것 {tame}, 배고픔 {hungry}, 먹일 것 {list.Count}");
            return list;
        }

        void Go(Step s)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 먹이: {m_step} → {s} (먹임 {Fed})");
            m_step = s;
            m_stepTime = 0f;
            m_ai.StopMoving();
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_stepTime += dt;
            if ((m_time > TotalLimit || m_abort) && m_step < Step.Home) Go(Step.Home);
            switch (m_step)
            {
                case Step.Fetch: return UpdateFetch();
                case Step.Feed: return UpdateFeed(dt);
                case Step.Home: return UpdateHome(dt);
            }
            return false;
        }

        // ── 챙기기: 가축마다 그 우리 상자에서 먹이 1개. 상자는 한 프레임에 고친다. ──
        bool UpdateFetch()
        {
            Inventory mine = m_npc.GetInventory();
            foreach (var f in m_feeds)
            {
                if (f.Chest == null) continue;
                if (Chests.InUse(f.Chest)) { if (m_stepTime < BusyLimit) return true; continue; }
                Chests.TryEdit(f.Chest, inv =>
                {
                    ItemDrop.ItemData it = FoodIn(inv, f.Foods);
                    if (it == null) return;
                    ItemDrop.ItemData one = it.Clone();
                    one.m_stack = 1;
                    if (!mine.AddItem(one)) return;
                    inv.RemoveItem(it, 1);
                    f.Taken = it.m_dropPrefab.name;
                });
            }
            m_feeds.RemoveAll(f => f.Taken == null);
            if (m_feeds.Count == 0) { Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 먹이: 못 꺼냄(짐 빈칸 {mine.GetEmptySlots()})"); Go(Step.Home); return true; }
            Go(Step.Feed);
            return true;
        }

        bool UpdateFeed(float dt)
        {
            Vector3 me = m_npc.transform.position;
            Feed f = null;
            foreach (var x in m_feeds)
            {
                if (x.Over) continue;
                if (x.Animal == null || x.Animal.IsDead()) { x.Over = true; continue; }
                if (f == null || Utils.DistanceXZ(x.Animal.transform.position, me) < Utils.DistanceXZ(f.Animal.transform.position, me)) f = x;
            }
            if (f == null) { Go(Step.Home); return true; }
            Vector3 at = f.Animal.transform.position;
            if (!m_ai.MoveToPoint(dt, at, 2f, false) && Utils.DistanceXZ(at, me) > 2.5f)
            {
                if (m_stepTime > SpotLimit)
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 먹이 건너뜀: {f.Animal.m_name} 못 닿음 {at}");
                    f.Over = true;
                    m_stepTime = 0f;
                }
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(at);
            Inventory mine = m_npc.GetInventory();
            ItemDrop.ItemData it = FoodIn(mine, new HashSet<string> { f.Taken });
            if (it != null)
            {
                Vector3 drop = Vector3.Lerp(me, at, 0.6f) + Vector3.up * 0.5f;
                ItemDrop.DropItem(it, 1, drop, Quaternion.identity);
                mine.RemoveItem(it, 1);
                Fed++;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 먹이: {f.Animal.m_name}에게 {f.Taken} 떨굼 {drop}");
            }
            f.Taken = null;
            f.Over = true;
            m_stepTime = 0f;
            return true;
        }

        bool UpdateHome(float dt)
        {
            Vector3 home = m_npc.Home;
            if (m_ai.MoveToPoint(dt, home, 2.5f, true) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) return Done();
            if (m_stepTime > HomeLimit)
            {
                if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return true; }
                return Done();
            }
            return true;
        }

        /// <summary>못 준 먹이는 꺼낸 우리 상자에 되돌림(가득 차면 짐에 남음 — 다음 짐 내리기가 넣음).</summary>
        bool Done()
        {
            Inventory mine = m_npc.GetInventory();
            foreach (var f in m_feeds)
            {
                if (f.Taken == null || f.Chest == null || Chests.InUse(f.Chest)) continue;
                Chests.TryEdit(f.Chest, inv =>
                {
                    ItemDrop.ItemData it = FoodIn(mine, new HashSet<string> { f.Taken });
                    if (it != null && inv.CanAddItem(it.m_dropPrefab, 1)) { inv.AddItem(it.m_dropPrefab, 1); mine.RemoveItem(it, 1); }
                });
            }
            if (!m_told && Fed > 0)
            {
                m_told = true;
                m_npc.Say("feedDone", new Dictionary<string, string> { ["n"] = Fed.ToString() });
            }
            return false;
        }
    }
}
