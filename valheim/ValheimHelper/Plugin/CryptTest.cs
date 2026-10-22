using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_crypttest (⑦ 철 1조각): 거점 110m 앞에 늪 지하묘지를 바닐라 TestSpawnLocation으로 세우고(월드 저장은 다시 켤 때까지 꺼짐 — 시험 월드 보호),
    /// Dungeons가 찾는지·닫힌 문은 안 고르는지·문을 열면 고르는지, 입구 Teleport로 일꾼이 안(y &gt; 3000)에 들어갔다 나오는지 본다.
    /// 안에서 고철이 나오는 물체(이름·종류·도구 단계)를 diag에 적는다(다음 조각: 진흙 고철 더미 캐기).
    /// </summary>
    static class CryptTest
    {
        public static void Register() => TestRunner.Register("vh_crypttest", "지하묘지 시험", () => TestKit.Flat(Run()));

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
                if (w.Claude != null) w.Claude.TeleportTo(w.Home, w.Claude.transform.rotation, false);
                Cleanup(before, spot[0]);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static List<ZDO> Around(Vector3 at)
        {
            var list = new List<ZDO>();
            if (at != Vector3.zero) ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), new SimulationDistance(1, 0), list);
            return list;
        }

        /// <summary>세운 지하묘지(시험 전에 없던 ZDO 중 70m 안, 일꾼·플레이어 빼고)를 지운다.</summary>
        static void Cleanup(HashSet<ZDOID> before, Vector3 at)
        {
            if (at == Vector3.zero) return;
            int n = 0;
            foreach (var z in Around(at))
            {
                if (before.Contains(z.m_uid) || Utils.DistanceXZ(z.GetPosition(), at) > 70f) continue;
                var inst = ZNetScene.instance.FindInstance(z);
                if (inst != null && (inst.GetComponent<WorkerNpc>() != null || inst.GetComponent<Player>() != null)) continue;
                ZDOMan.instance.DestroyZDO(z);
                n++;
            }
            TestRunner.Log($"정리: 세운 지하묘지 물체 {n}개 지움");
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, HashSet<ZDOID> before, Vector3[] spot)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Claude != null);
            if (w.Claude == null) yield break;

            var names = ZoneSystem.instance.m_locations.Where(l => l.m_prefabName != null && l.m_prefabName.Contains("Crypt"))
                .Select(l => $"{l.m_prefabName}({l.m_biome})").Distinct().ToList();
            TestRunner.Log("Crypt 위치 이름: " + string.Join(", ", names));
            string name = ZoneSystem.instance.m_locations.Where(l => l.m_enable && l.m_prefabName != null && l.m_prefabName.Contains("SunkenCrypt"))
                .Select(l => l.m_prefabName).OrderByDescending(n => n).FirstOrDefault();
            checks.Add($"늪 지하묘지 위치 이름 있음 ({name})", name != null);
            if (name == null) yield break;

            Quaternion rot = w.Player.transform.rotation;
            Vector3 pos = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 110f));
            spot[0] = pos;
            foreach (var z in Around(pos)) before.Add(z.m_uid);
            bool spawned = ZoneSystem.instance.TestSpawnLocation(name, pos, disableSave: true);
            checks.Add("지하묘지 세움(TestSpawnLocation)", spawned);
            if (!spawned) yield break;
            yield return new WaitForSeconds(4f);

            Dungeons.Crypt mine = default;
            bool found = false;
            foreach (var c in Dungeons.All())
                if (Utils.DistanceXZ(c.Pos, pos) < 64f) { mine = c; found = true; }
            checks.Add($"Dungeons.All이 찾음 ({(found ? mine.Name + " " + Utils.DistanceXZ(mine.Pos, pos).ToString("0") + "m" : "없음")})", found);
            if (!found) yield break;
            string doorName = mine.Door != null ? ZNetScene.instance.GetPrefab(mine.Door.GetPrefab())?.name : null;
            checks.Add($"입구 열쇠 문 찾음 ({doorName}), 처음엔 닫힘", mine.Door != null && !mine.Open);
            if (mine.Door == null) yield break;
            bool chosen = Dungeons.TryChoose(w.Home, out var pick) && Utils.DistanceXZ(pick.Pos, mine.Pos) < 1f;
            checks.Add("닫힌 지하묘지는 안 고름", !chosen);

            ZNetView doorView = ZNetScene.instance.FindInstance(mine.Door);
            if (doorView != null) doorView.ClaimOwnership();
            mine.Door.Set(ZDOVars.s_state, 1);
            chosen = Dungeons.TryChoose(w.Home, out pick) && Utils.DistanceXZ(pick.Pos, mine.Pos) < 1f && pick.Open;
            checks.Add("문을 열면 고름", chosen);

            Teleport gate = Dungeons.Entrance(mine.Pos);
            checks.Add($"입구 Teleport 찾음 ({(gate != null ? Utils.DistanceXZ(gate.transform.position, mine.Pos).ToString("0") + "m" : "없음")})", gate != null);
            if (gate == null) yield break;
            Vector3 inside = Dungeons.ArrivePoint(gate, out _);
            TestRunner.Log($"입구 {gate.transform.position} → 안 {inside}");

            Dungeons.Use(w.Claude, gate);
            yield return TestKit.WaitFor(() => w.Claude.InInterior(), 10f);
            checks.Add($"일꾼이 안으로 들어감 (y {w.Claude.transform.position.y:0}, {TestKit.Waited:0.0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            yield return new WaitForSeconds(3f);
            checks.Add($"안에서 떨어지지 않음 (y {w.Claude.transform.position.y:0}, 도착점과 {Vector3.Distance(w.Claude.transform.position, inside):0.0}m)",
                w.Claude.InInterior() && Vector3.Distance(w.Claude.transform.position, inside) < 15f);
            // 안의 고철 물체 조사: ResourceCatalog(Destructible → 부서지면 생기는 것까지 따라감)가 고철을 낸다고 보는 것
            Vector3 here = w.Claude.transform.position;
            var seen = new Dictionary<string, int>();
            var all = new Dictionary<string, int>();
            foreach (var v in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (!v.IsValid() || !Character.InInterior(v.transform.position) || Utils.DistanceXZ(v.transform.position, here) > 120f) continue;
                string k = v.gameObject.name.Replace("(Clone)", "");
                all[k] = all.TryGetValue(k, out int c) ? c + 1 : 1;
                var info = ResourceCatalog.Get(v.GetZDO().GetPrefab());
                if (info == null || !info.Produces.Contains("IronScrap")) continue;
                var de = v.GetComponent<Destructible>();
                string key = $"{info.Prefab} [{info.Tool} 단계 {info.MinToolTier}, 체력 {(de != null ? de.m_health : 0f)}, 부서지면 {(de != null && de.m_spawnWhenDestroyed != null ? de.m_spawnWhenDestroyed.name : "-")}]";
                seen[key] = seen.TryGetValue(key, out int s) ? s + 1 : 1;
            }
            TestRunner.Log("안의 고철 물체: " + (seen.Count == 0 ? "없음" : string.Join(", ", seen.Select(p => p.Key + " ×" + p.Value))));
            TestRunner.Log($"안의 ZNetView 물체 {all.Values.Sum()}개: " + string.Join(", ", all.OrderByDescending(p => p.Value).Take(25).Select(p => p.Key + " ×" + p.Value)));
            checks.Add($"안에 고철 나오는 물체 있음 ({seen.Values.Sum()}개)", seen.Count > 0);

            Teleport exit = Dungeons.Exit(w.Claude.transform.position);
            checks.Add($"나가는 Teleport 찾음 ({(exit != null ? Vector3.Distance(exit.transform.position, here).ToString("0") + "m" : "없음")})", exit != null);
            if (exit == null) yield break;
            Dungeons.Use(w.Claude, exit);
            yield return TestKit.WaitFor(() => !w.Claude.InInterior(), 10f);
            float toGate = Utils.DistanceXZ(w.Claude.transform.position, gate.transform.position);
            checks.Add($"밖으로 나옴 (입구와 {toGate:0.0}m, {TestKit.Waited:0.0}초)", TestKit.Ok && toGate < 15f);
            yield return new WaitForSeconds(2f);
        }
    }
}
