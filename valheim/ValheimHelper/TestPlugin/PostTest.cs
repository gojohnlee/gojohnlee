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
                TestKit.Ok && side > 0.5f && Utils.DistanceXZ(ai.Post, doorAt) < 3f); // 자리는 문틀 가운데에서 바깥 1.0m(DoorWay.PostBy) — 1f로 보면 소수점에 갈림

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
                // 물가 시험(회차 611): 점 10m 안쪽이 물 쪽이면 그레일링이 물에 빠져 싸움 안 함 → 물 위 1m 넘는 땅이 나올 때까지 집 쪽으로 4m씩
                for (int s = 0; s < 6 && GatherTest.Ground(at).y < ZoneSystem.instance.m_waterLevel + 1f; s++) at += (w.Home - at).normalized * 4f;
                foe.transform.position = GatherTest.Ground(at) + Vector3.up * 0.3f;
                Plugin.Log.LogInfo($"시험 그레일링 자리 {foe.transform.position} (물 위 {foe.transform.position.y - ZoneSystem.instance.m_waterLevel:0.0}m)");
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

            // 5) 쉬는 호위병도 거점 안 집안일(decisions 221줄, 회차 813: Guards가 TickRefill에 빠져 한 번도 안 돌았음) — 호위병이 먼저 고름
            yield return TestKit.WaitFor(() => ai.Current == WorkerAI.Mode.Idle && !ai.Patrolling, 30f);
            Vector3 penAt = GatherTest.Ground(w.Home + f * 8f);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), penAt, w.Player.transform.rotation, doAttack: false);
            Container pen = GatherTest.ChestAt(penAt);
            checks.Add("가축 상자 지음", pen != null);
            if (pen == null) yield break;
            made.Add(pen.gameObject);
            pen.GetComponent<ZNetView>().GetZDO().Set("ChestName", "가축");
            TestKit.PutInChest(pen, "Raspberry", 5);
            var pig = LivestockTest.Spawn("Boar", penAt + Vector3.Cross(Vector3.up, f) * 4f, made);
            var ptm = pig.GetComponent<Tameable>();
            pig.GetComponent<MonsterAI>().MakeTame();
            yield return new WaitForSeconds(1f);
            checks.Add("우리 멧돼지 길들고 배고픔", pig.IsTamed() && ptm.IsHungry());
            Departures.TestFeedNow();
            Departures.TestIdleChores = true; // 시험 중엔 쉴 때 집안일이 꺼져 있음(ChoresOn)
            Departures.TestRefillNow();
            yield return TestKit.WaitFor(() => guard.Ai.Job is LivestockJob, 10f);
            Departures.TestIdleChores = false;
            checks.Add($"쉬던 호위병이 우리 먹이 주러 감 ({guard.Ai.Current}/{guard.Ai.Job?.GetType().Name ?? "-"}, 순찰 {ai.Patrolling})", TestKit.Ok);
            yield return TestKit.WaitFor(() => pig == null || !ptm.IsHungry(), 60f);
            checks.Add($"우리 멧돼지가 먹이를 먹음 ({TestKit.Waited:0}초)", pig != null && !ptm.IsHungry());

            // 6) 쉬는 호위병 굽기(decisions 221줄, 회차 814): 상자 날고기 2 + 거점 화로·조리대 → 호위병이 구움(밖 재료면 안 감). 일꾼은 쉼(HoldChores)이라 안 낌
            yield return TestKit.WaitFor(() => guard.Ai.Current == WorkerAI.Mode.Idle && !ai.Patrolling, 60f);
            GameObject pit = ZNetScene.instance.GetPrefab("fire_pit"), stand = ZNetScene.instance.GetPrefab("piece_cookingstation");
            string burnt = stand.GetComponent<CookingStation>().m_overCookedItem.name;
            TestKit.ClearBaseStock(w, "RawMeat", "CookedMeat", "Wood", burnt);
            TestKit.PutInChest(w.Chest, "RawMeat", 2);
            TestKit.PutInChest(w.Chest, "Wood", 5);
            Vector3 cookAt = GatherTest.Ground(w.Home - Vector3.Cross(Vector3.up, f) * 6f);
            made.Add(Object.Instantiate(pit, cookAt, Quaternion.identity));
            made.Add(Object.Instantiate(stand, cookAt, Quaternion.identity));
            yield return new WaitForSeconds(2f);
            w.Ragnar.Ai.HoldChores();
            w.Floki?.Ai.HoldChores();
            Departures.TestIdleChores = true;
            for (int i = 0; i < 30 && !(guard.Ai.Job is PlanJob); i++) // 후보 87개를 한 차례에 계획 4개씩 — 안 되는 것은 10분 쉬니 차례마다 아래로 내려가 날고기에 닿음
            {
                Departures.TestRefillNow();
                yield return TestKit.WaitFor(() => guard.Ai.Job is PlanJob, 1f);
            }
            Departures.TestIdleChores = false;
            checks.Add($"쉬던 호위병이 상자 날고기로 굽기 시작 ({guard.Ai.Current}/{guard.Ai.Job?.GetType().Name ?? "-"}, 라그나 {w.Ragnar.Ai.Job?.GetType().Name ?? "-"})", guard.Ai.Job is PlanJob);
            checks.Add("호위병 굽기는 동행 요청이면 놓는 집안일(GuardFree)", Departures.GuardFree(guard));
            string cooked = ObjectDB.instance.GetItemPrefab("CookedMeat").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            yield return TestKit.WaitFor(() => CookedNow(w, cooked, guard) >= 2, 240f);
            checks.Add($"호위병이 고기 2개 구움 (상자+가방 {CookedNow(w, cooked, guard)}, {TestKit.Waited:0}초)", CookedNow(w, cooked, guard) >= 2);

            // 7) 쉬는 호위병 발효기(decisions 221줄 미드·포션, 회차 815): 다 익은 독 저항 미드 바탕 + 상자 바탕 1 → 따서 상자에, 같은 바탕 다시 넣음
            yield return TestKit.WaitFor(() => guard.Ai.Current == WorkerAI.Mode.Idle && !ai.Patrolling, 120f);
            const string meadBase = "MeadBasePoisonResist", mead = "MeadPoisonResist";
            TestKit.ClearBaseStock(w, meadBase, mead);
            MachineStep.TestIgnoreRoof = true;
            GameObject ferm = Object.Instantiate(ZNetScene.instance.GetPrefab("fermenter"), GatherTest.Ground(w.Home + Vector3.Cross(Vector3.up, f) * 6f), Quaternion.identity);
            made.Add(ferm);
            Fermenter fer = ferm.GetComponent<Fermenter>();
            ZNetView fnv = ferm.GetComponent<ZNetView>();
            yield return new WaitForSeconds(0.5f);
            fnv.InvokeRPC("RPC_AddItem", meadBase.GetStableHashCode(), false);
            yield return new WaitForSeconds(0.5f);
            fnv.GetZDO().Set(ZDOVars.s_startTime, ZNet.instance.GetTime().AddSeconds(-fer.m_fermentationDuration - 10).Ticks);
            TestKit.PutInChest(w.Chest, meadBase, 1);
            Departures.TestBrewNow();
            Departures.TestIdleChores = true;
            for (int i = 0; i < 30 && !(guard.Ai.Job is PlanJob pj0 && pj0.Uses("fermenter")); i++)
            {
                Departures.TestRefillNow();
                yield return TestKit.WaitFor(() => guard.Ai.Job is PlanJob pj1 && pj1.Uses("fermenter"), 1f);
            }
            Departures.TestIdleChores = false;
            checks.Add($"쉬던 호위병이 발효기 보러 감 ({guard.Ai.Current}/{guard.Ai.Job?.GetType().Name ?? "-"})", guard.Ai.Job is PlanJob pj2 && pj2.Uses("fermenter"));
            checks.Add("호위병 발효기 일도 동행 요청이면 놓는 집안일(GuardFree)", Departures.GuardFree(guard));
            int want = fer.m_conversion.Find(c => c.m_from != null && c.m_from.gameObject.name == meadBase)?.m_producedItems ?? 1;
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, mead) >= want && fnv.GetZDO().GetInt(ZDOVars.s_content) == meadBase.GetStableHashCode(), 180f);
            checks.Add($"호위병이 미드를 따 상자에 넣고 바탕을 다시 넣음 (상자 미드 {GatherTest.CountBase(w.Home, mead)}/{want}, 통 다시 참 {fnv.GetZDO().GetInt(ZDOVars.s_content) == meadBase.GetStableHashCode()}, {TestKit.Waited:0}초)",
                GatherTest.CountBase(w.Home, mead) >= want && fnv.GetZDO().GetInt(ZDOVars.s_content) == meadBase.GetStableHashCode());
            MachineStep.TestIgnoreRoof = false;
        }

        static int CookedNow(TestKit.World w, string name, WorkerNpc guard)
        {
            int n = guard.GetInventory().CountItems(name);
            foreach (var c in Chests.NearBase(w.Home)) n += GatherTest.Count(c, "CookedMeat");
            return n;
        }
    }
}
