using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 구덩이 탈출 시험(2026-10-21 사용자 관찰: 점프를 안 해서 돌 캐다 판 구덩이에서 못 나옴).
    /// 깃발 → 1호 제자리 점프 높이 → 바닐라 곡괭이 흙 파기(digg_v3)로 거점 둘레에 구덩이 5개
    /// (넓은 것 1.0·1.8·2.8m = 둘레 5곳을 파 내려감, 좁은 것 2.0·3.2m = 한 곳만 파 내려간 광맥 구덩이처럼) →
    /// 1호를 구덩이에 넣고 거점으로 걷게 함: 걷기만 5초 → 점프 10초 → 경사로 깎기 75초 순서로 허락하며 언제 나오는지 잰다.
    /// 시험 앞뒤로 그 둘레 지형을 원래대로 되돌린다(TestTerrain).
    /// </summary>
    static class PitTest
    {
        public static void Register() => TestRunner.Register("vh_pittest", "구덩이 탈출 시험(점프·경사로)", Run);

        static readonly float[] Depths = { 1.0f, 1.8f, 2.8f, 2.0f, 3.2f };
        static readonly bool[] Narrow = { false, false, false, true, true };
        static readonly Vector3[] Dirs =
        {
            Vector3.right, Vector3.left, Vector3.back, new Vector3(0.7f, 0f, -0.7f), new Vector3(-0.7f, 0f, -0.7f),
        };
        static readonly string[] Phases = { "걷기만", "점프", "경사로" };
        static readonly float[] PhaseTimes = { 5f, 10f, 75f };

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            bool god = player.InGodMode();
            player.SetGodMode(true);
            var spots = new List<Vector3>();
            try
            {
                GameObject pick = ObjectDB.instance.GetItemPrefab("PickaxeAntler");
                GameObject dig = pick != null ? pick.GetComponent<ItemDrop>().m_itemData.m_shared.m_spawnOnHitTerrain : null;
                TerrainOp digOp = dig != null ? dig.GetComponentInChildren<TerrainOp>() : null;
                checks.Add($"뿔 곡괭이·흙 파기({(dig != null ? dig.name : "없음")}) 있음", digOp != null && BaseFlag.Prefab != null);
                if (digOp == null || BaseFlag.Prefab == null) { Done(checks); yield break; }
                var s = digOp.m_settings;
                TestRunner.Log($"흙 파기: 낮춤 {s.m_raise} 반지름 {s.m_raiseRadius} 깊이 {s.m_raiseDelta} 힘 {s.m_raisePower} 사각 {s.m_square}");

                // 1) 깃발과 NPC
                Workers.DespawnAll();
                yield return new WaitForSeconds(1f);
                Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
                Quaternion rot = player.transform.rotation;
                for (int i = 0; i < Depths.Length; i++) spots.Add(home + rot * Dirs[i] * 12f);
                int undone = 0;
                foreach (var sp in spots) undone += TestTerrain.Reset(sp, 9f); // 지난 시험의 구덩이
                if (undone > 0) TestRunner.Log($"지난 구덩이 되돌림: 꼭짓점 {undone}개");
                player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
                yield return new WaitForSeconds(3f);
                WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Scv);
                checks.Add("깃발에 NPC 나타남", claude != null);
                if (claude == null) { Done(checks); yield break; }
                var col = claude.GetComponent<CapsuleCollider>();
                TestRunner.Log($"몸: 점프 힘 {claude.m_jumpForce}, 앞 {claude.m_jumpForceForward}, 공중 조작 {claude.m_airControl}, 걷기 {claude.m_walkSpeed}/달리기 {claude.m_runSpeed}, " +
                               $"충돌체 반지름 {(col != null ? col.radius : -1f):0.00}/높이 {(col != null ? col.height : -1f):0.00}");

                // 2) 제자리 점프 높이
                float y0 = claude.transform.position.y, top = y0;
                claude.Jump();
                for (float t = 0f; t < 2f; t += Time.deltaTime)
                {
                    top = Mathf.Max(top, claude.transform.position.y);
                    yield return null;
                }
                checks.Add($"제자리 점프 높이 {top - y0:0.00}m", top - y0 > 0.5f);

                // 3) 구덩이마다 넣어 보기
                claude.GetInventory().AddItem(pick, 1); // 경사로를 깎을 곡괭이
                for (int i = 0; i < Depths.Length; i++)
                {
                    Vector3 spot = spots[i];
                    spot.y = TestTerrain.Height(spot);
                    float rim = spot.y;
                    int n = 0;
                    var offsets = Narrow[i] ? new[] { Vector3.zero }
                        : new[] { Vector3.zero, Vector3.right * 1.2f, Vector3.left * 1.2f, Vector3.forward * 1.2f, Vector3.back * 1.2f };
                    for (int round = 0; round < 60 && TestTerrain.Height(spot) > rim - Depths[i]; round++)
                    {
                        foreach (var o in offsets)
                        {
                            Vector3 p = spot + o;
                            p.y = TestTerrain.Height(p);
                            Attack.SpawnOnHitTerrain(p, dig, null, 0f, null, null);
                            n++;
                        }
                        yield return new WaitForSeconds(0.15f);
                    }
                    yield return new WaitForSeconds(0.5f);
                    float depth = rim - TestTerrain.Height(spot);
                    string kind = Narrow[i] ? "좁은" : "넓은";
                    TestRunner.Log($"구덩이 {i + 1}({kind}): 흙 파기 {n}번, 깊이 {depth:0.0}m (목표 {Depths[i]:0.0}), 1m 밖 {rim - TestTerrain.Height(spot + Vector3.right):0.0}m·2m 밖 {rim - TestTerrain.Height(spot + Vector3.right * 2f):0.0}m 깊이");

                    claude.TeleportTo(spot + Vector3.up * 0.3f, rot, false);
                    yield return new WaitForSeconds(1.5f);
                    int j0 = claude.Ai.Jumps, c0 = claude.Ai.CarveSwings, d0 = NpcPatches.NoDig.Dug;
                    var walk = new ExploreTest.WalkJob(claude.Ai, home);
                    claude.Ai.StartJob(walk);
                    string outBy = null;
                    float total = 0f;
                    for (int ph = 0; ph < Phases.Length && outBy == null; ph++)
                    {
                        WorkerAI.TestNoJump = ph < 1;
                        WorkerAI.TestNoCarve = ph < 2;
                        float logTimer = 0f;
                        for (float t = 0f; t < PhaseTimes[ph] && outBy == null; t += Time.deltaTime, total += Time.deltaTime)
                        {
                            Vector3 pos = claude.transform.position;
                            if (Utils.DistanceXZ(spot, pos) > 3.5f && pos.y > rim - 0.5f) outBy = Phases[ph];
                            logTimer -= Time.deltaTime;
                            if (logTimer <= 0f)
                            {
                                logTimer = 2f;
                                Plugin.Log.LogInfo($"  [{Phases[ph]} {t:0}s] 가운데에서 {Utils.DistanceXZ(spot, pos):0.0}m, 위 {pos.y - rim:0.0}m, 속도 {claude.GetVelocity().magnitude:0.0}, {claude.MoveDebug} / {claude.Ai.MoveState}");
                            }
                            yield return null;
                        }
                    }
                    walk.Stop = true;
                    WorkerAI.TestNoJump = WorkerAI.TestNoCarve = false;
                    TestRunner.Log($"구덩이 {i + 1}({kind} {depth:0.0}m): {(outBy != null ? outBy + "(으)로 나옴" : "못 나옴")} {total:0.0}초, " +
                                   $"점프 {claude.Ai.Jumps - j0}번, 경사로 곡괭이질 {claude.Ai.CarveSwings - c0}번, 흙 파기 {NpcPatches.NoDig.Dug - d0}번, 지금 {claude.MoveDebug}");
                    if (i == 0) checks.Add($"얕은 구덩이({depth:0.0}m)는 점프까지로 나옴", outBy == Phases[0] || outBy == Phases[1]);
                    else checks.Add($"{kind} 구덩이 {i + 1}({depth:0.0}m)에서 나옴", outBy != null);
                    yield return new WaitForSeconds(1f);
                    claude.Ai.GoHome(false);
                    yield return new WaitForSeconds(1f);
                }
                TestRunner.Log($"일꾼 흙 파기: 판 {NpcPatches.NoDig.Dug}번, 막음 {NpcPatches.NoDig.Blocked}번");
            }
            finally
            {
                WorkerAI.TestNoJump = WorkerAI.TestNoCarve = false;
                player.SetGodMode(god);
            }
            // 4) 되돌리기(시험 월드의 다른 시험이 구덩이에 빠지지 않게)
            int back = 0;
            foreach (var sp in spots) back += TestTerrain.Reset(sp, 9f);
            yield return new WaitForSeconds(0.5f);
            float worst = 0f;
            foreach (var sp in spots) worst = Mathf.Max(worst, Mathf.Abs(TestTerrain.Height(sp) - WorldGenerator.instance.GetHeight(sp.x, sp.z)));
            checks.Add($"시험 구덩이 되돌림(꼭짓점 {back}개, 원래 땅과 가장 큰 차 {worst:0.0}m)", back > 0 && worst < 0.5f);
            Done(checks);
        }

        static void Done(GatherTest.Checks checks)
        {
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
