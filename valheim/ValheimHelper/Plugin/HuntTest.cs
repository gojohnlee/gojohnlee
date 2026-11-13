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
            yield return TestKit.WaitFor(() => h.Ai.Job == null && h.Ai.Queue.Count > 0 && h.Ai.Queue[0].Why == "다침", 90f);
            checks.Add($"다쳐서 돌아와 '다침' 칸에 남김 ({TestKit.Waited:0}초, 이곳 {h.HurtSpots.CountAt(h.transform.position.x, h.transform.position.z)}번)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            h.SetHealth(h.GetMaxHealth() * 0.5f);
            yield return new WaitForSeconds(6f);
            checks.Add("체력이 차기 전엔 안 나감", !(h.Ai.Job is GatherJob) && h.Ai.Queue.Count > 0 && h.Ai.Queue[0].Why == "다침");
            h.SetHealth(h.GetMaxHealth());
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "RawMeat") >= 1, 150f);
            checks.Add($"체력이 차면 이어 가 고기 채움 ({GatherTest.CountBase(w.Home, "RawMeat")}개, {TestKit.Waited:0}초)", TestKit.Ok);
            TestKit.ClearBaseStock(w, "RawMeat");
        }
    }
}
