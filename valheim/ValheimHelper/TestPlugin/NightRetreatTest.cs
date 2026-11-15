using System.Collections;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_nightretreattest (요청 밤 몫 ②): 밤에 일하다 센 적에게서 도망치면 일을 접고 거점으로 돌아와, 하던 일을 대기열에 아침 칸으로 두고
    /// 밤 동안은 안 꺼내다가 아침이 되면 이어 가는지. 낮에 같은 도망은 일을 안 접는다(비교). 도망은 Guard.TestFlee(적 없이)로 건다.
    /// </summary>
    static class NightRetreatTest
    {
        public static void Register() => TestRunner.Register("vh_nightretreattest", "밤 위험 귀환·아침 이어 감 시험", () => TestKit.Flat(Run()));

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer() || EnvMan.instance == null)
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
            if (w.Ragnar == null || w.Chest == null) yield break;
            var ai = w.Ragnar.Ai;
            EnvMan env = EnvMan.instance;

            // 1) 낮(Setup이 정오로 고정): 일하다 도망쳐도 일은 그대로
            TestKit.ClearBaseStock(w, "Wood");
            GatherTest.Say($"!{Workers.Name1} 나무 20", null);
            yield return TestKit.WaitFor(() => ai.Job is GatherJob, 20f);
            checks.Add("일 시작함", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            Quaternion rot = w.Player.transform.rotation;
            Vector3 away = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 60f));
            w.Ragnar.TeleportTo(away + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            ai.Guard?.TestFlee(GatherTest.Ground(w.Ragnar.transform.position + rot * new Vector3(8f, 0f, 0f)));
            yield return TestKit.WaitFor(() => !(ai.Guard?.Fleeing ?? false), 45f);
            yield return new WaitForSeconds(1f);
            checks.Add($"낮 도망 끝 ({TestKit.Waited:0}초)", TestKit.Ok);
            checks.Add($"낮엔 일 안 접음 ({ai.Current})", ai.Job is GatherJob && ai.Current == WorkerAI.Mode.Work);

            // 2) 자정: 같은 도망이면 일을 접고 거점으로, 대기열에 아침 칸
            env.m_debugTime = 0f;
            yield return TestKit.WaitFor(() => EnvMan.IsNight(), 10f);
            checks.Add("밤이 됨", TestKit.Ok);
            int heard = w.Heard.Count;
            ai.Guard?.TestFlee(GatherTest.Ground(w.Ragnar.transform.position + rot * new Vector3(-8f, 0f, 0f)));
            yield return TestKit.WaitFor(() => ai.Current == WorkerAI.Mode.Home, 45f);
            checks.Add($"밤 도망 뒤 일 접고 귀환 모드 ({TestKit.Waited:0}초)", TestKit.Ok && ai.Job == null);
            QueuedWork q = ai.Queue.Find(x => x.Resume is GatherJob);
            checks.Add("하던 일을 대기열에 기억", q != null);
            checks.Add("저장 칸이 아침 칸", q?.Work != null && q.Work.Ready == ReadyWhen.Morning);
            checks.Add("까닭 = 밤 위험", q?.Why == "밤 위험");
            checks.Add("밤 귀환 대사", w.Heard.Count > heard);
            yield return TestKit.WaitFor(() => Utils.DistanceXZ(w.Ragnar.transform.position, w.Home) < 15f, 60f);
            checks.Add($"거점 근처로 돌아옴 ({Utils.DistanceXZ(w.Ragnar.transform.position, w.Home):0}m, {TestKit.Waited:0}초)", TestKit.Ok);
            yield return new WaitForSeconds(8f);
            checks.Add($"밤 동안 다시 안 나감 ({ai.Current})", !(ai.Job is GatherJob) && ai.Queue.Exists(x => x.Resume is GatherJob));

            // 3) 아침: 하던 일을 이어 감
            env.m_debugTime = 0.3f;
            yield return TestKit.WaitFor(() => ai.Job is GatherJob, 30f);
            checks.Add($"아침에 하던 일 이어 감 ({TestKit.Waited:0}초)", TestKit.Ok && !ai.Queue.Exists(x => x.Resume is GatherJob));

            // 4) 밤에 새 땅을 찾아야 하는 일(모르는 산의 수정): 실패로 접지 않고 남은 몫을 아침 칸에(요청 밤 몫 ③, E090)
            env.m_debugTime = 0f;
            yield return TestKit.WaitFor(() => EnvMan.IsNight(), 10f);
            ai.Queue.Clear();
            var ways = GatherJob.WaysFor(w.Ragnar, "Crystal");
            ai.StartJob(new GatherJob(w.Ragnar, "Crystal", 5, ways));
            yield return TestKit.WaitFor(() => !(ai.Job is GatherJob), 30f);
            QueuedWork nq = ai.Queue.Find(x => x.Work != null && x.Work.Item == "Crystal");
            checks.Add($"밤 새 땅 일은 접고 아침 칸에 (길 {ways.Count}, {TestKit.Waited:0}초)", TestKit.Ok && nq != null && nq.Work.Ready == ReadyWhen.Morning && nq.Work.Amount == 5 && nq.Work.Why == "밤");
            env.m_debugTime = 0.3f;
            yield return TestKit.WaitFor(() => ai.Job is GatherJob g && g.Item == "Crystal", 30f);
            checks.Add($"아침에 수정 일 다시 잡음 ({TestKit.Waited:0}초)", TestKit.Ok);
        }
    }
}
