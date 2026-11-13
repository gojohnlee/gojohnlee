using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_birdprobetest (땅에 앉은 새 화살 명중률 재기 — 프리팹 값 읽기): RandomFlyingBird가 붙은 프리팹을 모아
    /// Character 여부·최대 체력·떨굼 항목(CharacterDrop)을 diag에 남긴다. 게임 안 값만 읽고 아무것도 바꾸지 않는다.
    /// 쏘기·명중 셈은 다음 시험(vh_birdshoottest)에서.
    /// </summary>
    static class BirdProbeTest
    {
        public static void Register() => TestRunner.Register("vh_birdprobetest", "땅 앉은 새 프리팹 값 읽기", () => TestKit.Flat(Run()));

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

            var seen = new HashSet<string>();
            int count = 0;
            foreach (var bird in Resources.FindObjectsOfTypeAll<RandomFlyingBird>())
            {
                // 씬에 놓인 사본은 빼고 프리팹(에셋)만 센다
                if (bird == null || bird.gameObject.scene.IsValid()) continue;
                count++;
                string name = bird.gameObject.name;
                if (!seen.Add(name)) continue;
                Character ch = bird.GetComponent<Character>();
                CharacterDrop drop = bird.GetComponent<CharacterDrop>();
                string drops = drop == null ? "없음" : $"{drop.m_drops.Count}종";
                TestRunner.Log($"새 프리팹 {name}: Character {(ch != null ? "있음" : "없음")}" +
                               $"{(ch != null ? $", 최대 체력 {ch.GetMaxHealth():0.#}" : "")}, 떨굼 {drops}");
            }
            checks.Add($"땅 앉은 새 프리팹 찾음 ({count}개 중 {seen.Count}종)", seen.Count > 0);
            TestRunner.Log($"프리팹 종류 {string.Join(", ", seen)}");
            yield break;
        }
    }
}
