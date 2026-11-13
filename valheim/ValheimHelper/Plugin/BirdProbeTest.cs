using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_birdprobetest (땅에 앉은 새 화살 명중률 재기 — 프리팹 값 읽기): RandomFlyingBird가 붙은 프리팹을 모아
    /// Character 여부·최대 체력·떨굼 항목(CharacterDrop)을 diag에 남기고, 맞을 때 깎이는 값(Destructible 체력·저항)과 DropOnDestroyed 떨굼 표를 읽는다. 게임 안 값만 읽고 아무것도 바꾸지 않는다.
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
                // 체력이 어디서 깎이는지 찾으려고 붙은 부품(컴포넌트) 종류와 콜라이더 수를 남긴다
                var parts = new List<string>();
                foreach (var part in bird.GetComponentsInChildren<Component>()) parts.Add(part.GetType().Name);
                TestRunner.Log($"새 프리팹 {name} 부품: {string.Join(", ", parts)}, 콜라이더 {bird.GetComponentsInChildren<Collider>().Length}개");
                // 맞으면 깎이는 값(체력·저항·도구 단계)과 죽을 때 떨굼 — 명중 셈 시험이 이 값으로 몇 발에 죽는지 잰다
                var d = bird.GetComponent<Destructible>();
                var dropOnDestroyed = bird.GetComponent<DropOnDestroyed>();
                if (d != null)
                {
                    var m = d.m_damages;
                    TestRunner.Log($"새 프리팹 {name} 체력: m_health {d.m_health:0.#}, 단계 {d.m_minToolTier}, " +
                                   $"둔기 {m.m_blunt}·베기 {m.m_slash}·찌르기 {m.m_pierce}·불 {m.m_fire}");
                }
                var table = dropOnDestroyed?.m_dropWhenDestroyed;
                if (table != null)
                {
                    var items = new List<string>();
                    foreach (var dd in table.m_drops) items.Add($"{dd.m_item?.name}×{dd.m_stackMin}~{dd.m_stackMax}(확률 {dd.m_weight:0.##})");
                    TestRunner.Log($"새 프리팹 {name} 떨굼 {table.m_drops.Count}종: {string.Join(", ", items)}, 떨굼 횟수 {table.m_dropMin}~{table.m_dropMax}");
                }
            }
            checks.Add($"땅 앉은 새 프리팹 찾음 ({count}개 중 {seen.Count}종)", seen.Count > 0);
            TestRunner.Log($"프리팹 종류 {string.Join(", ", seen)}");
            yield break;
        }
    }
}
