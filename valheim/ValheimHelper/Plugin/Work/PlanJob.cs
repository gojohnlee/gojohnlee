using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 제작 계획을 실제로 돌리는 일(⑥ 실행기 셋째 조각): PlanRunner가 고른 단계를 하나씩 한다.
    /// Gather → GatherJob(목표만큼 거점 상자에 넣으면 끝), Craft → CraftStep. Load·Unload → MachineStep(Smelter만), Hunt·Farm은 아직 없어서 Failed(그에 기댄 단계는 Blocked).
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
        float m_nextFillAt;
        /// <summary>끼워 넣기는 Wait가 이만큼(초) 남았을 때만, 한 번에 이만큼(개)만 — 끝나면 곧바로 계획으로 돌아온다.</summary>
        const float FillMinWait = 90f, FillRetry = 60f;
        const int FillAmount = 20;

        public PlanJob(WorkerNpc npc, Plan plan, string goalLabel)
        {
            m_npc = npc;
            m_runner = new PlanRunner(plan);
            m_goal = goalLabel;
        }

        /// <summary>작업 상태 저장용: 이 계획을 세운 목표(제작 물건 또는 세트). 이어 할 때 계획을 다시 세운다.</summary>
        public WorkItem Goal;
        public PlanRunner Runner => m_runner;
        public string StepName => m_step != null ? m_step.Kind.ToString() : "Wait";
        public string Label => m_goal;
        public string StateLine() => m_goal + " 만드는 중(" + m_runner.Count(StepState.Done) + "/" + m_runner.Plan.Steps.Count + ")";

        public void AvoidHere(Vector3 danger) { m_gather?.AvoidHere(danger); m_filler?.AvoidHere(danger); }

        public bool Update(float dt)
        {
            m_now += dt;
            if (m_filler != null)
            {
                bool more = m_filler.Update(dt) && m_filler.Delivered < m_filler.Target;
                if (more) return true;
                Plugin.Log.LogInfo($"{m_npc.NpcName} Wait 중 보충 끝: {m_filler.Item} {m_filler.Delivered}/{m_filler.Target}");
                m_filler = null;
                m_nextFillAt = m_now + FillRetry;
                return true;
            }
            if (m_step == null && !Pick()) return !m_runner.Over ? TryFill() : Finish();
            if (m_step == null) return true;

            bool running = true;
            bool ok = false;
            switch (m_step.Kind)
            {
                case StepKind.Gather:
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
            if (ok) m_runner.Complete(m_step.Id);
            else
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 계획 단계 {m_step.Id} {m_step.Kind} {m_step.Item} 실패");
                m_runner.Fail(m_step.Id);
            }
            m_step = null; m_gather = null; m_craft = null; m_machine = null;
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
                        HashSet<ToolKind> ways = ResourceCatalog.WaysFor(s.Item);
                        if (ways.Count == 0 || s.Amount <= 0) { m_runner.Fail(s.Id); continue; }
                        m_gather = new GatherJob(m_npc, s.Item, s.Amount, ways);
                        break;
                    case StepKind.Craft:
                        m_craft = new CraftStep(m_npc, s);
                        break;
                    case StepKind.Load:
                    case StepKind.Unload:
                        if (MachineStep.Supported(s.Station)) { m_machine = new MachineStep(m_npc, s); break; }
                        m_runner.Fail(s.Id);
                        continue;
                    default: // Hunt·Farm: 아직 없음
                        m_runner.Fail(s.Id);
                        continue;
                }
                m_step = s;
                m_runner.Begin(s.Id, m_now);
                return true;
            }
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
            return false;
        }
    }
}
