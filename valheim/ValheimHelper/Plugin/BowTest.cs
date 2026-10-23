using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_bowtest (⑧ 활 사냥): 사슴 가죽(DeerHide)은 사슴(AnimalAI, 도망만 함)에게서만 나와 활이 있어야 잡는다.
    /// 1) 활이 없으면 "!DeerHide 1"을 곧 그만두고 needBow 대사. 2) 거점 상자에 활+화살을 넣으면 Prep이 챙기고, 30m 안에서 쏘아 잡아
    /// 가죽을 거점 상자에 넣는지. 사냥감 목록(Prey)이 사슴을 활 사냥감으로 아는지도 본다.
    /// </summary>
    static class BowTest
    {
        public static void Register() => TestRunner.Register("vh_bowtest", "활 사냥(사슴 → 가죽) 시험", () => TestKit.Flat(Run()));

        static readonly string[] Stock = { "Bow", "ArrowWood", "DeerHide" };

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
            var deer = new List<GameObject>();
            try { yield return Body(w, checks, deer); }
            finally
            {
                foreach (var d in deer) if (d != null) ZNetScene.instance.Destroy(d);
                if (w.Home != Vector3.zero) TestKit.ClearBaseStock(w, Stock);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> deer)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            GameObject prefab = ZNetScene.instance.GetPrefab("Deer");
            checks.Add("사슴 프리팹", prefab != null);
            if (prefab == null) yield break;
            Character deerCh = prefab.GetComponent<Character>();
            var ways = ResourceCatalog.WaysFor("DeerHide");
            checks.Add($"가죽은 사냥으로만 얻음(Hunt; 캐는 곳 {string.Join(",", ResourceCatalog.SourcesOf("DeerHide").ToArray())})", ways.Count == 1 && ways.Contains(ToolKind.Hunt));
            checks.Add("사슴은 활 사냥감", Prey.NeedsBow(deerCh) && Prey.Is("DeerHide", deerCh, true) && !Prey.Is("DeerHide", deerCh, false));
            checks.Add("멧돼지는 활 없이도 사냥감", !Prey.NeedsBow(ZNetScene.instance.GetPrefab("Boar").GetComponent<Character>()));
            TestKit.ClearBaseStock(w, Stock);

            // 1) 활 없음 → 곧 그만두고 needBow
            int heard = w.Heard.Count;
            GatherTest.Say("!DeerHide 1", null);
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob), 60f);
            checks.Add($"활 없으면 곧 그만둠 ({TestKit.Waited:0}초)", TestKit.Ok);
            bool said = false;
            for (int i = heard; i < w.Heard.Count; i++) if (w.Heard[i].Contains("활")) said = true;
            checks.Add("활이 필요하다고 말함", said);

            // 2) 활+화살을 주고 사슴을 25m 앞에 세움
            TestKit.PutInChest(w.Chest, "Bow", 1);
            TestKit.PutInChest(w.Chest, "ArrowWood", 60);
            // 지난 시험·야생 사슴을 치운다(남은 사슴에 쏘면 판정이 흐려짐)
            int old = 0;
            foreach (Character c in new List<Character>(Character.GetAllCharacters()))
                if (c != null && Utils.GetPrefabName(c.gameObject) == "Deer" && Utils.DistanceXZ(c.transform.position, w.Home) < 150f)
                { ZNetScene.instance.Destroy(c.gameObject); old++; }
            TestRunner.Log($"남은 사슴 {old}마리 치움");
            Quaternion rot = w.Player.transform.rotation;
            Vector3 pos = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 25f));
            GameObject go = Object.Instantiate(prefab, pos + Vector3.up * 0.5f, Quaternion.identity);
            go.GetComponent<Character>().SetLevel(1);
            deer.Add(go);
            GatherTest.Say("!DeerHide 1", null);
            bool heldBow = false;
            WorkerNpc hunter = null;
            yield return TestKit.WaitFor(() =>
            {
                if (hunter == null) hunter = w.Ragnar.Ai.Job is GatherJob ? w.Ragnar : w.Floki.Ai.Job is GatherJob ? w.Floki : null;
                if (hunter != null && hunter.HoldingBow()) heldBow = true;
                return GatherTest.CountBase(w.Home, "DeerHide") >= 1;
            }, 180f);
            checks.Add($"가죽이 상자에 ({GatherTest.CountBase(w.Home, "DeerHide")}개, {TestKit.Waited:0}초)", TestKit.Ok);
            checks.Add($"활을 들고 쏨({hunter?.NpcName})", heldBow);
            if (hunter == null) hunter = w.Ragnar;
            int arrows = Gear.CountArrows(hunter.GetInventory(), Gear.BestBow(hunter.GetInventory().GetAllItems()));
            checks.Add($"활·화살을 지님(화살 {arrows}개, 몫 {Core.Npc.Archery.ArrowsToCarry} 이하)", arrows > 0 && arrows <= Core.Npc.Archery.ArrowsToCarry);
            checks.Add($"사슴 죽음", go == null || go.GetComponent<Character>().IsDead());

        }
    }
}
