using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_hunttest (⑧ 사냥): "!RawMeat 2"(사냥만 되는 물건) → 거점 둘레 시야에 멧돼지 둘을 세워 두면 GatherJob 사냥 모드가
    /// 쫓아가 잡고, 떨어진 고기를 주워 거점 상자에 넣는지. 사냥감 목록(Prey)이 멧돼지→고기를 아는지도 본다.
    /// </summary>
    static class HuntTest
    {
        public static void Register() => TestRunner.Register("vh_hunttest", "사냥(멧돼지 → 고기) 시험", () => TestKit.Flat(Run()));

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
            var boars = new List<GameObject>();
            try { yield return Body(w, checks, boars); }
            finally
            {
                foreach (var b in boars) if (b != null) ZNetScene.instance.Destroy(b);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> boars)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            GameObject prefab = ZNetScene.instance.GetPrefab("Boar");
            checks.Add("멧돼지 프리팹", prefab != null);
            var ways = ResourceCatalog.WaysFor("RawMeat");
            checks.Add("고기는 사냥으로만 얻음(Hunt)", ways.Count == 1 && ways.Contains(ToolKind.Hunt));
            checks.Add("멧돼지는 사냥감", prefab != null && Prey.Is("RawMeat", prefab.GetComponent<Character>()));
            if (prefab == null) yield break;

            TestKit.ClearBaseStock(w, "RawMeat");
            Quaternion rot = w.Player.transform.rotation;
            for (int i = 0; i < 2; i++)
            {
                Vector3 pos = GatherTest.Ground(w.Home + rot * new Vector3(i * 6f - 3f, 0f, 25f + i * 6f));
                GameObject go = Object.Instantiate(prefab, pos + Vector3.up * 0.5f, Quaternion.identity);
                go.GetComponent<Character>().SetLevel(1);
                boars.Add(go);
            }
            GatherTest.Say("!RawMeat 2", null);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is GatherJob g && (g.StepName == "Hunt" || g.StepName == "Collect"), 90f);
            checks.Add($"사냥 시작 ({TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "RawMeat") >= 2, 150f);
            checks.Add($"고기 2개 이상이 상자에 ({GatherTest.CountBase(w.Home, "RawMeat")}개, {TestKit.Waited:0}초)", TestKit.Ok);
            int dead = 0;
            foreach (var b in boars) if (b == null || b.GetComponent<Character>().IsDead()) dead++;
            checks.Add($"멧돼지 죽음 ({dead}/2)", dead >= 1);
            checks.Add($"체력 남음 ({w.Ragnar.GetHealth():0})", w.Ragnar.GetHealth() > 0f);
            TestKit.ClearBaseStock(w, "RawMeat");
        }
    }
}
