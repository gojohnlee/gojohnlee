# Installs the Valheim unattended loop (scripts/vh-loop.ps1) as the scheduled task "VH loop".
# No trigger (user 11-02: do not start at logon); start by hand: Start-ScheduledTask "VH loop". Runs in a visible console window (plain conhost, not Windows Terminal: the loop places it on the right half
# and turns off quick edit). Does not start it now: first run is vh-loop.ps1 -Once in front of the user.
# ASCII only on purpose: Windows PowerShell 5.1 -File misreads BOM-less UTF-8, so this also re-saves vh-loop.ps1 with a BOM.
$ErrorActionPreference = 'Stop'
$loop = Join-Path $PSScriptRoot 'vh-loop.ps1'
$text = [IO.File]::ReadAllText($loop, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText($loop, $text, (New-Object Text.UTF8Encoding($true)))

$errs = $null
[void][Management.Automation.Language.Parser]::ParseFile($loop, [ref]$null, [ref]$errs)
if ($errs) { $errs | ForEach-Object { "line $($_.Extent.StartLineNumber): $($_.Message)" }; throw 'vh-loop.ps1 has syntax errors' }

$act = New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$loop`""   # headless (user 11-11): a visible conhost flashed on every restart before the loop hid it; set it back to plain conhost only when $ShowWindow is $true
$set = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$prn = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName 'VH loop' -Action $act -Settings $set -Principal $prn -Force | Out-Null
Get-ScheduledTask -TaskName 'VH loop' | Select-Object TaskName, State | Format-List
'Installed. Start now: Start-ScheduledTask "VH loop". Stop: create docs/valheim/loop-stop.txt. Remove: Unregister-ScheduledTask "VH loop"'
