using System.Collections.Generic;
using ValheimHelper.Core.Storage;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 일꾼 상자 정리(상자 기능 ②, 사용자 결정 2026-10-23): 거점 125m 안 상자에서 같은 물건을 가장 많이 든 상자로 모은다
    /// (그 물건 역할의 이름표 상자가 있으면 그쪽 — Unload와 같은 ChestNames.Select). 개인·보물 상자와 누가 연 상자는 안 건드린다.
    /// 가-1 Instant: 상자끼리 한 프레임 안에 옮긴다(두 상자 모두 TryEdit) — 아침 정산·플레이어 64m 밖일 때. 불러와진 상자만.
    /// 가-2 Tick: 거점에서 쉬는 일꾼이 플레이어 근처면 걸어서(TidyJob), 멀면 Instant.
    /// </summary>
    public static class Tidy
    {
        /// <summary>옮길 것 하나: From 상자의 Name을 To 상자로.</summary>
        public sealed class Move
        {
            public Container From, To;
            public string Name;
        }

        /// <summary>
        /// 옮길 것 목록(물건마다 모을 상자 하나, 거기 그 물건이 하나라도 더 들어갈 때만 — 꽉 찬 상자로는 다시 안 고름 → 걸어서 왔다 갔다 없음).
        /// 옮기는 상자 순서는 가까운 것부터.
        /// </summary>
        public static List<Move> Plan(Vector3 home)
        {
            var chests = new List<Container>();
            var tags = new List<ChestTags>();
            var provider = Chests.TagProvider;
            foreach (var zdo in Senses.Nearby(home, Chests.BaseRange, Chests.IsBaseChest))
            {
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                Container c = view != null ? view.GetComponent<Container>() : null;
                if (c == null || Chests.InUse(c)) continue;
                ChestTags tag = provider != null ? provider(zdo) : ChestTags.None;
                if ((tag & ChestTags.Private) != 0) continue;
                chests.Add(c);
                tags.Add(tag);
            }
            // 가까운 순(같은 수면 가까운 상자로 모음)
            var order = new List<int>();
            for (int i = 0; i < chests.Count; i++) order.Add(i);
            order.Sort((a, b) => Utils.DistanceXZ(chests[a].transform.position, home).CompareTo(Utils.DistanceXZ(chests[b].transform.position, home)));
            var sorted = new List<Container>();
            var tagArr = new ChestTags[order.Count];
            for (int i = 0; i < order.Count; i++) { sorted.Add(chests[order[i]]); tagArr[i] = tags[order[i]]; }

            var samples = new Dictionary<string, ItemDrop.ItemData>();
            foreach (var c in sorted)
                foreach (var it in c.GetInventory().GetAllItems())
                    if (it.m_dropPrefab != null && !samples.ContainsKey(it.m_dropPrefab.name)) samples[it.m_dropPrefab.name] = it;

            var moves = new List<Move>();
            foreach (var kv in samples)
            {
                string name = kv.Key;
                var counts = new int[sorted.Count];
                for (int i = 0; i < counts.Length; i++) counts[i] = Count(sorted[i], name);
                ChestRole role = Chests.RoleFor(kv.Value);
                ChestTags want = role == ChestRole.Gear ? ChestTags.Gear : role == ChestRole.Food ? ChestTags.Food : ChestTags.Mats;
                int t = ChestTidy.Target(counts, ChestNames.Select(tagArr, want));
                if (t < 0 || !sorted[t].GetInventory().CanAddItem(kv.Value, 1)) continue;
                for (int i = 0; i < counts.Length; i++)
                    if (i != t && counts[i] > 0) moves.Add(new Move { From = sorted[i], To = sorted[t], Name = name });
            }
            return moves;
        }

        /// <summary>바로 정리. 옮긴 물건 수.</summary>
        public static int Instant(Vector3 home)
        {
            int moved = 0;
            foreach (var m in Plan(home)) moved += MoveNow(m.From, m.To, m.Name);
            if (moved > 0) BaseStock.Changed();
            return moved;
        }

        /// <summary>from의 그 물건을 to로(자리 있는 만큼). 옮긴 수. 두 상자 합이 바뀌면 오류 로그(있으면 안 됨).</summary>
        static int MoveNow(Container from, Container to, string name)
        {
            int before = Count(to, name), sum = before + Count(from, name);
            Chests.TryEdit(to, tinv => Chests.TryEdit(from, finv =>
            {
                foreach (var it in new List<ItemDrop.ItemData>(finv.GetAllItems()))
                    if (it.m_dropPrefab != null && it.m_dropPrefab.name == name) tinv.MoveItemToThis(finv, it); // 자리 모자라면 들어간 만큼만 빠짐
            }));
            int after = Count(to, name);
            if (after + Count(from, name) != sum) Plugin.Log.LogError($"상자 정리: {name} 합이 바뀜 {sum} → {after + Count(from, name)}");
            return after - before;
        }

        internal static int Count(Container c, string prefab) => Count(c.GetInventory(), prefab);

        internal static int Count(Inventory inv, string prefab)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) n += it.m_stack;
            return n;
        }

        // ---- 가-2: 쉴 때 정리 ----

        /// <summary>쉬는 일꾼을 찾는 간격(초). 시험이 줄인다.</summary>
        internal static float CheckEvery = 60f;
        /// <summary>시험 중엔 꺼 둔다(다른 시험 상자를 옮기면 안 됨) — vh_tidywalktest만 켬.</summary>
        internal static bool TestAllow;
        static float s_timer = 60f;

        /// <summary>
        /// CheckEvery초마다(호스트만): 거점 30m 안에서 쉬는(Idle·대기열 없음·질문 없음·묘비 없음·싸움 아님) 일꾼 하나가 정리한다.
        /// 플레이어가 모두 64m 밖이면 Instant, 아니면 걸어서(TidyJob). 다른 일꾼이 이미 정리 중이면 안 함.
        /// </summary>
        public static void Tick(float dt)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (TestRunner.Running && !TestAllow) return;
            s_timer -= dt;
            if (s_timer > 0f) return;
            s_timer = CheckEvery;
            WorkerNpc npc = null;
            foreach (var n in WorkerNpc.All)
            {
                if (n == null || n.Ai == null) continue;
                if (n.Ai.Job is TidyJob) return;
                if (npc == null && n.Ai.Current == WorkerAI.Mode.Idle && n.Ai.Queue.Count == 0 && Departures.AskedAt(n) < 0 && !n.Ai.HasChores
                    && !n.Ai.GuardBusy && !n.InInterior() && Utils.DistanceXZ(n.transform.position, n.Home) <= 30f) npc = n;
            }
            if (npc == null) return;
            List<Move> plan = Plan(npc.Home);
            if (plan.Count == 0) return;
            if (GatherJob.FarFromPlayers(npc.Home))
            {
                int moved = Instant(npc.Home);
                Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 상자 정리(플레이어 멀어 바로): {moved}개");
                return;
            }
            Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 상자 정리(걸어서): 옮길 것 {plan.Count}");
            npc.Ai.StartJob(new TidyJob(npc));
        }

        /// <summary>시험용: 다음 Tick에 바로 확인.</summary>
        internal static void TestNow() => s_timer = 0f;
    }
}
