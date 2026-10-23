using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_silvertest (⑪ 산 1조각): 은이 나오는 것 중 숨는 광맥(Beacon)을 게임 데이터에서 찾고(이름·윗면 높이·도구 단계·Beacon 범위를 diag에),
    /// 찾는 물건(위시본, SE_Finder)을 알아내는지, 거점 앞에 묻힌 광맥·드러난 광맥을 하나씩 세워 드러났는지 판단과
    /// "위시본을 얻었을 때만 묻힌 것을 안다"(HiddenOres.Known)를 본다. 열쇠 상태처럼 위시본 얻음(알려진 재료·보스 월드 키)은 끝에 되돌린다.
    /// </summary>
    static class SilverTest
    {
        public static void Register() => TestRunner.Register("vh_silvertest", "은 광맥 시험", () => TestKit.Flat(Run()), 1400f); // 조각 92개를 다 캐면 15~20분(회차 117)

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
            var keys = new CryptTest.KeyState();
            try { yield return Body(w, checks, made, keys); }
            finally
            {
                GatherJob.TestIgnorePlayers = false;
                foreach (var go in made)
                    if (go != null) ZNetScene.instance.Destroy(go);
                keys.Restore();
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made, CryptTest.KeyState keys)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;

            const string ore = "SilverOre";
            GameObject vein = null;
            ResourceInfo info = null;
            foreach (string src in ResourceCatalog.SourcesOf(ore))
            {
                int cut = src.IndexOf('(');
                GameObject p = ZNetScene.instance.GetPrefab(cut > 0 ? src.Substring(0, cut) : src);
                ResourceInfo r = p != null ? ResourceCatalog.Get(p.name.GetStableHashCode()) : null;
                if (r == null) continue;
                Beacon b = p.GetComponentInChildren<Beacon>(true);
                TestRunner.Log($"은 출처 {src}: 숨음 {r.Hidden}, 윗면 {r.TopOffset:0.00}m, 도구 {r.Tool} 단계 {r.MinToolTier}, Beacon {(b != null ? b.m_range.ToString("0") + "m" : "없음")}, 부품 {Parts(p)}");
                if (r.Hidden && vein == null) { vein = p; info = r; }
            }
            checks.Add($"은 숨는 광맥 찾음 ({(vein != null ? vein.name : "없음")})", vein != null);
            if (vein == null) yield break;

            ItemDrop finder = HiddenOres.Finder;
            checks.Add($"찾는 물건 찾음 ({(finder != null ? Utils.GetPrefabName(finder.gameObject) : "없음")})", finder != null);
            if (finder == null) yield break;
            keys.Save(finder);
            checks.Add($"찾는 물건을 떨구는 보스 월드 키 ({keys.BossKey ?? "없음"}, 시험 전 주움 {keys.HadKnown}·월드 키 {keys.HadGlobal})", keys.BossKey != null);

            // 묻힌 것: 윗면이 땅 1m 아래. 드러난 것: 윗면이 땅 1.5m 위. 거점 앞 30m·40m.
            Quaternion rot = w.Player.transform.rotation;
            Vector3 g1 = GatherTest.Ground(w.Home + rot * new Vector3(-6f, 0f, 30f));
            Vector3 g2 = GatherTest.Ground(w.Home + rot * new Vector3(6f, 0f, 40f));
            GameObject buried = Object.Instantiate(vein, g1 - Vector3.up * (info.TopOffset + 1f), Quaternion.identity);
            GameObject shown = Object.Instantiate(vein, g2 - Vector3.up * (info.TopOffset - 1.5f), Quaternion.identity);
            made.Add(buried);
            made.Add(shown);
            yield return new WaitForSeconds(1f);
            ZDO zb = buried.GetComponent<ZNetView>()?.GetZDO(), zs = shown.GetComponent<ZNetView>()?.GetZDO();
            checks.Add("광맥 둘 세움", zb != null && zs != null);
            if (zb == null || zs == null) yield break;
            checks.Add("묻힌 광맥은 안 드러남", !HiddenOres.Exposed(zb, info));
            checks.Add("땅 위로 나온 광맥은 드러남", HiddenOres.Exposed(zs, info));

            keys.SetKnown(false);
            if (keys.Global) keys.SetGlobal(false);
            yield return TestKit.WaitFor(() => !keys.Global, 5f);
            checks.Add("위시본 없으면 묻힌 광맥은 모름", !HiddenOres.HaveFinder() && !HiddenOres.Known(zb, info));
            checks.Add("위시본 없어도 드러난 광맥은 앎", HiddenOres.Known(zs, info));

            keys.SetKnown(true);
            checks.Add("위시본을 주웠으면 묻힌 광맥도 앎", HiddenOres.Known(zb, info));
            keys.SetKnown(false);
            if (keys.BossKey != null)
            {
                keys.SetGlobal(true);
                yield return TestKit.WaitFor(() => keys.Global, 5f);
                checks.Add($"보스가 잡혔으면(월드 키) 묻힌 광맥도 앎 ({TestKit.Waited:0.0}초)", HiddenOres.Known(zb, info));
            }

            // ⑪ 2·요청 R5·E021 라: 묻힌 은 캐기 — 위시본을 얻은 채 "!은 300", 플레이어 멀리(TestIgnorePlayers)라 땅은 안 파고 조각을 바로 쳐 남김없이 캐고 집으로.
            // (근처 파 내려가기는 vh_pittest·vh_coppertest가 봄)
            ZNetScene.instance.Destroy(shown);
            keys.SetKnown(true);
            // 단계가 맞는 가장 낮은 곡괭이(회차 107: 청동은 "도구 단계 낮음" → 게임 데이터로 고름)
            string pick = null;
            var tiers = new List<string>();
            foreach (string n in new[] { "PickaxeAntler", "PickaxeBronze", "PickaxeIron", "PickaxeBlackMetal" })
            {
                int tier = ObjectDB.instance.GetItemPrefab(n)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_toolTier ?? -1;
                tiers.Add($"{n} {tier}");
                if (pick == null && tier >= info.MinToolTier) pick = n;
            }
            TestRunner.Log($"곡괭이 단계: {string.Join(", ", tiers)} → {pick ?? "없음"}(광맥 단계 {info.MinToolTier})");
            checks.Add("단계 맞는 곡괭이 있음", pick != null);
            if (pick == null) yield break;
            // 둘: 은 광맥 하나에 곡괭이 하나가 닳아 없어짐(회차 107: 29개 캐고 "연장 나갔네") — 예비는 짐 정리 때 상자로 갔다가 Tool 단계가 꺼냄
            // 회차 117: 철 곡괭이 2개로는 부위 약 42개뿐(조각 92개) → 5개
            for (int i = 0; i < 5; i++) w.Ragnar.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(pick), 1);
            yield return new WaitForSeconds(1f);
            var hashes = new HashSet<int> { vein.name.GetStableHashCode() };
            var des = vein.GetComponent<Destructible>();
            if (des != null && des.m_spawnWhenDestroyed != null) hashes.Add(des.m_spawnWhenDestroyed.name.GetStableHashCode());
            if (TestRunner.Quick)
            {
                // 약식: 광맥을 미리 깨고 맨 밑 부위 몇 개만 남긴다(같은 칸 — 남김없이·맨 밑 조각·나옴·개수 보존 — 을 짧게). 미리 깬 은은 치움.
                yield return PreBreak(buried, g1, hashes, ore, 6);
                checks.Add($"약식: 미리 깨고 맨 밑 부위만 남김(광맥 {Fragments(g1, hashes, ref s_unused)}개·부위 {s_parts}개)", s_parts > 0);
                if (s_parts == 0) yield break;
            }
            int dug0 = NpcPatches.NoDig.Dug, blocked0 = NpcPatches.NoDig.Blocked;
            int carried0 = w.Ragnar.CountItem(ore), base0 = GatherTest.CountBase(w.Home, ore);
            GatherJob.TestIgnorePlayers = true;
            GatherTest.Say($"!{Workers.Name1} 은 300", null);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Current == WorkerAI.Mode.Work, 10f);
            checks.Add($"\"은 300\"에 캐러 나섬({TestKit.Waited:0}초)", TestKit.Ok);
            float t = 0f, deepest = 0f;
            int left = Fragments(g1, hashes, ref deepest);
            for (; t < 1200f && left > 0 && w.Ragnar != null && w.Ragnar.Ai.Current == WorkerAI.Mode.Work; t += 5f)
            {
                yield return new WaitForSeconds(5f);
                left = Fragments(g1, hashes, ref deepest);
                if ((int)t % 30 == 0)
                    TestRunner.Log($"[{t:0}s] 은 광맥 {left}개·남은 부위 {s_parts}개(가장 깊은 조각 바닥 {deepest:0.0}m), 든 은 {w.Ragnar.CountItem(ore)}, 상태 {w.Ragnar.Ai.Current}/{w.Ragnar.Ai.Job?.StepName}, 판 {NpcPatches.NoDig.Dug - dug0}번");
            }
            int dug = NpcPatches.NoDig.Dug - dug0;
            TestRunner.Log($"묻힌 은: {t:0}초, 땅 파기 판 {dug}번·막음 {NpcPatches.NoDig.Blocked - blocked0}번, 구덩이 가장 깊은 곳 {GatherTest.Deepest(g1, 4f):0.0}m, 조각 바닥 가장 깊음 {deepest:0.0}m");
            checks.Add($"묻힌 은 광맥 조각 다 캠(남은 {left}개, {t:0}초)", left == 0);
            checks.Add($"멀리라 땅 안 팜(판 {dug}번)", dug == 0);
            // 다 캐면 떨어진 은을 주울 때까지 기다린다(회차 118: 바로 "멈춰"라 은 80개가 땅에 남음 — 시험 탓)
            yield return TestKit.WaitFor(() => w.Ragnar == null || w.Ragnar.Ai.Current != WorkerAI.Mode.Work || GatherTest.GroundCount(g1, ore) == 0, 180f);
            TestRunner.Log($"다 캔 뒤 줍기: {TestKit.Waited:0}초, 땅에 남은 은 {GatherTest.GroundCount(g1, ore)}개, 상태 {w.Ragnar?.Ai.Current}");
            if (w.Ragnar != null && w.Ragnar.Ai.Current == WorkerAI.Mode.Work) GatherTest.Say($"!{Workers.Name1} 멈춰", null);
            yield return TestKit.WaitFor(() => w.Ragnar == null || Utils.DistanceXZ(w.Ragnar.Home, w.Ragnar.transform.position) <= 5f, 120f);
            checks.Add($"구덩이에서 나와 집으로 돌아옴({TestKit.Waited:0}초)", TestKit.Ok);
            int got = (w.Ragnar != null ? w.Ragnar.CountItem(ore) : 0) - carried0 + GatherTest.CountBase(w.Home, ore) - base0;
            int ground = GatherTest.GroundCount(g1, ore);
            checks.Add($"캔 은을 다 주움(짐·상자 {got}개, 땅에 남음 {ground}개)", got > 0 && ground == 0);
            checks.Add($"죽지 않음({(w.Ragnar != null ? w.Ragnar.Deaths : -1)})", w.Ragnar != null && w.Ragnar.Deaths == 0);
            GatherTest.RemoveDeposits(g1, vein);
            TestRunner.Log($"구덩이 되돌림: 꼭짓점 {TestTerrain.Reset(g1, 10f)}개");
        }

        static int s_parts;
        static float s_unused;

        /// <summary>약식 준비: 통짜 광맥(Destructible)을 깨 조각(MineRock5)으로 만들고, 바닥이 가장 낮은 keep개를 뺀 부위를 큰 곡괭이 피해로 깬 뒤 떨어진 광석을 치운다.</summary>
        static IEnumerator PreBreak(GameObject vein, Vector3 at, HashSet<int> hashes, string ore, int keep)
        {
            HitData Big(Vector3 p, Collider c) => new HitData { m_damage = new HitData.DamageTypes { m_pickaxe = 100000f }, m_toolTier = 100, m_point = p, m_dir = Vector3.down, m_hitCollider = c };
            if (vein != null) vein.GetComponent<Destructible>()?.Damage(Big(vein.transform.position, null));
            yield return new WaitForSeconds(1f);
            int broke = 0;
            foreach (var zdo in Senses.Nearby(at, 10f))
            {
                if (!hashes.Contains(zdo.GetPrefab())) continue;
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                var rock = v != null ? v.GetComponent<MineRock5>() : null;
                if (rock == null) continue;
                var cols = new List<Collider>();
                foreach (var col in v.GetComponentsInChildren<Collider>())
                {
                    Transform up = col.transform.parent; // 부위 충돌체만(MineRock5.Damage와 같은 거름)
                    if (!col.isTrigger && col.enabled && (up == rock.transform || (up != null && up.parent == rock.transform))) cols.Add(col);
                }
                cols.Sort((a, b) => b.bounds.min.y.CompareTo(a.bounds.min.y)); // 위 → 아래
                // 받침 검사를 끄고 깬다: 땅 표면에 닿은 윗 부위가 없어지면 묻힌 아래 부위가 받침 없음으로 한꺼번에 부서진다(첫 실행에서 부위 다 사라짐).
                // 일꾼이 남은 부위를 치면 받침 검사가 다시 돌아 남은 것이 같이 부서질 수 있다(땅속에 은이 떨어짐 = 맨 밑 줍기 칸).
                rock.m_supportCheck = false;
                for (int i = 0; i < cols.Count - keep; i++)
                {
                    rock.Damage(Big(cols[i].bounds.center, cols[i]));
                    broke++;
                    if (v == null || !v.IsValid()) break;
                }
                if (rock != null) rock.m_supportCheck = true;
            }
            yield return new WaitForSeconds(2f);
            int cleared = 0;
            foreach (var item in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
                if (item != null && Utils.GetPrefabName(item.gameObject) == ore && Utils.DistanceXZ(item.transform.position, at) < 12f)
                {
                    ZNetScene.instance.Destroy(item.gameObject);
                    cleared++;
                }
            TestRunner.Log($"약식 준비: 부위 {broke}개 미리 깸, 떨어진 은 {cleared}개 치움");
        }

        /// <summary>at 10m 안 은 광맥(통짜·깨진 조각) ZDO 수(s_parts = 불러온 것의 남은 부위 충돌체 수). deepest = 불러온 조각 충돌체 바닥이 원래 땅에서 가장 깊은 정도(m, 커지기만).</summary>
        static int Fragments(Vector3 at, HashSet<int> hashes, ref float deepest)
        {
            int n = 0;
            s_parts = 0;
            foreach (var zdo in Senses.Nearby(at, 10f))
            {
                if (!hashes.Contains(zdo.GetPrefab())) continue;
                n++;
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                if (v == null) continue;
                foreach (var col in v.GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger || !col.enabled) continue;
                    s_parts++;
                    Vector3 c = col.bounds.center;
                    deepest = Mathf.Max(deepest, WorldGenerator.instance.GetHeight(c.x, c.z) - col.bounds.min.y);
                }
            }
            return n;
        }

        /// <summary>진단: 프리팹의 바닐라 부품(Destructible·MineRock5 등)과 깨지면 생기는 것.</summary>
        static string Parts(GameObject p)
        {
            var names = new List<string>();
            foreach (var c in p.GetComponents<MonoBehaviour>())
                if (c != null) names.Add(c.GetType().Name);
            var des = p.GetComponent<Destructible>();
            if (des != null && des.m_spawnWhenDestroyed != null) names.Add("→" + des.m_spawnWhenDestroyed.name);
            return string.Join(",", names);
        }
    }
}
