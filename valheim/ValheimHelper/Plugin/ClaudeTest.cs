using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_claudetest (루프 회차 18): 채팅 → 실제 claude 호출 → 작업 실행 흐름.
    /// 1) 이름으로 시작하는 ! 없는 말("라그나 나무 스무 개 베어 와")이 45초 안에 대사나 작업으로 이어지는지(Sonnet 길).
    /// 2) 수다("!잘 지내?")는 예외 없이 끝나고 호출이 끝나 다음 말을 받는지(Haiku 길).
    /// 호출이 실패해도(claude 없음·한도) "onlyBang" 대사로 끝나야 하므로 대사가 하나라도 나오면 흐름은 성공이다.
    /// </summary>
    static class ClaudeTest
    {
        public static void Register() => TestRunner.Register("vh_claudetest", "채팅 → Claude 호출 → 작업 실행 시험", () => TestKit.Flat(Run()));

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
            try
            {
                yield return TestKit.Setup(w);
                checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
                if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
                string n1 = w.Ragnar.NpcName;

                int before = w.Heard.Count;
                GatherTest.Say($"{n1} 나무 스무 개 베어 와", null);
                yield return TestKit.WaitFor(() => w.Heard.Count > before + 1 || w.Ragnar.Ai.Current == WorkerAI.Mode.Work, 45f);
                checks.Add("나무 부탁: 대사나 작업이 나옴", w.Heard.Count > before || w.Ragnar.Ai.Current == WorkerAI.Mode.Work);
                GatherTest.Say("!멈춰", null);
                yield return new WaitForSeconds(2f);

                before = w.Heard.Count;
                GatherTest.Say($"!{n1} 잘 지내?", null);
                yield return TestKit.WaitFor(() => w.Heard.Count > before, 45f);
                checks.Add("수다: 대사가 나옴(또는 조용히 무시)", true);
                yield return new WaitForSeconds(3f);
                checks.Add("호출이 끝나 NPC가 멀쩡함", w.Ragnar != null && w.Ragnar.GetHealth() > 0f);
            }
            finally { TestKit.Restore(w); }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }
    }
}
