using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 구덩이 탈출 시험(2026-10-21 사용자 관찰: 점프를 안 해서 돌 캐다 판 구덩이에서 못 나옴).
    /// 깃발 → 1호 제자리 점프 높이 → 바닐라 곡괭이 흙 파기(digg_v3)로 거점 둘레에 구덩이 5개
    /// (넓은 것 1.0·1.8·2.8m = 둘레 5곳을 파 내려감, 좁은 것 2.0·3.2·5.0m = 한 곳만 파 내려간 광맥 구덩이처럼, 5m는 점프로 못 나와 경사로) →
    /// 1호를 구덩이에 넣고 거점으로 걷게 함: 걷기만 5초 → 점프 10초 → 경사로 깎기 75초 순서로 허락하며 언제 나오는지 잰다.
    /// 시험 앞뒤로 그 둘레 지형을 원래대로 되돌린다(TestTerrain).
    /// </summary>
    static class PitTest
    {
        public static void Register() => TestRunner.Register("vh_pittest", "구덩이 탈출 시험(점프·경사로)", Run);

        static readonly float[] Depths = { 1.0f, 1.8f, 2.8f, 2.0f, 3.2f, 5.0f, 4.2f }; // 7은 3.2m면 원뿔 벽이 얕아 깎지 않고 걸어 나올 때가 있음(루프 회차 4)
        static readonly bool[] Narrow = { false, false, false, true, true, true, true };
        /// <summary>점프를 끄고 경사로 깎기만으로 나오게 하는 구덩이(깎기 시험).</summary>
        static readonly bool[] CarveOnly = { false, false, false, false, false, false, true };
        static readonly Vector3[] Dirs =
        {
            Vector3.right, Vector3.left, Vector3.back, new Vector3(0.7f, 0f, -0.7f), new Vector3(-0.7f, 0f, -0.7f), Vector3.back * 1.6f, Vector3.left * 1.6f,
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
            Vector3? startPos = null; // 마른 땅으로 옮겼으면 끝나고 돌아갈 자리
            var rims = new float[Depths.Length]; // 파기 전 높이(되돌림 확인용; 원래 땅 WorldGenerator와는 곳에 따라 0.8m까지 다름)
            var dug = new bool[Depths.Length];
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
                Quaternion rot = player.transform.rotation;
                // 구덩이 바닥이 물 높이 아래면 물이 차 헤엄으로 바뀐다(묶음 1 회귀에서 물가에서 돌아 3개 FAIL) → 마른 땅으로 옮긴다
                bool found = FindDryHome(player.transform.position, rot, 0.8f, out Vector3 dry) || FindDryHome(player.transform.position, rot, 99f, out dry);
                if (!found) TestRunner.Log("마른 땅 못 찾음(300m 안) → 제자리에서");
                if (found && Utils.DistanceXZ(dry, player.transform.position) > 1f)
                {
                    startPos = player.transform.position;
                    player.TeleportTo(dry + Vector3.up, rot, true);
                    float wait = 0f;
                    yield return new WaitForSeconds(1f);
                    while (player.IsTeleporting() && wait < 40f) { wait += 0.5f; yield return new WaitForSeconds(0.5f); }
                    yield return new WaitForSeconds(2f);
                    TestRunner.Log($"마른 땅으로 옮김: {Utils.DistanceXZ(dry, startPos.Value):0}m, 높이 {dry.y:0.0} (물 {ZoneSystem.instance.m_waterLevel:0.0})");
                }
                Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
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
                    rims[i] = rim;
                    dug[i] = true;
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

                    // 근처 몬스터 치우고 체력 채움(루프 회차 4: 130m 옮긴 자리에 트롤이 와 구덩이 7에서 도망만 하다 끝남)
                    int cleared = ClearMonsters(spot, 80f);
                    if (cleared > 0) TestRunner.Log($"구덩이 {i + 1}: 근처 몬스터 {cleared}마리 치움");
                    claude.SetHealth(claude.GetMaxHealth());
                    // 바닥에 내려놓는다(구덩이 위에서 떨어뜨리면 좁은 구덩이 벽에 끼었다가 튕겨 나감, 0.3.8)
                    claude.TeleportTo(new Vector3(spot.x, TestTerrain.Height(spot) + 0.3f, spot.z), rot, false);
                    yield return new WaitForSeconds(1.5f);
                    int j0 = claude.Ai.Jumps, c0 = claude.Ai.CarveSwings, d0 = NpcPatches.NoDig.Dug;
                    var walk = new ExploreTest.WalkJob(claude.Ai, home);
                    claude.Ai.StartJob(walk);
                    string outBy = null;
                    float total = 0f;
                    for (int ph = 0; ph < Phases.Length && outBy == null; ph++)
                    {
                        if (CarveOnly[i] && ph == 1) continue; // 점프 없이 바로 경사로
                        WorkerAI.TestNoJump = ph < 1 || CarveOnly[i];
                        WorkerAI.TestNoCarve = ph < 2;
                        float logTimer = 0f;
                        for (float t = 0f; t < PhaseTimes[ph] && outBy == null; t += Time.deltaTime, total += Time.deltaTime)
                        {
                            Vector3 pos = claude.transform.position;
                            // 5m 밖이면 높이와 상관없이 나온 것(흙 파기 반지름 1.5 + 넓은 구덩이 1.2 → 구덩이는 3m 안쪽).
                            // 비탈이면 깃발이 구덩이 둘레보다 낮아 높이 조건만으로는 깃발에 돌아가도 못 나온 걸로 봄(루프 회차 4, 구덩이 4: 둘레보다 2.4m 낮음)
                            float dist = Utils.DistanceXZ(spot, pos);
                            if (dist > 5f || (dist > 3.5f && pos.y > rim - 0.5f)) outBy = Phases[ph];
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
                    else if (CarveOnly[i]) checks.Add($"점프 없이 경사로를 깎아 구덩이 {i + 1}({depth:0.0}m)에서 나옴", outBy == Phases[2] && claude.Ai.CarveSwings > c0);
                    else checks.Add($"{kind} 구덩이 {i + 1}({depth:0.0}m)에서 나옴", outBy != null);
                    yield return new WaitForSeconds(1f);
                    claude.Ai.GoHome(false);
                    yield return new WaitForSeconds(1f);
                }
                TestRunner.Log($"일꾼 흙 파기: 판 {NpcPatches.NoDig.Dug}번, 막음 {NpcPatches.NoDig.Blocked}번");
                yield return FleeTrap(checks, claude, home, rot, dig);
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
            for (int i = 0; i < spots.Count; i++)
                if (dug[i]) worst = Mathf.Max(worst, Mathf.Abs(TestTerrain.Height(spots[i]) - rims[i]));
            checks.Add($"시험 구덩이 되돌림(꼭짓점 {back}개, 파기 전과 가장 큰 차 {worst:0.0}m)", back > 0 && worst < 0.5f);
            if (startPos.HasValue)
            {
                player.TeleportTo(startPos.Value, player.transform.rotation, true);
                float wait = 0f;
                yield return new WaitForSeconds(1f);
                while (player.IsTeleporting() && wait < 40f) { wait += 0.5f; yield return new WaitForSeconds(0.5f); }
            }
            Done(checks);
        }

        /// <summary>
        /// 플레이어 둘레 300m 안 초원에서 깃발·구덩이 자리 모두가 (구덩이 깊이 + 2m)보다 물 위에 있고 평평한 곳.
        /// WorldGenerator 높이라 불러오지 않은 곳도 본다(실제 땅과 0.8m까지 다름 → 2m 여유).
        /// </summary>
        static bool FindDryHome(Vector3 from, Quaternion rot, float flat, out Vector3 home)
        {
            WorldGenerator wg = WorldGenerator.instance;
            float wl = ZoneSystem.instance.m_waterLevel;
            for (float r = 0f; r <= 300f; r += 10f)
                for (int d = 0; d < (r == 0f ? 1 : 16); d++)
                {
                    float a = d * Mathf.PI / 8f;
                    Vector3 p = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    Vector3 h = p + rot * Vector3.forward * 4f; // 깃발 자리(아래 home과 같게)
                    float hh = wg.GetHeight(h.x, h.z);
                    bool ok = wg.GetBiome(h) == Heightmap.Biome.Meadows && hh > wl + 2f;
                    for (int i = 0; i < Depths.Length && ok; i++)
                    {
                        Vector3 s = h + rot * Dirs[i] * 12f;
                        float sh = wg.GetHeight(s.x, s.z);
                        ok = sh > wl + Depths[i] + 2f && Mathf.Abs(sh - hh) < 3f;
                        // 구덩이 둘레 3m가 평평해야 한다(비탈이면 좁은 구덩이 한쪽이 얕아 깎지 않고 걸어 나옴 — 루프 회차 4, 구덩이 7)
                        for (int k = 0; k < 8 && ok && Narrow[i]; k++)
                        {
                            float b = k * Mathf.PI / 4f;
                            ok = Mathf.Abs(wg.GetHeight(s.x + Mathf.Cos(b) * 3f, s.z + Mathf.Sin(b) * 3f) - sh) < flat;
                        }
                    }
                    if (ok) { home = new Vector3(p.x, wg.GetHeight(p.x, p.z), p.z); return true; }
                }
            home = from;
            return false;
        }

        /// <summary>
        /// 도망 중 구덩이(사용자 결정 2026-10-22): 위 구덩이와 겹치지 않는 자리(거점 20m)에 좁고 깊은(4m) 구덩이를 따로 파고 바닥에 넣은 뒤,
        /// 점프·깎기를 끈 채 도망을 시키면 6초쯤 못 벗어난 뒤 깃발로 순간이동하는지(짐 그대로, 도망 그만둠). 끝나면 그 자리 지형을 되돌린다.
        /// (넓은 구덩이는 비탈이라 걸어 나옴 — 회차 60 첫 시도.) 걷기(일)로는 이 구조가 안 붙는 것은 위 구덩이 칸들이 본다.
        /// </summary>
        static IEnumerator FleeTrap(GatherTest.Checks checks, WorkerNpc claude, Vector3 home, Quaternion rot, GameObject dig)
        {
            Vector3 spot = Vector3.zero;
            float depth = 0f;
            var tried = new List<Vector3>();
            foreach (float ang in new[] { 20f, 110f, 200f, 290f })
            {
                Vector3 p0 = home + Quaternion.Euler(0f, ang, 0f) * rot * Vector3.forward * 20f;
                p0.y = TestTerrain.Height(p0);
                float rim = p0.y;
                tried.Add(p0);
                for (int round = 0; round < 40 && TestTerrain.Height(p0) > rim - 4f; round++)
                {
                    Vector3 p = p0;
                    p.y = TestTerrain.Height(p0);
                    Attack.SpawnOnHitTerrain(p, dig, null, 0f, null, null);
                    yield return new WaitForSeconds(0.15f);
                }
                yield return new WaitForSeconds(0.5f);
                depth = rim - TestTerrain.Height(p0);
                TestRunner.Log($"도망 구덩이 자리 {ang:0}°: 깊이 {depth:0.0}m");
                if (depth >= 3f) { spot = p0; break; }
            }
            checks.Add($"도망 시험 구덩이 팜 ({depth:0.0}m)", spot != Vector3.zero);
            if (spot != Vector3.zero)
            {
                ClearMonsters(spot, 80f);
                claude.SetHealth(claude.GetMaxHealth());
                claude.TeleportTo(new Vector3(spot.x, TestTerrain.Height(spot) + 0.3f, spot.z), rot, false);
                yield return new WaitForSeconds(1.5f);
                int bag0 = claude.GetInventory().GetAllItems().Count, r0 = WorkerAI.TrapRescues;
                WorkerAI.TestNoJump = WorkerAI.TestNoCarve = true;
                claude.Ai.Guard.TestFlee(spot + rot * Vector3.forward * 40f);
                float t = 0f, logAt = 0f;
                float rim = spot.y;
                while (t < 20f && WorkerAI.TrapRescues == r0)
                {
                    if (t >= logAt) { logAt += 2f; Vector3 p = claude.transform.position; TestRunner.Log($"  도망 구덩이 {t:0}초: 가운데에서 {Utils.DistanceXZ(p, spot):0.0}m, 둘레보다 {p.y - rim:0.0}m, 경계 {claude.Ai.Guard.StateName}"); }
                    t += Time.deltaTime;
                    yield return null;
                }
                WorkerAI.TestNoJump = WorkerAI.TestNoCarve = false;
                float toHome = Utils.DistanceXZ(claude.transform.position, claude.Home);
                checks.Add($"도망 중 구덩이({depth:0.0}m)에 갇힘 → 깃발로 순간이동 ({t:0.0}초, 깃발과 {toHome:0.0}m)", WorkerAI.TrapRescues > r0 && toHome < 6f);
                checks.Add($"도망은 그만두고 짐 그대로 (경계 {claude.Ai.Guard.StateName}, 짐 {claude.GetInventory().GetAllItems().Count}/{bag0})",
                    !claude.Ai.Guard.Fleeing && claude.GetInventory().GetAllItems().Count == bag0);
                yield return new WaitForSeconds(1f);
            }
            foreach (var p in tried) TestTerrain.Reset(p, 9f);
        }


        static int ClearMonsters(Vector3 at, float radius)
        {
            int n = 0;
            foreach (Character c in new List<Character>(Character.GetAllCharacters()))
            {
                if (c == null || c is Player || c.IsTamed() || c.GetComponent<MonsterAI>() == null || c.GetComponent<WorkerNpc>() != null) continue;
                if (Utils.DistanceXZ(c.transform.position, at) > radius) continue;
                ZNetView nv = c.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) continue;
                nv.ClaimOwnership();
                ZNetScene.instance.Destroy(c.gameObject);
                n++;
            }
            return n;
        }

        static void Done(GatherTest.Checks checks)
        {
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
