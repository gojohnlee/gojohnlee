using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 집 안 걸음 기록기(사용자 11-11 "집 안에서 사람처럼" ①): 0.25초마다 자리·속도·남은 길(BaseAI.m_path)·벽 거리·문 곁·보는지·일을
    /// WalkTrace 한 줄로 모아 behavior-house.txt에 쓰고 요약(Core WalkTrace.Analyze)을 돌려준다. 시험이 Start/Stop으로 감싼다.
    /// </summary>
    sealed class WalkRecorder
    {
        public const float Step = 0.25f, SeeNear = 20f, SeeFar = 64f;
        static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, List<Vector3>> PathOf = HarmonyLib.AccessTools.FieldRefAccess<BaseAI, List<Vector3>>("m_path");

        readonly WorkerNpc m_npc;
        readonly List<Collider> m_walls;
        readonly List<Door> m_doors = new List<Door>();
        readonly List<WalkTrace.Sample> m_samples = new List<WalkTrace.Sample>();
        bool m_done;

        public IList<WalkTrace.Sample> Samples => m_samples;

        WalkRecorder(WorkerNpc npc, List<Collider> walls)
        {
            m_npc = npc;
            m_walls = walls;
            foreach (var col in walls)
            {
                var d = col != null ? col.GetComponentInParent<Door>() : null;
                if (d != null && !m_doors.Contains(d)) m_doors.Add(d);
            }
        }

        /// <summary>walls = 집 조각 충돌체(벽 거리·문 찾기). 끝은 Stop.</summary>
        public static WalkRecorder Start(WorkerNpc npc, List<Collider> walls)
        {
            var r = new WalkRecorder(npc, walls);
            Plugin.Instance.StartCoroutine(r.Run());
            return r;
        }

        /// <summary>그만 재고 behavior-house.txt(머리에 이름·요약)를 쓴 뒤 요약을 돌려준다.</summary>
        public WalkTrace.Report Stop(string name)
        {
            m_done = true;
            var rep = WalkTrace.Analyze(m_samples);
            var lines = new List<string> { $"# {name} {System.DateTime.Now:yyyy-MM-dd HH:mm} 줄 {m_samples.Count}", "# " + rep, "# t x y z speed goal wall door seen job" };
            foreach (var s in m_samples) lines.Add(WalkTrace.Line(s));
            try { File.WriteAllLines(Path.Combine(TestRunner.PluginDir, "behavior-house.txt"), lines.ToArray()); }
            catch (IOException e) { Plugin.Log.LogWarning($"behavior-house.txt 못 씀: {e.Message}"); }
            return rep;
        }

        IEnumerator Run()
        {
            float t0 = Time.time;
            var wait = new WaitForSeconds(Step);
            Vector3 last = m_npc != null ? m_npc.transform.position : Vector3.zero;
            float lastT = Time.time;
            while (!m_done && m_npc != null)
            {
                yield return wait;
                if (m_done || m_npc == null) break;
                Vector3 p = m_npc.transform.position;
                float dt = Mathf.Max(0.01f, Time.time - lastT);
                var job = m_npc.Ai.Job;
                string step = job is PlanJob plan && plan.InnerStep != null ? plan.InnerStep : job?.StepName;
                m_samples.Add(new WalkTrace.Sample
                {
                    T = Time.time - t0, X = p.x, Y = p.y, Z = p.z,
                    Speed = Utils.DistanceXZ(p, last) / dt,
                    Goal = job != null && !Standing(step) ? PathLeft(p) : -1f,
                    Wall = WallGap(p), Door = DoorNear(p), Seen = Seen(p),
                    Job = job != null ? job.GetType().Name + "/" + step : ""
                });
                last = p; lastT = Time.time;
            }
        }

        // 제자리 단계(베기·캐기, 찾기·도구 고르기)는 걷는 구간이 아님 — m_path가 지난 길로 남아 진척 없음으로 잘못 셈(회차 400 첫 기록).
        static bool Standing(string step) => step == "Work" || step == "Search" || step == "Tool" || step == "Prep" || step == "Unload";

        // 남은 길 =지금 자리 → m_path 점들(지난 점은 FollowPath가 지움). 길이 비면 음수(목표 없음으로 침).
        float PathLeft(Vector3 p)
        {
            var path = PathOf(m_npc.Ai);
            if (path == null || path.Count == 0) return -1f;
            float sum = Utils.DistanceXZ(p, path[0]);
            for (int i = 1; i < path.Count; i++) sum += Utils.DistanceXZ(path[i - 1], path[i]);
            return sum;
        }

        // 몸 겉에서 가장 가까운 집 조각까지(몸 가운데 높이, 캡슐 반지름 뺌). 문짝은 빼지 않음.
        float WallGap(Vector3 p)
        {
            var cap = m_npc.GetComponent<CapsuleCollider>();
            float radius = cap != null ? cap.radius : 0.4f;
            Vector3 mid = p + Vector3.up;
            float near = float.MaxValue;
            foreach (var col in m_walls)
                if (col != null && col.enabled && !col.isTrigger) near = Mathf.Min(near, Vector3.Distance(col.ClosestPoint(mid), mid));
            return near == float.MaxValue ? -1f : Mathf.Max(0f, near - radius);
        }

        // 1.5m 안 문: 열림(s_state ≠ 0) 1, 닫힘 2, 없음 0.
        int DoorNear(Vector3 p)
        {
            foreach (var d in m_doors)
            {
                if (d == null || Utils.DistanceXZ(d.transform.position, p) > 1.5f) continue;
                var zdo = d.GetComponent<ZNetView>()?.GetZDO();
                return zdo != null && zdo.GetInt(ZDOVars.s_state) != 0 ? 1 : 2;
            }
            return 0;
        }

        // 보는지: 20m 안이면 봄, 64m 안이면 카메라 시야 안 + 카메라에서 몸까지 막힘 없음.
        static bool Seen(Vector3 p)
        {
            var cam = Utils.GetMainCamera();
            if (cam == null) return false;
            Vector3 body = p + Vector3.up;
            float dist = Vector3.Distance(cam.transform.position, body);
            if (dist <= SeeNear) return true;
            if (dist > SeeFar) return false;
            Vector3 v = cam.WorldToViewportPoint(body);
            if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) return false;
            return !Physics.Linecast(cam.transform.position, body, LayerMask.GetMask("Default", "static_solid", "piece", "terrain"));
        }
    }
}
