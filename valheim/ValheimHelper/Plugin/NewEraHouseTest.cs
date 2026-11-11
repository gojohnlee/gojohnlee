using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 사용자 집 걸음 시험(사용자 11-11 20:53 "집 걸음은 내 집에서 먼저"): 사용자 월드 TheNewEra 복사본 vh_house_TheNewEra의
    /// 메인 베이스(가운데 (-3959,-5829), 1층 돌 바닥·2층 나무 바닥)에서 일꾼을 집 안에 두고 집 안 길만 걷게 한다 —
    /// 1층 상자 → 부엌 화덕 → 2층 침대(계단 오름) → 1층 상자(내림) → 침대 → 화덕. 나무 베기·바깥 채집은 없음.
    /// 깃발은 집 밖 빈 땅에(TestKit.Setup의 around — 거점 상자 치우기·생물 치우기를 안 해 사용자 집 살림을 그대로 둠).
    /// 다리마다 WalkRecorder로 behavior-house.txt에 이어 쓰고(층별 그림은 scripts/vh-house-map.ps1) 요약을 diag에.
    /// 실행: VH_WORLD=vh_house_TheNewEra, VH_TEST_WORLDS=vh_house_TheNewEra(되돌리기 원본은 첫 실행 때 복사본에서 뜸 — 원본 TheNewEra는 안 건드림).
    /// </summary>
    static class NewEraHouseTest
    {
        public static void Register() => TestRunner.Register("vh_newerahouse", "TheNewEra 복사본 집 안 걸음(상자·화덕·2층 침대)", () => TestKit.Flat(Run()));

        const string World = "vh_house_TheNewEra";
        static readonly Vector3 Center = new Vector3(-3959f, 0f, -5829f);
        const float Radius = 22f;

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer() || ZNet.instance.GetWorldName() != World)
            {
                TestRunner.Log($"FAIL: 월드 {World}에서(VH_WORLD) 서버로 실행해야 합니다(지금 {ZNet.instance?.GetWorldName()}).");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            try { yield return Body(w, checks); }
            finally { TestKit.Restore(w); }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        /// <summary>집 밖 빈 땅: 집에서 16~52m, 위에 건축 조각이 없고 2.5m 안에 건축 조각이 없는 곳. why = 못 쓴 까닭 셈.</summary>
        static Vector3? OpenGround(out string why)
        {
            float wl = ZoneSystem.instance.m_waterLevel;
            int water = 0, roof = 0, near = 0;
            why = "";
            for (float d = 16f; d <= 52f; d += 3f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 p = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                    if (!ZoneSystem.instance.GetGroundHeight(p, out float h) || h < wl + 1f) { water++; continue; }
                    p.y = h;
                    if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out RaycastHit hit, 39.5f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Piece>() != null) { roof++; continue; }
                    bool clear = true;
                    foreach (var col in Physics.OverlapSphere(p + Vector3.up, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                        if (col.GetComponentInParent<Piece>() != null) { clear = false; break; }
                    if (clear) return p;
                    near++;
                }
            why = $"물·못 읽음 {water}·위 조각 {roof}·곁 조각 {near}";
            return null;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            Player pl = Player.m_localPlayer;
            float gy = WorldGenerator.instance.GetHeight(Center.x, Center.z);
            pl.TeleportTo(new Vector3(Center.x + 20f, gy + 1f, Center.z), pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            yield return new WaitForSeconds(5f); // 집 조각 1,192개가 다 생길 때까지
            int pieces = 0;
            var beds = new List<Bed>(); var chests = new List<Container>(); var fires = new List<Fireplace>(); var doors = new List<Door>();
            int stairs = 0;
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (Utils.DistanceXZ(p.transform.position, Center) > Radius) continue;
                pieces++;
                if (p.GetComponent<Bed>() is Bed b) beds.Add(b);
                if (p.GetComponent<Container>() is Container c) chests.Add(c);
                if (p.GetComponent<Fireplace>() is Fireplace f) fires.Add(f);
                if (p.GetComponent<Door>() is Door d) doors.Add(d);
                if (Utils.GetPrefabName(p.gameObject).Contains("stair")) stairs++;
            }
            TestRunner.Log($"집 조각 {pieces}개({Radius:0}m 안): 침대 {beds.Count}·상자 {chests.Count}·불 {fires.Count}·문 {doors.Count}·계단 {stairs}");
            checks.Add($"집이 불러와짐 (조각 {pieces}, 침대 {beds.Count})", pieces > 500 && beds.Count > 0);
            if (beds.Count == 0) yield break;
            Bed bed = beds[0];
            float upper = bed.transform.position.y - 3f; // 침대가 2층 → 3m 아래부터 1층
            Container chest = null; Fireplace fire = null;
            foreach (var c in chests) if (c.transform.position.y < upper && (chest == null || Utils.DistanceXZ(c.transform.position, Center) < Utils.DistanceXZ(chest.transform.position, Center))) chest = c;
            foreach (var f in fires) if (f.transform.position.y < upper && (fire == null || Utils.GetPrefabName(f.gameObject).Contains("hearth"))) fire = f;
            Plugin.Log.LogInfo($"TheNewEra 집: 침대 {bed.transform.position:F1}, 1층 상자 {chest?.transform.position:F1}, 화덕 {(fire != null ? Utils.GetPrefabName(fire.gameObject) : "없음")} {fire?.transform.position:F1}");
            checks.Add($"1층 상자·화덕 찾음 (상자 {chest != null}, 화덕 {(fire != null ? Utils.GetPrefabName(fire.gameObject) : "없음")})", chest != null && fire != null);
            if (chest == null || fire == null) yield break;

            Vector3? open = OpenGround(out string whyNot);
            checks.Add($"집 밖 깃발 자리 ({open?.ToString("F1") ?? "없음 — " + whyNot})", open.HasValue);
            if (!open.HasValue) yield break;
            Vector3 out_ = open.Value - Center; out_.y = 0f;
            pl.TeleportTo(open.Value + Vector3.up * 0.3f, Quaternion.LookRotation(out_.normalized), false); // 깃발은 플레이어 앞 4m = 집 반대쪽
            yield return new WaitForSeconds(2f);
            yield return TestKit.Setup(w, pl.transform.position);
            checks.Add("깃발·NPC 2명", w.Ragnar != null && w.Floki != null);
            if (w.Ragnar == null || w.Floki == null) yield break;

            // 벽 거리용 충돌체: 집 조각만, ClosestPoint가 되는 것만(오목 메시는 늘 0이 나옴 — Unity는 Box·Sphere·Capsule·볼록 메시만)
            var cols = new List<Collider>();
            foreach (var col in Physics.OverlapSphere(new Vector3(Center.x, bed.transform.position.y, Center.z), Radius + 4f))
                if (!col.isTrigger && col.GetComponentInParent<Piece>() != null && !(col is MeshCollider mc && !mc.convex)) cols.Add(col);

            Vector3 Near(Component c) { Vector3 p = c.transform.position, to = Center - p; to.y = 0f; return p + to.normalized * 1.2f; }
            Vector3 start = Near(chest) + Vector3.up * 0.5f;
            w.Ragnar.TeleportTo(start, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(2f);
            int tele0 = Teleports();
            string[] names = { "상자→화덕", "화덕→침대(오름)", "침대→상자(내림)", "상자→침대(오름)", "침대→화덕(내림)" }; // ValueTuple은 게임 mono에 없음
            Component[] tos = { fire, bed, chest, bed, fire };
            int reached = 0, jumps = 0;
            for (int i = 0; i < names.Length; i++)
            {
                Vector3 goal = tos[i].transform.position;
                var walk = new ExploreTest.WalkJob(w.Ragnar.Ai, goal, true);
                var rec = WalkRecorder.Start(w.Ragnar, cols);
                w.Ragnar.Ai.StartJob(walk);
                bool At() => w.Ragnar != null && Utils.DistanceXZ(w.Ragnar.transform.position, goal) < 2.8f && Mathf.Abs(w.Ragnar.transform.position.y - goal.y) < 1.3f; // 계단에서 상자 곁(1.7m 위)은 닿음 아님(회차 405)
                yield return TestKit.WaitFor(At, 90f);
                bool ok = At();
                float took = TestKit.Waited;
                walk.Halt = true;
                var rep = rec.Stop($"newerahouse {i + 1} {names[i]}", i > 0);
                jumps += rep.TeleSeen + rep.TeleUnseen;
                TestRunner.Log($"집 걸음({names[i]}): {rep}");
                checks.Add($"{i + 1}) {names[i]} 닿음 ({took:0.0}초, 남은 거리 {Utils.DistanceXZ(w.Ragnar.transform.position, goal):0.0}m·높이 차 {w.Ragnar.transform.position.y - goal.y:0.0}m, 발밑 {Under(w.Ragnar.transform.position)})", ok);
                if (ok) reached++;
                yield return new WaitForSeconds(1f);
            }
            // 라그나 걸음 기록의 순간이동만 셈 — 전역 셈은 사용자 월드에 원래 사는 일꾼(비욘 등)이 먼 깃발로 옮겨진 것까지 셈(회차 405)
            checks.Add($"집 안 순간이동 없음 (라그나 {jumps}, 다른 일꾼 포함 전체 {Teleports() - tele0}, 닿은 다리 {reached}/{names.Length})", jumps == 0);
        }

        /// <summary>발밑 조각 이름·높이(다리 끝에 어느 바닥에 섰는지 — 회차 404 상자 위 45.5에 선 것).</summary>
        static string Under(Vector3 p) => Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 2f, WorkerAI.SolidMask, QueryTriggerInteraction.Ignore)
            ? $"{(hit.collider.GetComponentInParent<Piece>() is Piece pc ? Utils.GetPrefabName(pc.gameObject) : hit.collider.name)} {hit.point.y:0.0}" : "없음";

        static int Teleports() => WorkerAI.StuckTeleports + WorkerAI.TrapRescues + WorkerAI.PitEdgeRescues;
    }
}
