using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_birdshoottest (땅에 앉은 새 명중 셈 — 뼈대): 시험 자리 근처의 땅 앉은 새(씬에 놓인 사본)를 거리 10·20·30m 띠별로 센다.
    /// 아직 쏘지 않는다 — 활 쏘기·맞음·잡음 셈은 다음 조각. 지금은 새 찾기·거리 재기가 게임 안에서 도는지만 본다.
    /// </summary>
    static class BirdShootTest
    {
        public static void Register() => TestRunner.Register("vh_birdshoottest", "땅 앉은 새 명중 셈 뼈대", () => TestKit.Flat(Run()));

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
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);

            Vector3 me = Player.m_localPlayer.transform.position;
            int near10 = 0, near20 = 0, near30 = 0;
            foreach (var bird in Resources.FindObjectsOfTypeAll<RandomFlyingBird>())
            {
                // 씬에 놓인 사본만 센다(프리팹 에셋은 뺌)
                if (bird == null || !bird.gameObject.scene.IsValid()) continue;
                float dist = Vector3.Distance(me, bird.transform.position);
                if (dist <= 10f) near10++;
                if (dist <= 20f) near20++;
                if (dist <= 30f) near30++;
            }
            TestRunner.Log($"땅 앉은 새 거리 띠: 10m {near10}마리 · 20m {near20}마리 · 30m {near30}마리");
            yield break;
        }
    }
}
