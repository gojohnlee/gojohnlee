using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_skiptest (멀리선 이동 생략 가-2, 사용자 결정 2026-10-23 "멀리선 단순하게"): 나무를 든 일꾼이 거점 120m 밖에서 돌아갈 때
    /// 걷지 않고 길이 ÷ 달리기 속도만큼 서서 기다리며(그동안 몸이 안 움직임, 2초마다 지도 위치 밝힘) 다 기다리면 목적지 쪽으로 옮겨져
    /// 마저 걸어 상자에 넣는지. 짐·체력 그대로, 나무 합 보존. 먼 곳(플레이어 64m 밖) 흉내는 GatherJob.TestIgnorePlayers
    /// (120m = 플레이어 둘레 불러와진 구역 안이라 땅이 있다). 가는 길 밝혀짐·물 피한 길·플레이어 다가옴·아이콘은 가-3~5 칸.
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

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            var ai = w.Ragnar.Ai;

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
            yield return TestKit.WaitFor(() =>
            {
                if (Time.time - start < expect - 1f) stood = Mathf.Max(stood, Utils.DistanceXZ(w.Ragnar.transform.position, at));
                return GatherJob.FarLanded > landed0 || GatherJob.FarCancelled > cancel0;
            }, expect + 40f);
            float took = Time.time - start;
            Vector3 landedAt = w.Ragnar.transform.position;
            checks.Add($"다 기다린 뒤 옮겨짐 (끝 {GatherJob.FarLanded - landed0}, 그만둠 {GatherJob.FarCancelled - cancel0}, {took:0.0}초)", GatherJob.FarLanded > landed0 && GatherJob.FarCancelled == cancel0);
            checks.Add($"기다리는 동안 걷지 않음 (출발점에서 최대 {stood:0.0}m)", stood < 2f);
            checks.Add($"기다린 시간 ≥ 예상의 95% ({took:0.0}/{expect:0.0}초)", took >= expect * 0.95f);
            checks.Add($"기다리는 동안 밝힌 지도 위치 {GatherJob.FarReveals}번 (2초마다 + 끝)", GatherJob.FarReveals >= Mathf.FloorToInt(expect / 2f));
            checks.Add($"목적지 20m 안에 내림 (거점까지 {Utils.DistanceXZ(landedAt, w.Home):0.0}m, 출발점에서 {Utils.DistanceXZ(landedAt, at):0}m)", Utils.DistanceXZ(landedAt, w.Home) <= 21f);
            bool ground = ZoneSystem.instance.GetSolidHeight(landedAt, out float h);
            checks.Add($"단단한 땅 위 (높이 {landedAt.y:0.0}/땅 {h:0.0})", ground && Mathf.Abs(landedAt.y - h) < 1.5f && Floating.GetLiquidLevel(landedAt) < h + 0.1f);
            checks.Add($"짐 그대로 ({carried} → {Wood(w.Ragnar)})", Wood(w.Ragnar) == carried);
            checks.Add($"체력 그대로 ({hp0:0.0} → {w.Ragnar.GetHealth():0.0})", w.Ragnar.GetHealth() >= hp0 - 1f);

            GatherJob.TestIgnorePlayers = false;
            yield return TestKit.WaitFor(() => Wood(w.Ragnar) == 0, 60f);
            int after = Wood(w.Ragnar) + GatherTest.CountBase(w.Home, "Wood");
            checks.Add($"마저 걸어 상자에 넣음 (짐 {Wood(w.Ragnar)}, 합 {total} → {after})", TestKit.Ok && after == total);
        }
    }
}
