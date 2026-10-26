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
            var saved = new float[6];
            Character held = null;
            bool running = true;
            Plugin.Instance.StartCoroutine(TestKit.ClearWhileHeld(w, () => held, () => running)); // 묶인 일꾼은 도망 못 함 → 둘레 생물 치움
            try { yield return Body(w, checks, saved, c => held = c); }
            finally
            {
                running = false;
                if (held != null) Speeds(held, saved, false);
                TestKit.Restore(w);
            }
            if (TestKit.KeptClear > 0) TestRunner.Log($"발 묶인 동안 둘레 생물 {TestKit.KeptClear}마리 치움");
            TestKit.End(w, checks);
        }

        /// <summary>걷기·달리기·점프 힘을 0으로 묶거나(freeze) saved로 되돌린다.</summary>
        static void Speeds(Character c, float[] saved, bool freeze)
        {
            if (freeze) { saved[0] = c.m_speed; saved[1] = c.m_walkSpeed; saved[2] = c.m_runSpeed; saved[3] = c.m_swimSpeed; saved[4] = c.m_jumpForce; saved[5] = c.m_jumpForceForward; }
            c.m_speed = freeze ? 0f : saved[0];
            c.m_walkSpeed = freeze ? 0f : saved[1];
            c.m_runSpeed = freeze ? 0f : saved[2];
            c.m_swimSpeed = freeze ? 0f : saved[3];
            c.m_jumpForce = freeze ? 0f : saved[4];
            c.m_jumpForceForward = freeze ? 0f : saved[5]; // 점프 힘 0이어도 앞으로 튐(Character.Jump: forward × m_jumpForceForward) → 묶인 채 조금씩 나아가 귀환 막힘을 못 셈(E026)
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, float[] saved, System.Action<Character> hold)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            var ai = w.Ragnar.Ai;

            TestKit.ClearBaseStock(w, "Wood");
            GatherTest.Say($"!{Workers.Name1} 나무 30", null);
            // 준비(상자·도구)가 끝나 밖으로 나갈 때 묶는다(준비 중 상자 가기는 따로 순간이동함)
            yield return TestKit.WaitFor(() => ai.Job is GatherJob g && (g.StepName == "Search" || g.StepName == "Approach" || g.StepName == "GoZone"), 60f);
            checks.Add("일 시작함", TestKit.Ok);
            if (!TestKit.Ok) yield break;

            // 거점에서 40m 떨어진 곳(근처 나무가 시야 안)으로 옮기고 발을 묶는다
            Quaternion rot = w.Player.transform.rotation;
            // 나무 바로 옆이면 묶인 채 베어 버려 막히지 않는다(회차 122: pit→stuck 묶음에서 플레이어 방향이 달라 6m 안에서 일함, 단독은 PASS)
            // → 가장 가까운 나무가 8~40m인 방향을 고른다(없으면 앞쪽)
            Vector3 spot = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 40f));
            for (float ang = 0f; ang < 360f; ang += 45f)
            {
                Vector3 p = GatherTest.Ground(w.Home + Quaternion.Euler(0f, ang, 0f) * rot * new Vector3(0f, 0f, 40f));
                float near = float.MaxValue;
                foreach (var t in Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None))
                    near = Mathf.Min(near, Utils.DistanceXZ(t.transform.position, p));
                if (near >= 8f && near <= 40f) { spot = p; TestRunner.Log($"묶을 자리 {ang:0}°: 가장 가까운 나무 {near:0.0}m"); break; }
            }
            w.Ragnar.TeleportTo(spot + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(1f);
            Speeds(w.Ragnar, saved, true);
            hold(w.Ragnar);
            float hp0 = w.Ragnar.GetHealth();
            int rescues0 = GatherJob.Rescues, hops0 = GatherJob.Hops;
            float d0 = Utils.DistanceXZ(w.Ragnar.transform.position, w.Home);
            checks.Add($"거점에서 떨어져 발이 묶임 ({d0:0}m)", d0 > 30f);

            // 근처 막힘 건너뛰기(사용자 결정 10-23): NearStuckSeconds(15초, 10-26 절반) 넘게 못 가면 막힌 곳 너머 몇 m로 옮기고 구조 대사, 짐 그대로
            int skips0 = GatherJob.NearSkips, items0 = w.Ragnar.GetInventory().NrOfItems();
            Vector3 before = w.Ragnar.transform.position;
            yield return TestKit.WaitFor(() => GatherJob.NearSkips > skips0 || GatherJob.Rescues > rescues0, 70f);
            float skipAt = TestKit.Waited;
            bool skipped = GatherJob.NearSkips > skips0;
            Vector3 after = w.Ragnar.transform.position;
            float moved = Utils.DistanceXZ(before, after);
            bool ground = ZoneSystem.instance.GetGroundHeight(after, out float gh) && Mathf.Abs(after.y - gh) < 1.5f;
            TestRunner.Log($"근처 막힘 건너뛰기: {skipAt:0}초, {moved:0.0}m 옮김, 땅과 {after.y - gh:0.0}m, 짐 {items0} → {w.Ragnar.GetInventory().NrOfItems()}");
            checks.Add($"근처 {Core.Work.StuckHop.NearStuckSeconds:0}초 막힘 → 막힌 곳 너머로 옮김 ({skipAt:0}초, {moved:0.0}m)", skipped && skipAt >= (float)Core.Work.StuckHop.NearStuckSeconds - 5f && moved >= 2f && moved <= 9f);
            checks.Add("옮긴 자리가 땅 위", skipped && ground);
            checks.Add($"짐 그대로 ({items0} → {w.Ragnar.GetInventory().NrOfItems()})", w.Ragnar.GetInventory().NrOfItems() == items0);
            yield return new WaitForSeconds(1f);
            checks.Add("구조 대사를 함", w.Heard.Exists(h => h.Contains("오딘") || h.Contains("신들") || h.Contains("빠져나왔")));

            float waited = skipAt;
            for (; waited < 300f && GatherJob.Rescues == rescues0; waited += 10f)
            {
                yield return TestKit.WaitFor(() => GatherJob.Rescues > rescues0, 10f);
                TestRunner.Log($"  [{waited + 10f:0}s] 묶인 자리에서 {Utils.DistanceXZ(w.Ragnar.transform.position, spot):0.0}m, {ai.Current}/{ai.Job?.StepName}");
            }
            bool rescued = GatherJob.Rescues > rescues0;
            float d1 = Utils.DistanceXZ(w.Ragnar.transform.position, w.Home);
            Speeds(w.Ragnar, saved, false);
            hold(null);
            checks.Add($"깃발로 강제 복귀함 ({waited:0}초 안)", rescued);
            checks.Add($"플레이어 근처라 목표 쪽 순간이동 안 함 ({GatherJob.Hops - hops0}번)", GatherJob.Hops == hops0);
            if (!rescued) yield break;
            checks.Add($"거점에 있음 ({d1:0.0}m)", d1 < 5f);
            // 음식이 떨어지면 최대 체력이 줄어 그만큼 줄 수 있다(회차 72: 71 → 69, 최대도 69) → 최대보다 낮아졌을 때만 다친 것
            checks.Add($"체력 그대로 ({hp0:0} → {w.Ragnar.GetHealth():0}/{w.Ragnar.GetMaxHealth():0})", Mathf.Abs(w.Ragnar.GetHealth() - hp0) < 1f || w.Ragnar.GetHealth() >= w.Ragnar.GetMaxHealth() - 0.5f);
            checks.Add("막힘 대사를 함", w.Heard.Exists(h => h.Contains("막") || h.Contains("꼼짝")));
            yield return new WaitForSeconds(3f);
            checks.Add($"일을 이어 감 ({ai.Current}/{ai.Job?.StepName})", ai.Job is GatherJob);
        }
    }
}
