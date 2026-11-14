using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_finaltest 최종 실전 시험(사용자 11-12, decisions 71) ① 조각: 시험 기본 집에서 한 번만 되돌린 뒤 채팅 명령을 하나씩 내려
    /// 명령마다 거점 상자 개수로 PASS/FAIL. 되돌리기·상자 채우기 없이 이어 간다. 지금은 나무·돌·구리·구리주괴(활·밭·굽기·가축·사냥은 다음 조각).
    /// </summary>
    static class FinalTest
    {
        public static void Register() => TestRunner.Register("vh_finaltest", "최종 실전 시험 ① 나무·돌·구리·구리주괴 채팅 명령(되돌림 한 번)", Run, 2400f);

        static bool s_dbgTime;
        static float s_dbgValue;

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            bool god = player.InGodMode();
            // 낮으로 고정(시작이 밤이면 귀환이 걸림 — GatherTest와 같은 까닭). End가 되돌림
            s_dbgTime = EnvMan.instance.m_debugTimeOfDay; s_dbgValue = EnvMan.instance.m_debugTime;
            EnvMan.instance.m_debugTimeOfDay = true; EnvMan.instance.m_debugTime = 0.5f;
            player.SetGodMode(true);
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;

            // 1) 시작 때 한 번만 되돌림: 시험 기본 집(te4st41)에 깃발·상자 2개
            var site = new HouseBase.Site();
            yield return HouseBase.Go(site);
            checks.Add($"시험 기본 집 씀 ({site.Why})", site.Ok);
            if (!site.Ok) { End(player, god, checks, onHeard, heard); yield break; }
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f);
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(3f);
            checks.Add($"깃발에 일꾼 2명 나타남 ({WorkerNpc.All.Count}명)", WorkerNpc.All.Count == 2);
            if (WorkerNpc.All.Count != 2) { End(player, god, checks, onHeard, heard); yield break; }

            TestRunner.Log($"DIAG 시작 거점 상자: Wood {GatherTest.CountBase(home, "Wood")}, Stone {GatherTest.CountBase(home, "Stone")}, CopperOre {GatherTest.CountBase(home, "CopperOre")}, Copper {GatherTest.CountBase(home, "Copper")}");
            // 2) 명령마다 거점 상자 개수로 판정 — 실패해도 다음 명령을 계속(하나 막혀도 계속)
            yield return Order(checks, home, "나무", "Wood");
            yield return Order(checks, home, "돌", "Stone");
            yield return Order(checks, home, "구리", "CopperOre", 600f);
            yield return Order(checks, home, "구리주괴", "Copper", 900f, make: true); // 제련: 거점 제련소·석탄 연료가 있어야 함(없으면 FAIL로 드러남)

            End(player, god, checks, onHeard, heard);
        }

        /// <summary>"!일꾼 말 10" 한 번 내리고 거점 상자에 그 물건이 10개 더 생기면 PASS. 최대 maxSec초(기본 240초).
        /// 구리는 광맥을 찾아 멀리(240m) 가야 해서 회차 647에 600초로 늘림(찾기 152초 + 캐기 + 운반이 240초를 넘음).
        /// make: 만드는 물건(제작 계획)은 수량이 "상자에 n개 되게"라(PlanFor가 재고를 빼고 셈) 처음 개수 + 10을 시킨다 — 집 상자에 원래 구리주괴 14개가 있어 "구리주괴 10"은 "이미 상자에 있어"였음.</summary>
        static IEnumerator Order(GatherTest.Checks checks, Vector3 home, string word, string item, float maxSec = 240f, bool make = false)
        {
            int before = GatherTest.CountBase(home, item), got = 0;
            GatherTest.Say($"!{Workers.Name1} {word} {(make ? before + 10 : 10)}", null);
            float t = 0f;
            while (t < maxSec)
            {
                yield return new WaitForSeconds(5f);
                t += 5f;
                got = GatherTest.CountBase(home, item) - before;
                if (got >= 10) break;
            }
            checks.Add($"{word} 10 명령 → 거점 상자 {got}개 ({t:0}초)", got >= 10);
        }

        static void End(Player player, bool god, GatherTest.Checks checks, System.Action<string, string> onHeard, List<string> heard)
        {
            if (EnvMan.instance != null) { EnvMan.instance.m_debugTimeOfDay = s_dbgTime; EnvMan.instance.m_debugTime = s_dbgValue; }
            ChatNet.Heard -= onHeard;
            player.SetGodMode(god);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
