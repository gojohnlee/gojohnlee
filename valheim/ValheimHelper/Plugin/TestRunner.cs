using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 게임 안 시험 공통: 보고 모으기(콘솔·BepInEx 로그·플러그인 폴더 diag.txt), 자동 실행.
    /// 플러그인 폴더에 autotest.txt(내용: 콘솔 명령 한 줄)가 있으면 월드에 들어가고 3초 뒤 그 명령을 실행하고,
    /// 시험이 끝나면 게임을 끈다. 테스트 스크립트(scripts/valheim-test.ps1)가 이 파일을 만든다.
    /// </summary>
    static class TestRunner
    {
        public static bool Running { get; private set; }
        static readonly List<string> s_report = new List<string>();
        static string s_autoCommand;
        static bool s_auto;
        static float s_autoWait;
        static Terminal s_terminal;
        static float s_startTime;
        const float AutoTimeout = 300f;

        static string PluginDir => Path.GetDirectoryName(Plugin.Instance.Info.Location);

        public static bool Begin(Terminal t, string name)
        {
            if (Running) { t?.AddString("이미 시험 중입니다."); return false; }
            Running = true;
            s_startTime = Time.time;
            s_terminal = t;
            s_report.Clear();
            s_report.Add($"=== {name} {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} 게임 {Version.GetVersionString()} Unity {Application.unityVersion} 모드 {Plugin.Version} ===");
            return true;
        }

        public static void Log(string line)
        {
            s_report.Add(line);
            Plugin.Log.LogInfo(line);
            s_terminal?.AddString("[VH] " + line);
        }

        public static void Finish()
        {
            Running = false;
            try { File.WriteAllLines(Path.Combine(PluginDir, "diag.txt"), s_report.ToArray()); }
            catch (IOException e) { Plugin.Log.LogWarning("diag.txt 쓰기 실패: " + e.Message); }
            if (s_auto)
            {
                s_auto = false;
                Plugin.Instance.StartCoroutine(QuitSoon());
            }
        }

        public static void CheckAutoRun()
        {
            string flag = Path.Combine(PluginDir, "autotest.txt");
            if (!File.Exists(flag)) return;
            try
            {
                s_autoCommand = File.ReadAllText(flag).Trim();
                File.Delete(flag);
            }
            catch (IOException) { }
            if (string.IsNullOrEmpty(s_autoCommand)) return;
            Plugin.Log.LogInfo($"자동 시험 예약: 월드에 들어가면 {s_autoCommand}를 실행합니다.");
        }

        public static void UpdateAutoRun()
        {
            // 자동 시험이 오류로 끝나지 않으면 5분 뒤 결과를 남기고 끈다(사용자가 지켜보지 않아도 되게).
            if (s_auto && Running && Time.time - s_startTime > AutoTimeout)
            {
                Log($"FAIL: 시험이 {AutoTimeout:0}초 안에 끝나지 않음(오류는 BepInEx 로그 참고)");
                Finish();
                return;
            }
            if (s_autoCommand == null) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || Console.instance == null) { s_autoWait = 0f; return; }
            s_autoWait += Time.deltaTime;
            if (s_autoWait < 3f) return; // 사용자 요청: 10초 → 3초
            string cmd = s_autoCommand;
            s_autoCommand = null;
            s_auto = true;
            Console.instance.TryRunCommand(cmd, silentFail: false, skipAllowedCheck: true);
            if (!Running)
            {
                // 명령이 시험을 시작하지 못했다(등록 실패 등). 그래도 결과를 남기고 끈다.
                Begin(null, cmd);
                Log($"FAIL: 자동 시험 {cmd}가 시작되지 않음(오류는 BepInEx 로그 참고)");
                Finish();
            }
        }

        static IEnumerator QuitSoon()
        {
            for (int i = 10; i > 0; i--)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"[Valheim Helper] 시험 끝. {i}초 뒤 게임을 끕니다.");
                yield return new WaitForSeconds(1f);
            }
            Application.Quit();
        }
    }
}
