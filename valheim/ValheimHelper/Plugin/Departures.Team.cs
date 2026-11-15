using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>같이 캐기·늦게 끼기·지원 요청 대기열(쉬는 일꾼 없게, 사용자 11-09·11-13).</summary>
    public static partial class Departures
    {
        // ---- 같이 캐기 (사용자 11-09: "나무 캐 와" 하면 한 명만 가고 하나는 멍하니 → 둘이 출발하면 합쳐서 목표면 복귀) ----

        /// <summary>
        /// 이름 없이 시킨 채집: 그 일을 맡은 일꾼(lead)이 바로 떠났으면 그 순간 쉬던 다른 일꾼도 같은 물건을 캐러 간다(각자 따로 다님 — 일꾼끼리 같이 가지는 않음, 9절).
        /// 목표는 나누지 않고 같이 센다(GatherJob.Team): 둘이 넣은 것 + 들고 있는 것이 목표면 둘 다 복귀. 나중에 쉬게 된 일꾼은 JoinGather가 남은 몫이 클 때만 끼움.
        /// 동료는 묻지 않고 출발 판단이 Go일 때만 간다.
        /// </summary>
        internal static void ShareGather(ChatCommand cmd, WorkerNpc lead)
        {
            if (!ChoresOn || !(lead.Ai.Job is GatherJob g) || g.Item != cmd.Item || g.FromRefill || g.Delivered > 0 || g.Target < 2) return;
            foreach (var n in new List<WorkerNpc>(WorkerNpc.All))
            {
                if (n == lead || !(FreeForChores(n) || n.Ai.Current == WorkerAI.Mode.Work && n.Ai.Yields && n.Ai.Queue.Count == 0 && !s_pending.ContainsKey(n))) continue; // 스스로 하던 일은 접고 같이 감
                HashSet<ToolKind> ways = GatherJob.WaysFor(n, cmd.Item);
                if (ways.Count == 0) continue;
                var c = new ChatCommand { Kind = CommandKind.Item, Item = cmd.Item, Amount = g.Target };
                DepartureInput input = Input(n, c, ways, true);
                input.Stock = 0;
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{n.NpcName} 같이 캐기 {cmd.Item} {g.Target} ({lead.NpcName}와 합쳐서): {r.Action} {r.Question} ({r.Reason})");
                if (r.Action != DepartAction.Go) continue;
                var job = new GatherJob(n, cmd.Item, g.Target, ways);
                foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
                if (g.Team == null) g.Team = new List<GatherJob> { g };
                g.Team.Add(job);
                job.Team = g.Team;
                n.Ai.StartJob(job);
                n.Say("gatherHelp", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(cmd.Item), ["n"] = g.Target.ToString(), ["partner"] = lead.NpcName });
            }
            if (g.Team == null && g.Remaining() > 0) // 손 빈 동료가 없으면 한 명 더 청함(decisions 7절 "지원 요청 캐기 쪽")
            {
                g.HelpTask = $"{GatherJob.ItemLabel(cmd.Item)} {g.Remaining()}";
                Brain.Help.Add(g.HelpTask, 1);
            }
        }


        /// <summary>
        /// 쉬는 일꾼 없게 ①(사용자 11-13): 나중에 쉬게 된 일꾼도 동료가 명령받아 캐는 물건의 남은 몫이 크면(Core TeamJoin.Worth) 같은 팀으로 낀다.
        /// 자동 보충·갚기·쉴 때 일은 안 낌(그건 각자 맡음). 출발 판단이 Go일 때만.
        /// </summary>
        internal static bool JoinGather(WorkerNpc npc)
        {
            if (!ChoresOn) return false;
            foreach (var lead in WorkerNpc.All)
            {
                if (lead == npc || lead == null || lead.IsDead() || !(lead.Ai.Job is GatherJob g) || g.FromRefill || g.Repay || g.Target < 2) continue;
                int team = g.Team?.Count ?? 1;
                if (g.Team != null && g.Team.Exists(o => o.Npc == npc)) continue;
                if (!TeamJoin.Worth(g.Remaining() - g.Carried(), g.Target, team)) continue;
                HashSet<ToolKind> ways = GatherJob.WaysFor(npc, g.Item);
                if (ways.Count == 0) continue;
                var c = new ChatCommand { Kind = CommandKind.Item, Item = g.Item, Amount = g.Target };
                DepartureInput input = Input(npc, c, ways, true);
                input.Stock = 0;
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{npc.NpcName} 늦게 같이 캐기 {g.Item} 남은 {g.Remaining() - g.Carried()}/{g.Target} ({lead.NpcName} 팀 {team}): {r.Action} ({r.Reason})");
                if (r.Action != DepartAction.Go) continue;
                var job = new GatherJob(npc, g.Item, g.Target, ways);
                foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
                if (g.Team == null) g.Team = new List<GatherJob> { g };
                g.Team.Add(job);
                job.Team = g.Team;
                npc.Ai.StartJob(job);
                npc.Say("gatherHelp", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(g.Item), ["n"] = g.Target.ToString(), ["partner"] = lead.NpcName });
                return true;
            }
            return false;
        }
        /// <summary>
        /// 쉬는 일꾼 없게 ②(사용자 11-13, decisions 7절 "Next 소비처 기본안"): 지원 대기열 맨 앞 요청을 올린 기다리는 호위 곁으로 손 빈 일꾼의 호위가 달려간다(Summon).
        /// 그 호위를 못 찾거나 바쁘면 미룸(Postpone). 도착 확인은 Guard.CheckHelpArrived.
        /// </summary>
        internal static bool JoinHelp(WorkerNpc npc)
        {
            if (!ChoresOn || npc.Ai.Guard == null) return false;
            HelpQueue.Ask ask = Brain.Help.Next();
            if (ask == null) return false;
            WorkerNpc waiting = WorkerNpc.All.Find(w => w != null && w != npc && !w.IsDead() && w.Ai.Guard != null && w.Ai.Guard.HelpTask == ask.Task);
            if (waiting == null && !ask.Task.StartsWith("호위:")) return JoinHelpGather(npc, ask.Task);
            if (waiting == null || !npc.Ai.Guard.Summon(waiting))
            {
                HelpPostpone(ask.Task, waiting);
                return false;
            }
            Plugin.Log.LogInfo($"{npc.NpcName} 지원 요청 '{ask.Task}' → {waiting.NpcName} 쪽으로 달려감(대기열 맨 앞)");
            return true;
        }
        /// <summary>지원 요청을 미루고, 진척 없이 StaleAfter번 밀렸으면 한 번 로그 경고 + 요청한 일꾼 상태 말(사용자 11-13 "오래 끌면 말·diag").</summary>
        static void HelpPostpone(string task, WorkerNpc asker)
        {
            if (!Brain.Help.Postpone(task)) return;
            Plugin.Log.LogWarning($"지원 요청 '{task}' {HelpQueue.StaleAfter}번 미뤄짐 — 진척 없이 오래 끎");
            asker?.Say("helpStale", new Dictionary<string, string> { ["task"] = task });
        }
        /// <summary>
        /// 쉬는 일꾼 없게 ⓐ-2(decisions 7절 "지원 요청 캐기 쪽"): 캐기 요청("호위:"로 안 시작하는 일)을 받으면 그 요청을 올린 일꾼의 같은 물건 일에 ShareGather 규칙대로 낀다.
        /// 그 일이 이미 없으면 요청을 지우고, 판단이 안 가면 미룸(Postpone). 낀 것은 Served로 채움.
        /// </summary>
        static bool JoinHelpGather(WorkerNpc npc, string task)
        {
            WorkerNpc lead = WorkerNpc.All.Find(w => w != null && w != npc && !w.IsDead() && w.Ai.Job is GatherJob lg && lg.HelpTask == task);
            if (lead == null) { Brain.Help.Remove(task); return false; }
            var g = (GatherJob)lead.Ai.Job;
            HashSet<ToolKind> ways = GatherJob.WaysFor(npc, g.Item);
            if (ways.Count == 0) { HelpPostpone(task, lead); return false; }
            var c = new ChatCommand { Kind = CommandKind.Item, Item = g.Item, Amount = g.Target };
            DepartureInput input = Input(npc, c, ways, true);
            input.Stock = 0;
            DepartureResult r = Departure.Judge(input);
            Plugin.Log.LogInfo($"{npc.NpcName} 지원 요청 '{task}' 같이 캐기 ({lead.NpcName}): {r.Action} ({r.Reason})");
            if (r.Action != DepartAction.Go) { HelpPostpone(task, lead); return false; }
            var job = new GatherJob(npc, g.Item, g.Target, ways);
            foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
            if (g.Team == null) g.Team = new List<GatherJob> { g };
            g.Team.Add(job);
            job.Team = g.Team;
            npc.Ai.StartJob(job);
            npc.Say("gatherHelp", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(g.Item), ["n"] = g.Target.ToString(), ["partner"] = lead.NpcName });
            if (Brain.Help.Served(task, 1)) g.HelpTask = null;
            return true;
        }
    }
}
