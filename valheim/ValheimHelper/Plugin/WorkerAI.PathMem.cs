using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>WorkerAI 집 안 길 기억(요청 11-15 ③ 첫 조각): 집(일꾼 집 40m) 안에서 걸어 닿은 길을 Brain.Paths에 넣는다. 길 찾기에 쓰기는 다음 조각.</summary>
    public partial class WorkerAI
    {
        readonly PathTrail m_trail = new PathTrail();
        /// <summary>시험용: 이 일꾼이 기억에 넣은 길 수.</summary>
        internal int PathsRemembered;

        static PathMemory.P P(Vector3 v) => new PathMemory.P(v.x, v.y, v.z);

        bool NearHome(Vector3 v) => m_character is WorkerNpc n && !Character.InInterior(v) && Utils.DistanceXZ(v, n.Home) <= PlayWalkLog.HouseRadius;

        // 일은 대개 손 닿기 안이면 스스로 도착으로 치고 다음 목표를 줌(MoveToPoint 도착 줄까지 안 옴) → 새 목표를 받을 때 옛 목표 곁(가로 2.5m·높이 1.5m 안)이면 닿은 것으로
        void TrailStart(Vector3 pos, Vector3 oldTarget)
        {
            if (m_trail.Active && Utils.DistanceXZ(pos, oldTarget) <= 2.5f && Mathf.Abs(pos.y - oldTarget.y) <= 1.5f) TrailDone(pos, oldTarget);
            if (NearHome(pos)) m_trail.Start(P(pos));
            else m_trail.Stop();
        }

        void TrailStep(Vector3 pos) { if (m_trail.Active) m_trail.Step(P(pos)); }

        /// <summary>닿음: 자취는 지금 자리에서 끝나고, 열쇠는 출발 자리 → 목표(다음에 같은 목표를 받을 때 찾게).</summary>
        void TrailDone(Vector3 pos, Vector3 target)
        {
            if (!m_trail.Active) return;
            var from = m_trail.From;
            var pts = m_trail.Finish(P(pos));
            if (pts == null || !NearHome(target)) return;
            Brain.Paths.Remember(from, P(target), pts, ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time);
            PathsRemembered++;
            Diag.Log($"길 기억: ({from.X:0.0},{from.Y:0.0},{from.Z:0.0})→{target:F1} 점 {pts.Length}, 모두 {Brain.Paths.Count}");
        }
    }
}
