# 발헤임 무인 루프 (설계: docs/valheim/loop-plan.txt). 작업 스케줄러 "VH loop"가 로그인 때 숨김으로 실행한다.
# 회차: pull → 멈춤 파일 확인 → 계정 고르기 → claude -p 새 세션(scripts/vh-loop-prompt.txt) → 결과 판정 → status.txt 푸시.
# 15분마다 작업 트리를 valheim-wip에 백업(작업 트리·인덱스는 안 건드림). 두 계정 모두 한도면 풀리는 시각까지 기다리고,
# 자리 비움(입력 없음 10분)이면 깨우기 예약 후 최대 절전(30초 예약은 못 깨움 → 10분 넘게 남았을 때만).
# 멈추기: 저장소에 docs/valheim/loop-stop.txt를 만든다(GitHub 앱에서도 됨) 또는 %USERPROFILE%\vh-loop\stop 파일.
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
$Diag = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\plugins\ValheimHelper\diag.txt'
$Utf8 = New-Object Text.UTF8Encoding($false)

$mutex = New-Object Threading.Mutex($false, 'Global\VhLoop')
if (-not $mutex.WaitOne(0)) { exit }   # 이미 돌고 있음

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
    $s = @{ round = 0; acct = 0; until = @('', ''); lastFail = ''; noProg = 0 }
    if (Test-Path $StateFile) {
        try { $j = Get-Content $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json; foreach ($k in @($s.Keys)) { if ($null -ne $j.$k) { $s[$k] = $j.$k } } } catch { }
    }
    $s.until = @($s.until); $s.noProg = 0; $s.lastFail = ''   # 다시 켜면 멈춤 셈은 새로
    $s
}
function Save-State { [IO.File]::WriteAllText($StateFile, ($st | ConvertTo-Json), $Utf8) }

function Prepare-Git {
    $lock = Join-Path $Repo '.git\index.lock'
    if ((Test-Path $lock) -and -not (Get-Process git -ErrorAction SilentlyContinue)) { Remove-Item $lock -Force; Log 'index.lock 지움' }
    if ((G branch --show-current | Select-Object -Last 1) -ne 'valheim') { G checkout -q valheim | Out-Null }
    G fetch -q origin | Out-Null
    $out = G merge --ff-only -q origin/valheim
    if ($LASTEXITCODE -ne 0) { Log "pull 못 함: $out" }
}

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
    $todo = Get-Content (Join-Path $Repo 'docs\valheim\progress.txt') -Encoding UTF8 | Where-Object { $_ -match '^★ 지금 할 일' } | Select-Object -First 1
    $test = if (Test-Path $Diag) { "$((Get-Item $Diag).LastWriteTime.ToString('MM-dd HH:mm')) $(Get-Content $Diag -Encoding UTF8 -TotalCount 1)" } else { '(없음)' }
    $lim = @(0, 1 | ForEach-Object { if ($st.until[$_] -and [datetime]$st.until[$_] -gt (Get-Date)) { "$($Accounts[$_].Name) $(([datetime]$st.until[$_]).ToString('MM-dd HH:mm'))까지" } })
    $text = @(
        "갱신: $(Get-Date -Format 'yyyy-MM-dd HH:mm')  (무인 루프, scripts/vh-loop.ps1)",
        "지금: $now",
        "회차: $($st.round), 계정: $($Accounts[$st.acct].Name), 한도: $(if ($lim.Count) { $lim -join ', ' } else { '없음' })",
        "할 일: $todo",
        "마지막 시험: $test",
        "Claude가 한 일: $last",
        "멈추려면 이 폴더에 loop-stop.txt를 만든다(내용 아무거나)."
    ) -join "`n"
    $f = Join-Path $Repo 'docs\valheim\status.txt'
    [IO.File]::WriteAllText($f, "$text`n", $Utf8)
    G add docs/valheim/status.txt | Out-Null
    G commit -q -m 'Valheim loop status' -- docs/valheim/status.txt | Out-Null
    G push -q origin valheim | Out-Null
    if ($LASTEXITCODE -ne 0) { G pull -q --rebase --autostash origin valheim | Out-Null; G push -q origin valheim | Out-Null }
}

function Run-Claude($acct, $outFile) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $Claude
    $psi.Arguments = '-p "Read scripts/vh-loop-prompt.txt and follow it exactly." --dangerously-skip-permissions'
    $psi.WorkingDirectory = $Repo
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $Utf8; $psi.StandardErrorEncoding = $Utf8
    if ($acct.Config) { $psi.EnvironmentVariables['CLAUDE_CONFIG_DIR'] = $acct.Config } else { $psi.EnvironmentVariables.Remove('CLAUDE_CONFIG_DIR') }
    $p = [Diagnostics.Process]::Start($psi)
    $so = $p.StandardOutput.ReadToEndAsync(); $se = $p.StandardError.ReadToEndAsync()
    $start = Get-Date; $bk = Get-Date
    while (-not $p.WaitForExit(60000)) {
        if (((Get-Date) - $bk).TotalMinutes -ge $BackupMin) { Backup-Wip; $bk = Get-Date }
        if (((Get-Date) - $start).TotalMinutes -ge $RoundTimeoutMin) { Log '회차 시간 초과 → claude 끔'; & taskkill /T /F /PID $p.Id | Out-Null; break }
    }
    $p.WaitForExit()
    $text = "$($so.Result)`n$($se.Result)"
    [IO.File]::WriteAllText($outFile, $text, $Utf8)
    @{ Code = $p.ExitCode; Text = $text }
}

# 한도 문구는 아직 실제로 못 봄(2026-10-21) → 넓게 잡고 출력 원문은 round-N.txt에 남는다. 풀리는 시각을 못 읽으면 1시간 뒤.
function Limit-Until($tail) {
    if ($tail -notmatch '(?i)(usage limit|limit reached|hit your limit|limit will reset|out of extra usage)') { return $null }
    if ($tail -match '\|(\d{10})') { return [DateTimeOffset]::FromUnixTimeSeconds([long]$Matches[1]).LocalDateTime.AddMinutes(2) }
    if ($tail -match '(?i)resets?\s+(?:at\s+)?(\d{1,2})(?::(\d{2}))?\s*(am|pm)?') {
        $h = [int]$Matches[1]; $m = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
        if ($Matches[3] -eq 'pm' -and $h -lt 12) { $h += 12 }
        if ($Matches[3] -eq 'am' -and $h -eq 12) { $h = 0 }
        $t = (Get-Date).Date.AddHours($h).AddMinutes($m)
        if ($t -le (Get-Date)) { $t = $t.AddDays(1) }
        return $t.AddMinutes(2)
    }
    (Get-Date).AddHours(1)
}

function Wait-Until($t) {
    # 기다리는 동안 30초마다 본다: 10분 넘게 남았고 자리 비움(입력 없음 10분)이면 최대 절전(사용자가 쓰는 중이면 그냥 기다림)
    $bk = $false
    while ((Get-Date) -lt $t) {
        if (($t - (Get-Date)).TotalMinutes -gt 10 -and [VH.Power]::IdleMs() -gt 600000) {
            if (-not $bk) { Backup-Wip; $bk = $true }
            $act = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit'
            $set = New-ScheduledTaskSettingsSet -WakeToRun -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
            Register-ScheduledTask -TaskName 'VH loop wake' -Action $act -Trigger (New-ScheduledTaskTrigger -Once -At $t) -Settings $set -Force | Out-Null
            Log "최대 절전, $($t.ToString('MM-dd HH:mm')) 깨우기 예약"
            [void][VH.Power]::SetThreadExecutionState($Release)
            & shutdown /h
            Start-Sleep 120   # 절전 들어가는 동안 다시 걸지 않게
        }
        Start-Sleep 30
    }
    [void][VH.Power]::SetThreadExecutionState($Awake)
    Log '기다림 끝'
}

$st = Load-State
Log "루프 시작$(if ($Once) { ' (한 회차만)' })"
while ($true) {
    Prepare-Git
    if ((Test-Path (Join-Path $Repo 'docs\valheim\loop-stop.txt')) -or (Test-Path (Join-Path $Dir 'stop'))) {
        Log '멈춤 파일 → 끝'; Write-Status '멈춤: 멈춤 파일이 있음' '-'; break
    }
    $free = @(0, 1 | Where-Object { -not $st.until[$_] -or [datetime]$st.until[$_] -le (Get-Date) })
    if ($free.Count -eq 0) {
        $t = @(0, 1 | ForEach-Object { [datetime]$st.until[$_] } | Sort-Object)[0]
        Write-Status "한도 대기: 두 계정 모두 한도, $($t.ToString('MM-dd HH:mm'))에 다시" '-'
        Save-State; Wait-Until $t; continue
    }
    if ($free -notcontains $st.acct) { $st.acct = $free[0] }
    $acct = $Accounts[$st.acct]
    $st.round++
    Write-Status "회차 $($st.round) 진행 중" '(진행 중)'
    $before = G rev-parse origin/valheim | Select-Object -Last 1
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
    }
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
    Write-Status $(if ($stop) { "멈춤: $stop" } elseif ($Once) { '한 회차 끝(시험 실행)' } else { '다음 회차 준비' }) $last
    Save-State
    if ($stop) { Log "멈춤: $stop"; break }
    if ($Once) { break }
}
Log '루프 끝'
[void][VH.Power]::SetThreadExecutionState($Release)
