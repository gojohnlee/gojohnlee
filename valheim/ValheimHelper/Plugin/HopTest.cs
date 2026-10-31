using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_hoptest (막힘 복구 2) 목표 쪽 순간이동, 사용자 결정 2026-10-23): 채집 일로 나무에 가다 막히면 첫 실패에 목표 둘레 단단한 땅으로 옮기고
    /// (짐·체력 그대로), 옮긴 뒤에도 또 막히면 그 목적지를 빼는지(같은 목적지로 순간이동을 되풀이하지 않음). 먼 곳(플레이어 64m 밖)만 하는 일이라
    /// 시험 동안 플레이어 판정을 끈다(GatherJob.TestIgnorePlayers). 끼임은 vh_stucktest처럼 걷기·점프 힘을 0으로 묶어 흉내 낸다.
    /// </summary>
    static class HopTest
    {
        public static void Register() => TestRunner.Register("vh_hoptest", "막힘 복구: 목표 쪽 순간이동 시험", () => TestKit.Flat(Run()));

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
            var saved = new float[6];
            Character held = null;
            bool running = true;
            Plugin.Instance.StartCoroutine(TestKit.ClearWhileHeld(w, () => held, () => running)); // 묶인 일꾼은 도망 못 함 → 둘레 생물 치움
            try { yield return Body(w, checks, saved, c => held = c); }
            finally
            {
                running = false;
                GatherJob.TestIgnorePlayers = GatherJob.TestNoFarStuck = false;
                if (held != null) Speeds(held, saved, false);
                TestKit.Restore(w);
            }
            if (TestKit.KeptClear > 0) TestRunner.Log($"발 묶인 동안 둘레 생물 {TestKit.KeptClear}마리 치움");
            TestKit.End(w, checks);
        }

        internal static void Speeds(Character c, float[] saved, bool freeze)
        {
            if (freeze) { saved[0] = c.m_speed; saved[1] = c.m_walkSpeed; saved[2] = c.m_runSpeed; saved[3] = c.m_swimSpeed; saved[4] = c.m_jumpForce; saved[5] = c.m_jumpForceForward; }
            c.m_speed = freeze ? 0f : saved[0];
            c.m_walkSpeed = freeze ? 0f : saved[1];
            c.m_runSpeed = freeze ? 0f : saved[2];
            c.m_swimSpeed = freeze ? 0f : saved[3];
            c.m_jumpForce = freeze ? 0f : saved[4];
            c.m_jumpForceForward = freeze ? 0f : saved[5]; // 점프 힘 0이어도 앞으로 튐(Character.Jump: forward × m_jumpForceForward) → 묶인 채 조금씩 나아가 귀환 막힘을 못 셈(E026)
        }

        static int Carried(Humanoid c)
        {
            int n = 0;
            foreach (var it in c.GetInventory().GetAllItems()) n += it.m_stack;
            return n;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, float[] saved, System.Action<Character> hold)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            var ai = w.Ragnar.Ai;

            TestKit.ClearBaseStock(w, "Wood");
            GatherTest.Say($"!{Workers.Name1} 나무 30", null);
            yield return TestKit.WaitFor(() => ai.Job is GatherJob g && (g.StepName == "Search" || g.StepName == "Approach" || g.StepName == "GoZone"), 60f);
            checks.Add("일 시작함", TestKit.Ok);
            if (!TestKit.Ok) yield break;

            // 거점에서 40m 떨어진 곳으로 옮기고 발을 묶는다. 근처 판정은 끈다(먼 곳 흉내).
            GatherJob.TestIgnorePlayers = true;
            GatherJob.TestNoFarStuck = true; // 이 칸은 순간이동을 본다(목표 몇 m 앞 막힘 → 그 자리 치기는 아래 묶인 채 칸에서)
            Quaternion rot = w.Player.transform.rotation;
            Vector3 spot = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 40f));
            // 목표가 3.9m 앞이면 순간이동이 0.8m만 옮겨 "자리를 옮김" FAIL(회차 183, 막힘 시간 절반으로 11초에 막힘) → 둘레 나무를 치워 목표를 멀리
            int cut0 = GatherTest.ClearWood(spot, 12f);
            if (cut0 > 0) TestRunner.Log($"자리 12m 안 나무·통나무·그루터기 {cut0}개 치움");
            w.Ragnar.TeleportTo(spot + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(1f);
            Speeds(w.Ragnar, saved, true);
            hold(w.Ragnar);
            float hp0 = w.Ragnar.GetHealth();
            int carried0 = Carried(w.Ragnar);
            int hops0 = GatherJob.Hops, rescues0 = GatherJob.Rescues;
            Vector3 before = w.Ragnar.transform.position;

            // 묶인 채로도 닿는 나무는 베어 줍는다 → 짐·체력은 순간이동 직전 프레임 값과 바로 뒤 값을 비교한다(회차 92 첫 시험: 묶은 뒤 4 → 9는 줍기 몫)
            int carriedBefore = carried0;
            float hpBefore = hp0;
            yield return TestKit.WaitFor(() =>
            {
                if (GatherJob.Hops > hops0) return true;
                carriedBefore = Carried(w.Ragnar);
                hpBefore = w.Ragnar.GetHealth();
                return false;
            }, 150f);
            bool hopped = GatherJob.Hops > hops0;
            int carriedAfter = Carried(w.Ragnar);
            float hpAfter = w.Ragnar.GetHealth();
            checks.Add($"막히자 목표 쪽으로 순간이동 ({GatherJob.Hops - hops0}번)", hopped);
            if (!hopped) yield break;
            checks.Add($"짐 그대로 (순간이동 직전 {carriedBefore} → 직후 {carriedAfter})", carriedAfter == carriedBefore);
            checks.Add($"체력 그대로 (직전 {hpBefore:0.0} → 직후 {hpAfter:0.0})", Mathf.Abs(hpAfter - hpBefore) < 1f);
            yield return new WaitForSeconds(1f);
            Vector3 p = w.Ragnar.transform.position;
            GatherJob.TestNoFarStuck = false;
            // 12m를 치워도 묶인 채 3.4m 앞 목표에 막히면 0.4m만 옮겨 목표 3m 자리에 선다(회차 297) → 그것도 옮긴 것
            float toDest = Utils.DistanceXZ(p, GatherJob.LastHopDest);
            checks.Add($"자리를 옮김 ({Utils.DistanceXZ(before, p):0.0}m, 목표까지 {toDest:0.0}m)", Utils.DistanceXZ(before, p) > 1f || toDest < 3.6f);
            bool ground = ZoneSystem.instance.GetSolidHeight(p, out float h);
            checks.Add($"단단한 땅 위 (높이 {p.y:0.0}/땅 {h:0.0}, 물 {Floating.GetLiquidLevel(p):0.0})", ground && Mathf.Abs(p.y - h) < 1.5f && Floating.GetLiquidLevel(p) < h + 0.1f);
            checks.Add("깃발 강제 복귀 아님", GatherJob.Rescues == rescues0);

            // 발은 계속 묶여 있다(옮긴 자리에서 닿으면 그냥 벤다): 90초 동안 같은 목적지가 2번 넘게 막히지 않음 = 같은 곳으로 걷기·순간이동 되풀이 없음.
            // 2번째 막히면 뺀다는 순서는 dotnet StuckBookTests.HopFirstThenExclude.
            int hops1 = GatherJob.Hops;
            string book = "";
            int worst = 0;
            for (float t = 0f; t < 90f; t += 1f)
            {
                yield return new WaitForSeconds(1f);
                if (!(ai.Job is GatherJob g)) break;
                book = g.StuckSave;
                foreach (var part in book.Split(';'))
                {
                    var f = part.Split('|');
                    if (f.Length == 3 && int.TryParse(f[2], out int n)) worst = Mathf.Max(worst, n);
                }
            }
            TestRunner.Log($"  90초 묶인 채: 순간이동 {GatherJob.Hops - hops1}번 더, 뺀 곳 {(ai.Job is GatherJob gj ? gj.StuckExcluded : -1)}, 기록 {book}");
            checks.Add($"같은 목적지 되풀이 없음 (한 곳 최대 {worst}번)", worst <= 2);
            // E012: 멀리(플레이어 판정 끔) 서 있는 나무 5m 앞에서 묶여 막히면 그 자리에서 직접 피해로 벤다 → 나무가 쓰러진다.
            // 들고 있는 도끼로 벨 수 있는 나무만(TreeBase.RPC_Damage: CheckToolTier(m_minToolTier) 못 넘으면 피해 없음 — 회차 141 기본 자리에서
            // 가장 가까운 게 자작나무라 직접 치기 60번에도 안 쓰러짐. 일의 Search는 이미 도구 단계로 거름, TestApproach는 안 거름)
            ItemDrop.ItemData axe = WorkerNpc.BestTool(w.Ragnar.GetInventory(), ToolKind.Axe);
            int axeTier = axe != null ? axe.m_shared.m_toolTier : 0;
            TreeBase tree = null;
            float bestD = float.MaxValue;
            string hard = "";
            float hardD = float.MaxValue;
            Vector3 me = w.Ragnar.transform.position;
            foreach (var t in Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None))
            {
                float d = Utils.DistanceXZ(t.transform.position, me);
                if (d <= 10f || d >= 40f || d >= bestD) continue;
                if (t.m_minToolTier > axeTier) { if (d < hardD) { hardD = d; hard = $"{Utils.GetPrefabName(t.gameObject)} {d:0}m(단계 {t.m_minToolTier})"; } continue; }
                bestD = d;
                tree = t;
            }
            if (tree != null) TestRunner.Log($"  묶인 채 나무 칸: {Utils.GetPrefabName(tree.gameObject)} {bestD:0}m (단계 {tree.m_minToolTier}, 도끼 {axeTier}){(hardD < bestD ? ", 더 가깝지만 못 벰 " + hard : "")}");
            if (tree == null || !(ai.Job is GatherJob gt)) checks.Add("가) 멀리 목표 앞 막힘 칸: 10~40m 안 나무가 있음", false);
            else
            {
                ZDO treeZdo = tree.GetComponent<ZNetView>().GetZDO();
                ZDOID treeId = treeZdo.m_uid;
                Vector3 off = me - tree.transform.position;
                off.y = 0f;
                Vector3 stand = GatherTest.Ground(tree.transform.position + off.normalized * 5f);
                // 옮긴 그 프레임에 목표를 바꾼다: 1초 기다리면 묶인 채 쌓인 막힘 시간으로 옛 목표 쪽 순간이동이 끼어들어 나무에서 멀어짐(회차 141 기본 자리: 15.8m에서 시작해 걸어가 보통으로 벰)
                w.Ragnar.TeleportTo(stand + Vector3.up * 0.3f, Quaternion.LookRotation(-off), false);
                int far1 = GatherJob.FarStuckHits;
                gt.TestApproach(treeZdo);
                yield return TestKit.WaitFor(() => GatherJob.FarStuckHits > far1 && ZDOMan.instance.GetZDO(treeId) == null, 90f);
                checks.Add($"묶인 채 나무 5m 앞 막힘 → 그 자리에서 벰 (직접 치기 {GatherJob.FarStuckHits - far1}번, 나무 쓰러짐 {ZDOMan.instance.GetZDO(treeId) == null})", TestKit.Ok);
            }

            Speeds(w.Ragnar, saved, false);
            hold(null);
            GatherJob.TestIgnorePlayers = false;
            yield return new WaitForSeconds(3f);
            checks.Add($"일을 이어 감 ({ai.Current}/{ai.Job?.StepName})", ai.Job is GatherJob);
            if (ai.Job is GatherJob) yield return CantReach(w, checks, saved, hold);
        }

        static int Wood(Humanoid c)
        {
            int n = 0;
            foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "Wood") n += it.m_stack;
            return n;
        }

        /// <summary>
        /// 막힘 복구 4)·귀환 상한(조각 다): 이미 세 곳을 뺀 일(작업 저장 꼴로 넣음)이 근처(순간이동 안 함)에서 또 막히면 네 곳째를 빼고
        /// "갈 수가 없어요"로 그만두고, 든 나무를 넣으러 가다 귀환 걷기가 StuckBook.HomeWalkTries번 막히면 깃발로 순간이동해 넣는지. 나무 개수 보존.
        /// </summary>
        static IEnumerator CantReach(TestKit.World w, GatherTest.Checks checks, float[] saved, System.Action<Character> hold)
        {
            var ai = w.Ragnar.Ai;
            Quaternion rot = w.Player.transform.rotation;
            Vector3 spot = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 40f));
            // 묶인 채 팔 닿는 나무·통나무가 있으면 150초 내내 베고 기다려 막힘이 안 생김(회차 182, 짐 14→17) → 그 둘레만 치움
            int cut = GatherTest.ClearWood(spot, 12f);
            if (cut > 0) TestRunner.Log($"가) 자리 12m 안 나무·통나무·그루터기 {cut}개 치움");
            w.Ragnar.TeleportTo(spot + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(1f);
            if (!(ai.Job is GatherJob g)) { checks.Add("가) 일이 남아 있음", false); yield break; }
            if (Wood(w.Ragnar) == 0) w.Ragnar.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 3);
            g.LoadStuck("1000|1000|2;1001|1000|2;1002|1000|2");
            Speeds(w.Ragnar, saved, true);
            hold(w.Ragnar);
            int unreach0 = GatherJob.Unreachable, rescues0 = GatherJob.Rescues, hops0 = GatherJob.Hops;
            int heard0 = w.Heard.Count;
            int total = Wood(w.Ragnar) + GatherTest.CountBase(w.Home, "Wood");
            yield return TestKit.WaitFor(() =>
            {
                if (GatherJob.Unreachable > unreach0) return true;
                total = Wood(w.Ragnar) + GatherTest.CountBase(w.Home, "Wood"); // 묶인 채로 닿는 나무는 벨 수 있다 → 그만두기 직전 값
                return false;
            }, 150f);
            checks.Add($"뺀 곳 4곳째에 그만둠 (대안 없음 {GatherJob.Unreachable - unreach0}번)", GatherJob.Unreachable > unreach0);
            checks.Add($"근처라 목표 쪽 순간이동 안 함 ({GatherJob.Hops - hops0}번)", GatherJob.Hops == hops0);
            if (GatherJob.Unreachable == unreach0) yield break;
            checks.Add($"든 나무를 넣으러 감 ({ai.Job?.StepName})", ai.Job is GatherJob gr && gr.StepName == "Return");

            // 발은 계속 묶임: 귀환 걷기가 상한만큼 막히면 깃발로 순간이동
            int jumps0 = ai.Jumps;
            yield return TestKit.WaitFor(() => Utils.DistanceXZ(w.Ragnar.transform.position, w.Home) < 5f, 120f);
            checks.Add($"귀환 막힘 상한 → 깃발로 순간이동 ({Utils.DistanceXZ(w.Ragnar.transform.position, w.Home):0.0}m, 뛴 횟수 {ai.Jumps - jumps0})", TestKit.Ok);
            checks.Add("깃발 강제 복귀(다시 막힘)와 따로", GatherJob.Rescues == rescues0);
            Speeds(w.Ragnar, saved, false);
            hold(null);
            yield return TestKit.WaitFor(() => !(ai.Job is GatherJob), 60f);
            checks.Add($"넣고 일을 끝냄 ({ai.Current}/{ai.Job?.StepName})", TestKit.Ok);
            bool said = false;
            for (int i = heard0; i < w.Heard.Count; i++) said |= TestKit.SaidKey(w.Heard[i], w.Ragnar.Personality, "cantReach");
            checks.Add("\"갈 수가 없어요\" 말함", said);
            int after = Wood(w.Ragnar) + GatherTest.CountBase(w.Home, "Wood");
            checks.Add($"나무 개수 그대로 (짐 {Wood(w.Ragnar)}, 그만둘 때 합 {total} → 지금 합 {after})", after == total && Wood(w.Ragnar) == 0);
        }
    }
}
