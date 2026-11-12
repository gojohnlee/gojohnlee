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

# 임시 조치(사용자 11-13): 11-13 18:30이 지나거나 루프가 할 일 없이 멈춰 기다리면 지킴이를 스스로 지운다
# (stay-on 지움 → 루프는 원래대로 자리 비우면 끔, 작업 "VH keep on" 지움, 끝).
$Deadline = [datetime]'2026-11-13 18:30'
function Test-LoopIdle {
    $l = Get-Content (Join-Path $Dir 'loop.log') -Tail 200 -Encoding UTF8 -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '회차 \d+ 시작|루프 시작|다시 돎|멈춤:|할 일 없음|답 기다림|멈춤 파일' } | Select-Object -Last 1
    [bool]($l -and $l -match '멈춤:|할 일 없음|답 기다림|멈춤 파일')
}
function Remove-KeepOn($why) {
    Remove-Item (Join-Path $Dir 'stay-on') -Force -ErrorAction SilentlyContinue
    Log "지킴이 지움: $why (stay-on 지움, 작업 VH keep on 지움)"
    Unregister-ScheduledTask -TaskName 'VH keep on' -Confirm:$false -ErrorAction SilentlyContinue
    exit
}

[IO.File]::WriteAllText((Join-Path $Dir 'stay-on'), "1", $Utf8)
Log "지킴이 시작($($Deadline.ToString('MM-dd HH:mm'))까지 또는 루프가 할 일 없이 멈출 때까지)"
$down = 0; $tick = 0
while ($true) {
    if ((Get-Date) -ge $Deadline) { Remove-KeepOn "$($Deadline.ToString('HH:mm')) 지남" }
    if (Test-LoopIdle) { Remove-KeepOn '루프가 할 일 없이 멈춤' }
    [void][VHK.Power]::SetThreadExecutionState([uint32]'0x80000001')   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED
    & shutdown /a 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Log '예약된 컴퓨터 끄기 취소함(shutdown /a)' }
    if (-not (Test-Path (Join-Path $Dir 'stay-on'))) { [IO.File]::WriteAllText((Join-Path $Dir 'stay-on'), "1", $Utf8) }
    $tick++
    if ($tick % 3 -eq 0) {
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
