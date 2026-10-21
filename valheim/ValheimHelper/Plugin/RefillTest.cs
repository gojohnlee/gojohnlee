using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_refilltest (⑩ 자동 보충): "!채워 나무 20" 기준치를 두면 한가한 일꾼이 스스로 나가 나무를 상자에 채우고,
    /// 다 채워지면 더 나가지 않는지. 1분 시계는 시험이 바로 돌린다(Departures.TestRefillNow).
    /// </summary>
    static class RefillTest
    {
        public static void Register() => TestRunner.Register("vh_refilltest", "자동 보충(!채워) 시험", () => TestKit.Flat(Run()));

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
            finally
            {
                Brain.Refill.Off();
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static int Wood(TestKit.World w) => GatherTest.CountBase(w.Home, "Wood");

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
            TestKit.ClearBaseStock(w, "Wood");
            checks.Add("거점 나무 0으로 시작", Wood(w) == 0);

            GatherTest.Say($"!채워 나무 20", null);
            yield return new WaitForSeconds(1f);
            checks.Add("기준치 등록됨", Brain.Refill.Count == 1);

            Departures.TestRefillNow();
            yield return new WaitForSeconds(1f);
            bool started = w.Claude.Ai.Job is GatherJob || w.Alarak.Ai.Job is GatherJob;
            checks.Add("한가한 일꾼이 채집 시작", started);
            if (!started) yield break;
            yield return TestKit.WaitFor(() => Wood(w) >= 18, 180f);
            checks.Add($"나무가 기준치 근처까지 찼음 ({Wood(w)}, {TestKit.Waited:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null && w.Alarak.Ai.Job == null, 60f);
            checks.Add("일 끝남", TestKit.Ok);
            int full = Wood(w);
            Departures.TestRefillNow();
            yield return new WaitForSeconds(2f);
            checks.Add($"채워진 뒤엔 다시 안 나감 (나무 {full})", !(w.Claude.Ai.Job is GatherJob) && !(w.Alarak.Ai.Job is GatherJob));
            TestKit.ClearBaseStock(w, "Wood");
        }
    }
}
