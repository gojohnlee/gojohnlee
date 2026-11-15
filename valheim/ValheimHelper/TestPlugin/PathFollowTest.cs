using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_pathfollow (길 기억 ③ 따라 걷기, 요청 11-15): 같은 출발·목표면 기억한 길 점을 따라 걷는지. 일꾼 자리 → 8m 앞 목표에 옆으로 4m 꺾인 가짜 길을 넣고
    /// 시험 걷기를 시키면 꺾인 점을 지나 닿아야 한다(바닐라는 곧장). 꺾인 길 첫 토막 가운데에 벽을 놓은 뒤(놓기가 지운 길을 다시 넣어 손님이 놓은 조각 흉내) 다시 걸으면
    /// 시작 때 막힘을 보고 그 길을 지우고 바닐라로 닿아야 한다.
    /// </summary>
    static class PathFollowTest
    {
        public static void Register() => TestRunner.Register("vh_pathfollow", "길 기억 따라 걷기 시험", () => TestKit.Flat(Run()));

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
            ExploreTest.WalkJob walk = null;
            try { yield return Body(w, checks, made, j => walk = j); }
            finally
            {
                if (walk != null) walk.Halt = true;
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static PathMemory.P P(Vector3 v) => new PathMemory.P(v.x, v.y, v.z);

        static void Line(List<PathMemory.P> pts, Vector3 a, Vector3 b)
        {
            int n = Mathf.CeilToInt(Utils.DistanceXZ(a, b) / 0.5f);
            for (int i = pts.Count == 0 ? 0 : 1; i <= n; i++) pts.Add(P(GatherTest.Ground(Vector3.Lerp(a, b, (float)i / n))));
        }

        static PathMemory.P[] Bent(Vector3 a, Vector3 c, Vector3 b) { var pts = new List<PathMemory.P>(); Line(pts, a, c); Line(pts, c, b); return pts.ToArray(); }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made, System.Action<ExploreTest.WalkJob> keep)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;
            var npc = w.Ragnar; var ai = npc.Ai;
            Quaternion rot = w.Player.transform.rotation;
            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right;
            Vector3 a0 = GatherTest.Ground(w.Home + right * 5f);
            Vector3 b = GatherTest.Ground(a0 + fwd * 8f), c = GatherTest.Ground(a0 + fwd * 4f + right * 4f), mid = GatherTest.Ground(a0 + fwd * 2f + right * 2f);
            int cut = GatherTest.ClearWood(a0 + fwd * 4f, 9f);
            if (cut > 0) TestRunner.Log($"자리 9m 안 나무 {cut}개 치움");

            var route = Bent(a0, c, b); // 벽 놓기 전에 — 놓은 뒤엔 Ground가 벽 꼭대기를 땅으로 잡아 점이 벽 위로 뜸(첫 시험들)
            for (int round = 0; round < 3; round++)
            {
                bool wall = round == 2, hurt = round == 1; // hurt: 걷는 중 맞으면 성공한 길 아님(사용자 11-15) — 기억 길 지우고 새 자취도 안 넣음
                if (wall)
                {
                    // 벽 방향을 몰라도 막히게 십자로 두 장(한 장은 길과 나란히 놓여 옆을 지나갔음 — 첫 시험)
                    foreach (var dir in new[] { fwd + right, fwd - right })
                        w.Player.PlacePiece(ZNetScene.instance.GetPrefab("woodwall").GetComponent<Piece>(), mid + Vector3.up, Quaternion.LookRotation(dir), doAttack: false);
                    yield return null;
                    foreach (var col in Physics.OverlapSphere(mid + Vector3.up, 1.5f)) { var wt = col.GetComponentInParent<WearNTear>(); if (wt != null && !made.Contains(wt.gameObject)) { made.Add(wt.gameObject); TestRunner.Log($"벽 자리 {wt.transform.position - mid:F1} 층 {LayerMask.LayerToName(col.gameObject.layer)} {col.bounds.size.x:0.0}×{col.bounds.size.z:0.0}m 높이 {col.bounds.min.y - mid.y:+0.0;-0.0}~{col.bounds.max.y - mid.y:+0.0;-0.0}"); } }
                    TestRunner.Log($"a0→c 직선 막힘(piece): {Physics.Linecast(a0 + Vector3.up * 0.8f, c + Vector3.up * 0.8f, LayerMask.GetMask("piece"))}, 아무 층: {Physics.Linecast(a0 + Vector3.up * 0.8f, c + Vector3.up * 0.8f)}");
                    checks.Add($"꺾인 길 가운데에 벽 놓임 ({made.Count}장)", made.Count == 2);
                }
                npc.TeleportTo(a0 + Vector3.up * 0.3f, rot, false);
                yield return new WaitForSeconds(2f);
                Vector3 a = npc.transform.position;
                double now = ZNet.instance.GetTimeSeconds();
                var pts = (PathMemory.P[])route.Clone(); pts[0] = P(a);
                Brain.Paths.Remember(P(a), P(b), pts, now);
                int f0 = ai.PathsFollowed, d0 = ai.PathsDropped, s0 = ai.PathsSpoiled;
                bool hit = false;
                var walk = new ExploreTest.WalkJob(ai, b);
                keep(walk);
                ai.ForgetMoveTarget(); // 앞 판 목표가 같은 b라 새 목표로 안 쳐 따라 걷기가 안 시작됨(회차 696 맞음 판)
                ai.StartJob(walk);
                float bend = float.MaxValue, t0 = Time.time;
                bool on = false;
                yield return TestKit.WaitFor(() =>
                {
                    bend = Mathf.Min(bend, Utils.DistanceXZ(npc.transform.position, c));
                    on |= ai.OnMemPath;
                    if (hurt && !hit && ai.OnMemPath && Time.time - t0 > 0.4f) { hit = true; var h = new HitData(); h.m_damage.m_blunt = 1f; h.m_point = npc.transform.position; npc.Damage(h); }
                    return Utils.DistanceXZ(npc.transform.position, b) < 2.2f;
                }, 30f);
                bool reached = TestKit.Ok;
                walk.Halt = true;
                string why = $"{Time.time - t0:0.0}초, 꺾인 점까지 최소 {bend:0.0}m, 따라 {ai.PathsFollowed - f0}·지움 {ai.PathsDropped - d0}, {npc.MoveDebug}";
                TestRunner.Log($"{(wall ? "벽 뒤" : "벽 앞")}: {why}");
                if (round == 0)
                {
                    checks.Add($"기억한 길 따라 걷기 시작 ({why})", ai.PathsFollowed - f0 == 1 && on);
                    checks.Add($"꺾인 점을 지나 닿음 ({why})", reached && bend < 1.2f);
                }
                else if (hurt)
                {
                    yield return new WaitForSeconds(1f);
                    bool kept = Brain.Paths.Find(P(a), P(b), ZNet.instance.GetTimeSeconds()) != null;
                    checks.Add($"걷다 맞으면 기억 길 지우고 새로 안 넣음 (맞음 {hit}, 남음 {kept}, 기억 안 함 {ai.PathsSpoiled - s0}, {why})", hit && ai.PathsDropped - d0 == 1 && !kept);
                }
                else
                {
                    checks.Add($"막힌 기억 길은 지우고 바닐라로 ({why})", ai.PathsDropped - d0 == 1 && ai.PathsFollowed - f0 == 0 && bend > 1f);
                    checks.Add($"그래도 닿음 ({why})", reached);
                }
                Brain.Paths.Forget(P(a), P(b));
                yield return new WaitForSeconds(1f);
            }
        }
    }
}
