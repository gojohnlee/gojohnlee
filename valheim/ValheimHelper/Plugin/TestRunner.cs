using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 게임 안 시험 공통: 보고 모으기(콘솔·BepInEx 로그·플러그인 폴더 diag.txt), 자동 실행.
    /// 플러그인 폴더에 autotest.txt(내용: 콘솔 명령 한 줄)가 있으면 월드에 들어가고 10초 뒤 그 명령을 실행하고,
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

        static string PluginDir => Path.GetDirectoryName(Plugin.Instance.Info.Location);

        public static bool Begin(Terminal t, string name)
        {
            if (Running) { t?.AddString("이미 시험 중입니다."); return false; }
            Running = true;
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
            if (s_autoCommand == null) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || Console.instance == null) { s_autoWait = 0f; return; }
            s_autoWait += Time.deltaTime;
            if (s_autoWait < 10f) return;
            string cmd = s_autoCommand;
            s_autoCommand = null;
            s_auto = true;
            Console.instance.TryRunCommand(cmd, silentFail: false, skipAllowedCheck: true);
            if (!Running) s_auto = false; // 명령이 시험을 시작하지 않았다
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
