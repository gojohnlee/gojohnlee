using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_mistlandstest (⑬ 안개땅 1조각): 안개땅 물건(검은 대리석·부드러운 조직·키틴·턱·수액·위습·버섯 등)을 게임 데이터에서 어떻게 얻는지
    /// (캐기·줍기·사냥·밭)와 안개땅 적(시커·시커 병정·진드기·걀·드베르그) 한 마리 위험 비율을 시험 일꾼 장비로 diag에 남긴다.
    /// 다음 조각(채굴·싸움·출발 판단·감염된 광산)의 바탕 자료 — 판정은 프리팹이 있는지·데이터로 셀 수 있는지만, 나머지는 기록.
    /// </summary>
    static class MistlandsTest
    {
        public static void Register() => TestRunner.Register("vh_mistlandstest", "안개땅 자료 시험", () => Run(), 1200f);

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

        static readonly string[] Items = { "BlackMarble", "Softtissue", "Carapace", "Mandible", "BlackCore", "Sap", "Wisp", "RoyalJelly",
            "GiantBloodSack", "MushroomJotunPuffs", "MushroomMagecap", "Iron", "HareMeat", "ChickenEgg", "BugMeat" };
        static readonly string[] Foes = { "Seeker", "SeekerBrute", "SeekerBrood", "Tick", "Gjall", "Dverger", "DvergerMage", "Hare", "Hen" };

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;

            foreach (string item in Items)
            {
                if (ObjectDB.instance.GetItemPrefab(item) == null) { TestRunner.Log($"{item}: 물건 프리팹 없음"); continue; }
                var ways = ResourceCatalog.WaysFor(item);
                Crop crop = Crops.ForProduce(item);
                TestRunner.Log($"{item}: 방법 [{string.Join(",", new List<ToolKind>(ways).ConvertAll(t => t.ToString()).ToArray())}], 사냥감 {Prey.Any(item)}, " +
                    $"출처 [{string.Join(", ", ResourceCatalog.SourcesOf(item).ToArray())}], 작물 {(crop != null ? crop.Sapling + " 바이옴 " + crop.Biomes : "없음")}");
            }
            checks.Add("검은 대리석을 얻는 길이 있음", ResourceCatalog.WaysFor("BlackMarble").Count > 0);
            checks.Add("부드러운 조직을 얻는 길이 있음", ResourceCatalog.WaysFor("Softtissue").Count > 0);
            checks.Add("키틴은 사냥(시커)으로", ResourceCatalog.WaysFor("Carapace").Contains(ToolKind.Hunt));

            float hp = w.Ragnar.GetMaxHealth(), hit = Guard.HitOf(w.Ragnar);
            var taken = Guard.TakenFor(w.Ragnar);
            TestRunner.Log($"시험 일꾼: 체력 {hp:0}, 한 방 {hit:0.0}, 갑옷 {w.Ragnar.GetBodyArmor():0}");
            foreach (string kind in Foes)
            {
                GameObject p = ZNetScene.instance.GetPrefab(kind);
                Character c = p != null ? p.GetComponent<Character>() : null;
                if (c == null) { TestRunner.Log($"{kind}: 프리팹 없음"); continue; }
                Foe f = Guard.Resolve(new GroupFoe { Kind = kind, Count = 1, Level = 1 });
                float ratio = Danger.Ratio(new List<Foe> { f }, hp, hit, taken);
                float data = Guard.DataHit(c);
                TestRunner.Log($"{kind}: 체력 {f.Health:0}, 한 방 {f.Damage:0.0}(데이터 {data:0.0}), 날기 {c.m_flying}, 무리 {c.m_group}, 위험 비율 {ratio:0.00}");
                if (kind == "Seeker" || kind == "SeekerBrute" || kind == "Tick" || kind == "Gjall")
                    checks.Add($"{kind} 한 방을 게임 데이터로 셈 (데이터 {data:0.0})", data > 0f);
            }
            checks.Add("안개땅 적 프리팹(시커·진드기·걀) 있음",
                ZNetScene.instance.GetPrefab("Seeker") != null && ZNetScene.instance.GetPrefab("Tick") != null && ZNetScene.instance.GetPrefab("Gjall") != null);
            checks.Add("안개땅 토박이 적에 시커가 있음", Departures.NativeTo("Seeker", (int)Heightmap.Biome.Mistlands));
            LogPost("blackmarble_post01");
            if (TestKit.RunBiome != Heightmap.Biome.Mistlands) yield break;
            // 회차 239: 5.9km 옮긴 직후엔 둘레 구역이 덜 생겨 150m 안 유해 0개일 때가 있음 → 보일 때까지(30초)
            yield return TestKit.WaitFor(() => Senses.Nearby(w.Home, 150f, z => z.GetPrefab() != 0 && (ResourceCatalog.Get(z.GetPrefab())?.Prefab.StartsWith("giant_") ?? false)).Count > 0, 30f);
            TestRunner.Log($"거인 유해 기다림 {TestKit.Waited:0}초");
            Vector3? giant = Site(w, checks);
            if (giant != null) yield return Mine(w, checks, giant.Value);
        }

        /// <summary>
        /// ⑬ 나) 안개땅 자리(VH_BIOME=mistlands): TestKit이 찾은 자리가 안개땅이고 둘레에 거인 유해가 있는지,
        /// 시야(Senses.Look — ZDO 거리 40m라 안개와 무관)로 그 유해의 검은 대리석·부드러운 조직이 지식에 적히는지.
        /// </summary>
        static Vector3? Site(TestKit.World w, GatherTest.Checks checks)
        {
            var biome = WorldGenerator.instance.GetBiome(w.Home);
            checks.Add($"시험 자리가 안개땅 (바이옴 {biome})", biome == Heightmap.Biome.Mistlands);
            ZDO giant = null;
            float best = float.MaxValue;
            int count = 0;
            var kinds = new Dictionary<string, int>();
            foreach (var z in Senses.Nearby(w.Home, 150f, z => ResourceCatalog.Get(z.GetPrefab()) != null))
            {
                ResourceInfo info = ResourceCatalog.Get(z.GetPrefab());
                foreach (string item in info.Produces) { kinds.TryGetValue(item, out int n); kinds[item] = n + 1; }
                if (!info.Produces.Contains("BlackMarble") && !info.Produces.Contains("Softtissue")) continue;
                if (!info.Prefab.StartsWith("giant_")) continue; // 드베르그 기둥(blackmarble_post01)은 유해가 아님(E042)
                count++;
                float d = Utils.DistanceXZ(w.Home, z.GetPosition());
                if (d < best) { best = d; giant = z; }
            }
            var parts = new List<string>();
            foreach (var kv in kinds) parts.Add(kv.Key + " " + kv.Value);
            TestRunner.Log($"안개땅 자리 150m 안 자원: {string.Join(", ", parts.ToArray())}");
            checks.Add($"150m 안에 거인 유해 ({count}개, 가장 가까운 {(giant == null ? "-" : best.ToString("0") + "m")})", giant != null);
            if (giant == null) return null;
            Vector3 g = giant.GetPosition();
            Vector3 eye = g + (w.Home - g).normalized * 30f; // 유해에서 30m 떨어진 곳(시야 40m 안)
            Senses.Look(eye);
            var mem = Brain.Get().Get(Senses.ZoneOf(g));
            bool known = mem != null && (mem.Items.ContainsKey("BlackMarble") || mem.Items.ContainsKey("Softtissue"));
            checks.Add("안개 속 30m에서 본 거인 유해가 지식에 적힘", known);
            checks.Add("가 본 곳 바이옴에 안개땅이 적힘", Brain.Get().KnowsBiome((int)Heightmap.Biome.Mistlands));
            return g;
        }

        /// <summary>E042: 드베르그 기둥이 깨질 때 무엇을 어디에 떨구는지(부품·떨굼 표·깨진 뒤 생기는 것)를 diag에.</summary>
        static void LogPost(string name)
        {
            GameObject p = ZNetScene.instance.GetPrefab(name);
            if (p == null) { TestRunner.Log($"{name}: 프리팹 없음"); return; }
            var comps = new List<string>();
            foreach (var c in p.GetComponents<Component>()) comps.Add(c.GetType().Name);
            var des = p.GetComponent<Destructible>();
            var drop = p.GetComponent<DropOnDestroyed>();
            var items = new List<string>();
            if (drop?.m_dropWhenDestroyed?.m_drops != null)
                foreach (var d in drop.m_dropWhenDestroyed.m_drops) items.Add($"{d.m_item?.name} {d.m_stackMin}-{d.m_stackMax}");
            TestRunner.Log($"{name}: 부품 {string.Join(",", comps.ToArray())} · 단계 {des?.m_minToolTier} · 떨굼 [{string.Join(", ", items.ToArray())}] " +
                $"(번 {drop?.m_dropWhenDestroyed?.m_dropMin}-{drop?.m_dropWhenDestroyed?.m_dropMax}, 확률 {drop?.m_dropWhenDestroyed?.m_dropChance}) · 깨진 뒤 {des?.m_spawnWhenDestroyed?.name ?? "-"}");
        }

        static readonly string[] MineGear = { "ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded", "CapeLinen", "SwordSilver" };

        /// <summary>
        /// ⑬ 다) 거인 유해 채굴: 단계 맞는 곡괭이·평원 장비로 "!BlackMarble 5"·"!Softtissue 3" → 거인 유해(giant_ribs·skull·brain)를 캐고
        /// 깨진 조각(_frac)의 떨굼을 주워 거점 상자에. 시험 동안 거점과 가장 가까운 유해 둘레 생물은 치운다(싸움은 라) 조각).
        /// </summary>
        static IEnumerator Mine(TestKit.World w, GatherTest.Checks checks, Vector3 giant)
        {
            int tier = 0;
            foreach (var z in Senses.Nearby(w.Home, 150f, z => ResourceCatalog.Get(z.GetPrefab()) != null))
            {
                ResourceInfo info = ResourceCatalog.Get(z.GetPrefab());
                if (info.Produces.Contains("BlackMarble") || info.Produces.Contains("Softtissue")) tier = Mathf.Max(tier, info.MinToolTier);
            }
            string pick = null;
            foreach (string n in new[] { "PickaxeAntler", "PickaxeBronze", "PickaxeIron", "PickaxeBlackMetal" })
                if (pick == null && (ObjectDB.instance.GetItemPrefab(n)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_toolTier ?? -1) >= tier) pick = n;
            TestRunner.Log($"거인 유해 곡괭이 단계 {tier} → {pick ?? "없음"}");
            checks.Add("거인 유해에 맞는 곡괭이 있음", pick != null);
            if (pick == null || w.Floki == null || w.Chest == null) yield break;
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                foreach (var n in MineGear) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                for (int i = 0; i < 2; i++) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(pick), 1);
                Prep.WearBest(npc);
                var food = new Inventory("vh_test_food", null, 4, 2);
                foreach (var n in new[] { "CookedLoxMeat", "Sausages", "SerpentStew" }) food.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                npc.ResetMeals();
                npc.EatFrom(food);
                npc.Heal(1000f, false);
            }
            s_keep = true;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(w.Home, () => s_keep));
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(giant, () => s_keep));
            try
            {
                TestKit.ClearBaseStock(w, "BlackMarble", "Softtissue");
                int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
                GatherTest.Say("!BlackMarble 5", null);
                yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "BlackMarble") >= 5, 420f);
                checks.Add($"검은 대리석 5개가 상자에 ({GatherTest.CountBase(w.Home, "BlackMarble")}개, {TestKit.Waited:0}초)", TestKit.Ok);
                yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob), 60f);
                GatherTest.Say("!Softtissue 3", null);
                yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "Softtissue") >= 3, 420f);
                checks.Add($"부드러운 조직 3개가 상자에 ({GatherTest.CountBase(w.Home, "Softtissue")}개, {TestKit.Waited:0}초)", TestKit.Ok);
                checks.Add("안 죽음", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
                TestRunner.Log($"유해 둘레 치운 생물 {TestKit.KeptClear}");
                TestKit.ClearBaseStock(w, "BlackMarble", "Softtissue");
            }
            finally { s_keep = false; }
        }

        static bool s_keep;
    }
}
