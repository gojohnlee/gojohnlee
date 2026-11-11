using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 실플레이 집 안 걸음 기록(사용자 11-11 "집 안에서 사람처럼" ④): 설정 Features.PlayWalkLog(기본 끔)을 켜면 이 PC가 맡은 일꾼이
    /// 집(Home) 40m 안에서 일하는 동안 WalkRecorder로 재고, 집을 벗어나거나 일이 없거나 2분이 차면 behavior-house-play.txt에 한 구간 덧붙임.
    /// 파일이 2MB를 넘으면 .old로 돌려 크기 상한. 시험 중에는 안 잼(시험은 behavior-house.txt를 따로 씀).
    /// </summary>
    static class PlayWalkLog
    {
        public const float HouseRadius = 40f, WallRadius = 30f, Segment = 120f, Check = 1f;
        public const long MaxBytes = 2L * 1024 * 1024;
        const string File = "behavior-house-play.txt";

        sealed class Rec { public WalkRecorder R; public float Since; }
        static readonly Dictionary<WorkerNpc, Rec> s_recs = new Dictionary<WorkerNpc, Rec>();
        static readonly List<Piece> s_pieces = new List<Piece>();
        static readonly List<WorkerNpc> s_gone = new List<WorkerNpc>();
        static float s_tick;
        /// <summary>시험용(vh_newerahouse): 켜면 설정·시험 중과 상관없이 잼, 집 가운데를 Home 대신.</summary>
        public static bool TestOn;
        public static Vector3? TestHome;

        public static void Tick(float dt)
        {
            if (s_recs.Count == 0 && !Features.PlayWalkLog && !TestOn) return;
            s_tick += dt;
            if (s_tick < Check) return;
            s_tick = 0f;
            bool on = TestOn || (Features.PlayWalkLog && !TestRunner.Running);
            s_gone.Clear();
            foreach (var kv in s_recs)
                if (!on || kv.Key == null || !Inside(kv.Key) || Time.time - kv.Value.Since >= Segment) s_gone.Add(kv.Key);
            foreach (var n in s_gone) Close(n);
            if (!on) return;
            foreach (var n in WorkerNpc.All)
                if (n != null && !s_recs.ContainsKey(n) && Inside(n))
                    s_recs[n] = new Rec { R = WalkRecorder.Start(n, Walls(n.transform.position)), Since = Time.time };
        }

        static bool Inside(WorkerNpc n) => n.Zdo != null && n.Zdo.IsOwner() && n.Ai != null && n.Ai.Job != null && !n.IsGuard
            && Utils.DistanceXZ(n.transform.position, TestHome ?? n.Home) <= HouseRadius;

        /// <summary>재던 구간을 모두 씀(시험 끝).</summary>
        public static void Flush() { s_gone.Clear(); s_gone.AddRange(s_recs.Keys); foreach (var n in s_gone) Close(n); }

        static void Close(WorkerNpc n)
        {
            var r = s_recs[n];
            s_recs.Remove(n);
            if (n == null) { r.R.Abort(); return; }
            if (r.R.Samples.Count < 8) { r.R.Abort(); return; } // 2초 안 된 구간은 버림
            Rotate();
            r.R.Stop("실플레이 " + n.NpcName, true, File);
        }

        // 크기 상한: 넘으면 옛 것 하나만 남김
        static void Rotate()
        {
            try
            {
                string path = Path.Combine(TestRunner.PluginDir, File);
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < MaxBytes) return;
                string old = path + ".old";
                if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                System.IO.File.Move(path, old);
            }
            catch (IOException e) { Plugin.Log.LogWarning($"{File} 돌리기 못 함: {e.Message}"); }
        }

        // 벽 = 둘레 30m 조각 충돌체(구간 시작 때 한 번 — 2분마다 새로라 그사이 놓은 조각은 다음 구간부터)
        static List<Collider> Walls(Vector3 p)
        {
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(p, WallRadius, s_pieces);
            var cols = new List<Collider>();
            foreach (var pc in s_pieces) if (pc != null) foreach (var col in pc.GetComponentsInChildren<Collider>()) if (!col.isTrigger && !(col is MeshCollider mc && !mc.convex)) cols.Add(col); // ClosestPoint 되는 것만(NewEraHouseTest와 같음)
            return cols;
        }
    }
}
