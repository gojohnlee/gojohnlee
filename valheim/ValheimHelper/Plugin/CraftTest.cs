using System.Collections;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_crafttest (⑥ 제작 실행기): 명령이 PlanJob을 시작해 실제로 끝내는지.
    /// 1) 제작대 앞 제작: 활 재료를 거점 상자에 넣고 제작대를 세운 뒤 "!활" → 상자에 활이 생기고 재료가 줄어듦.
    /// 2) 제련: 구리 광석·석탄을 상자에 넣고 제련소를 세운 뒤 "!구리주괴 2" → 제련소에 넣고 기다렸다 꺼내 상자에 구리 주괴 2.
    /// 3) 발효: 독 저항 미드 바탕 + 발효기 → 지붕 없으면 안 넣음, 지붕 판단 끄면 넣고 따라 6개(발효 시각은 시험이 당김).
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
            // 개수 보존: 넣은 재료는 정확히 다 쓰였고(복사 없음) 활은 1개뿐(공중 분해·복사 없음)
            string left = "";
            bool conserved = Stock(w, "Bow") == 1;
            foreach (var req in bow.m_resources)
            {
                int c = Stock(w, Utils.GetPrefabName(req.m_resItem.gameObject));
                left += $" {Utils.GetPrefabName(req.m_resItem.gameObject)}={c}";
                if (c != 0) conserved = false;
            }
            checks.Add($"제작 뒤 개수 보존 (활 {Stock(w, "Bow")},{left})", conserved);
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
            // 광석 4개에서 주괴가 늘어나지 않음(상자 광석 + 주괴 ≤ 4, 복사 없음)
            int ore = Stock(w, "CopperOre"), bars = Stock(w, "Copper");
            checks.Add($"제련 뒤 개수 보존 (광석 {ore} + 주괴 {bars} ≤ 4)", ore + bars <= 4 && bars <= 4);
            yield return Stop();
            yield return Blast(w, checks, made, right);
            yield return Ferment(w, checks, made, right);
            yield return FullChest(w, checks);
        }

        /// <summary>3) 용광로: 고철 4·석탄 20 → "!철주괴 2" → 용광로(blastfurnace)에 넣고 기다렸다 꺼내 상자에 철 주괴 2(⑦ 철 4조각).</summary>
        static IEnumerator Blast(TestKit.World w, GatherTest.Checks checks, System.Collections.Generic.List<GameObject> made, Vector3 right)
        {
            string n1 = w.Claude.NpcName;
            TestKit.ClearBaseStock(w, "IronScrap", "Iron", "Coal");
            TestKit.PutInChest(w.Chest, "IronScrap", 4);
            TestKit.PutInChest(w.Chest, "Coal", 20);
            if (ZNetScene.instance.GetPrefab("blastfurnace") == null) { checks.Add("용광로 프리팹 있음", false); yield break; }
            Place(made, "blastfurnace", w.Home - right * 10f);
            yield return new WaitForSeconds(1f);
            yield return Say($"!{n1} 철주괴 2", 1f);
            checks.Add($"용광로 계획 시작 ({w.Claude.Ai.Current}, {w.Claude.Ai.Job?.GetType().Name})", w.Claude.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => Stock(w, "Iron") >= 2, 240f);
            checks.Add($"철 주괴 2개가 상자에 ({TestKit.Waited:0}초, 고철 {Stock(w, "IronScrap")}, 석탄 {Stock(w, "Coal")})", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 10f);
            int scrap = Stock(w, "IronScrap"), bars = Stock(w, "Iron");
            checks.Add($"제련 뒤 개수 보존 (고철 {scrap} + 주괴 {bars} ≤ 4)", scrap + bars <= 4);
            yield return Stop();
        }

        /// <summary>
        /// 4) 상자 꽉 참(사용자 결정 2026-10-22): 활 재료(더미가 남게 하나씩 더)와 돌로 상자를 가득 채우고 "!활" → 만든 활은 짐이 아니라
        /// 상자 앞 땅에 놓이고 "상자 하나만 더" 대사. 놓인 활은 시험이 치운다.
        /// </summary>
        static IEnumerator FullChest(TestKit.World w, GatherTest.Checks checks)
        {
            Recipe bow = null;
            foreach (var r in ObjectDB.instance.m_recipes)
                if (r.m_item != null && Utils.GetPrefabName(r.m_item.gameObject) == "Bow") bow = r;
            if (bow == null) yield break;
            string n1 = w.Claude.NpcName;
            TestKit.ClearBaseStock(w, "Bow", "Stone");
            foreach (var req in bow.m_resources)
                TestKit.PutInChest(w.Chest, Utils.GetPrefabName(req.m_resItem.gameObject), req.m_amount + 1);
            var chests = Chests.NearBase(w.Home); // 지난 시험이 남긴 상자까지 전부 채운다
            int empty = 0;
            foreach (var c in chests)
            {
                for (int k = 0; k < 64 && c.GetInventory().GetEmptySlots() > 0; k++) TestKit.PutInChest(c, "Stone", 50);
                empty += c.GetInventory().GetEmptySlots();
            }
            checks.Add($"거점 상자 {chests.Count}개 가득 (빈칸 {empty})", empty == 0);
            int heard0 = w.Heard.Count;
            string bowName = Localization.instance.Localize(bow.m_item.m_itemData.m_shared.m_name);
            yield return Say($"!{n1} {bowName} 1", 1f);
            ItemDrop onGround = null;
            yield return TestKit.WaitFor(() => (onGround = BowNear(chests)) != null, 90f);
            checks.Add($"꽉 찬 상자 → 만든 활은 상자 앞 땅에 ({TestKit.Waited:0}초)", TestKit.Ok);
            checks.Add($"짐에는 안 넣음 (짐 활 {w.Claude.CountItem("Bow")})", w.Claude.CountItem("Bow") == 0);
            checks.Add("\"상자 하나만 더\" 대사", w.Heard.FindIndex(heard0, h => h.StartsWith(n1 + ":") && h.Contains("상자")) >= 0);
            yield return Stop();
            if (onGround != null && onGround.TryGetComponent(out ZNetView v) && v.IsValid()) v.Destroy();
            TestKit.ClearBaseStock(w, "Stone");
            foreach (var req in bow.m_resources) TestKit.ClearBaseStock(w, Utils.GetPrefabName(req.m_resItem.gameObject));
        }

        static ItemDrop BowNear(System.Collections.Generic.List<Container> chests)
        {
            foreach (var c in chests)
            foreach (Collider col in Physics.OverlapSphere(c.transform.position, 2.5f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d != null && Utils.GetPrefabName(d.gameObject) == "Bow") return d;
            }
            return null;
        }

        /// <summary>
        /// 3) 발효(D 저항 미드 2조각): 독 저항 미드 바탕 1개 + 발효기. 지붕 없으면 넣지 않고(바탕 그대로),
        /// 지붕 판단을 끄면 넣고 → 시험이 발효 시작 시각을 40분 앞으로 당김 → 다 익은 걸 보고 기다림을 끝내 따라 6개.
        /// </summary>
        static IEnumerator Ferment(TestKit.World w, GatherTest.Checks checks, System.Collections.Generic.List<GameObject> made, Vector3 right)
        {
            const string Base = "MeadBasePoisonResist", Mead = "MeadPoisonResist";
            string n1 = w.Claude.NpcName;
            TestKit.ClearBaseStock(w, Base, Mead);
            foreach (var npc in new[] { w.Claude, w.Alarak }) RemoveFromBag(npc, Mead);
            if (ZNetScene.instance.GetPrefab("fermenter") == null) { checks.Add("발효기 프리팹 있음", false); yield break; }
            TestKit.PutInChest(w.Chest, Base, 1);
            GameObject fe = Place(made, "fermenter", w.Home + right * 6f);
            yield return new WaitForSeconds(1f);
            ZDO fz = fe.GetComponent<ZNetView>().GetZDO();
            string layers = "";
            foreach (var col in fe.GetComponentsInChildren<Collider>()) layers += $" {LayerMask.LayerToName(col.gameObject.layer)}{(col.isTrigger ? "(t)" : "")}";
            TestRunner.Log($"발효기 충돌체 층:{layers}, 거점 설비 {string.Join(",", Recipes.At(w.Home).Processors.Keys)}");
            string meadName = Localization.instance.Localize(ObjectDB.instance.GetItemPrefab(Mead).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
            TestRunner.Log($"독 저항 미드 게임 이름: [{meadName}] → Items.Resolve {Items.Resolve(meadName)}");

            MachineStep.TestIgnoreRoof = false;
            yield return Say($"!{n1} {meadName} 6", 1f);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 60f);
            checks.Add($"지붕 없는 발효기엔 안 넣음 (바탕 {Stock(w, Base)}, 발효기 {fz.GetInt(ZDOVars.s_content) != 0})",
                TestKit.Ok && Stock(w, Base) == 1 && fz.GetInt(ZDOVars.s_content) == 0);
            yield return Stop();

            MachineStep.TestIgnoreRoof = true;
            try
            {
                yield return Say($"!{n1} {meadName} 6", 1f);
                checks.Add($"발효 계획 시작 ({w.Claude.Ai.Current}, {w.Claude.Ai.Job?.GetType().Name})", w.Claude.Ai.Job is PlanJob);
                yield return TestKit.WaitFor(() => fz.GetInt(ZDOVars.s_content) != 0, 60f);
                checks.Add($"발효기에 바탕을 넣음 ({TestKit.Waited:0}초, 상자 바탕 {Stock(w, Base)})", TestKit.Ok && Stock(w, Base) == 0);
                if (!TestKit.Ok) yield break;
                // 게임 시계로 40분은 못 기다리니 시작 시각을 당긴다(지붕 없어도 다 익은 뒤엔 바닐라가 시계를 되돌리지 않음)
                fz.Set(ZDOVars.s_startTime, ZNet.instance.GetTime().AddSeconds(-fe.GetComponent<Fermenter>().m_fermentationDuration - 10).Ticks);
                yield return TestKit.WaitFor(() => Total(w, Mead) >= 6 && w.Claude.Ai.Job == null, 60f);
                checks.Add($"독 저항 미드 6개 (상자+짐 {Total(w, Mead)}, {TestKit.Waited:0}초, {w.Claude.Ai.Current})", TestKit.Ok);
                checks.Add($"넘는 미드는 상자로 (상자 {Stock(w, Mead)} ≥ 4, 짐은 몫 2까지)", Stock(w, Mead) >= 6 - Core.Npc.Resist.ToCarry);
                checks.Add($"발효 뒤 개수 보존 (미드 {Total(w, Mead)} = 6, 바탕 {Stock(w, Base)} = 0, 발효기 빔)",
                    Total(w, Mead) == 6 && Stock(w, Base) == 0 && fz.GetInt(ZDOVars.s_content) == 0);
            }
            finally
            {
                MachineStep.TestIgnoreRoof = false;
                TestKit.ClearBaseStock(w, Base, Mead);
                foreach (var npc in new[] { w.Claude, w.Alarak }) RemoveFromBag(npc, Mead);
            }
        }

        /// <summary>상자 + 두 일꾼 짐(준비가 저항 미드를 둘씩 들고 다님).</summary>
        static int Total(TestKit.World w, string prefab)
        {
            string name = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            int n = Stock(w, prefab);
            foreach (var npc in new[] { w.Claude, w.Alarak }) if (npc != null) n += npc.GetInventory().CountItems(name);
            return n;
        }

        static void RemoveFromBag(WorkerNpc npc, string prefab)
        {
            if (npc == null) return;
            string name = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            int n = npc.GetInventory().CountItems(name);
            if (n > 0) npc.GetInventory().RemoveItem(name, n);
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
