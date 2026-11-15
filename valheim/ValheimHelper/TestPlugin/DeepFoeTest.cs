using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_deepfoetest (⑮ 딥노스 마): 딥노스 적 판단·싸움. 애쉬랜드 다음 차림(AshFoeTest.BestGear — 불꽃금속 제작품·애쉬랜드 재료 음식 셋)으로
    /// 언 회색난쟁이·언 회색난쟁이 주술사·딥노스 해골·엘라킹·등불 엘라킹 하나씩 → 판단이 위험 비율대로인지(AshFoeTest.EachFoe).
    /// 평평한 시험 자리(딥노스 자리는 vh_deepnorthtest만).
    /// </summary>
    static class DeepFoeTest
    {
        public static void Register() => TestRunner.Register("vh_deepfoetest", "딥노스 적 싸움 시험", () => TestKit.Flat(Run()));

        static readonly string[] Foes = { "Greydwarf_Frozen", "Greydwarf_Shaman_Frozen", "Skeleton_DeepNorth", "Elaking", "ElakingLantern" };

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
                AshFoeTest.Cleanup(spawned);
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
            for (int i = 0; i < Foes.Length; i++) if (prefabs[i] == null) TestRunner.Log($"적 프리팹 없음: {Foes[i]}");
            checks.Add("딥노스 적 프리팹", prefabs.All(p => p != null));
            if (prefabs.Any(p => p == null)) yield break;
            var npc = w.Ragnar;
            var best = AshFoeTest.BestGear();
            TestRunner.Log($"애쉬랜드 다음 차림: {string.Join(",", best.ToArray())}");
            bool gearOk = best.Count == 7;
            foreach (var n in best) gearOk &= AshFoeTest.Give(npc, n);
            checks.Add("불꽃금속 차림·음식 셋 프리팹", gearOk);
            Prep.WearBest(npc);
            npc.ResetMeals();
            var ate = npc.EatFrom(npc.GetInventory());
            checks.Add($"음식 먹음 ({string.Join(",", ate.ToArray())})", ate.Count == 3);
            yield return AshFoeTest.EachFoe(checks, npc, Foes, prefabs, spawned);
        }
    }
}
