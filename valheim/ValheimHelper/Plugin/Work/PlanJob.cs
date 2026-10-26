using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 제작 계획을 실제로 돌리는 일(⑥ 실행기 셋째 조각): PlanRunner가 고른 단계를 하나씩 한다.
    /// Gather → GatherJob(목표만큼 거점 상자에 넣으면 끝), Craft → CraftStep. Load·Unload → MachineStep(Smelter·Fermenter), Hunt는 GatherJob 사냥 모드(⑧), Farm은 아직 없어서 Failed(그에 기댄 단계는 Blocked).
    /// Wait만 남으면 가만히 시계를 기다린다(다른 일 하기는 다음 조각). 실패가 있으면 끝에 한 번 알린다.
    /// </summary>
    public sealed class PlanJob : IWorkerJob
    {
        readonly WorkerNpc m_npc;
        readonly PlanRunner m_runner;
        readonly string m_goal;
        float m_now;
        PlanStep m_step;
        GatherJob m_gather;
        CraftStep m_craft;
        MachineStep m_machine;
        /// <summary>Wait만 남았을 때 끼워 넣는 짧은 자동 보충(저장하지 않음 — 보충은 기준치에서 다시 정해진다).</summary>
        GatherJob m_filler;
        float m_nextFillAt, m_nextReadyCheck;
        /// <summary>끼워 넣기는 Wait가 이만큼(초) 남았을 때만, 한 번에 이만큼(개)만 — 끝나면 곧바로 계획으로 돌아온다.</summary>
        const float FillMinWait = 90f, FillRetry = 60f;
        const int FillAmount = 20;
        /// <summary>
        /// 재료 채집 단계도 직접 명령과 같은 출발 판단(외부 검토 10-26): 위험한 곳은 피하고, 다 위험하면 호위병과 — 그 단계가 끝나면 호위병을 돌려보냄.
        /// 호위병이 바쁘면 그 단계는 거점에서 기다렸다가(m_waitGuard, 3초마다 다시 판단) 같이 출발(사용자 10-26).
        /// </summary>
        bool m_withGuard, m_waitGuard;
        float m_guardCheckAt;

        public PlanJob(WorkerNpc npc, Plan plan, string goalLabel) : this(npc, new PlanRunner(plan), goalLabel) { }

        /// <summary>저장에서 되살린 계획으로(재시작 — PlanSave.Load, Departures.ResumeMake). 돌던 기다림은 지금부터 다시 잰다(m_now 0).</summary>
        public PlanJob(WorkerNpc npc, PlanRunner runner, string goalLabel)
        {
            m_npc = npc;
            m_runner = runner;
            m_goal = goalLabel;
        }

        /// <summary>
        /// 작업 상태 저장용: 이 계획을 세운 목표(제작 물건 또는 세트). 저장 칸(ToWork)에는 계획 단계·상태·진행도 같이 넣어
        /// 재시작 뒤 다시 세우지 않고 이어 간다(외부 검토 + 사용자 승인 10-26). 계획이 깨져 못 읽으면 이 목표로 다시 세운다.
        /// </summary>
        public WorkItem Goal;

        public WorkItem ToWork() => new WorkItem
        {
            Kind = Goal.Kind, Resume = Goal.Resume, Label = Goal.Label, Item = Goal.Item, Amount = Goal.Amount, SetNo = Goal.SetNo,
            Plan = PlanSave.Save(m_runner, Progress),
        };

        /// <summary>모으던 단계의 진행: 그 단계 수에서 남은 수를 뺀 것(죽은 뒤 남은 수로 새로 만든 채집도 맞음). 그 밖엔 0.</summary>
        int Progress(int id) =>
            m_step != null && m_step.Id == id && m_gather != null ? m_step.Amount - Mathf.Max(0, m_gather.Target - m_gather.Delivered) : 0;
        public PlanRunner Runner => m_runner;
        public string StepName => m_step != null ? m_step.Kind.ToString() : "Wait";
        public string Label => m_goal;
        public string StateLine() => m_goal + " 만드는 중(" + m_runner.Count(StepState.Done) + "/" + m_runner.Plan.Steps.Count + ")";

        public void AvoidHere(Vector3 danger) { m_gather?.AvoidHere(danger); m_filler?.AvoidHere(danger); }

        /// <summary>
        /// 일꾼이 죽었다(짐은 묘비로, 몸은 깃발로) — 이 계획은 묘비 회수 뒤 객체째 이어 한다(WorkerAI.OnDied). 다시 세우면 설비에 이미 올린 것
        /// (조리대 위 고기 등)을 몰라 재료를 또 모으러 간다(회차 128 cooktest). 모으던 단계는 남은 수만큼 새로(채집 이어 하기와 같게 — 들고 있던 것은
        /// 묘비 회수가 상자에 넣음), 끼워 넣은 보충은 버리고, 설비 단계는 그 설비로 다시 걸어가 이어 하되 소실 보충은 끈다(짐이 묘비로 감).
        /// </summary>
        public void OnDied()
        {
            m_filler = null;
            if (m_gather != null)
            {
                int left = Mathf.Max(1, m_gather.Target - m_gather.Delivered);
                m_gather = new GatherJob(m_npc, m_gather.Item, left, ResourceCatalog.WaysFor(m_gather.Item)) { InPlan = true };
            }
            m_machine?.OnDied();
        }

        public bool Update(float dt)
        {
            m_now += dt;
            if (m_now >= m_nextReadyCheck) { m_nextReadyCheck = m_now + 5f; FinishReadyWaits(); }
            if (m_filler != null)
            {
                bool more = m_filler.Update(dt) && m_filler.Delivered < m_filler.Target;
                if (more) return true;
                Plugin.Log.LogInfo($"{m_npc.NpcName} Wait 중 보충 끝: {m_filler.Item} {m_filler.Delivered}/{m_filler.Target}");
                m_filler = null;
                m_nextFillAt = m_now + FillRetry;
                return true;
            }
            if (m_step == null && !Pick())
            {
                if (m_waitGuard && Utils.DistanceXZ(m_npc.Home, m_npc.transform.position) > 4f) { m_npc.Ai.MoveToPoint(dt, m_npc.Home, 2f, true); return true; }
                m_npc.Ai.Hold(m_waitGuard ? "호위병 기다림" : "만들기 차례 기다림");
                m_npc.Ai.StopMoving(); // 가만히 기다림: 앞 단계의 걷기 방향이 남아 계속 걷지 않게(회차 128)
                return !m_runner.Over ? TryFill() : Finish();
            }
            if (m_step == null) return true;

            bool running = true;
            bool ok = false;
            switch (m_step.Kind)
            {
                case StepKind.Gather:
                case StepKind.Hunt:
                    running = m_gather.Update(dt);
                    ok = m_gather.Delivered >= m_gather.Target;
                    if (ok) running = false;
                    break;
                case StepKind.Craft:
                    CraftResult r = m_craft.Update(dt);
                    running = r == CraftResult.Working;
                    ok = r == CraftResult.Done;
                    break;
                case StepKind.Load:
                case StepKind.Unload:
                    CraftResult mr = m_machine.Update(dt);
                    running = mr == CraftResult.Working;
                    ok = mr == CraftResult.Done;
                    break;
            }
            if (running) return true;
            if (m_withGuard) { m_npc.Ai.EndEscort(); m_withGuard = false; } // 재료 채집 단계 끝: 호위병은 거점으로(제작 내내 붙잡지 않음)
            if (ok) m_runner.Complete(m_step.Id);
            else
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 계획 단계 {m_step.Id} {m_step.Kind} {m_step.Item} 실패");
                m_runner.Fail(m_step.Id);
            }
            m_step = null; m_gather = null; m_craft = null; m_machine = null;
            WorkSaves.Flush(m_npc); // 끝난 단계를 바로 저장: 1초 사이에 꺼지면 넣기·제작을 한 번 더 해 재료가 두 번 들어감
            return true;
        }

        /// <summary>다음 일 단계를 시작한다. 없으면 false.</summary>
        bool Pick()
        {
            while (true)
            {
                PlanStep s = m_runner.Next(m_now);
                if (s == null) return false;
                switch (s.Kind)
                {
                    case StepKind.Gather:
                    case StepKind.Hunt:
                        HashSet<ToolKind> ways = ResourceCatalog.WaysFor(s.Item);
                        if (ways.Count == 0 || s.Amount <= 0) { m_runner.Fail(s.Id); continue; }
                        if (m_waitGuard && m_now < m_guardCheckAt) return false;
                        DepartureResult j = Departures.JudgeStep(m_npc, s.Item, s.Amount);
                        if (j != null && j.Action == DepartAction.WaitGuard)
                        {
                            if (!m_waitGuard) { Plugin.Log.LogInfo($"{m_npc.NpcName} 계획 {m_goal}: {s.Item} 채집은 위험한데 호위병이 바쁨 → 거점에서 기다림"); m_npc.Say("depart.waitGuard", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(s.Item), ["n"] = s.Amount.ToString(), ["partner"] = WorkerNpc.TheGuard?.NpcName ?? "" }); }
                            m_waitGuard = true;
                            m_guardCheckAt = m_now + 3f;
                            return false;
                        }
                        m_waitGuard = false;
                        m_gather = new GatherJob(m_npc, s.Item, s.Amount, ways) { InPlan = true };
                        if (j != null) foreach (var o in j.Avoid) m_gather.AvoidZone(o.Zone);
                        m_withGuard = j != null && j.Action == DepartAction.GoTogether;
                        break;
                    case StepKind.Craft:
                        m_craft = new CraftStep(m_npc, s);
                        break;
                    case StepKind.Load:
                    case StepKind.Unload:
                        if (MachineStep.Supported(s.Station)) { m_machine = new MachineStep(m_npc, s); break; }
                        m_runner.Fail(s.Id);
                        continue;
                    default: // Farm: 아직 없음
                        m_runner.Fail(s.Id);
                        continue;
                }
                m_step = s;
                m_runner.Begin(s.Id, m_now);
                if (m_withGuard && Departures.GoWithGuard(m_npc) == null) m_withGuard = false;
                return true;
            }
        }

        /// <summary>
        /// Wait는 계획 시계보다 실제 설비 상태가 기준. 발효기: 다 익은 게 있으면 바로 끝(누가 미리 넣어 둔 것도 맞춤).
        /// 제련소·숯가마: 10초 넘게 기다렸고 거점의 그 설비가 모두 다 돌았으면(넣은 것 0) 끝 — 재시작 뒤 시계를 처음부터 다시 재므로
        /// 그새 다 된 것을 한참 더 기다리지 않게(10-26). 아직 도는 게 있으면 시계대로(꺼내기도 다 돌 때까지 곁에서 기다림).
        /// </summary>
        void FinishReadyWaits()
        {
            bool any = false;
            foreach (var s in m_runner.Plan.Steps)
            {
                if (s.Kind != StepKind.Wait || m_runner.StateOf(s.Id) != StepState.Running) continue;
                if (MachineStep.FermentReady(m_npc.Home, s.Station, s.Item))
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 발효 다 됨: {s.Item} — 기다림 끝");
                else if (m_runner.RunningFor(s.Id, m_now) >= 10f && MachineStep.SmeltersIdle(m_npc.Home, s.Station))
                    Plugin.Log.LogInfo($"{m_npc.NpcName} {s.Station} 다 돎: {s.Item} — 기다림 끝({m_runner.RunningFor(s.Id, m_now):0}초)");
                else continue;
                m_runner.Complete(s.Id);
                any = true;
            }
            if (any) WorkSaves.Flush(m_npc);
        }

        /// <summary>Wait만 도는 중이고 오래 남았으면 자동 보충 한 번을 끼운다. 항상 true(계속 기다림).</summary>
        bool TryFill()
        {
            if (m_now < m_nextFillAt || !m_runner.OnlyWaiting(m_now)) return true;
            m_nextFillAt = m_now + FillRetry;
            if (m_runner.NextWaitEnds(m_now) < FillMinWait) return true;
            m_filler = Departures.NextRefillJob(m_npc, FillAmount);
            if (m_filler != null) Plugin.Log.LogInfo($"{m_npc.NpcName} Wait 중 보충 시작: {m_filler.Item} {m_filler.Target}");
            return true;
        }

        bool Finish()
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 계획 끝: 완료 {m_runner.Count(StepState.Done)}, 실패 {m_runner.Count(StepState.Failed)}, 막힘 {m_runner.Count(StepState.Blocked)}");
            // 설비만 없던 계획(StationsOnly): 할 수 있는 단계를 다 했으면 "재료는 모았는데 ○○이 없어"(구상서 6-1, 10-26)
            var missing = m_runner.Plan.MissingStations;
            if (missing.Count > 0 && m_runner.Finished)
                m_npc.Say("craft.missingDone", new Dictionary<string, string> { ["item"] = m_goal, ["station"] = Recipes.StationName(missing[0]) });
            return false;
        }
    }
}
