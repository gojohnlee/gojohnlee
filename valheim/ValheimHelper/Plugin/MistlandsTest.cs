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
        public static void Register() => TestRunner.Register("vh_mistlandstest", "안개땅 자료 시험", () => Run(), 90f);

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
            if (TestKit.RunBiome == Heightmap.Biome.Mistlands) Site(w, checks);
        }

        /// <summary>
        /// ⑬ 나) 안개땅 자리(VH_BIOME=mistlands): TestKit이 찾은 자리가 안개땅이고 둘레에 거인 유해가 있는지,
        /// 시야(Senses.Look — ZDO 거리 40m라 안개와 무관)로 그 유해의 검은 대리석·부드러운 조직이 지식에 적히는지.
        /// </summary>
        static void Site(TestKit.World w, GatherTest.Checks checks)
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
                count++;
                float d = Utils.DistanceXZ(w.Home, z.GetPosition());
                if (d < best) { best = d; giant = z; }
            }
            var parts = new List<string>();
            foreach (var kv in kinds) parts.Add(kv.Key + " " + kv.Value);
            TestRunner.Log($"안개땅 자리 150m 안 자원: {string.Join(", ", parts.ToArray())}");
            checks.Add($"150m 안에 거인 유해 ({count}개, 가장 가까운 {(giant == null ? "-" : best.ToString("0") + "m")})", giant != null);
            if (giant == null) return;
            Vector3 g = giant.GetPosition();
            Vector3 eye = g + (w.Home - g).normalized * 30f; // 유해에서 30m 떨어진 곳(시야 40m 안)
            Senses.Look(eye);
            var mem = Brain.Get().Get(Senses.ZoneOf(g));
            bool known = mem != null && (mem.Items.ContainsKey("BlackMarble") || mem.Items.ContainsKey("Softtissue"));
            checks.Add("안개 속 30m에서 본 거인 유해가 지식에 적힘", known);
            checks.Add("가 본 곳 바이옴에 안개땅이 적힘", Brain.Get().KnowsBiome((int)Heightmap.Biome.Mistlands));
        }
    }
}
