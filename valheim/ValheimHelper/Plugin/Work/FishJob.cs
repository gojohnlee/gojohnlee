using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 쉴 때 낚시(사용자 11-09 요청 나): 상자 음식이 모자란데 쉴 때 먹거리(채집·사냥해 굽기)가 하나도 못 떠났으면(사냥이 위험·먼 경우 등)
    /// 집 FishFar m 안 물고기 둘레 물가로 가서 낚아 와 상자에 넣는다(굽기는 다음 쉴 때 먹거리가 맡음).
    /// 바닐라 찌(FishingFloat)는 주인을 플레이어 목록에서만 찾아(GetOwner) NPC가 못 쓴다 → 흉내: 물가에 서서 CastSeconds마다
    /// FishReach m 안의, 가진 미끼를 무는 물고기 하나를 바닐라 확률(m_baseHookChance × 미끼 m_chance)로 낚고, 낚으면 미끼 1을 쓰고
    /// 물고기 ItemDrop을 줍는다(바닐라 Catch도 ItemDrop.Pickup — 일꾼은 PickDrop). 못 낚은 던지기는 미끼를 안 씀(바닐라 ReturnBait). 게임 사실: codemap.
    /// </summary>
    public sealed class FishJob : IWorkerJob
    {
        const float TotalLimit = 5f * 60f, ShoreLimit = 40f, HomeLimit = 180f, CastSeconds = 6f, NoFishLimit = 30f, BusyLimit = 10f;
        /// <summary>집에서 이 거리 안 물고기만(불러온 것만 보임).</summary>
        internal const float FishFar = 100f;
        /// <summary>물가에서 이만큼 안 물고기를 낚는다(바닐라 찌 m_range 10 + 던지는 거리 몫).</summary>
        internal const float FishReach = 12f;
        /// <summary>한 번에 낚아 올 수.</summary>
        internal const int Target = 3;

        enum Step { Fetch, Shore, Cast, Home, Store }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        Step m_step = Step.Fetch;
        float m_time, m_stepTime, m_castTime, m_noFish;
        Vector3 m_stand, m_water;
        Fish m_hooked;
        float m_hookTime;
        Unload m_unload;
        bool m_abort, m_told;
        readonly HashSet<Fish> m_tried = new HashSet<Fish>();

        /// <summary>낚은 물고기 수.</summary>
        public int Got { get; private set; }
        public string StepName => "Fish" + m_step;
        public string Label => m_npc.Line("job.fish");
        public string StateLine() => m_npc.Line("state.fish", new Dictionary<string, string> { ["n"] = Got.ToString() });
        public void AvoidHere(Vector3 danger) => m_abort = true;

        public FishJob(WorkerNpc npc)
        {
            m_npc = npc;
            m_ai = npc.Ai;
        }

        internal static bool IsRod(ItemDrop.ItemData it) => it.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.FishingRod;

        internal static IEnumerable<Fish> Loaded()
        {
            foreach (var u in Fish.Instances)
                if (u is Fish f && f != null && f.TryGetComponent(out ItemDrop _) && f.TryGetComponent(out ZNetView v) && v.IsValid() && !f.IsOutOfWater()) yield return f;
        }

        /// <summary>이 미끼(프리팹 이름)를 무는 확률(바닐라 Fish.TestBate), 안 물면 0.</summary>
        internal static float BaitChance(Fish f, string bait)
        {
            foreach (var b in f.m_baits) if (b.m_bait != null && b.m_bait.name == bait) return b.m_chance;
            return 0f;
        }

        /// <summary>집 FishFar 안 불러온 물고기가 무는 미끼 이름들.</summary>
        internal static HashSet<string> BaitsNear(Vector3 home)
        {
            var set = new HashSet<string>();
            foreach (var f in Loaded())
                if (Utils.DistanceXZ(f.transform.position, home) < FishFar)
                    foreach (var b in f.m_baits) if (b.m_bait != null) set.Add(b.m_bait.name);
            return set;
        }

        static ItemDrop.ItemData FindRod(Inventory inv)
        {
            foreach (var it in inv.GetAllItems()) if (IsRod(it)) return it;
            return null;
        }

        static int BaitCount(Inventory inv, HashSet<string> baits)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems()) if (it.m_dropPrefab != null && baits.Contains(it.m_dropPrefab.name)) n += it.m_stack;
            return n;
        }

        /// <summary>거점 상자(장비)에 낚싯대가 있거나 들었고, 상자(자원)·짐에 둘레 물고기가 무는 미끼가 있는지.</summary>
        internal static bool CanFish(WorkerNpc npc, HashSet<string> baits)
        {
            if (baits.Count == 0) return false;
            Inventory mine = npc.GetInventory();
            if (FindRod(mine) == null && Chests.First(npc.Home, inv => FindRod(inv) != null, ChestRole.Gear) == null) return false;
            if (BaitCount(mine, baits) > 0) return true;
            foreach (var c in Chests.NearBase(npc.Home, ChestRole.Mats)) if (BaitCount(c.GetInventory(), baits) > 0) return true;
            return false;
        }

        void Go(Step s)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 낚시: {m_step} → {s} (물고기 {Got})");
            m_step = s;
            m_stepTime = 0f;
            m_castTime = 0f;
            if (s == Step.Store) m_unload = new Unload(m_npc);
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
                case Step.Shore: return UpdateShore(dt);
                case Step.Cast: return UpdateCast(dt);
                case Step.Home: return UpdateHome(dt);
                case Step.Store:
                    if (m_unload.Update(dt)) return true;
                    Tell();
                    return false;
            }
            return false;
        }

        // ── 챙기기: 낚싯대 1(장비 상자)·미끼 전부(자원 상자, 사람 제작 예약 몫 빼고). 상자는 한 프레임에 고친다. ──
        bool UpdateFetch()
        {
            Inventory mine = m_npc.GetInventory();
            HashSet<string> baits = BaitsNear(m_npc.Home);
            if (FindRod(mine) == null)
            {
                Container c = Chests.First(m_npc.Home, inv => FindRod(inv) != null, ChestRole.Gear);
                if (c == null) { Plugin.Log.LogInfo($"{m_npc.NpcName} 낚시: 낚싯대 없음"); return false; }
                if (Chests.InUse(c)) return m_stepTime < BusyLimit;
                Chests.TryEdit(c, inv =>
                {
                    var rod = FindRod(inv);
                    if (rod != null && mine.CanAddItem(rod) && BaseStock.Allow(c, rod.m_shared.m_name, 1) > 0) { mine.MoveItemToThis(inv, rod); BaseStock.Changed(); }
                });
                if (FindRod(mine) == null) { m_npc.Say("chestFull"); return false; }
            }
            foreach (var c in Chests.NearBase(m_npc.Home, ChestRole.Mats))
            {
                if (Chests.InUse(c)) continue;
                Chests.TryEdit(c, inv =>
                {
                    foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                    {
                        if (it.m_dropPrefab == null || !baits.Contains(it.m_dropPrefab.name)) continue;
                        int n = BaseStock.Allow(c, it.m_shared.m_name, it.m_stack);
                        if (n <= 0) continue;
                        ItemDrop.ItemData copy = it.Clone();
                        copy.m_stack = n;
                        if (!mine.AddItem(copy)) return;
                        inv.RemoveItem(it, n);
                        BaseStock.Changed();
                    }
                });
            }
            if (BaitCount(mine, baits) == 0) { Plugin.Log.LogInfo($"{m_npc.NpcName} 낚시: 미끼 없음"); Go(Step.Store); return true; }
            return PickShore() ? Go2(Step.Shore) : Go2(Step.Store);
        }

        bool Go2(Step s) { Go(s); return true; }

        /// <summary>
        /// 가진 미끼를 무는, 집 FishFar 안 물고기 중 가장 가까운 것의 물가: 물고기에서 집 쪽으로 1m씩 오며 물 깊이 0.5m 밑(걸어 설 수 있는) 첫 자리.
        /// 그 자리가 물고기에서 FishReach 넘게 멀면 다음 물고기. 없으면 false.
        /// </summary>
        bool PickShore()
        {
            Inventory mine = m_npc.GetInventory();
            float wl = ZoneSystem.instance.m_waterLevel;
            Vector3 home = m_npc.Home, me = m_npc.transform.position;
            var fish = new List<Fish>();
            foreach (var f in Loaded())
                if (!m_tried.Contains(f) && Utils.DistanceXZ(f.transform.position, home) < FishFar && BestBait(f, mine) != null) fish.Add(f);
            fish.Sort((a, b) => Utils.DistanceXZ(a.transform.position, me).CompareTo(Utils.DistanceXZ(b.transform.position, me)));
            foreach (var f in fish)
            {
                m_tried.Add(f);
                Vector3 p = f.transform.position, dir = home - p;
                dir.y = 0f;
                float len = dir.magnitude;
                if (len < 0.1f) continue;
                dir /= len;
                for (float s = 1f; s <= FishReach && s < len; s += 1f)
                {
                    Vector3 q = p + dir * s;
                    if (!ZoneSystem.instance.GetGroundHeight(q, out float h) || wl - h > 0.5f) continue;
                    m_stand = new Vector3(q.x, Mathf.Max(h, wl), q.z);
                    m_water = p;
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 낚시 자리 {m_stand} (물고기 {f.GetHoverName()} {s:0}m, 집에서 {Utils.DistanceXZ(m_stand, home):0}m)");
                    return true;
                }
            }
            Plugin.Log.LogInfo($"{m_npc.NpcName} 낚시: 물가 자리 없음(물고기 {fish.Count})");
            return false;
        }

        /// <summary>가진 미끼 중 이 물고기가 가장 잘 무는 것(프리팹 이름).</summary>
        static string BestBait(Fish f, Inventory inv)
        {
            string best = null;
            float bestChance = 0f;
            foreach (var it in inv.GetAllItems())
            {
                if (it.m_dropPrefab == null) continue;
                float c = BaitChance(f, it.m_dropPrefab.name);
                if (c > bestChance) { bestChance = c; best = it.m_dropPrefab.name; }
            }
            return best;
        }

        bool UpdateShore(float dt)
        {
            Vector3 me = m_npc.transform.position;
            if (m_ai.MoveToPoint(dt, m_stand, 1f, true) || Utils.DistanceXZ(m_stand, me) < 1.5f) { Go(Step.Cast); return true; }
            if (m_stepTime > ShoreLimit)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 낚시 자리 못 닿음 {m_stand} (남은 {Utils.DistanceXZ(m_stand, me):0}m)");
                if (PickShore()) Go(Step.Shore); else Go(Step.Home);
            }
            return true;
        }

        bool UpdateCast(float dt)
        {
            m_ai.StopMoving();
            m_ai.Face(m_water);
            if (m_hooked != null) return Land(dt);
            Inventory mine = m_npc.GetInventory();
            if (Got >= Target || FindRod(mine) == null) { Go(Step.Home); return true; }
            m_castTime += dt;
            if (m_castTime < CastSeconds) return true;
            m_castTime = 0f;
            Vector3 me = m_npc.transform.position;
            Fish pick = null;
            string bait = null;
            float best = float.MaxValue;
            foreach (var f in Loaded())
            {
                float d = Utils.DistanceXZ(f.transform.position, me);
                if (d > FishReach || d >= best) continue;
                string b = BestBait(f, mine);
                if (b == null) continue;
                best = d; pick = f; bait = b;
            }
            if (pick == null)
            {
                m_noFish += CastSeconds;
                if (BaitCount(mine, BaitsNear(m_npc.Home)) == 0) { Plugin.Log.LogInfo($"{m_npc.NpcName} 낚시: 미끼 다 씀"); Go(Step.Home); return true; }
                if (m_noFish >= NoFishLimit) { m_noFish = 0f; if (PickShore()) Go(Step.Shore); else Go(Step.Home); }
                return true;
            }
            m_noFish = 0f;
            m_water = pick.transform.position;
            float chance = pick.m_baseHookChance * BaitChance(pick, bait);
            if (Random.value >= chance) return true; // 안 물었다: 미끼는 그대로(바닐라 ReturnBait)
            var baitItem = mine.GetAllItems().Find(it => it.m_dropPrefab != null && it.m_dropPrefab.name == bait);
            if (baitItem != null) mine.RemoveItem(baitItem, 1);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 낚음: {pick.GetHoverName()} ({best:0}m, 미끼 {bait}, 확률 {chance:0.00})");
            m_hooked = pick;
            m_hookTime = 0f;
            return true;
        }

        /// <summary>낚은 물고기 줍기: 소유권을 받을 때까지 몇 프레임 걸릴 수 있다(PickDrop). 3초 넘으면 놓침.</summary>
        bool Land(float dt)
        {
            m_hookTime += dt;
            ItemDrop d = m_hooked != null ? m_hooked.GetComponent<ItemDrop>() : null;
            if (d != null && m_npc.PickDrop(d)) { Got++; m_hooked = null; return true; }
            if (d == null || m_hookTime > 3f)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 물고기 놓침(줍기 실패, 짐 빈칸 {m_npc.GetInventory().GetEmptySlots()})");
                m_hooked = null;
                if (m_npc.GetInventory().GetEmptySlots() == 0) Go(Step.Home);
            }
            return true;
        }

        bool UpdateHome(float dt)
        {
            Vector3 home = m_npc.Home;
            if (m_ai.MoveToPoint(dt, home, 2.5f, true) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) { Go(Step.Store); return true; }
            if (m_stepTime > HomeLimit)
            {
                if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return true; }
                Go(Step.Store);
            }
            return true;
        }

        void Tell()
        {
            if (m_told || Got == 0) return;
            m_told = true;
            m_npc.Say("fishDone", new Dictionary<string, string> { ["n"] = Got.ToString() });
        }
    }
}
