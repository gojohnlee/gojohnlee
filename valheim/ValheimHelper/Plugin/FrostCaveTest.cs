using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_frostcavetest (⑪ 산 4) 첫 조각, 사용자 결정 2026-10-23 "해 봄"): 서리 동굴(산 던전)에 무엇이 나오는지 게임 데이터로 확인한다.
    /// 거점 110m 앞에 산 동굴 위치를 바닐라 TestSpawnLocation으로 세우고(월드 저장은 꺼짐 — CryptTest와 같음), 바깥 열쇠 문·입구 Teleport와
    /// 안(y &gt; 3000)의 물체를 프리팹별로 세어 ResourceCatalog가 보는 것(도구·단계·나오는 것)·Door·Container·생물 스포너를 로그에 남긴다.
    /// 결과로 Dungeons 일반화(지하묘지와 같은 처리)를 정한다.
    /// </summary>
    static class FrostCaveTest
    {
        public static void Register() => TestRunner.Register("vh_frostcavetest", "서리 동굴 내용 확인 시험", () => TestKit.Flat(Run()));

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
            var before = new HashSet<ZDOID>();
            var spot = new Vector3[1];
            try { yield return Body(w, checks, before, spot); }
            finally
            {
                CryptTest.Cleanup(before, spot[0]);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, HashSet<ZDOID> before, Vector3[] spot)
        {
            w.CountsCreatures = true; // 동굴 안 생물 스포너가 생물을 만들 수 있음
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;

            var caves = ZoneSystem.instance.m_locations.Where(l => l.m_prefabName != null && l.m_prefabName.Contains("Cave")).ToList();
            TestRunner.Log("Cave 위치 이름: " + string.Join(", ", caves.Select(l => $"{l.m_prefabName}({l.m_biome}{(l.m_enable ? "" : ",꺼짐")})").Distinct()));
            string name = caves.Where(l => l.m_enable && (l.m_biome & Heightmap.Biome.Mountain) != 0)
                .Select(l => l.m_prefabName).OrderByDescending(n => n).FirstOrDefault();
            checks.Add($"산 동굴 위치 이름 있음 ({name})", name != null);
            if (name == null) yield break;

            Vector3 pos = GatherTest.Ground(w.Home + w.Player.transform.rotation * new Vector3(0f, 0f, 110f));
            spot[0] = pos;
            foreach (var z in CryptTest.Around(pos)) before.Add(z.m_uid);
            bool spawned = ZoneSystem.instance.TestSpawnLocation(name, pos, disableSave: true);
            checks.Add($"동굴 세움(TestSpawnLocation, 깃발에서 {Utils.DistanceXZ(pos, w.Home):0}m)", spawned);
            if (!spawned) yield break;
            yield return new WaitForSeconds(4f);

            // 바깥: 열쇠 문·입구 Teleport
            ZDO door = Dungeons.KeyDoor(pos);
            string doorName = door != null ? ZNetScene.instance.GetPrefab(door.GetPrefab())?.name : null;
            TestRunner.Log($"바깥 열쇠 문: {doorName ?? "없음"}");
            Teleport gate = Dungeons.Entrance(pos);
            checks.Add($"입구 Teleport 찾음 ({(gate != null ? Utils.DistanceXZ(gate.transform.position, pos).ToString("0") + "m" : "없음")})", gate != null);

            // 안: 새로 생긴 ZDO(y > 3000)를 프리팹별로
            var counts = new Dictionary<string, int>();
            var notes = new Dictionary<string, string>();
            int inside = 0, resources = 0;
            foreach (var z in CryptTest.Around(pos))
            {
                if (before.Contains(z.m_uid) || !Character.InInterior(z.GetPosition())) continue;
                if (Utils.DistanceXZ(z.GetPosition(), pos) > 100f) continue;
                GameObject prefab = ZNetScene.instance.GetPrefab(z.GetPrefab());
                string n = prefab != null ? prefab.name : z.GetPrefab().ToString();
                inside++;
                counts[n] = counts.TryGetValue(n, out int c) ? c + 1 : 1;
                if (notes.ContainsKey(n)) continue;
                var parts = new List<string>();
                var info = ResourceCatalog.Get(z.GetPrefab());
                if (info != null)
                {
                    parts.Add($"자원 {info.Tool}{(info.Tool == ToolKind.None ? "" : " 단계" + info.MinToolTier)}{(info.Pickable ? " 줍기" : "")}{(info.Hidden ? " 숨음" : "")} → {string.Join(",", info.Produces)}");
                    resources++;
                }
                if (prefab != null)
                {
                    var d = prefab.GetComponent<Door>();
                    if (d != null) parts.Add("문" + (d.m_keyItem != null ? " 열쇠 " + Utils.GetPrefabName(d.m_keyItem.gameObject) : ""));
                    if (prefab.GetComponent<Container>() != null) parts.Add("상자");
                    if (prefab.GetComponent<Character>() != null) parts.Add("생물");
                    var cs = prefab.GetComponent<CreatureSpawner>();
                    if (cs != null && cs.m_creaturePrefab != null) parts.Add("스포너 " + cs.m_creaturePrefab.name);
                    if (prefab.GetComponent<Pickable>() != null && info == null) parts.Add("Pickable(목록 밖)");
                }
                notes[n] = string.Join(" · ", parts);
            }
            foreach (var kv in counts.OrderByDescending(k => k.Value))
                TestRunner.Log($"  안 {kv.Key} ×{kv.Value}{(notes[kv.Key].Length > 0 ? "  [" + notes[kv.Key] + "]" : "")}");
            checks.Add($"안 물체 셈 ({inside}개, {counts.Count}종)", inside > 0);
            checks.Add($"안에 캐거나 줍는 것 있음 ({resources}종)", resources > 0);
        }
    }
}
