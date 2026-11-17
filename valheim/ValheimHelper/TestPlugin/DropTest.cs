using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_droptest(요청 11-11 뛰어내리기 지름길 ★ 라): 깃발 곁 평지에 4m 높이 wood_floor 판(2m×8m, 기둥 받침)과 먼 쪽 끝 계단.
    /// 판 가까운 끝에 선 라그나에게 판 옆 아래 땅을 걷게 함 → 바닥 끝에서 뛰어내렸는지(DropsTaken)·그만 0·닿음, 같은 다리를 뛰어내리기 끄고(계단) 한 번 더 걸어 시간 비교.
    /// </summary>
    static class DropTest
    {
        public static void Register() => TestRunner.Register("vh_droptest", "뛰어내리기 지름길 — 4m 판에서 계단 대신 뛰어내림", () => TestKit.Flat(Run()));

        const float Height = 4f;
        const int Tiles = 4; // 판 길이 2m × 4

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
                WorkerAI.TestNoDrop = false;
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명", w.Ragnar != null && w.Floki != null);
            if (w.Ragnar == null) yield break;

            // 1) 판 방향: 깃발에서 4~24m 땅이 가장 고른 쪽(8방향)
            Vector3 home = w.Home, f = Vector3.forward;
            float bestSpread = float.MaxValue;
            for (int a = 0; a < 8; a++)
            {
                Vector3 d = Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward;
                float lo = float.MaxValue, hi = float.MinValue;
                for (float s = 4f; s <= 24f; s += 2f)
                    foreach (float side in new[] { -5f, 0f, 5f })
                    {
                        float h = ZoneSystem.instance.GetGroundHeight(home + d * s + Vector3.Cross(Vector3.up, d) * side);
                        lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                    }
                if (ZoneSystem.instance.GetSolidHeight(home + d * 14f) < ZoneSystem.instance.m_waterLevel + 0.5f) continue;
                if (hi - lo < bestSpread) { bestSpread = hi - lo; f = d; }
            }
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Vector3 near = home + f * 6f; // 판 가까운 끝(가운데)
            int cleared = HouseTest.ClearPlants(near + f * 8f, 16f);
            yield return new WaitForSeconds(1f);

            // 2) 판: 땅 가장 높은 곳 + 4m, 2m 칸 4개, 칸 경계마다 양옆 기둥(2m 기둥을 땅까지 쌓음)
            float gTop = float.MinValue;
            for (float s = 0f; s <= Tiles * 2f; s += 1f)
                foreach (float side in new[] { -1f, 1f })
                    gTop = Mathf.Max(gTop, ZoneSystem.instance.GetGroundHeight(near + f * s + r * side));
            float floorTop = gTop + Height;
            Quaternion rot = Quaternion.LookRotation(f);
            int floors = 0, poles = 0;
            for (int i = 0; i < Tiles; i++)
            {
                var fl = HouseTest.Put(made, "wood_floor", near + f * (1f + 2f * i), rot, floorTop - 0.1f);
                if (fl == null) continue;
                fl.transform.position += Vector3.up * (floorTop - HouseTest.Box(fl).max.y); Physics.SyncTransforms();
                floors++;
            }
            for (int i = 0; i <= Tiles; i++)
                foreach (float side in new[] { -0.9f, 0.9f })
                {
                    Vector3 at = near + f * Mathf.Clamp(2f * i, 0.1f, Tiles * 2f - 0.1f) + r * side;
                    float g = ZoneSystem.instance.GetGroundHeight(at);
                    for (float b = floorTop - 0.1f - 2f; b > g - 2f; b -= 2f)
                        if (HouseTest.Put(made, "wood_pole2", at, rot, b) != null) poles++;
                }
            // 3) 계단: 먼 끝에서 f쪽으로 땅 닿을 때까지(wood_stair 한 칸 2m 가며 1m 오름 — housetest 4-4 따라, 높은 끝을 판 가장자리에)
            int stairs = 0;
            Quaternion srot = Quaternion.LookRotation(-f);
            Vector3 edge = near + f * (Tiles * 2f); edge.y = floorTop;
            for (int k = 0; k < 7; k++)
            {
                var st = HouseTest.Put(made, "wood_stair", edge + f * 1f, srot, edge.y - 1f);
                if (st == null || !Profile(st, f, out Vector3 hi, out Vector3 lo)) break;
                if (Vector3.Dot(hi - lo, f) > 0f) { srot = Quaternion.LookRotation(f); st.transform.rotation = srot; Profile(st, f, out hi, out lo); }
                st.transform.position += edge - hi;
                Profile(st, f, out hi, out lo);
                stairs++;
                if (lo.y <= ZoneSystem.instance.GetGroundHeight(lo) + 0.35f) break;
                edge = lo + f * 0.15f; edge.y = lo.y - 0.2f;
            }
            Brain.DropsRescan(); // Instantiate는 SetCreator를 안 거침 → 내려가기 점 다시 훑기
            Plugin.Log.LogInfo($"뛰어내리기 시험: 방향 {f:F2}·땅 차 {bestSpread:0.0}m·치운 것 {cleared}, 바닥 {floors}·기둥 {poles}·계단 {stairs}칸, 판 높이 {floorTop:0.0}(땅 {gTop:0.0})");
            yield return new WaitForSeconds(3f);
            bool built = floors == Tiles && stairs >= 3 && made.TrueForAll(g => g != null);
            checks.Add($"판·기둥·계단 (바닥 {floors}/{Tiles}, 기둥 {poles}, 계단 {stairs}칸, 남음 {made.TrueForAll(g => g != null)})", built);
            if (!built) yield break;

            // 4) 판 위 → 판 옆 아래 땅(가까운 끝 곁, 옆 5m): 뛰어내림 / 같은 다리 계단으로
            Vector3 start = near + f * 1f; start.y = floorTop + 0.3f;
            Vector3 goal = GatherTest.Ground(near + f * 1.5f + r * 5f);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            var ai = w.Ragnar.Ai;
            float[] took = new float[2];
            bool[] got = new bool[2];
            float stairT = 0f;
            for (int leg = 0; leg < 2; leg++)
            {
                WorkerAI.TestNoDrop = leg == 1;
                w.Ragnar.TeleportTo(start, rot, false);
                yield return new WaitForSeconds(3f);
                // GetPath는 첫 요청 때 타일을 굽기 시작해 처음엔 실패(계단 길 1000으로 고름·끄면 판에서 그냥 걸어 떨어짐, 회차 735 첫 실행) → 계단 길이 날 때까지 묻고 기다림
                float stairLen = -1f;
                // 앞 시험이 같은 자리 타일을 판 짓기 전에 구워 뒀으면 바닐라 Buildtiles가 다시 굽기(찔린 뒤 5초)까지 옛 땅 길(5m)을 줌(회차 744·769·772 housetest 뒤 FAIL, 단독 PASS) → 계단 길이 날 때까지
                yield return TestKit.WaitFor(() => (stairLen = ai.FullPathLength(start, goal)) > Tiles * 2f, 20f);
                if (leg == 0) stairT = stairLen / Mathf.Max(w.Ragnar.m_runSpeed, 0.5f);
                if (leg == 0) checks.Add($"판 위→아래 목표 계단 길 남 ({stairLen:0.0}m, {TestKit.Waited:0.0}초)", stairLen > Tiles * 2f);
                int drop0 = ai.DropsTaken, quit0 = ai.DropsQuit;
                var walk = new ExploreTest.WalkJob(ai, goal) { Stay = true };
                ai.StartJob(walk);
                float maxF = float.MinValue;
                yield return TestKit.WaitFor(() =>
                {
                    Vector3 p = w.Ragnar.transform.position;
                    maxF = Mathf.Max(maxF, Vector3.Dot(p - near, f));
                    return Utils.DistanceXZ(p, goal) < 2.8f && Mathf.Abs(p.y - goal.y) < 1.3f && w.Ragnar.IsOnGround() && !ai.Dropping; // 떨어지는 중 목표 곁을 지나면 안 침(회차 735 첫 실행)
                }, 90f);
                took[leg] = TestKit.Waited;
                Vector3 end = w.Ragnar.transform.position;
                got[leg] = Utils.DistanceXZ(end, goal) < 2.8f && Mathf.Abs(end.y - goal.y) < 1.3f;
                walk.Halt = true;
                int drops = ai.DropsTaken - drop0, quits = ai.DropsQuit - quit0;
                if (leg == 0)
                    checks.Add($"판 위에서 뛰어내려 아래 목표에 닿음 ({took[0]:0.0}초, 착지 {drops}, 그만 {quits}, 끝 {end:F1}·목표 {goal:F1})", got[0] && drops >= 1 && quits == 0);
                else
                    TestRunner.Log($"끄고 같은 다리: {took[1]:0.0}초, 닿음 {got[1]}, 착지 {drops}, 판 끝 너머 {maxF - Tiles * 2f:0.0}m — 참고만(회차 735 한 번은 길이 덜 구워져 1.5초에 판 끝에서 걸어 떨어짐)");
                yield return new WaitForSeconds(1f);
            }
            WorkerAI.TestNoDrop = false;
            checks.Add($"뛰어내리기가 계단 길보다 1초 넘게 빠름 (뜀 {took[0]:0.0}초 · 계단 길 뛰어 {stairT:0.0}초)", got[0] && took[0] + 1f < stairT);
            GatherTest.Say("!멈춰", null);
        }

        /// <summary>계단 한 칸의 f축 높은 끝·낮은 끝(위에서 쏴 잼 — HouseTest 4-4 Profile과 같음).</summary>
        static bool Profile(GameObject st, Vector3 f, out Vector3 hi, out Vector3 lo)
        {
            hi = lo = Vector3.zero; bool any = false;
            Physics.SyncTransforms();
            float sy = HouseTest.Box(st).max.y + 0.5f;
            for (float s = -2.5f; s <= 2.5f; s += 0.125f)
            {
                Vector3 o = st.transform.position + f * s; o.y = sy;
                foreach (var h in Physics.RaycastAll(o, Vector3.down, 6f, LayerMask.GetMask("piece")))
                {
                    if (!h.collider.transform.IsChildOf(st.transform)) continue;
                    if (!any || h.point.y > hi.y) hi = h.point;
                    if (!any || h.point.y < lo.y) lo = h.point;
                    any = true;
                }
            }
            return any;
        }
    }
}
