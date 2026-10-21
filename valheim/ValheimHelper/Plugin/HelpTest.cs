using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_helptest (5번 도와줘요): 위급해 물러나거나 도망칠 때 60m 안에 플레이어가 있으면 "도와줘요" 대사를 하는지.
    /// 트롤을 일꾼 앞에 세운다(플레이어는 거점, 갓 모드·유령 아님).
    /// </summary>
    static class HelpTest
    {
        public static void Register() => TestRunner.Register("vh_helptest", "도와줘요 시험", () => TestKit.Flat(Run()));

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
            var spawned = new List<GameObject>();
            bool god = Player.m_localPlayer.InGodMode();
            Player.m_localPlayer.SetGodMode(true);
            try { yield return Body(w, checks, spawned); }
            finally
            {
                Cleanup(spawned);
                Player.m_localPlayer.SetGodMode(god);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void Cleanup(List<GameObject> spawned)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Claude != null);
            GameObject troll = ZNetScene.instance.GetPrefab("Troll");
            checks.Add("트롤 프리팹 있음", troll != null);
            if (w.Claude == null || troll == null) yield break;
            var ai = w.Claude.Ai;
            Quaternion rot = w.Player.transform.rotation;
            Vector3 arena = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 30f));
            w.Claude.TeleportTo(arena + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            float dp = Vector3.Distance(w.Claude.transform.position, w.Player.transform.position);
            checks.Add($"플레이어가 60m 안 ({dp:0}m)", dp <= Guard.HelpRange);

            Vector3 pos = GatherTest.Ground(w.Claude.transform.position + rot * new Vector3(0f, 0f, 20f));
            GameObject go = Object.Instantiate(troll, pos + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Add(go);
            go.GetComponent<Character>().SetLevel(1);
            yield return TestKit.WaitFor(() => w.Heard.Exists(h => h.Contains("도와") || h.Contains("살려") || h.Contains("따라와")), 30f);
            checks.Add($"위급하자 도와달라고 말함 ({TestKit.Waited:0}초)", TestKit.Ok);
            checks.Add("물러남 또는 도망 상태", ai.Guard != null && (ai.Guard.StateName == "Back" || ai.Guard.StateName == "Flee"));
            checks.Add("살아 있음", !w.Claude.IsDead() && w.Claude.GetHealth() > 0f);
        }
    }
}
