using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_otherhouse(길 기억 ④ "다른 구조 집 두 판", decisions "시험 집" 11-15): vh_house_TheNewEra 안 메인 집과 구조가 다른 기지(VH_HOUSE_AT="x,z",
    /// 기본 단층 집 (-4867,-1157) — 트인 3층 작업장은 -3998,-5827)에서 살림(상자·침대·작업대·불) 중 가장 낮은 것 A·가장 높은 것 B·가로로 가장 먼 둘 C·D를
    /// A→B→C→D→A로 두 바퀴 걷는다. 1바퀴 = 길 기억 전, 2바퀴 = 기억한 길을 따름 → 바퀴마다 시간·진척 없음·충돌·길/직선·기억/따름 수를 diag에.
    /// 집 좌표에 맞춘 예외는 넣지 않는다(decisions "집 시험 원칙") — 살림은 자리로 고르고, 기준은 메인 집 시험과 같다.
    /// </summary>
    static class OtherHouseTest
    {
        public static void Register() => TestRunner.Register("vh_otherhouse", "다른 구조 집에서 두 바퀴 걷기(길 기억 전후)", () => TestKit.Flat(Run()));

        const string World = "vh_house_TheNewEra";
        static float Radius = 25f;   // 살림이 3곳 안 되면 40m(vh_housefind 무리 크기)까지 넓힘 — 탑처럼 살림이 적은 집

        static Vector3 At()
        {
            string s = System.Environment.GetEnvironmentVariable("VH_HOUSE_AT");
            if (string.IsNullOrEmpty(s)) return new Vector3(-4867f, 0f, -1157f);
            var a = s.Split(',');
            return new Vector3(float.Parse(a[0], CultureInfo.InvariantCulture), 0f, float.Parse(a[1], CultureInfo.InvariantCulture));
        }

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

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            Player pl = Player.m_localPlayer;
            Vector3 center = At();
            float gy = WorldGenerator.instance.GetHeight(center.x, center.z);
            pl.TeleportTo(new Vector3(center.x + 20f, gy + 1f, center.z), pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            // 조각 수가 2초 동안 안 늘 때까지(최대 30초) — 5초만 기다리면 탑이 277개 중 177개만 불러와짐(회차 700)
            int seen = -1;
            for (float t = 0f; t < 30f; t += 2f)
            {
                yield return new WaitForSeconds(2f);
                int n = 0;
                foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (Utils.DistanceXZ(p.transform.position, center) <= 40f) n++;
                if (n == seen && n > 0) break;
                seen = n;
            }
            int pieces = 0, doors = 0;
            var spots = new List<Component>();
            Radius = 25f;
            for (; ; Radius = 40f)
            {
                pieces = 0; doors = 0; spots.Clear();
                foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                {
                    if (Utils.DistanceXZ(p.transform.position, center) > Radius) continue;
                    pieces++;
                    if (p.GetComponent<Door>() != null) doors++;
                    Component c = (Component)p.GetComponent<Container>() ?? (Component)p.GetComponent<Bed>() ?? (Component)p.GetComponent<CraftingStation>() ?? p.GetComponent<Fireplace>();
                    if (c != null) spots.Add(c);
                }
                if (spots.Count >= 3 || Radius >= 40f) break;
            }
            // 찾은 차례는 판마다 달라서 자리로 줄 세움
            spots.Sort((a, b) => { var x = a.transform.position; var y = b.transform.position; return x.x != y.x ? x.x.CompareTo(y.x) : x.z.CompareTo(y.z); });
            TestRunner.Log($"집 ({center.x:0},{center.z:0}) 조각 {pieces}개({Radius:0}m 안): 살림 {spots.Count}·문 {doors}");
            checks.Add($"집이 불러와짐 (조각 {pieces}, 살림 {spots.Count})", pieces > 100 && spots.Count >= 3);
            if (spots.Count < 3) yield break;
            Component lo = spots[0];
            foreach (var s in spots) if (s.transform.position.y < lo.transform.position.y) lo = s;
            string Name(Component c) => $"{Utils.GetPrefabName(c.gameObject)} {c.transform.position:F1}";

            Vector3? open = HouseBase.OpenGround(center, out string whyNot);
            checks.Add($"집 밖 깃발 자리 ({open?.ToString("F1") ?? "없음 — " + whyNot})", open.HasValue);
            if (!open.HasValue) yield break;
            Vector3 out_ = open.Value - center; out_.y = 0f;
            pl.TeleportTo(open.Value + Vector3.up * 0.3f, Quaternion.LookRotation(out_.normalized), false);
            yield return new WaitForSeconds(2f);
            yield return TestKit.Setup(w, pl.transform.position);
            checks.Add("깃발·NPC 2명", w.Ragnar != null && w.Floki != null);
            if (w.Ragnar == null || w.Floki == null) yield break;

            var cols = new List<Collider>();
            foreach (var col in Physics.OverlapSphere(new Vector3(center.x, lo.transform.position.y, center.z), Radius + 4f))
                if (!col.isTrigger && col.GetComponentInParent<Piece>() != null && !(col is MeshCollider mc && !mc.convex)) cols.Add(col);

            Vector3 Near(Component c) { Vector3 p = c.transform.position, to = center - p; to.y = 0f; return p + to.normalized * 1.2f; }
            w.Ragnar.TeleportTo(Near(lo) + Vector3.up * 0.5f, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(2f);
            WorkerAI ai = w.Ragnar.Ai;
            // 걸을 곳은 라그나 자리에서 끝까지 길이 나는 살림만(바닥에서 4.6m 위 침대처럼 점프로만 닿는 곳은 빼고 diag에 — 회차 698 단층 집)
            var path = new List<Vector3>();
            var reach = new List<Component>();
            // 회차 700: 같은 집에서 6/10 → 1/10(전부 길 안 남)이 번갈아 나옴 — 옮긴 직후(2초)엔 일꾼이 아직 땅에 안 닿았거나 길 그물이 안 만들어짐. 3번까지 다시 잼
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                reach.Clear();
                foreach (var s in spots)
                    if (s == lo || Pathfinding.instance.GetPath(w.Ragnar.transform.position, s.transform.position, path, Pathfinding.AgentType.Humanoid, requireFullPath: true)) reach.Add(s);
                    else if (attempt == 3) TestRunner.Log($"길 안 남(뺌): {Name(s)}");
                TestRunner.Log($"길 재기 {attempt}번째: {reach.Count}/{spots.Count} (라그나 {w.Ragnar.transform.position:F1}, 땅 {w.Ragnar.IsOnGround()})");
                if (reach.Count >= 3) break;
                yield return new WaitForSeconds(3f);
            }
            // 살림이 모자란 집(탑: 상자 하나뿐, 회차 700)은 층마다(2m 띠) 가운데에서 가장 먼 바닥 조각 하나씩을 걸을 곳에 더함
            if (reach.Count < 3)
            {
                var bands = new SortedDictionary<int, Piece>();
                foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                {
                    if (Utils.DistanceXZ(p.transform.position, center) > 25f || Utils.GetPrefabName(p.gameObject).IndexOf("floor", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int band = Mathf.FloorToInt(p.transform.position.y / 2f);
                    if (!bands.TryGetValue(band, out var old) || Utils.DistanceXZ(p.transform.position, center) > Utils.DistanceXZ(old.transform.position, center)) bands[band] = p;
                }
                foreach (var p in bands.Values)
                    if (Pathfinding.instance.GetPath(w.Ragnar.transform.position, p.transform.position, path, Pathfinding.AgentType.Humanoid, requireFullPath: true)) reach.Add(p);
                    else TestRunner.Log($"길 안 남(뺌): {Name(p)}");
                TestRunner.Log($"살림 모자람 → 층별 바닥 {bands.Count}곳 가운데 길 나는 것 더함");
            }
            checks.Add($"길 나는 살림 3곳 넘음 ({reach.Count}/{spots.Count})", reach.Count >= 3);
            if (reach.Count < 3) yield break;
            Component hi = lo, c1 = reach[0], c2 = reach[1];
            float far = 0f;
            foreach (var s in reach) if (s.transform.position.y > hi.transform.position.y) hi = s;
            bool Ends(Component c) => reach.Count >= 4 && (c == hi || c == lo);   // 회차 771: B와 C가 같은 상자면 B→C가 0초 다리(지은 집)
            foreach (var s in reach)
            {
                if (Ends(s)) continue;
                foreach (var t in reach)
                {
                    if (Ends(t)) continue;
                    float d = Utils.DistanceXZ(s.transform.position, t.transform.position);
                    if (d > far) { far = d; c1 = s; c2 = t; }
                }
            }
            TestRunner.Log($"걸을 곳: A 낮음 {Name(lo)} · B 높음 {Name(hi)} · C {Name(c1)} · D {Name(c2)} (C·D 사이 {far:0.0}m)");
            int tele0 = Teleports();
            string[] names = { "A→B", "B→C", "C→D", "D→A" };
            Vector3[] tos = { hi.transform.position, c1.transform.position, c2.transform.position, lo.transform.position };   // 자리로 들고 감 — 회차 771: 걸음 중 살림 Component가 사라져 NRE
            var took = new float[2];
            int jumps = 0;
            for (int round = 0; round < 2; round++)
            {
                int reached = 0, noProg = 0, bumps = 0, rem0 = ai.PathsRemembered, fol0 = ai.PathsFollowed, drop0 = ai.PathsDropped, spoil0 = ai.PathsSpoiled;
                float worst = 0f;
                for (int i = 0; i < names.Length; i++)
                {
                    Vector3 goal = tos[i];
                    ClearWild(center); // 회색 난쟁이가 오면 도망·순간이동이 걸음에 섞임(회차 698 첫 판) — vh_newerahouse처럼 다리마다 치움
                    var walk = new ExploreTest.WalkJob(ai, goal, true) { Stay = true };
                    var rec = WalkRecorder.Start(w.Ragnar, cols);
                    ai.StartJob(walk);
                    bool Here() => w.Ragnar != null && Utils.DistanceXZ(w.Ragnar.transform.position, goal) < 2.8f && Mathf.Abs(w.Ragnar.transform.position.y - goal.y) < 1.3f;
                    float legT = Time.time;
                    yield return TestKit.WaitFor(Here, 90f);
                    bool ok = Here();
                    took[round] += Time.time - legT; // Waited는 먼 호위병이 싸우는 동안을 빼서 0.0초가 됨(회차 699)
                    walk.Halt = true;
                    var rep = rec.Stop($"otherhouse {round + 1}-{i + 1} {names[i]}", round > 0 || i > 0);
                    jumps += rep.TeleSeen + rep.TeleUnseen;
                    noProg += rep.NoProgress; bumps += rep.Bumps; worst = Mathf.Max(worst, rep.WorstRatio);
                    TestRunner.Log($"집 걸음({round + 1}바퀴 {names[i]}, {TestKit.Waited:0.0}초): {rep}");
                    if (ok) reached++;
                    else TestRunner.Log($"못 닿음 {round + 1}바퀴 {names[i]}: 남은 거리 {Utils.DistanceXZ(w.Ragnar.transform.position, goal):0.0}m·높이 차 {w.Ragnar.transform.position.y - goal.y:0.0}m");
                    yield return new WaitForSeconds(1f);
                }
                TestRunner.Log($"{round + 1}바퀴: {took[round]:0.0}초·진척 없음 {noProg}·충돌 {bumps}·길/직선 최악 {worst:0.00} · 길 기억 {ai.PathsRemembered - rem0}·따름 {ai.PathsFollowed - fol0}·지움 {ai.PathsDropped - drop0}·안 함 {ai.PathsSpoiled - spoil0}");
                checks.Add($"{round + 1}바퀴 다 닿음 ({reached}/{names.Length}, {took[round]:0.0}초)", reached == names.Length);
            }
            checks.Add($"2바퀴(기억 뒤)가 1바퀴보다 안 느림 ({took[0]:0.0} → {took[1]:0.0}초)", took[1] <= took[0] * 1.25f + 3f);
            checks.Add($"집 안 순간이동 없음 (라그나 {jumps}, 전체 {Teleports() - tele0})", jumps == 0);
            int piecesNow = 0;
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (Utils.DistanceXZ(p.transform.position, center) <= Radius) piecesNow++;
            checks.Add($"걷는 동안 집이 안 무너짐 (조각 {pieces} → {piecesNow})", piecesNow >= pieces - 2);   // 회차 771: 띄워 지은 집이 받침 없이 무너져 벽을 뚫고 걸은 듯 보임

            // E108: 바깥문마다 안쪽 2m·옆 0.6m에서 바깥 4m로 나가기 — 문 닫힘·열림 1·열림 -1 셋(2바퀴 출발이 벽 안이면 wood_gate에서 8초 포기)
            var doorList = new List<Door>();
            foreach (var d in Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
                if (d != null && d.m_keyItem == null && Utils.DistanceXZ(d.transform.position, center) < Radius && !doorList.Contains(d)) doorList.Add(d);
            int tries = 0, outs = 0, gave0 = DoorWay.GaveUps; float midOff = 0f; Vector3 shutMid = Vector3.zero;
            int mask = LayerMask.GetMask("piece");
            bool Roof(Vector3 p) => Physics.Raycast(p + Vector3.up * 1.8f, Vector3.up, 20f, mask);
            foreach (var d in doorList)
            {
                if (tries >= 9) break;
                Vector3 mid = DoorWay.Middle(d), n = d.transform.forward; n.y = 0f; n.Normalize();
                if (Roof(mid - n * 2f) && !Roof(mid + n * 2f)) n = -n;
                if (!Roof(mid + n * 2f) || Roof(mid - n * 4f)) { TestRunner.Log($"문 나가기 뺌(바깥문 아님): {d.name} {mid:F1}"); continue; }
                Vector3 side = Vector3.Cross(Vector3.up, n), inside = Vector3.zero, goal = mid - n * 4f;
                bool found = false;
                foreach (float s in new[] { 0.6f, -0.6f, 0f })
                {
                    inside = mid + n * 2f + side * s;
                    if (!Physics.CheckCapsule(inside + Vector3.up * 0.5f, inside + Vector3.up * 1.4f, 0.35f, mask, QueryTriggerInteraction.Ignore)) { found = true; break; }
                }
                if (!found) { TestRunner.Log($"문 나가기 뺌(안쪽 자리 없음): {d.name} {mid:F1}"); continue; }
                var dz = d.GetComponent<ZNetView>().GetZDO();
                foreach (int st in new[] { 0, 1, -1 })
                {
                    DoorWay.Forget();
                    dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, st);
                    w.Ragnar.TeleportTo(inside + Vector3.up * 0.3f, Quaternion.LookRotation(-n), false);
                    yield return new WaitForSeconds(2f);
                    {
                        Animator an = d.GetComponentInChildren<Animator>();
                        var info = an != null ? an.GetCurrentAnimatorStateInfo(0) : default;
                        var sb = new System.Text.StringBuilder();
                        foreach (var c in d.GetComponentsInChildren<Collider>())
                            if (!c.isTrigger) sb.Append($" {c.name}{(an != null && c.transform.IsChildOf(an.transform) ? "*" : "")}({c.bounds.center.x:0.0},{c.bounds.center.z:0.0} {c.bounds.size.x:0.0}x{c.bounds.size.z:0.0})");
                        Vector3 now = DoorWay.Middle(d);
                        if (st == 0) shutMid = now; else midOff = Mathf.Max(midOff, Utils.DistanceXZ(now, shutMid));
                        TestRunner.Log($"문 자리 {d.name} s_state {st}: 문 {d.transform.position:F1}·앞 ({n.x:0.00},{n.z:0.00})·애니 {(an == null ? "없음" : an.transform == d.transform ? "뿌리" : an.name)} 닫힘 {an != null && info.IsTag("closed")}·열림 {an != null && info.IsTag("open")} · Middle {now:F1}·지금 충돌체 {DoorWay.Bounds(d):F1} · 일꾼 {w.Ragnar.transform.position:F1} ·{sb}");
                    }
                    int ex0 = DoorWay.Exited;
                    var walk = new ExploreTest.WalkJob(ai, goal, true) { Stay = true };
                    ai.StartJob(walk);
                    bool Out() => w.Ragnar != null && Utils.DistanceXZ(w.Ragnar.transform.position, goal) < 2f;
                    yield return TestKit.WaitFor(Out, 15f);
                    walk.Halt = true;
                    tries++;
                    if (Out()) outs++;
                    TestRunner.Log($"문 나가기 {d.name} {mid:F1} 시작 s_state {st}: {(Out() ? "나감" : "못 나감")} {TestKit.Waited:0.0}초·문으로 나감 {DoorWay.Exited - ex0}·남은 {Utils.DistanceXZ(w.Ragnar.transform.position, goal):0.0}m");
                    yield return new WaitForSeconds(1f);
                }
            }
            DoorWay.Forget();
            if (doorList.Count == 0) TestRunner.Log("문 없는 집(트인 작업장) — 문 나가기 칸 건너뜀");
            else checks.Add($"바깥문 안쪽에서 15초 안에 나감 ({outs}/{tries})", tries > 0 && outs == tries);
            if (doorList.Count > 0) checks.Add($"열린 문에서 잰 문틀 가운데가 닫힌 문 때와 같음 (최대 {midOff:0.00}m, E108)", tries > 0 && midOff < 0.3f);
            checks.Add($"문 나가기 중 문 길 포기 없음 ({DoorWay.GaveUps - gave0}번, E108)", DoorWay.GaveUps == gave0);
        }

        static void ClearWild(Vector3 center)
        {
            foreach (var c in new List<Character>(Character.GetAllCharacters()))
                if (c != null && !c.IsDead() && !(c is Player) && !(c is WorkerNpc) && !c.IsTamed() && Utils.DistanceXZ(c.transform.position, center) < 60f)
                {
                    TestRunner.Log($"야생 생물 치움: {c.m_name} {Utils.DistanceXZ(c.transform.position, center):0}m");
                    ZNetScene.instance.Destroy(c.gameObject);
                }
        }

        static int Teleports() => WorkerAI.StuckTeleports + WorkerAI.TrapRescues + WorkerAI.PitEdgeRescues;
    }
}
