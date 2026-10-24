using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 콘솔 명령 vh_fartest: 먼 구역 유지 시험 (구상서 16장).
    /// 멧돼지를 소환해 추적하고, 플레이어를 약 1km 떨어진 땅으로 보낸 뒤 60초 동안
    /// 멧돼지가 계속 불러와져 있고(인스턴스), 호스트가 소유하고, 움직이는지(AI 동작) 본다.
    /// 그다음 대조 시험으로 패치를 끄고 10초 뒤 멧돼지가 사라지는지 본다.
    /// 결과는 콘솔, BepInEx 로그, 플러그인 폴더의 diag.txt에 남는다.
    /// </summary>
    static class FarZoneTest
    {
        public static void Register()
        {
            TestRunner.Register("vh_fartest", "먼 구역 유지 시험", Run);
        }

        static void Log(Terminal t, string line) => TestRunner.Log(line);

        static void Finish() => TestRunner.Finish();

        static IEnumerator Run()
        {
            Terminal t = null;
            Player player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                Log(t, "FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                Finish();
                yield break;
            }

            Vector3 home = player.transform.position;
            Quaternion rot = player.transform.rotation;
            bool god = player.InGodMode();
            player.SetGodMode(true);
            // 앞 시험의 일꾼이 남아 있으면 소환한 멧돼지를 바로 잡아 버린다(회차 91: stucktest 뒤 "멧돼지 데이터 없음" FAIL)
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);

            GameObject prefab = ZNetScene.instance.GetPrefab("Boar");
            // 앞 4m는 앞 시험의 거점 깃발·상자 자리(TestKit.Setup 거점 = 플레이어 앞 4m) → 둘레가 빈 땅에 소환(회차 141: 깃발 기둥 곁에서 60초 동안 걷기 1인데 0.1m)
            Vector3 spawn = ClearSpot(home, player.transform.forward);
            Character boar = Object.Instantiate(prefab, spawn + Vector3.up, Quaternion.identity).GetComponent<Character>();
            ZDOID id = boar.GetZDOID();
            yield return new WaitForSeconds(1f);
            FarZones.Enabled = true;
            FarZones.Track(id);
            Log(t, $"멧돼지 소환 {Fmt(spawn)} (플레이어에서 {Utils.DistanceXZ(spawn, home):0}m), 추적 시작");

            if (!FindFarLand(home, out Vector3 far))
            {
                Log(t, "FAIL: 주변 1km 안팎에 땅이 없습니다. 다른 곳에서 다시 해 주세요.");
                Cleanup(player, id, home, rot, god, false);
                Finish();
                yield break;
            }
            ZDO z0 = ZDOMan.instance.GetZDO(id);
            if (z0 != null) Log(t, $"떠나기 전 {AiDiag(z0, boar.gameObject)}, 둘레 1.5m [{Around(boar)}]");
            player.TeleportTo(far, rot, true);
            Log(t, $"플레이어 이동 → {Fmt(far)} (거리 {Vector3.Distance(home, far):0}m)");
            float wait = 0f;
            yield return new WaitForSeconds(1f);
            while (player.IsTeleporting() && wait < 40f) { wait += 0.5f; yield return new WaitForSeconds(0.5f); }

            // 1) 패치 켬: 60초 관찰. 혼자 둔 멧돼지는 25~37초마다 몇 m만 걷고(BaseAI.RandomMovement·m_randomMoveInterval) 그 자리로 길을 못 찾으면
            //    그 차례를 건너뛴다(BaseAI.MoveTo: FindPath 실패 → 도착 처리) → 나무 많은 검은숲에선 60초 내내 안 움직이기도 함(회차 141, 0.2m).
            //    그래서 따라갈 표지를 12m 앞에 두어 AI가 계속 걷게 한다(MonsterAI.UpdateAI → 목표 없으면 Follow → MoveTo). 닿거나 10초 못 다가가면 방향을 바꿈.
            var marker = new GameObject("vh_fartest_follow");
            int turn = 0, stall = 0;
            float lastGap = float.MaxValue;
            int ok = 0, samples = 12;
            Vector3? first = null, prev = null;
            float moved = 0f, minDist = float.MaxValue;
            Steer(id, marker, ref turn, ref stall, ref lastGap);
            for (int i = 0; i < samples; i++)
            {
                yield return new WaitForSeconds(5f);
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null) { Log(t, $"[{(i + 1) * 5}s] 멧돼지 데이터 없음(죽었거나 사라짐)"); continue; }
                Vector3 pos = zdo.GetPosition();
                bool inst = ZNetScene.instance.FindInstance(zdo) != null;
                bool owner = zdo.GetOwner() == ZDOMan.GetSessionID();
                bool loaded = ZoneSystem.instance.IsZoneLoaded(pos);
                float dist = Vector3.Distance(player.transform.position, pos);
                minDist = Mathf.Min(minDist, dist);
                if (first == null) first = pos;
                moved = Mathf.Max(moved, Vector3.Distance(first.Value, pos));
                if (inst && owner && loaded) ok++;
                Log(t, $"[{(i + 1) * 5}s] 인스턴스 {inst}, 호스트 소유 {owner}, 구역 {loaded}, 플레이어와 {dist:0}m, 위치 {Fmt(pos)}, {AiDiag(zdo, marker)}"
                    + (prev.HasValue && Vector3.Distance(prev.Value, pos) < 0.3f && ZNetScene.instance.FindInstance(zdo)?.GetComponent<Character>() is Character bc ? $", 그대로 — 둘레 1.5m [{Around(bc)}]" : ""));
                prev = pos;
                Steer(id, marker, ref turn, ref stall, ref lastGap);
            }
            ZDO zEnd = ZDOMan.instance.GetZDO(id);
            MonsterAI endAi = zEnd != null ? ZNetScene.instance.FindInstance(zEnd)?.GetComponent<MonsterAI>() : null;
            if (endAi != null) endAi.SetFollowTarget(null);
            Object.Destroy(marker);
            bool farEnough = minDist > 300f;
            bool alive = ok == samples;
            bool aiRuns = moved > 1f;
            Log(t, $"켬 결과: 유지 {ok}/{samples}, 최소 거리 {minDist:0}m, 최대 이동 {moved:0.0}m, 표지 옮김 {turn}번");

            // 2) 대조: 패치 끔 → 사라져야 정상(패치 덕분에 유지됐다는 증거)
            FarZones.Enabled = false;
            yield return new WaitForSeconds(10f);
            ZDO z2 = ZDOMan.instance.GetZDO(id);
            bool goneWhenOff = z2 == null || ZNetScene.instance.FindInstance(z2) == null;
            Log(t, $"끔 대조: 10초 뒤 인스턴스 사라짐 {goneWhenOff}");
            FarZones.Enabled = true;

            // 판정 줄은 다른 시험처럼 PASS/FAIL/환경 불량(요약이 읽음), FAIL이면 이유 줄을 먼저(요약의 첫 FAIL 줄)
            if (farEnough && !alive) Log(t, $"FAIL 먼 구역에서 멧돼지 유지 ({ok}/{samples})");
            if (farEnough && alive && !aiRuns) Log(t, $"FAIL 유지는 되지만 멧돼지가 안 움직임 (최대 이동 {moved:0.0}m)");
            if (farEnough && !goneWhenOff) Log(t, "FAIL 패치 끄면 사라짐 (대조)");
            string verdict = !farEnough ? $"환경 불량 (충분히 멀어지지 않음, 최소 {minDist:0}m)"
                : alive && aiRuns && goneWhenOff ? "PASS" : "FAIL";
            Log(t, $"판정: {verdict} (유지 {alive}, AI 동작 {aiRuns}, 끔 대조 {goneWhenOff})");

            Cleanup(player, id, home, rot, god, true);
            Log(t, "집으로 돌아갑니다. 시험 끝.");
            Finish();
        }

        static void Cleanup(Player player, ZDOID id, Vector3 home, Quaternion rot, bool god, bool teleport)
        {
            FarZones.Untrack(id);
            // 멧돼지를 지운다: 남기면 플레이어가 돌아올 때 거점에 다시 생겨 다음 시험의 갓 생긴 일꾼(음식 전)과 싸움(회차 141: 플로키가 멧돼지에 죽음)
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null)
            {
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view != null) { view.ClaimOwnership(); ZNetScene.instance.Destroy(view.gameObject); }
                else { zdo.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(zdo); }
            }
            if (teleport) player.TeleportTo(home, rot, true);
            player.SetGodMode(god);
        }

        /// <summary>집에서 약 1km(없으면 0.7·1.5km) 떨어진 물 위가 아닌 땅.</summary>
        static bool FindFarLand(Vector3 home, out Vector3 far)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            foreach (float d in new[] { 1000f, 700f, 1500f })
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.PI / 4f;
                    Vector3 p = home + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * d;
                    if (new Vector2(p.x, p.z).magnitude > 9500f) continue;
                    float h = WorldGenerator.instance.GetHeight(p.x, p.z);
                    if (h < water + 3f) continue;
                    p.y = h + 1.5f;
                    far = p;
                    return true;
                }
            far = default;
            return false;
        }

        static string Fmt(Vector3 v) => $"({v.x:0},{v.y:0},{v.z:0})";

        static readonly AccessTools.FieldRef<BaseAI, bool> PathResult = AccessTools.FieldRefAccess<BaseAI, bool>("m_lastFindPathResult");
        static readonly AccessTools.FieldRef<Character, Vector3> MoveDir = AccessTools.FieldRefAccess<Character, Vector3>("m_moveDir");

        /// <summary>
        /// 멧돼지가 따라갈 표지를 둔다: 표지를 안 따르면(새 인스턴스 등) 다시 붙이고, 표지에 닿았거나(4m) 두 번(10초) 못 다가갔으면
        /// 지금 자리에서 다음 방향(90°씩) 12m 땅으로 옮긴다.
        /// </summary>
        static void Steer(ZDOID id, GameObject marker, ref int turn, ref int stall, ref float lastGap)
        {
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            MonsterAI ai = zdo != null ? ZNetScene.instance.FindInstance(zdo)?.GetComponent<MonsterAI>() : null;
            if (ai == null) return;
            Vector3 p = ai.transform.position;
            float gap = Utils.DistanceXZ(marker.transform.position, p);
            stall = gap > lastGap - 0.5f ? stall + 1 : 0;
            if (ai.GetFollowTarget() != marker || gap < 4f || stall >= 2)
            {
                Vector3 q = p + Quaternion.Euler(0f, turn * 90f, 0f) * Vector3.forward * 12f;
                if (ZoneSystem.instance.GetSolidHeight(q, out float h)) q.y = h;
                marker.transform.position = q;
                ai.SetFollowTarget(marker);
                turn++;
                stall = 0;
                gap = Utils.DistanceXZ(q, p);
            }
            lastGap = gap;
        }

        static readonly AccessTools.FieldRef<Character, Animator> Anim = AccessTools.FieldRefAccess<Character, Animator>("m_animator");
        static readonly int FreezeTag = Animator.StringToHash("freeze");

        /// <summary>플레이어 둘레 8·12m에서 1.5m 안에 땅 말고 충돌체가 없는 자리(없으면 앞 8m).</summary>
        static Vector3 ClearSpot(Vector3 home, Vector3 forward)
        {
            foreach (float r in new[] { 8f, 12f })
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = home + Quaternion.Euler(0f, k * 45f, 0f) * forward * r;
                    if (!ZoneSystem.instance.GetSolidHeight(p, out float h)) continue;
                    p.y = h;
                    bool clear = true;
                    foreach (var col in Physics.OverlapSphere(p + Vector3.up * 1.2f, 1.5f))
                        if (!col.isTrigger && col.GetComponentInParent<Heightmap>() == null) { clear = false; break; }
                    if (clear && Mathf.Abs(ZoneSystem.instance.GetGroundHeight(p) - h) < 0.3f) return p; // 땅 위(바위·건물 위 아님)
                }
            Vector3 f = home + forward * 8f;
            if (ZoneSystem.instance.GetSolidHeight(f, out float fh)) f.y = fh;
            return f;
        }

        /// <summary>진단: 멧돼지 몸 둘레 1.5m의 남의 충돌체(끼었는지).</summary>
        static string Around(Character c)
        {
            var names = new List<string>();
            foreach (var col in Physics.OverlapSphere(c.GetCenterPoint(), 1.5f))
            {
                if (col.isTrigger || col.transform.IsChildOf(c.transform)) continue;
                Transform root = col.attachedRigidbody != null ? col.attachedRigidbody.transform : col.transform.root;
                names.Add($"{Utils.GetPrefabName(root.gameObject)}:{col.name}");
            }
            return string.Join(", ", names.ToArray());
        }

        static string AiDiag(ZDO zdo, GameObject marker)
        {
            ZNetView v = ZNetScene.instance.FindInstance(zdo);
            var ai = v != null ? v.GetComponent<MonsterAI>() : null;
            if (ai == null) return "AI 없음";
            Vector3 p = ai.transform.position;
            Character c = ai.GetComponent<Character>();
            Character target = ai.GetTargetCreature();
            Animator an = Anim(c);
            string anim = an == null ? "없음" : $"{an.enabled}/{an.cullingMode}, 상태 {an.GetCurrentAnimatorStateInfo(0).shortNameHash} 얼림 {an.GetCurrentAnimatorStateInfo(0).tagHash == FreezeTag}, 다음 얼림 {an.IsInTransition(0) && an.GetNextAnimatorStateInfo(0).tagHash == FreezeTag}";
            bool seen = false;
            foreach (var r in c.GetComponentsInChildren<Renderer>()) seen |= r.isVisible;
            Rigidbody body = c.GetComponent<Rigidbody>();
            return $"표지까지 {Utils.DistanceXZ(marker.transform.position, p):0.0}m, 목표 {(target != null ? Utils.GetPrefabName(target.gameObject) : "없음")}, "
                + $"길 {PathResult(ai)}, 걷기 {MoveDir(c).magnitude:0.00}, 움직임 가능 {c.CanMove()} 비틀 {c.IsStaggering()}, 애니 {anim}, 보임 {seen}, "
                + $"땅 {c.IsOnGround()}, 속도 {(body != null ? body.linearVelocity.magnitude : -1f):0.00}, 잠듦 {(body != null && body.IsSleeping())}";
        }
    }
}
