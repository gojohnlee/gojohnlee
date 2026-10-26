using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_plainsfarmtest (⑫ 사): 평원 자리(VH_BIOME=plains)에서
    /// A) 보리 4를 시키면(Farm.Plan, 씨앗 = 수확물) 거점 옆 밭에 보리 새싹을 심고, 자라게 한 뒤 거둬 상자에 넣는지(보리 수 보존 + 수확).
    /// B) "!밭"(Farm.Fill): 상자 보리 4·아마 4 → 먹을 몫을 남겨 반씩(보리 2·아마 2)만 심는지.
    /// C) 록스 사냥 판단: 평원 차림으로 록스 한 마리 → Guard.SafePrey 판단을 따르는지(곁의 플로키까지 셈해도 안 되면 덤비지 않고 "더 센 무기를 줘" preyStrong, 되면 잡음).
    /// </summary>
    static class PlainsFarmTest
    {
        public static void Register() => TestRunner.Register("vh_plainsfarmtest", "평원 밭(보리·아마)·록스 판단 시험", () => TestKit.Flat(Run()));

        const string Barley = "Barley", Flax = "Flax", Meat = "LoxMeat";

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
            w.CountsCreatures = true; // 록스를 부르는 시험
            FarmField field = null;
            var spawned = new List<GameObject>();
            try { yield return Body(w, checks, f => field = f, spawned); }
            finally
            {
                if (field != null) { Clean(field); Brain.Farms.Remove(field.X, field.Z); }
                FulingTest.Cleanup(spawned);
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Action<FarmField> made, List<GameObject> spawned)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            Crop barley = Crops.ForProduce(Barley), flax = Crops.ForProduce(Flax);
            checks.Add($"보리·아마 작물 ({barley?.Sapling}, {flax?.Sapling})", barley != null && flax != null && Crops.CultivatePiece != null);
            if (barley == null || flax == null || Crops.CultivatePiece == null) yield break;
            TestKit.ClearBaseStock(w, Crops.Cultivator, Barley, Flax, Meat);

            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            Vector3 at = GatherTest.Ground(w.Home - right * 12f);
            var field = new FarmField(at.x, at.z, 3);
            made(field);
            Clean(field);
            int biome = Scout.BiomeAt(at);
            checks.Add($"밭이 평원 (바이옴 {biome}, VH_BIOME=plains)", biome == (int)Heightmap.Biome.Plains);
            if (biome != (int)Heightmap.Biome.Plains) yield break;

            // A) 보리 4 → 계획대로 심고, 자라게 해서 거둠
            var want = new List<Ingredient> { new Ingredient(Barley, 4) };
            FarmPlan plan = Farm.Plan(Crops.All, biome, 100, item => item == Barley ? 6 : 0, want);
            int n = plan.Count(barley.Sapling), need = (4 + Mathf.Max(1, barley.Yield) - 1) / Mathf.Max(1, barley.Yield);
            checks.Add($"계획: 보리 6 → 보리 새싹 {n} (한 포기 {barley.Yield}개, 씨앗 유지 없음)", n == need && plan.Plant.Count == 1);
            TestKit.PutInChest(w.Chest, Barley, 6);
            TestKit.PutInChest(w.Chest, Crops.Cultivator, 1);
            int bag0 = FarmTest.Bag(w, Barley);
            var job = new FarmJob(w.Ragnar, field, plan);
            w.Ragnar.Ai.StartJob(job);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 180f);
            int planted = FarmTest.PlantsOf(field, barley.Sapling);
            int left = FarmTest.Stock(w, Barley) + FarmTest.Bag(w, Barley) - bag0;
            checks.Add($"보리 심음·돌아옴 ({TestKit.Waited:0}초, 새싹 {planted}/{n}, 남은 보리 {left}, 경작기 상자 {FarmTest.Stock(w, Crops.Cultivator)})",
                TestKit.Ok && planted == n && left + planted == 6 && FarmTest.Stock(w, Crops.Cultivator) == 1);

            yield return new WaitForSeconds(12f); // 심은 뒤 10초가 지나야 바닐라가 상태를 판정한다
            List<Plant> plants = FarmTest.Plants(field);
            foreach (var p in plants) TestRunner.Log($"DIAG 보리 새싹 {p.transform.position} 상태 {p.GetStatus()}");
            foreach (var p in plants) { p.m_growTime = 1f; p.m_growTimeMax = 1f; }
            yield return TestKit.WaitFor(() => FarmJob.Ripe(field).Count >= planted && planted > 0, 40f);
            checks.Add($"평원에서 보리 자람 ({TestKit.Waited:0}초, {FarmJob.Ripe(field).Count}/{planted})", TestKit.Ok);
            List<Pickable> ripe = FarmJob.Ripe(field);
            int expect = 0;
            foreach (var p in ripe) expect += p.m_dontScale ? p.m_amount : Mathf.Max(p.m_minAmountScaled, Game.instance.ScaleDrops(p.m_itemPrefab, p.m_amount));
            int stock0 = FarmTest.Stock(w, Barley);
            var harvest = new FarmJob(w.Ragnar, field, null, true);
            w.Ragnar.Ai.StartJob(harvest);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 150f);
            int got = FarmTest.Stock(w, Barley) - stock0;
            checks.Add($"거둔 보리 상자에 ({TestKit.Waited:0}초, 거둠 {harvest.Harvested}/{ripe.Count}, 상자 +{got}/{expect}, 짐 {FarmTest.Bag(w, Barley) - bag0})",
                TestKit.Ok && ripe.Count > 0 && harvest.Harvested == ripe.Count && got == expect);

            // B) "!밭": 보리 4·아마 4 → 씨앗 = 수확물이라 반씩만(보리 2·아마 2)
            Clean(field);
            TestKit.ClearBaseStock(w, Barley, Flax);
            TestKit.PutInChest(w.Chest, Barley, 4);
            TestKit.PutInChest(w.Chest, Flax, 4);
            Brain.Farms.Set(field.X, field.Z, field.Radius);
            Commands.StartFarm(w.Ragnar, Brain.Farms.At(field.X, field.Z), false);
            checks.Add($"!밭 → 밭일 ({w.Ragnar.Ai.Job?.GetType().Name ?? "없음"})", w.Ragnar.Ai.Job is FarmJob);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 240f);
            int b = FarmTest.PlantsOf(field, barley.Sapling), f2 = FarmTest.PlantsOf(field, flax.Sapling);
            int bl = FarmTest.Stock(w, Barley) + FarmTest.Bag(w, Barley) - bag0, fl = FarmTest.Stock(w, Flax);
            checks.Add($"!밭 보리·아마 반씩 ({TestKit.Waited:0}초, 보리 {b}·아마 {f2}, 남은 보리 {bl}·아마 {fl})",
                TestKit.Ok && b == 2 && f2 == 2 && bl == 2 && fl == 2);
            Brain.Farms.Remove(field.X, field.Z);
            Clean(field);
            TestKit.ClearBaseStock(w, Crops.Cultivator, Barley, Flax);

            // C) 록스 사냥 판단: 평원 차림(풀링 시험과 같음)으로 록스 한 마리
            GameObject loxPrefab = ZNetScene.instance.GetPrefab("Lox");
            checks.Add("록스는 록스 고기 사냥감", loxPrefab != null && Prey.Is(Meat, loxPrefab.GetComponent<Character>()));
            if (loxPrefab == null) yield break;
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                foreach (var g in FulingTest.PlainsGear) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(g), 1);
                Prep.WearBest(npc);
                var food = new Inventory("vh_test_food", null, 4, 2);
                foreach (var g in new[] { "CookedLoxMeat", "Sausages", "SerpentStew" }) food.AddItem(ObjectDB.instance.GetItemPrefab(g), 1);
                npc.ResetMeals();
                npc.EatFrom(food);
                npc.Heal(1000f, false);
            }
            yield return new WaitForSeconds(1f);
            Quaternion rot = w.Player.transform.rotation;
            Character lox = FulingTest.Spawn(loxPrefab, w.Home + rot * new Vector3(0f, 0f, 45f), spawned);
            yield return new WaitForSeconds(0.5f);
            bool safe = w.Ragnar.Ai.Guard.SafePrey(lox, out Character pack);
            TestRunner.Log($"C 판단: 차림 체력 {w.Ragnar.GetMaxHealth():0}·갑옷 {w.Ragnar.GetBodyArmor():0} → 록스 {(safe ? "혼자 잡을 만함" : pack != null ? "무리 때문에 안 됨" : "혼자 잡기엔 셈")}");
            checks.Add($"록스 판단에 무리 없음 ({(pack ? pack.name : "없음")})", pack == null);
            int heard = w.Heard.Count, deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
            var states = new HashSet<string>();
            GatherTest.Say($"!{Meat} 1", null);
            float t = 0f;
            while (t < 150f && (t < 2f || w.Ragnar.Ai.Job is GatherJob || w.Floki.Ai.Job is GatherJob))
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                foreach (var npc in new[] { w.Ragnar, w.Floki })
                    if (npc.Ai.Guard != null && npc.Ai.Guard.StateName != "Calm") states.Add(npc.Ai.Guard.StateName);
            }
            bool said = false;
            for (int i = heard; i < w.Heard.Count; i++) { TestRunner.Log($"들음: {w.Heard[i]}"); if (w.Heard[i].Contains("장비") || w.Heard[i].Contains("무기")) said = true; }
            bool done = !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob);
            string st = string.Join(",", new List<string>(states).ToArray());
            if (safe)
            {
                yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, Meat) >= 1, 60f);
                checks.Add($"잡을 만하면 잡아 고기를 상자에 ({t:0}초, 고기 {GatherTest.CountBase(w.Home, Meat)}, 록스 {(lox == null || lox.IsDead() ? "죽음" : "삶")})", TestKit.Ok);
                checks.Add($"안 죽음 (라그나 {w.Ragnar.GetHealth():0}, 플로키 {w.Floki.GetHealth():0})", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
            }
            else
            {
                checks.Add($"센 록스는 그만둠 ({t:0.0}초)", done);
                checks.Add("장비를 달라고 말함(preyStrong — 곁의 플로키까지 셈해도 안 됨)", said);
                checks.Add($"록스에 덤비지 않음 (경계 [{st}], 록스 {(lox == null || lox.IsDead() ? "죽음" : "삶 " + lox.GetHealth().ToString("0"))})", !states.Contains("Fight") && lox != null && !lox.IsDead());
            }
            if (lox != null && !lox.IsDead()) FulingTest.Kill(lox);
            TestKit.ClearBaseStock(w, Meat);
            yield return new WaitForSeconds(2f);
        }

        /// <summary>밭(반경 +1m)의 새싹·다 자란 작물을 지운다(시험 전·후).</summary>
        static void Clean(FarmField f)
        {
            var big = new FarmField(f.X, f.Z, f.Radius + 1);
            foreach (var p in FarmTest.Plants(big)) ZNetScene.instance.Destroy(p.gameObject);
            foreach (var p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
                if (p != null && Crops.IsGrown(Utils.GetPrefabName(p.gameObject)) && big.Contains(p.transform.position.x, p.transform.position.z))
                    ZNetScene.instance.Destroy(p.gameObject);
        }
    }
}
