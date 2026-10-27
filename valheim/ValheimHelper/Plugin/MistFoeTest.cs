using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_mistfoetest (⑬ 안개땅 라): 안개땅 적 판단·싸움.
    /// A) 안개땅 차림(키틴 갑옷·미스트워커·음식 셋)으로 시커 하나 → 판단이 위험 비율대로인지, 싸움이면 잡고 안 죽는지, 시커를 배우는지.
    /// B) 같은 차림으로 진드기 하나(체력 50, 위험 낮음) → 싸워서 잡음.
    /// D) 같은 차림으로 출발 판단(⑬ 라-2): 검은 대리석을 아는 안개땅 두 곳 중 가까운 곳에 시커 셋(물러난 기록) → 그곳은 피하고 먼 곳으로
    ///    (안개땅 전체가 장비 모자람이면 호위병과), 시커 있는 곳만 남으면 호위병과 같이(GoTogether). 판단만 보고 걷지는 않음.
    /// C) 약한 차림(몽둥이·갑옷 없음·음식 없음)으로 시커 하나 → 싸우지 않고 물러나거나 도망(결과·죽음은 기록).
    /// 평평한 시험 자리(안개땅 자리는 무거워 vh_mistlandstest만).
    /// </summary>
    static class MistFoeTest
    {
        public static void Register() => TestRunner.Register("vh_mistfoetest", "안개땅 적 싸움 시험", () => TestKit.Flat(Run()));

        static readonly string[] MistGear = { "ArmorCarapaceChest", "ArmorCarapaceLegs", "HelmetCarapace", "SwordMistwalker" };
        static readonly string[] MistFood = { "MisthareSupreme", "MeatPlatter", "HoneyGlazedChicken" };

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
            w.CountsCreatures = true;
            var spawned = new List<GameObject>();
            try { yield return Body(w, checks, spawned); }
            finally
            {
                Cleanup(spawned);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;
            GameObject seeker = ZNetScene.instance.GetPrefab("Seeker"), tick = ZNetScene.instance.GetPrefab("Tick");
            checks.Add("시커·진드기 프리팹", seeker != null && tick != null);
            if (seeker == null || tick == null) yield break;
            var npc = w.Ragnar;
            bool gearOk = true;
            foreach (var n in MistGear) gearOk &= Give(npc, n);
            foreach (var n in MistFood) gearOk &= Give(npc, n);
            checks.Add("안개땅 차림·음식 프리팹", gearOk);
            Prep.WearBest(npc);
            npc.ResetMeals();
            var ate = npc.EatFrom(npc.GetInventory());
            checks.Add($"안개땅 음식 먹음 ({string.Join(",", ate.ToArray())})", ate.Count == 3);
            npc.Heal(npc.GetMaxHealth(), false);
            yield return new WaitForSeconds(1f);

            // A) 시커 하나: 판단은 위험 비율대로, 싸움이면 잡음
            float ratio = RatioOf(npc, "Seeker");
            Verdict expect = Danger.Judge(Personality.Ragnar, new List<Foe> { Guard.Resolve(new GroupFoe { Kind = "Seeker", Count = 1, Level = 1 }) },
                npc.GetMaxHealth(), npc.GetMaxHealth(), Guard.HitOf(npc), Guard.TakenFor(npc), false);
            TestRunner.Log($"A 안개땅 차림: 체력 {npc.GetMaxHealth():0}, 갑옷 {npc.GetBodyArmor():0}, 한 방 {Guard.HitOf(npc):0.0}, 시커 위험 비율 {ratio:0.00} → 예상 {expect}");
            var states = new HashSet<string>();
            yield return Watch("A", npc, Spawn(seeker, npc, 15f, spawned), 45f, states, r => checks.Add(r.Item1, r.Item2));
            if (expect == Verdict.Fight)
                checks.Add($"안개땅 차림이면 시커와 싸움 (상태 {Join(states)})", states.Contains("Fight"));
            else
                checks.Add($"비율 {ratio:0.00}이면 시커와 싸우지 않음 (상태 {Join(states)})", !states.Contains("Fight") && states.Count > 0);
            if (states.Contains("Fight")) checks.Add("싸운 시커를 배움", Brain.Foes.Knows("Seeker"));
            if (npc.IsDead()) yield break;

            // B) 진드기 하나: 싸워서 잡음
            npc.Heal(npc.GetMaxHealth(), false);
            yield return new WaitForSeconds(1f);
            TestRunner.Log($"B 진드기 위험 비율 {RatioOf(npc, "Tick"):0.00}");
            states.Clear();
            yield return Watch("B", npc, Spawn(tick, npc, 12f, spawned), 40f, states, r => checks.Add(r.Item1, r.Item2));
            checks.Add($"진드기와 싸움 (상태 {Join(states)})", states.Contains("Fight"));
            if (npc.IsDead()) yield break;

            // D) 출발 판단: 시커를 아는 안개땅 자리는 피하고, 거기뿐이면 호위병과
            yield return Departs(w, checks, npc);

            // C) 약한 차림 + 시커 → 싸우지 않음
            foreach (var it in new List<ItemDrop.ItemData>(npc.GetInventory().GetAllItems())) { npc.UnequipItem(it, false); npc.GetInventory().RemoveItem(it); }
            npc.ResetMeals();
            Give(npc, "Club");
            Prep.WearBest(npc);
            npc.Heal(npc.GetMaxHealth(), false);
            yield return new WaitForSeconds(1f);
            ratio = RatioOf(npc, "Seeker");
            TestRunner.Log($"C 약한 차림: 체력 {npc.GetMaxHealth():0}(음식 없음), 갑옷 {npc.GetBodyArmor():0}, 시커 위험 비율 {ratio:0.00}");
            states.Clear();
            int deaths0 = npc.Deaths;
            Character c = Spawn(seeker, npc, 20f, spawned);
            float t = 0f;
            while (t < 20f && npc.Deaths == deaths0 && c != null && !c.IsDead())
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                if (npc.Ai.Guard != null && npc.Ai.Guard.StateName != "Calm") states.Add(npc.Ai.Guard.StateName);
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[C {t:0}s] 경계 {npc.Ai.Guard?.StateName}/{npc.Ai.Guard?.LastVerdict} 비율 {npc.Ai.Guard?.LastRatio:0.00}, 체력 {npc.GetHealth():0}, 시커 거리 {(c ? Utils.DistanceXZ(c.transform.position, npc.transform.position) : 0):0.0}m");
            }
            TestRunner.Log($"C 결과: 상태 [{Join(states)}], 죽음 {npc.Deaths - deaths0}, {t:0.0}초");
            checks.Add($"약한 차림은 시커와 싸우지 않고 피함 (비율 {ratio:0.00}, 상태 {Join(states)})", ratio > Danger.FightRagnar && !states.Contains("Fight") && states.Count > 0);
            if (c != null && !c.IsDead()) Kill(c);
            yield return new WaitForSeconds(2f);
        }

        static IEnumerator Departs(TestKit.World w, GatherTest.Checks checks, WorkerNpc npc)
        {
            yield return TestKit.SpawnGuard();
            checks.Add($"호위병 생김 ({TestKit.Waited:0}초)", TestKit.Ok && WorkerNpc.TheGuard != null);
            Give(npc, "PickaxeBlackMetal");
            string k0 = Brain.Get().Save(), p0 = Brain.Places.Save();
            try
            {
                ZoneXY hz = Senses.ZoneOf(w.Home), near = new ZoneXY(hz.X + 3, hz.Y), far = new ZoneXY(hz.X - 5, hz.Y);
                var marble = new Core.Chat.ChatCommand { Kind = Core.Chat.CommandKind.Item, Item = "BlackMarble", Amount = 5 };
                Brain.ReplaceForTest("", "");
                double now = ZNet.instance.GetTimeSeconds();
                foreach (var z in new[] { near, far })
                {
                    Brain.Get().Saw(new Dictionary<ZoneXY, Dictionary<string, int>> { [z] = new Dictionary<string, int> { ["BlackMarble"] = 5 } }, new List<ZoneXY> { z }, now);
                    Brain.Get().SetBiome(z, (int)Heightmap.Biome.Mistlands);
                }
                Brain.Places.Record(near, new List<GroupFoe> { new GroupFoe { Kind = "Seeker", Count = 3 } }, Outcome.Back, now, Guard.Resolve);
                DepartureResult r = Departures.JudgeFor(npc, marble);
                TestRunner.Log($"D 두 곳: {r?.Action} 곳 {Z(r?.Place)} 대신 {Z(r?.Alternative)} 피함 {r?.Avoid.Count} — {r?.Reason}");
                // 시커를 이긴 적이 없으면 안개땅 전체가 장비 모자람(BiomeRisk)이라 먼 곳도 위험 → 호위병과 먼 곳으로
                bool avoided = r != null && (r.Action == DepartAction.Go || r.Action == DepartAction.GoTogether) && Same(r.Place, far) && r.Avoid.Exists(o => o.Zone.Equals(near));
                checks.Add($"시커 셋 있던 가까운 안개땅은 피하고 먼 곳으로 ({r?.Action}, {Z(r?.Place)})", avoided);

                Brain.Get().Forget(far, "BlackMarble");
                r = Departures.JudgeFor(npc, marble);
                TestRunner.Log($"D 시커 곳만: {r?.Action} 곳 {Z(r?.Place)} — {r?.Reason}");
                checks.Add($"시커 있는 곳뿐이면 묻지 않고 호위병과 ({r?.Action})", r != null && r.Action == DepartAction.GoTogether && r.Question == Question.None);
            }
            finally
            {
                Brain.ReplaceForTest(k0, p0);
                Workers.SkipGuard = true; // C는 호위병 없이(곁에 있으면 경계가 그 힘을 셈)
                Workers.DespawnGuards();
            }
        }

        static bool Same(PlaceOption p, ZoneXY z) => p != null && p.Zone.Equals(z);
        static string Z(PlaceOption p) => p == null ? "-" : $"({p.Zone.X},{p.Zone.Y}) {p.Distance:0}m 비율 {p.Ratio:0.00} 장비 {p.GearRatio:0.00}";

        /// <summary>적 하나가 죽거나 일꾼이 죽거나 limit초까지 지켜보며 경계 상태를 모은다. 싸웠으면 잡음·안 죽음을 칸으로.</summary>
        static IEnumerator Watch(string tag, WorkerNpc npc, Character a, float limit, HashSet<string> states, System.Action<System.Tuple<string, bool>> add)
        {
            int deaths0 = npc.Deaths;
            float t = 0f, low = npc.GetHealth();
            while (t < limit && a != null && !a.IsDead() && npc.Deaths == deaths0)
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                if (npc.Ai.Guard != null && npc.Ai.Guard.StateName != "Calm") states.Add(npc.Ai.Guard.StateName);
                low = Mathf.Min(low, npc.GetHealth());
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[{tag} {t:0}s] 경계 {npc.Ai.Guard?.StateName}/{npc.Ai.Guard?.LastVerdict} 비율 {npc.Ai.Guard?.LastRatio:0.00} ({npc.Ai.Guard?.LastDetail}), 체력 {npc.GetHealth():0}, " +
                    $"적 {(a ? a.GetHealth() : 0):0} 거리 {(a ? Utils.DistanceXZ(a.transform.position, npc.transform.position) : 0):0.0}m");
            }
            TestRunner.Log($"{tag} 결과: 상태 [{Join(states)}], 죽음 {npc.Deaths - deaths0}, 가장 낮은 체력 {low:0}/{npc.GetMaxHealth():0}, {t:0.0}초");
            if (states.Contains("Fight"))
            {
                add(System.Tuple.Create($"{tag}: 싸워서 잡음 ({t:0.0}초)", a == null || a.IsDead()));
                add(System.Tuple.Create($"{tag}: 안 죽음 (가장 낮은 체력 {low:0})", npc.Deaths == deaths0 && !npc.IsDead()));
            }
            if (a != null && !a.IsDead()) Kill(a);
            yield return new WaitForSeconds(2f);
        }

        static float RatioOf(WorkerNpc npc, string kind) =>
            Danger.Ratio(new List<Foe> { Guard.Resolve(new GroupFoe { Kind = kind, Count = 1, Level = 1 }) }, npc.GetMaxHealth(), Guard.HitOf(npc), Guard.TakenFor(npc));

        static bool Give(WorkerNpc npc, string name)
        {
            GameObject p = ObjectDB.instance.GetItemPrefab(name);
            if (p == null) { TestRunner.Log($"물건 프리팹 없음: {name}"); return false; }
            npc.GetInventory().AddItem(p, 1);
            return true;
        }

        static string Join(HashSet<string> s) => string.Join(",", new List<string>(s).ToArray());

        static void Cleanup(List<GameObject> spawned)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
        }

        static Character Spawn(GameObject prefab, WorkerNpc npc, float dist, List<GameObject> spawned)
        {
            GameObject go = Object.Instantiate(prefab, GatherTest.Ground(npc.transform.position + npc.transform.forward * dist) + Vector3.up * 1f, Quaternion.identity);
            spawned.Add(go);
            Character ch = go.GetComponent<Character>();
            ch.SetLevel(1);
            return ch;
        }

        static void Kill(Character c)
        {
            var hit = new HitData { m_point = c.GetCenterPoint() };
            hit.m_damage.m_damage = 99999f;
            c.Damage(hit);
        }
    }
}
