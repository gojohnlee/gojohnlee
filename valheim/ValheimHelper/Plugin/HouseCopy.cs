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
            // 집 밑 땅 높이 폭(다시 세울 때 고를 땅 범위)
            float gMin = float.MaxValue, gMax = float.MinValue;
            for (float x = -Radius; x <= Radius; x += 4f)
                for (float zz = -Radius; zz <= Radius; zz += 4f)
                    if (x * x + zz * zz <= Radius * Radius && ZoneSystem.instance.GetGroundHeight(Center + new Vector3(x, 0f, zz), out float h))
                    { gMin = Mathf.Min(gMin, h - baseY); gMax = Mathf.Max(gMax, h - baseY); }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines);
            TestRunner.Log($"가운데 땅 {baseY:0.0}, 조각 높이 {minY:0.0}~{maxY:0.0}, 땅 {gMin:0.0}~{gMax:0.0}, 뺀 ZDO {skipped}, 상자 {chests}");
            TestRunner.Log("많은 조각: " + string.Join(", ", kinds.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key} {k.Value}")));
            checks.Add($"집 조각을 파일로 ({lines.Count}개, 상자 {chests}, {FilePath})", lines.Count > 500 && chests > 0);
        }
    }
}
