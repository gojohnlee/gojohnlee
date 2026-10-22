# Restarts the Valheim loop (scripts/vh-loop.ps1) with the current script file, with nobody at the PC.
# ASCII only on purpose: Windows PowerShell 5.1 -File misreads BOM-less UTF-8.
#   (no switch)  run once per round by the round's claude: if the loop that started this claude runs an older
#                vh-loop.ps1 (env VH_LOOP_HASH missing or different), schedule a restart. Otherwise do nothing.
#   -Now         run by vh-loop.ps1 itself after a pull changed it: schedule a restart.
#   -Wait        run by the one-shot task "VH loop restart": wait until the running loop exits, then start task "VH loop".
# Scheduling = task "VH loop restart" (runs outside the loop's process tree, so it survives the loop exiting)
#              + %USERPROFILE%\vh-loop\stop (every loop version stops at the top of its next round, after the current round)
#              + %USERPROFILE%\vh-loop\restart (marks that stop file as ours: the new loop deletes both when it starts).
param([switch]$Now, [switch]$Wait)
$ErrorActionPreference = 'Stop'
$Loop = Join-Path $PSScriptRoot 'vh-loop.ps1'
$Dir = Join-Path $env:USERPROFILE 'vh-loop'
$Stop = Join-Path $Dir 'stop'
$Mark = Join-Path $Dir 'restart'
function Log($m) {
    $l = "$(Get-Date -Format 'MM-dd HH:mm:ss') $m"
    try { [IO.File]::AppendAllText((Join-Path $Dir 'restart.log'), "$l`r`n") } catch { }
    Write-Output $l
}

if ($Wait) {
    $m = New-Object Threading.Mutex($false, 'Global\VhLoop')
    try { [void]$m.WaitOne() } catch [Threading.AbandonedMutexException] { }   # the loop holds it for its whole life
    $m.ReleaseMutex(); $m.Dispose()
    Remove-Item $Stop, $Mark -Force -ErrorAction SilentlyContinue
    $t = Get-Date   # the old task instance may still be closing (conhost); "VH loop" ignores a start while it runs
    while ((Get-ScheduledTask -TaskName 'VH loop' -ErrorAction SilentlyContinue).State -eq 'Running' -and ((Get-Date) - $t).TotalSeconds -lt 120) { Start-Sleep 5 }
    Start-ScheduledTask -TaskName 'VH loop'
    Log 'old loop ended -> started task "VH loop"'
    exit
}

if (-not $Now -and $env:VH_LOOP_HASH -eq (Get-FileHash $Loop).Hash) { 'loop is up to date'; exit }
$b = [IO.File]::ReadAllBytes($Loop)
if ($b.Length -lt 3 -or $b[0] -ne 0xEF -or $b[1] -ne 0xBB -or $b[2] -ne 0xBF) { Log 'not restarting: vh-loop.ps1 has no UTF-8 BOM'; exit 1 }
$errs = $null
[void][Management.Automation.Language.Parser]::ParseFile($Loop, [ref]$null, [ref]$errs)
if ($errs) { Log "not restarting: vh-loop.ps1 line $($errs[0].Extent.StartLineNumber): $($errs[0].Message)"; exit 1 }
New-Item -ItemType Directory $Dir -Force | Out-Null
if ((Get-ScheduledTask -TaskName 'VH loop restart' -ErrorAction SilentlyContinue).State -ne 'Running') {
    $act = New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Wait"
    $set = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    $prn = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName 'VH loop restart' -Action $act -Settings $set -Principal $prn -Force | Out-Null
    Start-ScheduledTask -TaskName 'VH loop restart'
}
# only after the waiter runs: a stop file without a waiter would leave the loop stopped for good
New-Item -ItemType File $Mark -Force | Out-Null
New-Item -ItemType File $Stop -Force | Out-Null
Log 'restart scheduled: the loop stops after this round and starts again with the new vh-loop.ps1'
