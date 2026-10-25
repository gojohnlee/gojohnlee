# Mercenary Camp in-game QA (isolated profile; the player's own config and saves are untouched).
#   powershell -ExecutionPolicy Bypass -File run-qa.ps1                 # full scenario on a quicktest map
#   powershell -ExecutionPolicy Bypass -File run-qa.ps1 -LoadSave x.rws # load an older save, check the layout migration
# The game runs muted in a small window parked off-screen, quits by itself, and the result is printed from Camp-QA.log.
#   -Profile <folder>: another Config folder, e.g. a ModsConfig without the Anomaly DLC
param([string]$Data = (Join-Path $env:TEMP 'mcqa-profile'), [int]$TimeoutSec = 600, [string]$LoadSave, [string]$Profile = (Join-Path $PSScriptRoot 'profile'))
$ErrorActionPreference = 'Stop'
# An empty -savedatafolder makes the game use the drive root, so refuse anything that is not a dedicated folder.
if ([string]::IsNullOrWhiteSpace($Data) -or (Split-Path $Data -Leaf) -notmatch 'mcqa') { throw "QA data folder must be a dedicated *mcqa* folder: '$Data'" }
Add-Type -Namespace QA -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
# Park the window just past the right edge of the whole desktop. A real minimize stops the engine from drawing
# the loading screen, and the save/load stage then waits forever.
$parkX = [QA.Win]::GetSystemMetrics(76) + [QA.Win]::GetSystemMetrics(78) + 200
# Fresh profile each run: caches left by the QA's hard shutdown can stall the next boot.
if (Test-Path $Data) { Get-ChildItem $Data | Remove-Item -Recurse -Force } else { New-Item -ItemType Directory $Data | Out-Null }
Copy-Item $Profile (Join-Path $Data 'Config') -Recurse
$log = Join-Path $Data 'Camp-QA.log'
$mode = @('-quicktest')
if ($LoadSave) {
  New-Item -ItemType Directory (Join-Path $Data 'Saves') | Out-Null
  Copy-Item $LoadSave (Join-Path $Data 'Saves')
  $mode = @("-campqaload=$([IO.Path]::GetFileNameWithoutExtension($LoadSave))")
}
$exe = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')) 'RimWorld.exe'
$p = Start-Process $exe -ArgumentList (@("-savedatafolder=$Data", '-campqa') + $mode) -PassThru -WindowStyle Minimized
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
  # The engine ignores the launch window style, so keep the window parked off-screen (no size/z-order change, no activation).
  $p.Refresh(); $h = $p.MainWindowHandle; $r = New-Object QA.Win+RECT
  if ($h -ne [IntPtr]::Zero -and [QA.Win]::GetWindowRect($h, [ref]$r) -and $r.Left -ne $parkX) { [void][QA.Win]::SetWindowPos($h, [IntPtr]::Zero, $parkX, 0, 0, 0, 0x15) }
  Start-Sleep -Milliseconds 200
  if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'TESTS_FINISHED' -Quiet)) { Start-Sleep 10; break }
}
if (-not $p.HasExited) { $p.Refresh(); "STILL RUNNING title='$($p.MainWindowTitle)'"; Stop-Process -Id $p.Id -Force; "KILLED after $([int]$sw.Elapsed.TotalSeconds)s" }
else { "EXITED after $([int]$sw.Elapsed.TotalSeconds)s" }
if (Test-Path $log) { Get-Content $log -Encoding UTF8 | Select-String 'PASS|FAIL|SKIP|ERROR|EXCEPTION|SUMMARY' | ForEach-Object { $_.Line } } else { 'NO QA LOG' }
