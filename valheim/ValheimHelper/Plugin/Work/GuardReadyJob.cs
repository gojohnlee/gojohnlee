using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 호위병 준비(사용자 10-26): 쉬는 호위병이 가끔 거점 상자에서 더 좋은 무기·갑옷·음식·미드를 챙긴다(일꾼 출발 준비 Prep와 같은 규칙, 활은 안 챙김).
    /// 챙길 게 없으면 바로 끝난다. 일꾼이 부르면(Follow) 이 일은 그냥 버려진다(Prep은 상자마다 한 번에 옮겨서 중간에 끊겨도 잃는 것 없음).
    /// </summary>
    public sealed class GuardReadyJob : IWorkerJob
    {
        readonly Prep m_prep;

        public GuardReadyJob(WorkerNpc npc) { m_prep = new Prep(npc); }

        public bool Update(float dt) => m_prep.Update(dt);
        public string StepName => "Prep";
        public string StateLine() => "장비 챙기는 중";
        public void AvoidHere(Vector3 danger) { }
        public string Label => "장비 챙기기";
    }
}
