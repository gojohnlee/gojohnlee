using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>창자 20개 사냥 중 두 번 다침 → 집 대기·근처 채집 대기 뒤 이어 가 채움(요청 다침 ⑤). 회복 시간·왕복·포탈은 DIAG로 history에.</summary>
    static class EntrailsTest
    {
        const int Goal = 20;

        public static void Register() => TestRunner.Register("vh_entrailstest", "창자 20개 사냥·다침 회복 시험", () => TestKit.Flat(Run()), 1500f);

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
            var spawned = new List<GameObject>();
            try { yield return Body(w, checks, spawned); }
            finally
            {
                foreach (var g in spawned) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static WorkerNpc Hunter(TestKit.World w)
        {
            foreach (var n in new[] { w.Ragnar, w.Floki })
                if (n != null && n.Ai.Job is GatherJob g && g.Item == "Entrails" && (g.StepName == "Hunt" || g.StepName == "Collect")) return n;
            return null;
        }

        static int Alive(List<GameObject> list, GameObject prefab)
        {
            int n = 0;
            foreach (var g in list)
                if (g != null && g.name.StartsWith(prefab.name) && g.TryGetComponent(out Character c) && !c.IsDead()) n++;
            return n;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned)
        {
            w.CountsCreatures = true;
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            GameObject prey = null;
            foreach (string name in new[] { "Draugr", "Blob" })
            {
                var p = ZNetScene.instance.GetPrefab(name);
                if (p != null && Prey.Is("Entrails", p.GetComponent<Character>())) { prey = p; break; }
            }
            checks.Add($"창자 사냥감 ({prey?.name ?? "없음"})", prey != null);
            GameObject shroom = ZNetScene.instance.GetPrefab("Pickable_Mushroom");
            checks.Add("창자는 사냥으로 얻음(고기 더미 줍기 아님)", ResourceCatalog.WaysFor("Entrails").Contains(ToolKind.Hunt));
            if (prey == null) yield break;

            foreach (var n in new[] { w.Ragnar, w.Floki }) // 드라우그는 가죽 차림 혼자엔 셈(preyStrong, 맞는 판단) — 철 차림·청동 칼
            {
                if (n == null) continue;
                foreach (var a in new[] { "ArmorIronChest", "ArmorIronLegs", "HelmetIron", "SwordBronze" }) n.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(a), 1);
                Prep.WearBest(n);
            }
            TestKit.ClearBaseStock(w, "Entrails");
            w.Ragnar.ClearHurt(); w.Floki?.ClearHurt();
            Quaternion rot = w.Player.transform.rotation;
            int portals0 = PortalBuilder.Mine().Count, wave = 0, hurtNo = 0, trips = 0;
            float start = Time.time, lastGone = -100f, idleSince = -1f;
            bool idleEnd = false;
            var recover = new List<string>();
            WorkerNpc lastHunter = null;
            bool wasHome = true;
            GatherTest.Say($"!Entrails {Goal}", null);
            while (GatherTest.CountBase(w.Home, "Entrails") < Goal && Time.time - start < 1380f)
            {
                if (Alive(spawned, prey) > 0) lastGone = Time.time;
                // 일꾼 누구도 일·대기열이 없는 채 60초(게임 시간) 넘으면 더 기다리지 않고 FAIL로 끝냄(사용자 11-14 00:58)
                bool busy = w.Ragnar.Ai.Job != null || w.Ragnar.Ai.Queue.Count > 0 || (w.Floki != null && (w.Floki.Ai.Job != null || w.Floki.Ai.Queue.Count > 0));
                if (busy || GatherTest.CountBase(w.Home, "Entrails") >= Goal) idleSince = -1f;
                else if (idleSince < 0f) idleSince = Time.time;
                if (idleSince >= 0f && Time.time - idleSince > 60f) { idleEnd = true; break; }
                if (Alive(spawned, prey) == 0 && wave < 60 && Time.time - lastGone > 3f) // 한 마리씩(넷이면 preyPack, 맞는 판단), 집에서 45~55m. 죽고 3초 뒤(20초면 그사이 탐험 나가 막혀 cantReach — 회차 580, 창자는 확률 떨굼이라 60마리까지)
                {
                    for (int i = 0; i < 1; i++)
                    {
                        Vector3 pos = GatherTest.Ground(w.Home + rot * new Vector3((wave % 5 - 2) * 6f, 0f, 45f + (wave % 3) * 5f));
                        var go = Object.Instantiate(prey, pos + Vector3.up * 0.5f, Quaternion.identity);
                        go.GetComponent<Character>().SetLevel(1);
                        spawned.Add(go);
                    }
                    wave++;
                }
                WorkerNpc h = Hunter(w);
                if (h != null) lastHunter = h;
                bool home = lastHunter == null || Utils.DistanceXZ(lastHunter.transform.position, lastHunter.Home) < 15f;
                if (wasHome && !home) trips++;
                wasHome = home;
                int got = GatherTest.CountBase(w.Home, "Entrails");
                if (h != null && Utils.DistanceXZ(h.transform.position, h.Home) > 15f && (hurtNo == 0 && got >= 4 || hurtNo == 1 && got >= 12))
                {
                    hurtNo++;
                    if (hurtNo == 2 && shroom != null) // 두 번째는 둘레에 버섯 → 근처 채집 대기
                    {
                        Vector3 back = (h.Home - h.transform.position); back.y = 0f; back = back.normalized;
                        for (int i = 0; i < 4; i++)
                            spawned.Add(Object.Instantiate(shroom, GatherTest.Ground(h.transform.position + back * (6f + i * 2f) + Vector3.Cross(back, Vector3.up) * (i - 1.5f) * 2f), Quaternion.identity));
                    }
                    foreach (var g in spawned) // 사냥감이 25m 안이면 근처 회복 안 함(맞는 동작) — 남은 적은 멀리 둬서 회복 길을 봄
                        if (hurtNo == 2 && g != null && g.name.StartsWith(prey.name) && Utils.DistanceXZ(g.transform.position, h.transform.position) < 30f)
                            g.transform.position = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 95f)) + Vector3.up * 0.5f;
                    h.SetHealth(h.GetMaxHealth() * 0.3f);
                    float t0 = Time.time;
                    yield return TestKit.WaitFor(() => h.Ai.Queue.Count > 0 && h.Ai.Queue[0].Why == "다침", 90f);
                    bool near = h.Ai.Job is RecoverNearJob;
                    checks.Add($"{hurtNo}번째 다침 → '다침' 칸 ({(near ? "근처 채집 대기" : "집 대기")}, {TestKit.Waited:0}초, 창자 {got}개)", TestKit.Ok);
                    if (!TestKit.Ok) yield break;
                    yield return TestKit.WaitFor(() => h.Ai.Job is GatherJob g2 && g2.Item == "Entrails", 420f);
                    recover.Add($"{hurtNo}번 {(near ? "근처" : "집")} {Time.time - t0:0}초(체력 {h.GetHealth() / h.GetMaxHealth() * 100f:0}%, 집에서 {Utils.DistanceXZ(h.transform.position, h.Home):0}m)");
                    checks.Add($"{hurtNo}번째 회복 뒤 이어 감 ({Time.time - t0:0}초)", TestKit.Ok);
                    if (!TestKit.Ok) yield break;
                }
                yield return new WaitForSeconds(1f);
            }
            int have = GatherTest.CountBase(w.Home, "Entrails"), portals = PortalBuilder.Mine().Count - portals0;
            checks.Add($"창자 {Goal}개 채움 ({have}개, {Time.time - start:0}초, 무리 {wave}{(idleEnd ? ", 일꾼 일 없이 60초 넘어 일찍 끝냄" : "")})", have >= Goal);
            checks.Add($"두 번 다침 시험함 ({hurtNo}/2)", hurtNo == 2);
            Plugin.Log.LogInfo($"DIAG 창자 회복: {string.Join(" · ", recover)} · 집 나섬 {trips}번 · 포탈 새로 {portals}개 · 다침 칸 기록 {lastHunter?.NpcName}");
        }
    }
}
