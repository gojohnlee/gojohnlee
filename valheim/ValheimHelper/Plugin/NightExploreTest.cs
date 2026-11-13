using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_nightexploretest (밤 명령 판단, 사용자 11-13 "밤이라고 칼같이 접지 말기"): 자정으로 고정하고
    /// ① 처음 장비 Ragnar의 밤 목초지·늪 위험 값을 숫자로 남김(판정 아님) ② 방향만 말한 밤 탐험은 거절
    /// ③ 가장 좋은 차림(AshFoeTest.BestGear)이면 밤 목초지 위험 값이 HardFrom 밑 → "탐험 목초지" 시작 → 90초 동안 접지 않음(복귀 0) → "!멈춰".
    /// </summary>
    static class NightExploreTest
    {
        public static void Register() => TestRunner.Register("vh_nightexploretest", "밤 탐험 명령 판단(장비별)", () => TestKit.Flat(Run()), 600f);

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
            EnvMan env = EnvMan.instance;
            bool debugTime = env.m_debugTimeOfDay;
            float debugValue = env.m_debugTime;
            try { yield return Body(w, checks, env); }
            finally
            {
                env.m_debugTimeOfDay = debugTime;
                env.m_debugTime = debugValue;
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, EnvMan env)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;
            var npc = w.Ragnar;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0f; // 자정
            yield return new WaitForSeconds(2f);
            checks.Add($"밤 맞춤 (날 비율 {EnvMan.instance.GetDayFraction():0.000})", EnvMan.IsNight());
            int meadows = (int)Heightmap.Biome.Meadows, swamp = (int)Heightmap.Biome.Swamp;
            Brain.Get().SetBiome(Senses.ZoneOf(npc.Home), meadows); // 거점 바이옴은 아는 땅

            // ① 처음 장비: 값만 남김
            float weakMeadow = Departures.NightRatio(npc, meadows), weakSwamp = Departures.NightRatio(npc, swamp);
            TestRunner.Log($"DIAG 처음 장비 밤 위험: 목초지 {weakMeadow:0.00} · 늪 {weakSwamp:0.00} (무리 기준 {Core.Npc.Departure.HardFrom:0.00}, 밤 몫 목초지 {Departures.NightOf(meadows).Count}줄·늪 {Departures.NightOf(swamp).Count}줄)");

            // ② 방향만 말한 밤 탐험은 거절
            Say($"!{Workers.Name1} 탐험 북");
            yield return new WaitForSeconds(1.5f);
            checks.Add("방향만 말한 밤 탐험 거절", !(npc.Ai.Job is ExploreJob));

            // ③ 가장 좋은 차림
            foreach (var it in new List<ItemDrop.ItemData>(npc.GetInventory().GetAllItems())) { npc.UnequipItem(it, false); npc.GetInventory().RemoveItem(it); }
            var best = AshFoeTest.BestGear();
            bool gearOk = best.Count == 7;
            foreach (var n in best) gearOk &= AshFoeTest.Give(npc, n);
            Prep.WearBest(npc);
            npc.ResetMeals();
            npc.EatFrom(npc.GetInventory());
            checks.Add($"좋은 차림 ({string.Join(",", best.ToArray())})", gearOk);
            float strong = Departures.NightRatio(npc, meadows);
            TestRunner.Log($"DIAG 좋은 차림 밤 위험: 목초지 {strong:0.00} · 늪 {Departures.NightRatio(npc, swamp):0.00}");
            checks.Add($"좋은 차림 밤 목초지 안 위험 ({strong:0.00} < {Core.Npc.Departure.HardFrom:0.00})", strong < Core.Npc.Departure.HardFrom);

            Say($"!{Workers.Name1} 탐험 목초지");
            yield return new WaitForSeconds(1.5f);
            checks.Add("밤 목초지 탐험 시작", npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job is ExploreJob);
            float t = 0f, maxDist = 0f, kept = 0f;
            while (t < 90f && npc != null && !npc.IsDead())
            {
                yield return new WaitForSeconds(1f);
                t += 1f;
                if (npc.Ai.Job is ExploreJob) kept = t;
                maxDist = Mathf.Max(maxDist, Utils.DistanceXZ(npc.Home, npc.transform.position));
                if (!EnvMan.IsNight()) break;
            }
            TestRunner.Log($"DIAG 밤 탐험 {kept:0}초 이어 감 · 최대 {maxDist:0}m · 체력 {(npc ? npc.GetHealth() : 0):0} · 상태 {(npc ? npc.Ai.Current + "/" + npc.Ai.Job?.StepName : "없음")}");
            checks.Add($"밤 탐험 접지 않음 ({kept:0}초 동안 탐험, 최대 {maxDist:0}m)", npc != null && !npc.IsDead() && (kept >= 89f || maxDist > 100f));
            Say($"!{Workers.Name1} 멈춰");
            yield return new WaitForSeconds(1.5f);
            checks.Add("!멈춰 → 탐험 그만", npc != null && !(npc.Ai.Job is ExploreJob));
        }

        static void Say(string text)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text, ZDOID.None);
        }
    }
}
