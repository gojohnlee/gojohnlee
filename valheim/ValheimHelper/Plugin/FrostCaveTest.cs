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
        /// <summary>시험 종류: 서리 동굴(산) 또는 감염된 광산(⑬ 바), 안개땅 — 같은 몸, 위치·물건만 다름).</summary>
        sealed class Kind { public string Label, Item, ItemKo; public System.Func<ZoneSystem.ZoneLocation, bool> Pick; }
        static readonly Kind Cave = new Kind { Label = "동굴", Item = "Crystal", ItemKo = "수정", Pick = l => l.m_prefabName.Contains("Cave") && (l.m_biome & Heightmap.Biome.Mountain) != 0 };
        static readonly Kind Mine = new Kind { Label = "광산", Item = "BlackCore", ItemKo = "검은 핵", Pick = l => l.m_prefabName.Contains("DvergrTown") };
        static Kind s_kind = Cave;

        public static void Register()
        {
            TestRunner.Register("vh_frostcavetest", "서리 동굴 내용 확인 시험", () => { s_kind = Cave; return TestKit.Flat(Run()); });
            TestRunner.Register("vh_minetest", "감염된 광산(안개땅) 검은 핵 시험", () => { s_kind = Mine; return TestKit.Flat(Run()); });
        }

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

            var k = s_kind;
            var caves = ZoneSystem.instance.m_locations.Where(l => l.m_prefabName != null &&
                (k == Cave ? l.m_prefabName.Contains("Cave") : (l.m_biome & Heightmap.Biome.Mistlands) != 0)).ToList();
            TestRunner.Log($"{(k == Cave ? "Cave" : "안개땅")} 위치 이름: " + string.Join(", ", caves.Select(l => $"{l.m_prefabName}({l.m_biome}{(l.m_enable ? "" : ",꺼짐")})").Distinct()));
            string name = caves.Where(l => l.m_enable && k.Pick(l))
                .Select(l => l.m_prefabName).OrderByDescending(n => n).FirstOrDefault();
            checks.Add($"{k.Label} 위치 이름 있음 ({name})", name != null);
            if (name == null) yield break;

            Vector3 pos = GatherTest.Ground(w.Home + w.Player.transform.rotation * new Vector3(0f, 0f, 110f));
            spot[0] = pos;
            foreach (var z in CryptTest.Around(pos)) before.Add(z.m_uid);
            bool spawned = ZoneSystem.instance.TestSpawnLocation(name, pos, disableSave: true);
            checks.Add($"{s_kind.Label} 세움(TestSpawnLocation, 깃발에서 {Utils.DistanceXZ(pos, w.Home):0}m)", spawned);
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
                    if (info == null && Breakable(prefab) is string br) parts.Add(br);
                }
                notes[n] = string.Join(" · ", parts);
            }
            foreach (var kv in counts.OrderByDescending(k => k.Value))
                TestRunner.Log($"  안 {kv.Key} ×{kv.Value}{(notes[kv.Key].Length > 0 ? "  [" + notes[kv.Key] + "]" : "")}");
            checks.Add($"안 물체 셈 ({inside}개, {counts.Count}종)", inside > 0);
            checks.Add($"안에 캐거나 줍는 것 있음 ({resources}종)", resources > 0);

            // ⑪ 4다) "!{s_kind.ItemKo} 3" → 열쇠 문 없는 동굴 입구로 들어가 수정을 캐고 나와 거점 상자에 넣는다(박쥐·울브 스포너, 얼음 종유석이 길을 막는지도 봄)
            if (gate == null) yield break;
            yield return PathDiag(w, gate);
            bool listed =Dungeons.All(s_kind.Item).Any(c => Utils.DistanceXZ(c.Pos, pos) < 64f && c.Keyless && c.Left > 0);
            checks.Add($"Dungeons.All({s_kind.ItemKo})이 이 {s_kind.Label}을 열린 곳으로 셈", listed);
            TestKit.ClearBaseStock(w, s_kind.Item);
            // 약식: 거점에서 110m 가는 길은 빼고 입구 12m 앞에서 시작(CryptTest와 같음)
            Vector3 start = TestRunner.Quick ? GatherTest.Ground(gate.transform.position + Vector3.Scale(w.Home - gate.transform.position, new Vector3(1f, 0f, 1f)).normalized * 12f) : w.Home;
            w.Ragnar.TeleportTo(start, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(1.5f);
            int stockBefore = GatherTest.CountBase(w.Home, s_kind.Item);
            GatherTest.Say($"!{w.Ragnar.NpcName} {s_kind.ItemKo} 3", null);
            bool wasInside = false;
            float t = 0f, inAt = -1f, outAt = -1f;
            while (t < 480f && GatherTest.CountBase(w.Home, s_kind.Item) - stockBefore < 3)
            {
                yield return new WaitForSeconds(1f);
                t += 1f;
                if (w.Ragnar.InInterior()) { wasInside = true; if (inAt < 0f) inAt = t; }
                else if (wasInside && outAt < 0f) outAt = t;
            }
            int got = GatherTest.CountBase(w.Home, s_kind.Item) - stockBefore;
            TestRunner.Log($"{s_kind.ItemKo} 일: {t:0}초(들어감 {inAt:0}초·나옴 {outAt:0}초{(TestRunner.Quick ? ", 약식 입구 앞 시작" : "")}), 상자에 {s_kind.ItemKo} {got}개, 단계 {(w.Ragnar.Ai.Job is GatherJob g ? g.StepName : "-")}, 체력 {w.Ragnar.GetHealth():0}/{w.Ragnar.GetMaxHealth():0}");
            checks.Add($"일꾼이 {s_kind.Label} 안으로 들어감", wasInside);
            checks.Add($"{s_kind.ItemKo}이 거점 상자에 들어옴 ({got}개, {t:0}초)", got >= 3);
            yield return new WaitForSeconds(3f);
            checks.Add("일꾼이 안에 남지 않음", !w.Ragnar.InInterior());
        }

        /// <summary>진단: 자원 목록 밖인데 부술 수 있는 물체(Destructible·MineRock5·MineRock)의 바닐라 값.</summary>
        static string Breakable(GameObject p)
        {
            string Mods(HitData.DamageModifiers m) => $"곡괭이 {m.m_pickaxe}·둔기 {m.m_blunt}·베기 {m.m_slash}·찌르기 {m.m_pierce}·도끼 {m.m_chop}·불 {m.m_fire}";
            var d = p.GetComponent<Destructible>();
            if (d != null)
            {
                var drop = p.GetComponent<DropOnDestroyed>();
                var hurt = d.m_spawnWhenDestroyed != null ? d.m_spawnWhenDestroyed.GetComponentInChildren<ImpactEffect>(true) : null;
                return $"Destructible 체력 {d.m_health} 단계 {d.m_minToolTier} {Mods(d.m_damages)} 종류 {d.m_destructibleType}" +
                       $"{(d.m_spawnWhenDestroyed != null ? " → " + d.m_spawnWhenDestroyed.name + (hurt != null ? $"(부딪힘 피해 {hurt.m_damages.GetTotalDamage():0})" : "") : "")}" +
                       $"{(drop?.m_dropWhenDestroyed?.m_drops?.Count > 0 ? $" 떨굼 {drop.m_dropWhenDestroyed.m_drops.Count}종" : "")}" +
                       $", 길 막으면 {(ResourceCatalog.Obstacle(p.name.GetStableHashCode()) != null ? "깸" : "안 깸")}";
            }
            var r5 = p.GetComponent<MineRock5>();
            if (r5 != null) return $"MineRock5 체력 {r5.m_health} 단계 {r5.m_minToolTier} {Mods(r5.m_damageModifiers)} 떨굼 {r5.m_dropItems?.m_drops?.Count ?? 0}종";
            var r = p.GetComponent<MineRock>();
            if (r != null) return $"MineRock 체력 {r.m_health} 단계 {r.m_minToolTier} {Mods(r.m_damageModifiers)} 떨굼 {r.m_dropItems?.m_drops?.Count ?? 0}종";
            return null;
        }

        /// <summary>
        /// 진단(E031): 일꾼을 입구로 들여보내 도착 자리에서 수정마다 묘지 안 길(InteriorNav)이 있는지, 없으면 막은 것을 적고 다시 내보낸다.
        /// 동굴 방 배치는 세운 자리마다 달라(DungeonGenerator 씨앗 = 월드 씨앗 + 자리) 길이 막히는 배치가 따로 있다.
        /// </summary>
        static IEnumerator PathDiag(TestKit.World w, Teleport gate)
        {
            var dg = Object.FindObjectsByType<DungeonGenerator>(FindObjectsSortMode.None).FirstOrDefault(g => Utils.DistanceXZ(g.transform.position, gate.transform.position) < 80f);
            TestRunner.Log($"동굴 생성기 {(dg != null ? $"{dg.name} 씨앗 {dg.m_generatedSeed}" : "없음")}");
            Dungeons.Use(w.Ragnar, gate);
            yield return TestKit.WaitFor(() => w.Ragnar.InInterior(), 10f);
            if (!TestKit.Ok) { TestRunner.Log("진단: 안으로 못 들어감"); yield break; }
            yield return new WaitForSeconds(2f);
            Vector3 here = w.Ragnar.transform.position;
            InteriorNav.UseBody(w.Ragnar);
            var tool = WorkerNpc.BestTool(w.Ragnar.GetInventory(), ToolKind.Pickaxe);
            TestRunner.Log($"  일꾼 곡괭이 {(tool != null ? $"{tool.m_shared.m_name} 단계 {tool.m_shared.m_toolTier} 곡괭이 {tool.GetDamage().m_pickaxe:0}·둔기 {tool.GetDamage().m_blunt:0}" : "없음")}");
            int ok = 0, n = 0, explained = 0;
            foreach (var v in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None).Select(p => p.GetComponent<ZNetView>())
                .Where(v => v != null && v.IsValid() && Character.InInterior(v.transform.position) && Utils.DistanceXZ(v.transform.position, here) < 120f)
                .Where(v => ResourceCatalog.Get(v.GetZDO().GetPrefab())?.Produces.Contains(s_kind.Item) == true)
                .OrderBy(v => Utils.DistanceXZ(v.transform.position, here)))
            {
                n++;
                float reach = GatherJob.ReachOf(v, true);
                if (n == 1) TestRunner.Log($"  가장 가까운 것 닿을 거리 {reach:0.0}m, 둘레 1.5m 충돌체: " + string.Join(", ", Physics.OverlapSphere(v.transform.position, 1.5f, InteriorNav.Mask, QueryTriggerInteraction.Ignore)
                    .Select(c => $"{c.name}/{(c.transform.parent != null ? c.transform.parent.name : "-")}(반폭 {Mathf.Max(c.bounds.extents.x, c.bounds.extents.z):0.0}, 위 {c.bounds.max.y - v.transform.position.y:+0.0;-0.0}m)").Take(8)));
                var path = InteriorNav.Plan(here, v.transform.position, reach, out string why);
                if (path != null) ok++;
                TestRunner.Log($"  {s_kind.ItemKo} {Utils.DistanceXZ(v.transform.position, here):0}m 높이 {v.transform.position.y - here.y:+0.0;-0.0}: 길 {(path != null ? "있음" : "없음")} {why}"
                    + (path == null && explained++ < 3 ? " / " + InteriorNav.Explain(here, v.transform.position, reach) : ""));
                if (path == null && explained == 1) TestRunner.Log("  가장 가까운 것 둘레 칸:" + InteriorNav.AroundDiag(here, v.transform.position, reach + 1.5f));
                if (path == null && explained == 1) TestRunner.Log("  가장 가까운 것 쪽 지도(±14칸, 위 +z):" + InteriorNav.Map(here, 14, v.transform.position));
            }
            TestRunner.Log($"  도착 자리에서 길 있는 {s_kind.ItemKo} {ok}/{n}개");
            if (ok * 2 < n) TestRunner.Log("  도착 자리 둘레 지도(±14칸, 위 +z):" + InteriorNav.Map(here, 14));
            if (ok * 2 < n)
                foreach (var cl in Physics.OverlapSphere(here, 15f).Where(cl => cl.transform.parent != null && cl.transform.parent.name.Contains("gate_stair")).Take(4))
                    TestRunner.Log($"  계단 충돌체 {cl.name}<{cl.transform.parent.name} {cl.GetType().Name}{(cl is MeshCollider mc ? (mc.convex ? " 볼록" : " 오목") : "")} 층 {LayerMask.LayerToName(cl.gameObject.layer)} 크기 {cl.bounds.size} 가운데 {cl.bounds.center - here}");
            Teleport exit = Dungeons.Exit(here);
            if (exit != null) Dungeons.Use(w.Ragnar, exit);
            yield return TestKit.WaitFor(() => !w.Ragnar.InInterior(), 10f);
            yield return new WaitForSeconds(1f);
        }
    }
}
