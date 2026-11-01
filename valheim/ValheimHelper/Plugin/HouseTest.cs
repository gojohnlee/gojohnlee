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

        /// <summary>회차 348: 비탈이면 문간 안쪽 땅이 바닥보다 솟아 머리가 문틀에 걸림(안쪽으로 Terrain 0.0m) → 플레이어가 땅 고르듯 집 자리(벽 밖 1.5m까지)를 바닥 높이로.</summary>
        static string s_levelWhy = "";
        static int LevelGround(Vector3 c, Vector3 r, Vector3 f, float y, float half = Half + 1.5f)
        {
            // 바닐라 RPC_ApplyOperation → TerrainComp.DoOperation(비공개)에 고르기 설정을 바로 줌(괭이 조각표엔 m_level 켠 것이 없음 — mud_road_v2·raise_v2·path_v2·paved_road_v2 모두 False, 회차 348 시험)
            var settings = new TerrainOp.Settings { m_level = true, m_levelRadius = half * 1.42f, m_square = true, m_raise = false, m_smooth = false, m_paintCleared = false };
            var doOp = HarmonyLib.AccessTools.Method(typeof(TerrainComp), "DoOperation");
            var maps = new List<Heightmap>();
            Heightmap.FindHeightmap(c, settings.m_levelRadius, maps);
            int n = 0;
            foreach (var hm in maps)
            {
                TerrainComp tc = hm.GetAndCreateTerrainCompiler();
                if (tc == null || doOp == null) continue;
                doOp.Invoke(tc, new object[] { new Vector3(c.x, y, c.z), Vector3.zero, settings });
                n++;
            }
            s_levelWhy = doOp == null ? "DoOperation 없음" : $"지형 {maps.Count}";
            return n;
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
            for (int a = 0; a < 360; a += 15)
                foreach (float dist in new[] { 8f, 10f, 12f, 14f, 16f, 18f, 20f })
                {
                    Vector3 cc = GatherTest.Ground(w.Home + Quaternion.AngleAxis(a, Vector3.up) * r * dist);
                    float lo = float.MaxValue, hi = float.MinValue; // 덤불 위에 설 수 있는 Ground가 아니라 지형 높이로
                    for (int i = -1; i <= 1; i++)
                        for (int j = -1; j <= 1; j++)
                        {
                            float y = ZoneSystem.instance.GetGroundHeight(cc + r * (Half * i) + f * (Half * j));
                            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                        }
                    float doorY = ZoneSystem.instance.GetGroundHeight(cc - r * Half), score = hi - lo + 2f * Mathf.Max(0f, hi - doorY); // 회차 348: 안 땅이 문 자리 바닥보다 0.5m 높으면 문간에서 머리가 문틀에 걸림 → 벌점
                    if (score < spread) { spread = score; c = cc; floorY = doorY; } // 바닥 = 문 자리 땅(가장 낮은 땅이면 문 쪽 땅이 높을 때 천장이 일꾼 키 1.85m보다 낮아 머리가 걸림)
                }
            Plugin.Log.LogInfo($"집 시험: 집 자리 점수(땅 높이 차 + 안이 문보다 높은 만큼×2) {spread:0.00}, 바닥 {floorY:0.0}");
            Plugin.Log.LogInfo($"집 시험: 집 자리 땅 고르기 {LevelGround(c, r, f, floorY)}번(바닐라 지형 고르기, 바닥 높이로, {s_levelWhy})");
            yield return new WaitForSeconds(1f);
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
            int innerTrips = 0, farSkips = 0, skipsSeen = GatherJob.NearSkips;
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
                    // 집에서 먼 근처 막힘 건너뛰기(나무 베다 굴러온 통나무 등, E057)는 집 벽 탓이 아니라 빼고 셈
                    if (GatherJob.NearSkips != skipsSeen)
                    {
                        skipsSeen = GatherJob.NearSkips;
                        if (Utils.DistanceXZ(GatherJob.LastNearSkipFrom, c) > Half + 4f) farSkips++;
                    }
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
            int tele = Teleports() - tele0 - farSkips;
            Plugin.Log.LogInfo($"집 시험: {took:0}초, 벽 박음 {wallStuck:0.0}초(최장 연속 {maxRun:0.0}초), 순간이동 {tele}(집에서 먼 건너뛰기 {farSkips} 뺌), 문 열림 {opened}, 안에 들어감 {innerTrips}번, 상자 나무 {Count(w.Chest, "Wood")}");
            checks.Add($"안 작업대에서 활 만듦 (활 {Stock(w, "Bow")}, {took:0}초, {w.Ragnar.Ai.Current})", Stock(w, "Bow") >= 1);
            checks.Add($"나무를 안 상자로 (안 상자 나무 {Count(w.Chest, "Wood")}·활 재료로 씀, 안에 들어감 {innerTrips}번)", innerTrips > 0);
            checks.Add($"벽에 박은 시간 짧음 ({wallStuck:0.0}초, 최장 연속 {maxRun:0.0}초 ≤ 5)", maxRun <= 5f);
            checks.Add($"순간이동 없음 ({tele}, 문 열림 {opened})", tele == 0);
            int closed0 = DoorWay.Closed;
            yield return TestKit.WaitFor(() => DoorWay.Closed > closed0 && dz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position), 40f); // 나갈 때 문짝 바꿔 열기로 잠깐 닫힘(E064)
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
            yield return TestKit.WaitFor(() => DoorWay.Closed > closed0 && dz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position), 40f); // 나갈 때 문짝 바꿔 열기로 잠깐 닫힘(E064)
            checks.Add($"제련 끝나고 나와서 문 닫음 (s_state {dz.GetInt(ZDOVars.s_state)}, 닫기 {DoorWay.Closed - closed0}번, {TestKit.Waited:0}초, 일꾼 안 {In(w.Ragnar.transform.position)}, {DoorWay.OpenedDiag()})", dz.GetInt(ZDOVars.s_state) == 0 && DoorWay.Closed > closed0);
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");

            // 3) 집 안 상자에서 꺼내기(③-4, Prep): 체력 미드는 안 상자에만, 일꾼 짐엔 없음. 일 시키면 나가기 전 준비로 문 지나 들어가 챙기는지
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.ClearBaseStock(w, "MeadHealthMinor");
            foreach (var it in new List<ItemDrop.ItemData>(w.Ragnar.GetInventory().GetAllItems()))
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "MeadHealthMinor") w.Ragnar.GetInventory().RemoveItem(it);
            TestKit.PutInChest(w.Chest, "MeadHealthMinor", 3);
            if (dz.IsValid() && dz.GetInt(ZDOVars.s_state) != 0) { dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, 0); }
            yield return new WaitForSeconds(6f);
            w.Ragnar.TeleportTo(GatherTest.Ground(c - r * (Half + 3f)) + Vector3.up * 0.3f, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(1f);
            checks.Add($"꺼내기 단계: 문 닫힘·일꾼 밖·미드 없음 (s_state {dz.GetInt(ZDOVars.s_state)}, 안 {In(w.Ragnar.transform.position)}, 미드 {w.Ragnar.CountItem("MeadHealthMinor")})",
                dz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position) && w.Ragnar.CountItem("MeadHealthMinor") == 0);
            tele0 = Teleports(); entered0 = DoorWay.Entered; int doors0 = ChestHop.Doors;
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() => w.Ragnar.CountItem("MeadHealthMinor") >= 3, 90f);
            took = TestKit.Waited;
            tele = Teleports() - tele0;
            checks.Add($"안 상자에서 체력 미드 챙김 (들고 {w.Ragnar.CountItem("MeadHealthMinor")}, 상자 {Count(w.Chest, "MeadHealthMinor")}, {took:0}초, {w.Ragnar.Ai.Job?.StepName})", w.Ragnar.CountItem("MeadHealthMinor") >= 3);
            // 문 앞에서 막혀 상자 가기가 문을 열고 문간에서 손 거리로 꺼내도 됨(회차 345 — 문 지나기 없이 5초)
            checks.Add($"꺼내러 문 지나 들어감 ({DoorWay.Entered - entered0}번, 상자 가기 문 열기 {ChestHop.Doors - doors0}번, 순간이동 {tele})", DoorWay.Entered - entered0 + ChestHop.Doors - doors0 > 0 && tele == 0);
            closed0 = DoorWay.Closed;
            yield return TestKit.WaitFor(() => DoorWay.Closed > closed0 && dz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position), 40f); // 나갈 때 문짝 바꿔 열기로 잠깐 닫힘(E064)
            checks.Add($"꺼내고 나와서 문 닫음 (s_state {dz.GetInt(ZDOVars.s_state)}, 닫기 {DoorWay.Closed - closed0}번, {TestKit.Waited:0}초, {DoorWay.OpenedDiag()})", dz.GetInt(ZDOVars.s_state) == 0 && DoorWay.Closed > closed0);
            GatherTest.Say("!멈춰", null);
            TestKit.ClearBaseStock(w, "MeadHealthMinor");

            // 3-2) 플레이어가 열어 둔 문(밖에서 엶 — 문짝이 안쪽): 그대로 지나 들어가 꺼내고, 나올 때 문짝 바꿔 열기로 닫았으면 원래 쪽으로 다시 열어 둠(남이 연 문)
            yield return new WaitForSeconds(1f);
            foreach (var it in new List<ItemDrop.ItemData>(w.Ragnar.GetInventory().GetAllItems()))
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "MeadHealthMinor") w.Ragnar.GetInventory().RemoveItem(it);
            TestKit.PutInChest(w.Chest, "MeadHealthMinor", 3);
            Vector3 outAt = GatherTest.Ground(c - r * (Half + 3f)) + Vector3.up * 0.3f;
            int openSt = Vector3.Dot(door.transform.forward, door.transform.position - c) < 0f ? 1 : -1; // 문 바깥에서 Dungeons.Open이 열 때와 같은 쪽(문짝이 안쪽)
            if (dz.IsValid()) { dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, openSt); }
            yield return new WaitForSeconds(6f); // 열린 문 navmesh
            w.Ragnar.TeleportTo(outAt, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(1f);
            tele0 = Teleports(); entered0 = DoorWay.Entered; doors0 = ChestHop.Doors; closed0 = DoorWay.Closed; int opened0 = DoorWay.Opened;
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() => w.Ragnar.CountItem("MeadHealthMinor") >= 3, 90f);
            took = TestKit.Waited;
            yield return TestKit.WaitFor(() => !In(w.Ragnar.transform.position), 30f);
            yield return new WaitForSeconds(4f); // 나오며 문짝 바꿔 열기(E064)로 닫았으면 CloseBehind가 원래대로 다시 엶
            yield return TestKit.WaitFor(() => dz.GetInt(ZDOVars.s_state) == openSt, 10f);
            tele = Teleports() - tele0;
            checks.Add($"열어 둔 문으로 들어가 미드 챙김 (들고 {w.Ragnar.CountItem("MeadHealthMinor")}, {took:0}초, 순간이동 {tele}, 일꾼 안 {In(w.Ragnar.transform.position)})",
                w.Ragnar.CountItem("MeadHealthMinor") >= 3 && tele == 0 && !In(w.Ragnar.transform.position));
            checks.Add($"열어 둔 문은 열린 채 둠 (s_state {dz.GetInt(ZDOVars.s_state)}/{openSt}, 엶 {DoorWay.Opened - opened0}·닫기 {DoorWay.Closed - closed0}번, {TestKit.Waited:0}초)",
                dz.GetInt(ZDOVars.s_state) == openSt);
            GatherTest.Say("!멈춰", null);
            TestKit.ClearBaseStock(w, "MeadHealthMinor");

            // 3-3) 플레이어가 열린 문간에 서 있음(8초 뒤 비킴): 순간이동·포기 없이 기다렸다 지나 들어가 꺼내고, 플레이어 곁에서 문을 닫지 않음
            yield return new WaitForSeconds(1f);
            foreach (var it in new List<ItemDrop.ItemData>(w.Ragnar.GetInventory().GetAllItems()))
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "MeadHealthMinor") w.Ragnar.GetInventory().RemoveItem(it);
            TestKit.PutInChest(w.Chest, "MeadHealthMinor", 3);
            if (dz.IsValid()) { dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, openSt); }
            Vector3 park = w.Player.transform.position;
            Vector3 gap = door.transform.position; gap.y = floorY + 0.1f; // 문틀 가운데(TeleportTo는 문틀 위에 올림 → 몸을 직접)
            var prb = w.Player.GetComponent<Rigidbody>();
            w.Player.transform.position = gap; prb.position = gap; prb.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(6f);
            var rrb = w.Ragnar.GetComponent<Rigidbody>(); // TeleportTo로는 집 안에 남은 적 있음(회차 351) → 몸을 직접
            w.Ragnar.transform.position = outAt; rrb.position = outAt; rrb.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(1f);
            checks.Add($"문간 단계: 문 열림·플레이어 문간·일꾼 밖 (s_state {dz.GetInt(ZDOVars.s_state)}, 플레이어 문에서 {Utils.DistanceXZ(w.Player.transform.position, gap):0.0}m, 일꾼 안 {In(w.Ragnar.transform.position)})",
                dz.GetInt(ZDOVars.s_state) == openSt && !In(w.Ragnar.transform.position));
            tele0 = Teleports(); entered0 = DoorWay.Entered; closed0 = DoorWay.Closed;
            bool shutOnPlayer = false;
            float blockEnd = Time.time + 8f;
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() =>
            {
                if (Time.time < blockEnd)
                {
                    if (Utils.DistanceXZ(w.Player.transform.position, gap) > 0.4f) { w.Player.transform.position = gap; prb.position = gap; prb.linearVelocity = Vector3.zero; } // 일꾼이 밀어도 그 자리
                    if (dz.GetInt(ZDOVars.s_state) == 0) shutOnPlayer = true;
                }
                else if (Utils.DistanceXZ(w.Player.transform.position, park) > 1f) w.Player.TeleportTo(park, w.Player.transform.rotation, false);
                return w.Ragnar.CountItem("MeadHealthMinor") >= 3;
            }, 90f);
            took = TestKit.Waited;
            if (Utils.DistanceXZ(w.Player.transform.position, park) > 1f) w.Player.TeleportTo(park, w.Player.transform.rotation, false);
            yield return TestKit.WaitFor(() => !In(w.Ragnar.transform.position), 30f);
            yield return new WaitForSeconds(4f); // 나오며 문짝 바꿔 열기로 닫았으면 CloseBehind가 다시 엶 — 끝난 뒤에 닫아야 다음 단계 문이 다시 열리지 않음
            yield return TestKit.WaitFor(() => dz.GetInt(ZDOVars.s_state) == openSt, 10f);
            tele = Teleports() - tele0;
            checks.Add($"문간의 플레이어가 비킨 뒤 들어가 미드 챙김 (들고 {w.Ragnar.CountItem("MeadHealthMinor")}, {took:0}초, 순간이동 {tele}, 들어감 {DoorWay.Entered - entered0}번, 일꾼 안 {In(w.Ragnar.transform.position)}, {w.Ragnar.Ai.Job?.StepName})",
                w.Ragnar.CountItem("MeadHealthMinor") >= 3 && tele == 0 && !In(w.Ragnar.transform.position));
            checks.Add($"문간에 플레이어 있는 동안 문 안 닫음 (닫힘 {shutOnPlayer})", !shutOnPlayer);
            GatherTest.Say("!멈춰", null);
            TestKit.ClearBaseStock(w, "MeadHealthMinor");
            if (dz.IsValid()) { dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, 0); }

            // 4) 안에서 닫힌 문으로 나가기(③-5): 일꾼을 안에 두고 문을 닫은 채 바깥 일(나무)을 시키면 문을 열고 나가는지
            yield return new WaitForSeconds(1f);
            if (dz.IsValid() && dz.GetInt(ZDOVars.s_state) != 0) { dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, 0); }
            yield return new WaitForSeconds(6f); // 닫힌 문 navmesh 벽이 다시 생길 때까지(일꾼은 밖 — 안에 두고 기다리면 집으로 가며 먼저 나가 버림)
            tele0 = Teleports(); int exited0 = DoorWay.Exited; closed0 = DoorWay.Closed;
            Vector3 inAt = GatherTest.Ground(c + f * 1.2f) + Vector3.up * 0.3f; // TeleportTo는 가장 위 면(지붕) 위에 올림 → 몸을 직접
            w.Ragnar.transform.position = inAt; w.Ragnar.GetComponent<Rigidbody>().position = inAt; w.Ragnar.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            checks.Add($"나가기 단계: 문 닫힘·일꾼 안 (s_state {dz.GetInt(ZDOVars.s_state)}, 안 {In(w.Ragnar.transform.position)})", dz.GetInt(ZDOVars.s_state) == 0 && In(w.Ragnar.transform.position));
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() => DoorWay.Exited > exited0, 40f); // 안 판정(벽에서 0.3m 안)은 문틀을 지나는 중에 끝남
            took = TestKit.Waited;
            tele = Teleports() - tele0;
            checks.Add($"안에서 문 열고 나감 ({took:0}초, 나감 {DoorWay.Exited - exited0}번, 순간이동 {tele}, {w.Ragnar.Ai.Job?.StepName})", !In(w.Ragnar.transform.position) && DoorWay.Exited > exited0 && tele == 0);
            // closed0은 단계 시작에서 — 나올 때 문짝이 내 쪽이면 바로 닫음(회차 348)
            yield return TestKit.WaitFor(() => DoorWay.Closed > closed0 && dz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position), 40f); // 나갈 때 문짝 바꿔 열기로 잠깐 닫힘(E064)
            checks.Add($"나가서 문 닫음 (s_state {dz.GetInt(ZDOVars.s_state)}, 닫기 {DoorWay.Closed - closed0}번, {TestKit.Waited:0}초)", dz.GetInt(ZDOVars.s_state) == 0 && DoorWay.Closed > closed0);
            GatherTest.Say("!멈춰", null);

            // 4-2) 문 두 개(④): 상자 쪽 벽 가운데를 뒷문으로. 일꾼을 집 뒤에 두면 앞문으로 돌지 않고 뒷문으로 들어가 미드를 챙기고 뒷문을 닫는지
            yield return new WaitForSeconds(1f);
            foreach (var g in made)
                if (g != null && Utils.GetPrefabName(g) == "woodwall" && Utils.DistanceXZ(g.transform.position, c + r * Half) < 0.5f) { ZNetScene.instance.Destroy(g); break; }
            Vector3 backAt = c + r * Half; backAt.y = floorY;
            var back = Put(made, "wood_door", backAt, Quaternion.LookRotation(r), floorY - 0.05f);
            ZDO bz = back != null ? back.GetComponent<ZNetView>().GetZDO() : null;
            if (bz == null) { checks.Add("뒷문 세움", false); yield break; }
            TestKit.ClearBaseStock(w, "MeadHealthMinor");
            foreach (var it in new List<ItemDrop.ItemData>(w.Ragnar.GetInventory().GetAllItems()))
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "MeadHealthMinor") w.Ragnar.GetInventory().RemoveItem(it);
            TestKit.PutInChest(w.Chest, "MeadHealthMinor", 3);
            if (dz.IsValid() && dz.GetInt(ZDOVars.s_state) != 0) { dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, 0); }
            w.Ragnar.TeleportTo(GatherTest.Ground(c + r * (Half + 3f)) + Vector3.up * 0.3f, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(7f); // 새 문·닫은 문 navmesh
            checks.Add($"문 두 개 단계: 두 문 닫힘·일꾼 집 뒤 (앞 {dz.GetInt(ZDOVars.s_state)}·뒤 {bz.GetInt(ZDOVars.s_state)}, 안 {In(w.Ragnar.transform.position)}, 뒷문까지 {Utils.DistanceXZ(w.Ragnar.transform.position, backAt):0.0}m)",
                dz.GetInt(ZDOVars.s_state) == 0 && bz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position));
            tele0 = Teleports(); entered0 = DoorWay.Entered; closed0 = DoorWay.Closed;
            bool frontOpened = false, backOpened = false;
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() =>
            {
                if (dz.GetInt(ZDOVars.s_state) != 0) frontOpened = true;
                if (bz.GetInt(ZDOVars.s_state) != 0) backOpened = true;
                return w.Ragnar.CountItem("MeadHealthMinor") >= 3;
            }, 90f);
            took = TestKit.Waited;
            tele = Teleports() - tele0;
            checks.Add($"뒷문으로 들어가 미드 챙김 (들고 {w.Ragnar.CountItem("MeadHealthMinor")}, {took:0}초, 뒷문 열림 {backOpened}·앞문 열림 {frontOpened}, 들어감 {DoorWay.Entered - entered0}번, 순간이동 {tele})",
                w.Ragnar.CountItem("MeadHealthMinor") >= 3 && backOpened && !frontOpened && tele == 0);
            // closed0은 단계 시작에서 — 나올 때 문짝이 내 쪽이면 바로 닫음(회차 348)
            yield return TestKit.WaitFor(() => DoorWay.Closed > closed0 && dz.GetInt(ZDOVars.s_state) == 0 && bz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position), 40f); // E064 잠깐 닫힘
            checks.Add($"나와서 두 문 다 닫힘 (앞 {dz.GetInt(ZDOVars.s_state)}·뒤 {bz.GetInt(ZDOVars.s_state)}, 닫기 {DoorWay.Closed - closed0}번, {TestKit.Waited:0}초, {DoorWay.OpenedDiag()})",
                dz.GetInt(ZDOVars.s_state) == 0 && bz.GetInt(ZDOVars.s_state) == 0 && DoorWay.Closed > closed0);
            GatherTest.Say("!멈춰", null);
            TestKit.ClearBaseStock(w, "MeadHealthMinor");

            // 4-3) 좁은 복도(④-2): 앞문 앞에 폭 2m·길이 4m 벽 복도. 일꾼을 복도 입구 옆에 두면 복도로 들어가 앞문을 열고 미드를 챙긴 뒤 복도로 나와 문을 닫는지
            yield return new WaitForSeconds(1f);
            int hall = 0;
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 2; k++)
                {
                    Vector3 at = c - r * (Half + 1f + 2f * k) + f * s; at.y = floorY;
                    if (Put(made, "woodwall", at, Quaternion.LookRotation(f * s), Mathf.Min(floorY, ZoneSystem.instance.GetGroundHeight(at)) - 0.05f) != null) hall++;
                }
            foreach (var it in new List<ItemDrop.ItemData>(w.Ragnar.GetInventory().GetAllItems()))
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "MeadHealthMinor") w.Ragnar.GetInventory().RemoveItem(it);
            TestKit.PutInChest(w.Chest, "MeadHealthMinor", 3);
            if (dz.IsValid() && dz.GetInt(ZDOVars.s_state) != 0) { dz.SetOwner(ZDOMan.GetSessionID()); dz.Set(ZDOVars.s_state, 0); }
            ZNetScene.instance.Destroy(back); // 뒷문은 다시 벽으로(회차 318: 상자가 뒷벽 곁이라 상자 앞까지 걸은 일꾼이 뒷문을 고름 → 복도를 안 지남)
            bool wallBack = Put(made, "woodwall", backAt, Quaternion.LookRotation(r), floorY - 0.05f) != null;
            w.Ragnar.TeleportTo(GatherTest.Ground(c - r * (Half + 6f) + f * 3f) + Vector3.up * 0.3f, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(7f); // 새 벽·닫은 문 navmesh
            checks.Add($"복도 단계: 복도 벽 {hall}/4·뒷벽 {wallBack}·앞문 닫힘·일꾼 밖 (앞 {dz.GetInt(ZDOVars.s_state)}, 안 {In(w.Ragnar.transform.position)})",
                hall == 4 && wallBack && dz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position));
            tele0 = Teleports(); entered0 = DoorWay.Entered; closed0 = DoorWay.Closed;
            frontOpened = false;
            float hallRun = 0f, hallMax = 0f; Vector3 hallLast = w.Ragnar.transform.position;
            GatherTest.Say($"!{w.Ragnar.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() =>
            {
                if (dz.GetInt(ZDOVars.s_state) != 0) frontOpened = true;
                Vector3 p = w.Ragnar.transform.position; // 매 틀 → 틀 사이 0.02m 아래면 멈춤으로(0.5초 간격 근사)
                if (Utils.DistanceXZ(p, hallLast) < 0.004f) { hallRun += Time.deltaTime; hallMax = Mathf.Max(hallMax, hallRun); } else hallRun = 0f;
                hallLast = p;
                return w.Ragnar.CountItem("MeadHealthMinor") >= 3;
            }, 90f);
            took = TestKit.Waited;
            tele = Teleports() - tele0;
            checks.Add($"복도 지나 앞문으로 들어가 미드 챙김 (들고 {w.Ragnar.CountItem("MeadHealthMinor")}, {took:0}초, 앞문 열림 {frontOpened}, 들어감 {DoorWay.Entered - entered0}번, 순간이동 {tele}, 멈춤 최장 {hallMax:0.0}초)",
                w.Ragnar.CountItem("MeadHealthMinor") >= 3 && frontOpened && tele == 0);
            // closed0은 단계 시작에서 — 나올 때 문짝이 내 쪽이면 바로 닫음(회차 348)
            yield return TestKit.WaitFor(() => DoorWay.Closed > closed0 && dz.GetInt(ZDOVars.s_state) == 0 && !In(w.Ragnar.transform.position), 40f); // E064 잠깐 닫힘
            checks.Add($"복도로 나와서 문 닫힘 (앞 {dz.GetInt(ZDOVars.s_state)}, 닫기 {DoorWay.Closed - closed0}번, {TestKit.Waited:0}초, {DoorWay.OpenedDiag()})",
                dz.GetInt(ZDOVars.s_state) == 0 && DoorWay.Closed > closed0);
            GatherTest.Say("!멈춰", null);
            TestKit.ClearBaseStock(w, "MeadHealthMinor");

            // 4-4) 2층 상자(④-3): 집 앞(f쪽) 밖에 기둥 4개 위 바닥(2×2m)과 계단, 그 위 상자에만 미드. 일꾼을 계단 아래 옆에 두면 계단으로 올라가 챙기는지
            yield return new WaitForSeconds(1f);
            Vector3 lp = c + f * (Half + 3f);
            foreach (var col in Physics.OverlapSphere(lp + f * 2f, 5f))
            {
                ZNetView nv = col.GetComponentInParent<ZNetView>();
                if (nv == null || !nv.IsValid() || nv.GetComponent<Piece>() != null || nv.GetComponent<Character>() != null) continue;
                if (nv.GetComponent<TreeBase>() != null || nv.GetComponent<Destructible>() != null || nv.GetComponent<Pickable>() != null) ZNetScene.instance.Destroy(nv.gameObject);
            }
            // 회차 352(E063): 2층 자리가 비탈이면 계단 1칸에 땅이 닿고 언덕 쪽 땅에서 2층 상자에 손이 닿음(땅 위 0.3m, 5초) → 2층·계단 자리도 고름
            Plugin.Log.LogInfo($"집 시험: 2층 자리 땅 고르기 {LevelGround(lp + f * 2.5f, r, f, ZoneSystem.instance.GetGroundHeight(lp), 4f)}번");
            yield return new WaitForSeconds(1f);
            float lg = ZoneSystem.instance.GetGroundHeight(lp), floorTop = lg + 2f; // 기둥 wood_pole2 2m 위
            // 계단 한 칸(wood_stair)은 2m 가며 1m 오름(회차 319 잼) → 바닥 가장자리부터 f쪽으로 땅에 닿을 때까지 잇는다.
            // 모양은 재 봄: f 축을 따라 위에서 아래로 쏴 높은 끝·낮은 끝 → 높은 끝이 바닥 쪽
            bool Profile(GameObject st, out Vector3 hi, out Vector3 lo)
            {
                hi = lo = Vector3.zero; bool any = false;
                Physics.SyncTransforms();
                float sy = Box(st).max.y + 0.5f;
                for (float s = -2.5f; s <= 2.5f; s += 0.125f)
                {
                    Vector3 o = st.transform.position + f * s; o.y = sy;
                    foreach (var h in Physics.RaycastAll(o, Vector3.down, 6f, LayerMask.GetMask("piece")))
                    {
                        if (!h.collider.transform.IsChildOf(st.transform)) continue;
                        if (!any || h.point.y > hi.y) hi = h.point;
                        if (!any || h.point.y < lo.y) lo = h.point;
                        any = true;
                    }
                }
                return any;
            }
            var stairs = new List<GameObject>();
            GameObject stair = null;
            Quaternion srot = Quaternion.LookRotation(-f);
            Vector3 edge = lp + f * 1f; edge.y = floorTop; // 바닥 판 가장자리, 다음 칸 높은 끝이 갈 곳
            Vector3 shi = default, slo = default;
            for (int k = 0; k < 4; k++)
            {
                var st = Put(made, "wood_stair", edge + f * 1f, srot, edge.y - 1f);
                if (st == null || !Profile(st, out shi, out slo)) { stair = null; break; }
                if (Vector3.Dot(shi - slo, f) > 0f) { srot = Quaternion.LookRotation(f); st.transform.rotation = srot; Profile(st, out shi, out slo); }
                st.transform.position += edge - shi; // 높은 끝을 edge에
                Profile(st, out shi, out slo);
                stairs.Add(st); stair = st;
                Plugin.Log.LogInfo($"집 시험: 계단 {k + 1}칸 높은 끝 {shi:F1}·낮은 끝 {slo:F1}(오름 {shi.y - slo.y:0.00}m·길이 {Utils.DistanceXZ(shi, slo):0.00}m), 밑 {Box(st).min.y - ZoneSystem.instance.GetGroundHeight(slo):0.00}m 땅 위");
                if (slo.y <= ZoneSystem.instance.GetGroundHeight(slo) + 0.35f) break;
                edge = slo + f * 0.15f; edge.y = slo.y - 0.2f; // 맨 아래 디딤판 다음 칸(디딤 높이 ≈ 0.2m)
            }
            var lfloor = Put(made, "wood_floor", lp, Quaternion.LookRotation(f), floorTop - 0.1f);
            Bounds lfb = lfloor != null ? Box(lfloor) : default;
            if (lfloor != null) { lfloor.transform.position += Vector3.up * (floorTop - lfb.max.y); Physics.SyncTransforms(); lfb = Box(lfloor); }
            int poles = 0;
            foreach (float a in new[] { -0.9f, 0.9f })
                foreach (float b in new[] { -0.9f, 0.9f })
                {
                    var pole = Put(made, "wood_pole2", lp + r * a + f * b, Quaternion.LookRotation(f), lfb.min.y - 2f);
                    if (pole != null) poles++;
                }
            var loft = Put(made, "piece_chest_wood", lp, Quaternion.LookRotation(-f), lfb.max.y);
            yield return new WaitForSeconds(2f);
            Container lc = loft != null ? loft.GetComponent<Container>() : null;
            bool loftAlive = lfloor != null && stair != null && lc != null;
            if (lc != null) TestKit.PutInChest(lc, "MeadHealthMinor", 3);
            w.Ragnar.TeleportTo(GatherTest.Ground(lp + f * 3f + r * 3f) + Vector3.up * 0.3f, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(7f); // 새 바닥·계단 navmesh
            int had = w.Ragnar.CountItem("MeadHealthMinor"); // 회차 319 첫 실행: 기다리는 7초 사이 이미 3개 들고 있었음 → 말하기 바로 앞에서 비움
            foreach (var it in new List<ItemDrop.ItemData>(w.Ragnar.GetInventory().GetAllItems()))
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "MeadHealthMinor") w.Ragnar.GetInventory().RemoveItem(it);
            // 회차 329: 기다리는 사이 일꾼이 집 안 상자에 미드를 넣어 두어 2층 대신 집 안 상자에서 챙김 → 거점 미드를 다 비우고 2층 상자에만 다시 3
            TestKit.ClearBaseStock(w, "MeadHealthMinor");
            if (lc != null) TestKit.PutInChest(lc, "MeadHealthMinor", 3);
            float chestUp = lc != null ? lc.transform.position.y - lg : 0f;
            checks.Add($"2층 단계: 기둥 {poles}/4·바닥·계단·상자 (계단 {stairs.Count}칸, 남음 {loftAlive}, 들고 있던 미드 {had}, 상자 높이 {chestUp:0.0}m, 상자 미드 {Count(lc, "MeadHealthMinor")})",
                poles == 4 && loftAlive && stairs.Count >= 2 && chestUp > 1.5f && Count(lc, "MeadHealthMinor") == 3); // 계단 1칸이면 땅이 바닥 가까이 — 언덕에서 손이 닿음(회차 352)
            if (lfloor != null && stair != null && loft != null && lc != null)
            {
                tele0 = Teleports();
                float topY = float.MinValue;
                GatherTest.Say($"!{w.Ragnar.NpcName} 나무 5", null);
                yield return TestKit.WaitFor(() =>
                {
                    Vector3 rp = w.Ragnar.transform.position; // 땅 위 높이(비탈) — 계단 중간에서 바닥 너머로 손을 뻗어 꺼내도 됨(바닥에 안 가림, 회차 319)
                    topY = Mathf.Max(topY, rp.y - ZoneSystem.instance.GetGroundHeight(rp));
                    return w.Ragnar.CountItem("MeadHealthMinor") >= 3;
                }, 90f);
                took = TestKit.Waited;
                tele = Teleports() - tele0;
                checks.Add($"계단 올라 2층 상자에서 미드 챙김 (들고 {w.Ragnar.CountItem("MeadHealthMinor")}, {took:0}초, 땅 위 최고 {topY:0.0}m·바닥 {lfb.max.y - lg:0.0}m, 순간이동 {tele}, {w.Ragnar.Ai.Job?.StepName})",
                    w.Ragnar.CountItem("MeadHealthMinor") >= 3 && topY > 0.8f && tele == 0);
                GatherTest.Say("!멈춰", null);
            }
            TestKit.ClearBaseStock(w, "MeadHealthMinor");
            foreach (var g in stairs) if (g != null) ZNetScene.instance.Destroy(g);
            foreach (var g in new[] { loft, lfloor }) if (g != null) ZNetScene.instance.Destroy(g);

            // 5) 지붕 없는 작업대(③-1): 지붕(wood_roof)을 걷으면 바닐라처럼 못 쓰니 일꾼도 안 만들고 재료가 그대로
            yield return new WaitForSeconds(2f);
            int removed = 0;
            foreach (var g in made)
            {
                if (g == null) continue;
                string p = Utils.GetPrefabName(g);
                if (p == "smelter") ZNetScene.instance.Destroy(g); // 2단계가 작업대 자리에 놓은 것
                else if (p == "wood_roof") { ZNetScene.instance.Destroy(g); removed++; }
            }
            yield return new WaitForSeconds(1f);
            bench = Put(made, "piece_workbench", GatherTest.Ground(c + r * 1.2f - f * 1.5f), Quaternion.LookRotation(-r));
            cs = bench != null ? bench.GetComponent<CraftingStation>() : null;
            yield return new WaitForSeconds(2f);
            foreach (var req in bow.m_resources) TestKit.PutInChest(w.Chest, Utils.GetPrefabName(req.m_resItem.gameObject), req.m_amount);
            int bows0 = Stock(w, "Bow"), fails0 = CraftStep.RoofFails;
            CraftStep.TestCheckRoof = true;
            try
            {
                GatherTest.Say($"!{w.Ragnar.NpcName} {bowName} 1", null);
                yield return TestKit.WaitFor(() => CraftStep.RoofFails > fails0 || Stock(w, "Bow") > bows0, 120f);
                bool usable = cs != null && cs.CheckUsable(w.Player, false);
                checks.Add($"지붕 걷은 작업대에선 활 안 만듦 (지붕 {removed}/9 걷음, 활 {Stock(w, "Bow")}, 바닐라 CheckUsable {(usable ? "됨" : "안 됨")}, 실패 {CraftStep.RoofFails - fails0}, {TestKit.Waited:0}초)",
                    removed == 9 && !usable && Stock(w, "Bow") == bows0 && CraftStep.RoofFails > fails0);
            }
            finally { CraftStep.TestCheckRoof = false; GatherTest.Say("!멈춰", null); }
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
