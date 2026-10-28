using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_ashlandstest (⑭ 애쉬랜드 1조각): 애쉬랜드 물건(불꽃금속 광석·그라우스텐·검은나무·탄 뼈 등)을 게임 데이터에서 어떻게 얻는지
    /// (캐기·줍기·사냥·밭)와 애쉬랜드 적(탄 자·모르겐·발키리·아스크스빈·볼처 등) 한 마리 위험 비율을 시험 일꾼 장비로 diag에 남긴다.
    /// 다음 조각(자리·용암 피하기·불 저항·광석 캐기·싸움)의 바탕 자료 — 판정은 프리팹이 있는지·데이터로 셀 수 있는지만, 나머지는 기록.
    /// </summary>
    static class AshlandsTest
    {
        public static void Register() => TestRunner.Register("vh_ashlandstest", "애쉬랜드 자료 시험", () => Run(), 1200f);

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

        static readonly string[] Items = { "FlametalOreNew", "FlametalNew", "Grausten", "Blackwood", "CharredBone", "MorgenSinew", "MorgenHeart",
            "AskHide", "AskBladder", "AsksvinMeat", "VoltureMeat", "VoltureEgg", "CelestialFeather", "CharcoalResin", "ProustitePowder",
            "SulfurStone", "MoltenCore", "Vineberry", "Fiddleheadfern", "BonemawSerpentTooth", "BonemawSerpentMeat" };
        static readonly string[] Foes = { "Charred_Melee", "Charred_Archer", "Charred_Twitcher", "Charred_Mage", "Morgen", "FallenValkyrie",
            "Asksvin", "Volture", "BlobLava", "BonemawSerpent" };

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
            checks.Add("불꽃금속 광석 프리팹 있음", ObjectDB.instance.GetItemPrefab("FlametalOreNew") != null);
            checks.Add("불꽃금속 광석을 얻는 길이 있음", ResourceCatalog.WaysFor("FlametalOreNew").Count > 0);
            checks.Add("그라우스텐을 얻는 길이 있음", ResourceCatalog.WaysFor("Grausten").Count > 0);

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
                TestRunner.Log($"{kind}: 체력 {f.Health:0}, 한 방 {f.Damage:0.0}(데이터 {data:0.0}), 날기 {c.m_flying}, 무리 {c.m_group}, " +
                    $"토박이 {Departures.NativeTo(kind, (int)Heightmap.Biome.AshLands)}, 위험 비율 {ratio:0.00}");
                if (kind == "Charred_Melee" || kind == "Morgen" || kind == "Asksvin")
                    checks.Add($"{kind} 한 방을 게임 데이터로 셈 (데이터 {data:0.0})", data > 0f);
            }
            checks.Add("애쉬랜드 적 프리팹(탄 전사·모르겐) 있음",
                ZNetScene.instance.GetPrefab("Charred_Melee") != null && ZNetScene.instance.GetPrefab("Morgen") != null);
            checks.Add("애쉬랜드 토박이 적에 탄 전사가 있음", Departures.NativeTo("Charred_Melee", (int)Heightmap.Biome.AshLands));
            if (TestKit.RunBiome != Heightmap.Biome.AshLands) yield break;
            Site(w, checks);
            yield return Gather(w, checks);
        }

        static readonly string[] GatherGear = { "ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded", "CapeLinen", "SwordSilver" };

        /// <summary>도구 종류·단계에 맞는 가장 낮은 도구 프리팹(없으면 null).</summary>
        static string ToolFor(ToolKind kind, int tier)
        {
            string[] names = kind == ToolKind.Axe
                ? new[] { "AxeStone", "AxeFlint", "AxeBronze", "AxeIron", "AxeBlackMetal", "AxeJotunBane" }
                : new[] { "PickaxeAntler", "PickaxeBronze", "PickaxeIron", "PickaxeBlackMetal" };
            foreach (string n in names)
                if ((ObjectDB.instance.GetItemPrefab(n)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_toolTier ?? -1) >= tier) return n;
            return null;
        }

        /// <summary>
        /// ⑭ 나) 애쉬랜드 자리 채집: 단계 맞는 곡괭이·도끼를 주고 "!Grausten 5"·"!Blackwood 5"를 지금 GatherJob 그대로 → 거점 상자에.
        /// 탄 해골 생성기(Spawner_CharredStone) 둘레 적은 시험 동안 거점·가장 가까운 자원 둘레에서 치운다(싸움은 바) 조각).
        /// </summary>
        static IEnumerator Gather(TestKit.World w, GatherTest.Checks checks)
        {
            if (w.Floki == null || w.Chest == null) yield break;
            var tools = new List<string>();
            var near = new List<Vector3>();
            foreach (string item in new[] { "Grausten", "Blackwood" })
            {
                int tier = 0;
                ToolKind kind = ToolKind.None;
                ZDO best = null;
                float bd = float.MaxValue;
                foreach (var z in Senses.Nearby(w.Home, 150f, z => ResourceCatalog.Get(z.GetPrefab())?.Produces.Contains(item) == true))
                {
                    ResourceInfo info = ResourceCatalog.Get(z.GetPrefab());
                    if (info.Tool == ToolKind.Hunt || info.Tool == ToolKind.None) continue;
                    kind = info.Tool;
                    tier = Mathf.Max(tier, info.MinToolTier);
                    float d = Utils.DistanceXZ(w.Home, z.GetPosition());
                    if (d < bd) { bd = d; best = z; }
                }
                string tool = kind == ToolKind.None ? null : ToolFor(kind, tier);
                TestRunner.Log($"{item}: {kind} 단계 {tier} → {tool ?? "없음"}, 가장 가까운 {(best == null ? "-" : bd.ToString("0") + "m")}");
                checks.Add($"{item}에 맞는 도구 있음 ({tool ?? "없음"})", tool != null);
                if (tool == null) yield break;
                tools.Add(tool);
                near.Add(best.GetPosition());
            }
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                foreach (var n in GatherGear) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                foreach (var t in tools) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(t), 1);
                Prep.WearBest(npc);
                var food = new Inventory("vh_test_food", null, 4, 2);
                foreach (var n in new[] { "CookedLoxMeat", "Sausages", "SerpentStew" }) food.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                npc.ResetMeals();
                npc.EatFrom(food);
                npc.Heal(1000f, false);
            }
            s_keep = true;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(w.Home, () => s_keep));
            foreach (var p in near) Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(p, () => s_keep));
            try
            {
                TestKit.ClearBaseStock(w, "Grausten", "Blackwood");
                int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
                GatherTest.Say("!Grausten 5", null);
                yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "Grausten") >= 5, 420f);
                checks.Add($"그라우스텐 5개가 상자에 ({GatherTest.CountBase(w.Home, "Grausten")}개, {TestKit.Waited:0}초)", TestKit.Ok);
                yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob), 60f);
                GatherTest.Say("!Blackwood 5", null);
                yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "Blackwood") >= 5, 420f);
                checks.Add($"검은나무 5개가 상자에 ({GatherTest.CountBase(w.Home, "Blackwood")}개, {TestKit.Waited:0}초)", TestKit.Ok);
                checks.Add("안 죽음", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
                TestRunner.Log($"자원 둘레 치운 생물 {TestKit.KeptClear}");
                TestKit.ClearBaseStock(w, "Grausten", "Blackwood");
            }
            finally { s_keep = false; }
        }

        static bool s_keep;

        /// <summary>VH_BIOME=ashlands: 자리가 애쉬랜드인지, 둘레 100m 용암 칸 비율(Heightmap.IsLava, 4m 간격)과 150m 안 자원을 diag에(나·다 조각 바탕).</summary>
        static void Site(TestKit.World w, GatherTest.Checks checks)
        {
            var biome = WorldGenerator.instance.GetBiome(w.Home);
            checks.Add($"시험 자리가 애쉬랜드 (바이옴 {biome})", biome == Heightmap.Biome.AshLands);
            int lava = 0, all = 0;
            for (float x = -100f; x <= 100f; x += 4f)
                for (float z = -100f; z <= 100f; z += 4f)
                {
                    Vector3 at = w.Home + new Vector3(x, 0f, z);
                    Heightmap hm = Heightmap.FindHeightmap(at);
                    if (hm == null) continue;
                    all++;
                    if (hm.IsLava(at)) lava++;
                }
            TestRunner.Log($"애쉬랜드 자리 100m 안 용암 칸 {lava}/{all}");
            var kinds = new Dictionary<string, int>();
            foreach (var z in Senses.Nearby(w.Home, 150f, z => ResourceCatalog.Get(z.GetPrefab()) != null))
                foreach (string item in ResourceCatalog.Get(z.GetPrefab()).Produces) { kinds.TryGetValue(item, out int n); kinds[item] = n + 1; }
            var parts = new List<string>();
            foreach (var kv in kinds) parts.Add(kv.Key + " " + kv.Value);
            TestRunner.Log($"애쉬랜드 자리 150m 안 자원: {string.Join(", ", parts.ToArray())}");
        }
    }
}
