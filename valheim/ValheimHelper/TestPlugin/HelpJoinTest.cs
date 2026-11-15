using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_helpjointest (지원 요청 Next 소비처, decisions 7절 "Next 소비처 기본안"): 1호(Ragnar)는 지원 요청 "호위:테스트"를 올려 기다리는 중,
    /// 2호(Floki)는 손이 빈 채 40m 밖에 있다. A) 2호가 JoinHelp → 1호 쪽으로 달려감(Come), 곁(AllyRange)에 오면 요청이 채워져 대기열에서 빠짐, 1호 HelpTask 지움.
    /// B) 기다리는 호위가 없는 요청은 미룸(Postpone) — 대기열에 남고 미룬 횟수 1.
    /// </summary>
    static class HelpJoinTest
    {
        public static void Register() => TestRunner.Register("vh_helpjointest", "지원 요청 Next 소비처 시험", () => TestKit.Flat(Run()));

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
            const string task = "호위:테스트";
            const string none = "호위:없음";
            Departures.TestIdleChores = true; // 쉬는 일꾼 판단(JoinHelp)은 시험 중엔 꺼져 있음
            try { yield return Body(w, checks, task, none); }
            finally
            {
                Departures.TestIdleChores = false;
                Brain.Help.Remove(task);
                Brain.Help.Remove(none);
                Player.m_localPlayer.SetGhostMode(ghost);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, string task, string none)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 둘", w.Ragnar != null && w.Floki != null);
            if (w.Ragnar == null || w.Floki == null) yield break;
            Player p = w.Player;
            p.SetGhostMode(true); // 몬스터·플레이어 한 방은 안 셈
            WorkerNpc r = w.Ragnar, f = w.Floki;
            Quaternion rot = p.transform.rotation;

            // 1호: 거점 앞 20m에서 지원 요청을 올리고 기다림 / 2호: 40m 밖에서 쉬는 중
            r.TeleportTo(GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 20f)) + Vector3.up * 0.3f, rot, false);
            f.TeleportTo(GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, -40f)) + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            Brain.Help.Add(task, 1);
            r.Ai.Guard?.TestWaitForHelp(task);
            checks.Add("1호가 지원 요청을 올려 기다림", r.Ai.Guard != null && r.Ai.Guard.HelpTask == task && Brain.Help.Count == 1);

            // A) 2호가 Next를 받아 달려감 → 곁에 오면 채움
            bool joined = Departures.JoinHelp(f);
            checks.Add($"A 2호가 지원 요청을 받아 달려감 (Come, 1호 {r.Ai.Guard?.HelpTask})", joined && f.Ai.Guard?.StateName == "Come");
            float near = 999f;
            float t = 0f;
            while (t < 40f && Brain.Help.Count > 0 && !f.IsDead() && !r.IsDead())
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                near = Mathf.Min(near, Vector3.Distance(f.transform.position, r.transform.position));
            }
            TestRunner.Log($"[A] {t:0.0}s, 가장 가까이 {near:0}m, 2호 {f.Ai.Guard?.StateName}, 요청 {Brain.Help.Count}");
            checks.Add($"A 2호가 곁(AllyRange)에 와서 요청이 채워짐 (가까이 {near:0}m, {t:0}초)", Brain.Help.Count == 0 && r.Ai.Guard?.HelpTask == null);

            // B) 기다리는 호위가 없는 요청은 미룸
            Brain.Help.Add(none, 1);
            bool gone = Departures.JoinHelp(f);
            HelpQueue.Ask left = Brain.Help.Next();
            checks.Add($"B 기다리는 호위가 없으면 안 감, 미룸 1 ({left?.Task} {left?.Postponed})", !gone && left != null && left.Task == none && left.Postponed == 1);
        }
    }
}
