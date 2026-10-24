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

            // ⑪ 4다) "!수정 3" → 열쇠 문 없는 동굴 입구로 들어가 수정을 캐고 나와 거점 상자에 넣는다(박쥐·울브 스포너, 얼음 종유석이 길을 막는지도 봄)
            if (gate == null) yield break;
            bool listed = Dungeons.All("Crystal").Any(c => Utils.DistanceXZ(c.Pos, pos) < 64f && c.Keyless && c.Left > 0);
            checks.Add("Dungeons.All(수정)이 이 동굴을 열린 곳으로 셈", listed);
            TestKit.ClearBaseStock(w, "Crystal");
            // 약식: 거점에서 110m 가는 길은 빼고 입구 12m 앞에서 시작(CryptTest와 같음)
            Vector3 start = TestRunner.Quick ? GatherTest.Ground(gate.transform.position + Vector3.Scale(w.Home - gate.transform.position, new Vector3(1f, 0f, 1f)).normalized * 12f) : w.Home;
            w.Ragnar.TeleportTo(start, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(1.5f);
            int stockBefore = GatherTest.CountBase(w.Home, "Crystal");
            GatherTest.Say($"!{w.Ragnar.NpcName} 수정 3", null);
            bool wasInside = false;
            float t = 0f, inAt = -1f, outAt = -1f;
            while (t < 480f && GatherTest.CountBase(w.Home, "Crystal") - stockBefore < 3)
            {
                yield return new WaitForSeconds(1f);
                t += 1f;
                if (w.Ragnar.InInterior()) { wasInside = true; if (inAt < 0f) inAt = t; }
                else if (wasInside && outAt < 0f) outAt = t;
            }
            int got = GatherTest.CountBase(w.Home, "Crystal") - stockBefore;
            TestRunner.Log($"수정 일: {t:0}초(들어감 {inAt:0}초·나옴 {outAt:0}초{(TestRunner.Quick ? ", 약식 입구 앞 시작" : "")}), 상자에 수정 {got}개, 단계 {(w.Ragnar.Ai.Job is GatherJob g ? g.StepName : "-")}, 체력 {w.Ragnar.GetHealth():0}/{w.Ragnar.GetMaxHealth():0}");
            checks.Add("일꾼이 동굴 안으로 들어감", wasInside);
            checks.Add($"수정이 거점 상자에 들어옴 ({got}개, {t:0}초)", got >= 3);
            yield return new WaitForSeconds(3f);
            checks.Add("일꾼이 안에 남지 않음", !w.Ragnar.InInterior());
        }
    }
}
