using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 실제 집 안 상자·설비 시험(사용자 요청 10-31): 깃발 곁에 나무 벽·문 하나·평지붕 집(6m×6m)을 세우고 안에 상자·작업대.
    /// 문은 닫힌 채 "!활 1"(가죽 조각·사슴 가죽만 안 상자에) → 나무를 캐 와 안 상자에 넣고 안 작업대에서 활을 만드는지.
    /// diag: 벽에 박은 시간(집 충돌체 1m 안에서 0.5초에 0.15m도 못 감), 순간이동 수(막힘·rescue·옆 옮김 카운터 합), 문 열림 여부, 문 쪽 Pathfinding 층.
    /// 바닐라 Pathfinding: 타일은 m_layers 물리 충돌체로 굽고(층 이름은 프리팹 값이라 여기서 찍음), 길을 물을 때마다 찌르며
    /// 찔린 시각이 마지막 굽기보다 5초(m_updateInterval) 넘게 뒤면 한 번에 한 타일씩 다시 굽는다. BaseAI는 문을 열지 않는다.
    /// </summary>
    static class HouseTest
    {
        public static void Register() => TestRunner.Register("vh_housetest", "실제 집(벽·지붕·문) 안 상자·작업대", () => TestKit.Flat(Run()));

        const float Half = 3f;

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            var made = new List<GameObject>();
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.ClearBaseStock(w, "Wood", "LeatherScraps", "DeerHide", "Bow");
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static GameObject Put(List<GameObject> made, string prefab, Vector3 at, Quaternion rot, float? bottom = null)
        {
            GameObject p = ZNetScene.instance.GetPrefab(prefab);
            if (p == null) { Plugin.Log.LogWarning($"집 시험: 프리팹 {prefab} 없음"); return null; }
            var g = Object.Instantiate(p, at, rot);
            made.Add(g);
            if (bottom.HasValue)
            {
                Physics.SyncTransforms();
                Bounds b = Box(g);
                g.transform.position += Vector3.up * (bottom.Value - b.min.y);
                Physics.SyncTransforms(); // 안 하면 다음 Box(g)가 옮기기 전 자리(지붕이 문보다 낮게 섬)
            }
            return g;
        }

        static Bounds Box(GameObject g)
        {
            var cs = g.GetComponentsInChildren<Collider>();
            Bounds b = new Bounds(g.transform.position, Vector3.zero);
            bool any = false;
            foreach (var c in cs)
            {
                if (c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return b;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            Recipe bow = null;
            foreach (var rc in ObjectDB.instance.m_recipes)
                if (rc.m_item != null && Utils.GetPrefabName(rc.m_item.gameObject) == "Bow") bow = rc;
            checks.Add("활 레시피 있음", bow != null);
            if (bow == null) yield break;
            TestKit.ClearBaseStock(w, "Wood", "LeatherScraps", "DeerHide", "Bow");

            // 집: 깃발에서 오른쪽 5~11m, 문은 깃발 쪽 벽 가운데. 바깥 시험 상자는 치우고 안 상자를 w.Chest로
            Vector3 f = w.Player.transform.forward; f.y = 0f; f.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
            // 비탈이면 지붕이 낮은 쪽 문틀을 막는다(회차 311: 문 쪽 땅 2.5m 높아 지붕판이 무릎 높이) → 둘레 땅 높이 차가 가장 작은 자리
            Vector3 c = default; float spread = float.MaxValue, floorY = 0f; // 조각은 모두 같은 지형 높이(물체 무시)에 — 문 자리가 덤불 등 위에 서면 지붕과 틈이 막힘
            for (int a = 0; a < 360; a += 30)
                foreach (float dist in new[] { 8f, 10f, 12f, 14f })
                {
                    Vector3 cc = GatherTest.Ground(w.Home + Quaternion.AngleAxis(a, Vector3.up) * r * dist);
                    float lo = float.MaxValue, hi = float.MinValue; // 덤불 위에 설 수 있는 Ground가 아니라 지형 높이로
                    for (int i = -1; i <= 1; i++)
                        for (int j = -1; j <= 1; j++)
                        {
                            float y = ZoneSystem.instance.GetGroundHeight(cc + r * (Half * i) + f * (Half * j));
                            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                        }
                    if (hi - lo < spread) { spread = hi - lo; c = cc; floorY = ZoneSystem.instance.GetGroundHeight(cc - r * Half); } // 바닥 = 문 자리 땅(가장 낮은 땅이면 문 쪽 땅이 높을 때 천장이 일꾼 키 1.85m보다 낮아 머리가 걸림)
                }
            Plugin.Log.LogInfo($"집 시험: 집 자리 땅 높이 차 {spread:0.00}m, 바닥 {floorY:0.0}");
            ZNetScene.instance.Destroy(w.Chest.gameObject);
            // 집 둘레 나무·덤불은 치우고 짓는다(회차 312: 옆벽 곁 작은 너도밤나무와 벽 사이에 일꾼이 끼어 문까지 못 감)
            var cleared = new HashSet<ZNetView>();
            foreach (var col in Physics.OverlapSphere(c, Half * 1.42f + 2.5f))
            {
                ZNetView nv = col.GetComponentInParent<ZNetView>();
                if (nv == null || !nv.IsValid() || nv.GetComponent<Piece>() != null || nv.GetComponent<Character>() != null) continue;
                if ((nv.GetComponent<TreeBase>() != null || nv.GetComponent<Destructible>() != null || nv.GetComponent<Pickable>() != null) && cleared.Add(nv)) ZNetScene.instance.Destroy(nv.gameObject);
            }
            Plugin.Log.LogInfo($"집 시험: 집 둘레 나무·덤불 {cleared.Count}개 치움");
            float top = float.MinValue;
            int walls = 0;
            GameObject door = null;
            foreach (var side in new[] { r, -r, f, -f })
            {
                Vector3 along = Vector3.Cross(Vector3.up, side).normalized;
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 at = c + side * Half + along * (2f * i); at.y = floorY;
                    bool doorSlot = side == -r && i == 0;
                    var g = Put(made, doorSlot ? "wood_door" : "woodwall", at, Quaternion.LookRotation(side), at.y - 0.05f);
                    if (g == null) continue;
                    if (doorSlot) { door = g; top = Mathf.Max(top, Box(g).max.y); } else { walls++; top = Mathf.Max(top, Box(g).max.y); }
                }
            }
            int roofs = 0;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    if (Put(made, "wood_floor", c + r * (2f * i) + f * (2f * j), Quaternion.LookRotation(f), top - 0.05f) != null) roofs++;
            // wood_floor는 바닐라 지붕 판정(Cover 위 SphereCast)이 "leaky"로 지나침(회차 311 가림 0.5~0.9인데 지붕 False) → 천장 위에 wood_roof
            int tiles = 0;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    if (Put(made, "wood_roof", c + r * (2f * i) + f * (2f * j), Quaternion.LookRotation(f), top + 0.2f) != null) tiles++;
            Vector3 chestAt = GatherTest.Ground(c + r * 1.5f + f * 1.5f);
            Put(made, "piece_chest_wood", chestAt, Quaternion.LookRotation(-r));
            var bench = Put(made, "piece_workbench", GatherTest.Ground(c + r * 1.2f - f * 1.5f), Quaternion.LookRotation(-r));
            yield return new WaitForSeconds(2f);
            w.Chest = GatherTest.ChestAt(chestAt);
            ZDO dz = door != null ? door.GetComponent<ZNetView>().GetZDO() : null;
            Bounds db = door != null ? Box(door) : default;
            var pf = Pathfinding.instance;
            var layers = new List<string>();
            for (int i = 0; i < 32; i++) if ((pf.m_layers.value & (1 << i)) != 0) layers.Add(LayerMask.LayerToName(i));
            int pieceLayer = door != null ? door.layer : -1;
            Plugin.Log.LogInfo($"집 시험: Pathfinding m_layers [{string.Join(",", layers)}], 문 층 {LayerMask.LayerToName(pieceLayer)}, 문 크기 {db.size}, 벽 위 {top - c.y:0.00}m, 집 가운데 {c:F1}·문 {door?.transform.position:F1}");
            int alive = 0;
            foreach (var g in made) if (g != null) alive++;
            checks.Add($"집 세움 (벽 {walls}/11·문 {(door != null ? 1 : 0)}·천장 {roofs}/9·지붕 {tiles}/9·상자 {(w.Chest != null ? 1 : 0)}·작업대 {(bench != null ? 1 : 0)}, 남은 조각 {alive}/{made.Count})",
                walls == 11 && door != null && roofs == 9 && tiles == 9 && w.Chest != null && bench != null && alive == made.Count);
            if (w.Chest == null || door == null || bench == null) yield break;
            checks.Add($"문 닫힘으로 시작 (s_state {dz.GetInt(ZDOVars.s_state)})", dz.GetInt(ZDOVars.s_state) == 0);
            int wood = 0;
            foreach (var req in bow.m_resources)
            {
                string p = Utils.GetPrefabName(req.m_resItem.gameObject);
                if (p == "Wood") wood = req.m_amount; else TestKit.PutInChest(w.Chest, p, req.m_amount);
            }
            var cs = bench.GetComponent<CraftingStation>();
            float cover = 0f; bool under = false;
            if (cs != null && cs.m_roofCheckPoint != null) Cover.GetCoverForPoint(cs.m_roofCheckPoint.position, out cover, out under);
            checks.Add($"작업대가 지붕 아래라 쓸 수 있음 (바닐라 CheckUsable {(cs != null && cs.CheckUsable(w.Player, false) ? "됨" : "안 됨")}, 지붕 {under}·가림 {cover:0.00} ≥ 0.7)", cs != null && cs.CheckUsable(w.Player, false));

            // 지켜보기: 벽 박음·순간이동·문 열림·안에 들어감
            int tele0 = Teleports();
            float wallStuck = 0f, maxRun = 0f, run = 0f;
            bool opened = false, inside = false;
            int innerTrips = 0;
            var pieces = new List<Collider>();
            foreach (var g in made) if (g != null) foreach (var col in g.GetComponentsInChildren<Collider>()) if (!col.isTrigger) pieces.Add(col);
            bool In(Vector3 p) => Mathf.Abs(Vector3.Dot(p - c, r)) < Half - 0.3f && Mathf.Abs(Vector3.Dot(p - c, f)) < Half - 0.3f;
            bool done = false;
            IEnumerator Watch()
            {
                Vector3 last = w.Ragnar.transform.position;
                float tick = 0f;
                while (!done && w.Ragnar != null)
                {
                    yield return null; // 안 들어감은 매 틀(회차 311: 들어가자마자 활이 나와 0.5초 간격이 놓침), 벽 박음은 0.5초마다
                    tick += Time.deltaTime;
                    if (w.Ragnar == null) break;
                    Vector3 p = w.Ragnar.transform.position;
                    if (dz.IsValid() && dz.GetInt(ZDOVars.s_state) != 0) opened = true;
                    bool now = In(p);
                    if (now && !inside) innerTrips++;
                    inside = now;
                    if (tick < 0.5f) continue;
                    tick = 0f;
                    float near = float.MaxValue;
                    foreach (var col in pieces) if (col != null) near = Mathf.Min(near, Vector3.Distance(col.ClosestPoint(p + Vector3.up), p + Vector3.up));
                    if (w.Ragnar.Ai.Job != null && near < 1f && Utils.DistanceXZ(p, last) < 0.15f) { wallStuck += 0.5f; run += 0.5f; maxRun = Mathf.Max(maxRun, run); }
                    else run = 0f;
                    last = p;
                }
            }
            Plugin.Instance.StartCoroutine(Watch());
            string bowName = Localization.instance.Localize(bow.m_item.m_itemData.m_shared.m_name);
            GatherTest.Say($"!{w.Ragnar.NpcName} {bowName} 1", null);
            yield return new WaitForSeconds(2f);
            checks.Add($"활 계획 시작 ({w.Ragnar.Ai.Job?.GetType().Name} {w.Ragnar.Ai.Job?.StepName})", w.Ragnar.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => Stock(w, "Bow") >= 1 || (w.Ragnar.Ai.Job == null && Stock(w, "Wood") >= wood), 420f);
            float took = TestKit.Waited;
            done = true;
            int tele = Teleports() - tele0;
            Plugin.Log.LogInfo($"집 시험: {took:0}초, 벽 박음 {wallStuck:0.0}초(최장 연속 {maxRun:0.0}초), 순간이동 {tele}, 문 열림 {opened}, 안에 들어감 {innerTrips}번, 상자 나무 {Count(w.Chest, "Wood")}");
            checks.Add($"안 작업대에서 활 만듦 (활 {Stock(w, "Bow")}, {took:0}초, {w.Ragnar.Ai.Current})", Stock(w, "Bow") >= 1);
            checks.Add($"나무를 안 상자로 (안 상자 나무 {Count(w.Chest, "Wood")}·활 재료로 씀, 안에 들어감 {innerTrips}번)", innerTrips > 0);
            checks.Add($"벽에 박은 시간 짧음 ({wallStuck:0.0}초, 최장 연속 {maxRun:0.0}초 ≤ 5)", maxRun <= 5f);
            checks.Add($"순간이동 없음 ({tele}, 문 열림 {opened})", tele == 0);
            int closed0 = DoorWay.Closed;
            yield return TestKit.WaitFor(() => dz.GetInt(ZDOVars.s_state) == 0, 40f);
            checks.Add($"일 끝나고 나와서 문 닫음 (s_state {dz.GetInt(ZDOVars.s_state)}, 닫기 {DoorWay.Closed - closed0}번, {TestKit.Waited:0}초, 일꾼 안 {In(w.Ragnar.transform.position)}, {DoorWay.OpenedDiag()})", dz.GetInt(ZDOVars.s_state) == 0 && DoorWay.Closed > closed0);

            // 2) 집 안 제련소(③-2): 작업대 자리에 제련소, 광석·석탄은 안 상자. 일꾼은 밖에서 시작, 플레이어는 25m 밖(5m 안이면 보충 안 함)
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            if (ZNetScene.instance.GetPrefab("smelter") == null) { checks.Add("제련소 프리팹 있음", false); yield break; }
            ZNetScene.instance.Destroy(bench);
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
            TestKit.PutInChest(w.Chest, "CopperOre", 2);
            TestKit.PutInChest(w.Chest, "Coal", 10);
            var smelter = Put(made, "smelter", GatherTest.Ground(c + r * 1f - f * 1.2f), Quaternion.LookRotation(-r));
            w.Player.TeleportTo(GatherTest.Ground(w.Home - r * 25f) + Vector3.up * 0.3f, w.Player.transform.rotation, false);
            yield return new WaitForSeconds(2f);
            if (dz.IsValid() && dz.GetInt(ZDOVars.s_state) != 0) { dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, 0); } // 일꾼이 안 닫았을 때만(위 검사가 FAIL)
            yield return new WaitForSeconds(6f); // 닫힌 문 navmesh 벽이 다시 생길 때까지
            w.Ragnar.TeleportTo(GatherTest.Ground(c - r * (Half + 3f)) + Vector3.up * 0.3f, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(1f); // 옮긴 뒤 열린 문으로 다시 들어가던 것(회차 312 두 번째 실행: 집 가운데서 시작)
            checks.Add($"제련 단계: 문 닫힘·일꾼 밖에서 시작 (s_state {dz.GetInt(ZDOVars.s_state)}, 일꾼 집 가운데서 {Utils.DistanceXZ(w.Ragnar.transform.position, c):0.0}m, 안 {In(w.Ragnar.transform.position)})", dz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position));
            tele0 = Teleports(); int hops0 = MachineStep.Hops, side0 = MachineStep.SideTries, entered0 = DoorWay.Entered;
            GatherTest.Say($"!{w.Ragnar.NpcName} 구리주괴 1", null); // 게임 이름 "구리"는 광석으로 풀림(vh_crafttest와 같은 말)
            yield return TestKit.WaitFor(() => Stock(w, "Copper") >= 1, 300f);
            took = TestKit.Waited;
            tele = Teleports() - tele0;
            Plugin.Log.LogInfo($"집 시험 제련: {took:0}초, 문 들어감 {DoorWay.Entered - entered0}, 옆 돌기 {MachineStep.SideTries - side0}, 순간이동 {tele}(설비 {MachineStep.Hops - hops0})");
            checks.Add($"집 안 제련소에서 구리 주괴 (주괴 {Stock(w, "Copper")}, {took:0}초, {w.Ragnar.Ai.Current})", Stock(w, "Copper") >= 1);
            checks.Add($"제련소로 문 지나 들어감 ({DoorWay.Entered - entered0}번, 옆 돌기 {MachineStep.SideTries - side0})", DoorWay.Entered - entered0 > 0);
            checks.Add($"제련 중 순간이동 없음 ({tele})", tele == 0);
            closed0 = DoorWay.Closed;
            yield return TestKit.WaitFor(() => dz.GetInt(ZDOVars.s_state) == 0, 40f);
            checks.Add($"제련 끝나고 나와서 문 닫음 (s_state {dz.GetInt(ZDOVars.s_state)}, 닫기 {DoorWay.Closed - closed0}번, {TestKit.Waited:0}초, 일꾼 안 {In(w.Ragnar.transform.position)}, {DoorWay.OpenedDiag()})", dz.GetInt(ZDOVars.s_state) == 0 && DoorWay.Closed > closed0);
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
        }

        static int Teleports() => WorkerAI.StuckTeleports + WorkerAI.TrapRescues + WorkerAI.PitEdgeRescues
            + GatherJob.Hops + GatherJob.Rescues + GatherJob.InteriorHops + MachineStep.Hops;

        static int Count(Container c, string prefab) => c != null ? GatherTest.Count(c, prefab) : 0;

        static int Stock(TestKit.World w, string prefab)
        {
            int n = 0;
            foreach (var ch in Chests.NearBase(w.Home)) n += GatherTest.Count(ch, prefab);
            return n;
        }
    }
}
