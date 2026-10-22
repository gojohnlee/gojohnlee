# 발헤임 무인 루프 (설계: docs/valheim/loop-plan.txt). 작업 스케줄러 "VH loop"가 로그인 때 숨김으로 실행한다.
# 회차: pull → 멈춤 파일 확인 → 계정 고르기 → claude -p 새 세션(scripts/vh-loop-prompt.txt) → 결과 판정 → status.txt 푸시.
# 15분마다 작업 트리를 valheim-wip에 백업(작업 트리·인덱스는 안 건드림). 금방 끝난 오류는 그 계정만 잠깐 쉬게 한다.
# 백업 때 회차 시작 뒤 UserRequest.txt가 바뀌었으면 답 전용 짧은 claude(scripts/vh-reply-prompt.txt)를 따로 작업 트리에서 돌려 먼저 답한다.
# 두 계정 모두 한도(또는 쉼)면 풀리는 시각까지 기다리고,
# 자리 비움(입력 없음 10분)이면 깨우기 예약 후 최대 절전(30초 예약은 못 깨움 → 10분 넘게 남았을 때만).
# 멈추기: 저장소에 docs/valheim/loop-stop.txt를 만든다(GitHub 앱에서도 됨) 또는 %USERPROFILE%\vh-loop\stop 파일.
# 쉬기: docs/valheim/loop-pause.txt에 다시 시작할 시각("yyyy-MM-dd HH:mm")을 쓴다(아래 Get-PauseUntil).
# 밖에서 요청: docs/valheim/UserRequest.txt(회차가 먼저 처리, 답은 UserReply.txt). 멈춤 조건이면 끝내지 않고 그 파일이 바뀔 때까지 기다린다.
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
$Awake = [uint32]2147483649   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED: 도는 동안 저절로 잠들지 않게
$Release = [uint32]2147483648
[void][VH.Power]::SetThreadExecutionState($Awake)

function Log($m) { $l = "$(Get-Date -Format 'MM-dd HH:mm:ss') $m"; [IO.File]::AppendAllText($LogFile, "$l`r`n", $Utf8); Write-Host $l }
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
    if ($tree -eq (G rev-parse 'HEAD^{tree}' | Select-Object -Last 1)) { return }   # 바뀐 것 없음
    $c = G commit-tree $tree -p (G rev-parse HEAD | Select-Object -Last 1) -m "WIP backup $(Get-Date -Format 'MM-dd HH:mm')" | Select-Object -Last 1
    G push -q -f origin "${c}:refs/heads/valheim-wip" | Out-Null
    Log "valheim-wip 백업 $c"
}

function Write-Status($now, $last) {
    # 할 일: progress "★ 지금 …" 머리줄 아래 들여 쓴 줄들에서 [끝]이 아닌 첫 번호 항목, 없으면 머리줄(머리줄 이름이 바뀌어도 비지 않게)
    $p = @(Get-Content (Join-Path $Repo 'docs\valheim\progress.txt') -Encoding UTF8)
    $todo = ''
    for ($i = 0; $i -lt $p.Count; $i++) {
        if ($p[$i] -notmatch '^★ 지금') { continue }
        for ($j = $i + 1; $j -lt $p.Count -and $p[$j] -match '^\s+\S'; $j++) {
            if ($p[$j] -match '^\s+\d+\)' -and $p[$j] -notmatch '^\s+\d+\)\s*\[끝') { $todo = $p[$j]; break }
        }
        if (-not $todo) { $todo = $p[$i] }
        break
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
        "요청은 이 폴더 UserRequest.txt에 쓴다(답은 UserReply.txt, 돌고 있는 회차가 있어도 15분마다 확인). 멈추려면 loop-stop.txt를 만든다(내용 아무거나)."
    ) + @(if ($StaleNote) { $StaleNote }) -join "`n"
    $f = Join-Path $Repo 'docs\valheim\status.txt'
    [IO.File]::WriteAllText($f, "$text`n", $Utf8)
    G add docs/valheim/status.txt | Out-Null
    G commit -q -m 'Valheim loop status' -- docs/valheim/status.txt | Out-Null
    G push -q origin valheim | Out-Null
    if ($LASTEXITCODE -ne 0) { G pull -q --rebase --autostash origin valheim | Out-Null; G push -q origin valheim | Out-Null }
}

function Start-Claude($acct, $workDir, $prompt) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $Claude
    $psi.Arguments = "-p `"Read $prompt and follow it exactly.`" --dangerously-skip-permissions"
    $psi.WorkingDirectory = $workDir
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $Utf8; $psi.StandardErrorEncoding = $Utf8
    if ($acct.Config) { $psi.EnvironmentVariables['CLAUDE_CONFIG_DIR'] = $acct.Config } else { $psi.EnvironmentVariables.Remove('CLAUDE_CONFIG_DIR') }
    # 배경 서브에이전트가 남은 채 턴이 끝나도 10분 뒤 끊지 않고 끝까지 기다린다(회차 54가 그렇게 끊겨 LOOP 줄 없이 끝남). 회차 시간 제한은 그대로.
    $psi.EnvironmentVariables['CLAUDE_CODE_PRINT_BG_WAIT_CEILING_MS'] = '0'
    $psi.EnvironmentVariables['VH_LOOP_HASH'] = $SelfHash   # 회차 claude가 vh-loop-restart.ps1로 옛 루프인지 본다
    $p = [Diagnostics.Process]::Start($psi)
    @{ P = $p; So = $p.StandardOutput.ReadToEndAsync(); Se = $p.StandardError.ReadToEndAsync(); Start = Get-Date }
}

function End-Claude($c, $outFile) {
    $c.P.WaitForExit()
    $text = "$($c.So.Result)`n$($c.Se.Result)"
    [IO.File]::WriteAllText($outFile, $text, $Utf8)
    @{ Code = $c.P.ExitCode; Text = $text; Min = ((Get-Date) - $c.Start).TotalMinutes }
}

function Run-Claude($acct, $outFile) {
    $c = Start-Claude $acct $Repo 'scripts/vh-loop-prompt.txt'
    $bk = Get-Date
    while (-not $c.P.WaitForExit(60000)) {
        if (((Get-Date) - $bk).TotalMinutes -ge $BackupMin) { Backup-Wip; Reply-Requests $acct; $bk = Get-Date }
        if (((Get-Date) - $c.Start).TotalMinutes -ge $RoundTimeoutMin) { Log '회차 시간 초과 → claude 끔'; & taskkill /T /F /PID $c.P.Id | Out-Null; break }
    }
    End-Claude $c $outFile
}

# 15분 백업 때마다: 회차가 시작한 뒤 UserRequest.txt가 바뀌었으면(사용자가 GitHub에서 새로 씀) 회차 끝을 기다리지 않고
# 답 전용 claude를 짧게 따로 돌려 UserReply.txt에 먼저 답한다(사용자 결정 2026-10-22). 회차의 작업 트리·게임은 안 건드린다:
# 따로 작업 트리($ReplyDir, origin/valheim의 분리된 HEAD)에서 UserReply.txt만 커밋해 valheim에 푸시.
# 회차도 같은 파일 끝에 덧붙이므로 .gitattributes가 UserReply.txt를 merge=union으로 둔다(합칠 때 양쪽 줄을 다 남김).
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
    $c = Start-Claude $acct $ReplyDir 'scripts/vh-reply-prompt.txt'
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
    # 기다리는 동안 30초마다 본다: 10분 넘게 남았고 자리 비움(입력 없음 10분)이면 최대 절전(사용자가 쓰는 중이면 그냥 기다림)
    $bk = $false; $noHib = $false
    while ((Get-Date) -lt $t) {
        if (-not $noHib -and ($t - (Get-Date)).TotalMinutes -gt 10 -and [VH.Power]::IdleMs() -gt 600000) {
            if (-not $bk) { Backup-Wip; $bk = $true }
            $act = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit'
            $set = New-ScheduledTaskSettingsSet -WakeToRun -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
            Register-ScheduledTask -TaskName 'VH loop wake' -Action $act -Trigger (New-ScheduledTaskTrigger -Once -At $t) -Settings $set -Force | Out-Null
            Log "최대 절전, $($t.ToString('MM-dd HH:mm')) 깨우기 예약"
            [void][VH.Power]::SetThreadExecutionState($Release)
            & shutdown /h
            if ($LASTEXITCODE) { Log "최대 절전 못 함(코드 $LASTEXITCODE) → 켠 채로 기다림"; $noHib = $true }
            # 깨우기 예약으로 켜진(사람 없는) PC는 2분 안에 다시 잠든다 → 깨어나면 곧바로 Awake.
            # Start-Sleep은 절전 동안 시간이 안 흐르므로 벽시계로 2분을 잰다(절전 들어가는 동안 다시 걸지 않게).
            $h = Get-Date
            while (((Get-Date) - $h).TotalSeconds -lt 120) { Start-Sleep 2 }
            [void][VH.Power]::SetThreadExecutionState($Awake)
            if (((Get-Date) - $h).TotalSeconds -gt 150) { Log '깨어남' }
            continue
        }
        Start-Sleep 30
    }
    [void][VH.Power]::SetThreadExecutionState($Awake)
    Log '기다림 끝'
}

# 멈춤 조건(사용자 확인 필요·할 일 없음·같은 FAIL·진척 없음)이면 끝내지 않고 사용자 요청을 기다린다: 밖에서 GitHub으로
# docs/valheim/UserRequest.txt를 고치면 다시 돈다(5분마다 pull해서 그 파일이 바뀌었나 봄). 기다리는 동안 PC는 깨어 있다. 멈춤 파일이면 $false.
function Wait-UserRequest($why, $last) {
    $f = 'origin/valheim:docs/valheim/UserRequest.txt'
    $h0 = G rev-parse $f | Select-Object -Last 1
    Log "멈춤: $why → UserRequest.txt가 바뀔 때까지 기다림"
    Write-Status "답 기다림: $why — UserRequest.txt에 쓰면 5분 안에 이어 감(물은 것은 UserReply.txt)" $last
    while ($true) {
        Start-Sleep 300
        Prepare-Git
        if (Test-StopFile) { return $false }
        if ((G rev-parse $f | Select-Object -Last 1) -ne $h0) { Log 'UserRequest.txt 바뀜 → 다시 돎'; return $true }
    }
}

$st = Load-State
Log "루프 시작$(if ($Once) { ' (한 회차만)' })"
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
    if ($after -eq $before) { $st.noProg++ } else { $st.noProg = 0 }
    $stop = $null
    if ($last -match '^LOOP: NEED_USER') { $stop = '사용자 확인 필요' }
    elseif ($last -match '^LOOP: ALLDONE') { $stop = '할 일 없음' }
    elseif ($last -match '^LOOP: FAIL (\S+)') {
        if ($st.lastFail -eq $Matches[1]) { $stop = "같은 시험 2회차 연속 FAIL($($Matches[1]))" }
        $st.lastFail = $Matches[1]
    } else { $st.lastFail = '' }
    if (-not $stop -and $st.noProg -ge 3) { $stop = '3회차 진척 없음(푸시 없음)' }
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
