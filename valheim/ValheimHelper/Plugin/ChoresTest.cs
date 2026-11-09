using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_chorestest (사용자 11-09 "극초반 좀 더 자유롭게"):
    /// 1) 이름 없이 "!나무 10" → 쉬는 두 일꾼이 나눠 캔다(목표 합 10).
    /// 2) "!계속채워 나무 n" → 대답하고 몇 초 안에 실제로 출발(1분 기다리지 않음).
    /// 3) 거점 음식 상자에 먹을 것이 없으면 쉬는 일꾼이 스스로 라즈베리를 따 온다.
    /// </summary>
    static class ChoresTest
    {
        public static void Register() => TestRunner.Register("vh_chorestest", "같이 캐기·바로 보충·쉴 때 먹거리 시험", () => TestKit.Flat(Run()));

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
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                Brain.Refill.Off();
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static int Count(TestKit.World w, string prefab) => GatherTest.CountBase(w.Home, prefab);
        static bool Idle(WorkerNpc n) => n.Ai.Job == null && n.Ai.Current == WorkerAI.Mode.Idle;
        static bool Gathering(WorkerNpc n, string item) => n.Ai.Job is GatherJob g && g.Item == item;
        static int TargetOf(WorkerNpc n) => n.Ai.Job is GatherJob g ? g.Target : 0;

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
            int sum = TargetOf(w.Ragnar) + TargetOf(w.Floki);
            checks.Add($"!나무 10 → 둘 다 나무 캐러 감(라그나 {TargetOf(w.Ragnar)}·플로키 {TargetOf(w.Floki)})", both && sum == 10);
            yield return TestKit.WaitFor(() => Count(w, "Wood") >= 10 && Idle(w.Ragnar) && Idle(w.Floki), 240f);
            checks.Add($"나무 10개 넘게 들어오고 둘 다 쉼 (나무 {Count(w, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);

            // 2) 계속채워 → 바로 출발
            int wood = Count(w, "Wood");
            GatherTest.Say($"!계속채워 나무 {wood + 10}", null);
            float t0 = Time.time;
            yield return TestKit.WaitFor(() => Gathering(w.Ragnar, "Wood") || Gathering(w.Floki, "Wood"), 10f);
            checks.Add($"!계속채워 → {Time.time - t0:0.0}초 만에 출발(10초 안)", TestKit.Ok);
            yield return TestKit.WaitFor(() => Count(w, "Wood") >= wood + 9 && Idle(w.Ragnar) && Idle(w.Floki), 240f);
            checks.Add($"보충 끝 (나무 {Count(w, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);
            Brain.Refill.Off();

            // 3) 쉴 때 먹거리: 음식 비우고 둘레에 라즈베리 덤불
            Vector3 right = Vector3.Cross(Vector3.up, (w.Chest.transform.position - w.Home).normalized);
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            for (int i = 0; i < 6; i++)
            {
                Vector3 at = w.Home + right * (12f + i * 2f) + Vector3.forward * ((i % 2) * 3f);
                made.Add(Object.Instantiate(ZNetScene.instance.GetPrefab("RaspberryBush"), GatherTest.Ground(at), Quaternion.identity));
            }
            TestKit.ClearBaseStock(w, Foods);
            yield return new WaitForSeconds(1f);
            t0 = Time.time;
            yield return TestKit.WaitFor(() => Gathering(w.Ragnar, "Raspberry") || Gathering(w.Floki, "Raspberry"), 70f);
            checks.Add($"음식 없음 → 쉬던 일꾼이 라즈베리 따러 감 ({Time.time - t0:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => Count(w, "Raspberry") >= 5, 180f);
            checks.Add($"라즈베리가 거점 상자에 들어옴 ({Count(w, "Raspberry")}, {TestKit.Waited:0}초)", TestKit.Ok);
        }
    }
}
