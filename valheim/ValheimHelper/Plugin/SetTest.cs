using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_settest (⑧ 세트 계획): 세트 10번에 작업대 물건 둘(활·곤봉)을 저장하고 "!세트 10 1" → 세트 안 물건마다 하나씩 상자에 생김.
    /// 재료는 시험이 게임 레시피에서 읽어 상자에 넣고, 작업대·세트는 끝나면 치운다. 개수 보존(재료 전부 소모·물건 1개씩)도 확인.
    /// </summary>
    static class SetTest
    {
        const int SetNo = 10;
        static readonly string[] Items = { "Bow", "Club" };

        public static void Register() => TestRunner.Register("vh_settest", "세트메뉴 제작 시험", () => TestKit.Flat(Run()));

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
            var made = new List<GameObject>();
            SetMenu old = Brain.Sets.Get(SetNo);
            string oldItems = old == null ? null : string.Join(";", old.Items.ToArray());
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                Brain.Sets.Remove(SetNo);
                if (oldItems != null) Brain.Sets.Save(SetNo, oldItems.Split(';'), old.Name, old.By);
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            string n1 = w.Ragnar.NpcName;
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            var clear = new List<string>(Items);
            var recipes = new List<Recipe>();
            foreach (string item in Items)
            {
                Recipe found = null;
                foreach (var r in ObjectDB.instance.m_recipes)
                    if (r.m_item != null && Utils.GetPrefabName(r.m_item.gameObject) == item) found = r;
                if (found != null) { recipes.Add(found); foreach (var req in found.m_resources) clear.Add(Utils.GetPrefabName(req.m_resItem.gameObject)); }
            }
            checks.Add("레시피 둘 있음", recipes.Count == Items.Length);
            if (recipes.Count != Items.Length) yield break;
            TestKit.ClearBaseStock(w, clear.ToArray());
            var need = new Dictionary<string, int>();
            foreach (var r in recipes)
                foreach (var req in r.m_resources)
                {
                    string key = Utils.GetPrefabName(req.m_resItem.gameObject);
                    need.TryGetValue(key, out int n);
                    need[key] = n + req.m_amount;
                }
            foreach (var kv in need) TestKit.PutInChest(w.Chest, kv.Key, kv.Value);
            var g = Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"), GatherTest.Ground(w.Home - right * 4f), Quaternion.identity);
            made.Add(g);
            yield return new WaitForSeconds(1f);

            int saved = Brain.Sets.Save(SetNo, Items, "시험 세트", "시험");
            checks.Add("세트 10번 저장", saved == SetNo);
            GatherTest.Say($"!{n1} 세트 {SetNo} 1", null);
            yield return new WaitForSeconds(1f);
            yield return TestKit.WaitFor(() => Stock(w, "Bow") >= 1 && Stock(w, "Club") >= 1, 150f);
            checks.Add($"활·곤봉이 상자에 ({TestKit.Waited:0}초, 활 {Stock(w, "Bow")}, 곤봉 {Stock(w, "Club")})", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 10f);
            checks.Add("끝나고 계획 일 없음", TestKit.Ok);
            string left = "";
            bool conserved = Stock(w, "Bow") == 1 && Stock(w, "Club") == 1;
            foreach (var kv in need)
            {
                int c = Stock(w, kv.Key);
                left += $" {kv.Key}={c}";
                if (c != 0) conserved = false;
            }
            checks.Add($"개수 보존 (활 {Stock(w, "Bow")}, 곤봉 {Stock(w, "Club")},{left})", conserved);
        }

        static int Stock(TestKit.World w, string prefab)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(w.Home)) n += GatherTest.Count(c, prefab);
            return n;
        }
    }
}
