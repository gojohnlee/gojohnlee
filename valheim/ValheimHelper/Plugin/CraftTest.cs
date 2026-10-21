using System.Collections;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_crafttest (⑥ 제작 실행기): 명령이 PlanJob을 시작해 실제로 끝내는지.
    /// 1) 제작대 앞 제작: 활 재료를 거점 상자에 넣고 제작대를 세운 뒤 "!활" → 상자에 활이 생기고 재료가 줄어듦.
    /// 2) 제련: 구리 광석·석탄을 상자에 넣고 제련소를 세운 뒤 "!구리주괴 2" → 제련소에 넣고 기다렸다 꺼내 상자에 구리 주괴 2.
    /// 설비는 시험이 직접 만들고 끝나면 지운다. 게임 시계로 기다리므로 2)는 1~2분 걸린다.
    /// </summary>
    static class CraftTest
    {
        public static void Register() => TestRunner.Register("vh_crafttest", "제작 실행기(PlanJob) 시험", () => TestKit.Flat(Run()));

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
            var made = new System.Collections.Generic.List<GameObject>();
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            yield return Stop();
            TestKit.End(w, checks);
        }

        static GameObject Place(System.Collections.Generic.List<GameObject> made, string prefab, Vector3 at)
        {
            at = GatherTest.Ground(at);
            var g = Object.Instantiate(ZNetScene.instance.GetPrefab(prefab), at, Quaternion.identity);
            made.Add(g);
            return g;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Collections.Generic.List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Claude != null && w.Alarak != null && w.Chest != null);
            if (w.Claude == null || w.Alarak == null || w.Chest == null) yield break;
            string n1 = w.Claude.NpcName;
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            TestKit.ClearBaseStock(w, "Wood", "LeatherScraps", "DeerHide", "CopperOre", "Copper", "Coal", "Bow");

            // 1) 제작대 앞 제작 — 활 재료는 게임 레시피에서 읽는다
            Recipe bow = null;
            foreach (var r in ObjectDB.instance.m_recipes)
                if (r.m_item != null && Utils.GetPrefabName(r.m_item.gameObject) == "Bow") bow = r;
            checks.Add("활 레시피 있음", bow != null);
            if (bow == null) yield break;
            foreach (var req in bow.m_resources)
                TestKit.PutInChest(w.Chest, Utils.GetPrefabName(req.m_resItem.gameObject), req.m_amount);
            Place(made, "piece_workbench", w.Home - right * 4f);
            yield return new WaitForSeconds(1f);
            string bowName = Localization.instance.Localize(bow.m_item.m_itemData.m_shared.m_name);
            yield return Say($"!{n1} {bowName} 1", 1f);
            yield return TestKit.WaitFor(() => Stock(w, "Bow") >= 1, 90f);
            checks.Add($"활이 거점 상자에 생김 ({TestKit.Waited:0}초, {w.Claude.Ai.Current})", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 10f);
            checks.Add("끝나고 계획 일 없음", TestKit.Ok);
            yield return Stop();

            // 2) 제련 — 구리 광석 4·석탄 20, 제련소
            TestKit.PutInChest(w.Chest, "CopperOre", 4);
            TestKit.PutInChest(w.Chest, "Coal", 20);
            if (ZNetScene.instance.GetPrefab("smelter") == null) { checks.Add("제련소 프리팹 있음", false); yield break; }
            Place(made, "smelter", w.Home - right * 7f);
            yield return new WaitForSeconds(1f);
            string barName = Localization.instance.Localize(ObjectDB.instance.GetItemPrefab("Copper").GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
            TestRunner.Log($"구리 주괴 게임 이름: [{barName}] → Items.Resolve {Items.Resolve(barName)}, \"구리주괴\" → {Items.Resolve("구리주괴")}");
            yield return Say($"!{n1} 구리주괴 2", 1f);
            checks.Add($"제련 계획 시작 ({w.Claude.Ai.Current}, {w.Claude.Ai.Job?.GetType().Name})", w.Claude.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => Stock(w, "Copper") >= 2, 240f);
            checks.Add($"구리 주괴 2개가 상자에 ({TestKit.Waited:0}초, 광석 {Stock(w, "CopperOre")}, 석탄 {Stock(w, "Coal")})", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 10f);
            checks.Add("끝나고 계획 일 없음", TestKit.Ok);
        }

        static IEnumerator Say(string text, float wait)
        {
            GatherTest.Say(text, null);
            yield return new WaitForSeconds(wait);
        }

        static int Stock(TestKit.World w, string prefab)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(w.Home)) n += GatherTest.Count(c, prefab);
            return n;
        }

        static IEnumerator Stop() => Say("!멈춰", 1f);
    }
}
