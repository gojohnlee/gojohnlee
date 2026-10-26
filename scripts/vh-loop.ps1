# 발헤임 무인 루프 (설계: docs/valheim/loop-plan.txt). 작업 스케줄러 "VH loop"가 로그인 때 conhost 창으로 실행한다.
# claude가 하는 일(고친 코드·명령·결과)을 그 창에 찍는다. 창은 $ShowWindow가 $false면 켜자마자 숨긴다(사용자 요청 2026-10-22 저녁:
# 보이게 했다가 다시 숨김, 다시 보려면 $true). 보이는 창을 닫으면 루프도 끝난다.
# 회차: pull → 멈춤 파일 확인 → 계정 고르기 → claude -p 새 세션(scripts/vh-loop-prompt.txt) → 결과 판정 → status.txt 푸시.
# 15분마다 작업 트리를 valheim-wip에 백업(작업 트리·인덱스는 안 건드림, 바뀐 것이 없어도 올려 루프가 살아 있는지 보이게). 금방 끝난 오류는 그 계정만 잠깐 쉬게 한다.
# 백업 때 회차 시작 뒤 UserRequest.txt가 바뀌었으면 답 전용 짧은 claude(scripts/vh-reply-prompt.txt)를 따로 작업 트리에서 돌려 먼저 답한다.
# 두 계정 모두 한도(또는 쉼)면 풀리는 시각까지 기다리고,
# 자리 비움(입력 없음 10분)일 때 깨우기 예약 후 최대 절전(30초 예약은 못 깨움 → 10분 넘게 남았을 때만).
# 멈추기: 저장소에 docs/valheim/loop-stop.txt를 만든다(GitHub 앱에서도 됨) 또는 %USERPROFILE%\vh-loop\stop 파일.
# 쉬기: docs/valheim/loop-pause.txt에 다시 시작할 시각("yyyy-MM-dd HH:mm")을 쓴다(아래 Get-PauseUntil).
# 밖에서 요청: docs/valheim/UserRequest.txt(회차가 먼저 처리, 답은 history.md 맨 위, 가져간 요청은 지움). 멈춤 조건이면 끝내지 않고 그 파일이 바뀔 때까지 기다린다.
# pull로 이 스크립트가 바뀌면 이번 회차 뒤 새 스크립트로 저절로 다시 켜진다(scripts/vh-loop-restart.ps1, PC에 사람이 없어도). 고칠 때 UTF-8 BOM을 지킨다.
# 기록: %USERPROFILE%\vh-loop\loop.log, 회차별 claude 출력 round-N.txt, 상태 state.json.
# 분업(decisions.txt 2절, docs/valheim/Harness.md): handoff-on.txt + 기준 5회차면 회차에 VH_HANDOFF=1 → 회차가 "LOOP: HANDOFF"로 끝내면
# vh-harness.ps1이 그 커밋을 빌드·게임 시험·Astra 리뷰(Invoke-VhHandoff), 결과 파일을 다음 회차에 VH_VERIFY로 넘긴다.
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
$GameDll = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll'
$GameNote = ''   # 게임 업데이트 감지 때 status.txt에 한 줄(그 회차가 전체 회귀)
$Utf8 = New-Object Text.UTF8Encoding($false)
. (Join-Path $PSScriptRoot 'vh-harness-lib.ps1')

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
    public string ResultJson = "";
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
                if (ind == "") ResultJson = js.Serialize(d);
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

function Log($m) { $l = "$(Get-Date -Format 'MM-dd HH:mm:ss') $m"; [IO.File]::AppendAllText($LogFile, "$l`r`n", $Utf8); [VH.Show]::Put("── $l", 'Gray') }
function G { & git -C $Repo @args 2>&1 | ForEach-Object { "$_" } }

function Load-State {
    $s = @{ round = 0; acct = 0; until = @('', ''); lastFail = ''; noProg = 0; errs = 0; reqSeen = ''; replyFails = 0; game = ''
            verify = ''; verifyFail = ''; verifyFailN = 0; astraUntil = ''; astraFails = 0 }   # 분업: 다음 회차에 넘길 검증 결과·같은 시험 연속 FAIL·Astra 쉼
    if (Test-Path $StateFile) {
        try { $j = Get-Content $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json; foreach ($k in @($s.Keys)) { if ($null -ne $j.$k) { $s[$k] = $j.$k } } } catch { }
    }
    $s.until = @($s.until); $s.noProg = 0; $s.lastFail = ''; $s.errs = 0; $s.verifyFail = ''; $s.verifyFailN = 0   # 다시 켜면 멈춤 셈은 새로
    $s
}
# 게임 업데이트 감지: assembly_valheim.dll 파일 버전 + 해시 앞 12자. 지난 회차 것($st.game)과 다르면 그 회차에 VH_GAME_UPDATE로 알린다.
function Get-GameId {
    if (-not (Test-Path $GameDll)) { return '' }
    try { "$((Get-Item $GameDll).VersionInfo.FileVersion) $((Get-FileHash $GameDll).Hash.Substring(0, 12))" } catch { '' }
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

# 할 일: progress "다음" 아래 첫 안 끝난 "★" 덩어리와 그 안의 첫 안 끝난 조각("  가) …"·"  2) …").
# [끝]·[보류]로 시작하는 덩어리·조각, 조각이 다 끝난 덩어리는 건너뛴다. 조각이 없는 덩어리는 이름만.
# ★가 없으면 "구현 단계" 표의 첫 [ ]·[~] 단계.
function Get-Todo($path) {
    $p = @(Get-Content $path -Encoding UTF8)
    $i = 0
    while ($i -lt $p.Count -and $p[$i] -notmatch '^다음') { $i++ }
    for ($i++; $i -lt $p.Count; $i++) {
        if ($p[$i] -notmatch '^★\s*(.*)$') { continue }
        $head = $Matches[1]
        if ($head -match '^\[(끝|보류)') { continue }
        $sub = ''; $items = 0
        for ($j = $i + 1; $j -lt $p.Count -and $p[$j] -match '^\s+\S'; $j++) {
            if ($p[$j] -notmatch '^\s+[^\s\[\]()]{1,4}\)\s*(.*)$') { continue }
            $items++
            if ($Matches[1] -match '^\[(끝|보류)') { continue }
            $sub = $p[$j].Trim(); break
        }
        if ($items -and -not $sub) { continue }   # 조각이 다 끝난 덩어리(다음 회차가 지움)
        $name = ($head -split '\s[(—]')[0].Trim()
        $todo = if ($sub) { "$name → $sub" } else { $name }
        if ($todo.Length -gt 120) { $todo = $todo.Substring(0, 120) + '…' }
        return $todo
    }
    $stage = $p | Where-Object { $_ -match '^- \[( |~)\]' } | Select-Object -First 1
    if ($stage) { return "$stage".Trim() }
    return '(없음)'
}

function Write-Status($now, $last) {
    $script:StatusNow = $now   # Wait-Until이 여기에 절전 상태를 덧붙인다
    $todo = Get-Todo (Join-Path $Repo 'docs\valheim\progress.txt')
    $test = if (Test-Path $Diag) { "$((Get-Item $Diag).LastWriteTime.ToString('MM-dd HH:mm')) $(Get-Content $Diag -Encoding UTF8 -TotalCount 1)" } else { '(없음)' }
    $lim = @(0, 1 | ForEach-Object { if ($st.until[$_] -and [datetime]$st.until[$_] -gt (Get-Date)) { "$($Accounts[$_].Name) $(([datetime]$st.until[$_]).ToString('MM-dd HH:mm'))까지" } })
    $text = @(
        "갱신: $(Get-Date -Format 'yyyy-MM-dd HH:mm')  (무인 루프, scripts/vh-loop.ps1)",
        "지금: $now",
        "회차: $($st.round), 계정: $($Accounts[$st.acct].Name), 한도: $(if ($lim.Count) { $lim -join ', ' } else { '없음' })",
        "할 일: $todo",
        "마지막 시험: $test",
        "Claude가 한 일: $last",
        "분업(Claude 구현 → Astra 검증): $(if ($Gate) { $Gate.why } else { '-' })",
        "요청은 이 폴더 UserRequest.txt에 쓴다(답은 history.md 맨 위, 돌고 있는 회차가 있어도 15분마다 확인). 멈추려면 loop-stop.txt를 만든다(내용 아무거나)."
    ) + @(if ($StaleNote) { $StaleNote }) + @(if ($GameNote) { $GameNote }) -join "`n"
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
    # 다이어트(사용자 10-27 "소모량이 너무 큼"): 스킬·MCP 끄고 도구를 줄이고 effort 보통 → 매 턴 다시 읽히는 기본분 4.1만 → 1.7만 토큰(실측)
    $psi.Arguments = "-p `"Read $prompt and follow it exactly.`" --dangerously-skip-permissions --output-format stream-json --verbose --effort medium --disable-slash-commands --strict-mcp-config --tools `"Bash,PowerShell,Read,Edit,Write,Grep,Glob,Agent`""
    $psi.WorkingDirectory = $workDir
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $Utf8; $psi.StandardErrorEncoding = $Utf8
    if ($acct.Config) { $psi.EnvironmentVariables['CLAUDE_CONFIG_DIR'] = $acct.Config } else { $psi.EnvironmentVariables.Remove('CLAUDE_CONFIG_DIR') }
    # 배경 서브에이전트가 남은 채 턴이 끝나도 10분 뒤 끊지 않고 끝까지 기다린다(회차 54가 그렇게 끊겨 LOOP 줄 없이 끝남). 회차 시간 제한은 그대로.
    $psi.EnvironmentVariables['CLAUDE_CODE_PRINT_BG_WAIT_CEILING_MS'] = '0'
    $psi.EnvironmentVariables['VH_LOOP_HASH'] = $SelfHash   # 회차 claude가 vh-loop-restart.ps1로 옛 루프인지 본다
    if ($GameUpdate) { $psi.EnvironmentVariables['VH_GAME_UPDATE'] = $GameUpdate } else { $psi.EnvironmentVariables.Remove('VH_GAME_UPDATE') }   # 회차 claude: 전체 회귀부터(vh-loop-prompt 1-0)
    # 분업(scripts/vh-loop-handoff.txt): VH_HANDOFF=1이면 게임 시험 대신 인계, VH_VERIFY = 지난 인계의 검증 결과 파일. 답 전용 세션에는 안 줌.
    $verify = if (-not $tag -and $st.verify -and (Test-Path $st.verify)) { $st.verify } else { '' }
    if ($tag) { $psi.EnvironmentVariables.Remove('VH_HANDOFF') } else { $psi.EnvironmentVariables['VH_HANDOFF'] = $(if ($HandoffOn) { '1' } else { '0' }) }
    if ($verify) { $psi.EnvironmentVariables['VH_VERIFY'] = $verify } else { $psi.EnvironmentVariables.Remove('VH_VERIFY') }
    $p = [Diagnostics.Process]::Start($psi)
    @{ P = $p; F = New-Object VH.Feed($p, $tag, $workDir); Start = Get-Date; RunId = [guid]::NewGuid().ToString('N'); Account = $acct.Name; Kind = $(if ($tag) { 'reply' } else { 'implementation' }); Todo = (Get-Todo (Join-Path $workDir 'docs\valheim\progress.txt'))
       Mode = $(if (-not $tag -and ($HandoffOn -or $verify)) { 'handoff' } else { 'claude-only' }) }   # 기준(분업 전) 회차와 분업 회차를 따로 센다
}

function End-Claude($c, $outFile) {
    $c.P.WaitForExit()
    $c.F.Join()
    $text = "$($c.F.Final)`n$($c.F.Err)"
    [IO.File]::WriteAllText($outFile, $text, $Utf8)
    try {
        $u = Write-VhUsage $Dir $c.F.ResultJson $st.round $c.Account $c.Kind $c.Todo $c.Start $c.P.ExitCode $c.RunId $c.Mode
        $script:LastUsage = $u
        Log ("usage {0}/{9} round={1} in={2} out={3} cache-read={4} cache-write={5} turns={6} minutes={7} result={8}" -f $u.kind,$u.round,$u.input_tokens,$u.output_tokens,$u.cache_read_tokens,$u.cache_write_tokens,$u.turns,$u.elapsed_minutes,$u.result,$u.mode)
    } catch { Log "usage write failed (not zero usage): $_" }
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
    # 기다리는 동안 30초마다 본다: 10분 넘게 남았고 자리 비움(입력 없음 10분)이면 최대 절전(사용자가 쓰는 중이면 그냥 기다림).
    # 모니터는 안 본다(사용자 결정 2026-10-23 밤: 이 PC 모니터는 DDC/CI에 답이 없어 "꺼짐"으로 보고 쓰는 중에도 잠들었음).
    # 잠들 때·못 잠들 때·입력 때문에 안 잠들 때(처음 한 번, 그 뒤 30분마다 마지막 입력 시각)는 status.txt "지금"에 덧붙인다
    # → 밖에서도 보인다(2026-10-23: 사람 없는 07:13에 "10분 안에 입력 있음"으로 안 잠듦, 무슨 입력인지 보려고).
    # 잠들어야 하는데 못 잠들면(명령 오류, 또는 명령은 됐는데 2분 동안 안 잠듦) 2분마다 다시 시도(사용자 요청 2026-10-23).
    # status.txt는 첫 실패와 그 뒤 15번(약 30분)마다 + 그 실패를 알린 다음 시도 때만 고친다(2분마다 커밋하지 않게).
    $bk = $false; $fails = 0; $failTold = $false; $told = [datetime]::MinValue; $why = $script:StatusNow
    while ((Get-Date) -lt $t) {
        $idle = [VH.Power]::IdleMs()
        if (($t - (Get-Date)).TotalMinutes -gt 10 -and $idle -gt 600000) {
            $how = '입력 없음 10분'
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
        if (((Get-Date) - $told).TotalMinutes -ge 30 -and ($t - (Get-Date)).TotalMinutes -gt 10) {
            $told = Get-Date
            $in = (Get-Date).AddMilliseconds(-[double]$idle).ToString('HH:mm:ss')
            Log "자리 비움 아님(마지막 입력 $in) → 안 잠듦"
            Write-Status "$why — 키보드·마우스 입력이 있어 안 잠듦(마지막 입력 $in, 입력 없이 10분 지나면 잠듦)" '-'
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

$TrialFile = Join-Path $Dir 'trial.json'   # {"total":5,"left":5,"first":N,"started":"..."} — 있으면 그 회차 수만 돌고 보고 후 끔
function End-Trial($tr, $why) {
    $range = "회차 $($tr.first)-$($st.round), 시작 $($tr.started)$(if ($why) { ", 일찍 끝남: $why" })"
    Log "시험 운전 끝($range) → 보고 세션"
    Write-Status "시험 운전 끝($range): 결과를 history에 올리고 컴퓨터를 끔" '-'
    $env:VH_TRIAL = $range
    $free = @(0, 1 | Where-Object { -not $st.until[$_] -or [datetime]$st.until[$_] -le (Get-Date) })
    $c = Start-Claude $Accounts[$(if ($free.Count) { $free[0] } else { $st.acct })] $Repo 'scripts/vh-trial-prompt.txt' '[보고] '
    if (-not $c.P.WaitForExit(20 * 60000)) { Log '보고 세션 시간 초과 → 끔'; & taskkill /T /F /PID $c.P.Id | Out-Null }
    $r = End-Claude $c (Join-Path $Dir "trial-report-$($st.round).txt")
    Log "보고: $(@($r.Text -split "`r?`n" | Where-Object { $_ -match '^\s*REPORT:' } | Select-Object -Last 1))"
    Remove-Item $TrialFile -Force
    [IO.File]::WriteAllText((Join-Path $Dir 'stop'), "trial done $(Get-Date -Format 'MM-dd HH:mm') — 다시 돌리려면 이 파일을 지움", $Utf8)
    Prepare-Git
    Write-Status "멈춤: 시험 운전 끝($range) — history.md 맨 위에 결과. 다시 돌리려면 %USERPROFILE%\vh-loop\stop을 지움" '-'
    # 사용자 10-26 밤: 다 돌면 컴퓨터를 끈다(절전 아님). 2분 여유 — 그사이 사람이 있으면 shutdown /a로 취소
    Log '시험 운전 끝 → 컴퓨터 끔(2분 뒤)'
    [void][VH.Power]::SetThreadExecutionState($Release)
    & shutdown /s /t 120 /c "발헤임 시험 운전 끝 - 2분 뒤 꺼짐(취소: shutdown /a)"
}
$st = Load-State
Log "루프 시작$(if ($Once) { ' (한 회차만)' })"
$Gate = $null; $HandoffOn = $false
$hr = Join-Path $Dir 'handoff-running.json'
if (Test-Path $hr) {
    # 인계 도중 루프가 꺼졌음(재시작·절전 실패 등): 결과가 없다는 것을 다음 회차에 알린다(PASS 아님, 같은 커밋을 다시 인계하거나 직접 시험)
    $j = try { Get-Content $hr -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $null }
    $f = Join-Path $Dir 'verify-result.md'
    [IO.File]::WriteAllText($f, "# 검증 인계 중단(루프가 도중에 꺼짐)`n- 회차 $($j.round), 기준 커밋 $($j.base), 작업 $($j.task) — 결과 없음, PASS 아님`n- 원본: $Dir\harness\$($j.task) (state.json 단계). 같은 커밋을 다시 인계하거나 직접 시험한다.`n", $Utf8)
    $st.verify = $f; Remove-Item $hr -Force; Log "지난 인계가 도중에 끊김(작업 $($j.task)) → 다음 회차에 알림"
}
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
        Log '멈춤 파일 → 끝'; Write-Status $(if (Test-Path $mark) { '다시 켜는 중: 새 루프 스크립트로(1~2분)' } else { '멈춤: 멈춤 파일이 있음' }) '-'
        if (Test-Path $mark) {
            # 다시 켜기: 기다리는 작업(vh-loop-restart.ps1 -Wait)이 PC를 깨워 둔 뒤에 끝낸다. 사람 없이 예약으로 깨운 PC는
            # 깨워 두는 프로그램이 없는 순간 바로 잠든다(10-23 09:18: 옛 루프 끝 → 2초 뒤 잠듦, 11:44 사람이 깨울 때까지).
            $w = Get-Date
            while (-not (Test-Path (Join-Path $Dir 'restart-awake')) -and ((Get-Date) - $w).TotalSeconds -lt 30) { Start-Sleep 1 }
        }
        break
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
    $gid = Get-GameId; $GameUpdate = ''; $GameNote = ''
    if ($gid -and $st.game -and $gid -ne $st.game) {
        $GameUpdate = "$($st.game) → $gid"
        $GameNote = "게임 업데이트 감지: $GameUpdate (확인된 게임 사실은 1.0.20 기준) — 회차 $($st.round)이 전체 회귀부터"
        Log $GameNote
    }
    $Gate = Get-VhHandoffGate $Dir $Repo $st.astraUntil; $HandoffOn = $Gate.on
    Log "분업 $($Gate.why)"
    if ((Test-Path $TrialFile) -and $Gate.baseline -ge 5) {
        # 시험 운전(사용자 10-26): 기준 회차가 일찍 끝나도 ChatGPT 한도가 풀리기 전엔 분업 회차를 시작하지 않고 절전했다가 잇는다
        $af = (Get-Content $TrialFile -Raw -Encoding UTF8 | ConvertFrom-Json).astraFrom
        if ($af -and [datetime]$af -gt (Get-Date)) {
            $st.round--; Log "시험 운전: 기준 끝, ChatGPT 준비 $(([datetime]$af).ToString('MM-dd HH:mm'))까지 쉼"
            Write-Status "시험 운전: 기준 5회차 끝 — ChatGPT 한도가 풀리는 $(([datetime]$af).ToString('MM-dd HH:mm'))까지 쉼(입력 10분 없으면 절전)" '-'
            Save-State; Wait-Until ([datetime]$af); continue
        }
    }
    $plan = Join-Path $Dir 'handoff-plan.json'   # 지난 회차가 HANDOFF 없이 남긴 계획은 쓰지 않는다
    if (Test-Path $plan) { Move-Item $plan (Join-Path $Dir "handoff-plan.stale-$(Get-Date -Format 'yyyyMMdd-HHmmss').json") -Force }
    Write-Status "회차 $($st.round) 진행 중" '(진행 중)'
    $before = G rev-parse origin/valheim | Select-Object -Last 1
    $st.reqSeen = G rev-parse 'HEAD:docs/valheim/UserRequest.txt' | Select-Object -Last 1   # 이 회차가 0번에서 읽을 요청(이후 바뀌면 백업 때 따로 답)
    $st.replyFails = 0
    Log "회차 $($st.round) 시작 ($($acct.Name) 계정)"
    $Host.UI.RawUI.WindowTitle = "VH loop - 회차 $($st.round)"
    $script:LastUsage = $null
    $r = Run-Claude $acct (Join-Path $Dir "round-$($st.round).txt")
    $roundUsage = $script:LastUsage   # 뒤의 답 전용 세션(인계 대기 중)이 덮기 전에
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
    if ($gid) { $st.game = $gid }   # 알린 회차가 끝났으면 본 것으로(한도·금방 끝난 오류면 위에서 continue → 다음 회차에 다시 알림)
    $last = $last.Trim()
    Log $last
    $st.verify = ''   # 이 회차가 받은 검증 결과는 다 넘겨 줌
    $handoff = $last -match '^LOOP: HANDOFF'
    if ($handoff -and -not $HandoffOn) { Log "분업 꺼짐($($Gate.why))인데 HANDOFF → 인계 안 함(progress에 검증 대기로 남음)"; $handoff = $false }
    Prepare-Git
    $after = G rev-parse origin/valheim | Select-Object -Last 1
    # 분업(decisions.txt 2절): 회차가 커밋한 것을 스크립트가 빌드·Core·게임 시험하고 Astra가 리뷰(모델 세션 없이 기다림), 결과는 다음 회차에 VH_VERIFY로.
    # 구현 회차 + 그 검증이 한 논리 회차: 검증 통과면 진척은 다음 회차가 [끝]을 적을 때 세고, 아니면 끝낸 것 없음으로 센다.
    $e = $null; $vstop = $null; $vtext = ''
    if ($handoff) {
        $hb = Get-Date; $script:HandoffBk = Get-Date
        $wait = { if (((Get-Date) - $script:HandoffBk).TotalMinutes -ge $BackupMin) { Backup-Wip; Reply-Requests $acct; $script:HandoffBk = Get-Date } }
        Write-Status "회차 $($st.round) 인계 검증 중(빌드·게임 시험·Astra 리뷰 — 스크립트가 함)" $last   # Prepare 전에 커밋(검증 중에는 HEAD를 안 바꿈)
        $v = Invoke-VhHandoff $Repo $Dir $before $RoundTimeoutMin $wait $st.round   # 기준 = 회차 시작 때 커밋, 대상 = 지금 HEAD
        $st.verify = $v.File; $e = $v.Effect
        $vtext = if ($e) { "$($v.Id) $($e.stage), 리뷰 $($e.review)$(if ($e.fail.Count) { ", FAIL $($e.fail -join ',')" })" } else { '준비 실패' }
        Log "인계 검증: $vtext ($([math]::Round(((Get-Date) - $hb).TotalMinutes))분) → $($v.File)"
        $last = "$last · 검증: $vtext"
        if ($e -and $e.fail.Count) {
            $key = (@($e.fail) | Sort-Object) -join ','
            if ($st.verifyFail -eq $key) { $st.verifyFailN++ } else { $st.verifyFail = $key; $st.verifyFailN = 1 }
            # 예전엔 회차 안에서 한 번 고쳐 다시 돌렸다: 처음 구현 FAIL + 고친 회차 두 번 FAIL = 예전 "같은 시험 2회차 연속 FAIL"
            if ($st.verifyFailN -ge 3) { $vstop = "같은 시험 검증 3번 연속 FAIL($key)" }
        } else { $st.verifyFail = ''; $st.verifyFailN = 0 }
        if ($e -and $e.astra_down) {
            $st.astraFails++
            if ($e.retry_at) { $st.astraUntil = "$($e.retry_at)"; $how = 'Codex가 알려 준 해제 시각' }   # 한도 문구의 "try again at …"
            else { $h = [math]::Min(24, 3 * [math]::Pow(2, $st.astraFails - 1)); $st.astraUntil = (Get-Date).AddHours($h).ToString('o'); $how = "${h}시간 뒤 다시 시도(해제 시각 아님)" }
            Log "Astra 리뷰 못 함($($e.review)) → $(([datetime]$st.astraUntil).ToString('MM-dd HH:mm'))까지 분업 끔($how, 그동안 회차가 직접 시험·리뷰)"
        } elseif ($e -and $e.review -eq 'COMPLETE') { $st.astraFails = 0 }
    }
    if (Test-RoundResult $before $after $st.round) { $st.noProg = 0 }
    elseif (-not ($e -and $e.complete)) { $st.noProg++; Log "끝낸 것 없는 회차 $($st.noProg)/3" }
    $stop = $null
    if ($last -match '^LOOP: NEED_USER') { $stop = '사용자 확인 필요' }
    elseif ($last -match '^LOOP: ALLDONE') { $stop = '할 일 없음' }
    elseif ($last -match '^LOOP: FAIL (\S+)') {
        if ($st.lastFail -eq $Matches[1]) { $stop = "같은 시험 2회차 연속 FAIL($($Matches[1]))" }
        $st.lastFail = $Matches[1]
    } else { $st.lastFail = '' }
    if (-not $stop -and $vstop) { $stop = $vstop }
    if (-not $stop -and $st.noProg -ge 3) { $stop = '3회차 연속 근거 있는 완료 없음(커밋·시험 붙은 [끝]·질문 답·닫은 문제 없음)' }
    if ($last -match '^LOOP: ERROR') { Start-Sleep 300 }   # 네트워크 등 → 5분 쉬고
    if ($stop -and $Once) { Write-Status "멈춤: $stop" $last; Save-State; Log "멈춤: $stop"; break }
    if (-not $stop) { Write-Status $(if ($Once) { '한 회차 끝(시험 실행)' } else { '다음 회차 준비' }) $last }
    Save-State
    if (Test-Path $TrialFile) {
        # 시험 운전(사용자 10-26): 정한 회차 수를 돌면(또는 멈춤 조건이면) 보고 세션 → 멈춤 파일 → 컴퓨터 끔
        # 회차마다 그 회차 history 항목에 토큰 줄을 바로 붙여 올린다(사용자 10-26: 한도로 중간에 멈춰도 남게). 항목이 없으면 새 항목.
        if ($roundUsage) {
            $u = $roundUsage
            $note = "**토큰**(루프 기록) · 입력 $($u.input_tokens) · 출력 $($u.output_tokens) · 캐시 읽기 $($u.cache_read_tokens) · 캐시 쓰기 $($u.cache_write_tokens) · $($u.turns)턴 · $([math]::Round([double]$u.elapsed_minutes))분 · $(if ($u.mode -eq 'handoff') { '분업' } else { 'Claude만(기준)' })$(if ($vtext) { " · 검증: $vtext" })"
            $nf = Join-Path $Dir 'round-note.txt'; [IO.File]::WriteAllText($nf, "$note`n", $Utf8)
            $bash = 'C:\Program Files\Git\bin\bash.exe'
            $hp = Start-Process $bash -ArgumentList 'scripts/vh-history-note.sh', "`"회차 $($st.round) ·`"" -WorkingDirectory $Repo -RedirectStandardInput $nf -NoNewWindow -Wait -PassThru
            if ($hp.ExitCode) {
                [IO.File]::WriteAllText($nf, "### 회차 $($st.round) · $(Get-Date -Format 'MM-dd') · (회차가 기록을 못 남김 — 루프가 남긴 토큰)`n`n**요약**`n`n- $last`n`n$note`n", $Utf8)
                [void](Start-Process $bash -ArgumentList 'scripts/vh-history-add.sh' -WorkingDirectory $Repo -RedirectStandardInput $nf -NoNewWindow -Wait -PassThru)
            }
            G add docs/valheim/history.md | Out-Null; G commit -q -m "Valheim: round $($st.round) usage" -- docs/valheim/history.md | Out-Null
            G push -q origin valheim | Out-Null
            if ($LASTEXITCODE -ne 0) { G pull -q --rebase --autostash origin valheim | Out-Null; G push -q origin valheim | Out-Null }
        }
        $tr = Get-Content $TrialFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $tr.left = [int]$tr.left - 1; [IO.File]::WriteAllText($TrialFile, ($tr | ConvertTo-Json), $Utf8)
        Log "시험 운전: 남은 회차 $($tr.left)"
        if ($tr.left -le 0 -or $stop) { End-Trial $tr $stop; break }
    }
    if ($stop) {
        if (-not (Wait-UserRequest $stop $last)) { Log '멈춤 파일 → 끝'; Write-Status '멈춤: 멈춤 파일이 있음' '-'; break }
        $st.noProg = 0; $st.lastFail = ''; Save-State; continue
    }
    if ($Once) { break }
}
Log '루프 끝'
[void][VH.Power]::SetThreadExecutionState($Release)
$mutex.ReleaseMutex()   # -NoExit 창이 열려 있어도 다음 루프가 시작되게
