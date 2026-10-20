using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 구덩이 탈출 시험(2026-10-21 사용자 관찰: 점프를 안 해서 돌 캐다 판 구덩이에서 못 나옴).
    /// 깃발 → 1호 제자리 점프 높이 → 바닐라 곡괭이 흙 파기로 거점 둘레에 구덩이 3개(1.0·1.8·2.8m) →
    /// 1호를 구덩이에 넣고 거점으로 걷게 함: 걷기만 8초 → 점프 12초 → 경사로 깎기 90초 순서로 허락하며 언제 나오는지 잰다.
    /// 끝나면 구덩이를 바닐라 평탄화로 메운다.
    /// </summary>
    static class PitTest
    {
        public static void Register() => TestRunner.Register("vh_pittest", "구덩이 탈출 시험(점프·경사로)", Run);

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
            var spots = new List<KeyValuePair<Vector3, float>>();
            TerrainOp level = null;
            try
            {
                GameObject pick = ObjectDB.instance.GetItemPrefab("PickaxeAntler");
                GameObject dig = pick != null ? pick.GetComponent<ItemDrop>().m_itemData.m_shared.m_spawnOnHitTerrain : null;
                TerrainOp digOp = dig != null ? dig.GetComponentInChildren<TerrainOp>() : null;
                checks.Add($"뿔 곡괭이·흙 파기({(dig != null ? dig.name : "없음")}) 있음", digOp != null && BaseFlag.Prefab != null);
                if (digOp == null || BaseFlag.Prefab == null) { Done(checks); yield break; }
                var s = digOp.m_settings;
                TestRunner.Log($"흙 파기: 낮춤 {s.m_raise} 반지름 {s.m_raiseRadius} 깊이 {s.m_raiseDelta} 힘 {s.m_raisePower} 사각 {s.m_square}, 고르기 {s.m_smooth} {s.m_smoothRadius}");
                foreach (var op in ObjectDB.instance.m_terrainOps)
                    if (op != null && op.m_settings.m_level && !op.m_settings.m_raise && (level == null || op.m_settings.m_levelRadius > level.m_settings.m_levelRadius))
                        level = op;
                TestRunner.Log($"평탄화: {(level != null ? $"{level.name} 반지름 {level.m_settings.m_levelRadius} 높이차 {level.m_settings.m_levelOffset}" : "없음")}");

                // 1) 깃발과 NPC
                Workers.DespawnAll();
                yield return new WaitForSeconds(1f);
                Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
                Quaternion rot = player.transform.rotation;
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

                claude.GetInventory().AddItem(pick, 1); // 경사로를 깎을 곡괭이
                float[] depths = { 1.0f, 1.8f, 2.8f };
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.back };
                for (int i = 0; i < depths.Length; i++)
                {
                    Vector3 spot = GatherTest.Ground(home + rot * dirs[i] * 12f);
                    float rim = spot.y;
                    spots.Add(new KeyValuePair<Vector3, float>(spot, rim));
                    // 바닐라 곡괭이 흙 파기를 가운데와 둘레 4곳에 되풀이(플레이어가 광맥을 파 내려간 구덩이처럼)
                    int n = 0;
                    var offsets = new[] { Vector3.zero, Vector3.right * 1.2f, Vector3.left * 1.2f, Vector3.forward * 1.2f, Vector3.back * 1.2f };
                    for (int round = 0; round < 60 && Height(spot) > rim - depths[i]; round++)
                    {
                        foreach (var o in offsets)
                        {
                            Vector3 p = spot + o;
                            p.y = Height(p);
                            Attack.SpawnOnHitTerrain(p, dig, null, 0f, null, null);
                            n++;
                        }
                        yield return new WaitForSeconds(0.15f);
                    }
                    yield return new WaitForSeconds(0.5f);
                    float depth = rim - Height(spot);
                    TestRunner.Log($"구덩이 {i + 1}: 흙 파기 {n}번, 깊이 {depth:0.0}m (목표 {depths[i]:0.0})");

                    claude.TeleportTo(spot + Vector3.up * 0.3f, rot, false);
                    yield return new WaitForSeconds(1.5f);
                    int j0 = claude.Ai.Jumps, c0 = claude.Ai.CarveSwings, d0 = NpcPatches.NoDig.Dug;
                    var walk = new ExploreTest.WalkJob(claude.Ai, home);
                    claude.Ai.StartJob(walk);
                    string outBy = null;
                    float total = 0f;
                    string[] names = { "걷기만", "점프", "경사로" };
                    float[] times = { 8f, 12f, 90f };
                    for (int ph = 0; ph < names.Length && outBy == null; ph++)
                    {
                        WorkerAI.TestNoJump = ph < 1;
                        WorkerAI.TestNoCarve = ph < 2;
                        for (float t = 0f; t < times[ph] && outBy == null; t += Time.deltaTime, total += Time.deltaTime)
                        {
                            Vector3 pos = claude.transform.position;
                            if (Utils.DistanceXZ(spot, pos) > 3.5f && pos.y > rim - 0.5f) outBy = names[ph];
                            yield return null;
                        }
                        if (outBy != null) break;
                    }
                    walk.Stop = true;
                    WorkerAI.TestNoJump = WorkerAI.TestNoCarve = false;
                    TestRunner.Log($"구덩이 {i + 1}({depth:0.0}m): {(outBy != null ? outBy + "로 나옴" : "못 나옴")} {total:0.0}초, " +
                                   $"점프 {claude.Ai.Jumps - j0}번, 경사로 곡괭이질 {claude.Ai.CarveSwings - c0}번, 흙 파기 {NpcPatches.NoDig.Dug - d0}번, 지금 {claude.MoveDebug}");
                    if (i == 0) checks.Add($"얕은 구덩이({depth:0.0}m)는 점프까지로 나옴", outBy == "걷기만" || outBy == "점프");
                    else checks.Add($"구덩이 {i + 1}({depth:0.0}m)에서 나옴", outBy != null);
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
            // 3) 메우기(시험 월드의 다른 시험이 구덩이에 빠지지 않게)
            if (level != null)
            {
                foreach (var kv in spots)
                {
                    float r = Mathf.Max(1f, level.m_settings.m_levelRadius);
                    for (float x = -6f; x <= 6f; x += r)
                        for (float z = -6f; z <= 6f; z += r)
                        {
                            Vector3 p = kv.Key + new Vector3(x, 0f, z);
                            p.y = kv.Value - level.m_settings.m_levelOffset;
                            Attack.SpawnOnHitTerrain(p, level.gameObject, null, 0f, null, null);
                        }
                    yield return new WaitForSeconds(0.3f);
                    TestRunner.Log($"메움: 가운데 높이 차 {Height(kv.Key) - kv.Value:0.0}m");
                }
            }
            Done(checks);
        }

        static void Done(GatherTest.Checks checks)
        {
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }

        static float Height(Vector3 p) => ZoneSystem.instance.GetGroundHeight(p, out float h) ? h : p.y;
    }
}
