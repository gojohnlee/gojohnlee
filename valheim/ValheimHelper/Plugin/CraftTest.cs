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

        /// <summary>
        /// 제련 개수 보존: 원료 from과 결과 to가 상자(거점)·두 일꾼 짐·땅(설비·깃발 둘레 6m)·설비 안(대기열 s_queued + 꺼낼 것 s_spawnAmount)을 합쳐 정확히 total.
        /// 원료 하나 = 결과 하나(제련소·용광로). 일꾼이 아직 꺼내는 중이면 땅·짐이 바뀌니 10초까지 기다려 다시 센다.
        /// </summary>
        static IEnumerator Conserved(TestKit.World w, GatherTest.Checks checks, GameObject machine, string from, string to, int total)
        {
            string where = "";
            int sum = -1;
            for (int i = 0; i < 10 && sum != total; i++)
            {
                if (i > 0) yield return new WaitForSeconds(1f);
                int chest = Stock(w, from) + Stock(w, to);
                int bag = BaseTest.Carried(w.Ragnar, from) + BaseTest.Carried(w.Ragnar, to) + BaseTest.Carried(w.Floki, from) + BaseTest.Carried(w.Floki, to);
                Vector3 at = machine != null ? machine.transform.position : w.Home;
                int ground = BaseTest.OnGround(at, from) + BaseTest.OnGround(at, to) + BaseTest.OnGround(w.Home, from) + BaseTest.OnGround(w.Home, to);
                ZDO z = machine != null ? machine.GetComponent<ZNetView>()?.GetZDO() : null;
                int inside = z != null ? z.GetInt(ZDOVars.s_queued) + z.GetInt(ZDOVars.s_spawnAmount) : 0;
                sum = chest + bag + ground + inside;
                where = $"상자 {chest}(원료 {Stock(w, from)}·결과 {Stock(w, to)}), 짐 {bag}, 땅 {ground}, 설비 안 {inside}, 보충 누적 {MachineStep.Refilled}";
            }
            checks.Add($"제련 개수 보존 {from}→{to} ({where} → 합 {sum} = {total})", sum == total);
        }

        /// <summary>
        /// 0) 설비만 없음(부분 계획, 외부 검토 + 사용자 승인 10-26): 제작대 없이 "!활 1"(목재 말고 재료는 상자에) → 목재만 모아 두고
        /// "재료는 모았는데 제작대가 없어"(craft.missingDone). 활은 안 생기고 다른 재료는 그대로. 끝나면 활 재료를 비워 1)이 개수를 다시 셈.
        /// </summary>
        static IEnumerator MissingStation(TestKit.World w, GatherTest.Checks checks, Recipe bow)
        {
            string station = bow.m_craftingStation != null ? bow.m_craftingStation.m_name : null;
            bool none = station != null && !Recipes.At(w.Home).HasStation(station, 1);
            checks.Add($"제작대 없는 거점에서 시작 ({station})", none);
            if (!none) yield break;
            int wood = 0;
            var others = new System.Collections.Generic.List<string>();
            foreach (var req in bow.m_resources)
            {
                string p = Utils.GetPrefabName(req.m_resItem.gameObject);
                if (p == "Wood") wood = req.m_amount;
                else { TestKit.PutInChest(w.Chest, p, req.m_amount); others.Add(p); }
            }
            checks.Add($"활 재료에 목재 ({wood}개)", wood > 0);
            if (wood == 0) yield break;
            string bowName = Localization.instance.Localize(bow.m_item.m_itemData.m_shared.m_name);
            int heard0 = w.Heard.Count;
            yield return Say($"!{w.Ragnar.NpcName} {bowName} 1", 1f);
            checks.Add($"설비가 없어도 재료 모으기 시작 ({w.Ragnar.Ai.Job?.GetType().Name} {w.Ragnar.Ai.Job?.StepName})", w.Ragnar.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null && Stock(w, "Wood") >= wood, 240f);
            checks.Add($"목재 {Stock(w, "Wood")}/{wood} 모아 두고 끝 ({TestKit.Waited:0}초, {w.Ragnar.Ai.Current})", TestKit.Ok);
            string said = null;
            for (int i = heard0; i < w.Heard.Count; i++)
                if (w.Heard[i].StartsWith(w.Ragnar.NpcName) && (w.Heard[i].Contains("모았") || w.Heard[i].Contains("모아 뒀"))) said = w.Heard[i];
            checks.Add($"끝에 설비를 지어 달라고 함 ({said ?? "못 들음"})", said != null);
            bool kept = Stock(w, "Bow") == 0;
            foreach (var p in others) kept &= Stock(w, p) == Need(bow, p);
            checks.Add($"활은 안 생기고 다른 재료는 그대로 (활 {Stock(w, "Bow")})", kept);
            yield return Stop();
            foreach (var req in bow.m_resources) TestKit.ClearBaseStock(w, Utils.GetPrefabName(req.m_resItem.gameObject));
        }

        static int Need(Recipe r, string prefab)
        {
            foreach (var req in r.m_resources) if (Utils.GetPrefabName(req.m_resItem.gameObject) == prefab) return req.m_amount;
            return 0;
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
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            string n1 = w.Ragnar.NpcName;
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            TestKit.ClearBaseStock(w, "Wood", "LeatherScraps", "DeerHide", "CopperOre", "Copper", "Coal", "Bow");

            // 1) 제작대 앞 제작 — 활 재료는 게임 레시피에서 읽는다
            Recipe bow = null;
            foreach (var r in ObjectDB.instance.m_recipes)
                if (r.m_item != null && Utils.GetPrefabName(r.m_item.gameObject) == "Bow") bow = r;
            checks.Add("활 레시피 있음", bow != null);
            if (bow == null) yield break;
            yield return MissingStation(w, checks, bow);
            foreach (var req in bow.m_resources)
                TestKit.PutInChest(w.Chest, Utils.GetPrefabName(req.m_resItem.gameObject), req.m_amount);
            Place(made, "piece_workbench", w.Home - right * 4f);
            yield return new WaitForSeconds(1f);
            string bowName = Localization.instance.Localize(bow.m_item.m_itemData.m_shared.m_name);
            yield return Say($"!{n1} {bowName} 1", 1f);
            yield return TestKit.WaitFor(() => Stock(w, "Bow") >= 1, 90f);
            checks.Add($"활이 거점 상자에 생김 ({TestKit.Waited:0}초, {w.Ragnar.Ai.Current})", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 10f);
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
            GameObject smelter = Place(made, "smelter", w.Home - right * 7f);
            yield return new WaitForSeconds(1f);
            string barName = Localization.instance.Localize(ObjectDB.instance.GetItemPrefab("Copper").GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
            TestRunner.Log($"구리 주괴 게임 이름: [{barName}] → Items.Resolve {Items.Resolve(barName)}, \"구리주괴\" → {Items.Resolve("구리주괴")}");
            int refilled0 = MachineStep.Refilled;
            MachineStep.TestLose = 1; // 정리 시작 때 짐의 주괴 1개를 지워 소실 보충을 본다(E001)
            Vector3 playerAt = w.Player.transform.position; // 5m 안 플레이어가 있으면 보충 안 함(복제 방지) → 제련 동안 25m 밖에
            w.Player.TeleportTo(GatherTest.Ground(w.Home + right * 25f) + Vector3.up * 0.3f, w.Player.transform.rotation, false);
            yield return Say($"!{n1} 구리주괴 2", 1f);
            checks.Add($"제련 계획 시작 ({w.Ragnar.Ai.Current}, {w.Ragnar.Ai.Job?.GetType().Name})", w.Ragnar.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => Stock(w, "Copper") >= 2, 240f);
            checks.Add($"구리 주괴 2개가 상자에 ({TestKit.Waited:0}초, 광석 {Stock(w, "CopperOre")}, 석탄 {Stock(w, "Coal")})", TestKit.Ok);
            int dist = (int)Vector3.Distance(Player.m_localPlayer.transform.position, w.Ragnar.transform.position);
            checks.Add($"짐에서 1개 잃으면 보충 1개 (보충 {MachineStep.Refilled - refilled0}, 시험 흉내 남음 {MachineStep.TestLose}, 플레이어와 {dist}m)", MachineStep.Refilled - refilled0 == 1);
            MachineStep.TestLose = 0;
            w.Player.TeleportTo(playerAt, w.Player.transform.rotation, false);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 10f);
            checks.Add("끝나고 계획 일 없음", TestKit.Ok);
            // 광석 4개 = 어디에 있든 합 4(복제도 소실도 없음, 사용자 2026-10-22 — E001): 상자 + 일꾼 짐 + 땅(제련소·상자 둘레) + 제련소 안
            yield return Conserved(w, checks, smelter, "CopperOre", "Copper", 4);
            yield return Stop();
            yield return Blast(w, checks, made, right);
            yield return Ferment(w, checks, made, right);
            yield return FullChest(w, checks);
        }

        /// <summary>3) 용광로: 고철 4·석탄 20 → "!철주괴 2" → 용광로(blastfurnace)에 넣고 기다렸다 꺼내 상자에 철 주괴 2(⑦ 철 4조각).</summary>
        static IEnumerator Blast(TestKit.World w, GatherTest.Checks checks, System.Collections.Generic.List<GameObject> made, Vector3 right)
        {
            string n1 = w.Ragnar.NpcName;
            TestKit.ClearBaseStock(w, "IronScrap", "Iron", "Coal");
            TestKit.PutInChest(w.Chest, "IronScrap", 4);
            TestKit.PutInChest(w.Chest, "Coal", 20);
            if (ZNetScene.instance.GetPrefab("blastfurnace") == null) { checks.Add("용광로 프리팹 있음", false); yield break; }
            GameObject furnace = Place(made, "blastfurnace", w.Home - right * 10f);
            yield return new WaitForSeconds(1f);
            yield return Say($"!{n1} 철주괴 2", 1f);
            checks.Add($"용광로 계획 시작 ({w.Ragnar.Ai.Current}, {w.Ragnar.Ai.Job?.GetType().Name})", w.Ragnar.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => Stock(w, "Iron") >= 2, 240f);
            checks.Add($"철 주괴 2개가 상자에 ({TestKit.Waited:0}초, 고철 {Stock(w, "IronScrap")}, 석탄 {Stock(w, "Coal")})", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 10f);
            yield return Conserved(w, checks, furnace, "IronScrap", "Iron", 4);
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
            string n1 = w.Ragnar.NpcName;
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
            if (!TestKit.Ok) // 회차 128 한 번: 땅에 없음(로그에 "상자 앞에 둠"도 없음) → 어느 상자에 들어갔고 빈칸이 어떻게 생겼는지
                foreach (var c in Chests.NearBase(w.Home))
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var it in c.GetInventory().GetAllItems()) if (it.m_stack < it.m_shared.m_maxStackSize || it.m_dropPrefab?.name == "Bow") names.Add($"{it.m_dropPrefab?.name}x{it.m_stack}");
                    TestRunner.Log($"DIAG 상자 {Utils.DistanceXZ(c.transform.position, w.Home):0.0}m 빈칸 {c.GetInventory().GetEmptySlots()} 덜 찬 더미 [{string.Join(", ", names.ToArray())}]");
                }
            checks.Add($"짐에는 안 넣음 (짐 활 {w.Ragnar.CountItem("Bow")})", w.Ragnar.CountItem("Bow") == 0);
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
            string n1 = w.Ragnar.NpcName;
            TestKit.ClearBaseStock(w, Base, Mead);
            foreach (var npc in new[] { w.Ragnar, w.Floki }) RemoveFromBag(npc, Mead);
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
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 60f);
            checks.Add($"지붕 없는 발효기엔 안 넣음 (바탕 {Stock(w, Base)}, 발효기 {fz.GetInt(ZDOVars.s_content) != 0})",
                TestKit.Ok && Stock(w, Base) == 1 && fz.GetInt(ZDOVars.s_content) == 0);
            yield return Stop();

            MachineStep.TestIgnoreRoof = true;
            try
            {
                yield return Say($"!{n1} {meadName} 6", 1f);
                checks.Add($"발효 계획 시작 ({w.Ragnar.Ai.Current}, {w.Ragnar.Ai.Job?.GetType().Name})", w.Ragnar.Ai.Job is PlanJob);
                yield return TestKit.WaitFor(() => fz.GetInt(ZDOVars.s_content) != 0, 60f);
                checks.Add($"발효기에 바탕을 넣음 ({TestKit.Waited:0}초, 상자 바탕 {Stock(w, Base)})", TestKit.Ok && Stock(w, Base) == 0);
                if (!TestKit.Ok) yield break;
                // 게임 시계로 40분은 못 기다리니 시작 시각을 당긴다(지붕 없어도 다 익은 뒤엔 바닐라가 시계를 되돌리지 않음)
                fz.Set(ZDOVars.s_startTime, ZNet.instance.GetTime().AddSeconds(-fe.GetComponent<Fermenter>().m_fermentationDuration - 10).Ticks);
                yield return TestKit.WaitFor(() => Total(w, Mead) >= 6 && w.Ragnar.Ai.Job == null, 60f);
                checks.Add($"독 저항 미드 6개 (상자+짐 {Total(w, Mead)}, {TestKit.Waited:0}초, {w.Ragnar.Ai.Current})", TestKit.Ok);
                checks.Add($"넘는 미드는 상자로 (상자 {Stock(w, Mead)} ≥ 4, 짐은 몫 2까지)", Stock(w, Mead) >= 6 - Core.Npc.Resist.ToCarry);
                checks.Add($"발효 뒤 개수 보존 (미드 {Total(w, Mead)} = 6, 바탕 {Stock(w, Base)} = 0, 발효기 빔)",
                    Total(w, Mead) == 6 && Stock(w, Base) == 0 && fz.GetInt(ZDOVars.s_content) == 0);
            }
            finally
            {
                MachineStep.TestIgnoreRoof = false;
                TestKit.ClearBaseStock(w, Base, Mead);
                foreach (var npc in new[] { w.Ragnar, w.Floki }) RemoveFromBag(npc, Mead);
            }
        }

        /// <summary>상자 + 두 일꾼 짐(준비가 저항 미드를 둘씩 들고 다님).</summary>
        static int Total(TestKit.World w, string prefab)
        {
            string name = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            int n = Stock(w, prefab);
            foreach (var npc in new[] { w.Ragnar, w.Floki }) if (npc != null) n += npc.GetInventory().CountItems(name);
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
