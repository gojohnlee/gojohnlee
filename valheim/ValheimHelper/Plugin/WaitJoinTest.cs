using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_waitjointest (기다림·합류, decisions 7절 "기다림·합류 시험 기본안"): 1호(Ragnar)가 나무 20개를 캐는데 2호(Floki)는 다른 일 중이다.
    /// A) 손 빈 동료가 없어 "나무 남은수" 지원 요청을 올림(1호 GatherJob.HelpTask).
    /// B) 2호가 쉬게 돼 JoinHelp → 같은 물건 일에 같이 낌(팀 2명), 요청은 채워져 빠짐.
    /// C) 기다림(시간 제한 240초) 뒤 팀 합계가 20개 이상(통나무는 여러 개씩 나와 넘침은 표시만).
    /// D) 목표가 찬 뒤 60초 안에 둘 다 일을 놓고(E088), 든 것은 상자에 넣고 집 곁으로 돌아온 뒤 60초 동안 다시 일하러 나가지 않음.
    /// </summary>
    static class WaitJoinTest
    {
        const int Amount = 20;
        const float LimitSeconds = 240f;
        const float DropLimit = 60f;
        const float WatchSeconds = 60f;
        const float HomeRadius = 30f;
        const float LateSeconds = 15f;

        public static void Register() => TestRunner.Register("vh_waitjointest", "기다림·합류 시험(20개)", () => TestKit.Flat(Run(false)));
        /// <summary>vh_waitlatetest: 같은 판이지만 2호는 1호가 캐기 시작한 뒤 15초에 멀리(60m)서 늦게 낌. 목표가 찬 뒤 둘 다 일을 놓는지 본다(E088 늦게 낀 판).</summary>
        public static void RegisterLate() => TestRunner.Register("vh_waitlatetest", "지원 늦게 낀 판 시험(20개)", () => TestKit.Flat(Run(true)));

        static IEnumerator Run(bool late)
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            bool ghost = Player.m_localPlayer.InGhostMode();
            Departures.TestIdleChores = true; // 쉬는 일꾼 판단(JoinHelp)은 시험 중엔 꺼져 있음
            try { yield return Body(w, checks, late); }
            finally
            {
                Departures.TestIdleChores = false;
                foreach (WorkerNpc n in new[] { w.Ragnar, w.Floki }) if (n != null && !n.IsDead()) n.Ai.Interrupt(); // 두 일꾼의 일(시험 일)을 접어 다음 시험이 환경 불량으로 안 걸리게
                for (HelpQueue.Ask a = Brain.Help.Next(); a != null; a = Brain.Help.Next()) Brain.Help.Remove(a.Task);
                Player.m_localPlayer.SetGhostMode(ghost);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, bool late)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 둘", w.Ragnar != null && w.Floki != null);
            if (w.Ragnar == null || w.Floki == null) yield break;
            Player p = w.Player;
            p.SetGhostMode(true); // 몬스터·플레이어 한 방은 안 셈
            WorkerNpc r = w.Ragnar, f = w.Floki;
            Quaternion rot = p.transform.rotation;
            const string item = "Wood";

            // 1호는 나무 20개를 캐는 중 / 2호는 다른 나무 일로 바빠 같이 못 감
            r.TeleportTo(GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 20f)) + Vector3.up * 0.3f, rot, false);
            f.TeleportTo(GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, -40f)) + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            var lead = new GatherJob(r, item, Amount, GatherJob.WaysFor(r, item));
            r.Ai.StartJob(lead);
            var busy = new GatherJob(f, item, 3, GatherJob.WaysFor(f, item));
            f.Ai.StartJob(busy);
            yield return new WaitForSeconds(1f);

            // A) 손 빈 동료가 없어 지원 요청을 올림
            Departures.ShareGather(new ChatCommand { Kind = CommandKind.Item, Item = item, Amount = Amount }, r);
            string task = lead.HelpTask;
            checks.Add($"A 손 빈 동료가 없어 캐기 요청을 올림 ({task})", task != null && Brain.Help.Count == 1 && Brain.Help.Next().Task == task && lead.Team == null);

            // B) 2호가 쉬게 돼 요청을 받아 같이 캠. 늦은 판은 1호가 캐기 시작한 뒤 15초에 2호가 60m 밖에서 달려옴
            if (late)
            {
                yield return new WaitForSeconds(LateSeconds);
                checks.Add($"B(늦은 판) {LateSeconds:0}초 뒤 1호가 아직 캐는 중 (1호 일 {(r.Ai.Job == lead ? "있음" : "없음")})", r.Ai.Job == lead);
                f.TeleportTo(GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, -60f)) + Vector3.up * 0.3f, rot, false);
            }
            f.Ai.Interrupt();
            yield return new WaitForSeconds(1f);
            bool joined = Departures.JoinHelp(f);
            checks.Add($"B 쉬는 일꾼이 요청을 받아 같은 물건 일에 낌 (팀 {lead.Team?.Count ?? 0})", joined && f.Ai.Job is GatherJob fj && fj.Item == item && lead.Team != null && lead.Team.Count == 2);
            checks.Add("B 채워져 요청이 빠지고 1호 요청 표시도 지움", Brain.Help.Count == 0 && lead.HelpTask == null);

            // C) 둘이 다 끝날 때까지 기다림(시간 제한) → 팀 합계가 정확히 20개
            float waited = 0f, filledAt = -1f;
            while (waited < LimitSeconds && (r.Ai.Job == lead || (f.Ai.Job is GatherJob fg && fg.Team == lead.Team)))
            {
                yield return new WaitForSeconds(2f);
                waited += 2f;
                if (filledAt < 0f && TeamTotal(lead) >= Amount) filledAt = waited;
            }
            int total = TeamTotal(lead);
            checks.Add($"C 팀 합계 {total}/{Amount}개(넘침 {total - Amount}), 기다림 {waited:0}초", total >= Amount);

            // D) 목표가 찬 뒤 60초 안에 둘 다 일을 놓고, 든 것은 상자에 넣고 집 곁으로 돌아온 뒤 60초 동안 다시 일하러 나가거나 먼 데 머물지 않음
            bool off = r.Ai.Job != lead && !(f.Ai.Job is GatherJob dj && dj.Team == lead.Team);
            float afterFill = filledAt < 0f ? -1f : waited - filledAt;
            checks.Add($"D 목표 찬 뒤 {afterFill:0}초 만에 둘 다 일을 놓음 (기준 {DropLimit:0}초)", filledAt >= 0f && off && afterFill <= DropLimit);
            // 든 것을 상자에 넣고 집 곁으로 돌아올 때까지 기다림(최대 DropLimit초)
            float back = 0f, far;
            int held;
            while (true)
            {
                // 먹을거리 일(다른 물건 GatherJob)로 먼 데 간 것은 거리에서 뺌(E088 결정: 먹을거리 일은 이 기준 밖, 확인 필요)
                far = Mathf.Max(WoodNear(r, item, w.Home), WoodNear(f, item, w.Home));
                held = r.CountItem(item) + f.CountItem(item);
                if (held == 0 && far <= HomeRadius || back >= DropLimit) break;
                yield return new WaitForSeconds(2f);
                back += 2f;
            }
            checks.Add($"D 든 것 {held}개(0이어야), {back:0}초 뒤 집에서 최대 {far:0}m (기준 {DropLimit:0}초·{HomeRadius:0}m 안)", held == 0 && far <= HomeRadius);
            float watched = 0f, maxFar = far;
            bool again = false, other = false;
            while (watched < WatchSeconds)
            {
                yield return new WaitForSeconds(2f);
                watched += 2f;
                // 같은 나무 일만 셈. 빈 음식 상자 때문에 나가는 먹을거리 일(IdleFood)은 따로 적기만 함(사용자 규칙 밖의 필요)
                if (r.Ai.Job is GatherJob rg && rg.Item == item || f.Ai.Job is GatherJob fw && fw.Item == item) again = true;
                if (r.Ai.Job is GatherJob || f.Ai.Job is GatherJob) other = true;
                maxFar = Mathf.Max(maxFar, Mathf.Max(Utils.DistanceXZ(r.transform.position, w.Home), Utils.DistanceXZ(f.transform.position, w.Home)));
            }
            checks.Add($"D 뒤 {WatchSeconds:0}초 동안 나무 일 다시 나감 {(again ? "있음" : "없음")} (다른 일 나감 {(other ? "있음" : "없음")}, 집에서 최대 {maxFar:0}m)", !again);
        }

        /// <summary>나무 일(또는 일 없음)일 때만 집에서 거리, 다른 물건 일이면 0.</summary>
        static float WoodNear(WorkerNpc n, string item, Vector3 home)
        {
            if (n.Ai.Job is GatherJob g && g.Item != item) return 0f;
            return Utils.DistanceXZ(n.transform.position, home);
        }

        static int TeamTotal(GatherJob job)
        {
            int total = 0;
            if (job.Team != null) foreach (var o in job.Team) total += o.Delivered;
            return total;
        }
    }
}
