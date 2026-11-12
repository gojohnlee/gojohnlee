# 컴퓨터 안 끄기 지킴이(사용자 11-13: "밖이라 컴터가 꺼지면 손쓸 방법이 없어").
# 작업 "VH keep on"이 돌린다. 끄려면: Stop-ScheduledTask 'VH keep on' 그리고 %USERPROFILE%\vh-loop\stay-on 지움.
# - 이 프로세스가 사는 동안 Windows 유휴 절전을 막는다(SetThreadExecutionState).
# - 20초마다 shutdown /a: 예약된 끄기(루프의 shutdown /s /t 120 등)를 취소한다.
# - stay-on 파일을 둔다: vh-loop.ps1 Test-Away가 이걸 보면 끄기·최대 절전을 아예 안 한다.
# - 루프 작업 "VH loop"가 3분 넘게 안 돌고 있으면(멈춤 파일 없을 때) 다시 켠다.
$ErrorActionPreference = 'Continue'
$Dir = Join-Path $env:USERPROFILE 'vh-loop'
$Repo = Split-Path $PSScriptRoot -Parent
$LogFile = Join-Path $Dir 'keepon.log'
$Utf8 = New-Object Text.UTF8Encoding $false
function Log($m) { [IO.File]::AppendAllText($LogFile, "$(Get-Date -Format 'MM-dd HH:mm:ss') $m`r`n", $Utf8) }

Add-Type -Namespace VHK -Name Power -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
$m = New-Object Threading.Mutex($false, 'Global\VHKeepOn')
if (-not $m.WaitOne(0)) { exit }   # 이미 하나 돎

# 임시 조치(사용자 11-13): 11-13 18:30이 지나거나 루프가 할 일을 다 끝내면 지킴이를 스스로 지운다(답 기다림이면 안 지움)
# (stay-on 지움 → 루프는 원래대로 자리 비우면 끔, 작업 "VH keep on" 지움, 끝).
$Deadline = [datetime]'2026-11-13 18:30'
function Test-LoopIdle {
    $l = Get-Content (Join-Path $Dir 'loop.log') -Tail 200 -Encoding UTF8 -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '회차 \d+ 시작|루프 시작|다시 돎|멈춤:|할 일 없음|답 기다림|멈춤 파일' } | Select-Object -Last 1
    # 답 기다림(멈춤:)은 할 일 없음이 아니다 — 사용자가 답할 수 있게 PC를 켜 둔다(사용자 11-13). 다 끝남·멈춤 파일만.
    [bool]($l -and $l -match '할 일 없음|멈춤 파일')
}
# 텔레그램 알림(사용자 11-13): %USERPROFILE%\vh-loop\telegram.txt 첫 줄 봇 토큰, 둘째 줄 chat id(저장소에 안 올림). 없으면 조용히 건너뜀.
function Send-Tg($text) {
    $c = @(Get-Content (Join-Path $Dir 'telegram.txt') -Encoding UTF8 -ErrorAction SilentlyContinue | Where-Object { $_.Trim() })
    if ($c.Count -lt 2) { Log "텔레그램 못 보냄(chat id 없음): $text"; return }
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $b = @{ chat_id = $c[1].Trim(); text = $text } | ConvertTo-Json -Compress
        [void](Invoke-RestMethod "https://api.telegram.org/bot$($c[0].Trim())/sendMessage" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($b)) -TimeoutSec 30)
        Log "텔레그램 보냄: $text"
    } catch { Log "텔레그램 실패: $($_.Exception.Message)" }
}
$LoopLog = Join-Path $Dir 'loop.log'
$script:Seen = @(Get-Content $LoopLog -Encoding UTF8 -ErrorAction SilentlyContinue).Count
# 루프 기록에 새로 멈춤·질문·다 끝남이 찍히면 한 번씩 텔레그램.
function Watch-LoopLog {
    $all = @(Get-Content $LoopLog -Encoding UTF8 -ErrorAction SilentlyContinue)
    if ($all.Count -lt $script:Seen) { $script:Seen = 0 }
    $new = @($all | Select-Object -Skip $script:Seen); $script:Seen = $all.Count
    foreach ($l in $new) {
        if ($l -match '멈춤:|LOOP: NEED_USER') { Send-Tg "발헤임 루프가 멈춰 네 답을 기다려. $($l.Substring([math]::Min(15, $l.Length)))`n답: GitHub gojohnlee/gojohnlee valheim 브랜치 docs/valheim/UserRequest.txt 구분선 아래에 쓰면 5분 안에 다시 돎(PC는 켜 둠)." }
        elseif ($l -match '할 일 없음') { Send-Tg "발헤임 루프: 할 일 다 끝남. 지킴이 지우고 원래대로(자리 비우면 PC 끔)." }
    }
}
# 회차 기록(사용자 11-13): 루프가 history.md에 새 회차 기록을 올리면 그 "자세히" 부분만(맨 위 제목 한 줄과) 텔레그램.
# 루프 작업 트리는 건드리지 않고 푸시된 origin/valheim에서 읽는다.
function Get-HistEntries {
    $lines = @(git -C $Repo show 'origin/valheim:docs/valheim/history.md' 2>$null)
    $i = 0; while ($i -lt $lines.Count -and $lines[$i] -notmatch '^## 회차 기록') { $i++ }
    $out = @(); $cur = $null
    for ($j = $i + 1; $j -lt $lines.Count -and $out.Count -lt 40; $j++) {
        if ($lines[$j] -match '^## ') { break }
        if ($lines[$j] -match '^### (.+)') { if ($cur) { $out += $cur }; $cur = @{ Title = $Matches[1].Trim(); Body = @() }; continue }
        if ($cur) { $cur.Body += $lines[$j] }
    }
    if ($cur) { $out += $cur }
    foreach ($e in $out) {
        $k = [array]::IndexOf(@($e.Body | ForEach-Object { $_.Trim() }), '**자세히**')
        $e.Detail = if ($k -ge 0) { (@($e.Body | Select-Object -Skip ($k + 1)) -join "`n").Trim() -replace '\*\*', '' } else { '' }
    }
    $out
}
$script:HistSeen = New-Object 'System.Collections.Generic.HashSet[string]'
function Send-NewHistory($first) {
    $es = @(Get-HistEntries)
    if (-not $es.Count) { return }
    if ($first) { foreach ($e in $es) { [void]$script:HistSeen.Add($e.Title) }; return }   # 사용자 11-13: 지난 것은 안 보내고 앞으로 끝난 것만
    [array]::Reverse($es)   # 오래된 것부터
    foreach ($e in $es) {
        if (-not $script:HistSeen.Add($e.Title)) { continue }
        if (-not $e.Detail) { continue }
        $t = "$($e.Title)`n`n$($e.Detail)"
        if ($t.Length -gt 3900) { $t = $t.Substring(0, 3900) + '…' }
        Send-Tg $t
    }
}
function Remove-KeepOn($why) {
    Send-Tg "발헤임 지킴이 지움: $why. 이제 루프가 멈추면 자리 비울 때 PC가 꺼질 수 있어."
    Remove-Item (Join-Path $Dir 'stay-on') -Force -ErrorAction SilentlyContinue
    Log "지킴이 지움: $why (stay-on 지움, 작업 VH keep on 지움)"
    Unregister-ScheduledTask -TaskName 'VH keep on' -Confirm:$false -ErrorAction SilentlyContinue
    exit
}

[IO.File]::WriteAllText((Join-Path $Dir 'stay-on'), "1", $Utf8)
Log "지킴이 시작($($Deadline.ToString('MM-dd HH:mm'))까지 또는 루프가 할 일 없이 멈출 때까지)"
$down = 0; $tick = 0
[Console]::OutputEncoding = [Text.Encoding]::UTF8   # git show 한글
Send-NewHistory $true
while ($true) {
    if ((Get-Date) -ge $Deadline) { Remove-KeepOn "$($Deadline.ToString('HH:mm')) 지남" }
    Watch-LoopLog
    if (Test-LoopIdle) { Remove-KeepOn '루프가 할 일을 다 끝냄' }
    [void][VHK.Power]::SetThreadExecutionState([uint32]'0x80000001')   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED
    & shutdown /a 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Log '예약된 컴퓨터 끄기 취소함(shutdown /a)' }
    if (-not (Test-Path (Join-Path $Dir 'stay-on'))) { [IO.File]::WriteAllText((Join-Path $Dir 'stay-on'), "1", $Utf8) }
    $tick++
    if ($tick % 3 -eq 0) {
        Send-NewHistory $false
        $stop = (Test-Path (Join-Path $Repo 'docs\valheim\loop-stop.txt')) -or (Test-Path (Join-Path $Dir 'stop'))
        $run = @('VH loop', 'VH loop restart' | ForEach-Object { (Get-ScheduledTask -TaskName $_ -ErrorAction SilentlyContinue).State }) -contains 'Running'
        if ($run -or $stop) { $down = 0 }
        else {
            $down++
            if ($down -ge 3) {
                Remove-Item (Join-Path $Dir 'done') -Force -ErrorAction SilentlyContinue
                Start-ScheduledTask -TaskName 'VH loop'
                Log '루프가 3분 넘게 안 돌아서 다시 켬'
                $down = 0
            }
        }
    }
    Start-Sleep 20
}
