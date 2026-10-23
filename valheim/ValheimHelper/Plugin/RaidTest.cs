using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_raidtest (5번 습격 귀환): 일하는 중 습격 구역 안이고 거점에서 30m 넘으면 일을 접고 거점으로 돌아오는지.
    /// 습격은 RandEventSystem.SetRandomEventByName으로 일꾼 위치에 건다(플레이어는 거점, 갓 모드).
    /// </summary>
    static class RaidTest
    {
        public static void Register() => TestRunner.Register("vh_raidtest", "습격 귀환 시험", () => TestKit.Flat(Run()));

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer() || RandEventSystem.instance == null)
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            try { yield return Body(w, checks); }
            finally
            {
                RandEventSystem.instance.ResetRandomEvent();
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            var ai = w.Ragnar.Ai;
            var evs = RandEventSystem.instance.m_events;
            checks.Add("습격 목록 있음", evs.Count > 0);
            if (evs.Count == 0) yield break;

            // 1) 일을 시킨 뒤 거점에서 60m 밖으로 옮김
            TestKit.ClearBaseStock(w, "Wood");
            GatherTest.Say($"!{Workers.Name1} 나무 20", null);
            yield return TestKit.WaitFor(() => ai.Job is GatherJob, 20f);
            checks.Add("일 시작함", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            Quaternion rot = w.Player.transform.rotation;
            Vector3 away = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 60f));
            w.Ragnar.TeleportTo(away + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            float d0 = Utils.DistanceXZ(w.Ragnar.transform.position, w.Home);
            checks.Add($"거점에서 30m 넘게 떨어짐 ({d0:0}m)", d0 > 30f);

            // 2) 일꾼 자리에 습격을 건다
            string name = evs[0].m_name;
            RandEventSystem.instance.SetRandomEventByName(name, w.Ragnar.transform.position);
            var ev = RandEventSystem.instance.GetCurrentRandomEvent();
            checks.Add($"습격이 걸림 ({name})", ev != null);
            if (ev == null) yield break;
            yield return TestKit.WaitFor(() => ai.Current == WorkerAI.Mode.Home, 12f);
            checks.Add($"일을 접고 거점 귀환 모드 ({TestKit.Waited:0}초)", TestKit.Ok && ai.Job == null);
            bool said = w.Heard.Exists(h => h.Contains("돌아"));
            checks.Add("귀환 대사를 함", said);
            yield return TestKit.WaitFor(() => Utils.DistanceXZ(w.Ragnar.transform.position, w.Home) < 15f, 60f);
            checks.Add($"거점 근처로 돌아옴 ({Utils.DistanceXZ(w.Ragnar.transform.position, w.Home):0}m, {TestKit.Waited:0}초)", TestKit.Ok);
        }
    }
}
