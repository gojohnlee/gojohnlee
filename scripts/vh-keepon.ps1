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

# 사이드 루프(사용자 11-13): 끝 시각이 지나거나 루프가 할 일을 다 끝내면 지킴이를 끈다(답 기다림이면 안 끔).
# 끌 때 작업 "VH keep on"은 지우지 않고 '사용 안 함'으로만 둔다 → 다시 켤 땐 scripts/vh-keepon-on.ps1(끝 시각 -Until).
# 끝 시각 = %USERPROFILE%\vh-loop\keepon-until.txt(켤 때 vh-keepon-on.ps1이 씀). 없으면 오늘 18:30, 이미 지났으면 지금부터 12시간.
$UntilFile = Join-Path $Dir 'keepon-until.txt'
$Deadline = $null
try { $Deadline = [datetime]::Parse((Get-Content $UntilFile -ErrorAction Stop | Select-Object -First 1).Trim()) } catch { }
if (-not $Deadline) { $Deadline = (Get-Date).Date.AddHours(18.5); if ((Get-Date) -ge $Deadline) { $Deadline = (Get-Date).AddHours(12) } }
function Test-LoopIdle {
    $l = Get-Content (Join-Path $Dir 'loop.log') -Tail 200 -Encoding UTF8 -ErrorAction SilentlyContinue |
        Where-Object { $_ -notmatch 'REPLY:|LOOP:|usage ' -and $_ -match '회차 \d+ 시작|루프 시작|다시 돎|멈춤:|할 일 없음|답 기다림|멈춤 파일' } | Select-Object -Last 1
    # 답 기다림(멈춤:)은 할 일 없음이 아니다 — 사용자가 답할 수 있게 PC를 켜 둔다(사용자 11-13). 다 끝남·멈춤 파일만.
    # claude가 남긴 줄(REPLY:·LOOP:)은 루프 상태가 아님 — 11-13 11:32 답 전용 세션의 "REPLY: … 할 일 없음"을 다 끝남으로 잘못 보고 꺼짐.
    [bool]($l -and $l -match '할 일 없음 →|멈춤 파일')
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
        if ($l -notmatch 'REPLY:' -and $l -match '멈춤:|LOOP: NEED_USER') { Send-Tg (Get-StopReport $l) }
        elseif ($l -notmatch 'REPLY:|LOOP:' -and $l -match '할 일 없음 →') { Send-Tg "발헤임 루프: 할 일 다 끝남. 지킴이 지우고 원래대로(자리 비우면 PC 끔)." }
    }
}
# 멈춤 알림을 사람이 읽고 바로 답할 수 있게(사용자 11-13): 까닭을 쉬운 말로 + 최근 회차가 한 일 + 루프가 물은 것 + 지금 할 일.
function Get-StopReport($l) {
    git -C $Repo fetch -q origin valheim 2>$null | Out-Null
    $why = ($l -replace '^.*?(멈춤:|LOOP: NEED_USER:?)\s*', '') -replace '\s*→.*$', ''
    $plain = switch -regex ($why) {
        '연속 근거 있는 완료 없음' { '최근 3회차 동안 새로 끝낸 일이 없어(커밋·통과한 시험·닫은 문제 0). 같은 일을 되풀이하거나 어딘가 막혀 있을 수 있어.'; break }
        '진척 없음' { '회차가 돌아도 진척이 없어. 같은 데서 막혀 있을 수 있어.'; break }
        default { $why }
    }
    $es = @(Get-HistEntries)
    $t = "발헤임 루프가 멈췄어 — 네 답 기다림.`n`n왜: $plain"
    $q = $es | Where-Object { $_.Title -match '^질문' } | Select-Object -First 1
    if ($l -match 'NEED_USER' -and $q) { $t += "`n`n루프가 묻는 것: $($q.Title)`n$((@($q.Body) -join "`n").Trim() -replace '\*\*', '')" }
    $recent = @($es | Where-Object { $_.Title -notmatch '^(질문|요청)' } | Select-Object -First 3)
    if ($recent.Count) {
        $t += "`n`n최근 회차:"
        foreach ($e in $recent) { $t += "`n· $($e.Title)" + $(if ($e.Summary) { " — $($e.Summary)" } else { '' }) }
    }
    $todo = @(git -C $Repo show 'origin/valheim:docs/valheim/status.txt' 2>$null) | Where-Object { $_ -match '^할 일:' } | Select-Object -First 1
    if ($todo) { $t += "`n`n지금 할 일: $(($todo -replace '^할 일:\s*', '').Trim())" }
    $t += "`n`n이 대화에 답장하면 그대로 요청(UserRequest.txt)으로 올라가고 5분 안에 다시 돌아. 예: `"계속 가`", `"그건 관찰로 두고 다음 거`". `"상태`"라고 보내면 지금 상태를 알려 줘."
    if ($t.Length -gt 3900) { $t = $t.Substring(0, 3900) + '…' }
    $t
}
# 텔레그램 답장 → UserRequest.txt(사용자 11-13): telegram.txt의 chat id에서 온 글만 받는다(다른 대화는 무시).
# 루프 작업 트리는 안 건드리고 %USERPROFILE%\vh-loop\req-repo(따로 받은 저장소)에서 커밋·푸시 → 루프가 origin을 보고 5분 안에 다시 돎.
$TgOffsetFile = Join-Path $Dir 'telegram-offset.txt'
function Get-TgCreds { @(Get-Content (Join-Path $Dir 'telegram.txt') -Encoding UTF8 -ErrorAction SilentlyContinue | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) }
function Add-UserRequest($text) {
    $rd = Join-Path $Dir 'req-repo'
    if (-not (Test-Path (Join-Path $rd '.git'))) {
        $url = (git -C $Repo remote get-url origin 2>$null | Select-Object -First 1)
        git clone -q --branch valheim $url $rd 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { return "요청 못 올림: 저장소를 못 받았어(clone 실패). GitHub에서 직접 써 줘." }
        foreach ($k in 'user.name', 'user.email') { $v = git -C $Repo config $k; if ($v) { git -C $rd config $k $v } }
    }
    $stamp = Get-Date -Format 'MM-dd HH:mm'
    $lines = @($text -split "`r?`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ })
    $block = "`r`n텔레그램 답(사용자 $stamp)`r`n" + (($lines | ForEach-Object { "- $_" }) -join "`r`n") + "`r`n"
    $f = Join-Path $rd 'docs\valheim\UserRequest.txt'
    for ($try = 1; $try -le 3; $try++) {
        git -C $rd fetch -q origin valheim 2>$null | Out-Null
        git -C $rd reset -q --hard origin/valheim 2>$null | Out-Null
        $cur = [IO.File]::ReadAllText($f, $Utf8)
        $pre = if ($cur.EndsWith("`n")) { '' } else { "`r`n" }
        [IO.File]::AppendAllText($f, $pre + $block, $Utf8)
        git -C $rd add docs/valheim/UserRequest.txt 2>$null | Out-Null
        git -C $rd commit -q -m "UserRequest: 텔레그램 답 $stamp" 2>$null | Out-Null
        git -C $rd push -q origin HEAD:valheim 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $h = git -C $rd rev-parse --short HEAD
            Log "텔레그램 답 → UserRequest.txt 커밋 $h"
            return "받았어. 요청으로 올렸어(커밋 $h). 루프가 멈춰 있었으면 5분 안에, 돌고 있으면 15분 백업 때 가져가."
        }
        Start-Sleep 3
    }
    Log "텔레그램 답 푸시 실패: $text"
    "요청 못 올림: 푸시가 3번 실패했어. 잠시 뒤 다시 보내거나 GitHub에서 직접 써 줘."
}
function Watch-TgReplies {
    $c = Get-TgCreds
    if ($c.Count -lt 2) { return }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $api = "https://api.telegram.org/bot$($c[0])"
    $off = $null
    if (Test-Path $TgOffsetFile) { $off = (Get-Content $TgOffsetFile -ErrorAction SilentlyContinue | Select-Object -First 1) }
    try {
        if (-not $off) {
            # 처음: 지난 메시지는 요청으로 올리지 않고 건너뜀
            $r = Invoke-RestMethod "$api/getUpdates?offset=-1&timeout=0" -TimeoutSec 30
            $last = @($r.result) | Select-Object -Last 1
            $n = if ($last) { [int64]$last.update_id + 1 } else { 0 }
            [IO.File]::WriteAllText($TgOffsetFile, "$n", $Utf8); return
        }
        $r = Invoke-RestMethod "$api/getUpdates?offset=$off&timeout=0" -TimeoutSec 30
    } catch { Log "텔레그램 받기 실패: $($_.Exception.Message)"; return }
    foreach ($u in @($r.result)) {
        [IO.File]::WriteAllText($TgOffsetFile, "$([int64]$u.update_id + 1)", $Utf8)
        $m = $u.message
        if (-not $m -or -not $m.text) { continue }
        if ("$($m.chat.id)" -ne $c[1]) { Log "텔레그램: 모르는 대화($($m.chat.id)) 글 무시"; continue }
        $t = $m.text.Trim()
        if ($t -match '^/start') { continue }
        if ($t -match '^/?(상태|status)$') {
            git -C $Repo fetch -q origin valheim 2>$null | Out-Null
            $s = (@(git -C $Repo show 'origin/valheim:docs/valheim/status.txt' 2>$null) | Select-Object -First 6) -join "`n"
            Send-Tg $(if ($s) { $s } else { '상태 파일을 못 읽었어.' }); continue
        }
        Send-Tg (Add-UserRequest $t)
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
        $s = [array]::IndexOf(@($e.Body | ForEach-Object { $_.Trim() }), '**요약**')
        $e.Summary = if ($s -ge 0) { (@($e.Body | Select-Object -Skip ($s + 1)) | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -ne '**자세히**' } | Select-Object -First 1) -replace '^- ', '' -replace '\*\*', '' } else { '' }
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
    Send-Tg "발헤임 지킴이 끔: $why. 이제 텔레그램 알림·답장은 쉬고, 루프가 멈추면 자리 비울 때 PC가 꺼질 수 있어. 다시 켜려면 PC에서 scripts/vh-keepon-on.ps1(또는 Claude한테 '지킴이 켜')."
    Remove-Item (Join-Path $Dir 'stay-on') -Force -ErrorAction SilentlyContinue
    Remove-Item $UntilFile -Force -ErrorAction SilentlyContinue
    Log "지킴이 끔: $why (stay-on 지움, 작업 VH keep on 사용 안 함 — 지우지 않음)"
    Disable-ScheduledTask -TaskName 'VH keep on' -ErrorAction SilentlyContinue | Out-Null
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
    Watch-TgReplies
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
