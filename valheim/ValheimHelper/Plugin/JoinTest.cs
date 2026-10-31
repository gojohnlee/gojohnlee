using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_jointest (멀티 시험 나): 손님으로 전용 서버에 들어왔는지. autotest.txt "join=127.0.0.1:2456;pass=…"(scripts/vh-server.ps1 join)로
    /// 자동 접속한 뒤 실행: 서버 아님·연결됨·서버 피어 있음·플레이어 섬·10초 뒤에도 연결 유지. 서버 쪽 예외는 vh-server.ps1이 서버 로그로 본다.
    /// </summary>
    static class JoinTest
    {
        public static void Register() => TestRunner.Register("vh_jointest", "멀티: 손님 전용 서버 접속 시험", Run);

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            checks.Add("손님(서버 아님)", ZNet.instance != null && !ZNet.instance.IsServer());
            checks.Add("서버에 연결됨", ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected);
            checks.Add("서버 피어 있음", ZNet.instance != null && ZNet.instance.GetServerPeer() != null);
            checks.Add("플레이어 섬", Player.m_localPlayer != null && !Player.m_localPlayer.IsDead());
            yield return new WaitForSeconds(10f);
            checks.Add("10초 뒤에도 연결", ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && Player.m_localPlayer != null);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
