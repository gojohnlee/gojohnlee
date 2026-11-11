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
        // 걷는 중 벽 0.3m 안에 붙은 조각별 횟수(이름@자리, 회차 407 — 벽 23~38%가 어디서 나는지)
        readonly Dictionary<string, int> m_nearPieces = new Dictionary<string, int>();
        bool m_done;
        // 구간 출발 자리·도착(m_path 끝) — 그 손 닿기 2.8m 안은 벽 셈에서 뺌(회차 408: 붙는 것 대부분이 목적지 화덕·상자였음)
        Vector3 m_legStart, m_legGoal;
        bool m_hasGoal;
        public const float Reach = 2.8f;

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

        /// <summary>그만 재고 behavior-house.txt(머리에 이름·요약)를 쓴 뒤 요약을 돌려준다. append면 뒤에 붙임(한 시험 여러 단계 — 단계마다 머리 두 줄).</summary>
        public WalkTrace.Report Stop(string name, bool append)
        {
            m_done = true;
            var rep = WalkTrace.Analyze(m_samples);
            var lines = new List<string> { $"# {name} {System.DateTime.Now:yyyy-MM-dd HH:mm} 줄 {m_samples.Count}", "# " + rep, "# 벽 붙음 " + NearPieces(), "# t x y z speed goal wall door seen job" };
            foreach (var s in m_samples) lines.Add(WalkTrace.Line(s));
            try
            {
                string path = Path.Combine(TestRunner.PluginDir, "behavior-house.txt");
                if (append) File.AppendAllLines(path, lines); else File.WriteAllLines(path, lines.ToArray());
            }
            catch (IOException e) { Plugin.Log.LogWarning($"behavior-house.txt 못 씀: {e.Message}"); }
            return rep;
        }

        IEnumerator Run()
        {
            float t0 = Time.time;
            var wait = new WaitForSeconds(Step);
            Vector3 last = m_npc != null ? m_npc.transform.position : Vector3.zero;
            float lastT = Time.time;
            m_legStart = last;
            while (!m_done && m_npc != null)
            {
                yield return wait;
                if (m_done || m_npc == null) break;
                Vector3 p = m_npc.transform.position;
                float dt = Mathf.Max(0.01f, Time.time - lastT);
                float speed = Utils.DistanceXZ(p, last) / dt;
                float gap = WallGap(p, out var nearCol);
                if (NearEnds(p)) { gap = -1f; nearCol = null; }
                if (nearCol != null && gap < WalkTrace.WallNear && speed >= WalkTrace.StandSpeed) CountNear(nearCol);
                var job = m_npc.Ai.Job;
                string step = job is PlanJob plan && plan.InnerStep != null ? plan.InnerStep : job?.StepName;
                m_samples.Add(new WalkTrace.Sample
                {
                    T = Time.time - t0, X = p.x, Y = p.y, Z = p.z,
                    Speed = speed,
                    Goal = job != null && !Standing(step) && Time.time - m_npc.Ai.HoldAt > Step ? PathLeft(p) : -1f,
                    Wall = gap, Door = DoorNear(p), Seen = Seen(p),
                    Job = job != null ? job.GetType().Name + "/" + step : ""
                });
                last = p; lastT = Time.time;
            }
        }

        // 제자리 단계(베기·캐기, 찾기·도구 고르기)는 걷는 구간이 아님 — m_path가 지난 길로 남아 진척 없음으로 잘못 셈(회차 400 첫 기록).
        // 일부러 선 순간(WorkerAI.Hold — 줍기 "떨어짐 기다림" 등, 회차 401)도 뺌: Hold는 서 있는 동안 프레임마다 불림.
        static bool Standing(string step) => step == "Work" || step == "Search" || step == "Tool" || step == "Prep" || step == "Unload";

        // 남은 길 =지금 자리 → m_path 점들(지난 점은 FollowPath가 지움) → 층 오름이면 모퉁이 뒤 남은 길(FloorRest)까지. 길이 비면 음수(목표 없음으로 침).
        // 회차 409: m_path는 다음 모퉁이까지뿐이라 모퉁이마다 값이 튀어 진척 없음·길/직선이 잘못 나왔음.
        float PathLeft(Vector3 p)
        {
            var path = PathOf(m_npc.Ai);
            if (path == null || path.Count == 0) return -1f;
            float sum = Utils.DistanceXZ(p, path[0]);
            for (int i = 1; i < path.Count; i++) sum += Utils.DistanceXZ(path[i - 1], path[i]);
            var rest = m_npc.Ai.FloorRest;
            if (rest != null && rest.Count > 0)
            {
                sum += Utils.DistanceXZ(path[path.Count - 1], rest[0]);
                for (int i = 1; i < rest.Count; i++) sum += Utils.DistanceXZ(rest[i - 1], rest[i]);
            }
            return sum;
        }

        // 출발·도착 손 닿기 안인지. 도착(m_path 끝, 층 오름이면 그 길의 끝)이 2.8m 넘게 바뀌면 새 구간 — 지금 자리가 새 출발.
        bool NearEnds(Vector3 p)
        {
            var path = PathOf(m_npc.Ai);
            if (path != null && path.Count > 0)
            {
                var rest = m_npc.Ai.FloorRest;
                Vector3 g = rest != null && rest.Count > 0 ? rest[rest.Count - 1] : path[path.Count - 1];
                if (m_hasGoal && Utils.DistanceXZ(g, m_legGoal) > Reach) m_legStart = p;
                m_legGoal = g; m_hasGoal = true;
            }
            return Utils.DistanceXZ(p, m_legStart) < Reach || (m_hasGoal && Utils.DistanceXZ(p, m_legGoal) < Reach);
        }

        // 몸 겉에서 가장 가까운 집 조각까지(몸 가운데 높이, 캡슐 반지름 뺌). 문짝은 빼지 않음.
        float WallGap(Vector3 p, out Collider nearCol)
        {
            nearCol = null;
            var cap = m_npc.GetComponent<CapsuleCollider>();
            float radius = cap != null ? cap.radius : 0.4f;
            Vector3 mid = p + Vector3.up;
            float near = float.MaxValue;
            foreach (var col in m_walls)
                if (col != null && col.enabled && !col.isTrigger)
                {
                    // 옆쪽만 벽: 가장 가까운 점이 수평보다 위·아래로 더 멀면(디딘 계단·바닥, 머리 위 천장·들보) 뺌 — 회차 405 벽 10~35%는 계단 자체를 셈(회차 406)
                    Vector3 d = col.ClosestPoint(mid) - mid;
                    float flat = new Vector2(d.x, d.z).magnitude;
                    if (Mathf.Abs(d.y) > flat) continue;
                    if (d.magnitude < near) { near = d.magnitude; nearCol = col; }
                }
            return near == float.MaxValue ? -1f : Mathf.Max(0f, near - radius);
        }

        void CountNear(Collider col)
        {
            var piece = col.GetComponentInParent<Piece>();
            var t = piece != null ? piece.transform : col.transform;
            Vector3 q = t.position;
            string key = $"{Utils.GetPrefabName(t.gameObject)}@{q.x:F1},{q.z:F1},{q.y:F1}";
            m_nearPieces.TryGetValue(key, out int n);
            m_nearPieces[key] = n + 1;
        }

        // 많이 붙은 조각 8개: 이름@x,z,y×횟수
        string NearPieces()
        {
            var top = new List<KeyValuePair<string, int>>(m_nearPieces);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < top.Count && i < 8; i++) parts.Add($"{top[i].Key}×{top[i].Value}");
            return parts.Count > 0 ? string.Join(" ", parts.ToArray()) : "없음";
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
