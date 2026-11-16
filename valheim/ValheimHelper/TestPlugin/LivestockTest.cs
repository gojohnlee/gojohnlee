using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_livestocktest (사용자 11-09 요청 다 "가축", 첫 조각 = 확인): 가축 프리팹 수치(길들이기·먹이·번식·자람)를 적고,
    /// 야생 멧돼지가 일꾼을 적으로 보는지·일꾼이 멀리 있을 때 떨군 먹이를 먹고 길들이기가 줄어드는지·길들인 멧돼지가 SetFollowTarget(일꾼)으로 따라오는지 본다.
    /// </summary>
    static class LivestockTest
    {
        public static void Register() => TestRunner.Register("vh_livestocktest", "가축 확인(수치·먹이·따라오기)", () => TestKit.Flat(Run()), 600f);

        static readonly string[] Kinds = { "Boar", "Wolf", "Lox", "Hen", "Asksvin" };

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World { House = true };
            var made = new List<GameObject>();
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void LogPrefabs()
        {
            foreach (string k in Kinds)
            {
                var pf = ZNetScene.instance.GetPrefab(k);
                if (pf == null) { TestRunner.Log($"가축 {k}: 프리팹 없음"); continue; }
                var t = pf.GetComponent<Tameable>();
                var ai = pf.GetComponent<BaseAI>();
                var m = ai as MonsterAI;
                var pr = pf.GetComponent<Procreation>();
                var c = pf.GetComponent<Character>();
                string eats = m != null && m.m_consumeItems != null ? string.Join(",", m.m_consumeItems.ConvertAll(i => i != null ? i.name : "?")) : "-";
                string line = $"가축 {k}: AI {(ai != null ? ai.GetType().Name : "-")}, 진영 {(c != null ? c.m_faction.ToString() : "-")}, 체력 {(c != null ? c.m_health : 0):0}";
                if (ai != null) line += $", 시야 {ai.m_viewRange:0} 듣기 {ai.m_hearRange:0}";
                if (m != null) line += $", 경보 {m.m_alertRange:0}, 먹이 찾기 {m.m_consumeSearchRange:0}m/{m.m_consumeSearchInterval:0}초, 먹이 [{eats}]";
                if (t != null) line += $", 길들이기 {t.m_tamingTime:0}초, 배부름 {t.m_fedDuration:0}초, 명령 {t.m_commandable}";
                if (pr != null)
                {
                    var gu = pr.m_offspring != null ? pr.m_offspring.GetComponent<Growup>() : null;
                    line += $", 번식 {pr.m_updateInterval:0}초마다 확률 {pr.m_pregnancyChance:0.##} 사랑 {pr.m_requiredLovePoints} 임신 {pr.m_pregnancyDuration:0}초 최대 {pr.m_maxCreatures}({pr.m_totalCheckRange:0}m) 짝 {pr.m_partnerCheckRange:0}m, 새끼 {(pr.m_offspring != null ? pr.m_offspring.name : "-")} 자람 {(gu != null ? gu.m_growTime : 0):0}초";
                }
                TestRunner.Log(line);
            }
            foreach (string e in new[] { "ChickenEgg", "AsksvinEgg" })
            {
                var pf = ObjectDB.instance.GetItemPrefab(e);
                var eg = pf != null ? pf.GetComponent<EggGrow>() : null;
                if (eg == null) { TestRunner.Log($"알 {e}: EggGrow 없음"); continue; }
                var gu = eg.m_grownPrefab != null ? eg.m_grownPrefab.GetComponent<Growup>() : null;
                TestRunner.Log($"알 {e}: 부화 {eg.m_growTime:0}초, 길듦 {eg.m_tamed}, 나옴 {(eg.m_grownPrefab != null ? eg.m_grownPrefab.name : "-")} 자람 {(gu != null ? gu.m_growTime : 0):0}초→{(gu != null && gu.m_grownPrefab != null ? gu.m_grownPrefab.name : "-")}, 불 {eg.m_requireNearbyFire}, 지붕 {eg.m_requireUnderRoof} 덮임 {eg.m_requireCoverPercentige:0.##}, {eg.m_updateInterval:0}초마다");
            }
            // 야생으로 나오는 종(스폰 목록에 켜진 것)
            var wild = new Dictionary<string, string>();
            foreach (var sl in Resources.FindObjectsOfTypeAll<SpawnSystemList>())
                foreach (var sd in sl.m_spawners)
                    if (sd.m_enabled && sd.m_prefab != null && System.Array.IndexOf(Kinds, sd.m_prefab.name) >= 0)
                        wild[sd.m_prefab.name] = (wild.TryGetValue(sd.m_prefab.name, out var b) ? b + "," : "") + sd.m_biome;
            foreach (string k in Kinds) TestRunner.Log($"야생 {k}: {(wild.TryGetValue(k, out var bs) ? bs : "스폰 목록에 없음")}");
        }

        /// <summary>FAIL 때 짐승이 왜 안 움직였나 가르는 줄: 자리·속도·경보·가장 가까운 산딸기 떨굼.</summary>
        static string BoarState(Character c)
        {
            var ai = c.GetComponent<MonsterAI>();
            float near = -1f;
            foreach (ItemDrop d in JobKit.DropsNear(c.transform.position, 40f))
                if (Utils.GetPrefabName(d.gameObject) == "Raspberry")
                { float dd = Vector3.Distance(d.transform.position, c.transform.position); if (near < 0f || dd < near) near = dd; }
            return $"자리 {c.transform.position:0.0} 속도 {c.GetVelocity().magnitude:0.0} 경보 {ai.IsAlerted()} 주인 {c.IsOwner()} 길듦 {c.IsTamed()} 따라감 {(ai.GetFollowTarget() != null)} 표적 {(ai.GetTargetCreature() != null ? ai.GetTargetCreature().m_name : "-")} 산딸기 {(near < 0f ? "없음" : near.ToString("0.0") + "m")} 땅닿음 {c.IsOnGround()} FPS {1f / Time.smoothDeltaTime:0}";
        }

        static Character Spawn(string prefab, Vector3 at, List<GameObject> made)
        {
            var g = Object.Instantiate(ZNetScene.instance.GetPrefab(prefab), GatherTest.Ground(at) + Vector3.up * 0.3f, Quaternion.identity);
            made.Add(g);
            return g.GetComponent<Character>();
        }

        /// <summary>월드가 낳은 길들일 짐승(집 TameFar+20m 안)을 치움 — 회차 374: 80m 밖 야생 멧돼지를 일꾼이 길들이러 가 시험이 어긋남.</summary>
        static void ClearOthers(Vector3 home, List<GameObject> made)
        {
            foreach (var c in new List<Character>(Character.GetAllCharacters()))
                if (c != null && !made.Contains(c.gameObject) && c.GetComponent<Tameable>() != null && Utils.DistanceXZ(c.transform.position, home) < TameJob.TameFar + 20f)
                {
                    TestRunner.Log($"시험 밖 짐승 치움: {c.m_name} {Utils.DistanceXZ(c.transform.position, home):0}m");
                    ZNetScene.instance.Destroy(c.gameObject);
                }
        }

        static float TameLeft(Character c) => c.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, c.GetComponent<Tameable>().m_tamingTime);

        /// <summary>
        /// 짐승이 걷는 줄(우리 쪽 -12m ~ 야생 쪽 +42m)이 트인 방향. 바닐라 따라오기(BaseAI.Follow→MoveTo)는 navmesh 길이 있어도
        /// 큰 바위 모서리·가파른 턱에 몸이 끼면 그 자리에서 멈춘다(11-12: te4st41 집 거점 동쪽 Stone1_huge 곁 (271,457)에서 3)·6) 멧돼지 둘 다 속도 0).
        /// 예전엔 라그나 자리로 방향을 정해 회차마다 달라 가끔 막힘 → 16방향 중 막힌 점이 가장 적은 쪽(같으면 원래 쪽에 가까운 것).
        /// </summary>
        static Vector3 PickSide(Vector3 home, Vector3 first)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "blocker");
            float wl = ZoneSystem.instance.m_waterLevel;
            Vector3 best = first;
            int bestBad = int.MaxValue, firstBad = -1;
            float bestAngle = 0f;
            for (int k = 0; k < 16; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, k * 22.5f, 0f) * first;
                int bad = 0;
                float prev = float.NaN;
                for (float d = -12f; d <= 42f; d += 1f)
                {
                    Vector3 p = home + dir * d;
                    if (!ZoneSystem.instance.GetGroundHeight(p, out float h) || h < wl + 0.5f) { bad++; prev = float.NaN; continue; }
                    if (!float.IsNaN(prev) && Mathf.Abs(h - prev) > 0.6f) bad++;   // 1m에 0.6m 넘는 턱(31°)
                    prev = h;
                    p.y = h;
                    if (Mathf.Abs(d) < 3f) continue;   // 깃발·일꾼 자리
                    if (Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.2f, 0.8f, mask, QueryTriggerInteraction.Ignore)) bad++;
                }
                float angle = k <= 8 ? k * 22.5f : (16 - k) * 22.5f;
                if (k == 0) firstBad = bad;
                if (bad < bestBad || (bad == bestBad && angle < bestAngle)) { best = dir; bestBad = bad; bestAngle = angle; }
            }
            TestRunner.Log($"짐승 줄 방향: 원래 쪽 막힘 {firstBad} → {bestAngle:0}° 돌린 쪽 막힘 {bestBad}");
            return best;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            LogPrefabs();
            yield return TestKit.Setup(w);
            checks.Add("깃발·두 일꾼·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null) yield break;
            Vector3 home = w.Home;
            ClearOthers(home, made);
            Vector3 side = Vector3.Cross(Vector3.up, (w.Ragnar.transform.position - home).sqrMagnitude > 1f ? (w.Ragnar.transform.position - home).normalized : Vector3.forward);
            if (w.Site != null) { side.y = 0f; side = PickSide(home, side.sqrMagnitude > 0.01f ? side.normalized : Vector3.right); } // 집 거점만(집 모드 끔은 옛 쪽 그대로 — 돌린 쪽에선 5) 야생이 안 골라짐, 11-12)

            // 1) 야생 멧돼지 — 일꾼(진영 Players)을 적으로 보는지, 6m 옆에서 경보·목표가 되는지
            var wild = Spawn("Boar", w.Ragnar.transform.position + side * 6f, made);
            checks.Add("야생 멧돼지는 일꾼을 적으로 봄(BaseAI.IsEnemy)", BaseAI.IsEnemy(wild, w.Ragnar));
            float hp0 = w.Ragnar.GetHealth();
            yield return new WaitForSeconds(6f);
            if (wild == null) TestRunner.Log($"야생 멧돼지 6m: 6초 안에 죽음(일꾼이 잡음), 라그나르 체력 {hp0:0}→{w.Ragnar.GetHealth():0}");
            else
            {
                TestRunner.Log($"야생 멧돼지 6m: 경보 {wild.GetComponent<MonsterAI>().IsAlerted()}, 거리 {Vector3.Distance(wild.transform.position, w.Ragnar.transform.position):0.0}m, 라그나르 체력 {hp0:0}→{w.Ragnar.GetHealth():0}");
                ZNetScene.instance.Destroy(wild.gameObject);
            }

            // 2) 일꾼이 멀리(40m) 있을 때 떨군 먹이를 먹고 길들이기가 줄어드는지
            Vector3 far = home + side * 40f;
            var boar = Spawn("Boar", far, made);
            var tm = boar.GetComponent<Tameable>();
            yield return new WaitForSeconds(1f);
            float left0 = TameLeft(boar);
            var berry = Object.Instantiate(ObjectDB.instance.GetItemPrefab("Raspberry"), boar.transform.position + side * 1.5f + Vector3.up, Quaternion.identity);
            berry.GetComponent<ItemDrop>().SetStack(3);
            made.Add(berry);
            yield return TestKit.WaitFor(() => boar != null && !tm.IsHungry(), 25f);
            if (boar != null && tm.IsHungry()) TestRunner.Log($"먹이 안 먹음(25초): {BoarState(boar)} 일꾼 {Vector3.Distance(boar.transform.position, w.Ragnar.transform.position):0}m");
            checks.Add("떨군 산딸기를 먹음(배부름)", boar != null && !tm.IsHungry());
            yield return new WaitForSeconds(7f);
            if (boar == null) { checks.Add("먹이 시험 멧돼지 살아 있음", false); yield break; }
            float left1 = TameLeft(boar);
            TestRunner.Log($"길들이기 남은 시간 {left0:0}→{left1:0}초, 경보 {boar.GetComponent<MonsterAI>().IsAlerted()}, 일꾼까지 {Vector3.Distance(boar.transform.position, w.Ragnar.transform.position):0}m");
            checks.Add("배부르면 길들이기 시간이 줄어듦", left1 < left0);

            // 3) 길들인 멧돼지가 일꾼을 따라오는지(SetFollowTarget — 바닐라 Command는 Player만 받음)
            var mai = boar.GetComponent<MonsterAI>();
            mai.MakeTame();
            checks.Add("길들인 멧돼지는 일꾼을 적으로 안 봄", boar.IsTamed() && !BaseAI.IsEnemy(boar, w.Ragnar));
            mai.SetFollowTarget(w.Ragnar.gameObject);
            float d0 = Vector3.Distance(boar.transform.position, w.Ragnar.transform.position);
            yield return TestKit.WaitFor(() => boar == null || Vector3.Distance(boar.transform.position, w.Ragnar.transform.position) < 5f, 30f);
            if (boar == null) { checks.Add("따라오기 멧돼지 살아 있음", false); yield break; }
            float d1 = Vector3.Distance(boar.transform.position, w.Ragnar.transform.position);
            TestRunner.Log($"따라오기: {d0:0}m → {d1:0.0}m ({TestKit.Waited:0}초, {BoarState(boar)})");
            checks.Add("SetFollowTarget(일꾼)으로 따라옴(5m 안)", d1 < 5f);
            mai.SetFollowTarget(null);
            mai.SetPatrolPoint();   // 플레이어가 "머물러" 둔 꼴 — 6) 데려오기가 건드리지 않아야 함

            // 4) 우리 먹이(요청 다-2): "가축" 상자 둘레 배고픈 길들인 멧돼지 → 쉬던 일꾼이 상자 산딸기 1개를 곁에 떨궈 먹임
            Vector3 penAt = GatherTest.Ground(home - side * 8f);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), penAt, w.Player.transform.rotation, doAttack: false);
            Container pen = GatherTest.ChestAt(penAt);
            checks.Add("가축 상자 지음", pen != null);
            if (pen == null) yield break;
            made.Add(pen.gameObject);
            pen.GetComponent<ZNetView>().GetZDO().Set("ChestName", "가축");
            TestKit.PutInChest(pen, "Raspberry", 5);
            var pig = Spawn("Boar", penAt - side * 4f, made);
            var ptm = pig.GetComponent<Tameable>();
            pig.GetComponent<MonsterAI>().MakeTame();
            yield return new WaitForSeconds(1f);
            checks.Add("우리 멧돼지 길들고 배고픔", pig.IsTamed() && ptm.IsHungry());
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 30f);
            Departures.TestFeedNow();
            checks.Add("쉬던 일꾼이 우리 먹이 주러 감", Departures.IdleFeed(w.Ragnar) && w.Ragnar.Ai.Job is LivestockJob);
            yield return TestKit.WaitFor(() => pig == null || !ptm.IsHungry(), 60f);
            checks.Add($"우리 멧돼지가 먹이를 먹음 ({TestKit.Waited:0}초)", pig != null && !ptm.IsHungry());
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is LivestockJob), 60f);
            int left = 0;
            foreach (var it in pen.GetInventory().GetAllItems()) if (it.m_dropPrefab != null && it.m_dropPrefab.name == "Raspberry") left += it.m_stack;
            checks.Add($"먹이 주기 끝, 가축 상자 산딸기 5→{left}", TestKit.Ok && left == 4);
            if (pig != null) pig.GetComponent<MonsterAI>().SetPatrolPoint();   // 우리에 세워 둠 — 안 그러면 떠돌다 10m 밖에서 6) 데려오기 대상이 됨(회차 374)

            // 5) 길들이기(요청 다-3): 집 38m 야생 멧돼지 → 쉬던 일꾼이 가축 상자 산딸기를 곁에 떨구고 시야 밖에서 지켜봄 → 먹고 길들이기가 줄고, 일꾼이 안 잡음
            ClearOthers(home, made);
            foreach (Collider col in Physics.OverlapSphere(home + side * 38f, 15f, LayerMask.GetMask("item")))   // 1)~3) 길들이기 칸에 떨군 산딸기가 38m 자리 10m 안에 남으면 멧돼지가 혼자 먹어 일꾼이 안 고름(11-12)
            { ItemDrop d = col.GetComponentInParent<ItemDrop>(); if (d != null) ZNetScene.instance.Destroy(d.gameObject); }
            var wildPig = Spawn("Boar", home + side * 38f, made);   // 30m면 집 곁(8~10m)에서 쉬는 일꾼이 멧돼지 시야(20m) 끝이라 가끔 경보 → 일꾼이 잡음(11-12 집 거점)
            var wtm = wildPig.GetComponent<Tameable>();
            yield return new WaitForSeconds(1f);
            float wl0 = TameLeft(wildPig);
            checks.Add("야생 멧돼지 배고프고 아직 안 길들임", !wildPig.IsTamed() && wtm.IsHungry() && !TameJob.Taming(wildPig));
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 30f);
            Departures.TestTameNow();
            checks.Add("쉬던 일꾼이 길들이러 감", Departures.IdleTame(w.Ragnar) && w.Ragnar.Ai.Job is TameJob);
            float nextLog = Time.time + 10f;   // 먹기까지 오래 걸리는 까닭 가르기(E127)
            yield return TestKit.WaitFor(() =>
            {
                if (wildPig != null && Time.time > nextLog) { nextLog = Time.time + 10f; TestRunner.Log($"먹이 기다림 {TestKit.Waited:0}초: {BoarState(wildPig)} 일꾼 {Vector3.Distance(wildPig.transform.position, w.Ragnar.transform.position):0}m"); }
                return wildPig == null || !wtm.IsHungry();
            }, 90f);
            if (wildPig != null && wtm.IsHungry()) TestRunner.Log($"먹이 안 먹음: {BoarState(wildPig)}");
            checks.Add($"야생 멧돼지가 떨군 먹이를 먹음 ({TestKit.Waited:0}초)", wildPig != null && !wtm.IsHungry());
            yield return new WaitForSeconds(7f);
            if (wildPig == null) { checks.Add("길들이는 멧돼지 살아 있음", false); yield break; }
            float wl1 = TameLeft(wildPig);
            TestRunner.Log($"길들이기 남은 시간 {wl0:0}→{wl1:0}초, 경보 {wildPig.GetComponent<MonsterAI>().IsAlerted()}, 일꾼까지 {Vector3.Distance(wildPig.transform.position, w.Ragnar.transform.position):0}m");
            checks.Add("길들이기 시간이 줄어듦(Taming)", wl1 < wl0 && TameJob.Taming(wildPig));
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is TameJob), 90f);
            int left2 = 0;
            foreach (var it in pen.GetInventory().GetAllItems()) if (it.m_dropPrefab != null && it.m_dropPrefab.name == "Raspberry") left2 += it.m_stack;
            checks.Add($"길들이기 끝, 상자 산딸기 4→{left2}", TestKit.Ok && left2 == 3);
            Departures.TestTameNow();
            checks.Add("배부른 길들이는 멧돼지는 다시 안 감, 살아 있음", wildPig != null && !wildPig.IsDead() && !(Departures.IdleTame(w.Ragnar) && w.Ragnar.Ai.Job is TameJob tj && tj.Animal == wildPig));

            // 6) 데려오기(요청 다-3b): 남은 시간을 줄여 길들게 한 뒤 → 쉬던 일꾼이 우리 상자까지 따라오게 해 풀어 둠(순찰 지점), 머물러 둔 3)의 멧돼지는 그대로
            wildPig.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_tameTimeLeft, 1f);
            yield return TestKit.WaitFor(() => wildPig == null || wildPig.IsTamed(), 15f);
            checks.Add($"멧돼지 길듦 ({TestKit.Waited:0}초)", wildPig != null && wildPig.IsTamed());
            if (wildPig == null || !wildPig.IsTamed()) yield break;
            var wmai = wildPig.GetComponent<MonsterAI>();
            float pd0 = Utils.DistanceXZ(wildPig.transform.position, pen.transform.position);
            ClearOthers(home, made);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 60f);
            Departures.TestTameNow();
            checks.Add($"쉬던 일꾼이 길든 멧돼지 데리러 감(우리까지 {pd0:0}m)", Departures.IdleTame(w.Ragnar) && w.Ragnar.Ai.Job is TameJob);
            yield return TestKit.WaitFor(() => wildPig == null || !(w.Ragnar.Ai.Job is TameJob), 200f);
            if (wildPig == null) { checks.Add("데려온 멧돼지 살아 있음", false); yield break; }
            float pd1 = Utils.DistanceXZ(wildPig.transform.position, pen.transform.position);
            var pzdo = wildPig.GetComponent<ZNetView>().GetZDO();
            TestRunner.Log($"데려오기(떠돌기 범위 {wmai.m_randomMoveRange}m·간격 {wmai.m_randomMoveInterval}초): 우리까지 {pd0:0}m → {pd1:0.0}m ({TestKit.Waited:0}초), 따라감 {wmai.GetFollowTarget() != null}, 순찰 {pzdo.GetBool(ZDOVars.s_patrol)}");
            checks.Add("길든 멧돼지가 우리 상자 10m 안에 옴", pd1 < LivestockJob.PenRange);
            checks.Add("우리에 풀어 둠(안 따라감·순찰 지점)", wmai.GetFollowTarget() == null && pzdo.GetBool(ZDOVars.s_patrol));
            checks.Add($"우리 안 얌전(떠돌기 범위 {wmai.m_randomMoveRange}m ≤ {TameJob.PenRoam}m)", wmai.m_randomMoveRange <= TameJob.PenRoam);
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForSeconds(2f);
                if (wildPig == null) break;
                TestRunner.Log($"풀어 둔 멧돼지 {i * 2 + 2}초: 우리까지 {Utils.DistanceXZ(wildPig.transform.position, pen.transform.position):0.0}m, 순찰점까지 {Utils.DistanceXZ(wildPig.transform.position, pzdo.GetVec3(ZDOVars.s_patrolPoint, Vector3.zero)):0.0}m, 경보 {wmai.IsAlerted()}, 배고픔 {wildPig.GetComponent<Tameable>().IsHungry()}, 따라감 {wmai.GetFollowTarget()?.name}, 표적 {wmai.GetTargetCreature()?.m_name}, 달림 {wildPig.IsRunning()}, 떠돌기 목표 순찰점까지 {Utils.DistanceXZ(HarmonyLib.Traverse.Create(wmai).Field("m_randomMoveTarget").GetValue<Vector3>(), pzdo.GetVec3(ZDOVars.s_patrolPoint, Vector3.zero)):0.0}m");
            }
            float pd2 = Utils.DistanceXZ(wildPig.transform.position, pen.transform.position);
            // 떠돌기 범위(우리 안 4m) 두 배 넘어야 돌아옴(바닐라, 회차 378)
            checks.Add($"10초 뒤에도 우리에 있음({pd2:0.0}m)", pd2 < wmai.m_randomMoveRange * 2f + 1f && LivestockJob.InPen(wildPig, pen.transform.position));
            ClearOthers(home, made);   // 집 거점 95m 둘레에서 월드가 멧돼지를 다시 낳음(11-12) — 그걸 길들이러 가면 이 칸이 어긋남
            Departures.TestTameNow();
            checks.Add("머물러 둔 멧돼지·우리 멧돼지는 안 데리러 감", !Departures.IdleTame(w.Ragnar));

            // 7) 우리 솎기(요청 다-4): 우리에 8마리(다 자란 것 + 새끼 1 + 이름 붙인 것 1) → 쉬던 일꾼이 다 자란 것 2마리를 잡아 떨굼을 주워 거점 상자에 넣음
            ClearOthers(home, made);
            // 3)·4)·6)의 멧돼지는 떠돌다 들고 남(회차 378: 7·9마리) → 치우고 8마리를 직접 놓음
            foreach (var old in new[] { boar, pig, wildPig }) if (old != null) ZNetScene.instance.Destroy(old.gameObject);
            var penPigs = new List<Character>();
            for (int i = 0; i < 8; i++)
            {
                var p = Spawn(i == 4 ? "Boar_piggy" : "Boar", penAt + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 3f, made);
                p.SetTamed(true);   // Boar_piggy는 MonsterAI가 없을 수 있음(회차 375 NRE)
                p.GetComponent<BaseAI>()?.SetPatrolPoint();
                TestRunner.Log($"우리에 {p.m_name}: AI {p.GetComponent<BaseAI>()?.GetType().Name}, 자람 {p.GetComponent<Growup>() != null}");
                penPigs.Add(p);
            }
            Character piggy = penPigs[4], named = penPigs[5];
            named.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_tamedName, "Bacon");
            yield return new WaitForSeconds(1f);
            int penCount = 0;
            foreach (var c in Character.GetAllCharacters())
                if (c != null && !c.IsDead() && c.IsTamed() && !(c is WorkerNpc) && LivestockJob.InPen(c, pen.transform.position)) penCount++;
            checks.Add($"우리에 길든 것 {penCount}마리(6 넘음)", penCount == 8);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 60f);
            Departures.TestCullNow();
            bool culling = Departures.IdleCull(w.Ragnar) && w.Ragnar.Ai.Job is CullJob;
            checks.Add("쉬던 일꾼이 우리 솎으러 감", culling);
            if (!culling) yield break;
            var cull = (CullJob)w.Ragnar.Ai.Job;
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is CullJob), 240f);
            int meatLeft = 0;
            foreach (var it in w.Ragnar.GetInventory().GetAllItems()) if (it.m_dropPrefab != null && (it.m_dropPrefab.name == "RawMeat" || it.m_dropPrefab.name == "LeatherScraps")) meatLeft += it.m_stack;
            TestRunner.Log($"솎기: 잡음 {cull.Culled}, 주움 {cull.Got} ({TestKit.Waited:0}초), 일꾼 짐 고기 {meatLeft}");
            checks.Add($"다 자란 것 2마리 잡음({cull.Culled})", TestKit.Ok && cull.Culled == 2);
            checks.Add($"떨군 것을 주워 상자에 넣음(주움 {cull.Got}, 짐에 고기 {meatLeft})", cull.Got > 0 && meatLeft == 0);
            checks.Add("새끼·이름 붙인 것은 살아 있음", piggy != null && !piggy.IsDead() && named != null && !named.IsDead());
            Departures.TestCullNow();
            checks.Add("6마리면 다시 안 감", !Departures.IdleCull(w.Ragnar));

            // 8) 짝 남김(사용자 11-10 가축 1): 성체 2 + 새끼 6 = 8마리여도 짝은 남기니 지금은 안 잡음(새끼가 자란 뒤 잡음)
            foreach (var p in penPigs) if (p != null && !p.IsDead()) ZNetScene.instance.Destroy(p.gameObject);
            var pair = new List<Character>();
            for (int i = 0; i < 8; i++)
            {
                var p = Spawn(i < 2 ? "Boar" : "Boar_piggy", penAt + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 3f, made);
                p.SetTamed(true);
                p.GetComponent<BaseAI>()?.SetPatrolPoint();
                pair.Add(p);
            }
            yield return new WaitForSeconds(1f);
            Departures.TestCullNow();
            bool culled = Departures.IdleCull(w.Ragnar);
            checks.Add($"성체 2+새끼 6이면 안 잡음(종 {CullJob.Species(pair[2])})", !culled && CullJob.Species(pair[2]) == "Boar");
            if (culled) w.Ragnar.Ai.GoHome(false);

            // 9) 종 우리(사용자 11-10 가축 4): "가축"은 들어온 종(멧돼지)으로 굳음, 상자 이름이 "늑대"면 멧돼지는 안 받음(먹이·솎기·데려오기)
            Container penBox = pen.GetComponent<Container>();
            var map = Pens.SpeciesMap(new List<Container> { penBox });
            checks.Add($"\"가축\" 우리는 들어온 종으로 굳음({Pens.SpeciesOf(penBox)})", Pens.SpeciesOf(penBox) == "Boar" && Pens.Accepts(map, penBox, pair[0]));
            ZDO boxZdo = pen.GetComponent<ZNetView>().GetZDO();
            string oldName = ChestTagReader.Name(boxZdo);
            boxZdo.Set("ChestName", "늑대");
            map = Pens.SpeciesMap(new List<Container> { penBox });
            checks.Add($"\"늑대\" 상자는 늑대 우리, 멧돼지 안 받음({Pens.SpeciesOf(penBox)})", Pens.SpeciesOf(penBox) == "Wolf" && !Pens.Accepts(map, penBox, pair[0]) && CullJob.Plan(home, new Dictionary<Character, Vector3>()).Count == 0);

            // 10) 센 종 길들이기(가축 ④ 다): 늑대 우리에 고기, 집 38m 야생 늑대 → 쉬던 일꾼이 길들이러 가며 위험한 일(Risky)로 동행을 찾음
            TestKit.PutInChest(penBox, "RawMeat", 3);
            var wolf = Spawn("Wolf", home + side * 38f, made);
            yield return new WaitForSeconds(1f);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 30f);
            Departures.TestTameNow();
            bool went = Departures.IdleTame(w.Ragnar) && w.Ragnar.Ai.Job is TameJob wj && wj.Animal == wolf;
            checks.Add($"야생 늑대는 길들이러 갈 때 위험한 일로 동행 찾음(감 {went}, 위험 {w.Ragnar.Ai.Risky}, 동행 {w.Ragnar.Ai.EscortName ?? "-"})", went && w.Ragnar.Ai.Risky && TameJob.NeedsGuard(wolf) && !TameJob.NeedsGuard(pair[0]));
            w.Ragnar.Ai.GoHome(false);
            if (wolf != null) ZNetScene.instance.Destroy(wolf.gameObject);
            boxZdo.Set("ChestName", oldName ?? "");

            // 11) 알 품기(가축 ④ 라): 가축 상자에 아스크스빈 알 2 + 우리 곁 모닥불 → 쉬던 일꾼이 알 1개를 불 곁에 한 개만 놓고, 게임 EggGrow가 데우기 시작, 다 되면 길든 새끼
            int Eggs() { int n = 0; foreach (var it in penBox.GetInventory().GetAllItems()) if (it.m_dropPrefab != null && it.m_dropPrefab.name == "AsksvinEgg") n += it.m_stack; return n; }
            TestKit.PutInChest(penBox, "AsksvinEgg", 2);
            Vector3 fireAt = GatherTest.Ground(home - side * 14f);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("fire_pit").GetComponent<Piece>(), fireAt, Quaternion.identity, doAttack: false);
            Fireplace fire = null;
            foreach (Collider col in Physics.OverlapSphere(fireAt, 2f)) if ((fire = col.GetComponentInParent<Fireplace>()) != null) break;
            checks.Add("모닥불 지음", fire != null);
            if (fire == null) yield break;
            made.Add(fire.gameObject);
            fire.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_fuel, 10f);
            yield return new WaitForSeconds(2f);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 30f);
            Departures.TestEggNow();
            bool egg = Departures.IdleEgg(w.Ragnar) && w.Ragnar.Ai.Job is EggJob;
            Vector3 spot = egg ? ((EggJob)w.Ragnar.Ai.Job).Spot : Vector3.zero;
            string areas = ""; foreach (var ea in EffectArea.GetAllAreas()) if (ea != null && Utils.DistanceXZ(ea.transform.position, fireAt) < 5f) areas += $" {ea.m_type}/{ea.gameObject.activeInHierarchy}/{(ea.GetComponent<Collider>() is SphereCollider sc ? sc.radius * ea.transform.lossyScale.x : -1f):0.0}m/높이{ea.transform.position.y - fireAt.y:0.0}";
            checks.Add($"쉬던 일꾼이 알 품으러 감(자리 {spot}, 불과 {Utils.DistanceXZ(spot, fireAt):0.0}m, 불 곁 구역{areas}, 연료 {fire.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_fuel):0}, {EggJob.LastWhy})", egg);
            if (!egg) yield break;
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is EggJob), 90f);
            ItemDrop laid = null;
            foreach (ItemDrop d in JobKit.DropsNear(spot, 3f)) if (Utils.GetPrefabName(d.gameObject) == "AsksvinEgg") laid = d;
            checks.Add($"알 한 개가 불 곁 땅에, 상자 알 2→{Eggs()} ({TestKit.Waited:0}초)", laid != null && laid.m_itemData.m_stack == 1 && Eggs() == 1);
            if (laid == null) yield break;
            made.Add(laid.gameObject);
            ZDO eggZdo = laid.GetComponent<ZNetView>().GetZDO();
            yield return TestKit.WaitFor(() => eggZdo.GetFloat(ZDOVars.s_growStart) > 0f, 15f);
            checks.Add($"알이 데워지기 시작(EggGrow, {TestKit.Waited:0}초)", eggZdo.GetFloat(ZDOVars.s_growStart) > 0f);
            eggZdo.Set(ZDOVars.s_growStart, (float)ZNet.instance.GetTimeSeconds() - laid.GetComponent<EggGrow>().m_growTime - 1f);
            Character chick = null;
            yield return TestKit.WaitFor(() => { foreach (var a in Character.GetAllCharacters()) if (a != null && Utils.GetPrefabName(a.gameObject) == "Asksvin_hatchling" && Utils.DistanceXZ(a.transform.position, spot) < 3f) chick = a; return chick != null; }, 15f);
            if (chick != null) made.Add(chick.gameObject);
            checks.Add($"알이 깨어 길든 새끼({(chick != null ? chick.IsTamed().ToString() : "없음")}, {TestKit.Waited:0}초)", chick != null && chick.IsTamed());

            // 12) 울타리 우리(가축 ⑥): 벽 6m×6m에 문 하나, 안에 배고픈 길든 멧돼지, 상자는 문 앞 밖 → 쉬던 일꾼이 문 열고 들어가 먹이고 나옴
            Vector3 fwd = Vector3.Cross(side, Vector3.up).normalized;
            Vector3 pc = GatherTest.Ground(home + fwd * 16f);
            HouseTest.ClearPlants(pc, 7f);
            const float Half = 3f;
            int walls = 0; GameObject gate = null;
            foreach (var n in new[] { fwd, -fwd, side, -side })
            {
                Vector3 along = Vector3.Cross(Vector3.up, n).normalized;
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 at = pc + n * Half + along * (2f * i);
                    bool gateSlot = n == -fwd && i == 0;
                    var g = HouseTest.Put(made, gateSlot ? "wood_door" : "woodwall", at, Quaternion.LookRotation(n), ZoneSystem.instance.GetGroundHeight(at) - 0.05f);
                    if (g == null) continue;
                    if (gateSlot) gate = g; else walls++;
                }
            }
            checks.Add($"울타리 우리 지음(벽 {walls}, 문 {gate != null})", walls == 11 && gate != null);
            if (gate == null) yield break;
            Vector3 gateBoxAt = GatherTest.Ground(pc - fwd * (Half + 2.5f) + side * 2f);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), gateBoxAt, w.Player.transform.rotation, doAttack: false);
            Container gateBox = GatherTest.ChestAt(gateBoxAt);
            if (gateBox == null) { checks.Add("울타리 우리 상자 지음", false); yield break; }
            made.Add(gateBox.gameObject);
            gateBox.GetComponent<ZNetView>().GetZDO().Set("ChestName", "멧돼지");
            TestKit.PutInChest(gateBox, "Raspberry", 3);
            var penned = Spawn("Boar", pc + Vector3.up * 0.3f, made);
            var pentm = penned.GetComponent<Tameable>();
            penned.GetComponent<MonsterAI>().MakeTame();
            yield return new WaitForSeconds(7f); // 새 벽·닫힌 문 navmesh
            penned.GetComponent<MonsterAI>().SetPatrolPoint();
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 60f);
            int entered0 = DoorWay.Entered;
            Departures.TestFeedNow();
            checks.Add("쉬던 일꾼이 울타리 우리 먹이 주러 감", Departures.IdleFeed(w.Ragnar) && w.Ragnar.Ai.Job is LivestockJob);
            yield return TestKit.WaitFor(() => penned == null || !pentm.IsHungry(), 120f);
            checks.Add($"문 열고 들어가 울타리 안 멧돼지 먹임 ({TestKit.Waited:0}초, 문 지남 {DoorWay.Entered - entered0}, 일꾼과 우리 가운데 {Utils.DistanceXZ(w.Ragnar.transform.position, pc):0.0}m)",
                penned != null && !pentm.IsHungry() && DoorWay.Entered > entered0);
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is LivestockJob), 60f);
            checks.Add($"먹이 주기 끝내고 울타리 밖으로 나옴 ({TestKit.Waited:0}초, 우리 가운데와 {Utils.DistanceXZ(w.Ragnar.transform.position, pc):0.0}m)",
                TestKit.Ok && Utils.DistanceXZ(w.Ragnar.transform.position, pc) > Half + 0.5f);
        }
    }
}
