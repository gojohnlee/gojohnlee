using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_fishtest (사용자 11-09 요청 나 "낚시"): 시험 캐릭터를 300m 안 바닷가 뭍으로 옮겨 거점을 세우고, 상자에 낚싯대·미끼, 물가 물속에 농어(Fish1) 4마리.
    /// 쉴 때 먹거리를 모두 쉬게 해(사냥·채집이 못 떠난 경우) 음식이 없으면 쉬던 일꾼이 낚으러 가서 물고기를 상자에 넣고 낚싯대를 돌려놓는지.
    /// 끝나면 원래 자리로 되돌린다(다음 시험 자리가 바뀌지 않게).
    /// </summary>
    static class FishTest
    {
        public static void Register() => TestRunner.Register("vh_fishtest", "쉴 때 낚시 시험", () => TestKit.Flat(Run()), 1200f);

        static readonly string[] Foods = { "Raspberry", "Blueberries", "Mushroom", "MushroomYellow", "Cloudberry", "Honey", "CookedMeat" };

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            Player p = Player.m_localPlayer;
            Vector3 origPos = p.transform.position;
            Quaternion origRot = p.transform.rotation;
            var w = new TestKit.World();
            var made = new List<GameObject>();
            bool moved = false;
            try
            {
                if (!ExploreTest.FindSea(origPos, out Vector3 shore, out _)) { checks.Add("300m 안 바다", false); }
                else
                {
                    Vector3 dir = shore - origPos;
                    dir.y = 0f;
                    dir.Normalize();
                    float wl = ZoneSystem.instance.m_waterLevel;
                    Vector3? land = null;
                    for (float k = 10f; k <= 40f && land == null; k += 2f)
                    {
                        Vector3 q = shore - dir * k;
                        float h = WorldGenerator.instance.GetHeight(q.x, q.z);
                        if (h > wl + 1.5f) land = new Vector3(q.x, h, q.z);
                    }
                    checks.Add($"바닷가 뭍 자리 (물가 {Utils.DistanceXZ(origPos, shore):0}m)", land != null);
                    if (land != null)
                    {
                        p.TeleportTo(land.Value + Vector3.up, Quaternion.LookRotation(-dir), true);
                        moved = true;
                        yield return new WaitForSeconds(2f);
                        yield return TestKit.WaitFor(() => !p.IsTeleporting(), 30f);
                        yield return new WaitForSeconds(2f);
                        yield return Body(w, checks, made, shore, dir);
                    }
                }
            }
            finally
            {
                Departures.TestIdleChores = false;
                foreach (string f in Departures.FoodRank()) Departures.TestSkip(f, false);
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                if (w.Player != null) TestKit.Restore(w);
                if (moved) p.TeleportTo(origPos, origRot, true);
            }
            if (moved) yield return TestKit.WaitFor(() => !p.IsTeleporting(), 30f);
            TestKit.End(w, checks);
        }

        static string SharedName(string prefab) => ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

        static int All(TestKit.World w, string prefab)
        {
            string name = SharedName(prefab);
            return GatherTest.CountBase(w.Home, prefab) + w.Ragnar.GetInventory().CountItems(name) + w.Floki.GetInventory().CountItems(name);
        }

        static bool Fishing(TestKit.World w) => w.Ragnar.Ai.Job is FishJob || w.Floki.Ai.Job is FishJob;

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made, Vector3 shore, Vector3 dir)
        {
            GameObject fish = ZNetScene.instance.GetPrefab("Fish1");
            bool prefabs = fish != null && ObjectDB.instance.GetItemPrefab("FishingRod") != null && ObjectDB.instance.GetItemPrefab("FishingBait") != null;
            checks.Add("농어·낚싯대·미끼 프리팹", prefabs);
            if (!prefabs) yield break;
            yield return TestKit.Setup(w);
            checks.Add("깃발·두 일꾼·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            foreach (string f in Departures.FoodRank()) Departures.TestSkip(f, true); // 쉴 때 먹거리가 하나도 못 떠난 경우
            Departures.TestIdleChores = true; // 시작 검사(Setup) 뒤에 켬 — 먼저 켜면 빈 상자를 보고 바로 채집하러 나가 환경 불량
            TestKit.ClearBaseStock(w, Foods);
            TestKit.ClearBaseStock(w, "Fish1", "FishingRod", "FishingBait");
            TestKit.PutInChest(w.Chest, "FishingRod", 1);
            TestKit.PutInChest(w.Chest, "FishingBait", 20);
            float wl = ZoneSystem.instance.m_waterLevel;
            for (int i = 0; i < 4; i++)
            {
                Vector3 side = Vector3.Cross(Vector3.up, dir) * ((i - 1.5f) * 2f);
                Vector3 at = shore + dir * (2f + i % 2) + side;
                at.y = wl - 1f;
                made.Add(Object.Instantiate(fish, at, Quaternion.identity));
            }
            yield return new WaitForSeconds(1f);
            int fish0 = All(w, "Fish1"), bait0 = All(w, "FishingBait");
            TestRunner.Log($"집에서 물가 {Utils.DistanceXZ(w.Home, shore):0}m, 둘레 물고기 {new List<Fish>(FishJob.Loaded()).Count}, 물고기 {fish0}, 미끼 {bait0}");
            Departures.TestFishNow();
            float t0 = Time.time;
            yield return TestKit.WaitFor(() => Fishing(w), 70f);
            checks.Add($"음식 없음·먹거리 못 떠남 → 쉬던 일꾼이 낚시하러 감 ({Time.time - t0:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            WorkerNpc who = w.Ragnar.Ai.Job is FishJob ? w.Ragnar : w.Floki;
            var job = (FishJob)who.Ai.Job;
            yield return TestKit.WaitFor(() => !Fishing(w), 400f);
            checks.Add($"낚시 끝 ({TestKit.Waited:0}초, 낚은 수 {job.Got})", TestKit.Ok);
            int fish1 = All(w, "Fish1"), bait1 = All(w, "FishingBait");
            checks.Add($"물고기가 상자·짐에 들어옴 (농어 {fish0} → {fish1}, 낚은 수 {job.Got})", job.Got >= 1 && fish1 >= fish0 + job.Got);
            checks.Add($"낚은 만큼만 미끼를 씀 (미끼 {bait0} → {bait1})", bait1 == bait0 - job.Got);
            checks.Add($"낚싯대는 상자로 돌아옴 (상자 {GatherTest.CountBase(w.Home, "FishingRod")})", GatherTest.CountBase(w.Home, "FishingRod") == 1);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null && w.Floki.Ai.Job == null, 120f);
            yield return Cook(w, checks, made);
        }

        static bool Cooking(TestKit.World w) => w.Ragnar.Ai.Job is PlanJob a && a.Goal != null && a.Goal.Item == "FishCooked"
            || w.Floki.Ai.Job is PlanJob b && b.Goal != null && b.Goal.Item == "FishCooked";

        /// <summary>요청 나-2: 상자에 농어가 있고 화로·조리대(+손질 제작대)가 있으면 쉴 때 먹거리가 손질(FishRaw)·굽기(FishCooked)를 계획해 상자에 넣는다.</summary>
        static IEnumerator Cook(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            RecipeBook book = Recipes.Book();
            foreach (var r in book.RecipesFor("FishRaw"))
                TestRunner.Log($"FishRaw 제작법: {r.OutputAmount}개 ← {string.Join(", ", r.Inputs.ConvertAll(i => i.Item + " " + i.Amount).ToArray())} @ {r.Station ?? "손"}");
            foreach (var r in book.ProcessesFor("FishCooked")) TestRunner.Log($"FishCooked 가공: {r.Input} @ {r.Station}");
            var station = new List<GameObject>();
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            Vector3 at = GatherTest.Ground(w.Home + right * 6f);
            foreach (string pre in new[] { "fire_pit", "piece_cookingstation" }) station.Add(Object.Instantiate(ZNetScene.instance.GetPrefab(pre), at, Quaternion.identity));
            foreach (var r in book.RecipesFor("FishRaw"))
                if (r.Station != null)
                    foreach (GameObject go in ZNetScene.instance.m_prefabs)
                    {
                        CraftingStation cs = go != null ? go.GetComponent<CraftingStation>() : null;
                        if (cs == null || cs.m_name != r.Station) continue;
                        station.Add(Object.Instantiate(go, GatherTest.Ground(w.Home - right * 6f), Quaternion.identity));
                        break;
                    }
            made.AddRange(station);
            TestKit.ClearBaseStock(w, "FishRaw", "FishCooked", "Wood");
            TestKit.PutInChest(w.Chest, "Fish1", 6);
            TestKit.PutInChest(w.Chest, "Wood", 20);
            foreach (string f in Departures.FoodRank()) Departures.TestSkip(f, f != "FishCooked");
            yield return new WaitForSeconds(2f);
            TestKit.ClearBaseStock(w, Foods);
            int fish0 = All(w, "Fish1");
            float t0 = Time.time;
            yield return TestKit.WaitFor(() => Cooking(w), 70f);
            checks.Add($"상자에 농어·조리대 → 쉬던 일꾼이 물고기 구우러 감 ({Time.time - t0:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "FishCooked") >= 2, 400f);
            checks.Add($"구운 물고기가 상자에 들어옴 ({GatherTest.CountBase(w.Home, "FishCooked")}, {TestKit.Waited:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => !Cooking(w), 200f);
            checks.Add($"상자 농어를 씀 (농어 {fish0} → {All(w, "Fish1")})", All(w, "Fish1") < fish0);
            foreach (var g in station) if (g != null) ZNetScene.instance.Destroy(g);
        }
    }
}
