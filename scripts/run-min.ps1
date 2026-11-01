# 시험 실행: Steam 창 없이 exe를 숨겨서 직접 켜고(steam_appid.txt 덕에 Steam으로 다시 안 켜짐),
# 창이 생기는 즉시 내린 뒤 플러그인(NECRO_MIN=1)이 계속 내려 둔다. 소리는 Windows 앱별 음소거(끝나면 해제).
# 정해진 초 뒤 로그 요약을 보여주고 끈다.
param([int]$Wait = 90, [switch]$Keep)
Add-Type -Name W -Namespace U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(System.IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr h);
'@
if (-not ('AppMute' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'Mute.cs') }
$g = 'C:\Program Files (x86)\Steam\steamapps\common\The Necromancer'
if (-not (Test-Path "$g\steam_appid.txt")) { Set-Content "$g\steam_appid.txt" '1315320' -Encoding ascii }
$env:NECRO_MIN = '1'
$p = Start-Process "$g\TheNecromancer.exe" -WorkingDirectory $g -WindowStyle Hidden -PassThru
$env:NECRO_MIN = $null
$sw = [Diagnostics.Stopwatch]::StartNew(); $min = $false; $muted = $false; $tick = 0
while ($sw.Elapsed.TotalSeconds -lt $Wait) {
  $q = Get-Process TheNecromancer -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($q) {
    if ($q.MainWindowHandle -ne 0 -and -not [U.W]::IsIconic($q.MainWindowHandle)) { [void][U.W]::ShowWindowAsync($q.MainWindowHandle, 6); $min = $true }
    if (($tick++ % 10) -eq 0 -and [AppMute]::Mute($q.Id, $true)) { $muted = $true }   # 소리 세션은 늦게 생김 → 0.5초마다 다시
  }
  Start-Sleep -Milliseconds 50
}
"minimized=$min muted=$muted alive=" + [bool](Get-Process TheNecromancer -ErrorAction SilentlyContinue)
$k = "$g\KoreanFont\KoreanFont.log"
if (Test-Path $k) { Get-Content $k -Encoding utf8 | Select-Object -First 20 } else { 'no KoreanFont.log' }
$e = Join-Path $env:TEMP 'KoreanFont-start-error.txt'; if (Test-Path $e) { Get-Content $e | Select-Object -First 3; Remove-Item $e }
$pl = "$env:USERPROFILE\AppData\LocalLow\Psychic Software\TheNecromancer\Player.log"
"missing-glyph lines: " + (Select-String -Path $pl -Pattern 'was not found in the').Count
Select-String -Path $pl -Pattern 'Crash|Exception' | Select-Object -First 5 | ForEach-Object { $_.Line }
if (-not $Keep) {
  $q = Get-Process TheNecromancer -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($q) { [void][AppMute]::Mute($q.Id, $false) }   # Windows가 음소거를 기억하지 않게 해제 후 종료
  Stop-Process -Name TheNecromancer -Force -ErrorAction SilentlyContinue
}
