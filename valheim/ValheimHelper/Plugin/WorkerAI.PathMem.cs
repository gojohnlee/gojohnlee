using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>WorkerAI 집 안 길 기억(요청 11-15 ③ 첫 조각): 집(일꾼 집 40m) 안에서 걸어 닿은 길을 Brain.Paths에 넣는다. 같은 출발·목표면 그 길을 따라 걷는다(막히면 지우고 바닐라).</summary>
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
            if (pts == null && m_trail.SpoilReason != null) { PathsSpoiled++; Diag.Log($"길 기억 안 함({m_trail.SpoilReason}): ({from.X:0.0},{from.Y:0.0},{from.Z:0.0})→{target:F1}"); }
            if (pts == null || !NearHome(target)) return;
            Brain.Paths.Remember(from, P(target), pts, ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time);
            PathsRemembered++;
            Diag.Log($"길 기억: ({from.X:0.0},{from.Y:0.0},{from.Z:0.0})→{target:F1} 점 {pts.Length}, 모두 {Brain.Paths.Count}");
        }

        // ---- 기억한 길 따라 걷기(③ 마지막 조각): 같은 출발·목표 칸이면 걸어 닿았던 점을 차례로 따라간다. 막히면 그 길을 지우고 바닐라로 ----
        PathMemory.P[] m_memPath; int m_memIdx; PathMemory.P m_memFrom, m_memTo;
        /// <summary>시험용: 기억한 길로 걷기 시작한 수, 막혀 지운 수.</summary>
        internal int PathsFollowed, PathsDropped;
        /// <summary>시험용: 기억한 길 따라 걷는 중.</summary>
        internal bool OnMemPath => m_memPath != null;
        /// <summary>시험용: 다음 MoveToPoint를 새 목표로 치게(같은 목표로 다시 걸리면 따라 걷기·자취가 새로 안 시작됨).</summary>
        internal void ForgetMoveTarget() { m_moveTarget = new Vector3(0f, -10000f, 0f); m_trail.Stop(); }
        static int s_memMask;
        static int MemMask => s_memMask != 0 ? s_memMask : (s_memMask = LayerMask.GetMask("piece"));

        void MemPathStart(Vector3 pos, Vector3 target)
        {
            m_memPath = null;
            if (!NearHome(pos) || !NearHome(target)) return;
            var from = P(pos); var to = P(target);
            var pts = Brain.Paths.Find(from, to, ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time);
            if (pts == null || pts.Length < 2) return;
            // 닫힌 문·새 벽(손님이 놓은 조각은 서버가 못 봄): 점 사이를 허리 높이(0.8m)로 건축물 층에 쏴 막히면 지우고 바닐라
            for (int i = 1; i < pts.Length; i++)
            {
                Vector3 a = V(pts[i - 1]) + Vector3.up * 0.8f, b = V(pts[i]) + Vector3.up * 0.8f;
                if (Physics.Linecast(a, b, MemMask)) { Brain.Paths.Forget(from, to); PathsDropped++; Diag.Log($"길 기억 막힘(놓을 때 {i}/{pts.Length}): 지움 → 바닐라"); return; }
            }
            m_memPath = pts; m_memIdx = 1; m_memFrom = from; m_memTo = to;
            PathsFollowed++;
        }

        static Vector3 V(PathMemory.P p) => new Vector3(p.X, p.Y, p.Z);

        /// <summary>기억한 길 다음 점으로 한 걸음. 길 끝이면 놓고 false(남은 몇 m는 바닐라).</summary>
        bool MemPathStep(Vector3 pos, bool run)
        {
            if (m_memPath == null) return false;
            // 밀리거나 지나쳤으면 앞 4점 안에서 0.7m 곁인 가장 먼 점 다음으로
            for (int i = Mathf.Min(m_memPath.Length - 1, m_memIdx + 4); i >= m_memIdx; i--)
            {
                Vector3 q = V(m_memPath[i]);
                if (Utils.DistanceXZ(pos, q) < 0.7f && Mathf.Abs(pos.y - q.y) < 1.5f) { m_memIdx = i + 1; break; }
            }
            if (m_memIdx >= m_memPath.Length) { m_memPath = null; return false; }
            MoveTowards(Flat(V(m_memPath[m_memIdx]) - pos), run);
            m_moveDiag = $"길 기억 따라 {m_memIdx}/{m_memPath.Length}";
            return true;
        }

        /// <summary>시험용: 성공한 길이 아니라 기억 안 한 자취 수.</summary>
        internal int PathsSpoiled;

        /// <summary>성공한 길이 아님(사용자 11-15 — 피해·막힘·문 못 엶): 걷는 자취는 기억 안 하고, 기억한 길로 가던 중이면 그 길을 지우고 바닐라로.</summary>
        internal void PathSpoil(string why)
        {
            m_trail.Spoil(why);
            if (m_memPath == null) return;
            Brain.Paths.Forget(m_memFrom, m_memTo);
            PathsDropped++;
            Diag.Log($"길 기억 {why}({m_memIdx}/{m_memPath.Length}): 지움 → 바닐라");
            m_memPath = null;
        }
    }
}
