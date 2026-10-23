# 발헤임 무인 루프 (설계: docs/valheim/loop-plan.txt). 작업 스케줄러 "VH loop"가 로그인 때 conhost 창으로 실행한다.
# claude가 하는 일(고친 코드·명령·결과)을 그 창에 찍는다. 창은 $ShowWindow가 $false면 켜자마자 숨긴다(사용자 요청 2026-10-22 저녁:
# 보이게 했다가 다시 숨김, 다시 보려면 $true). 보이는 창을 닫으면 루프도 끝난다.
# 회차: pull → 멈춤 파일 확인 → 계정 고르기 → claude -p 새 세션(scripts/vh-loop-prompt.txt) → 결과 판정 → status.txt 푸시.
# 15분마다 작업 트리를 valheim-wip에 백업(작업 트리·인덱스는 안 건드림, 바뀐 것이 없어도 올려 루프가 살아 있는지 보이게). 금방 끝난 오류는 그 계정만 잠깐 쉬게 한다.
# 백업 때 회차 시작 뒤 UserRequest.txt가 바뀌었으면 답 전용 짧은 claude(scripts/vh-reply-prompt.txt)를 따로 작업 트리에서 돌려 먼저 답한다.
# 두 계정 모두 한도(또는 쉼)면 풀리는 시각까지 기다리고,
# 모니터가 꺼졌으면 바로, 켜져 있으면 자리 비움(입력 없음 10분)일 때 깨우기 예약 후 최대 절전(30초 예약은 못 깨움 → 10분 넘게 남았을 때만).
# 멈추기: 저장소에 docs/valheim/loop-stop.txt를 만든다(GitHub 앱에서도 됨) 또는 %USERPROFILE%\vh-loop\stop 파일.
# 쉬기: docs/valheim/loop-pause.txt에 다시 시작할 시각("yyyy-MM-dd HH:mm")을 쓴다(아래 Get-PauseUntil).
# 밖에서 요청: docs/valheim/UserRequest.txt(회차가 먼저 처리, 답은 history.md 맨 위, 가져간 요청은 지움). 멈춤 조건이면 끝내지 않고 그 파일이 바뀔 때까지 기다린다.
# pull로 이 스크립트가 바뀌면 이번 회차 뒤 새 스크립트로 저절로 다시 켜진다(scripts/vh-loop-restart.ps1, PC에 사람이 없어도). 고칠 때 UTF-8 BOM을 지킨다.
# 기록: %USERPROFILE%\vh-loop\loop.log, 회차별 claude 출력 round-N.txt, 상태 state.json.
param([switch]$Once)   # -Once: 한 회차만(처음 지켜보는 시험용)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$Repo = Split-Path $PSScriptRoot -Parent
$Dir = Join-Path $env:USERPROFILE 'vh-loop'
New-Item -ItemType Directory $Dir -Force | Out-Null
$LogFile = Join-Path $Dir 'loop.log'
$StateFile = Join-Path $Dir 'state.json'
$Claude = Join-Path $env:USERPROFILE '.local\bin\claude.exe'
$Accounts = @(
    @{ Name = '1번'; Config = $null },
    @{ Name = '2번'; Config = (Join-Path $env:USERPROFILE '.claude-acct2') }
)
$ShowWindow = $false   # 루프 창 보이기(오른쪽 절반). $false = 숨김
$RoundTimeoutMin = 240
$BackupMin = 15
$ReplyTimeoutMin = 15
$ReplyDir = Join-Path $Dir 'reply'   # 답 전용 작업 트리(회차가 쓰는 저장소 작업 트리와 따로)
$SelfHash = (Get-FileHash $PSCommandPath).Hash   # 지금 도는 스크립트(바뀌면 다시 켜기, claude에는 VH_LOOP_HASH로 넘김)
$StaleNote = ''   # 다시 켜기를 못 하면 status.txt에 이유
$Diag = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\plugins\ValheimHelper\diag.txt'
$Utf8 = New-Object Text.UTF8Encoding($false)

$mutex = New-Object Threading.Mutex($false, 'Global\VhLoop')
try { $own = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $own = $true }   # 강제로 끝난 루프가 남긴 것은 넘겨받음
if (-not $own) { exit }   # 이미 돌고 있음
$mark = Join-Path $Dir 'restart'
if (Test-Path $mark) { Remove-Item $mark, (Join-Path $Dir 'stop') -Force -ErrorAction SilentlyContinue }   # 다시 켜기로 켜짐: 옛 루프를 멈추려던 stop 파일을 치움

Add-Type -Namespace VH -Name Power -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);
[StructLayout(LayoutKind.Sequential)] public struct LII { public uint cbSize; public uint dwTime; }
[DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LII p);
public static uint IdleMs() { LII l = new LII(); l.cbSize = 8; GetLastInputInfo(ref l); return (uint)Environment.TickCount - l.dwTime; }
'@
# 창에 보이기(사용자 요청 2026-10-22): claude를 stream-json으로 돌려 한 줄씩 사람이 읽게 찍는다(고친 코드·명령·결과).
# 줄은 큐에 넣고 따로 스레드가 찍는다 → 창이 멈춰도(선택 모드 등) claude 출력 읽기는 안 막힌다. 마지막 결과 글은 Feed.Final.
Add-Type -ReferencedAssemblies System.Web.Extensions -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace VH {
public static class Show {
    static readonly BlockingCollection<KeyValuePair<ConsoleColor, string>> Q = new BlockingCollection<KeyValuePair<ConsoleColor, string>>();
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr h, out uint m);
    [DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr h, uint m);
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool r);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    public static void Hide() { IntPtr w = GetConsoleWindow(); if (w != IntPtr.Zero) ShowWindow(w, 0); }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint b, ref RECT r, uint f);
    public static void Init(bool place) {
        uint m; IntPtr h = GetStdHandle(-10);
        if (GetConsoleMode(h, out m)) SetConsoleMode(h, (m & ~0x40u) | 0x80u);   // 빠른 편집 끔: 창을 눌러도 출력이 안 멈춤
        RECT a = new RECT(); IntPtr w = GetConsoleWindow();
        if (place && w != IntPtr.Zero && SystemParametersInfo(0x30, 0, ref a, 0)) MoveWindow(w, (a.L + a.R) / 2, a.T, (a.R - a.L) / 2, a.B - a.T, true);   // 화면 오른쪽 절반
        Thread t = new Thread(Run); t.IsBackground = true; t.Start();
    }
    public static void Put(string s, ConsoleColor c) { Q.Add(new KeyValuePair<ConsoleColor, string>(c, s)); }
    static void Run() {
        foreach (KeyValuePair<ConsoleColor, string> kv in Q.GetConsumingEnumerable()) {
            try { Console.ForegroundColor = kv.Key; Console.WriteLine(kv.Value); Console.ResetColor(); } catch { }
        }
    }
}
public class Feed {
    public readonly StringBuilder Final = new StringBuilder();
    public readonly StringBuilder Err = new StringBuilder();
    readonly string tag, root;
    readonly Dictionary<string, string> tools = new Dictionary<string, string>();
    readonly JavaScriptSerializer js = new JavaScriptSerializer();
    readonly Thread o, e;
    string lastText = "";
    bool gotResult;
    public Feed(Process p, string tag, string root) {
        this.tag = tag; this.root = root.Replace('/', '\\').TrimEnd('\\') + "\\";
        js.MaxJsonLength = int.MaxValue;
        o = new Thread(() => { string l; while ((l = p.StandardOutput.ReadLine()) != null) { try { Line(l); } catch (Exception x) { Put(ConsoleColor.DarkGray, "(표시 못 함: " + x.Message + ")"); } } });
        e = new Thread(() => { string l; while ((l = p.StandardError.ReadLine()) != null) { lock (Err) Err.AppendLine(l); Put(ConsoleColor.Red, l); } });
        o.IsBackground = true; e.IsBackground = true; o.Start(); e.Start();
    }
    // 끝난 뒤 남은 출력까지 읽는다(claude가 남긴 자식이 파이프를 쥐고 있어도 30초까지만). 결과 줄이 없으면(강제 종료 등) 마지막 글로.
    public void Join() { o.Join(30000); e.Join(30000); lock (Final) { if (!gotResult) Final.Append(lastText); } }

    void Put(ConsoleColor c, string s) { Show.Put(tag + s, c); }
    void Head(ConsoleColor c, string s) { Put(c, DateTime.Now.ToString("HH:mm:ss") + " " + s); }
    // 여러 줄 글: max줄까지(tail이면 끝 쪽 max줄), 한 줄은 200자까지
    void Block(ConsoleColor c, string pre, string text, int max, bool tail) {
        string[] ls = text.Replace("\r", "").Replace("\t", "    ").TrimEnd('\n').Split('\n');
        if (ls.Length == 1 && ls[0].Trim() == "") return;
        int from = tail && ls.Length > max ? ls.Length - max : 0, to = Math.Min(ls.Length, from + max);
        if (from > 0) Put(ConsoleColor.DarkGray, "         " + pre + "… (앞 " + from + "줄)");
        for (int i = from; i < to; i++) Put(c, "         " + pre + (ls[i].Length > 200 ? ls[i].Substring(0, 200) + "…" : ls[i]));
        if (to < ls.Length) Put(ConsoleColor.DarkGray, "         " + pre + "… (+" + (ls.Length - to) + "줄)");
    }
    string Rel(string p) { p = p.Replace('/', '\\'); return p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.Substring(root.Length) : p; }
    static string Cut(string s, int n) { s = s.Replace("\r", "").Replace('\n', ' '); return s.Length > n ? s.Substring(0, n) + "…" : s; }
    static object V(object o, string k) { Dictionary<string, object> d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
    static string S(object o, string k) { object v = V(o, k); return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }
    static object[] A(object o, string k) { return V(o, k) as object[] ?? new object[0]; }
    static string Content(object b) {
        object c = V(b, "content");
        if (c is string) return (string)c;
        StringBuilder sb = new StringBuilder();
        foreach (object x in c as object[] ?? new object[0]) if (S(x, "type") == "text") sb.AppendLine(S(x, "text"));
        return sb.ToString();
    }

    void Line(string l) {
        if (l.Trim() == "") return;
        Dictionary<string, object> d = null;
        if (l.StartsWith("{")) { try { d = js.DeserializeObject(l) as Dictionary<string, object>; } catch { } }
        if (d == null) { lock (Final) Final.AppendLine(l); Put(ConsoleColor.Gray, l); return; }
        string ind = S(d, "parent_tool_use_id") != "" ? "  | " : "";   // 서브에이전트가 한 일
        switch (S(d, "type")) {
            case "system":
                if (S(d, "subtype") == "init") Head(ConsoleColor.DarkCyan, "claude 시작 (" + S(d, "model") + ")");
                break;
            case "assistant":
                foreach (object b in A(V(d, "message"), "content")) {
                    string t = S(b, "type");
                    if (t == "text" && S(b, "text").Trim() != "") {
                        if (ind == "") lastText = S(b, "text");
                        Head(ConsoleColor.White, ind + "·"); Block(ConsoleColor.White, ind, S(b, "text"), 30, false);
                    }
                    else if (t == "tool_use") Tool(ind, S(b, "id"), S(b, "name"), V(b, "input"));
                }
                break;
            case "user":
                foreach (object b in A(V(d, "message"), "content")) if (S(b, "type") == "tool_result") Result(ind, b);
                break;
            case "result":
                lock (Final) { gotResult = true; Final.Insert(0, S(d, "result") + "\n"); }
                double ms; double.TryParse(S(d, "duration_ms"), NumberStyles.Any, CultureInfo.InvariantCulture, out ms);
                Head(ConsoleColor.Magenta, "claude 끝: " + Math.Round(ms / 60000) + "분, " + S(d, "num_turns") + "턴" + (S(d, "is_error") == "True" ? ", 오류" : ""));
                break;
        }
    }

    void Tool(string ind, string id, string name, object inp) {
        if (id != "") tools[id] = name;
        switch (name) {
            case "Edit":
                Head(ConsoleColor.Cyan, ind + "[수정] " + Rel(S(inp, "file_path")) + (S(inp, "replace_all") == "True" ? " (모두 바꿈)" : ""));
                Block(ConsoleColor.Red, ind + "- ", S(inp, "old_string"), 12, false);
                Block(ConsoleColor.Green, ind + "+ ", S(inp, "new_string"), 40, false);
                break;
            case "MultiEdit":
                Head(ConsoleColor.Cyan, ind + "[수정] " + Rel(S(inp, "file_path")));
                foreach (object x in A(inp, "edits")) { Block(ConsoleColor.Red, ind + "- ", S(x, "old_string"), 8, false); Block(ConsoleColor.Green, ind + "+ ", S(x, "new_string"), 20, false); }
                break;
            case "Write":
                Head(ConsoleColor.Cyan, ind + "[새로 씀] " + Rel(S(inp, "file_path")) + " (" + S(inp, "content").Split('\n').Length + "줄)");
                Block(ConsoleColor.Green, ind + "+ ", S(inp, "content"), 40, false);
                break;
            case "Bash": case "PowerShell":
                Head(ConsoleColor.Yellow, ind + "[명령] " + Cut(S(inp, "description"), 100));
                Block(ConsoleColor.Yellow, ind + "> ", S(inp, "command"), 8, false);
                break;
            case "Read":
                Head(ConsoleColor.DarkGray, ind + "[읽기] " + Rel(S(inp, "file_path")) + (S(inp, "offset") != "" ? " " + S(inp, "offset") + "줄부터" : ""));
                break;
            case "Grep": case "Glob":
                Head(ConsoleColor.DarkGray, ind + "[찾기] " + Cut(S(inp, "pattern"), 100) + " " + Rel(S(inp, "path")) + S(inp, "glob"));
                break;
            case "Agent": case "Task":
                Head(ConsoleColor.Magenta, ind + "[에이전트 " + S(inp, "subagent_type") + "] " + Cut(S(inp, "description"), 100));
                break;
            case "TodoWrite":
                Head(ConsoleColor.DarkCyan, ind + "[할 일]");
                foreach (object x in A(inp, "todos")) {
                    string s = S(x, "status");
                    Put(ConsoleColor.DarkCyan, "         " + ind + (s == "completed" ? "[x] " : s == "in_progress" ? "[>] " : "[ ] ") + Cut(S(x, "content"), 120));
                }
                break;
            default:
                Head(ConsoleColor.DarkGray, ind + "[" + name + "] " + Cut(js.Serialize(inp), 160));
                break;
        }
    }

    void Result(string ind, object b) {
        string name; tools.TryGetValue(S(b, "tool_use_id"), out name);
        string text = Content(b);
        if (S(b, "is_error") == "True") Block(ConsoleColor.Red, ind + "! ", text, 6, false);
        else if (name == "Bash" || name == "PowerShell") Block(ConsoleColor.DarkGray, ind + "| ", text, 8, true);
        else if (name == "Agent" || name == "Task") Block(ConsoleColor.DarkMagenta, ind + "| ", text, 10, false);
    }
}
}
'@
[VH.Show]::Init(-not $Once)   # -Once는 사용자가 연 창에서 돌리므로 창을 옮기지 않음
if (-not $ShowWindow -and -not $Once) { [VH.Show]::Hide() }   # 숨김: 작업 스케줄러가 연 창만(-Once는 사용자 창)
$Host.UI.RawUI.WindowTitle = 'VH loop'

$Awake = [uint32]2147483649   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED: 도는 동안 저절로 잠들지 않게
$Release = [uint32]2147483648
[void][VH.Power]::SetThreadExecutionState($Awake)

# 모니터 전원(사용자 결정 2026-10-23: 기다릴 때 모니터가 꺼져 있으면 입력과 상관없이 바로 최대 절전, 켜져 있으면 입력 없음 10분).
# 모니터에 DDC/CI(VCP D6 전원 모드)로 묻는다. Power(): 1 = 켜짐이라고 답한 모니터가 있음, 0 = 답한 모니터가 모두 대기·꺼짐,
# -1 = 아무도 답 안 함(꺼져서 Windows가 모니터를 놓쳤거나 DDC/CI를 안 하는 모니터).
$MonErr = $null
try {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace VH {
public static class Mon {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PM { public IntPtr H; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string D; }
    delegate bool EnumProc(IntPtr m, IntPtr dc, IntPtr r, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumProc cb, IntPtr p);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr m, out uint n);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr m, uint n, [Out] PM[] a);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, PM[] a);
    public static int Power() {
        List<IntPtr> hs = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr m, IntPtr dc, IntPtr r, IntPtr p) { hs.Add(m); return true; }, IntPtr.Zero);
        int res = -1;
        foreach (IntPtr m in hs) {
            uint n;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(m, out n) || n == 0) continue;
            PM[] a = new PM[n];
            if (!GetPhysicalMonitorsFromHMONITOR(m, n, a)) continue;
            try {
                foreach (PM x in a) {
                    uint cur, max;
                    if (!GetVCPFeatureAndVCPFeatureReply(x.H, 0xD6, IntPtr.Zero, out cur, out max)) continue;
                    if (cur == 1) return 1;
                    res = 0;
                }
            } finally { DestroyPhysicalMonitors(n, a); }
        }
        return res;
    }
}
}
'@
} catch { $MonErr = "$_" }

# 기다릴 때 30초마다 부른다. "켜짐"이라고 답한 모니터가 없으면 꺼짐(사용자 결정 2026-10-23: 모니터가 없거나 DDC/CI를 안 하거나
# 물을 수 없어도 켜 둘 이유가 없음). 꺼짐이 2번(30초 간격) 이어져야 $true → DDC/CI가 잠깐 안 되는 것으로 쓰는 중에 잠들지 않게.
$MonOff = 0; $MonState = '모름'
function Test-MonitorOff {
    $p = -1
    if (-not $MonErr) { try { $p = [VH.Mon]::Power() } catch { } }
    if ($p -eq 1) { $script:MonOff = 0; $script:MonState = '켜짐'; return $false }
    $script:MonOff++; $script:MonState = '꺼짐'
    return $script:MonOff -ge 2
}

function Log($m) { $l = "$(Get-Date -Format 'MM-dd HH:mm:ss') $m"; [IO.File]::AppendAllText($LogFile, "$l`r`n", $Utf8); [VH.Show]::Put("── $l", 'Gray') }
function G { & git -C $Repo @args 2>&1 | ForEach-Object { "$_" } }

function Load-State {
    $s = @{ round = 0; acct = 0; until = @('', ''); lastFail = ''; noProg = 0; errs = 0; reqSeen = ''; replyFails = 0 }
    if (Test-Path $StateFile) {
        try { $j = Get-Content $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json; foreach ($k in @($s.Keys)) { if ($null -ne $j.$k) { $s[$k] = $j.$k } } } catch { }
    }
    $s.until = @($s.until); $s.noProg = 0; $s.lastFail = ''; $s.errs = 0   # 다시 켜면 멈춤 셈은 새로
    $s
}
function Save-State { [IO.File]::WriteAllText($StateFile, ($st | ConvertTo-Json), $Utf8) }

function Prepare-Git {
    $lock = Join-Path $Repo '.git\index.lock'
    if ((Test-Path $lock) -and -not (Get-Process git -ErrorAction SilentlyContinue)) { Remove-Item $lock -Force; Log 'index.lock 지움' }
    if ((G branch --show-current | Select-Object -Last 1) -ne 'valheim') { G checkout -q valheim | Out-Null }
    G fetch -q origin | Out-Null
    $out = G merge --ff-only -q origin/valheim
    if ($LASTEXITCODE -ne 0) {
        # 사용자가 GitHub에서 고친 것(UserRequest.txt 등)과 안 올라간 로컬 커밋이 갈라짐 → 로컬 커밋을 그 위로 다시 쌓는다
        $out = G pull -q --rebase --autostash origin valheim
        if ($LASTEXITCODE -ne 0) { G rebase --abort | Out-Null; Log "pull 못 함: $out" }
    }
}

# 쉬기: 저장소에 docs/valheim/loop-pause.txt(첫 줄 "yyyy-MM-dd HH:mm", 이 PC 시각)를 두면 그 시각까지 새 회차를 시작하지 않는다.
# 돌고 있는 회차는 끝까지 하고, 기다리는 동안은 Wait-Until(자리 비움이면 최대 절전 + 깨우기 예약). 지난 시각이면 없는 것과 같다.
function Get-PauseUntil {
    $p = Join-Path $Repo 'docs\valheim\loop-pause.txt'
    if (-not (Test-Path $p)) { return $null }
    $line = [IO.File]::ReadAllLines($p, $Utf8) | Where-Object { $_.Trim() } | Select-Object -First 1
    $t = [datetime]::MinValue
    if ($line -and [datetime]::TryParseExact($line.Trim(), 'yyyy-MM-dd HH:mm', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$t) -and $t -gt (Get-Date)) { return $t }
    return $null
}

function Test-StopFile { (Test-Path (Join-Path $Repo 'docs\valheim\loop-stop.txt')) -or (Test-Path (Join-Path $Dir 'stop')) }

function Backup-Wip {
    $idx = Join-Path $Dir 'wip.index'
    Copy-Item (Join-Path $Repo '.git\index') $idx -Force
    $env:GIT_INDEX_FILE = $idx
    G add -A | Out-Null
    $tree = G write-tree | Select-Object -Last 1
    Remove-Item Env:GIT_INDEX_FILE
    # 바뀐 것이 없어도 올린다(사용자 요청 2026-10-23: WIP가 안 올라오면 루프가 멈췄는지 알 수 없음)
    $same = $tree -eq (G rev-parse 'HEAD^{tree}' | Select-Object -Last 1)
    $c = G commit-tree $tree -p (G rev-parse HEAD | Select-Object -Last 1) -m "WIP backup $(Get-Date -Format 'MM-dd HH:mm')$(if ($same) { ' (바뀐 것 없음, 루프 도는 중)' })" | Select-Object -Last 1
    G push -q -f origin "${c}:refs/heads/valheim-wip" | Out-Null
    Log "valheim-wip 백업 $c"
}

function Write-Status($now, $last) {
    $script:StatusNow = $now   # Wait-Until이 여기에 절전 상태를 덧붙인다
    # 할 일: progress "★ 지금 …" 머리줄 아래 들여 쓴 줄들에서 [끝]이 아닌 첫 번호 항목.
    # 번호 항목이 전부 [끝]이면 다음 "★ 지금" 머리줄로, 번호 항목이 없는 머리줄은 그 줄 자체가 할 일.
    $p = @(Get-Content (Join-Path $Repo 'docs\valheim\progress.txt') -Encoding UTF8)
    $todo = ''
    for ($i = 0; $i -lt $p.Count -and -not $todo; $i++) {
        if ($p[$i] -notmatch '^★ 지금') { continue }
        $items = 0
        for ($j = $i + 1; $j -lt $p.Count -and $p[$j] -match '^\s+\S'; $j++) {
            if ($p[$j] -notmatch '^\s+\d+\)') { continue }
            $items++
            if ($p[$j] -notmatch '^\s+\d+\)\s*\[끝') { $todo = $p[$j]; break }
        }
        if (-not $todo -and $items -eq 0) { $todo = $p[$i] }
    }
    $todo = "$todo".Trim(); if ($todo.Length -gt 120) { $todo = $todo.Substring(0, 120) + '…' }
    $test = if (Test-Path $Diag) { "$((Get-Item $Diag).LastWriteTime.ToString('MM-dd HH:mm')) $(Get-Content $Diag -Encoding UTF8 -TotalCount 1)" } else { '(없음)' }
    $lim = @(0, 1 | ForEach-Object { if ($st.until[$_] -and [datetime]$st.until[$_] -gt (Get-Date)) { "$($Accounts[$_].Name) $(([datetime]$st.until[$_]).ToString('MM-dd HH:mm'))까지" } })
    $text = @(
        "갱신: $(Get-Date -Format 'yyyy-MM-dd HH:mm')  (무인 루프, scripts/vh-loop.ps1)",
        "지금: $now",
        "회차: $($st.round), 계정: $($Accounts[$st.acct].Name), 한도: $(if ($lim.Count) { $lim -join ', ' } else { '없음' })",
        "할 일: $todo",
        "마지막 시험: $test",
        "Claude가 한 일: $last",
        "요청은 이 폴더 UserRequest.txt에 쓴다(답은 history.md 맨 위, 돌고 있는 회차가 있어도 15분마다 확인). 멈추려면 loop-stop.txt를 만든다(내용 아무거나)."
    ) + @(if ($StaleNote) { $StaleNote }) -join "`n"
    $f = Join-Path $Repo 'docs\valheim\status.txt'
    [IO.File]::WriteAllText($f, "$text`n", $Utf8)
    G add docs/valheim/status.txt | Out-Null
    G commit -q -m 'Valheim loop status' -- docs/valheim/status.txt | Out-Null
    G push -q origin valheim | Out-Null
    if ($LASTEXITCODE -ne 0) { G pull -q --rebase --autostash origin valheim | Out-Null; G push -q origin valheim | Out-Null }
}

function Start-Claude($acct, $workDir, $prompt, $tag) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $Claude
    $psi.Arguments = "-p `"Read $prompt and follow it exactly.`" --dangerously-skip-permissions --output-format stream-json --verbose"
    $psi.WorkingDirectory = $workDir
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $Utf8; $psi.StandardErrorEncoding = $Utf8
    if ($acct.Config) { $psi.EnvironmentVariables['CLAUDE_CONFIG_DIR'] = $acct.Config } else { $psi.EnvironmentVariables.Remove('CLAUDE_CONFIG_DIR') }
    # 배경 서브에이전트가 남은 채 턴이 끝나도 10분 뒤 끊지 않고 끝까지 기다린다(회차 54가 그렇게 끊겨 LOOP 줄 없이 끝남). 회차 시간 제한은 그대로.
    $psi.EnvironmentVariables['CLAUDE_CODE_PRINT_BG_WAIT_CEILING_MS'] = '0'
    $psi.EnvironmentVariables['VH_LOOP_HASH'] = $SelfHash   # 회차 claude가 vh-loop-restart.ps1로 옛 루프인지 본다
    $p = [Diagnostics.Process]::Start($psi)
    @{ P = $p; F = New-Object VH.Feed($p, $tag, $workDir); Start = Get-Date }
}

function End-Claude($c, $outFile) {
    $c.P.WaitForExit()
    $c.F.Join()
    $text = "$($c.F.Final)`n$($c.F.Err)"
    [IO.File]::WriteAllText($outFile, $text, $Utf8)
    @{ Code = $c.P.ExitCode; Text = $text; Min = ((Get-Date) - $c.Start).TotalMinutes }
}

function Run-Claude($acct, $outFile) {
    $c = Start-Claude $acct $Repo 'scripts/vh-loop-prompt.txt' ''
    $bk = Get-Date
    while (-not $c.P.WaitForExit(60000)) {
        if (((Get-Date) - $bk).TotalMinutes -ge $BackupMin) { Backup-Wip; Reply-Requests $acct; $bk = Get-Date }
        if (((Get-Date) - $c.Start).TotalMinutes -ge $RoundTimeoutMin) { Log '회차 시간 초과 → claude 끔'; & taskkill /T /F /PID $c.P.Id | Out-Null; break }
    }
    End-Claude $c $outFile
}

# 15분 백업 때마다: 회차가 시작한 뒤 UserRequest.txt가 바뀌었으면(사용자가 GitHub에서 새로 씀) 회차 끝을 기다리지 않고
# 답 전용 claude를 짧게 따로 돌려 history.md 맨 위에 먼저 답한다(사용자 결정 2026-10-22). 회차의 작업 트리·게임은 안 건드린다:
# 따로 작업 트리($ReplyDir, origin/valheim의 분리된 HEAD)에서 history.md·UserRequest.txt만 커밋해 valheim에 푸시.
# 회차도 history.md 맨 위에 넣으므로 .gitattributes가 history.md를 merge=union으로 둔다(합칠 때 양쪽 줄을 다 남김).
function Reply-Requests($acct) {
    G fetch -q origin valheim | Out-Null
    $req = G rev-parse 'origin/valheim:docs/valheim/UserRequest.txt' | Select-Object -Last 1
    if ($req -eq $st.reqSeen) { return }
    if (-not (Test-Path (Join-Path $ReplyDir '.git'))) {
        if (Test-Path $ReplyDir) { Remove-Item $ReplyDir -Recurse -Force }
        G worktree prune | Out-Null; G worktree add -q --detach $ReplyDir origin/valheim | Out-Null
    }
    & git -C $ReplyDir checkout -q -f --detach origin/valheim 2>&1 | Out-Null
    & git -C $ReplyDir clean -q -fd 2>&1 | Out-Null
    Log 'UserRequest.txt 바뀜(회차 도중) → 답 전용 claude'
    $c = Start-Claude $acct $ReplyDir 'scripts/vh-reply-prompt.txt' '[답] '
    if (-not $c.P.WaitForExit($ReplyTimeoutMin * 60000)) { Log '답 전용 claude 시간 초과 → 끔'; & taskkill /T /F /PID $c.P.Id | Out-Null }
    $r = End-Claude $c (Join-Path $Dir "reply-$($st.round).txt")
    $last = @($r.Text -split "`r?`n" | Where-Object { $_ -match '^\s*REPLY:' }) | Select-Object -Last 1
    if ($last) { Log $last.Trim(); $st.replyFails = 0; $st.reqSeen = $req }
    else {
        # 한도·오류: 다음 백업 때 한 번 더, 두 번째도 안 되면 이 내용은 회차 끝(다음 회차 0번)에 맡긴다
        $st.replyFails++
        Log "답 전용 claude 실패(종료 코드 $($r.Code), $($st.replyFails)번째)"
        if ($st.replyFails -ge 2) { $st.replyFails = 0; $st.reqSeen = $req }
    }
    Save-State
}

# 실제 한도 문구(2026-10-21): "You've hit your session limit · resets 12:30am (Asia/Seoul)". 주간 한도는 "resets Oct 24, 9pm"처럼
# 날짜가 붙을 수 있다 → 넓게 잡고 출력 원문은 round-N.txt에 남는다. 풀리는 시각을 못 읽으면 1시간 뒤.
function Limit-Until($tail) {
    if ($tail -notmatch '(?i)(usage limit|limit reached|hit your (\w+ )?limit|limit will reset|out of extra usage|limit\b.*\bresets?\b)') { return $null }
    if ($tail -match '\|(\d{10})') { return [DateTimeOffset]::FromUnixTimeSeconds([long]$Matches[1]).LocalDateTime.AddMinutes(2) }
    $date = $null
    if ($tail -match '(?i)resets?\s+(?:on\s+)?([a-z]{3})[a-z]*\.?\s+(\d{1,2})(?:st|nd|rd|th)?,?\s+(?:at\s+)?(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)') {
        $mo = [array]::IndexOf(@('jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'), $Matches[1].ToLower()) + 1
        if ($mo) { $clock = $Matches[3]; try { $date = New-Object DateTime((Get-Date).Year, $mo, [int]$Matches[2]) } catch { } }
    }
    if (-not $date) {
        if ($tail -notmatch '(?i)resets?\s+(?:at\s+)?(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)') { return (Get-Date).AddHours(1) }
        $date = (Get-Date).Date; $clock = $Matches[1]
    }
    $null = $clock -match '(?i)(\d{1,2})(?::(\d{2}))?\s*(am|pm)?'
    $h = [int]$Matches[1]; $m = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
    if ($Matches[3] -eq 'pm' -and $h -lt 12) { $h += 12 }
    if ($Matches[3] -eq 'am' -and $h -eq 12) { $h = 0 }
    $t = $date.AddHours($h).AddMinutes($m)
    if ($t -le (Get-Date)) { $t = if ($date -eq (Get-Date).Date) { $t.AddDays(1) } else { $t.AddYears(1) } }
    $t.AddMinutes(2)
}

function Wait-Until($t) {
    # 기다리는 동안 30초마다 본다: 10분 넘게 남았고 모니터가 꺼졌거나(Test-MonitorOff, 입력과 상관없이) 자리 비움(입력 없음 10분)이면
    # 최대 절전(모니터가 켜져 있고 사용자가 쓰는 중이면 그냥 기다림).
    # 잠들 때·못 잠들 때·입력 때문에 안 잠들 때(처음 한 번, 그 뒤 30분마다 마지막 입력 시각)는 status.txt "지금"에 덧붙인다
    # → 밖에서도 보인다(2026-10-23: 사람 없는 07:13에 "10분 안에 입력 있음"으로 안 잠듦, 무슨 입력인지 보려고).
    # 잠들어야 하는데 못 잠들면(명령 오류, 또는 명령은 됐는데 2분 동안 안 잠듦) 2분마다 다시 시도(사용자 요청 2026-10-23).
    # status.txt는 첫 실패와 그 뒤 15번(약 30분)마다 + 그 실패를 알린 다음 시도 때만 고친다(2분마다 커밋하지 않게).
    $bk = $false; $fails = 0; $failTold = $false; $told = [datetime]::MinValue; $why = $script:StatusNow; $script:MonOff = 0
    while ((Get-Date) -lt $t) {
        $idle = [VH.Power]::IdleMs()
        $off = Test-MonitorOff
        if (($t - (Get-Date)).TotalMinutes -gt 10 -and ($off -or $idle -gt 600000)) {
            $how = if ($off) { '모니터 꺼짐' } else { '입력 없음 10분' }
            if (-not $bk) { Backup-Wip; $bk = $true }
            $act = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit'
            $set = New-ScheduledTaskSettingsSet -WakeToRun -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
            Register-ScheduledTask -TaskName 'VH loop wake' -Action $act -Trigger (New-ScheduledTaskTrigger -Once -At $t) -Settings $set -Force | Out-Null
            $again = if ($fails) { ", 다시 시도 $($fails + 1)번째" } else { '' }
            Log "최대 절전($how$again), $($t.ToString('MM-dd HH:mm')) 깨우기 예약"
            if (-not $fails -or $failTold) { Write-Status "$why — $(Get-Date -Format 'HH:mm')부터 최대 절전($how$again, 깨우기 예약)" '-'; $failTold = $false }
            [void][VH.Power]::SetThreadExecutionState($Release)
            $h = Get-Date   # 부르기 전에 잰다: shutdown /h가 바로 돌아오든 깰 때까지 안 돌아오든 절전 시간이 벽시계에 들어간다
            & shutdown /h
            $code = $LASTEXITCODE
            # 깨우기 예약으로 켜진(사람 없는) PC는 2분 안에 다시 잠든다 → 깨어나면 곧바로 Awake.
            # Start-Sleep은 절전 동안 시간이 안 흐르므로 벽시계로 2분을 잰다(절전 들어가는 동안 다시 걸지 않게).
            while (((Get-Date) - $h).TotalSeconds -lt 120) { Start-Sleep 2 }
            [void][VH.Power]::SetThreadExecutionState($Awake)
            # 잤다 = 2분보다 훨씬 지남(깨우기 예약) 또는 방금 입력(사람이 깨움). 아니면 못 잠든 것 → 조건이 그대로면 다음 바퀴에 다시 시도.
            if (-not $code -and (((Get-Date) - $h).TotalSeconds -gt 150 -or [VH.Power]::IdleMs() -lt 120000)) { Log '깨어남'; $fails = 0; continue }
            $fails++
            $err = if ($code) { "shutdown /h 코드 $code" } else { '명령은 됐는데 안 잠듦' }
            Log "최대 절전 실패 ${fails}번째($err) → 2분 뒤 다시 시도"
            if ($fails % 15 -eq 1) { Write-Status "$why — 최대 절전 실패 ${fails}번($err), 2분마다 다시 시도 중" '-'; $failTold = $true }
            continue
        }
        # 모니터 꺼짐을 처음 본 때(30초 뒤 한 번 더 보고 잠듦)는 알리지 않는다
        if ($script:MonOff -eq 0 -and ((Get-Date) - $told).TotalMinutes -ge 30 -and ($t - (Get-Date)).TotalMinutes -gt 10) {
            $told = Get-Date
            $in = (Get-Date).AddMilliseconds(-[double]$idle).ToString('HH:mm:ss')
            Log "자리 비움 아님(모니터 켜짐, 마지막 입력 $in) → 안 잠듦"
            Write-Status "$why — 모니터 켜짐, 키보드·마우스 입력이 있어 안 잠듦(마지막 입력 $in, 모니터를 끄거나 입력 없이 10분 지나면 잠듦)" '-'
        }
        Start-Sleep 30
    }
    [void][VH.Power]::SetThreadExecutionState($Awake)
    Log '기다림 끝'
}

# 멈춤 조건(사용자 확인 필요·할 일 없음·같은 FAIL·진척 없음)이면 끝내지 않고 사용자 요청을 기다린다: 밖에서 GitHub으로
# docs/valheim/UserRequest.txt를 고치면 다시 돈다(5분마다 pull해서 그 파일이 바뀌었나 봄). 기다리는 동안 PC는 깨어 있다. 멈춤 파일이면 $false.
# 회차 결과(사용자 결정 2026-10-23): 진척은 근거가 붙은 "끝낸 것"이 새로 생겼을 때만 센다. [끝] 표시·푸시·문구 고침·시험 실행·코드 변경만으로는 안 셈.
# 끝낸 것 = $before..$after에서 새로 들어온 줄 중
#  progress "[끝, 회차 N"(이번 회차 번호)이면서 근거: 실제 있는 커밋 해시 + 시험 PASS, 또는 "문서만"(문서 항목은 변경 내용으로 확인)
#  history "**답**" … 회차 N(질문에 답) · Errors-resolved.txt에 새로 옮긴 ID(줄에 실제 있는 커밋 해시).
# "구현 완료·검증 대기"는 끝이 아니다. 허위 완료까지 잡지는 않는다(사용자: 거기에 시간 쓰지 말 것).
function Test-RoundResult($before, $after, $n) {
    if ($before -eq $after) { return $false }
    $hasCommit = {
        param($text)
        foreach ($m in [regex]::Matches($text, '\b[0-9a-f]{7,40}\b')) {
            $c = G rev-parse -q --verify "$($m.Value)^{commit}" | Select-Object -Last 1
            if ($c -match '^[0-9a-f]{40}$' -and (G merge-base $c $after | Select-Object -Last 1) -eq $c) { return $true }
        }
        return $false
    }
    $add = @(G diff -U0 $before $after -- docs/valheim/progress.txt | Where-Object { $_ -match "^\+[^+].*\[끝,?\s*회차\s*$n(?!\d)" })
    foreach ($l in $add) { if ($l -match '문서만' -or ($l -match '\bPASS\b' -and (& $hasCommit $l))) { return $true } }
    if (@(G diff -U0 $before $after -- docs/valheim/history.md | Where-Object { $_ -match "^\+\*\*답\*\*.*회차\s*$n(?!\d)" }).Count) { return $true }
    $res = @(G diff -U0 $before $after -- docs/valheim/Errors-resolved.txt)
    $minus = @($res | Where-Object { $_ -match '^-[^-]' } | ForEach-Object { [regex]::Matches($_, 'E\d{3}') } | ForEach-Object { $_.Value })
    foreach ($l in @($res | Where-Object { $_ -match '^\+[^+]' })) {
        $new = @([regex]::Matches($l, 'E\d{3}') | ForEach-Object { $_.Value } | Where-Object { $minus -notcontains $_ })   # 고쳐 쓴 줄(같은 ID가 빠지고 들어옴)은 안 셈
        if ($new.Count -and (& $hasCommit $l)) { return $true }
    }
    return $false
}

function Wait-UserRequest($why, $last) {
    $f = 'origin/valheim:docs/valheim/UserRequest.txt'
    $h0 = G rev-parse $f | Select-Object -Last 1
    Log "멈춤: $why → UserRequest.txt가 바뀔 때까지 기다림"
    Write-Status "답 기다림: $why — UserRequest.txt에 쓰면 5분 안에 이어 감(물은 것은 history.md 맨 위)" $last
    while ($true) {
        Start-Sleep 300
        Prepare-Git
        if (Test-StopFile) { return $false }
        if ((G rev-parse $f | Select-Object -Last 1) -ne $h0) { Log 'UserRequest.txt 바뀜 → 다시 돎'; return $true }
    }
}

$st = Load-State
Log "루프 시작$(if ($Once) { ' (한 회차만)' })"
[void](Test-MonitorOff)   # 시작 때 모니터 상태를 기록에 남긴다
Log "모니터: $MonState$(if ($MonErr) { " (확인 못 만듦: $MonErr)" })"
while ($true) {
    Prepare-Git
    if (-not $Once -and (Get-FileHash $PSCommandPath).Hash -ne $SelfHash) {
        # pull로 이 스크립트가 바뀜: 문법·BOM 검사 후 다시 켜기 예약(stop 파일을 만들어 바로 아래에서 끝나고, 대기 작업이 새 스크립트로 켬)
        $out = (& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'vh-loop-restart.ps1') -Now 2>&1 | Out-String).Trim()
        Log "루프 스크립트 바뀜 → $out"
        if ($LASTEXITCODE) { $StaleNote = "루프 스크립트가 바뀌었지만 옛것으로 도는 중: $out" }
        $SelfHash = (Get-FileHash $PSCommandPath).Hash   # 못 했으면 같은 것으로 다시 시도하지 않음
    }
    if (Test-StopFile) {
        Log '멈춤 파일 → 끝'; Write-Status $(if (Test-Path $mark) { '다시 켜는 중: 새 루프 스크립트로(1~2분)' } else { '멈춤: 멈춤 파일이 있음' }) '-'; break
    }
    $pause = Get-PauseUntil
    if ($pause) {
        Log "쉼 파일 → $($pause.ToString('MM-dd HH:mm'))까지 새 회차 안 함"
        Write-Status "쉼: loop-pause.txt — $($pause.ToString('MM-dd HH:mm'))에 다시" '-'
        Save-State; Wait-Until $pause; continue
    }
    $free = @(0, 1 | Where-Object { -not $st.until[$_] -or [datetime]$st.until[$_] -le (Get-Date) })
    if ($free.Count -eq 0) {
        $t = @(0, 1 | ForEach-Object { [datetime]$st.until[$_] } | Sort-Object)[0]
        Write-Status "한도 대기: 두 계정 모두 한도(또는 오류로 쉼), $($t.ToString('MM-dd HH:mm'))에 다시" '-'
        Save-State; Wait-Until $t; continue
    }
    if ($free -notcontains $st.acct) { $st.acct = $free[0] }
    $acct = $Accounts[$st.acct]
    $st.round++
    Write-Status "회차 $($st.round) 진행 중" '(진행 중)'
    $before = G rev-parse origin/valheim | Select-Object -Last 1
    $st.reqSeen = G rev-parse 'HEAD:docs/valheim/UserRequest.txt' | Select-Object -Last 1   # 이 회차가 0번에서 읽을 요청(이후 바뀌면 백업 때 따로 답)
    $st.replyFails = 0
    Log "회차 $($st.round) 시작 ($($acct.Name) 계정)"
    $Host.UI.RawUI.WindowTitle = "VH loop - 회차 $($st.round)"
    $r = Run-Claude $acct (Join-Path $Dir "round-$($st.round).txt")
    $lines = @($r.Text -split "`r?`n" | Where-Object { $_.Trim() })
    $last = $lines | Where-Object { $_ -match '^\s*LOOP:' } | Select-Object -Last 1
    if (-not $last) {
        $lim = Limit-Until (($lines | Select-Object -Last 5) -join "`n")
        if ($lim) {
            $st.until[$st.acct] = $lim.ToString('o'); $st.round--
            Log "$($acct.Name) 계정 한도 → $($lim.ToString('MM-dd HH:mm'))"; Save-State; continue
        }
        $last = "LOOP: ERROR 마지막 줄 없음(종료 코드 $($r.Code)): $(($lines | Select-Object -Last 1))"
        if ($last.Length -gt 200) { $last = $last.Substring(0, 200) }
        if ($r.Min -lt 10) {
            # 금방 끝난 오류(네트워크·서버·로그인 등)는 진척 셈에 안 넣는다. 그 계정만 5·10·20·40·60분 쉬게 해서 다른 계정이 비었으면 그걸로 잇는다.
            $st.errs++
            $rest = [math]::Min(60, 5 * [math]::Pow(2, $st.errs - 1))
            $st.until[$st.acct] = (Get-Date).AddMinutes($rest).ToString('o'); $st.round--
            Log "$last → $($acct.Name) 계정 ${rest}분 쉼"; Save-State; continue
        }
    }
    $st.errs = 0
    $last = $last.Trim()
    Log $last
    Prepare-Git
    $after = G rev-parse origin/valheim | Select-Object -Last 1
    if (Test-RoundResult $before $after $st.round) { $st.noProg = 0 } else { $st.noProg++; Log "끝낸 것 없는 회차 $($st.noProg)/3" }
    $stop = $null
    if ($last -match '^LOOP: NEED_USER') { $stop = '사용자 확인 필요' }
    elseif ($last -match '^LOOP: ALLDONE') { $stop = '할 일 없음' }
    elseif ($last -match '^LOOP: FAIL (\S+)') {
        if ($st.lastFail -eq $Matches[1]) { $stop = "같은 시험 2회차 연속 FAIL($($Matches[1]))" }
        $st.lastFail = $Matches[1]
    } else { $st.lastFail = '' }
    if (-not $stop -and $st.noProg -ge 3) { $stop = '3회차 연속 근거 있는 완료 없음(커밋·시험 붙은 [끝]·질문 답·닫은 문제 없음)' }
    if ($last -match '^LOOP: ERROR') { Start-Sleep 300 }   # 네트워크 등 → 5분 쉬고
    if ($stop -and $Once) { Write-Status "멈춤: $stop" $last; Save-State; Log "멈춤: $stop"; break }
    if (-not $stop) { Write-Status $(if ($Once) { '한 회차 끝(시험 실행)' } else { '다음 회차 준비' }) $last }
    Save-State
    if ($stop) {
        if (-not (Wait-UserRequest $stop $last)) { Log '멈춤 파일 → 끝'; Write-Status '멈춤: 멈춤 파일이 있음' '-'; break }
        $st.noProg = 0; $st.lastFail = ''; Save-State; continue
    }
    if ($Once) { break }
}
Log '루프 끝'
[void][VH.Power]::SetThreadExecutionState($Release)
$mutex.ReleaseMutex()   # -NoExit 창이 열려 있어도 다음 루프가 시작되게
