using System.Collections.Generic;
using ValheimHelper.Core.Storage;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 일꾼 상자 정리(상자 기능 ② 가-1, 사용자 결정 2026-10-23): 거점 125m 안 상자에서 같은 물건을 가장 많이 든 상자로 모은다
    /// (그 물건 역할의 이름표 상자가 있으면 그쪽 — Unload와 같은 ChestNames.Select). 개인·보물 상자와 누가 연 상자는 안 건드린다.
    /// 상자끼리 한 프레임 안에 옮긴다(두 상자 모두 TryEdit) — 아침 정산(보는 사람 없음)에서 걷지 않고 한 번에. 불러와진 상자만.
    /// </summary>
    public static class Tidy
    {
        /// <summary>바로 정리. 옮긴 물건 수.</summary>
        public static int Instant(Vector3 home)
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

            int moved = 0;
            foreach (var kv in samples)
            {
                string name = kv.Key;
                var counts = new int[sorted.Count];
                for (int i = 0; i < counts.Length; i++) counts[i] = Count(sorted[i], name);
                ChestRole role = Chests.RoleFor(kv.Value);
                ChestTags want = role == ChestRole.Gear ? ChestTags.Gear : role == ChestRole.Food ? ChestTags.Food : ChestTags.Mats;
                int t = ChestTidy.Target(counts, ChestNames.Select(tagArr, want));
                if (t < 0) continue;
                for (int i = 0; i < counts.Length; i++)
                    if (i != t && counts[i] > 0) moved += Move(sorted[i], sorted[t], name);
            }
            if (moved > 0) BaseStock.Changed();
            return moved;
        }

        /// <summary>from의 그 물건을 to로(자리 있는 만큼). 옮긴 수. 두 상자 합이 바뀌면 오류 로그(있으면 안 됨).</summary>
        static int Move(Container from, Container to, string name)
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

        static int Count(Container c, string prefab)
        {
            int n = 0;
            foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) n += it.m_stack;
            return n;
        }
    }
}
