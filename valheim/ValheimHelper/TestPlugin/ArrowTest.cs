using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_arrowtest (화살·볼트 값 읽기 — 사용자 11-13 "화살 아끼기" 가): ObjectDB 제작법 중 결과물이 Arrow·Bolt인 것의
    /// 재료·개수·제작대와 그 화살의 피해 점수(Gear.WeaponScore)를 diag에 남긴다. 게임 안 값만 읽고 아무것도 바꾸지 않는다.
    /// </summary>
    static class ArrowTest
    {
        public static void Register() => TestRunner.Register("vh_arrowtest", "화살·볼트 제작법 값 읽기", () => TestKit.Flat(Run()));

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            try { yield return Body(w, checks); }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);

            int count = 0;
            foreach (Recipe r in ObjectDB.instance.m_recipes)
            {
                if (r == null || r.m_item == null) continue;
                string name = r.m_item.name;
                if (!name.Contains("Arrow") && !name.Contains("Bolt")) continue;
                count++;
                var parts = new List<string>();
                foreach (Piece.Requirement req in r.m_resources)
                    if (req.m_resItem != null) parts.Add($"{req.m_resItem.name} {req.m_amount}");
                string station = r.m_craftingStation == null ? "맨손" : r.m_craftingStation.m_name;
                TestRunner.Log($"제작법 {name} x{r.m_amount} ← {string.Join(", ", parts)} · 제작대 {station}");
            }
            checks.Add($"화살·볼트 제작법 읽음 ({count}개)", count > 0);

            // 화살 자체의 피해 점수(Gear.WeaponScore와 같은 합)와 이름 — 활 피해 칸에 쓴다
            foreach (string name in new[] { "ArrowWood", "ArrowFlint", "ArrowBone", "ArrowFire", "ArrowFrost", "ArrowObsidian", "ArrowBronze", "ArrowIron", "ArrowSilver", "ArrowCarapace", "ArrowNeedle", "ArrowPoison", "BoltWood", "BoltBronze", "BoltIron", "BoltBlackmetal", "BoltCarapace" })
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(name);
                if (prefab == null) { TestRunner.Log($"화살 {name}: 없음"); continue; }
                ItemDrop.ItemData it = prefab.GetComponent<ItemDrop>().m_itemData;
                TestRunner.Log($"화살 {name}: 피해 점수 {Gear.WeaponScore(it):0.#}, 이름 {it.m_shared.m_name}");
            }
        }
    }
}
