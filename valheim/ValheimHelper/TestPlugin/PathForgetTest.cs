using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_pathforget (길 기억 ③, 요청 11-15): 집 안 길 기억이 건축 바뀜·나이로 지워지는지. 플레이어 앞 땅에 가짜 길 둘(가까운 것·20m 먼 것)을 넣고
    /// 가까운 길 위에 나무 벽을 놓으면(PlacePiece → SetCreator) 가까운 길만 지워지고, 다시 넣은 뒤 벽을 부수면(ZDO 지움 → HandleDestroyedZDO) 또 지워지는지.
    /// 오래된 길은 Prune(PathMaxAge)로. 일꾼 없이 돈다(Brain.Paths는 서버 기억).
    /// </summary>
    static class PathForgetTest
    {
        public static void Register() => TestRunner.Register("vh_pathforget", "길 기억 건축 바뀜·나이 지움 시험", () => TestKit.Flat(Run()));

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
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static PathMemory.P P(Vector3 v) => new PathMemory.P(v.x, v.y, v.z);

        static PathMemory.P[] Line(Vector3 a, Vector3 b)
        {
            var pts = new List<PathMemory.P>();
            int n = Mathf.CeilToInt(Vector3.Distance(a, b) / 0.5f);
            for (int i = 0; i <= n; i++) pts.Add(P(Vector3.Lerp(a, b, (float)i / n)));
            return pts.ToArray();
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            w.Player = Player.m_localPlayer;
            Transform t = w.Player.transform;
            Vector3 at = GatherTest.Ground(t.position + t.forward * 6f);
            Vector3 a = GatherTest.Ground(at - t.right * 3f), b = GatherTest.Ground(at + t.right * 3f);
            Vector3 fa = GatherTest.Ground(a + t.forward * 20f), fb = GatherTest.Ground(b + t.forward * 20f);
            var paths = Brain.Paths;
            double now = ZNet.instance.GetTimeSeconds();
            int c0 = paths.Count, f0 = Brain.PathsForgotBuild;
            paths.Remember(P(a), P(b), Line(a, b), now);
            paths.Remember(P(fa), P(fb), Line(fa, fb), now);
            checks.Add($"가짜 길 둘 넣음 ({c0}→{paths.Count})", paths.Find(P(a), P(b), now) != null && paths.Find(P(fa), P(fb), now) != null);

            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("wood_wall_half").GetComponent<Piece>(), at, t.rotation, doAttack: false);
            yield return null;
            WearNTear wall = null;
            foreach (var col in Physics.OverlapSphere(at, 1.5f)) { wall = col.GetComponentInParent<WearNTear>(); if (wall != null) break; }
            if (wall != null) made.Add(wall.gameObject);
            checks.Add("벽 놓임", wall != null);
            checks.Add($"놓기: 가까운 길 지움, 먼 길 남음 (지운 수 {Brain.PathsForgotBuild - f0})", paths.Find(P(a), P(b), now) == null && paths.Find(P(fa), P(fb), now) != null);

            paths.Remember(P(a), P(b), Line(a, b), now);
            int f1 = Brain.PathsForgotBuild;
            bool placed = wall != null; // 부순 뒤엔 Unity가 wall을 null로 침
            if (wall != null) { ZNetScene.instance.Destroy(wall.gameObject); made.Remove(wall.gameObject); }
            float until = Time.time + 3f;
            while (Time.time < until && paths.Find(P(a), P(b), now) != null) yield return null;
            checks.Add($"부서짐: 가까운 길 지움, 먼 길 남음 (지운 수 {Brain.PathsForgotBuild - f1})", placed && paths.Find(P(a), P(b), now) == null && paths.Find(P(fa), P(fb), now) != null);

            paths.Remember(P(a), P(b), Line(a, b), now - Brain.PathMaxAge - 10);
            int pruned = paths.Prune(now, Brain.PathMaxAge);
            checks.Add($"오래된 길만 지움 (Prune {pruned})", pruned >= 1 && paths.Find(P(a), P(b), now) == null && paths.Find(P(fa), P(fb), now) != null);
            paths.Forget(P(fa), P(fb));
            checks.Add($"길 수 제자리 ({c0}→{paths.Count}, 원래 오래된 것 {pruned - 1})", paths.Count == c0 - (pruned - 1));
        }
    }
}
