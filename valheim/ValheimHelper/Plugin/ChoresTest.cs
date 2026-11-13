using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_chorestest (사용자 11-09 "극초반 좀 더 자유롭게"):
    /// 1) 이름 없이 "!나무 10" → 쉬던 두 일꾼이 같이 출발, 둘이 합쳐 10개면 둘 다 복귀(나무 10~ 조금 넘게).
    /// 1-2) 다른 일 하던 일꾼이 쉬게 되면 동료의 남은 몫 큰 채집에 낌.
    /// 1-3) 호위병 없이 위험한 데 가는 동료(Risky)를 쉬는 일꾼이 따라감, 명령이 오면 바로 그 일로.
    /// 2) "!계속채워 나무 n" → 대답하고 몇 초 안에 실제로 출발(1분 기다리지 않음).
    /// 3) 음식이 없고 조리대·화로가 있으면 쉬는 일꾼이 멧돼지를 사냥해 구워 둔다.
    /// 4) 조리대가 없으면 채집 음식(이 단계는 라즈베리만 남김)을 따 온다.
    /// 5) 음식이 넉넉해도 !밭으로 정한 밭에 빈자리·씨앗이 있으면 쉬는 일꾼이 스스로 심는다.
    /// </summary>
    static class ChoresTest
    {
        public static void Register() => TestRunner.Register("vh_chorestest", "같이 캐기·바로 보충·쉴 때 먹거리·밭일·꿀 시험", () => TestKit.Flat(Run()), 1300f);

        static readonly string[] Foods = { "Raspberry", "Blueberries", "Mushroom", "MushroomYellow", "Cloudberry", "Honey", "CookedMeat" };

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
            Departures.TestIdleChores = true;
            try { yield return Body(w, checks, made); }
            finally
            {
                Departures.TestIdleChores = false;
                OnlyFood(null, false);
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                Brain.Refill.Off();
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static int Count(TestKit.World w, string prefab) => GatherTest.CountBase(w.Home, prefab);
        static int HoneyAll(TestKit.World w) => Count(w, "Honey") + w.Ragnar.GetInventory().CountItems("$item_honey") + w.Floki.GetInventory().CountItems("$item_honey");
        static bool Idle(WorkerNpc n) => n.Ai.Job == null && n.Ai.Current == WorkerAI.Mode.Idle;
        static bool Gathering(WorkerNpc n, string item) => n.Ai.Job is GatherJob g && g.Item == item;
        static int TargetOf(WorkerNpc n) => n.Ai.Job is GatherJob g ? g.Target : 0;
        static bool Cooking(WorkerNpc n) => n.Ai.Job is PlanJob p && p.Goal != null && p.Goal.Item == "CookedMeat";
        /// <summary>쉴 때 먹거리 후보(체력순) 중 keep 말고는 쉬게(on) 또는 풀기 — 단계마다 보려는 음식만 고르게.</summary>
        static void OnlyFood(string keep, bool on) { foreach (string f in Departures.FoodRank()) Departures.TestSkip(f, on && f != keep); }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·두 일꾼·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            // 먹을 것이 있는 동안엔 쉴 때 먹거리가 끼지 않는다
            TestKit.ClearBaseStock(w, Foods);
            TestKit.PutInChest(w.Chest, "Honey", 20);
            TestKit.ClearBaseStock(w, "Wood");
            yield return TestKit.WaitFor(() => Idle(w.Ragnar) && Idle(w.Floki), 30f);

            // 1) 같이 캐기
            GatherTest.Say("!나무 10", null);
            yield return new WaitForSeconds(2f);
            bool both = Gathering(w.Ragnar, "Wood") && Gathering(w.Floki, "Wood");
            bool team = both && ((GatherJob)w.Ragnar.Ai.Job).Team != null && ((GatherJob)w.Ragnar.Ai.Job).Team == ((GatherJob)w.Floki.Ai.Job).Team;
            checks.Add($"!나무 10 → 둘 다 나무 캐러 감, 같이 셈(목표 라그나 {TargetOf(w.Ragnar)}·플로키 {TargetOf(w.Floki)})", team && TargetOf(w.Ragnar) == 10 && TargetOf(w.Floki) == 10);
            yield return TestKit.WaitFor(() => Count(w, "Wood") >= 10 && Idle(w.Ragnar) && Idle(w.Floki), 240f);
            // 쓰러뜨린 자기 통나무는 목표를 채워도 끝까지 쪼개므로(사용자 11-01) 넘는 수는 판정하지 않고 기록만
            checks.Add($"합쳐 10개 넘게 들어오고 둘 다 복귀 (나무 {Count(w, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);

            // 1-2) 늦게 같이 캐기(사용자 11-13 쉬는 일꾼 없게 ①): 플로키는 다른 일(나무 1) 중에 라그나만 나무 40(한 그루에서 20 넘게 나와 끼기 전에 끝난 적 있음, 회차 483) → 플로키가 끝나고 쉬면 라그나 팀에 낌
            int wood0 = Count(w, "Wood");
            GatherTest.Say($"!{w.Floki.NpcName} 나무 1", null);
            yield return new WaitForSeconds(2f);
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 40", null);
            yield return new WaitForSeconds(2f);
            bool alone = Gathering(w.Ragnar, "Wood") && ((GatherJob)w.Ragnar.Ai.Job).Team == null && TargetOf(w.Floki) == 1;
            checks.Add($"라그나만 나무 40, 플로키는 제 일(목표 {TargetOf(w.Floki)})", alone);
            yield return TestKit.WaitFor(() => TargetOf(w.Floki) == 40 || !Gathering(w.Ragnar, "Wood"), 240f);
            bool joined = Gathering(w.Floki, "Wood") && Gathering(w.Ragnar, "Wood") && ((GatherJob)w.Floki.Ai.Job).Team != null && ((GatherJob)w.Floki.Ai.Job).Team == ((GatherJob)w.Ragnar.Ai.Job).Team;
            checks.Add($"플로키가 제 일 끝내고 라그나 팀에 낌 ({TestKit.Waited:0}초)", joined);
            yield return TestKit.WaitFor(() => Count(w, "Wood") >= wood0 + 40 && Idle(w.Ragnar) && Idle(w.Floki), 300f);
            checks.Add($"합쳐 40개 넘게 들어오고 둘 다 복귀 (나무 {Count(w, "Wood") - wood0}, {TestKit.Waited:0}초)", TestKit.Ok);

            // 1-3) 위험한 데 혼자 가는 동료 따라가기(사용자 11-13 쉬는 일꾼 없게 ①): 이 시험엔 호위병이 없다 — 라그나 일을 위험한 일로 표시하면 쉬는 플로키가 따라감
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 10", null);
            // 위험 표시는 일이 생긴 그 프레임에(실제 출발 GoTogether도 Start 직후 같은 프레임에 Risky를 켠다). 1초 뒤에 켜면 그 사이
            // 쉬는 플로키가 늦게 같이 캐기(JoinGather)로 먼저 라그나 팀에 껴서 따라가기를 못 본다(11-14 FAIL "나무 나도 캔다! 둘이 10개!")
            if (w.Ragnar.Ai.Job == null) yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job != null, 5f);
            if (w.Ragnar.Ai.Job != null) w.Ragnar.Ai.Risky = true;
            yield return TestKit.WaitFor(() => w.Floki.Ai.Current == WorkerAI.Mode.Follow, 90f);
            checks.Add($"플로키가 위험한 데 혼자 가는 라그나를 따라감 (동행 {w.Ragnar.Ai.EscortName}, {TestKit.Waited:0}초)",
                TestKit.Ok && w.Floki.Ai.FollowTarget == w.Ragnar.GetZDOID() && w.Ragnar.Ai.EscortName == w.Floki.NpcName);
            yield return new WaitForSeconds(5f);
            GatherTest.Say($"!{w.Floki.NpcName} 돌 1", null);
            yield return new WaitForSeconds(2f);
            checks.Add($"따라가다 명령 오면 바로 그 일 ({w.Floki.Ai.Current}, {(w.Floki.Ai.Job as GatherJob)?.Item})", w.Floki.Ai.Current == WorkerAI.Mode.Work && Gathering(w.Floki, "Stone"));
            yield return TestKit.WaitFor(() => Idle(w.Ragnar) && Idle(w.Floki), 300f);
            checks.Add($"둘 다 일 끝내고 쉼 ({TestKit.Waited:0}초)", TestKit.Ok);

            // 2) 계속채워 → 바로 출발
            int wood = Count(w, "Wood");
            GatherTest.Say($"!계속채워 나무 {wood + 20}", null);
            float t0 = Time.time;
            yield return TestKit.WaitFor(() => Gathering(w.Ragnar, "Wood") || Gathering(w.Floki, "Wood"), 10f);
            checks.Add($"!계속채워 → {Time.time - t0:0.0}초 만에 출발(10초 안)", TestKit.Ok);
            yield return TestKit.WaitFor(() => Count(w, "Wood") >= wood + 9 && Idle(w.Ragnar) && Idle(w.Floki), 240f);
            checks.Add($"보충 끝 (나무 {Count(w, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);
            Brain.Refill.Off();

            // 3) 쉴 때 고기: 불 붙은 화로·조리대, 나무 연료, 둘레 멧돼지 — 베리는 이 단계 동안 쉬게 함
            yield return Meat(w, checks, made);

            // 4) 쉴 때 먹거리: 음식 비우고 둘레에 라즈베리 덤불
            Vector3 right = Vector3.Cross(Vector3.up, (w.Chest.transform.position - w.Home).normalized);
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            for (int i = 0; i < 6; i++)
            {
                Vector3 at = w.Home + right * (12f + i * 2f) + Vector3.forward * ((i % 2) * 3f);
                made.Add(Object.Instantiate(ZNetScene.instance.GetPrefab("RaspberryBush"), GatherTest.Ground(at), Quaternion.identity));
            }
            TestKit.ClearBaseStock(w, Foods);
            OnlyFood("Raspberry", true);
            yield return new WaitForSeconds(1f);
            t0 = Time.time;
            yield return TestKit.WaitFor(() => Gathering(w.Ragnar, "Raspberry") || Gathering(w.Floki, "Raspberry"), 70f);
            checks.Add($"음식 없음 → 쉬던 일꾼이 라즈베리 따러 감 ({Time.time - t0:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => Count(w, "Raspberry") >= 5, 180f);
            checks.Add($"라즈베리가 거점 상자에 들어옴 ({Count(w, "Raspberry")}, {TestKit.Waited:0}초)", TestKit.Ok);

            // 5) 쉴 때 밭일
            yield return TestKit.WaitFor(() => Idle(w.Ragnar) && Idle(w.Floki), 180f);
            yield return Farm(w, checks);
            yield return Honey(w, checks, made);
        }

        /// <summary>6) 쉴 때 꿀: 거점 안 벌통 꿀 4 → 쉬던 일꾼이 거둬 상자에 넣음.</summary>
        static IEnumerator Honey(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab("piece_beehive");
            checks.Add("벌통 프리팹", prefab != null);
            if (prefab == null) yield break;
            yield return TestKit.WaitFor(() => Idle(w.Ragnar) && Idle(w.Floki), 180f);
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            GameObject hive = Object.Instantiate(prefab, GatherTest.Ground(w.Home + right * 8f), Quaternion.identity);
            made.Add(hive);
            yield return null;
            hive.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_level, 4);
            int before = HoneyAll(w);
            Departures.TestHoneyNow();
            float t0 = Time.time;
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is HoneyJob || w.Floki.Ai.Job is HoneyJob, 70f);
            checks.Add($"꿀 찬 벌통 → 쉬던 일꾼이 꿀 거두러 감 ({Time.time - t0:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is HoneyJob) && !(w.Floki.Ai.Job is HoneyJob), 120f);
            checks.Add($"꿀 거두기 끝 ({TestKit.Waited:0}초)", TestKit.Ok);
            checks.Add($"벌통 비고 꿀 4개가 상자·짐에 들어옴(음식 들고 감 몫은 짐에) (벌통 {HoneyJob.Level(hive.GetComponent<Beehive>())}, 꿀 {before} → {HoneyAll(w)})",
                HoneyJob.Level(hive.GetComponent<Beehive>()) == 0 && HoneyAll(w) >= before + 4);
        }

        static IEnumerator Farm(TestKit.World w, GatherTest.Checks checks)
        {
            // 앞 단계들이 넣은 나무·돌·싸움 떨굼으로 나무 상자(10칸)가 꽉 차 경작기가 안 들어갔다(11-14 FAIL "밭에 심음 (0)",
            // 로그 "Adding item Cultivator x1" 뒤 "Trying to add item to occupied slot -1, -1" → farmNoTool) — 먼저 비운다
            Chests.TryEdit(w.Chest, inv => inv.RemoveAll());
            TestKit.PutInChest(w.Chest, "Honey", 20); // 먹거리 말고 밭일이 고르게
            TestKit.ClearBaseStock(w, Crops.Cultivator, "CarrotSeeds", "Carrot");
            TestKit.PutInChest(w.Chest, "CarrotSeeds", 6);
            TestKit.PutInChest(w.Chest, Crops.Cultivator, 1);
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            Vector3 at = GatherTest.Ground(w.Home - right * 12f);
            var field = new FarmField(at.x, at.z, 3);
            FarmTest.Clean(field);
            Brain.Farms.Set(field.X, field.Z, field.Radius);
            FarmField booked = Brain.Farms.At(field.X, field.Z);
            try
            {
                Departures.TestFarmNow();
                float t0 = Time.time;
                yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is FarmJob || w.Floki.Ai.Job is FarmJob, 70f);
                checks.Add($"쉬던 일꾼이 스스로 밭일 시작 ({Time.time - t0:0}초)", TestKit.Ok);
                if (!TestKit.Ok) yield break;
                FarmJob job = (w.Ragnar.Ai.Job as FarmJob) ?? (w.Floki.Ai.Job as FarmJob);
                yield return TestKit.WaitFor(() => job.Planted >= 3, 150f);
                checks.Add($"밭에 심음 ({job.Planted}, {TestKit.Waited:0}초)", TestKit.Ok);
                yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is FarmJob) && !(w.Floki.Ai.Job is FarmJob), 120f);
                checks.Add($"밭일 끝나고 돌아옴 ({TestKit.Waited:0}초)", TestKit.Ok);
            }
            finally
            {
                FarmTest.Clean(field);
                if (booked != null) Brain.Farms.Remove(booked.X, booked.Z);
            }
        }

        static IEnumerator Meat(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            GameObject pit = ZNetScene.instance.GetPrefab("fire_pit"), stand = ZNetScene.instance.GetPrefab("piece_cookingstation"), boar = ZNetScene.instance.GetPrefab("Boar");
            checks.Add("화로·조리대·멧돼지 프리팹", pit != null && stand != null && boar != null);
            if (pit == null || stand == null || boar == null) yield break;
            OnlyFood("CookedMeat", true);
            Departures.TestSkip("CookedMeat", false);
            Departures.TestForgetWeak("RawMeat");
            TestKit.ClearBaseStock(w, "RawMeat", "CookedMeat", "Wood");
            TestKit.PutInChest(w.Chest, "Wood", 20);
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            Vector3 at = GatherTest.Ground(w.Home + right * 6f);
            var station = new List<GameObject> { Object.Instantiate(pit, at, Quaternion.identity), Object.Instantiate(stand, at, Quaternion.identity) };
            made.AddRange(station);
            for (int i = 0; i < 5; i++)
                made.Add(Object.Instantiate(boar, GatherTest.Ground(w.Home - right * (25f + i * 3f) + Vector3.forward * (i * 2f)), Quaternion.identity));
            yield return new WaitForSeconds(2f);
            TestKit.ClearBaseStock(w, Foods);
            float t0 = Time.time;
            yield return TestKit.WaitFor(() => Cooking(w.Ragnar) || Cooking(w.Floki), 70f);
            checks.Add($"음식 없음·조리대 있음 → 쉬던 일꾼이 사냥해 구우러 감 ({Time.time - t0:0}초)", TestKit.Ok);
            if (!TestKit.Ok) { OnlyFood(null, false); yield break; }
            yield return TestKit.WaitFor(() => Count(w, "CookedMeat") >= 2, 480f);
            checks.Add($"구운 고기가 거점 상자에 들어옴 ({Count(w, "CookedMeat")}, {TestKit.Waited:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => !Cooking(w.Ragnar) && !Cooking(w.Floki), 300f);
            foreach (var g in station) if (g != null) ZNetScene.instance.Destroy(g);
            yield return TestKit.WaitFor(() => Idle(w.Ragnar) && Idle(w.Floki), 120f);
        }
    }
}
