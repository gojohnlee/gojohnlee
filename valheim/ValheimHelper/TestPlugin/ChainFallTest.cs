using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_chainfalltest(사용자 11-12 나무 연쇄 쓰러짐): 깃발에서 바깥으로 5m마다 너도밤나무 한 줄(14~39m) — 일꾼이 첫 나무를 치면 바깥으로 넘어져 다음 나무를 쓰러뜨린다.
    /// "나무 1"만 시켜도 연쇄로 쓰러진 나무의 통나무까지 자기 통나무로 끝까지 쪼갬. 기준: 연쇄 1번 이상·줄 둘레 통나무 0·순간이동 0. 둘레 나무를 치우니 묶음 끝에 둔다.
    /// </summary>
    static class ChainFallTest
    {
        public static void Register() => TestRunner.Register("vh_chainfalltest", "나무 연쇄 쓰러짐 — 맞아 쓰러진 나무 통나무도 끝까지", () => TestKit.Flat(Run()));

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
            try { yield return Body(w, checks); }
            finally
            {
                if (w.Ragnar != null) w.Ragnar.GetInventory().RemoveItem("$item_wood", 999);
                TestKit.ClearBaseStock(w, "Wood");
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;

            // 1) 줄 방향: 깃발에서 땅이 가장 고른 쪽(8방향, 14~40m 높이 차 최소)
            Vector3 home = w.Home, dir = Vector3.forward;
            float bestSpread = float.MaxValue;
            for (int a = 0; a < 8; a++)
            {
                Vector3 d = Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward;
                float lo = float.MaxValue, hi = float.MinValue;
                for (float r = 12f; r <= 42f; r += 3f)
                {
                    if (!ZoneSystem.instance.GetGroundHeight(home + d * r, out float h)) { lo = float.MinValue; break; }
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
                if (lo == float.MinValue || ZoneSystem.instance.GetSolidHeight(home + d * 25f) < ZoneSystem.instance.m_waterLevel + 0.5f) continue;
                if (hi - lo < bestSpread) { bestSpread = hi - lo; dir = d; }
            }
            int cleared = HouseTest.ClearPlants(home + dir * 26f, 24f);
            // 줄 반대쪽 깃발 곁 나무를 먼저 베면 줄을 안 건드리고 끝남(회차 719: 2m 앞 Beech_small1, 5초) → 첫 줄 나무(14m)보다 가까운 것은 다 치움
            cleared += HouseTest.ClearPlants(home, 16f);
            // 깃발 곁 나뭇가지를 주우면 "나무 1"이 나무를 안 베고 끝난다(회차 711·713: 2.6m 앞 Pickable_Branch, 2초) → 60m 안 나뭇가지를 치움
            var branches = new HashSet<ZNetView>();
            foreach (var col in Physics.OverlapSphere(home, 60f))
            {
                ZNetView nv = col.GetComponentInParent<ZNetView>();
                if (nv != null && nv.IsValid() && nv.GetComponent<Pickable>() != null && nv.name.StartsWith("Pickable_Branch") && branches.Add(nv)) ZNetScene.instance.Destroy(nv.gameObject);
            }
            cleared += branches.Count;
            yield return new WaitForSeconds(1f);
            var row = new List<Vector3>();
            GameObject beech = ZNetScene.instance.GetPrefab("Beech1");
            for (float r = 14f; r <= 39f; r += 5f) // 3m 간격은 통나무가 다음 나무에 천천히 기대 서 연쇄 0번(회차 718 B13b, 기울기 7°) — 5m로 속도 붙여 부딪힘
            {
                Vector3 p = home + dir * r;
                ZoneSystem.instance.GetGroundHeight(p, out float h);
                p.y = h;
                Object.Instantiate(beech, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                row.Add(p);
            }
            // 첫 나무는 일꾼 반대로 쓰러지는데 일꾼이 비스듬히 서면 줄을 비껴 연쇄 0번(회차 714 B13b, 선 나무 8/9) — 둘째·셋째 자리 양옆 3m에도 한 그루씩
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * 3f;
            foreach (float r in new[] { 19f, 24f })
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 p = home + dir * r + side * s;
                    ZoneSystem.instance.GetGroundHeight(p, out float h);
                    p.y = h;
                    Object.Instantiate(beech, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                }
            // 양옆 3m는 ±31°까지만 — 회차 722 B13b 첫 나무가 줄에서 66° 비껴 넘어져 연쇄 0번(일꾼이 옆에서 침) → 첫 나무 둘레 ±55°·±80°에도(집에서 14m보다 멀어 첫 목표는 그대로)
            foreach (float a in new[] { -80f, -55f, 55f, 80f })
            {
                Vector3 p = row[0] + Quaternion.Euler(0f, a, 0f) * dir * 7f; // 5m는 비껴 넘어진 통나무가 천천히 기대 서 안 쓰러뜨림(회차 729: 39° 비껴 19° 기울어 멈춤) — 7m로 속도 붙여
                ZoneSystem.instance.GetGroundHeight(p, out float h);
                p.y = h;
                Object.Instantiate(beech, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
            TestRunner.Log($"  줄: 방향 {dir:F1} 나무 {row.Count}그루 14~39m 5m 간격(+양옆 4·첫 나무 둘레 7m 4), 높이 차 {bestSpread:0.0}m, 치운 것 {cleared}");
            yield return new WaitForSeconds(2f);

            // 2) 나무 1 — 첫 나무가 바깥으로 넘어져 연쇄
            TestKit.ClearBaseStock(w, "Wood");
            TestKit.PutInChest(w.Chest, "AxeFlint", 1);
            // 라그나를 줄 안쪽 8m에 세움: 앞 시험 뒤 서 있던 자리에 따라 첫 나무 둘레 곁나무를 먼저 쳐 옆으로 넘어져 연쇄 0번(회차 728 묶음, 단독은 2/2 PASS)
            Vector3 stand = home + dir * 8f;
            if (ZoneSystem.instance.GetGroundHeight(stand, out float sh)) stand.y = sh;
            w.Ragnar.TeleportTo(stand, Quaternion.LookRotation(dir), false);
            yield return new WaitForSeconds(1f);
            int tp0 = Teleports(), chain0 = GatherJob.ChainFelled;
            GatherJob.SkippedLogs.Clear();
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 1", null);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is GatherJob, 15f);
            checks.Add("나무 일 받음", w.Ragnar.Ai.Job is GatherJob);
            float t0 = Time.time;
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob), 480f);
            int chain = GatherJob.ChainFelled - chain0;
            int standing = 0;
            var left = new List<string>();
            int leftUnskipped = 0;
            foreach (var z in Senses.Nearby(home + dir * 26f, 30f))
            {
                if (ResourceCatalog.Get(z.GetPrefab()) is ResourceInfo i && i.Log)
                {
                    bool skipped = GatherJob.SkippedLogs.TryGetValue(z.m_uid, out string why);
                    if (!skipped) leftUnskipped++;
                    left.Add($"{Name(z)} {z.GetPosition():F1}{(skipped ? " 건너뜀:" + why : "")}");
                }
                else if (Name(z) == "Beech1") standing++;
            }
            if (left.Count > 0) TestRunner.Log($"  남은 통나무: {string.Join(" · ", left.ToArray())}");
            TestRunner.Log($"  연쇄 {chain}번, 선 나무 {standing}/{row.Count}, {Time.time - t0:0}초");
            checks.Add($"연쇄 쓰러짐 1번 이상 ({chain}번)", chain >= 1);
            checks.Add($"남은 통나무 0 (남음 {left.Count}, 건너뛰지 않은 것 {leftUnskipped})", leftUnskipped == 0);
            checks.Add($"순간이동 없음 ({Teleports() - tp0}번)", Teleports() == tp0);
            checks.Add($"쪼갠 나무가 상자에 ({GatherTest.CountBase(w.Home, "Wood")}개)", GatherTest.CountBase(w.Home, "Wood") > 0);
        }

        static string Name(ZDO z) => ZNetScene.instance.GetPrefab(z.GetPrefab()) is GameObject g ? g.name : "?";
        static int Teleports() => WorkerAI.StuckTeleports + WorkerAI.TrapRescues + WorkerAI.PitEdgeRescues;
    }
}
