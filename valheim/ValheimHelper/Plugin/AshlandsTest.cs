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
        }

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
