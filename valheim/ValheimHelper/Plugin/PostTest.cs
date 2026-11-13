using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_posttest (호위병 경계 — 사용자 11-09): 거점 4m 앞 방문(양쪽 지붕)·8m 앞 바깥문 → 쉬는 호위병이 바깥문 바깥 옆 자리로 가서 섬 → 집 반대편 35m 밖 그레일링(멈춤)은 문 옆에선 안 보임
    /// → 순찰(바로 시작, 거점 둘레 크게)하며 점 근처에 옮긴 적을 만나 처리 → 순찰 끝나면 문 옆으로. 순찰 순서·시간 한도는 Core PatrolTests.
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
            var w = new TestKit.World { House = true }; // 사용자 11-12: VH_HOUSE=1이면 TheNewEra 집 곁에서 순찰·문 옆 경계를 셈
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

            // 1) 거점 4m 앞 집 안 방문(양쪽 지붕) + 8m 앞 바깥문(거점 쪽만 지붕, 11-09) → 바깥문 바깥(거점 반대쪽) 옆에 섬
            Vector3 f = w.Player.transform.forward; f.y = 0f; f.Normalize();
            Vector3 doorAt = GatherTest.Ground(w.Home + f * 8f), inner = GatherTest.Ground(w.Home + f * 4f);
            GameObject dp = ZNetScene.instance.GetPrefab("wood_door");
            made.Add(Object.Instantiate(dp, inner, Quaternion.LookRotation(f)));
            made.Add(Object.Instantiate(dp, doorAt, Quaternion.LookRotation(f)));
            foreach (Vector3 r in new[] { inner - f * 1.3f, inner + f * 1.3f, doorAt - f * 1.3f })
            {
                var g = Object.Instantiate(ZNetScene.instance.GetPrefab("wood_floor"), r + Vector3.up * 3f, Quaternion.LookRotation(f));
                var wnt = g.GetComponent<WearNTear>();
                if (wnt != null) wnt.m_noSupportWear = false; // 받침 없이 떠 있는 지붕
                made.Add(g);
            }
            WorkerAI ai = guard.Ai;
            yield return TestKit.WaitFor(() => ai.HasPost && ai.Current == WorkerAI.Mode.Idle && !ai.GuardPatrol.Walking && Utils.DistanceXZ(guard.transform.position, ai.Post) < 1.5f, 70f);
            float side = Vector3.Dot(ai.Post - doorAt, f);
            checks.Add($"방문 말고 바깥문 바깥 옆에 섬 (방문까지 {Utils.DistanceXZ(ai.Post, inner):0.0}m, {TestKit.Waited:0.0}초, 문에서 {Utils.DistanceXZ(ai.Post, doorAt):0.0}m·바깥쪽 {side:0.0}m, 호위병 자리까지 {Utils.DistanceXZ(guard.transform.position, ai.Post):0.0}m)",
                TestKit.Ok && side > 1f && Utils.DistanceXZ(ai.Post, doorAt) < 3f);

            // 2) 집 반대편(거점 뒤 25m) 그레일링 — 문 옆(35m)에선 경계 밖
            Vector3 foeAt = w.Home - f * 25f;
            GameObject gp = ZNetScene.instance.GetPrefab("Greyling");
            Character foe = FulingTest.Spawn(gp, foeAt, made);
            foe.GetBaseAI().enabled = false; // 그 자리에 서 있음(EscortTest와 같음)
            yield return new WaitForSeconds(5f);
            checks.Add($"문 옆에선 안 감 (그레일링까지 {Utils.DistanceXZ(guard.transform.position, foe.transform.position):0}m, 싸움 {ai.Guard?.Engaged})",
                !foe.IsDead() && Utils.DistanceXZ(guard.transform.position, ai.Post) < 2f);

            // 3) 순찰(거점 둘레 크게, PatrolRoute 점) → 가운데쯤 점 10m 안쪽에 옮긴 그레일링 처리 → 순찰 끝 → 문 옆
            foreach (var n in new[] { "Club", "ArmorLeatherChest", "ArmorLeatherLegs" }) guard.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1); // 거점 밖 먼 점은 일꾼이 안 도움 — 맨손이면 물러남
            int rounds = WorkerAI.PatrolRounds;
            var pts = ai.RoutePoints;
            checks.Add($"순찰 점 고름 ({WorkerAI.LastRoute})", pts.Count >= 3);
            if (pts.Count > 0)
            {
                var sp = pts[pts.Count / 2];
                Vector3 at = new Vector3((float)sp.X, 0f, (float)sp.Z);
                at += (w.Home - at).normalized * 10f;
                foe.transform.position = GatherTest.Ground(at) + Vector3.up * 0.3f;
            }
            ai.GuardPatrol.Now();
            yield return TestKit.WaitFor(() => foe == null || foe.IsDead(), 300f);
            checks.Add($"순찰하며 몬스터 처리 ({TestKit.Waited:0.0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => WorkerAI.PatrolRounds > rounds && Utils.DistanceXZ(guard.transform.position, ai.Post) < 1.5f, 400f);
            checks.Add($"순찰 끝나고 문 옆으로 ({TestKit.Waited:0.0}초, 순찰 {WorkerAI.PatrolRounds - rounds}번, {WorkerAI.LastRound}, 자리까지 {Utils.DistanceXZ(guard.transform.position, ai.Post):0.0}m)", TestKit.Ok);

            // 4) 플레이어가 3m 앞을 지나가면 경례, 같은 사람은 60초 안에 다시 안 함(요청 11-09)
            Vector3 back = w.Player.transform.position;
            int salutes = WorkerAI.Salutes;
            yield return new WaitForSeconds(1f);
            w.Player.TeleportTo(GatherTest.Ground(guard.transform.position + f * 3f) + Vector3.up * 0.3f, w.Player.transform.rotation, false);
            yield return TestKit.WaitFor(() => WorkerAI.Salutes > salutes, 5f);
            Vector3 toMe = w.Player.transform.position - guard.transform.position; toMe.y = 0f;
            float face = Vector3.Angle(guard.transform.forward, toMe);
            checks.Add($"옆을 지나가면 경례 ({WorkerAI.LastSalute ?? "고개만"}, {TestKit.Waited:0.0}초, 나를 보는 각 {face:0}°)", TestKit.Ok && WorkerAI.LastSalute != null && face < 30f);
            w.Player.TeleportTo(back, w.Player.transform.rotation, false);
            yield return new WaitForSeconds(3f);
            w.Player.TeleportTo(GatherTest.Ground(guard.transform.position + f * 3f) + Vector3.up * 0.3f, w.Player.transform.rotation, false);
            yield return new WaitForSeconds(4f);
            checks.Add($"60초 안 다시 지나가면 안 함 (경례 {WorkerAI.Salutes - salutes}번)", WorkerAI.Salutes - salutes == 1);
            w.Player.TeleportTo(back, w.Player.transform.rotation, false);
        }
    }
}
