using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_stucktest (깃발 강제 복귀, 사용자 결정 2026-10-22): 일하러 나가는 중 아무리 걸어도 진척이 없고, 목표를 바꿔도(재탐색) 또 같은 자리에서
    /// 막히면 거점으로 순간이동해 체력은 그대로 두고 일을 이어 가는지. 끼임은 걷기·달리기·점프 힘을 0으로 묶어 흉내 낸다
    /// (돌벽 우리는 시험 월드 비탈에서 빠져나감 — 회차 55 첫 시험).
    /// </summary>
    static class StuckTest
    {
        public static void Register() => TestRunner.Register("vh_stucktest", "깃발 강제 복귀 시험", () => TestKit.Flat(Run()));

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
            var saved = new float[5];
            Character held = null;
            try { yield return Body(w, checks, saved, c => held = c); }
            finally
            {
                if (held != null) Speeds(held, saved, false);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        /// <summary>걷기·달리기·점프 힘을 0으로 묶거나(freeze) saved로 되돌린다.</summary>
        static void Speeds(Character c, float[] saved, bool freeze)
        {
            if (freeze) { saved[0] = c.m_speed; saved[1] = c.m_walkSpeed; saved[2] = c.m_runSpeed; saved[3] = c.m_swimSpeed; saved[4] = c.m_jumpForce; }
            c.m_speed = freeze ? 0f : saved[0];
            c.m_walkSpeed = freeze ? 0f : saved[1];
            c.m_runSpeed = freeze ? 0f : saved[2];
            c.m_swimSpeed = freeze ? 0f : saved[3];
            c.m_jumpForce = freeze ? 0f : saved[4];
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, float[] saved, System.Action<Character> hold)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
            var ai = w.Claude.Ai;

            TestKit.ClearBaseStock(w, "Wood");
            GatherTest.Say($"!{Workers.Name1} 나무 30", null);
            // 준비(상자·도구)가 끝나 밖으로 나갈 때 묶는다(준비 중 상자 가기는 따로 순간이동함)
            yield return TestKit.WaitFor(() => ai.Job is GatherJob g && (g.StepName == "Search" || g.StepName == "Approach" || g.StepName == "GoZone"), 60f);
            checks.Add("일 시작함", TestKit.Ok);
            if (!TestKit.Ok) yield break;

            // 거점에서 40m 떨어진 곳(근처 나무가 시야 안)으로 옮기고 발을 묶는다
            Quaternion rot = w.Player.transform.rotation;
            Vector3 spot = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 40f));
            w.Claude.TeleportTo(spot + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(1f);
            Speeds(w.Claude, saved, true);
            hold(w.Claude);
            float hp0 = w.Claude.GetHealth();
            int rescues0 = GatherJob.Rescues;
            float d0 = Utils.DistanceXZ(w.Claude.transform.position, w.Home);
            checks.Add($"거점에서 떨어져 발이 묶임 ({d0:0}m)", d0 > 30f);

            float waited = 0f;
            for (; waited < 150f && GatherJob.Rescues == rescues0; waited += 10f)
            {
                yield return TestKit.WaitFor(() => GatherJob.Rescues > rescues0, 10f);
                TestRunner.Log($"  [{waited + 10f:0}s] 묶인 자리에서 {Utils.DistanceXZ(w.Claude.transform.position, spot):0.0}m, {ai.Current}/{ai.Job?.StepName}");
            }
            bool rescued = GatherJob.Rescues > rescues0;
            float d1 = Utils.DistanceXZ(w.Claude.transform.position, w.Home);
            Speeds(w.Claude, saved, false);
            hold(null);
            checks.Add($"깃발로 강제 복귀함 ({waited:0}초 안)", rescued);
            if (!rescued) yield break;
            checks.Add($"거점에 있음 ({d1:0.0}m)", d1 < 5f);
            // 음식이 떨어지면 최대 체력이 줄어 그만큼 줄 수 있다(회차 72: 71 → 69, 최대도 69) → 최대보다 낮아졌을 때만 다친 것
            checks.Add($"체력 그대로 ({hp0:0} → {w.Claude.GetHealth():0}/{w.Claude.GetMaxHealth():0})", Mathf.Abs(w.Claude.GetHealth() - hp0) < 1f || w.Claude.GetHealth() >= w.Claude.GetMaxHealth() - 0.5f);
            checks.Add("막힘 대사를 함", w.Heard.Exists(h => h.Contains("막") || h.Contains("꼼짝")));
            yield return new WaitForSeconds(3f);
            checks.Add($"일을 이어 감 ({ai.Current}/{ai.Job?.StepName})", ai.Job is GatherJob);
        }
    }
}
