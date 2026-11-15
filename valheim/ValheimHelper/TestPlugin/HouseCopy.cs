using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// TheNewEra 집 옮기기(사용자 11-12 "시험 서버 기본 집 = TheNewEra 복사본"): 1단계 = vh_houseexport.
    /// vh_house_TheNewEra 메인 베이스(-3959,-5829) 둘레 ZDO 중 건축 조각(Piece 프리팹)을 ZDO.Serialize(위치 빼고 프리팹·회전·모든 값 — 상자 items도)로
    /// %USERPROFILE%/vh-test-base/house-TheNewEra.txt에 쓴다(사용자 집 자료라 저장소엔 안 올림). 줄 꼴: 프리팹 이름 \t dx dy dz(가운데 땅 높이 기준) \t base64.
    /// 2단계(다음 회차): 기본 시험 월드 te4st41에 이 파일로 다시 세움(땅 고르기 + CreateNewZDO + Deserialize + SetPosition).
    /// </summary>
    static class HouseCopy
    {
        public static void Register() => TestRunner.Register("vh_houseexport", "TheNewEra 복사본 집 조각을 파일로(시험 기본 월드로 옮기기 1단계)", () => TestKit.Flat(Export()));

        const string World = "vh_house_TheNewEra";
        internal static readonly Vector3 Center = new Vector3(-3959f, 0f, -5829f);
        internal const float Radius = 30f;
        const float Cell = 2f;
        internal static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "vh-test-base", "house-TheNewEra.txt");

        static IEnumerator Export()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer() || ZNet.instance.GetWorldName() != World)
            {
                TestRunner.Log($"FAIL: 월드 {World}에서(VH_WORLD) 서버로 실행해야 합니다(지금 {ZNet.instance?.GetWorldName()}).");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            try { yield return Body(checks); }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(GatherTest.Checks checks)
        {
            Player pl = Player.m_localPlayer;
            float gy = WorldGenerator.instance.GetHeight(Center.x, Center.z);
            pl.TeleportTo(new Vector3(Center.x + 20f, gy + 1f, Center.z), pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            yield return new WaitForSeconds(5f);
            ZoneSystem.instance.GetGroundHeight(Center, out float baseY); // 땅 고친 뒤 높이(가운데)
            var near = new List<ZDO>(); var distant = new List<ZDO>();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(Center), new SimulationDistance(1, 0, true), near, distant);
            near.AddRange(distant);
            var lines = new List<string>();
            var kinds = new Dictionary<string, int>();
            int skipped = 0, chests = 0;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var z in near)
            {
                if (z == null || !z.Persistent || Utils.DistanceXZ(z.GetPosition(), Center) > Radius) continue;
                GameObject pf = ZNetScene.instance.GetPrefab(z.GetPrefab());
                if (pf == null || pf.GetComponent<Piece>() == null || pf.GetComponent<Character>() != null) { skipped++; continue; }
                string name = pf.name;
                var pkg = new ZPackage();
                z.Serialize(pkg);
                Vector3 d = z.GetPosition() - new Vector3(Center.x, baseY, Center.z);
                lines.Add($"{name}\t{d.x:R} {d.y:R} {d.z:R}\t{Convert.ToBase64String(pkg.GetArray())}");
                kinds[name] = kinds.TryGetValue(name, out int n) ? n + 1 : 1;
                if (pf.GetComponent<Container>() != null) chests++;
                minY = Mathf.Min(minY, d.y); maxY = Mathf.Max(maxY, d.y);
            }
            // 집 밑 땅 높이(2m 격자, 가운데 땅 기준) — 다시 세울 때 칸마다 이 높이로 고름. 줄 꼴: "#ground x z dy"
            float gMin = float.MaxValue, gMax = float.MinValue;
            for (float x = -Radius; x <= Radius; x += Cell)
                for (float zz = -Radius; zz <= Radius; zz += Cell)
                    if (x * x + zz * zz <= Radius * Radius && ZoneSystem.instance.GetGroundHeight(Center + new Vector3(x, 0f, zz), out float h))
                    {
                        gMin = Mathf.Min(gMin, h - baseY); gMax = Mathf.Max(gMax, h - baseY);
                        lines.Add($"#ground {x:R} {zz:R} {h - baseY:R}");
                    }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines);
            TestRunner.Log($"가운데 땅 {baseY:0.0}, 조각 높이 {minY:0.0}~{maxY:0.0}, 땅 {gMin:0.0}~{gMax:0.0}, 뺀 ZDO {skipped}, 상자 {chests}");
            TestRunner.Log("많은 조각: " + string.Join(", ", kinds.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key} {k.Value}")));
            checks.Add($"집 조각을 파일로 ({lines.Count}개, 상자 {chests}, {FilePath})", lines.Count > 500 && chests > 0);
        }

        public static void RegisterImport() => TestRunner.Register("vh_houseimport", "TheNewEra 집을 기본 시험 월드에 다시 세움(옮기기 2단계)", () => TestKit.Flat(Import()));

        /// <summary>기본 시험 월드에 다시 세울 자리: 스폰에서 400~1600m 초원, 반지름 34m 안 생성 높이 폭 5m 밑·물 위 3m. 씨앗이 같으면 늘 같은 자리.</summary>
        internal static Vector3? Place(out string why)
        {
            var wg = WorldGenerator.instance; float wl = ZoneSystem.instance.m_waterLevel;
            int tried = 0;
            for (float d = 400f; d <= 1600f; d += 100f)
                for (int k = 0; k < 24; k++, tried++)
                {
                    float a = k * Mathf.PI / 12f;
                    Vector3 p = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    float lo = float.MaxValue, hi = float.MinValue; bool ok = true;
                    for (float x = -34f; x <= 34f && ok; x += 6f)
                        for (float z = -34f; z <= 34f && ok; z += 6f)
                        {
                            if (x * x + z * z > 34f * 34f) continue;
                            if (wg.GetBiome(p.x + x, p.z + z) != Heightmap.Biome.Meadows) { ok = false; break; }
                            float h = wg.GetHeight(p.x + x, p.z + z);
                            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        }
                    if (ok && hi - lo < 5f && lo > wl + 3f) { p.y = wg.GetHeight(p.x, p.z); why = $"{tried + 1}번째 자리, 높이 폭 {hi - lo:0.0}"; return p; }
                }
            why = $"{tried}곳 다 안 맞음";
            return null;
        }

        static IEnumerator Import()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer() || !File.Exists(FilePath))
            {
                TestRunner.Log($"FAIL: 서버로, 파일 {FilePath}이 있어야 합니다(먼저 vh_houseexport).");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            Vector3 start = Player.m_localPlayer.transform.position;
            try { yield return ImportBody(checks); yield return Back(start); }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
        }


        public static void RegisterGround() => TestRunner.Register("vh_houseground", "기본 시험 월드 집 밑 땅을 원래 높이로 다시 고르고 저장(회차 458: 북·남쪽 바닥이 땅에서 떠 무너짐)", () => TestKit.Flat(GroundFix()));

        static List<Vector3> GroundCells()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture; var ground = new List<Vector3>();
            foreach (string line in File.ReadAllLines(FilePath))
                if (line.StartsWith("#ground ")) { var g = line.Substring(8).Split(' '); ground.Add(new Vector3(float.Parse(g[0], inv), float.Parse(g[2], inv), float.Parse(g[1], inv))); }
            return ground;
        }

        /// <summary>땅 꼭짓점(1m)마다 한 번씩 원래 높이로(2m 칸 사이는 섞음). 반지름 0 = 꼭짓점 하나 — 겹쳐 고르면 높이를 다음 프레임에야 새로 세어
        /// 같은 꼭짓점에 차가 여러 번 쌓임(TerrainComp.LevelTerrain: m_levelDelta += 목표 - GetHeight, 회차 458 2m 칸·반지름 1.5로 ±7.6m 넘침). 고른 횟수.</summary>
        static int Level(Vector3 P, List<Vector3> ground)
        {
            var doOp = HarmonyLib.AccessTools.Method(typeof(TerrainComp), "DoOperation");
            var settings = new TerrainOp.Settings { m_level = true, m_levelRadius = 0f, m_square = true, m_raise = false, m_smooth = false, m_paintCleared = false };
            var at = new Dictionary<Vector2Int, float>();
            foreach (var g in ground) at[new Vector2Int(Mathf.RoundToInt(g.x / Cell), Mathf.RoundToInt(g.z / Cell))] = g.y;
            var maps = new List<Heightmap>(); int ops = 0;
            for (float x = -Radius; x <= Radius; x += 1f)
                for (float z = -Radius; z <= Radius; z += 1f)
                {
                    float fx = x / Cell, fz = z / Cell; int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
                    float sum = 0f, wsum = 0f;
                    for (int i = 0; i <= 1; i++) for (int k = 0; k <= 1; k++)
                    {
                        if (!at.TryGetValue(new Vector2Int(x0 + i, z0 + k), out float h)) continue;
                        float wgt = (i == 0 ? 1f - (fx - x0) : fx - x0) * (k == 0 ? 1f - (fz - z0) : fz - z0);
                        if (wgt <= 0f) continue;
                        sum += h * wgt; wsum += wgt;
                    }
                    if (wsum <= 0f) continue;
                    Vector3 c = P + new Vector3(x, sum / wsum, z);
                    maps.Clear(); Heightmap.FindHeightmap(c, 0.1f, maps);
                    foreach (var hm in maps) { var tc = hm.GetAndCreateTerrainCompiler(); if (tc != null && doOp != null) { doOp.Invoke(tc, new object[] { c, Vector3.zero, settings }); ops++; } }
                }
            return ops;
        }

        /// <summary>땅이 원래 높이와 가장 크게 다른 값(m)과 0.3m 넘게 다른 칸 수.</summary>
        static float GroundOff(Vector3 P, List<Vector3> ground, out int bad)
        {
            float worst = 0f; bad = 0;
            foreach (var g in ground)
            {
                Vector3 c = P + g; float d = ZoneSystem.instance.GetGroundHeight(c) - c.y;
                if (Mathf.Abs(d) > 0.3f) bad++;
                if (Mathf.Abs(d) > Mathf.Abs(worst)) worst = d;
            }
            return worst;
        }

        static IEnumerator GroundFix()
        {
            var checks = new GatherTest.Checks();
            var w = new TestKit.World();
            Vector3 start = Player.m_localPlayer.transform.position;
            try
            {
                Vector3? at = Place(out string why);
                checks.Add($"집 자리 찾음 ({why})", at != null && File.Exists(FilePath));
                if (at != null && File.Exists(FilePath))
                {
                    Vector3 P = at.Value; Player pl = Player.m_localPlayer; var ground = GroundCells();
                    pl.TeleportTo(P + new Vector3(Radius + 8f, 2f, 0f), pl.transform.rotation, true);
                    yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
                    yield return new WaitForSeconds(1f);
                    float before = GroundOff(P, ground, out int badBefore);
                    int ops = Level(P, ground);
                    yield return new WaitForSeconds(1f);
                    yield return new WaitForSeconds(1f);
                    float after = GroundOff(P, ground, out int badAfter);
                    TestRunner.Log($"집 밑 땅 {ground.Count}칸: 고르기 전 가장 큰 차 {before:0.00}m(0.3m 넘는 칸 {badBefore}) → {ops}번 고른 뒤 {after:0.00}m({badAfter})");
                    checks.Add($"땅이 원래 높이로 (0.3m 넘는 칸 {badAfter}/{ground.Count}, 가장 큰 차 {after:0.00}m)", badAfter <= ground.Count / 50);
                    yield return new WaitForSeconds(70f);
                    int n = HouseBase.Count(P, out int drops);
                    checks.Add($"70초 뒤 집이 섬 (조각 {n}/1144, 땅 물건 {drops})", n >= 1139);
                    ZNet.instance.Save(false);
                    yield return new WaitForSeconds(5f);
                }
                yield return Back(start);
            }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
        }

        public static void RegisterHere() => TestRunner.Register("vh_househere", "되돌린 기본 시험 월드에 TheNewEra 집이 그대로 있는지(원본에 넣은 뒤)", () => TestKit.Flat(Here()));

        static IEnumerator Here()
        {
            var checks = new GatherTest.Checks();
            var w = new TestKit.World();
            Vector3 start = Player.m_localPlayer.transform.position;
            try { yield return HereBody(checks); yield return Back(start); }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
        }

        static IEnumerator HereBody(GatherTest.Checks checks)
        {
            Vector3? at = Place(out string why);
            checks.Add($"집 자리 찾음 ({why})", at != null);
            if (at == null) yield break;
            Vector3 P = at.Value;
            Player pl = Player.m_localPlayer;
            pl.TeleportTo(P + new Vector3(Radius + 8f, 2f, 0f), pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            yield return new WaitForSeconds(10f);
            int alive = 0, beds = 0, chests = 0;
            foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (Utils.DistanceXZ(p.transform.position, P) <= Radius) { alive++; if (p.GetComponent<Bed>()) beds++; if (p.GetComponent<Container>()) chests++; }
            TestRunner.Log($"되돌린 월드 집 조각 {alive}·침대 {beds}·상자 {chests} (자리 {P.x:0},{P.z:0})");
            checks.Add($"집이 원본에 있음 (조각 {alive}/1144, 침대 {beds}, 상자 {chests})", alive >= 1139 && beds >= 2 && chests >= 20);
            // 구역마다 땅 고치기(TerrainComp)가 있는지·몇 번 고쳤는지(회차 458: 북·남쪽 바닥이 땅과 5~6m 떨어져 무너짐)
            var tcs = new List<string>();
            var opsField = HarmonyLib.AccessTools.Field(typeof(TerrainComp), "m_operations");
            for (int zx = -1; zx <= 1; zx++) for (int zz = -1; zz <= 1; zz++)
            {
                var zone = ZoneSystem.GetZone(P + new Vector3(zx * 32f, 0f, zz * 32f));
                Vector3 zc = ZoneSystem.GetZonePos(zone);
                var tc = TerrainComp.FindTerrainCompiler(zc);
                string id = $"{zone.x},{zone.y}";
                if (!tcs.Exists(t => t.StartsWith(id + "="))) tcs.Add($"{id}={(tc ? opsField?.GetValue(tc)?.ToString() : "없음")}");
            }
            TestRunner.Log($"땅 고치기(구역=횟수): {string.Join(" ", tcs.ToArray())}");
            float needLo = 99f, needHi = -99f; var diff = new Dictionary<string, float>(); var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (File.Exists(FilePath))
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (!line.StartsWith("#ground ")) continue;
                    var g = line.Substring(8).Split(' ');
                    Vector3 c = P + new Vector3(float.Parse(g[0], inv), float.Parse(g[2], inv), float.Parse(g[1], inv));
                    var zone = ZoneSystem.GetZone(c); string id = $"{zone.x},{zone.y}";
                    float d = ZoneSystem.instance.GetGroundHeight(c) - c.y; float need = c.y - WorldGenerator.instance.GetHeight(c.x, c.z); needLo = Mathf.Min(needLo, need); needHi = Mathf.Max(needHi, need);
                    diff.TryGetValue(id, out float m); if (Mathf.Abs(d) > Mathf.Abs(m)) diff[id] = d; else diff[id] = m;
                }
            TestRunner.Log($"땅 고칠 폭(원래 땅 기준, 게임 한도 ±8m) {needLo:0.0}~{needHi:0.0}m, 땅이 원래 높이와 다른 폭(구역=가장 큰 차, m): {string.Join(" ", diff.Select(kv => $"{kv.Key}={kv.Value:0.0}").ToArray())}");
            // 일꾼 없이도 무너지는지(회차 457~458 wood 시험 중 22초부터 148개 사라짐): 60초 더 두고 다시 셈
            var site = new HouseBase.Site { Center = P };
            foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (Utils.DistanceXZ(p.transform.position, P) <= Radius) HouseBase.Remember(site, p);
            Plugin.Instance.StartCoroutine(HouseBase.Watch(site));
            yield return new WaitForSeconds(60f);
            site.Done = true;
            int later = HouseBase.Count(P, out int drops);
            checks.Add($"일꾼 없이 70초 뒤에도 그대로 (조각 {later}/{alive}, 땅 물건 {drops})", later >= alive - 2);
        }

        /// <summary>집 곁으로 간 플레이어를 시작 자리로(다음 시험이 집 곁에서 거점을 잡아 집을 부수지 않게).</summary>
        static IEnumerator Back(Vector3 start)
        {
            Player pl = Player.m_localPlayer;
            pl.TeleportTo(start + Vector3.up * 0.5f, pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            yield return new WaitForSeconds(3f);
        }
        class Saved { public string Name; public Vector3 D; public byte[] Data; }

        static IEnumerator ImportBody(GatherTest.Checks checks)
        {
            Vector3? at = Place(out string why);
            TestRunner.Log($"자리 {at}: {why}");
            checks.Add($"다시 세울 자리 찾음 ({why})", at != null);
            if (at == null) yield break;
            Vector3 P = at.Value;
            Player pl = Player.m_localPlayer;
            pl.TeleportTo(P + new Vector3(Radius + 8f, 2f, 0f), pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            yield return new WaitForSeconds(5f);
            var pieces = new List<Saved>(); var ground = new List<Vector3>();
            foreach (string line in File.ReadAllLines(FilePath))
            {
                if (line.StartsWith("#ground ")) { var g = line.Substring(8).Split(' '); ground.Add(new Vector3(float.Parse(g[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(g[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(g[1], System.Globalization.CultureInfo.InvariantCulture))); continue; }
                var f = line.Split('	'); var v = f[1].Split(' ');
                pieces.Add(new Saved { Name = f[0], D = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture)), Data = Convert.FromBase64String(f[2]) });
            }
            // 땅: 꼭짓점마다 한 번 원래 높이로(Level — 회차 458 겹쳐 고르기 넘침 고침), 그 뒤 풀·나무 치움
            int ops = Level(P, ground);
            int plants = HouseTest.ClearPlants(P, Radius + 6f);
            yield return new WaitForSeconds(2f);
            int made = 0, bad = 0;
            foreach (var s in pieces)
            {
                if (ZNetScene.instance.GetPrefab(s.Name) == null) { bad++; continue; }
                Vector3 pos = P + s.D;
                ZDO z = ZDOMan.instance.CreateNewZDO(pos, s.Name.GetStableHashCode());
                z.Deserialize(new ZPackage(s.Data));
                z.SetPosition(pos + Vector3.up * 0.01f); z.SetPosition(pos); // 자리 다시 넣어 지속 ZDO 구역을 저장 대상으로
                made++;
            }
            TestRunner.Log($"땅 고르기 {ground.Count}칸({ops}번), 풀·나무 {plants}, 조각 {made}개 세움, 프리팹 없음 {bad}");
            yield return new WaitForSeconds(20f); // 조각이 생기고 WearNTear 받침 셈이 돌 때까지
            int alive = 0, beds = 0, chests = 0;
            foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (Utils.DistanceXZ(p.transform.position, P) <= Radius) { alive++; if (p.GetComponent<Bed>()) beds++; if (p.GetComponent<Container>()) chests++; }
            TestRunner.Log($"20초 뒤 조각 {alive}/{made}·침대 {beds}·상자 {chests} (자리 {P.x:0},{P.y:0.0},{P.z:0})");
            checks.Add($"집이 무너지지 않고 섬 ({alive}/{made}, 침대 {beds}, 상자 {chests})", alive >= made - 5 && beds >= 2 && chests >= 20);
            ZNet.instance.Save(false);
            yield return new WaitForSeconds(5f);
        }
    }
}
