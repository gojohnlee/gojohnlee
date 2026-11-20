# Casualties Carryover 자동 시험(조용히: 작은 창·바로 최소화·소리 끔).
# 1회차 +3·+3·+3 얻고 죽기 → 2회차 +3으로 시작하는지 확인하고 바로 죽기 → 다음 보너스 0인지 확인.
# 시험용 상태 파일을 따로 쓰고, 게임 설정(레지스트리 PlayerPrefs: 화면 크기·사망 횟수 등)은 시험 전 값으로 되돌린다.
param([int]$TimeoutSec = 480, [string]$Mode = "1")
$ErrorActionPreference = 'Stop'
$game = 'C:\Program Files (x86)\Steam\steamapps\common\Casualties Unknown'
$work = Join-Path $env:TEMP 'cu-carryover-test'
New-Item -ItemType Directory -Force $work | Out-Null
$result = Join-Path $work 'result.txt'; Remove-Item $result -ErrorAction SilentlyContinue
$regBak = Join-Path $work 'prefs.reg'
reg export 'HKCU\Software\Orsoniks\CasualtiesUnknown' $regBak /y | Out-Null

Add-Type -Namespace CU -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindowAsync(System.IntPtr h, int cmd); [DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr h);'
$env:CU_CARRYOVER_TEST = $Mode   # "1" 두 회차 시험, "lang" 언어 목록 확인
$env:CU_CARRYOVER_STATE = Join-Path $work 'state.txt'
$env:CU_CARRYOVER_RESULT = $result
try {
    $p = Start-Process (Join-Path $game 'CasualtiesUnknown.exe') -ArgumentList '-screen-fullscreen 0 -screen-width 640 -screen-height 360' -WorkingDirectory $game -PassThru
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $p.Refresh()
        if ($p.MainWindowHandle -ne 0 -and -not [CU.Win]::IsIconic($p.MainWindowHandle)) { [void][CU.Win]::ShowWindowAsync($p.MainWindowHandle, 6) }
        Start-Sleep -Milliseconds 300
    }
    if (-not $p.HasExited) { $p.Kill(); 'TIMEOUT (game killed)' }
} finally {
    Remove-Item Env:CU_CARRYOVER_TEST, Env:CU_CARRYOVER_STATE, Env:CU_CARRYOVER_RESULT -ErrorAction SilentlyContinue
    Start-Sleep 2
    $ErrorActionPreference = 'Continue'; cmd /c "reg import `"$regBak`" >nul 2>nul"
}
if (Test-Path $result) { Get-Content $result -Encoding UTF8 } else { 'no result file'; Get-Content (Join-Path $game 'BepInEx\LogOutput.log') -Tail 30 }
