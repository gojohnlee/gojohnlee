using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_fulingtest (⑫ 평원 마): 흑금속 고철 = 풀링 사냥(CharacterDrop).
    /// A) 평원 차림 + 풀링 하나(고철 떨굼을 이 한 마리만 100%로) → "!BlackMetalScrap 1"로 잡고, 떨군 고철을 주워 거점 상자에.
    /// B) 풀링 둘 + 큰 풀링 + 풀링 주술사를 한데 50m 앞에 → 혼자면 될 풀링이라도 무리가 세고 곁의 동료와 둘이도 안 되니(Guard.SafePrey pack)
    ///    안 덤비고 "더 좋은 장비를 줘"(preyPack)로 그만둠(이 칸은 호위병 없이).
    /// C) 호위병을 멀리 두고 혼자면 안 되고 둘이면 되는 무리 → 라그나가 호위병을 불러(preyMate, 사용자 결정 2026-10-26) 같이 감.
    /// 평원 자리는 VH_BIOME=plains.
    /// </summary>
    static class FulingTest
    {
        public static void Register() => TestRunner.Register("vh_fulingtest", "풀링 사냥(흑금속 고철) 시험", () => TestKit.Flat(Run()));

        internal static readonly string[] PlainsGear = { "ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake", "CapeWolf", "SwordIron" };
        const string Scrap = "BlackMetalScrap";

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
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            GameObject goblin = ZNetScene.instance.GetPrefab("Goblin"), brute = ZNetScene.instance.GetPrefab("GoblinBrute"), shaman = ZNetScene.instance.GetPrefab("GoblinShaman");
            checks.Add("풀링 프리팹", goblin != null && brute != null && shaman != null);
            if (goblin == null || brute == null || shaman == null) yield break;
            var ways = ResourceCatalog.WaysFor(Scrap);
            checks.Add("고철은 사냥으로 얻음(Hunt)", ways.Contains(ToolKind.Hunt));
            checks.Add("풀링은 고철 사냥감", Prey.Is(Scrap, goblin.GetComponent<Character>()));
            TestKit.ClearBaseStock(w, Scrap);
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                foreach (var n in PlainsGear) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                Prep.WearBest(npc);
                // 평원 갈 때 먹는 음식(없으면 체력 70쯤이라 풀링 하나도 물러남 — 회차 173 첫 실행)
                var food = new Inventory("vh_test_food", null, 4, 2);
                foreach (var n in new[] { "CookedLoxMeat", "Sausages", "SerpentStew" }) food.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                npc.ResetMeals();
                npc.EatFrom(food);
                npc.Heal(1000f, false);
            }
            TestRunner.Log($"차림: 체력 {w.Ragnar.GetMaxHealth():0}, 갑옷 {w.Ragnar.GetBodyArmor():0}");
            yield return new WaitForSeconds(1f);
            Quaternion rot = w.Player.transform.rotation;

            // A) 풀링 하나 → 잡고 고철을 상자에
            // 45m: 일꾼 경계(30m)보다 멀고 사냥 시야(60m) 안 → 일(Hunt)이 먼저 보고 쫓아감
            Character a = Spawn(goblin, w.Home + rot * new Vector3(0f, 0f, 45f), spawned);
            foreach (var d in a.GetComponent<CharacterDrop>().m_drops)
                if (d.m_prefab != null && d.m_prefab.name == Scrap) { d.m_chance = 1f; d.m_amountMin = Mathf.Max(1, d.m_amountMin); d.m_levelMultiplier = false; }
            bool alone = w.Ragnar.Ai.Guard.SafePrey(a, out Character pack0);
            checks.Add($"풀링 하나는 혼자 잡을 만함 (무리 {(pack0 ? pack0.name : "없음")})", alone);
            int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
            GatherTest.Say($"!{Scrap} 1", null);
            yield return TestKit.WaitFor(() => a == null || a.IsDead(), 120f);
            checks.Add($"풀링 잡음 ({TestKit.Waited:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, Scrap) >= 1, 120f);
            checks.Add($"떨군 고철을 주워 상자에 ({GatherTest.CountBase(w.Home, Scrap)}개, {TestKit.Waited:0}초)", TestKit.Ok);
            checks.Add($"안 죽음 (라그나 {w.Ragnar.GetHealth():0}, 플로키 {w.Floki.GetHealth():0})", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob), 60f);
            TestKit.ClearBaseStock(w, Scrap);
            if (a != null && !a.IsDead()) Kill(a);

            // B) 풀링 마을 무리 → 안 덤비고 도움 요청
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                npc.Ai.Interrupt();
                npc.Ai.Queue.Clear();
                var it = npc.GetInventory().GetItem(ObjectDB.instance.GetItemPrefab(Scrap).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
                if (it != null) npc.GetInventory().RemoveItem(it);
                npc.TeleportTo(w.Home + rot * new Vector3(npc == w.Ragnar ? 2f : -2f, 0f, 0f), rot, false);
            }
            foreach (var npc in new[] { w.Ragnar, w.Floki }) npc.Heal(1000f, false);
            yield return new WaitForSeconds(2f);
            Vector3 camp = w.Home + rot * new Vector3(0f, 0f, 50f);
            var band = new List<Character>
            {
                Spawn(goblin, camp + rot * new Vector3(-2f, 0f, 0f), spawned),
                Spawn(goblin, camp + rot * new Vector3(2f, 0f, 0f), spawned),
                Spawn(brute, camp + rot * new Vector3(0f, 0f, 3f), spawned),
                Spawn(shaman, camp + rot * new Vector3(0f, 0f, -3f), spawned),
            };
            yield return new WaitForSeconds(0.5f);
            bool soloOk = w.Ragnar.Ai.Guard.SafePrey(band[0], out Character pack);
            TestRunner.Log($"B 판단: 무리 속 풀링 {(pack != null ? $"무리 때문에 안 됨({Utils.GetPrefabName(pack.gameObject)})" : soloOk ? "됨" : "혼자도 안 됨")}");
            checks.Add($"무리 속 풀링은 무리 때문에 안 됨 (가장 센 {(pack ? Utils.GetPrefabName(pack.gameObject) : "없음")})", pack != null);
            int heard = w.Heard.Count, alive0 = band.FindAll(c => c != null && !c.IsDead()).Count;
            float low = Mathf.Min(w.Ragnar.GetHealth(), w.Floki.GetHealth());
            var states = new HashSet<string>();
            GatherTest.Say($"!{Scrap} 1", null);
            float t = 0f;
            bool called = false;
            while (t < 60f && (t < 2f || w.Ragnar.Ai.Job is GatherJob || w.Floki.Ai.Job is GatherJob))
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                foreach (var npc in new[] { w.Ragnar, w.Floki })
                    if (npc.Ai.Guard != null && npc.Ai.Guard.StateName != "Calm") states.Add(npc.Ai.Guard.StateName);
                low = Mathf.Min(low, w.Ragnar.GetHealth(), w.Floki.GetHealth());
                called |= w.Ragnar.Ai.EscortName != null || w.Floki.Ai.EscortName != null;
            }
            bool said = false;
            for (int i = heard; i < w.Heard.Count; i++) { TestRunner.Log($"들음: {w.Heard[i]}"); if (w.Heard[i].Contains("장비") || w.Heard[i].Contains("갑옷")) said = true; }
            checks.Add($"무리면 그만둠 ({t:0.0}초)", !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob));
            checks.Add("둘이도 안 되면 동료 안 부름", !called);
            checks.Add("장비를 달라고 말함(preyPack)", said);
            checks.Add($"무리에 덤비지 않음 (경계 [{string.Join(",", new List<string>(states).ToArray())}], 남은 풀링 {band.FindAll(c => c != null && !c.IsDead()).Count}/{alive0}, 가장 낮은 체력 {low:0})", !states.Contains("Fight"));
            foreach (var c in band) if (c != null && !c.IsDead()) Kill(c);
            TestKit.ClearBaseStock(w, Scrap);
            yield return new WaitForSeconds(2f);
            yield return MateCall(w, checks, spawned, goblin, brute, shaman, rot, camp);
        }

        /// <summary>
        /// C) 혼자면 안 되고 호위병과 둘이면 되는 무리(후보를 차례로 놓아 보고 고름 — 차림·난이도마다 수치가 달라서): 호위병을 만들어 플로키·호위병을 60m 뒤에 두고
        /// "!라그나 고철 1" → 라그나가 호위병을 부르고(동행, 사용자 10-26 — 일꾼끼리는 안 부름) 호위병이 따라옴, 아무도 안 죽음. 그런 무리가 없으면 로그만(판정 안 함).
        /// </summary>
        static IEnumerator MateCall(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned, GameObject goblin, GameObject brute, GameObject shaman, Quaternion rot, Vector3 camp)
        {
            yield return TestKit.SpawnGuard();
            WorkerNpc guard = WorkerNpc.TheGuard;
            checks.Add($"C 호위병 생김 ({guard?.NpcName ?? "없음"})", guard != null);
            if (guard == null) yield break;
            foreach (var n in PlainsGear) guard.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
            Prep.WearBest(guard);
            foreach (var npc in new[] { w.Ragnar, w.Floki, guard }) { npc.Ai.Interrupt(); npc.Ai.Queue.Clear(); npc.Heal(1000f, false); }
            w.Ragnar.TeleportTo(w.Home + rot * new Vector3(2f, 0f, 0f), rot, false);
            w.Floki.TeleportTo(w.Home + rot * new Vector3(-30f, 0f, -60f), rot, false);
            guard.TeleportTo(w.Home + rot * new Vector3(0f, 0f, -60f), rot, false);
            yield return new WaitForSeconds(2f);
            var bands = new[] { new[] { goblin, goblin }, new[] { goblin, goblin, goblin }, new[] { goblin, goblin, brute }, new[] { goblin, goblin, shaman } };
            List<Character> band = null;
            foreach (var kinds in bands)
            {
                var b = new List<Character>();
                for (int i = 0; i < kinds.Length; i++) b.Add(Spawn(kinds[i], camp + rot * new Vector3(i * 2f - 2f, 0f, 0f), spawned));
                yield return new WaitForSeconds(0.5f);
                bool solo = w.Ragnar.Ai.Guard.SafePrey(b[0], out _);
                bool duo = w.Ragnar.Ai.Guard.SafePrey(b[0], out _, Guard.HitOf(guard));
                TestRunner.Log($"C 후보 {kinds.Length}마리({Utils.GetPrefabName(kinds[kinds.Length - 1])}): 혼자 {solo}, 호위병과 둘이 {duo}");
                if (!solo && duo) { band = b; break; }
                foreach (var c in b) if (c != null && !c.IsDead()) Kill(c);
                yield return new WaitForSeconds(1f);
            }
            if (band == null) { TestRunner.Log("C: 혼자면 안 되고 둘이면 되는 무리가 이 차림엔 없음 → 판정 안 함"); yield break; }
            int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths + guard.Deaths;
            GatherTest.Say($"!{w.Ragnar.NpcName} {Scrap} 1", null);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.EscortName == guard.NpcName && guard.Ai.Current == WorkerAI.Mode.Follow
                && guard.Ai.FollowTarget == w.Ragnar.GetZDOID(), 60f);
            checks.Add($"둘이면 되는 무리 → 호위병을 불러 같이 감 ({TestKit.Waited:0}초, 라그나 동행 {w.Ragnar.Ai.EscortName ?? "없음"}, 호위병 {guard.Ai.Current}, 플로키 {w.Floki.Ai.Current})",
                TestKit.Ok && w.Floki.Ai.Current != WorkerAI.Mode.Follow);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.EscortPhase == Core.Npc.Escort.Phase.Leading, 60f);
            checks.Add($"호위병이 곁에 오면 앞장섬 ({TestKit.Waited:0}초, {w.Ragnar.Ai.EscortPhase})", TestKit.Ok);
            yield return TestKit.WaitFor(() => band.TrueForAll(c => c == null || c.IsDead()) || !(w.Ragnar.Ai.Job is GatherJob), 120f);
            TestRunner.Log($"C 끝: 남은 풀링 {band.FindAll(c => c != null && !c.IsDead()).Count}/{band.Count}, 라그나 {w.Ragnar.Ai.Current}·{w.Ragnar.GetHealth():0}, 호위병 {guard.Ai.Current}·{guard.GetHealth():0}");
            checks.Add($"둘이 싸워 안 죽음 (라그나 {w.Ragnar.GetHealth():0}, 호위병 {guard.GetHealth():0})", w.Ragnar.Deaths + w.Floki.Deaths + guard.Deaths == deaths0);
            foreach (var c in band) if (c != null && !c.IsDead()) Kill(c);
            GatherTest.Say("!멈춰", null);
            TestKit.ClearBaseStock(w, Scrap);
            yield return new WaitForSeconds(2f);
        }

        internal static void Cleanup(List<GameObject> spawned)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
        }

        internal static Character Spawn(GameObject prefab, Vector3 pos, List<GameObject> spawned)
        {
            GameObject go = Object.Instantiate(prefab, GatherTest.Ground(pos) + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Add(go);
            Character ch = go.GetComponent<Character>();
            ch.SetLevel(1);
            return ch;
        }

        internal static void Kill(Character c)
        {
            var hit = new HitData { m_point = c.GetCenterPoint() };
            hit.m_damage.m_damage = 99999f;
            c.Damage(hit);
        }
    }
}
