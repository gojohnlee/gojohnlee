using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_skiptest (멀리선 이동 생략 가-2, 사용자 결정 2026-10-23 "멀리선 단순하게"): 나무를 든 일꾼이 거점 120m 밖에서 돌아갈 때
    /// 걷지 않고 길이 ÷ 달리기 속도만큼 서서 기다리며(그동안 몸이 안 움직임, 2초마다 지도 위치 밝힘) 다 기다리면 목적지 쪽으로 옮겨져
    /// 마저 걸어 상자에 넣는지. 짐·체력 그대로, 나무 합 보존. 먼 곳(플레이어 64m 밖) 흉내는 GatherJob.TestIgnorePlayers
    /// (120m = 플레이어 둘레 불러와진 구역 안이라 땅이 있다). 물 피한 길은 WaterRoute 칸(가-3), 플레이어 다가옴·아이콘은 가-4~5 칸.
    /// </summary>
    static class SkipTest
    {
        public static void Register() => TestRunner.Register("vh_skiptest", "멀리선 이동 생략 시험", () => TestKit.Flat(Run()));

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
                GatherJob.TestIgnorePlayers = false;
                GatherJob.TestFakePlayer = null;
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static int Wood(Humanoid c)
        {
            int n = 0;
            foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "Wood") n += it.m_stack;
            return n;
        }

        /// <summary>거점에서 dist m, 물 아니고 평평한 땅(4방향 중 첫 곳).</summary>
        static bool FindLand(TestKit.World w, float dist, out Vector3 spot)
        {
            Quaternion rot = w.Player.transform.rotation;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = w.Home + rot * Quaternion.Euler(0f, 90f * i, 0f) * new Vector3(0f, 0f, dist);
                if (!ZoneSystem.instance.GetSolidHeight(p, out float h, out Vector3 normal, out GameObject go) || go == null) continue;
                if (go.GetComponentInParent<Heightmap>() == null || normal.y < 0.85f) continue;
                if (h < ZoneSystem.instance.m_waterLevel + 1f) continue;
                p.y = h;
                spot = p;
                return true;
            }
            spot = Vector3.zero;
            return false;
        }

        /// <summary>
        /// 가-3 칸: 거점 둘레에서 곧은 선이 깊은 물을 건너는 마른 땅 두 점을 찾아(게임 생성 지형) 도착점을 밝히고 GatherJob.FarRoute가
        /// 물을 돌아가는 거친 길을 주는지(두 점은 물가에서 16m 이상). 길 위 2m마다 깊은 물이 없고 직선보다 김. 그런 자리가 없으면 환경 불량.
        /// </summary>
        static void WaterRoute(TestKit.World w, GatherTest.Checks checks)
        {
            WorldGenerator wg = WorldGenerator.instance;
            float wl = ZoneSystem.instance.m_waterLevel;
            bool Deep(Vector3 p) => wl - wg.GetHeight(p.x, p.z) > (float)WaterRule.DeepDepth;
            bool Dry(Vector3 p) => wg.GetHeight(p.x, p.z) > wl + 1f;
            int tries = 0;
            for (int deg = 0; deg < 360 && tries < 60; deg += 15)
            {
                Vector3 dir = Quaternion.Euler(0f, deg, 0f) * Vector3.forward;
                Vector3 a = Vector3.zero;
                bool haveA = false, crossed = false;
                float dryRun = 0f; // 물 앞 마른 땅이 이어진 길이(m)
                for (float r = 16f; r <= 1500f && tries < 60; r += 4f)
                {
                    Vector3 p = w.Home + dir * r;
                    if (!crossed)
                    {
                        // 출발점은 물가에서 16m 뗀 마른 땅(물가 칸은 RoughPath가 봐주니 그 구간에 물이 없게)
                        if (Dry(p)) { dryRun += 4f; if (dryRun >= 20f) { a = p - dir * 16f; haveA = true; } }
                        else { if (Deep(p) && haveA) crossed = true; dryRun = 0f; }
                        continue;
                    }
                    if (!Dry(p) || !Dry(p + dir * 8f) || !Dry(p + dir * 16f)) continue;
                    Vector3 b = p + dir * 16f;
                    Scout.Reveal(b);
                    if (!Scout.ExploredAt(b)) continue;
                    List<RoughPath.P> path = GatherJob.FarRoute(a, b);
                    if (path.Count <= 2) // 못 돌아감(큰 강·바다 등): 이 방향 더 멀리
                    {
                        if (tries++ < 8) TestRunner.Log($"  {deg}° {r:0}m: 직선 {Utils.DistanceXZ(a, b):0}m 건넘 → {GatherJob.FarLastKind}({GatherJob.FarLastMs:0.0}ms)");
                        crossed = haveA = false;
                        dryRun = 0f;
                        continue;
                    }
                    float straight = Utils.DistanceXZ(a, b), len = (float)RoughPath.Length(path);
                    int wet = 0;
                    for (float t = 2f; t < len - 2f; t += 2f)
                    {
                        RoughPath.P q = RoughPath.PointAt(path, t);
                        if (Deep(new Vector3((float)q.X, 0f, (float)q.Z))) wet++;
                    }
                    TestRunner.Log($"물 건너는 두 점 {deg}°: 직선 {straight:0}m, 길 {len:0}m, {GatherJob.FarLastKind}, 찾기 {GatherJob.FarLastMs:0.0}ms");
                    checks.Add($"밝혀진 목적지 → 물 피한 거친 길 ({GatherJob.FarLastKind}, 깊은 물 지점 {wet}개)", GatherJob.FarLastKind.StartsWith("거친 길") && wet == 0);
                    checks.Add($"돌아가는 길이 직선보다 김 ({len:0}/{straight:0}m)", len > straight + 1f);
                    checks.Add($"길 찾기 100ms 안 ({GatherJob.FarLastMs:0.0}ms)", GatherJob.FarLastMs < 100f);
                    return;
                }
            }
            throw new TestKit.EnvBadException($"거점 1500m 안에 돌아갈 수 있는 깊은 물 건너기 자리 없음(시도 {tries})");
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            var ai = w.Ragnar.Ai;
            WaterRoute(w, checks);

            TestKit.ClearBaseStock(w, "Wood");
            int skips0 = GatherJob.FarSkips;
            GatherTest.Say($"!{Workers.Name1} 나무 30", null);
            yield return TestKit.WaitFor(() => ai.Job is GatherJob g && (g.StepName == "Search" || g.StepName == "Approach" || g.StepName == "GoZone"), 60f);
            checks.Add("일 시작함", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            checks.Add($"플레이어 곁에선 생략 안 함 ({GatherJob.FarSkips - skips0}번)", GatherJob.FarSkips == skips0);

            if (!FindLand(w, 120f, out Vector3 spot)) { checks.Add("거점 120m 밖 마른 평지 있음", false); yield break; }
            // 다 찼다고 보게 30개를 채워 준다 → 귀환(Return)
            int have = Wood(w.Ragnar);
            if (have < 30) w.Ragnar.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 30 - have);
            int skips1 = GatherJob.FarSkips, landed0 = GatherJob.FarLanded, cancel0 = GatherJob.FarCancelled;
            float hp0 = w.Ragnar.GetHealth();
            int total = Wood(w.Ragnar) + GatherTest.CountBase(w.Home, "Wood");
            GatherJob.TestIgnorePlayers = true;
            w.Ragnar.TeleportTo(spot + Vector3.up * 0.3f, w.Player.transform.rotation, false);
            yield return new WaitForSeconds(0.5f);
            if (!(ai.Job is GatherJob gj)) { checks.Add("일이 남아 있음", false); yield break; }
            gj.TestReturn();
            yield return TestKit.WaitFor(() => GatherJob.FarSkips > skips1, 30f);
            checks.Add($"멀리서 귀환 → 이동 생략 시작 ({ai.Job?.StepName}, 길 {GatherJob.FarLastLength:0}m, 기다림 {GatherJob.FarLastSeconds:0.0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            float start = Time.time;
            Vector3 at = w.Ragnar.transform.position;
            float expect = GatherJob.FarLastSeconds;
            checks.Add($"기다림 = 길 ÷ 달리기 속도 ({GatherJob.FarLastLength:0}m ÷ {w.Ragnar.m_runSpeed:0.0} = {expect:0.0}초)",
                Mathf.Abs(expect - GatherJob.FarLastLength / w.Ragnar.m_runSpeed) < 0.5f && GatherJob.FarLastLength > 100f);

            int carried = Wood(w.Ragnar);
            float stood = 0f; // 기다리는 동안(예상 시간 - 1초까지) 몸이 출발점에서 벗어난 최대 거리
            float iconGap = -1f, iconBody = 0f; // 기다린 지 6초쯤 지도 아이콘: 지금 지도상 위치와의 거리, 몸과의 거리
            int received0 = MapIcons.Received;
            yield return TestKit.WaitFor(() =>
            {
                if (Time.time - start < expect - 1f) stood = Mathf.Max(stood, Utils.DistanceXZ(w.Ragnar.transform.position, at));
                if (iconGap < 0f && Time.time - start > 6f && Time.time - start < expect - 2f && ai.Job is GatherJob fj && fj.FarMapPos.HasValue)
                    foreach (var pin in MapIcons.Pins)
                        if (pin != null && pin.m_name == w.Ragnar.NpcName)
                        {
                            iconGap = Utils.DistanceXZ(pin.m_pos, fj.FarMapPos.Value);
                            iconBody = Utils.DistanceXZ(pin.m_pos, w.Ragnar.transform.position);
                        }
                return GatherJob.FarLanded > landed0 || GatherJob.FarCancelled > cancel0;
            }, expect + 40f);
            float took = Time.time - start;
            Vector3 landedAt = w.Ragnar.transform.position;
            checks.Add($"다 기다린 뒤 옮겨짐 (끝 {GatherJob.FarLanded - landed0}, 그만둠 {GatherJob.FarCancelled - cancel0}, {took:0.0}초)", GatherJob.FarLanded > landed0 && GatherJob.FarCancelled == cancel0);
            checks.Add($"기다리는 동안 걷지 않음 (출발점에서 최대 {stood:0.0}m)", stood < 2f);
            checks.Add($"기다린 시간 ≥ 예상의 95% ({took:0.0}/{expect:0.0}초)", took >= expect * 0.95f);
            checks.Add($"기다리는 동안 밝힌 지도 위치 {GatherJob.FarReveals}번 (2초마다 + 끝)", GatherJob.FarReveals >= Mathf.FloorToInt(expect / 2f));
            int icons = 0;
            foreach (var pin in MapIcons.Pins) if (pin != null && (pin.m_name == w.Ragnar.NpcName || pin.m_name == w.Floki.NpcName)) icons++;
            checks.Add($"지도 아이콘이 생략 중 지도상 위치에 (아이콘–지도 위치 {iconGap:0.0}m, 아이콘–몸 {iconBody:0}m, 받음 {MapIcons.Received - received0}번, 일꾼 아이콘 {icons}개)",
                iconGap >= 0f && iconGap <= 20f && iconBody > 15f && MapIcons.Received - received0 >= Mathf.FloorToInt(took / 2f) - 1 && icons == 2);
            checks.Add($"목적지 20m 안에 내림 (거점까지 {Utils.DistanceXZ(landedAt, w.Home):0.0}m, 출발점에서 {Utils.DistanceXZ(landedAt, at):0}m)", Utils.DistanceXZ(landedAt, w.Home) <= 21f);
            bool ground = ZoneSystem.instance.GetSolidHeight(landedAt, out float h);
            checks.Add($"단단한 땅 위 (높이 {landedAt.y:0.0}/땅 {h:0.0})", ground && Mathf.Abs(landedAt.y - h) < 1.5f && Floating.GetLiquidLevel(landedAt) < h + 0.1f);
            checks.Add($"짐 그대로 ({carried} → {Wood(w.Ragnar)})", Wood(w.Ragnar) == carried);
            checks.Add($"체력 그대로 ({hp0:0.0} → {w.Ragnar.GetHealth():0.0})", w.Ragnar.GetHealth() >= hp0 - 1f);

            GatherJob.TestIgnorePlayers = false;
            yield return TestKit.WaitFor(() => Wood(w.Ragnar) == 0, 60f);
            int after = Wood(w.Ragnar) + GatherTest.CountBase(w.Home, "Wood");
            checks.Add($"마저 걸어 상자에 넣음 (짐 {Wood(w.Ragnar)}, 합 {total} → {after})", TestKit.Ok && after == total);
            yield return Drop(w, checks, spot);
        }

        /// <summary>
        /// 가-4 칸: 생략 중 플레이어가 지도상 위치 64m 안에 오면(GatherJob.TestFakePlayer로 흉내 — 몸에선 64m 밖) 몸을 그 근처 안전한 땅에 내려
        /// 거기서부터 걷는다. 짐·체력 그대로, 상자에 넣음.
        /// </summary>
        static IEnumerator Drop(TestKit.World w, GatherTest.Checks checks, Vector3 spot)
        {
            var ai = w.Ragnar.Ai;
            GatherJob.TestIgnorePlayers = false;
            TestKit.ClearBaseStock(w, "Wood");
            if (!(ai.Job is GatherJob))
            {
                GatherTest.Say($"!{Workers.Name1} 나무 30", null);
                yield return TestKit.WaitFor(() => ai.Job is GatherJob g && (g.StepName == "Search" || g.StepName == "Approach" || g.StepName == "GoZone"), 60f);
            }
            if (!(ai.Job is GatherJob gj)) { checks.Add("다가옴 칸: 일이 남아 있음", false); yield break; }
            int have = Wood(w.Ragnar);
            if (have < 30) w.Ragnar.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 30 - have);
            int skips1 = GatherJob.FarSkips, drop0 = GatherJob.FarDropped, cancel0 = GatherJob.FarCancelled, landed0 = GatherJob.FarLanded;
            float hp0 = w.Ragnar.GetHealth();
            int total = Wood(w.Ragnar) + GatherTest.CountBase(w.Home, "Wood");
            GatherJob.TestIgnorePlayers = true;
            w.Ragnar.TeleportTo(spot + Vector3.up * 0.3f, w.Player.transform.rotation, false);
            yield return new WaitForSeconds(0.5f);
            gj.TestReturn();
            yield return TestKit.WaitFor(() => GatherJob.FarSkips > skips1, 30f);
            checks.Add("다가옴 칸: 이동 생략 시작", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            Vector3 at = w.Ragnar.transform.position;
            yield return TestKit.WaitFor(() => GatherJob.FarReveals >= 4, 20f);
            int carried = Wood(w.Ragnar);
            // 몸에서 거점 쪽으로 (지도 위치 거리 + 30m, 최소 70m)에 가짜 플레이어 → 다음 밝힐 때(2초 안) 지도 위치는 64m 안, 몸은 64m 밖
            Vector3 map = GatherJob.FarLastMap;
            Vector3 fake = at + (w.Home - at).normalized * Mathf.Max(Utils.DistanceXZ(at, map) + 30f, 70f);
            GatherJob.TestFakePlayer = fake;
            float bodyToFake = Utils.DistanceXZ(at, fake);
            yield return TestKit.WaitFor(() => GatherJob.FarDropped > drop0 || GatherJob.FarCancelled > cancel0 || GatherJob.FarLanded > landed0, 10f);
            Vector3 landedAt = w.Ragnar.transform.position;
            checks.Add($"지도 위치에 다가오면 그 근처에 내림 (내림 {GatherJob.FarDropped - drop0}, 그만둠 {GatherJob.FarCancelled - cancel0}, 끝 {GatherJob.FarLanded - landed0}, 몸–플레이어 {bodyToFake:0}m)",
                GatherJob.FarDropped > drop0 && GatherJob.FarCancelled == cancel0 && bodyToFake > 64f);
            checks.Add($"내린 자리 = 그때 지도 위치 근처 ({Utils.DistanceXZ(landedAt, GatherJob.FarLastMap):0.0}m, 출발점에서 {Utils.DistanceXZ(landedAt, at):0}m)",
                Utils.DistanceXZ(landedAt, GatherJob.FarLastMap) <= 12f && Utils.DistanceXZ(landedAt, at) > 20f);
            bool ground = ZoneSystem.instance.GetSolidHeight(landedAt, out float h);
            checks.Add($"다가옴 칸: 단단한 땅 위 (높이 {landedAt.y:0.0}/땅 {h:0.0})", ground && Mathf.Abs(landedAt.y - h) < 1.5f && Floating.GetLiquidLevel(landedAt) < h + 0.1f);
            checks.Add($"다가옴 칸: 짐·체력 그대로 (짐 {carried} → {Wood(w.Ragnar)}, 체력 {hp0:0} → {w.Ragnar.GetHealth():0})", Wood(w.Ragnar) == carried && w.Ragnar.GetHealth() >= hp0 - 1f);
            int skips2 = GatherJob.FarSkips;
            yield return TestKit.WaitFor(() => Wood(w.Ragnar) == 0, 60f);
            int after = Wood(w.Ragnar) + GatherTest.CountBase(w.Home, "Wood");
            checks.Add($"내린 자리부터 걸어 상자에 넣음 (다시 생략 {GatherJob.FarSkips - skips2}번, 합 {total} → {after})", TestKit.Ok && after == total && GatherJob.FarSkips == skips2);
            GatherJob.TestFakePlayer = null;
            GatherJob.TestIgnorePlayers = false;
        }
    }
}
