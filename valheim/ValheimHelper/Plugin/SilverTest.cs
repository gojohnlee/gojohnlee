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
        public static void Register() => TestRunner.Register("vh_silvertest", "은 광맥 시험", () => TestKit.Flat(Run()));

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
            checks.Add("깃발·NPC", w.Claude != null);
            if (w.Claude == null) yield break;

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
