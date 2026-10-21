using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 제작 계획을 실제로 돌리는 일(⑥ 실행기 셋째 조각): PlanRunner가 고른 단계를 하나씩 한다.
    /// Gather → GatherJob(목표만큼 거점 상자에 넣으면 끝), Craft → CraftStep. Load·Unload·Hunt·Farm은 아직 없어서 Failed(그에 기댄 단계는 Blocked).
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

        public PlanJob(WorkerNpc npc, Plan plan, string goalLabel)
        {
            m_npc = npc;
            m_runner = new PlanRunner(plan);
            m_goal = goalLabel;
        }

        public PlanRunner Runner => m_runner;
        public string StepName => m_step != null ? m_step.Kind.ToString() : "Wait";
        public string Label => m_goal;
        public string StateLine() => m_goal + " 만드는 중(" + m_runner.Count(StepState.Done) + "/" + m_runner.Plan.Steps.Count + ")";

        public void AvoidHere(Vector3 danger) { if (m_gather != null) m_gather.AvoidHere(danger); }

        public bool Update(float dt)
        {
            m_now += dt;
            if (m_step == null && !Pick()) return !m_runner.Over || Finish();
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
            }
            if (running) return true;
            if (ok) m_runner.Complete(m_step.Id);
            else
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 계획 단계 {m_step.Id} {m_step.Kind} {m_step.Item} 실패");
                m_runner.Fail(m_step.Id);
            }
            m_step = null; m_gather = null; m_craft = null;
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
                    default: // Load·Unload·Hunt·Farm: 아직 없음
                        m_runner.Fail(s.Id);
                        continue;
                }
                m_step = s;
                m_runner.Begin(s.Id, m_now);
                return true;
            }
        }

        bool Finish()
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 계획 끝: 완료 {m_runner.Count(StepState.Done)}, 실패 {m_runner.Count(StepState.Failed)}, 막힘 {m_runner.Count(StepState.Blocked)}");
            return false;
        }
    }
}
