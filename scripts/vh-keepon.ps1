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

[IO.File]::WriteAllText((Join-Path $Dir 'stay-on'), "1", $Utf8)
Log '지킴이 시작'
$down = 0; $tick = 0
while ($true) {
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
