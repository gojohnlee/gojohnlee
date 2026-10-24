using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
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
        public static void Register() => TestRunner.Register("vh_plainstest", "평원 자료·출발 판단 시험", () => Run(), 150f);

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
                float data = Guard.DataHit(c);
                TestRunner.Log($"{kind}: 체력 {f.Health:0}, 한 방 {f.Damage:0.0}(데이터 {data:0.0}, 배움 {Brain.Foes.Knows(kind)}), 날기 {c.m_flying}, 위험 비율 {ratio:0.00}");
                // 회차 170: 안 배운 적 한 방은 몸집 짐작 대신 게임 데이터(공격 물건) — 평원 적은 모두 데이터가 있어야 한다
                if (kind != "Boar") checks.Add($"{kind} 한 방을 게임 데이터로 셈 (데이터 {data:0.0})", data > 0f);
            }
            float sting = Guard.DataHit(ZNetScene.instance.GetPrefab("Deathsquito")?.GetComponent<Character>());
            checks.Add($"데스킷 한 방이 몸집 짐작(4.7)보다 훨씬 셈 (데이터 {sting:0.0})", sting > 20f);
            checks.Add("평원 적 프리팹(풀링·데스킷·록스) 있음",
                ZNetScene.instance.GetPrefab("Goblin") != null && ZNetScene.instance.GetPrefab("Deathsquito") != null && ZNetScene.instance.GetPrefab("Lox") != null);

            // ⑫ 다: 평원에서 데스킷을 겪은 뒤 약한 장비로 평원 물건을 시키면 "이 장비론 평원 무리" → 장비 상자에 갑옷을 넣으면 스스로 출발
            yield return GearJudge(w, checks);
        }

        static readonly string[] WolfSet = { "ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake" };

        static IEnumerator GearJudge(TestKit.World w, GatherTest.Checks checks)
        {
            string k0 = Brain.Get().Save(), p0 = Brain.Places.Save();
            Departures.TestAutoGo = false;
            try
            {
                ZoneXY hz = Senses.ZoneOf(w.Home);
                var far = new ZoneXY(hz.X + 3, hz.Y);
                var fought = new ZoneXY(hz.X + 3, hz.Y + 2);
                double now = ZNet.instance.GetTimeSeconds();
                Brain.ReplaceForTest("", "");
                Brain.Get().Saw(new Dictionary<ZoneXY, Dictionary<string, int>> { [far] = new Dictionary<string, int> { ["Cloudberry"] = 5 } }, new List<ZoneXY> { far }, now);
                Brain.Get().SetBiome(far, (int)Heightmap.Biome.Plains);
                Brain.Get().SetBiome(fought, (int)Heightmap.Biome.Plains);
                TestRunner.Log($"거점 상자에서 갑옷 {TestKit.ClearBaseStock(w, WolfSet)}개 비움");
                var cmd = new ChatCommand { Kind = CommandKind.Item, Item = "Cloudberry", Amount = 10 };

                DepartureResult before = Departures.JudgeFor(w.Ragnar, cmd);
                TestRunner.Log($"데스킷 겪기 전: {before?.Action} {before?.Question} ({before?.Reason})");
                checks.Add("평원 적을 겪기 전엔 장비 질문 없음(맨땅에 헤딩)", before != null && before.Condition != "gear");

                Brain.Places.Record(fought, new List<GroupFoe> { new GroupFoe { Kind = "Deathsquito", Count = 2 } }, Outcome.Fled, now, Guard.Resolve);
                DepartureResult weak = Departures.JudgeFor(w.Ragnar, cmd);
                TestRunner.Log($"데스킷 겪은 뒤(갑옷 {w.Ragnar.GetBodyArmor():0}): {weak?.Action} {weak?.Question} {weak?.Condition} ({weak?.Reason})");
                checks.Add("약한 장비 → 평원 장비 조건 질문", weak != null && weak.Question == Question.Condition && weak.Condition == "gear");

                int heard0 = w.Heard.Count;
                Departures.Gather(new Speaker(Player.m_localPlayer.GetPlayerName(), ZDOID.None, ZDOID.None), cmd, w.Ragnar);
                yield return new WaitForSeconds(1f);
                string n1 = w.Ragnar.NpcName;
                bool said = w.Heard.FindIndex(heard0, h => h.StartsWith(n1 + ":") && h.Contains("평원")) >= 0;
                checks.Add($"\"평원\" 장비 말하고 기다림 (말 {said})", Departures.AskedAt(w.Ragnar) >= 0 && said && w.Ragnar.Ai.Current != WorkerAI.Mode.Work);

                foreach (string a in WolfSet) TestKit.PutInChest(w.Chest, a, 1);
                DepartureResult good = Departures.JudgeFor(w.Ragnar, cmd);
                TestRunner.Log($"상자에 늑대 갑옷: {good?.Action} {good?.Question} {good?.Condition} ({good?.Reason})");
                yield return TestKit.WaitFor(() => w.Ragnar.Ai.Current == WorkerAI.Mode.Work, 8f);
                checks.Add($"장비 상자에 갑옷 → 스스로 출발 ({TestKit.Waited:0.0}초)", TestKit.Ok && Departures.AskedAt(w.Ragnar) < 0);
                GatherTest.Say("!멈춰", null);
                yield return new WaitForSeconds(1f);
            }
            finally
            {
                Departures.TestAutoGo = true;
                Departures.Forget(w.Ragnar);
                Brain.ReplaceForTest(k0, p0);
                TestKit.ClearBaseStock(w, WolfSet);
            }
        }
    }
}
