using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_posttest (호위병 경계 — 사용자 11-09): 거점 8m 앞 문 → 쉬는 호위병이 문 바깥 옆 자리로 가서 섬 → 집 반대편 35m 밖 그레일링(멈춤)은 문 옆에선 안 보임
    /// → 순찰(바로 시작)하며 집 둘레를 돌다 만나 처리 → 순찰 끝나면 문 옆으로. 순찰 순서·시간 한도는 Core PatrolTests.
    /// </summary>
    static class PostTest
    {
        public static void Register() => TestRunner.Register("vh_posttest", "호위병 문 옆 경계·순찰", () => TestKit.Flat(Run()));

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
            var made = new List<GameObject>();
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                GatherTest.Say("!멈춰", null);
                TestKit.Restore(w);
            }
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·일꾼 둘·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null) yield break;
            yield return TestKit.SpawnGuard();
            WorkerNpc guard = WorkerNpc.TheGuard;
            checks.Add($"호위병 생김 ({guard?.NpcName ?? "없음"}, {TestKit.Waited:0}초)", TestKit.Ok && guard != null);
            if (guard == null) yield break;

            // 1) 거점 8m 앞 문(벽 없이 문만) → 바깥(거점 반대쪽) 옆에 섬
            Vector3 f = w.Player.transform.forward; f.y = 0f; f.Normalize();
            Vector3 doorAt = GatherTest.Ground(w.Home + f * 8f);
            GameObject dp = ZNetScene.instance.GetPrefab("wood_door");
            made.Add(Object.Instantiate(dp, doorAt, Quaternion.LookRotation(f)));
            WorkerAI ai = guard.Ai;
            yield return TestKit.WaitFor(() => ai.HasPost && ai.Current == WorkerAI.Mode.Idle && !ai.GuardPatrol.Walking && Utils.DistanceXZ(guard.transform.position, ai.Post) < 1.5f, 70f);
            float side = Vector3.Dot(ai.Post - doorAt, f);
            checks.Add($"문 바깥 옆에 섬 ({TestKit.Waited:0.0}초, 문에서 {Utils.DistanceXZ(ai.Post, doorAt):0.0}m·바깥쪽 {side:0.0}m, 호위병 자리까지 {Utils.DistanceXZ(guard.transform.position, ai.Post):0.0}m)",
                TestKit.Ok && side > 1f && Utils.DistanceXZ(ai.Post, doorAt) < 3f);

            // 2) 집 반대편(거점 뒤 25m) 그레일링 — 문 옆(35m)에선 경계 밖
            Vector3 foeAt = w.Home - f * 25f;
            GameObject gp = ZNetScene.instance.GetPrefab("Greyling");
            Character foe = FulingTest.Spawn(gp, foeAt, made);
            foe.GetBaseAI().enabled = false; // 그 자리에 서 있음(EscortTest와 같음)
            yield return new WaitForSeconds(5f);
            checks.Add($"문 옆에선 안 감 (그레일링까지 {Utils.DistanceXZ(guard.transform.position, foe.transform.position):0}m, 싸움 {ai.Guard?.Engaged})",
                !foe.IsDead() && Utils.DistanceXZ(guard.transform.position, ai.Post) < 2f);

            // 3) 순찰 → 둘레를 돌다 그레일링 처리 → 순찰 끝 → 문 옆
            int rounds = WorkerAI.PatrolRounds;
            ai.GuardPatrol.Now();
            yield return TestKit.WaitFor(() => foe == null || foe.IsDead(), 120f);
            checks.Add($"순찰하며 몬스터 처리 ({TestKit.Waited:0.0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => WorkerAI.PatrolRounds > rounds && Utils.DistanceXZ(guard.transform.position, ai.Post) < 1.5f, 180f);
            checks.Add($"순찰 끝나고 문 옆으로 ({TestKit.Waited:0.0}초, 순찰 {WorkerAI.PatrolRounds - rounds}번, 자리까지 {Utils.DistanceXZ(guard.transform.position, ai.Post):0.0}m)", TestKit.Ok);
        }
    }
}
