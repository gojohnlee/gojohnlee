using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_housebuild(요청 11-17 "복잡한 집에서 걷기·충돌", 회차 770): 다른 월드에 맞는 집이 없어(Chilsung은 만든이 0 폐허뿐, 5665553은 조각 33)
    /// 복사본 월드 vh_house_TheNewEra에 2층 집을 코드로 짓는다 — 겹문 현관·좁은 복도(2m)·문 3개·낮은 천장(벽 한 장 2m)·계단·가구 빽빽.
    /// 조각은 붙는 점(snappoint) 범위로 자리 잡고(아래 = 목표 높이, 가로 = 가운데), 붙는 점 없는 것(문·가구)은 충돌체 범위로.
    /// 만든이 = 시험 플레이어(vh_housefind가 셈). 저장 뒤 집 자리를 diag에 → vh_otherhouse VH_HOUSE_AT="x,z".
    /// 칸은 2m, 집은 가로 NX·세로 NZ 칸(원점 = 남서 모서리).
    /// </summary>
    static class HouseBuild
    {
        public static void Register() => TestRunner.Register("vh_housebuild", "복사본 월드에 복잡한 2층 시험 집 짓기", () => TestKit.Flat(Run()));

        const string World = "vh_house_TheNewEra";
        const int NX = 6, NZ = 5;
        const float C = 2f, Y0 = 0f, H = 2f;   // 1층 바닥 윗면 = 고른 땅(회차 771: 0.2m 띄우니 땅에 안 닿아 받침 0 → 다시 불러올 때 다 무너짐), 층 높이 = 벽 한 장
        static Vector3 P;
        /// <summary>이번 게임에서 지은 집 가운데 — vh_otherhouse VH_HOUSE_AT=built(같은 판에서 짓고 바로 걷기, 회차 773: 다시 불러온 옛 집은 조각 70만 남음).</summary>
        internal static Vector3? Built;
        static int made, missing;
        static readonly List<Transform> snaps = new List<Transform>();
        static readonly Dictionary<string, string> probe = new Dictionary<string, string>();

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer() || ZNet.instance.GetWorldName() != World)
            {
                TestRunner.Log($"FAIL: 월드 {World}에서(VH_WORLD·VH_RESET=0) 서버로 실행해야 합니다(지금 {ZNet.instance?.GetWorldName()}).");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            try { yield return Body(checks); }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
        }

        /// <summary>월드 가운데 둘레 150~1500m 풀밭·검은숲 중 집 터(+3m)의 높이 폭 2.5m 밑, 물 위 3m, 다른 기지(침대·문·작업대·상자) 80m 밖.</summary>
        static Vector3? Site(out string why)
        {
            var wg = WorldGenerator.instance; float wl = ZoneSystem.instance.m_waterLevel;
            var known = new List<Vector3>();
            foreach (string a in new[] { "bed", "piece_bed02", "wood_door", "piece_workbench", "piece_chest_wood", "piece_chest" })
                foreach (ZDO z in ZdoScan.All(a)) known.Add(z.GetPosition());
            Vector3 c0 = Vector3.zero;
            int tried = 0;
            for (float d = 150f; d <= 1500f; d += 50f)
                for (int k = 0; k < 24; k++, tried++)
                {
                    float a = k * Mathf.PI / 12f;
                    Vector3 p = c0 + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    if (known.Exists(q => Utils.DistanceXZ(q, p) < 80f)) continue;
                    float lo = float.MaxValue, hi = float.MinValue; bool ok = true;
                    for (float x = -3f; x <= NX * C + 3f && ok; x += 1.5f)
                        for (float z = -3f; z <= NZ * C + 3f; z += 1.5f)
                        {
                            if ((wg.GetBiome(p.x + x, p.z + z) & (Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest)) == 0) { ok = false; break; }
                            float h = wg.GetHeight(p.x + x, p.z + z);
                            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        }
                    if (ok && hi - lo < 2.5f && lo > wl + 3f) { p.y = (lo + hi) / 2f; why = $"{tried + 1}번째 자리, 높이 폭 {hi - lo:0.0}"; return p; }
                }
            why = $"{tried}곳 다 안 맞음";
            return null;
        }

        static IEnumerator Body(GatherTest.Checks checks)
        {
            Player pl = Player.m_localPlayer;
            Vector3? at = Site(out string why);
            TestRunner.Log($"집 터: {(at == null ? "없음" : $"({at.Value.x:0},{at.Value.y:0.0},{at.Value.z:0})")} — {why}");
            if (at == null) { checks.Add("집 터 찾음", false); yield break; }
            P = at.Value; made = missing = 0; probe.Clear();
            Vector3 mid = P + new Vector3(NX * C / 2f, 0f, NZ * C / 2f);
            pl.TeleportTo(mid + new Vector3(NX * C + 6f, 3f, 0f), pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            yield return new WaitForSeconds(3f);
            int ops = Level();
            int plants = HouseTest.ClearPlants(mid, NX * C);
            yield return new WaitForSeconds(2f);
            Build(pl.GetPlayerID());
            foreach (var s in probe.Values) TestRunner.Log("  " + s);
            TestRunner.Log($"땅 고르기 {ops}번, 풀·나무 {plants}, 조각 {made}개 세움, 프리팹 없음 {missing}");
            yield return new WaitForSeconds(20f); // WearNTear 받침 셈이 돌 때까지
            int alive = 0, beds = 0, chests = 0, doors = 0;
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                Vector3 d = p.transform.position - P;
                if (d.x < -1f || d.z < -1f || d.x > NX * C + 1f || d.z > NZ * C + 1f) continue;
                alive++;
                if (p.GetComponent<Bed>()) beds++;
                if (p.GetComponent<Container>()) chests++;
                if (p.GetComponent<Door>()) doors++;
            }
            Built = mid;
            TestRunner.Log($"20초 뒤 조각 {alive}/{made}·침대 {beds}·상자 {chests}·문 {doors} — 집 자리 VH_HOUSE_AT=\"{mid.x:0},{mid.z:0}\"");
            checks.Add($"집이 무너지지 않고 섬 ({alive}/{made}, 침대 {beds}, 상자 {chests}, 문 {doors})", missing == 0 && alive >= made - 2 && beds >= 2 && doors >= 4);
            ZNet.instance.Save(false);
            yield return new WaitForSeconds(5f);
        }

        /// <summary>집 터(+3m) 1m 꼭짓점마다 P.y로(HouseCopy.Level과 같은 방법 — 반지름 0, 꼭짓점 하나).</summary>
        static int Level()
        {
            var doOp = HarmonyLib.AccessTools.Method(typeof(TerrainComp), "DoOperation");
            var settings = new TerrainOp.Settings { m_level = true, m_levelRadius = 0f, m_square = true, m_raise = false, m_smooth = false, m_paintCleared = false };
            var maps = new List<Heightmap>(); int ops = 0;
            for (float x = -3f; x <= NX * C + 3f; x += 1f)
                for (float z = -3f; z <= NZ * C + 3f; z += 1f)
                {
                    Vector3 c = P + new Vector3(x, 0f, z);
                    maps.Clear(); Heightmap.FindHeightmap(c, 0.1f, maps);
                    foreach (var hm in maps) { var tc = hm.GetAndCreateTerrainCompiler(); if (tc != null && doOp != null) { doOp.Invoke(tc, new object[] { c, Vector3.zero, settings }); ops++; } }
                }
            return ops;
        }

        // 칸 (i,k)의 가운데, 층 f의 바닥 윗면
        static float Fy(int f) => Y0 + f * H;
        static void Floor(int i, int k, int f) => Put("wood_floor", i * C + 1f, Fy(f), k * C + 1f, 0f, true);
        static void WallX(int i, int zLine, int f, string name = "woodwall") => Put(name, i * C + 1f, Fy(f), zLine * C, 0f, true);   // z = zLine*2 선 위, x 칸 i
        static void WallZ(int xLine, int k, int f, string name = "woodwall") => Put(name, xLine * C, Fy(f), k * C + 1f, 90f, true);
        static void Thing(string name, float x, float z, int f, float yaw) => Put(name, x, Fy(f), z, yaw, false);

        /// <summary>
        /// 1층(칸 i 0~5, k 0~4): k=0 줄 = 벽장(i0, 복도로 트임)·현관(i1, 바깥문+안문 = 겹문)·창고(i2~5, 복도에서 문);
        /// k=1 줄 = 좁은 복도(폭 2m, 양쪽 벽); k=2~4 = 거실(i0~3, 가구 빽빽)·계단방(i4~5, 복도에서 트임, 거실과 k3에서 트임).
        /// 2층: 바닥(계단 구멍 빼고)·바깥벽·가운데 칸막이(문)·침대 둘·상자. 지붕 = 평평한 바닥 한 겹.
        /// </summary>
        static void Build(long creator)
        {
            Creator = creator;
            // 1층 바닥
            for (int i = 0; i < NX; i++) for (int k = 0; k < NZ; k++) Floor(i, k, 0);
            // 바깥벽 1층(남쪽 현관 칸 i1은 문)
            for (int i = 0; i < NX; i++) { if (i != 1) WallX(i, 0, 0); WallX(i, NZ, 0); }
            for (int k = 0; k < NZ; k++) { WallZ(0, k, 0); WallZ(NX, k, 0); }
            Thing("wood_door", 1 * C + 1f, 0f, 0, 0f);                       // 바깥문
            // z=2 선(k0 | 복도): i0 트임, i1 안문(겹문), i2·3·5 벽, i4 창고문
            WallX(2, 1, 0); WallX(3, 1, 0); WallX(5, 1, 0);
            Thing("wood_door", 1 * C + 1f, 1 * C, 0, 0f);
            Thing("wood_door", 4 * C + 1f, 1 * C, 0, 0f);
            WallZ(1, 0, 0); WallZ(2, 0, 0);                                    // 현관 양옆(벽장·창고와 가름)
            // z=4 선(복도 | 거실·계단방): i0~3 벽, i4·i5 트임(i5 = 계단 입구 — 회차 771: 벽이 막아 2층 길이 안 남)
            for (int i = 0; i < 4; i++) WallX(i, 2, 0);
            // x=8 선(거실 | 계단방): k2·k4 벽, k3 트임
            WallZ(4, 2, 0); WallZ(4, 4, 0);
            // 계단: 나무 계단 한 장 = 1m 오름(회차 770 잼: 충돌체 높이 1.12) → 두 장 이어 계단방 i5에서 북쪽으로 (5,2)→(5,3), 2층 구멍 = 둘
            Stair(5 * C + 1f, 2 * C + 1f, Fy(0));
            Stair(5 * C + 1f, 3 * C + 1f, Fy(0) + H / 2f);
            // 1층 가구: 벽장 상자, 창고 상자 넷, 거실 작업대·상자 셋·탁자·의자·화덕
            Thing("piece_chest_wood", 1f, 0.7f, 0, 0f);
            for (int i = 2; i < 6; i++) Thing("piece_chest_wood", i * C + 1f, 0.7f, 0, 0f);
            Thing("piece_workbench", 1.2f, 2 * C + 1.2f, 0, 90f);
            Thing("piece_chest_wood", 1f, 4 * C + 1.3f, 0, 180f);
            Thing("piece_chest_wood", 3f, 4 * C + 1.3f, 0, 180f);
            Thing("piece_chest_wood", 7f, 4 * C + 1.3f, 0, 180f);
            Thing("piece_table", 4.5f, 3 * C + 1f, 0, 0f);
            Thing("piece_chair", 4.5f, 3 * C - 0.2f, 0, 0f);
            Thing("piece_chair", 4.5f, 3 * C + 2.2f, 0, 180f);
            Thing("piece_chair", 6.8f, 3 * C + 1f, 0, 270f);
            // 2층 바닥(계단 구멍 (5,2)·(5,3) 빼고)
            for (int i = 0; i < NX; i++) for (int k = 0; k < NZ; k++) if (!(i == 5 && (k == 2 || k == 3))) Floor(i, k, 1);
            // 2층 바깥벽
            for (int i = 0; i < NX; i++) { WallX(i, 0, 1); WallX(i, NZ, 1); }
            for (int k = 0; k < NZ; k++) { WallZ(0, k, 1); WallZ(NX, k, 1); }
            // 2층 칸막이 x=6 선: k0·k1·k3·k4 벽, k2 문(계단 위 쪽에서 서쪽 침실로)
            WallZ(3, 0, 1); WallZ(3, 1, 1); WallZ(3, 3, 1); WallZ(3, 4, 1);
            Thing("wood_door", 3 * C, 2 * C + 1f, 1, 90f);
            // 2층 가구: 침실(서쪽) 침대 둘·상자, 동쪽 상자 둘
            Thing("bed", 1.5f, 1.5f, 1, 0f);
            Thing("bed", 1.5f, 4 * C + 0.5f, 1, 180f);
            Thing("piece_chest_wood", 5f, 0.7f, 1, 0f);
            Thing("piece_chest_wood", 7f, 4 * C + 1.3f, 1, 180f);
            Thing("piece_chest_wood", 9f, 4 * C + 1.3f, 1, 180f);
            // 지붕
            for (int i = 0; i < NX; i++) for (int k = 0; k < NZ; k++) Floor(i, k, 2);
        }

        /// <summary>계단을 놓고 남(-z)·북(+z) 0.7m 위에서 아래로 쏴 높은 쪽이 남이면 180° 돌림 → 북쪽으로 오름.</summary>
        static void Stair(float x, float z, float y)
        {
            Put("wood_stair", x, y, z, 0f, false);
            GameObject go = last;
            if (go == null) return;
            Physics.SyncTransforms();
            float S(float dz) => Physics.Raycast(P + new Vector3(x, y + 3f, z + dz), Vector3.down, out RaycastHit h, 4f) ? h.point.y : float.MinValue;
            float s = S(-0.7f), n = S(0.7f);
            if (s > n) { go.transform.RotateAround(P + new Vector3(x, y, z), Vector3.up, 180f); go.GetComponent<ZNetView>()?.GetZDO()?.SetPosition(go.transform.position); go.GetComponent<ZNetView>()?.GetZDO()?.SetRotation(go.transform.rotation); }
            if (!probe.ContainsKey("stair" + z)) probe["stair" + z] = $"계단 z {z:0}: 남 {s - P.y:0.00}·북 {n - P.y:0.00}{(s > n ? " → 돌림" : "")}";
        }

        static GameObject last;
        static long Creator;

        /// <summary>프리팹을 세우고 붙는 점(byPiece = true) 또는 충돌체 범위의 아래를 y, 가로 가운데를 (x,z)에 맞춤. 만든이 = 시험 플레이어.</summary>
        static void Put(string name, float x, float y, float z, float yaw, bool bySnap)
        {
            last = null;
            GameObject pf = ZNetScene.instance.GetPrefab(name);
            if (pf == null) { missing++; probe[name] = $"{name} 프리팹 없음"; return; }
            Vector3 target = P + new Vector3(x, y, z);
            GameObject go = Object.Instantiate(pf, target, Quaternion.Euler(0f, yaw, 0f));
            snaps.Clear();
            if (bySnap) go.GetComponent<Piece>()?.GetSnapPoints(snaps);
            Bounds b = default; bool has = false;
            if (snaps.Count > 0) { b = new Bounds(snaps[0].position, Vector3.zero); foreach (var s in snaps) b.Encapsulate(s.position); has = true; }
            else
            {
                Physics.SyncTransforms();
                foreach (var col in go.GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger) continue;
                    if (!has) { b = col.bounds; has = true; } else b.Encapsulate(col.bounds);
                }
            }
            if (has) go.transform.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
            ZDO zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null) { zdo.SetPosition(go.transform.position); zdo.Set(ZDOVars.s_creator, Creator); }
            if (!probe.ContainsKey(name)) probe[name] = $"{name}: {(snaps.Count > 0 ? $"붙는 점 {snaps.Count}" : has ? "충돌체" : "범위 없음")} 크기 {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00}, 피벗-아래 {target.y - b.min.y:0.00}";
            last = go;
            made++;
        }
    }
}
