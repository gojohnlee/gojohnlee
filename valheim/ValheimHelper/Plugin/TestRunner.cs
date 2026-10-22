using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 게임 안 시험 공통: 보고 모으기(콘솔·BepInEx 로그·플러그인 폴더 diag.txt), 자동 실행.
    /// 플러그인 폴더에 autotest.txt(내용: 콘솔 명령들, ;로 구분)가 있으면 월드에 들어가고 3초 뒤 차례로 실행하고,
    /// 모두 끝나면 게임을 끈다. 보고는 한 diag.txt에 이어 쓴다. 테스트 스크립트(scripts/valheim-test.ps1)가 이 파일을 만든다.
    /// </summary>
    static class TestRunner
    {
        public static bool Running { get; private set; }
        static readonly List<string> s_report = new List<string>();
        static readonly Queue<string> s_autoQueue = new Queue<string>();
        static readonly Dictionary<string, System.Func<IEnumerator>> s_tests = new Dictionary<string, System.Func<IEnumerator>>();

        /// <summary>
        /// 시험 등록: 콘솔 명령(F5, 콘솔을 켰을 때)으로도, 자동 시험으로도 돈다. 자동 시험은 콘솔을 거치지 않으므로
        /// 게임을 -console 없이 켜도 된다(Steam이 실행 인자 경고를 띄우지 않게).
        /// </summary>
        public static void Register(string name, string description, System.Func<IEnumerator> run)
        {
            s_tests[name] = run;
            new Terminal.ConsoleCommand(name, "[Valheim Helper] " + description, args =>
            {
                if (Begin(args.Context, name)) Plugin.Instance.StartCoroutine(Safe(run()));
            });
        }
        static bool s_auto;
        static float s_autoWait;
        static Terminal s_terminal;
        static float s_startTime;
        static float s_startReal;
        static int s_startFrame;
        const float AutoTimeout = 600f;

        static string PluginDir => Path.GetDirectoryName(Plugin.Instance.Info.Location);

        public static bool Begin(Terminal t, string name)
        {
            if (Running) { t?.AddString("이미 시험 중입니다."); return false; }
            Running = true;
            s_startTime = Time.time;
            s_startReal = Time.realtimeSinceStartup;
            s_startFrame = Time.frameCount;
            Minimized.Release();
            s_terminal = t;
            if (!s_auto) s_report.Clear(); // 자동 시험 여러 개는 한 보고에 이어 쓴다
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
            s_report.Add($"시간: {Time.time - s_startTime:0}초"); // 시험 스크립트 요약에 시험별로 보인다(평소보다 오래 걸린 것 알아채기)
            float real = Mathf.Max(0.001f, Time.realtimeSinceStartup - s_startReal);
            s_report.Add($"프레임: 평균 {(Time.frameCount - s_startFrame) / real:0} FPS (실시간 {real:0}초)"); // 시험 틀 바꾸기: 동시 시험 때 느려짐을 보기 위함
            var lines = new List<string>(s_report.Count + 1) { Summary(s_report) };
            lines.AddRange(s_report);
            try { File.WriteAllLines(Path.Combine(PluginDir, "diag.txt"), lines.ToArray()); }
            catch (IOException e) { Plugin.Log.LogWarning("diag.txt 쓰기 실패: " + e.Message); }
            if (s_auto && s_autoQueue.Count == 0)
            {
                s_auto = false;
                Plugin.Instance.StartCoroutine(QuitSoon());
            }
            s_autoWait = 0f; // 다음 자동 시험은 3초 뒤
        }

        /// <summary>
        /// diag.txt 첫 줄 요약(토큰 아끼기, 2026-10-21): 이 줄만 읽고 다 PASS면 끝, FAIL이 있으면 그 시험 부분만 읽는다.
        /// 예: "요약: 3/4 PASS | FAIL vh_stonetest: 돌 10개 이상 상자에".
        /// </summary>
        static string Summary(List<string> report)
        {
            int tests = 0, passed = 0;
            string name = null;
            bool verdict = false, failed = false;
            string firstFail = null;
            var fails = new List<string>();
            void Close()
            {
                if (name == null) return;
                tests++;
                if (verdict && !failed) passed++;
                else fails.Add(name + (firstFail != null ? ": " + firstFail : verdict ? "" : ": 판정 없음"));
            }
            foreach (var line in report)
            {
                if (line.StartsWith("=== "))
                {
                    Close();
                    string rest = line.Substring(4);
                    int sp = rest.IndexOf(' ');
                    name = sp > 0 ? rest.Substring(0, sp) : rest;
                    verdict = failed = false;
                    firstFail = null;
                    continue;
                }
                bool passLine = line.StartsWith("판정: PASS") || line == "PASS" || line.StartsWith("PASS");
                bool failLine = line.StartsWith("판정: FAIL") || line.StartsWith("FAIL");
                if (passLine) verdict = true;
                if (line.StartsWith("판정: 환경 불량"))
                {
                    verdict = failed = true;
                    if (firstFail == null) firstFail = line.Substring(4);
                }
                if (failLine)
                {
                    verdict = failed = true;
                    if (firstFail == null && !line.StartsWith("판정")) firstFail = line.Substring(4).Trim(' ', ':');
                }
            }
            Close();
            return $"요약: {passed}/{tests} PASS" + (fails.Count > 0 ? " | FAIL " + string.Join(" | FAIL ", fails.ToArray()) : "");
        }

        public static void CheckAutoRun()
        {
            string flag = Path.Combine(PluginDir, "autotest.txt");
            if (!File.Exists(flag)) return;
            string text = "";
            try
            {
                text = File.ReadAllText(flag);
                File.Delete(flag);
            }
            catch (IOException) { }
            foreach (var raw in text.Split(';', '\n', '\r'))
            {
                string c = raw.Trim();
                if (c.StartsWith("world=")) AutoStart.World = c.Substring(6).Trim(); // 메인 메뉴에서 이 월드로 자동 입장
                else if (c.StartsWith("char=")) AutoStart.Character = c.Substring(5).Trim().ToLowerInvariant(); // 시험 전용 캐릭터
                else if (c == "min") Minimized.On = true; // 창 최소화·소리 끔(시험 동안 다른 게임)
                else if (c.Length > 0) s_autoQueue.Enqueue(c);
            }
            if (s_autoQueue.Count == 0) return;
            s_auto = true;
            Plugin.Log.LogInfo($"자동 시험 예약: 월드에 들어가면 {string.Join(", ", s_autoQueue.ToArray())}를 차례로 실행합니다.");
        }

        public static void UpdateAutoRun()
        {
            if (!s_auto) return;
            // 시험이 오류로 끝나지 않으면 5분 뒤 결과를 남기고 넘어간다(사용자가 지켜보지 않아도 되게).
            if (Running)
            {
                if (Time.time - s_startTime > AutoTimeout)
                {
                    Log($"FAIL: 시험이 {AutoTimeout:0}초 안에 끝나지 않음(오류는 BepInEx 로그 참고)");
                    Finish();
                }
                return;
            }
            if (s_autoQueue.Count == 0) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting()) { s_autoWait = 0f; return; }
            s_autoWait += Time.deltaTime;
            if (s_autoWait < 3f) return; // 사용자 요청: 10초 → 3초
            string cmd = s_autoQueue.Dequeue();
            if (s_tests.TryGetValue(cmd, out var run) && Begin(null, cmd))
            {
                Plugin.Instance.StartCoroutine(Safe(run()));
                return;
            }
            // 모르는 시험이거나 등록에 실패했다. 그래도 결과를 남긴다.
            Begin(null, cmd);
            Log($"FAIL: 자동 시험 {cmd}가 시작되지 않음(오류는 BepInEx 로그 참고)");
            Finish();
        }

        /// <summary>
        /// 시험 코드에서 예외가 나면 FAIL로 적고 바로 끝낸다(0.3.7: NullReferenceException으로 멈춘 시험이 10분 시간 제한까지 기다림).
        /// </summary>
        static IEnumerator Safe(IEnumerator inner)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) yield break;
                    current = inner.Current;
                }
                catch (TestKit.EnvBadException e)
                {
                    Log($"판정: 환경 불량 ({e.Message}) — 본시험 안 함");
                    if (Running) Finish();
                    yield break;
                }
                catch (System.Exception e)
                {
                    Log($"FAIL: 시험 코드 오류 {e.GetType().Name}: {e.Message}");
                    Plugin.Log.LogError(e);
                    if (Running) Finish();
                    yield break;
                }
                yield return current;
            }
        }

        static IEnumerator QuitSoon()
        {
            for (int i = 2; i > 0; i--) // 사용자 요청: 10초 → 2초
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"[Valheim Helper] 시험 끝. {i}초 뒤 게임을 끕니다.");
                yield return new WaitForSeconds(1f);
            }
            Application.Quit();
        }
    }
}
