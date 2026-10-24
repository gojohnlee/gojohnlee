using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_plainstest (⑫ 평원 1조각): 평원 물건(흑금속 고철·타르·보리·아마·록스 고기·클라우드베리·바늘)을 게임 데이터에서 어떻게 얻는지
    /// (캐기·줍기·사냥·밭)와 평원 적(풀링·데스킷·록스·성장체) 한 마리 위험 비율을 시험 일꾼 장비로 diag에 남긴다.
    /// 다음 조각(평원 출발 판단·싸움·타르 웅덩이)의 바탕 자료 — 판정은 데이터 사실만, 위험 수치는 기록.
    /// </summary>
    static class PlainsTest
    {
        public static void Register() => TestRunner.Register("vh_plainstest", "평원 자료 시험", () => Run(), 120f);

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

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;

            foreach (string item in new[] { "BlackMetalScrap", "Tar", "Barley", "Flax", "LoxMeat", "Cloudberry", "Needle" })
            {
                var ways = ResourceCatalog.WaysFor(item);
                Crop crop = Crops.ForProduce(item);
                TestRunner.Log($"{item}: 방법 [{string.Join(",", new List<ToolKind>(ways).ConvertAll(t => t.ToString()).ToArray())}], 사냥감 {Prey.Any(item)}, " +
                    $"출처 [{string.Join(", ", ResourceCatalog.SourcesOf(item).ToArray())}], 작물 {(crop != null ? crop.Sapling + " 바이옴 " + crop.Biomes : "없음")}");
            }
            checks.Add("흑금속 고철은 사냥(풀링)으로", ResourceCatalog.WaysFor("BlackMetalScrap").Contains(ToolKind.Hunt));
            checks.Add("타르를 얻는 길이 있음", ResourceCatalog.WaysFor("Tar").Count > 0);
            checks.Add("록스 고기는 사냥으로", ResourceCatalog.WaysFor("LoxMeat").Contains(ToolKind.Hunt));
            checks.Add("클라우드베리는 맨손 줍기", ResourceCatalog.WaysFor("Cloudberry").Contains(ToolKind.None));
            var barley = ResourceCatalog.WaysFor("Barley");
            checks.Add("야생 보리는 맨손 줍기(곡괭이 아님)", barley.Contains(ToolKind.None) && !barley.Contains(ToolKind.Pickaxe));
            foreach (string item in new[] { "Barley", "Flax" })
            {
                Crop c = Crops.ForProduce(item);
                bool plains = c != null && Farm.CanGrow(c, (int)Heightmap.Biome.Plains);
                bool meadows = c != null && Farm.CanGrow(c, (int)Heightmap.Biome.Meadows);
                checks.Add($"{item}은 평원 밭에서만 자람 (평원 {plains}, 초원 {meadows})", plains && !meadows);
            }

            // 위험: 한 마리(별 없음)를 시험 일꾼 지금 장비로 — 다음 조각에서 출발 판단 기준을 정할 자료
            float hp = w.Ragnar.GetMaxHealth(), hit = Guard.HitOf(w.Ragnar);
            var taken = Guard.TakenFor(w.Ragnar);
            TestRunner.Log($"시험 일꾼: 체력 {hp:0}, 한 방 {hit:0.0}, 갑옷 {w.Ragnar.GetBodyArmor():0}");
            foreach (string kind in new[] { "Goblin", "GoblinBrute", "GoblinShaman", "Deathsquito", "Lox", "BlobTar", "Boar" })
            {
                GameObject p = ZNetScene.instance.GetPrefab(kind);
                Character c = p != null ? p.GetComponent<Character>() : null;
                if (c == null) { TestRunner.Log($"{kind}: 프리팹 없음"); continue; }
                Foe f = Guard.Resolve(new GroupFoe { Kind = kind, Count = 1, Level = 1 });
                float ratio = Danger.Ratio(new List<Foe> { f }, hp, hit, taken);
                TestRunner.Log($"{kind}: 체력 {f.Health:0}, 한 방 {f.Damage:0.0}, 날기 {c.m_flying}, 위험 비율 {ratio:0.00}");
            }
            checks.Add("평원 적 프리팹(풀링·데스킷·록스) 있음",
                ZNetScene.instance.GetPrefab("Goblin") != null && ZNetScene.instance.GetPrefab("Deathsquito") != null && ZNetScene.instance.GetPrefab("Lox") != null);
        }
    }
}
