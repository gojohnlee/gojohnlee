using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ValheimHelper.Core.Chat;

namespace ValheimHelper
{
    /// <summary>
    /// 호스트 PC의 claude 명령줄을 별도 프로세스로 부른다(구상서 5-1장). 게임 스레드는 막지 않고, 30초가 지나면 프로세스를 끊는다.
    /// 쓰는 쪽은 Done이 될 때까지 매 프레임 확인(Text: 답 본문, 실패면 null, Error: 이유).
    /// </summary>
    public sealed class ClaudeCli
    {
        public volatile bool Done;
        public string Text, Error;

        public static ClaudeCli Start(string model, string systemPrompt, string userText)
        {
            var call = new ClaudeCli();
            var t = new Thread(() => call.Run(model, systemPrompt, userText)) { IsBackground = true };
            t.Start();
            return call;
        }

        void Run(string model, string systemPrompt, string userText)
        {
            try
            {
                var args = new StringBuilder();
                foreach (var a in ClaudeCall.Args(model, systemPrompt)) args.Append(ClaudeCall.Quote(a)).Append(' ');
                var psi = new ProcessStartInfo(Exe(), args.ToString().TrimEnd())
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var err = new StringBuilder();
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null && err.Length < 500) err.AppendLine(e.Data); };
                    p.BeginErrorReadLine();
                    var bytes = new UTF8Encoding(false).GetBytes(userText);
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    p.StandardInput.Close();
                    string stdout = null;
                    var reader = new Thread(() => { try { stdout = p.StandardOutput.ReadToEnd(); } catch { } }) { IsBackground = true };
                    reader.Start();
                    if (!p.WaitForExit(ClaudeCall.TimeoutSeconds * 1000))
                    {
                        try { p.Kill(); } catch { }
                        Error = "시간 초과(" + ClaudeCall.TimeoutSeconds + "초)";
                        return;
                    }
                    reader.Join(2000);
                    Text = ClaudeCall.ExtractResult(stdout);
                    if (Text == null) Error = "답 없음 exit=" + p.ExitCode + " " + Trim(stdout) + " " + Trim(err.ToString());
                }
            }
            catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; }
            finally { Done = true; }
        }

        static string Trim(string s) => s == null ? "" : (s.Length > 300 ? s.Substring(0, 300) : s).Replace('\n', ' ');

        // Windows에서 claude는 claude.exe(네이티브) 또는 claude.cmd(npm)일 수 있다. .cmd는 UseShellExecute=false로 못 띄워 cmd /c 필요 없이 exe를 우선 찾는다.
        static string Exe()
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (dir.Length == 0) continue;
                try
                {
                    string f = Path.Combine(dir.Trim('"'), "claude.exe");
                    if (File.Exists(f)) return f;
                }
                catch { }
            }
            return "claude";
        }
    }
}
