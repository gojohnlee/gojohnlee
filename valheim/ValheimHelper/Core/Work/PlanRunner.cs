using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    public enum StepState { Pending, Running, Done, Failed, Blocked }

    /// <summary>
    /// 제작 계획 실행기의 상태기계 (⑥ 첫 조각). 단계를 실제로 하는 건 플러그인이고, 여기선 "다음에 할 단계"와 진행 상태만 정한다.
    /// Wait는 시작되면 시계로 저절로 끝나고, 그동안 선행이 끝난 다른 단계를 하게 한다(기다리며 다른 일). 실패한 단계에 기댄 단계는 Blocked.
    /// </summary>
    public sealed class PlanRunner
    {
        readonly Plan _plan;
        readonly StepState[] _state;
        readonly float[] _started;

        public PlanRunner(Plan plan)
        {
            _plan = plan;
            _state = new StepState[plan.Steps.Count];
            _started = new float[plan.Steps.Count];
        }

        public Plan Plan => _plan;
        public StepState StateOf(int id) => _state[id];

        bool DepsDone(PlanStep s)
        {
            foreach (int d in s.After) if (_state[d] != StepState.Done) return false;
            return true;
        }

        /// <summary>시계를 보고 시간이 찬 Wait를 끝낸다. 그 뒤 Blocked 전파는 Fail이 이미 했다.</summary>
        public void Tick(float now)
        {
            foreach (var s in _plan.Steps)
                if (s.Kind == StepKind.Wait && _state[s.Id] == StepState.Running && now - _started[s.Id] >= s.Seconds)
                    _state[s.Id] = StepState.Done;
        }

        /// <summary>지금 시작할 수 있는 다음 일 단계(Wait 제외, 번호 순). 없으면 null. Wait는 선행이 끝나는 대로 여기서 시작시킨다.</summary>
        public PlanStep Next(float now)
        {
            Tick(now);
            PlanStep pick = null;
            foreach (var s in _plan.Steps)
            {
                if (_state[s.Id] != StepState.Pending || !DepsDone(s)) continue;
                if (s.Kind == StepKind.Wait) { _state[s.Id] = StepState.Running; _started[s.Id] = now; continue; }
                if (pick == null) pick = s;
            }
            return pick;
        }

        public void Begin(int id, float now) { _state[id] = StepState.Running; _started[id] = now; }
        public void Complete(int id) { _state[id] = StepState.Done; }

        /// <summary>단계 실패: 그 단계와, 그에 (건너서라도) 기댄 단계를 모두 멈춘다.</summary>
        public void Fail(int id)
        {
            _state[id] = StepState.Failed;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var s in _plan.Steps)
                {
                    if (_state[s.Id] != StepState.Pending && _state[s.Id] != StepState.Running) continue;
                    foreach (int d in s.After)
                        if (_state[d] == StepState.Failed || _state[d] == StepState.Blocked) { _state[s.Id] = StepState.Blocked; changed = true; break; }
                }
            }
        }

        public int Count(StepState st) { int n = 0; foreach (var x in _state) if (x == st) n++; return n; }

        /// <summary>모든 단계가 끝났다.</summary>
        public bool Finished => Count(StepState.Done) == _state.Length;

        /// <summary>더 할 수 있는 게 없다(끝났거나 실패·막힘만 남음).</summary>
        public bool Over => Count(StepState.Pending) == 0 && Count(StepState.Running) == 0;

        /// <summary>일 단계는 없고 Wait만 도는 중: 다른 일(자동 보충 등)을 해도 되는 때.</summary>
        public bool OnlyWaiting(float now)
        {
            Tick(now);
            return Next(now) == null && Count(StepState.Running) > 0 && !Over;
        }

        /// <summary>Wait가 끝나기까지 남은 가장 짧은 초(도는 Wait가 없으면 -1).</summary>
        public float NextWaitEnds(float now)
        {
            float best = -1;
            foreach (var s in _plan.Steps)
                if (s.Kind == StepKind.Wait && _state[s.Id] == StepState.Running)
                {
                    float left = Math.Max(0, s.Seconds - (now - _started[s.Id]));
                    if (best < 0 || left < best) best = left;
                }
            return best;
        }
    }
}
