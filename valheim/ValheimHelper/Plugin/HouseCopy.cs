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
            try { yield return ImportBody(checks); }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
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
            // 땅: 2m 칸마다 원래 높이로 고름(HouseTest.LevelGround와 같은 DoOperation), 그 뒤 풀·나무 치움
            var doOp = HarmonyLib.AccessTools.Method(typeof(TerrainComp), "DoOperation");
            var settings = new TerrainOp.Settings { m_level = true, m_levelRadius = Cell * 0.75f, m_square = true, m_raise = false, m_smooth = false, m_paintCleared = false };
            var maps = new List<Heightmap>(); int ops = 0;
            foreach (var g in ground)
            {
                Vector3 c = new Vector3(P.x + g.x, P.y + g.y, P.z + g.z);
                maps.Clear(); Heightmap.FindHeightmap(c, settings.m_levelRadius, maps);
                foreach (var hm in maps) { var tc = hm.GetAndCreateTerrainCompiler(); if (tc != null && doOp != null) { doOp.Invoke(tc, new object[] { c, Vector3.zero, settings }); ops++; } }
            }
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
