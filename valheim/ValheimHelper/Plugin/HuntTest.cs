using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_hunttest (⑧ 사냥): "!RawMeat 2"(사냥만 되는 물건) → 거점 둘레 시야에 멧돼지 둘을 세워 두면 GatherJob 사냥 모드가
    /// 쫓아가 잡고, 떨어진 고기를 주워 거점 상자에 넣는지. 사냥감 목록(Prey)이 멧돼지→고기를 아는지도 본다.
    /// </summary>
    static class HuntTest
    {
        public static void Register() => TestRunner.Register("vh_hunttest", "사냥(멧돼지 → 고기) 시험", () => TestKit.Flat(Run()));

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
            var boars = new List<GameObject>();
            try { yield return Body(w, checks, boars); }
            finally
            {
                foreach (var b in boars) if (b != null) ZNetScene.instance.Destroy(b);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        /// <summary>사냥·솎기 대상 쓰러진 몸(Ragdoll) m_ttl = 떨굼이 나오기까지(LootWatch, 사용자 11-11 "재서 맞추기").</summary>
        static void LogRagdollTtl()
        {
            var parts = new List<string>();
            foreach (string name in new[] { "Boar", "Deer", "Neck", "Hen", "Wolf", "Lox", "Asksvin", "Hare", "Greyling", "Troll" })
            {
                Character c = ZNetScene.instance.GetPrefab(name)?.GetComponent<Character>();
                if (c == null) continue;
                float ttl = -1f;
                foreach (var e in c.m_deathEffects.m_effectPrefabs)
                    if (e.m_prefab != null && e.m_prefab.TryGetComponent(out Ragdoll r)) ttl = r.m_ttl;
                parts.Add($"{name} {ttl:0.0}");
            }
            Plugin.Log.LogInfo("DIAG 몸 m_ttl(초): " + string.Join(", ", parts));
        }

        /// <summary>바닐라 휴식(Rested) 값 — 일꾼 체력 회복에 닿는지(사용자 11-12 23:58). 체력 회복은 SE_Stats.m_healthRegenMultiplier만 닿는다(Player.UpdateFood).</summary>
        static void LogRested()
        {
            var se = ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Stats;
            Plugin.Log.LogInfo(se == null ? "DIAG Rested: SE_Stats 아님/없음"
                : $"DIAG Rested: 체력 회복 ×{se.m_healthRegenMultiplier:0.##}, 기력 회복 ×{se.m_staminaRegenMultiplier:0.##}, 에이트르 ×{se.m_eitrRegenMultiplier:0.##}, 기술 +{se.m_raiseSkillModifier:0.##}, 기본 {((SE_Rested)se).m_baseTTL:0}초");
        }

        /// <summary>지금 사냥 단계인 일꾼(명령은 손 빈 쪽이 맡음).</summary>
        static WorkerNpc Hunter(TestKit.World w)
        {
            foreach (var n in new[] { w.Ragnar, w.Floki })
                if (n != null && n.Ai.Job is GatherJob g && g.StepName == "Hunt") return n;
            return null;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> boars)
        {
            w.CountsCreatures = true; // 생물을 부르거나 세는 시험
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            GameObject prefab = ZNetScene.instance.GetPrefab("Boar");
            checks.Add("멧돼지 프리팹", prefab != null);
            LogRagdollTtl();
            LogRested();
            var ways = ResourceCatalog.WaysFor("RawMeat");
            checks.Add("고기는 사냥으로만 얻음(Hunt)", ways.Count == 1 && ways.Contains(ToolKind.Hunt));
            checks.Add("멧돼지는 사냥감", prefab != null && Prey.Is("RawMeat", prefab.GetComponent<Character>()));
            if (prefab == null) yield break;

            TestKit.ClearBaseStock(w, "RawMeat");
            Quaternion rot = w.Player.transform.rotation;
            for (int i = 0; i < 2; i++)
            {
                Vector3 pos = GatherTest.Ground(w.Home + rot * new Vector3(i * 6f - 3f, 0f, 25f + i * 6f));
                GameObject go = Object.Instantiate(prefab, pos + Vector3.up * 0.5f, Quaternion.identity);
                go.GetComponent<Character>().SetLevel(1);
                boars.Add(go);
            }
            GatherTest.Say("!RawMeat 2", null);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is GatherJob g && (g.StepName == "Hunt" || g.StepName == "Collect"), 90f);
            checks.Add($"사냥 시작 ({TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "RawMeat") >= 2, 150f);
            checks.Add($"고기 2개 이상이 상자에 ({GatherTest.CountBase(w.Home, "RawMeat")}개, {TestKit.Waited:0}초)", TestKit.Ok);
            int dead = 0;
            foreach (var b in boars) if (b == null || b.GetComponent<Character>().IsDead()) dead++;
            checks.Add($"멧돼지 죽음 ({dead}/2)", dead >= 1);
            checks.Add($"체력 남음 ({w.Ragnar.GetHealth():0})", w.Ragnar.GetHealth() > 0f);
            TestKit.ClearBaseStock(w, "RawMeat");

            // 다쳐도 일을 접지 않음(요청 다침 ③): 사냥 중 체력 40% 밑 → 돌아와 "다침" 칸 → 체력이 차기 전엔 안 나감 → 차면 이어 가 채움
            WorkerNpc h = null;
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 60f);
            w.Ragnar.ClearHurt(); w.Floki?.ClearHurt();
            Vector3 hp = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 50f));
            GameObject hb = Object.Instantiate(prefab, hp + Vector3.up * 0.5f, Quaternion.identity);
            hb.GetComponent<Character>().SetLevel(1);
            boars.Add(hb);
            GatherTest.Say("!RawMeat 1", null);
            yield return TestKit.WaitFor(() => (h = Hunter(w)) != null, 150f);
            checks.Add($"다침 시험 사냥 시작 ({h?.NpcName}, {TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            h.SetHealth(h.GetMaxHealth() * 0.3f);
            yield return TestKit.WaitFor(() => (h.Ai.Job == null || h.Ai.Job is RecoverNearJob) && h.Ai.Queue.Count > 0 && h.Ai.Queue[0].Why == "다침", 90f); // 둘레에 버섯 등이 있으면 근처 회복
            checks.Add($"다쳐서 돌아와 '다침' 칸에 남김 ({TestKit.Waited:0}초, 이곳 {h.HurtSpots.CountAt(h.transform.position.x, h.transform.position.z)}번)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            h.SetHealth(h.GetMaxHealth() * 0.5f);
            yield return new WaitForSeconds(6f);
            checks.Add("체력이 차기 전엔 안 나감", !(h.Ai.Job is GatherJob) && h.Ai.Queue.Count > 0 && h.Ai.Queue[0].Why == "다침");
            h.SetHealth(h.GetMaxHealth());
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "RawMeat") >= 1, 150f);
            checks.Add($"체력이 차면 이어 가 고기 채움 ({GatherTest.CountBase(w.Home, "RawMeat")}개, {TestKit.Waited:0}초)", TestKit.Ok);
            TestKit.ClearBaseStock(w, "RawMeat");

            // 근처 채집 대기(요청 다침 ③, Recovery.Nearby): 둘레에 적 없는 버섯이 있으면 집에 안 가고 주우며 회복 → 차면 이어 감
            GameObject shroom = ZNetScene.instance.GetPrefab("Pickable_Mushroom");
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null && (w.Floki == null || w.Floki.Ai.Job == null), 60f);
            Vector3 np = GatherTest.Ground(w.Home + rot * new Vector3(-12f, 0f, 55f)), pm = GatherTest.Ground(w.Home + rot * new Vector3(-7f, 0f, 33f)); // 멧돼지는 멀리, 버섯은 가는 길 중간(싸우는 중엔 다침을 안 봄)
            GameObject nb = Object.Instantiate(prefab, np + Vector3.up * 0.5f, Quaternion.identity);
            nb.GetComponent<Character>().SetLevel(1);
            boars.Add(nb);
            if (shroom != null)
                foreach (var off in new[] { new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, -4f), new Vector3(0f, 0f, -12f), new Vector3(-4f, 0f, -20f) })
                    Object.Instantiate(shroom, GatherTest.Ground(pm + rot * off), Quaternion.identity);
            GatherTest.Say("!RawMeat 1", null);
            h = null;
            yield return TestKit.WaitFor(() => (h = Hunter(w)) != null, 150f);
            if (h != null) yield return TestKit.WaitFor(() => Utils.DistanceXZ(h.transform.position, h.Home) > 32f || !(h.Ai.Job is GatherJob), 60f); // 버섯 둘레(집 밖 30m 넘게, 멧돼지 닿기 전)에서 다쳐야 근처 회복
            checks.Add($"근처 회복 시험 사냥 시작 ({h?.NpcName}, {TestKit.Waited:0}초, 집에서 {(h != null ? Utils.DistanceXZ(h.transform.position, h.Home) : 0f):0}m, 멧돼지까지 {(h != null && nb != null ? Utils.DistanceXZ(h.transform.position, nb.transform.position) : -1f):0}m)", h != null && nb != null && Utils.DistanceXZ(h.transform.position, h.Home) > 30f && Utils.DistanceXZ(h.transform.position, nb.transform.position) < 30f);
            if (!TestKit.Ok) yield break;
            if (nb != null) nb.transform.position = GatherTest.Ground(w.Home + rot * new Vector3(-12f, 0f, 90f)) + Vector3.up * 0.5f; // 사냥감이 25m 안이면 적 곁이라 근처 회복 안 함(맞는 동작) — 멀리 옮겨 안전한 경우를 봄
            h.SetHealth(h.GetMaxHealth() * 0.3f);
            yield return TestKit.WaitFor(() => h.Ai.Job is RecoverNearJob, 90f);
            checks.Add($"다쳐도 집에 안 가고 근처에서 회복 ({TestKit.Waited:0}초, 집에서 {Utils.DistanceXZ(h.transform.position, h.Home):0}m)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            h.SetHealth(h.GetMaxHealth() * 0.6f);
            yield return TestKit.WaitFor(() => h.Ai.Job is RecoverNearJob r && r.Picked >= 1, 60f);
            checks.Add($"회복하며 버섯 주움 ({TestKit.Waited:0}초, 주운 {(h.Ai.Job as RecoverNearJob)?.Picked}번)", TestKit.Ok);
            h.SetHealth(h.GetMaxHealth());
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "RawMeat") >= 1, 150f);
            checks.Add($"근처 회복 뒤 이어 가 고기 채움 ({GatherTest.CountBase(w.Home, "RawMeat")}개, {TestKit.Waited:0}초)", TestKit.Ok);
            TestKit.ClearBaseStock(w, "RawMeat");

            // 싸움 끼는 다른 모으기(요청 다침 ③): 사냥 말고 모으는 중 크게 다쳐도 접지 않고 "다침" 칸 → 체력이 차면 이어 감
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob) && (w.Floki == null || !(w.Floki.Ai.Job is GatherJob)), 60f);
            w.Ragnar.ClearHurt(); w.Floki?.ClearHurt();
            TestKit.ClearBaseStock(w, "Mushroom");
            if (shroom != null)
                for (int i = 0; i < 8; i++)
                    Object.Instantiate(shroom, GatherTest.Ground(w.Home + rot * new Vector3(-10f + i * 3f, 0f, -24f - (i % 2) * 3f)), Quaternion.identity);
            GatherTest.Say("!Mushroom 6", null);
            WorkerNpc g = null;
            yield return TestKit.WaitFor(() => (g = Gatherer(w, "Mushroom")) != null && g.Ai.Job is GatherJob gj && (gj.StepName == "Search" || gj.StepName == "Approach" || gj.StepName == "Collect"), 90f);
            checks.Add($"버섯 모으기 시작 ({g?.NpcName}, {TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            yield return new WaitForSeconds(0.5f);
            g.SetHealth(g.GetMaxHealth() * 0.3f);
            yield return TestKit.WaitFor(() => !(g.Ai.Job is GatherJob gj2 && gj2.Item == "Mushroom") && g.Ai.Queue.Count > 0 && g.Ai.Queue[0].Why == "다침", 90f);
            checks.Add($"모으다 다쳐도 '다침' 칸에 남김 ({TestKit.Waited:0}초, 일 {g.Ai.Job?.GetType().Name ?? "없음"})", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            g.SetHealth(g.GetMaxHealth() * 0.5f);
            yield return new WaitForSeconds(5f);
            checks.Add("모으기도 체력이 차기 전엔 안 나감", !(g.Ai.Job is GatherJob) && g.Ai.Queue.Count > 0 && g.Ai.Queue[0].Why == "다침");
            g.SetHealth(g.GetMaxHealth());
            yield return TestKit.WaitFor(() => g.Ai.Job is GatherJob gj3 && gj3.Item == "Mushroom", 60f);
            checks.Add($"체력이 차면 버섯 모으기 이어 감 ({TestKit.Waited:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => !(g.Ai.Job is GatherJob), 180f);
            TestKit.ClearBaseStock(w, "Mushroom");
        }

        static WorkerNpc Gatherer(TestKit.World w, string item)
        {
            foreach (var n in new[] { w.Ragnar, w.Floki })
                if (n != null && n.Ai.Job is GatherJob g && g.Item == item) return n;
            return null;
        }
    }
}
