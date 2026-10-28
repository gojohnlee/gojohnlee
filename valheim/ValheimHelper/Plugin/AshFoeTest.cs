using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_ashfoetest (⑭ 애쉬랜드 바): 애쉬랜드 적 판단·싸움.
    /// A) 가장 좋은 차림(제작법 있는 투구·갑옷·바지 중 방어 최고, 한손 칼 중 피해 최고, 음식 체력 셋)으로 탄 전사·아스크스빈·볼처(날기)·모르겐 하나씩
    ///    → 판단이 위험 비율대로인지(싸움이면 잡고 안 죽는지, 아니면 안 싸우는지).
    /// D) 출발 판단: 그라우스텐을 아는 애쉬랜드 두 곳 중 가까운 곳에 탄 전사 셋(물러난 기록) → 그곳은 피하고 먼 곳으로, 그곳뿐이면 묻지 않고 호위병과.
    /// C) 약한 차림(몽둥이·음식 없음)으로 탄 전사 하나 → 싸우지 않고 피함.
    /// 평평한 시험 자리(애쉬랜드 자리는 vh_ashlandstest만).
    /// </summary>
    static class AshFoeTest
    {
        public static void Register() => TestRunner.Register("vh_ashfoetest", "애쉬랜드 적 싸움 시험", () => TestKit.Flat(Run()));

        static readonly string[] Foes = { "Charred_Melee", "Asksvin", "Volture", "Morgen" };
        const float Borderline = 1.25f;

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
            var prefabs = Foes.Select(n => ZNetScene.instance.GetPrefab(n)).ToList();
            checks.Add("애쉬랜드 적 프리팹", prefabs.All(p => p != null));
            if (prefabs.Any(p => p == null)) yield break;
            var npc = w.Ragnar;
            var best = BestGear();
            TestRunner.Log($"가장 좋은 차림: {string.Join(",", best.ToArray())}");
            bool gearOk = best.Count == 7;
            foreach (var n in best) gearOk &= Give(npc, n);
            checks.Add("좋은 차림·음식 셋 프리팹", gearOk);
            Prep.WearBest(npc);
            npc.ResetMeals();
            var ate = npc.EatFrom(npc.GetInventory());
            checks.Add($"음식 먹음 ({string.Join(",", ate.ToArray())})", ate.Count == 3);

            // A) 적 하나씩: 판단은 위험 비율대로, 싸움이면 잡음
            var states = new HashSet<string>();
            for (int i = 0; i < Foes.Length; i++)
            {
                string kind = Foes[i];
                npc.Heal(npc.GetMaxHealth(), false);
                yield return new WaitForSeconds(1f);
                float ratio = RatioOf(npc, kind);
                Verdict expect = Danger.Judge(Personality.Ragnar, new List<Foe> { Guard.Resolve(new GroupFoe { Kind = kind, Count = 1, Level = 1 }) },
                    npc.GetMaxHealth(), npc.GetMaxHealth(), Guard.HitOf(npc), Guard.TakenFor(npc), false);
                TestRunner.Log($"A {kind}: 체력 {npc.GetMaxHealth():0}, 갑옷 {npc.GetBodyArmor():0}, 한 방 {Guard.HitOf(npc):0.0}, 위험 비율 {ratio:0.00} → 예상 {expect}");
                states.Clear();
                yield return Watch("A " + kind, kind, npc, Spawn(prefabs[i], npc, 15f, spawned), 45f, states, r => checks.Add(r.Item1, r.Item2));
                if (expect == Verdict.Fight)
                    checks.Add($"{kind}: 비율 {ratio:0.00}이면 싸움 (상태 {Join(states)})", states.Contains("Fight"));
                else if (ratio <= Borderline) // 경계선: 맞붙은 순간 적 체력이 줄면 비율이 1 밑으로 → 싸움도 맞음(아스크스빈 1.19 → 0.99, 회차 231). 싸우면 Watch가 잡음·안 죽음을 봄
                    checks.Add($"{kind}: 비율 {ratio:0.00}(경계선)이면 싸우든 물러나든 반응 (상태 {Join(states)})", states.Count > 0);
                else
                    checks.Add($"{kind}: 비율 {ratio:0.00}이면 싸우지 않음 (상태 {Join(states)})", !states.Contains("Fight") && states.Count > 0);
                if (npc.IsDead()) yield break;
            }

            // D) 출발 판단: 탄 전사를 아는 애쉬랜드 자리는 피하고, 거기뿐이면 호위병과
            yield return Departs(w, checks, npc);

            // C) 약한 차림 + 탄 전사 → 싸우지 않음
            foreach (var it in new List<ItemDrop.ItemData>(npc.GetInventory().GetAllItems())) { npc.UnequipItem(it, false); npc.GetInventory().RemoveItem(it); }
            npc.ResetMeals();
            Give(npc, "Club");
            Prep.WearBest(npc);
            npc.Heal(npc.GetMaxHealth(), false);
            yield return new WaitForSeconds(1f);
            float weak = RatioOf(npc, "Charred_Melee");
            TestRunner.Log($"C 약한 차림: 체력 {npc.GetMaxHealth():0}(음식 없음), 갑옷 {npc.GetBodyArmor():0}, 탄 전사 위험 비율 {weak:0.00}");
            states.Clear();
            int deaths0 = npc.Deaths;
            Character c = Spawn(prefabs[0], npc, 20f, spawned);
            float t = 0f;
            while (t < 20f && npc.Deaths == deaths0 && c != null && !c.IsDead())
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                if (npc.Ai.Guard != null && npc.Ai.Guard.StateName != "Calm") states.Add(npc.Ai.Guard.StateName);
            }
            TestRunner.Log($"C 결과: 상태 [{Join(states)}], 죽음 {npc.Deaths - deaths0}, {t:0.0}초");
            checks.Add($"약한 차림은 탄 전사와 싸우지 않고 피함 (비율 {weak:0.00}, 상태 {Join(states)})", weak > Danger.FightRagnar && !states.Contains("Fight") && states.Count > 0);
            if (c != null && !c.IsDead()) Kill(c);
            yield return new WaitForSeconds(2f);
        }

        /// <summary>애쉬랜드 차림: 불꽃금속(FlametalNew)으로 만드는 투구·갑옷·바지 중 방어 최고·한손 칼 중 피해 최고,
        /// 음식은 애쉬랜드 재료(아스크스빈·볼처 떨굼, 바인베리·고사리 줍기)가 든 것 중 체력 높은 셋. (제작법 있는 것 전체로 고르면 딥노스 물건이 섞임 — 회차 231)</summary>
        static List<string> BestGear()
        {
            var ash = new HashSet<string>();
            foreach (var n in new[] { "Asksvin", "Volture", "BonemawSerpent" })
                if (ZNetScene.instance.GetPrefab(n)?.GetComponent<CharacterDrop>() is CharacterDrop cd)
                    foreach (var d in cd.m_drops) if (d.m_prefab != null) ash.Add(d.m_prefab.name);
            foreach (var n in new[] { "VineAsh", "Pickable_Fiddlehead" })
                if (ZNetScene.instance.GetPrefab(n)?.GetComponent<Pickable>() is Pickable p && p.m_itemPrefab != null) ash.Add(p.m_itemPrefab.name);
            var recipes = ObjectDB.instance.m_recipes.Where(rc => rc.m_item != null && rc.m_enabled).ToList();
            bool Uses(Recipe rc, HashSet<string> names) => rc.m_resources.Any(q => q.m_resItem != null && names.Contains(q.m_resItem.name));
            var flametal = new HashSet<string> { "FlametalNew" };
            var gear = recipes.Where(rc => Uses(rc, flametal)).Select(rc => rc.m_item).ToList();
            var r = new List<string>();
            foreach (var type in new[] { ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs })
            {
                var top = gear.Where(d => d.m_itemData.m_shared.m_itemType == type).OrderByDescending(d => d.m_itemData.m_shared.m_armor).FirstOrDefault();
                if (top != null) r.Add(top.name);
            }
            var sword = gear.Where(d => d.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon && d.m_itemData.m_shared.m_skillType == Skills.SkillType.Swords)
                .OrderByDescending(d => d.m_itemData.m_shared.m_damages.GetTotalDamage()).FirstOrDefault();
            if (sword != null) r.Add(sword.name);
            TestRunner.Log($"애쉬랜드 재료: {string.Join(",", ash.ToArray())}");
            r.AddRange(recipes.Where(rc => Uses(rc, ash)).Select(rc => rc.m_item)
                .Where(d => d.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && d.m_itemData.m_shared.m_food > 0f && d.m_itemData.m_shared.m_foodStamina > 0f)
                .OrderByDescending(d => d.m_itemData.m_shared.m_food).Select(d => d.name).Distinct().Take(3));
            return r;
        }

        static IEnumerator Departs(TestKit.World w, GatherTest.Checks checks, WorkerNpc npc)
        {
            yield return TestKit.SpawnGuard();
            checks.Add($"호위병 생김 ({TestKit.Waited:0}초)", TestKit.Ok && WorkerNpc.TheGuard != null);
            string k0 = Brain.Get().Save(), p0 = Brain.Places.Save();
            try
            {
                ZoneXY hz = Senses.ZoneOf(w.Home), near = new ZoneXY(hz.X + 3, hz.Y), far = new ZoneXY(hz.X - 5, hz.Y);
                var stone = new Core.Chat.ChatCommand { Kind = Core.Chat.CommandKind.Item, Item = "Grausten", Amount = 5 };
                Brain.ReplaceForTest("", "");
                double now = ZNet.instance.GetTimeSeconds();
                foreach (var z in new[] { near, far })
                {
                    Brain.Get().Saw(new Dictionary<ZoneXY, Dictionary<string, int>> { [z] = new Dictionary<string, int> { ["Grausten"] = 5 } }, new List<ZoneXY> { z }, now);
                    Brain.Get().SetBiome(z, (int)Heightmap.Biome.AshLands);
                }
                Brain.Places.Record(near, new List<GroupFoe> { new GroupFoe { Kind = "Charred_Melee", Count = 3 } }, Outcome.Back, now, Guard.Resolve);
                DepartureResult r = Departures.JudgeFor(npc, stone);
                TestRunner.Log($"D 두 곳: {r?.Action} 곳 {Z(r?.Place)} 대신 {Z(r?.Alternative)} 피함 {r?.Avoid.Count} — {r?.Reason}");
                bool avoided = r != null && (r.Action == DepartAction.Go || r.Action == DepartAction.GoTogether) && Same(r.Place, far) && r.Avoid.Exists(o => o.Zone.Equals(near));
                checks.Add($"탄 전사 셋 있던 가까운 애쉬랜드는 피하고 먼 곳으로 ({r?.Action}, {Z(r?.Place)})", avoided);

                Brain.Get().Forget(far, "Grausten");
                r = Departures.JudgeFor(npc, stone);
                TestRunner.Log($"D 탄 전사 곳만: {r?.Action} 곳 {Z(r?.Place)} — {r?.Reason}");
                checks.Add($"탄 전사 있는 곳뿐이면 묻지 않고 호위병과 ({r?.Action})", r != null && r.Action == DepartAction.GoTogether && r.Question == Question.None);
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

        /// <summary>적 하나가 죽거나 일꾼이 죽거나 limit초까지 지켜보며 그 적을 보는 동안의 경계 상태를 모은다(근처 멧돼지와 싸운 것은 안 셈 — 회차 231). 싸웠으면 잡음·안 죽음을 칸으로.</summary>
        static IEnumerator Watch(string tag, string kind, WorkerNpc npc, Character a, float limit, HashSet<string> states, System.Action<System.Tuple<string, bool>> add)
        {
            int deaths0 = npc.Deaths;
            float t = 0f, low = npc.GetHealth();
            while (t < limit && a != null && !a.IsDead() && npc.Deaths == deaths0)
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                if (npc.Ai.Guard != null && npc.Ai.Guard.StateName != "Calm" && (npc.Ai.Guard.LastDetail ?? "").Contains("[" + kind + " ")) states.Add(npc.Ai.Guard.StateName);
                low = Mathf.Min(low, npc.GetHealth());
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[{tag} {t:0}s] 경계 {npc.Ai.Guard?.StateName}/{npc.Ai.Guard?.LastVerdict} 비율 {npc.Ai.Guard?.LastRatio:0.00} ({npc.Ai.Guard?.LastDetail}), 체력 {npc.GetHealth():0}, " +
                    $"적 {(a ? a.GetHealth() : 0):0} 거리 {(a ? Utils.DistanceXZ(a.transform.position, npc.transform.position) : 0):0.0}m 높이 {(a ? a.transform.position.y - npc.transform.position.y : 0):0.0}m");
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
            // 모르겐은 잠든 채로 생김(MonsterAI.m_sleeping) — 잠든 적은 경계가 안 셈. 판단을 보려고 깨움(MonsterAI.Wakeup은 private).
            if (go.GetComponent<MonsterAI>() is MonsterAI ai && ai.m_sleeping)
            {
                TestRunner.Log($"{prefab.name} 잠든 채 생김 → 깨움");
                HarmonyLib.AccessTools.Method(typeof(MonsterAI), "Wakeup").Invoke(ai, null);
            }
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
