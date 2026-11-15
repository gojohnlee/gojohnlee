using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_houselogtest (E083 집 곁 통나무, 사용자 11-12): 시험 기본 집(VH_HOUSE=1) 1층 벽·돌 바닥 가장자리·계단·문 곁에 통나무(통·반토막)를 놓고
    /// 라그나르의 자기 통나무(쓰러뜨린 자리)로 알린 뒤 나무를 시킨다. 닿는 자리에서 쳐 끝까지 쪼개고, 못 닿으면 되풀이 말고 건너뜀(SkippedLogs) — diag에 자리·까닭.
    /// 기준: 남은 통나무 0(건너뜀만 남음)·사라진 조각 0·순간이동 0(긁힘은 기록만, 사용자 11-12).
    /// </summary>
    static class HouseLogTest
    {
        public static void Register() => TestRunner.Register("vh_houselogtest", "집 곁 통나무 끝까지 쪼갬(벽·바닥 가장자리·계단·문)", () => TestKit.Flat(Run()));

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
                foreach (var g in made) if (g != null && g.GetComponent<ZNetView>()?.IsValid() == true) ZNetScene.instance.Destroy(g);
                if (w.Ragnar != null) w.Ragnar.GetInventory().RemoveItem("$item_wood", 999);
                TestKit.ClearBaseStock(w, "Wood");
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static int Mask => LayerMask.GetMask("piece", "Default", "static_solid", "terrain", "vehicle");

        static string Name(ZDO z) => ZNetScene.instance.GetPrefab(z.GetPrefab()) is GameObject g ? g.name : "?";

        /// <summary>자리 이름 → 집 조각 이름에 들어간 말(1층만, 가장자리 = 집 가운데에서 먼 것·문/계단은 상자에 가까운 것).</summary>
        sealed class Kind { public string spot, log; public string[] keys; public bool outer; public Kind(string s, string[] k, bool o, string l) { spot = s; keys = k; outer = o; log = l; } } // ValueTuple은 게임 Mono에 없음
        static readonly Kind[] Kinds =
        {
            new Kind("벽", new[] { "wall" }, true, "beech_log"),
            new Kind("돌 바닥 가장자리", new[] { "stone_floor" }, true, "beech_log_half"),
            new Kind("계단", new[] { "stair" }, false, "beech_log_half"),
            new Kind("문 앞", new[] { "door" }, false, "beech_log"),
        };

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            checks.Add("시험 기본 집(VH_HOUSE=1)", w.Site != null);
            if (w.Ragnar == null || w.Chest == null || w.Site == null) yield break;
            Vector3 c = w.Site.Center;
            float y1 = w.Chest.transform.position.y;

            // 1) 자리 고르기: 1층(상자 높이 ±2m) 조각 중 종류마다 하나, 그 조각에서 집 가운데 반대쪽 1.4m 바닥·땅
            var pieces = new List<ZDO>();
            foreach (var z in Senses.Nearby(c, 30f))
                if (Buildings.IsPiece(z.GetPrefab())) pieces.Add(z);
            var spots = new List<Vector3>();
            var spotNames = new List<string>();
            foreach (var k in Kinds)
            {
                ZDO pick = null; float score = float.MinValue;
                foreach (var z in pieces)
                {
                    Vector3 p = z.GetPosition();
                    if (Mathf.Abs(p.y - y1) > 2f) continue;
                    string n = Name(z).ToLowerInvariant();
                    bool hit = false;
                    foreach (var key in k.keys) if (n.Contains(key)) hit = true;
                    if (!hit) continue;
                    float s = k.outer ? Utils.DistanceXZ(p, c) : -Utils.DistanceXZ(p, w.Chest.transform.position);
                    if (s > score) { score = s; pick = z; }
                }
                if (pick == null) { TestRunner.Log($"  {k.spot}: 1층 조각 없음 → 건너뜀"); continue; }
                Vector3 pp = pick.GetPosition(), away = pp - c;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                away.Normalize();
                Vector3 at = pp + away * 1.4f;
                at.y = y1 + 1.6f;
                at = Physics.Raycast(at, Vector3.down, out RaycastHit h, 6f, Mask) ? h.point : new Vector3(at.x, y1, at.z);
                GameObject lp = ZNetScene.instance.GetPrefab(k.log);
                if (lp == null) { TestRunner.Log($"  {k.spot}: {k.log} 프리팹 없음"); continue; }
                // 긴 쪽이 조각을 따라 눕게(통나무 프리팹은 긴 축이 위 — 옆으로 뉘임)
                var g = Object.Instantiate(lp, at + Vector3.up * 0.5f, Quaternion.LookRotation(away) * Quaternion.Euler(0f, 0f, 90f));
                made.Add(g);
                spots.Add(at); spotNames.Add(k.spot);
                TestRunner.Log($"  {k.spot}: {Name(pick)} {pp:F1} 곁 {k.log} {at:F1}(땅 위 {at.y - (ZoneSystem.instance.GetGroundHeight(at, out float gh) ? gh : at.y):0.0}m)");
            }
            checks.Add($"집 곁 통나무 놓음({spots.Count}/4: {string.Join("·", spotNames.ToArray())})", spots.Count >= 3);
            if (spots.Count == 0) yield break;
            yield return new WaitForSeconds(4f); // 놓은 통나무가 가라앉을 때까지 — 체력 기준은 그 뒤

            // 2) 기준: 자리 8m 안 조각 체력·통나무 수
            var health = new Dictionary<ZDOID, float>();
            var names = new Dictionary<ZDOID, string>();
            foreach (var z in pieces)
                foreach (var s in spots)
                    if (Vector3.Distance(z.GetPosition(), s) < 8f) { health[z.m_uid] = z.GetFloat(ZDOVars.s_health, -1f); names[z.m_uid] = $"{Name(z)} {z.GetPosition():F1}"; break; }
            int logs0 = Logs(spots);
            TestRunner.Log($"  기준: 조각 {health.Count}개(자리 8m), 통나무 {logs0}개");

            // 3) 나무 시키고 자기 통나무로 알림
            TestKit.ClearBaseStock(w, "Wood");
            TestKit.PutInChest(w.Chest, "AxeFlint", 1);
            int tp0 = Teleports();
            GatherJob.SkippedLogs.Clear();
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 1", null);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is GatherJob, 15f);
            checks.Add("나무 일 받음", w.Ragnar.Ai.Job is GatherJob);
            if (!(w.Ragnar.Ai.Job is GatherJob job)) yield break;
            foreach (var s in spots) job.TestFelled(s);

            // 4) 통나무가 다 없어지거나(건너뛴 것만 남음) 일이 끝날 때까지
            float t0 = Time.time;
            yield return TestKit.WaitFor(() => Logs(spots, skip: true) == 0 || !(w.Ragnar.Ai.Job is GatherJob), 360f);
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob), 90f); // 쪼갠 나무 줍기·넣기까지
            int left = Logs(spots), leftUnskipped = Logs(spots, skip: true);
            var leftAt = new List<string>();
            foreach (var s in spots)
                foreach (var z in Senses.Nearby(s, 15f))
                    if (IsLog(z)) leftAt.Add($"{Name(z)} {z.GetPosition():F1}{(GatherJob.SkippedLogs.TryGetValue(z.m_uid, out string why) ? " 건너뜀:" + why : "")}");
            if (leftAt.Count > 0) TestRunner.Log($"  남은 통나무: {string.Join(" · ", leftAt.ToArray())}");
            checks.Add($"남은 통나무 0 (남음 {left}, 그중 건너뛰지 않은 것 {leftUnskipped}, {Time.time - t0:0}초)", leftUnskipped == 0);

            int changed = 0, gone = 0;
            var bad = new List<string>();
            foreach (var kv in health)
            {
                ZDO z = ZDOMan.instance.GetZDO(kv.Key);
                if (z == null) { gone++; bad.Add("사라짐 " + names[kv.Key]); continue; }
                float now = z.GetFloat(ZDOVars.s_health, -1f);
                if (Mathf.Abs(now - kv.Value) > 0.01f) { changed++; bad.Add($"{names[kv.Key]} {kv.Value:0}→{now:0}"); }
            }
            if (bad.Count > 0) TestRunner.Log($"  조각 변화: {string.Join(" · ", bad.ToArray())}");
            // 긁힘(체력 조금 깎임)은 괜찮음 — 사라진 조각만 FAIL(사용자 11-12)
            checks.Add($"사라진 조각 0 (사라짐 {gone}, 긁힘 {changed} / {health.Count})", gone == 0);
            checks.Add($"순간이동 없음 ({Teleports() - tp0}번)", Teleports() == tp0);
            checks.Add($"쪼갠 나무가 상자에 ({GatherTest.CountBase(w.Home, "Wood")}개)", GatherTest.CountBase(w.Home, "Wood") > 0);
        }

        static int Teleports() => WorkerAI.StuckTeleports + WorkerAI.TrapRescues + WorkerAI.PitEdgeRescues;

        static bool IsLog(ZDO z) => ResourceCatalog.Get(z.GetPrefab()) is ResourceInfo i && i.Log;

        /// <summary>자리마다 15m(OwnLog 범위) 안 통나무 수(겹친 것은 한 번). skip이면 건너뛴 것은 뺌.</summary>
        static int Logs(List<Vector3> spots, bool skip = false)
        {
            var seen = new HashSet<ZDOID>();
            foreach (var s in spots)
                foreach (var z in Senses.Nearby(s, 15f))
                    if (IsLog(z) && !(skip && GatherJob.SkippedLogs.ContainsKey(z.m_uid))) seen.Add(z.m_uid);
            return seen.Count;
        }
    }
}
