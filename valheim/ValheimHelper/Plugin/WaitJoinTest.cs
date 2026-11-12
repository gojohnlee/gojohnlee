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
    /// </summary>
    static class WaitJoinTest
    {
        const int Amount = 20;
        const float LimitSeconds = 240f;

        public static void Register() => TestRunner.Register("vh_waitjointest", "기다림·합류 시험(20개)", () => TestKit.Flat(Run()));

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
            bool ghost = Player.m_localPlayer.InGhostMode();
            Departures.TestIdleChores = true; // 쉬는 일꾼 판단(JoinHelp)은 시험 중엔 꺼져 있음
            try { yield return Body(w, checks); }
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

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
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

            // B) 2호가 쉬게 돼 요청을 받아 같이 캠
            f.Ai.Interrupt();
            yield return new WaitForSeconds(1f);
            bool joined = Departures.JoinHelp(f);
            checks.Add($"B 쉬는 일꾼이 요청을 받아 같은 물건 일에 낌 (팀 {lead.Team?.Count ?? 0})", joined && f.Ai.Job is GatherJob fj && fj.Item == item && lead.Team != null && lead.Team.Count == 2);
            checks.Add("B 채워져 요청이 빠지고 1호 요청 표시도 지움", Brain.Help.Count == 0 && lead.HelpTask == null);

            // C) 둘이 다 끝날 때까지 기다림(시간 제한) → 팀 합계가 정확히 20개
            float waited = 0f;
            while (waited < LimitSeconds && (r.Ai.Job == lead || (f.Ai.Job is GatherJob fg && fg.Team == lead.Team)))
            {
                yield return new WaitForSeconds(2f);
                waited += 2f;
            }
            int total = 0;
            if (lead.Team != null) foreach (var o in lead.Team) total += o.Delivered;
            checks.Add($"C 팀 합계 {total}/{Amount}개(넘침 {total - Amount}), 기다림 {waited:0}초", total >= Amount);
        }
    }
}
