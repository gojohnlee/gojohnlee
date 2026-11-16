using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public partial class WorkerAI
    {
        // ---- 뛰어내리기 지름길(요청 11-11 ★ 다): 집 안 내려가는 길이 계단보다 빠르면 바닥 끝으로 걸어가 내딛음 ----
        DropShortcut.Spot m_drop;
        /// <summary>0 없음, 1 끝으로 걷기, 2 내딛기(발이 뜰 때까지), 3 떨어지는 중.</summary>
        int m_dropPhase;
        float m_dropUntil, m_dropStartAt;
        Vector3 m_dropDir;
        /// <summary>떨어지는 중(내딛기 포함) — 막힘 시계 멈춤.</summary>
        internal bool Dropping => m_dropPhase >= 2;
        /// <summary>시험용: 뛰어내려 착지한 수·고른 뒤 그만둔 수.</summary>
        internal int DropsTaken, DropsQuit;
        /// <summary>시험용(vh_droptest): 켜면 뛰어내리기를 안 고름 — 계단 길 시간 비교.</summary>
        internal static bool TestNoDrop;

        void DropStart(Vector3 pos, Vector3 target, bool run)
        {
            m_dropPhase = 0;
            if (TestNoDrop || !(m_character is WorkerNpc n) || !NearHome(pos) || !NearHome(target) || pos.y - target.y < DropShortcut.MinDrop || !m_character.IsOnGround()) return;
            var drops = Brain.Drops(n.Home);
            if (drops.Count == 0) return;
            float stair = FullPathLength(pos, target);
            if (stair < 0f) stair = 1000f; // 계단 길 없음 — 뛰어내리기만 길
            float speed = run ? m_character.m_runSpeed : m_character.m_walkSpeed;
            if (!DropShortcut.Pick(drops.Spots, P(pos), P(target), stair, speed, out var s)) return;
            m_drop = s; m_dropPhase = 1; m_dropStartAt = Time.time;
            m_dropDir = Flat(V(s.Land) - V(s.Edge)).normalized;
            m_dropUntil = Time.time + Utils.DistanceXZ(pos, V(s.Edge)) / Mathf.Max(speed, 0.5f) + 4f; // 끝까지 걷기 + 여유
            m_memPath = null;
            Diag.Log($"뛰어내리기 고름: {pos:F1} → {target:F1}, 끝 {V(s.Edge):F1} 높이 {s.Height:0.0}m, 계단 길 {stair:0.0}m");
        }

        /// <summary>목표에 닿음: 떨어져 내려와 있으면 착지로 셈.</summary>
        void DropEnd(Vector3 pos) { if (m_dropPhase == 3 && pos.y < m_drop.Edge.Y - 1f) Landed(pos); m_dropPhase = 0; }

        void Landed(Vector3 pos) { m_dropPhase = 0; DropsTaken++; Diag.Log($"뛰어내리기 착지: {pos:F1}, {Time.time - m_dropStartAt:0.0}초"); }

        void DropQuit(string why) { m_dropPhase = 0; DropsQuit++; Diag.Log($"뛰어내리기 그만({why}) — 바닐라 길"); }

        /// <summary>뛰어내리기 중이면 한 걸음 맡고 true.</summary>
        bool DropStep(Vector3 pos, bool run)
        {
            if (m_dropPhase == 0) return false;
            Vector3 edge = V(m_drop.Edge);
            if (m_dropPhase < 3 && pos.y < edge.y - 1f) { m_dropPhase = 3; m_dropUntil = Time.time + 4f; } // 끝 닿기 전에 이미 떨어짐
            switch (m_dropPhase)
            {
                case 1:
                    if (Time.time > m_dropUntil) { DropQuit("끝까지 못 감"); return false; }
                    if (Utils.DistanceXZ(pos, edge) > 0.3f) { MoveTowards(Flat(edge - pos), run); m_moveDiag = "뛰어내리기 끝으로"; return true; }
                    m_dropPhase = 2; m_dropUntil = Time.time + 2f;
                    goto case 2;
                case 2:
                    if (Time.time > m_dropUntil) { DropQuit("내딛어도 안 떨어짐"); return false; }
                    MoveTowards(m_dropDir, run); m_moveDiag = "뛰어내리기 내딛기";
                    if (!m_character.IsOnGround() && pos.y < edge.y - 0.3f) { m_dropPhase = 3; m_dropUntil = Time.time + 4f; }
                    return true;
                default:
                    if (Time.time > m_dropUntil) { DropQuit("착지 못 함"); return false; }
                    MoveTowards(m_dropDir, false); m_moveDiag = "뛰어내리기 떨어지는 중";
                    if (m_character.IsOnGround() && pos.y < edge.y - 1f)
                    { Landed(pos); return false; }
                    return true;
            }
        }
    }
}
