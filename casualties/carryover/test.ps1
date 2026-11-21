# Casualties Carryover 자동 시험(조용히: 320x180 창을 화면 밖에 띄우고 바로 최소화·소리 끔, 설정은 끝나면 되돌림).
# 1회차 +3·+3·+3 얻고 죽기 → 2회차 +3으로 시작하는지 확인하고 바로 죽기 → 다음 보너스 0인지 확인.
# 시험용 상태 파일을 따로 쓰고, 게임 설정(레지스트리 PlayerPrefs: 화면 크기·사망 횟수 등)은 시험 전 값으로 되돌린다.
param([int]$TimeoutSec = 480, [string]$Mode = "1")
$ErrorActionPreference = 'Stop'
$game = 'C:\Program Files (x86)\Steam\steamapps\common\Casualties Unknown'
$work = Join-Path $env:TEMP 'cu-carryover-test'
New-Item -ItemType Directory -Force $work | Out-Null
$result = Join-Path $work 'result.txt'; Remove-Item $result -ErrorAction SilentlyContinue
$regKey = 'HKCU:\Software\Orsoniks\CasualtiesUnknown'
$regBak = Join-Path $work 'prefs.reg'
reg export 'HKCU\Software\Orsoniks\CasualtiesUnknown' $regBak /y | Out-Null
# 화면에 아무것도 안 보이게(사용자 2026-11-21): 게임 설정(settings.json)의 전체 화면·해상도를 시험 동안만 320x180 창 모드로,
# Unity 화면 기록(레지스트리 Screenmanager)은 창 모드·화면 밖 위치로 바꿔 띄우고, 끝나면 둘 다 원래대로 되돌린다.
$settings = Join-Path $env:USERPROFILE 'AppData\LocalLow\Orsoniks\CasualtiesUnknown\settings.json'
$settingsBak = Join-Path $work 'settings.json.bak'
Copy-Item $settings $settingsBak -Force
$js = Get-Content $settings -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($s in $js) {
    if ($s.name -eq 'reswidth') { $s.value = 320 } elseif ($s.name -eq 'resheight') { $s.value = 180 }
    elseif ($s.name -eq 'fullscreen') { $s.value = $false } elseif ($s.name -eq 'mastervolume') { $s.value = 0 }
}
[IO.File]::WriteAllText($settings, (ConvertTo-Json @($js) -Depth 5), (New-Object Text.UTF8Encoding $false))
$names = (Get-Item $regKey).Property
function Set-Pref($prefix, [int]$v) { foreach ($n in $names) { if ($n -like "$prefix`_h*") { Set-ItemProperty $regKey $n ([BitConverter]::ToInt32([BitConverter]::GetBytes($v), 0)) } } }
Set-Pref 'Screenmanager Fullscreen mode' 3
Set-Pref 'Screenmanager Resolution Width' 320; Set-Pref 'Screenmanager Resolution Height' 180
Set-Pref 'Screenmanager Resolution Window Width' 320; Set-Pref 'Screenmanager Resolution Window Height' 180
Set-Pref 'Screenmanager Window Position X' -32000; Set-Pref 'Screenmanager Window Position Y' -32000

Add-Type -Namespace CU -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(System.IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
public struct RECT { public int L, T, R, B; }
'@
Add-Type -AssemblyName System.Windows.Forms
$screen = [Windows.Forms.SystemInformation]::VirtualScreen
$env:CU_CARRYOVER_TEST = $Mode   # "1" 두 회차 시험, "perk" 퍽 효과 시험, "lang" 언어 목록 확인
$env:CU_CARRYOVER_STATE = Join-Path $work 'state.txt'
$env:CU_CARRYOVER_RESULT = $result
$env:CU_CARRYOVER_PERKS = Join-Path $work 'perks.txt'
$env:CU_CARRYOVER_SHOT = Join-Path $work 'perkmenu.png'
$env:CU_CARRYOVER_SUBSPACE = Join-Path $work 'subspace.json'
Remove-Item $env:CU_CARRYOVER_SUBSPACE -ErrorAction SilentlyContinue   # 지난 시험 아공간 물건이 새 회차로 넘어오지 않게
Add-Type -Namespace CU -Name HiddenDesk -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern System.IntPtr CreateDesktop(string name, System.IntPtr dev, System.IntPtr mode, int flags, uint access, System.IntPtr sa);
[DllImport("user32.dll")] public static extern bool CloseDesktop(System.IntPtr h);
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct STARTUPINFO { public int cb; public string lpReserved; public string lpDesktop; public string lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public System.IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
[StructLayout(LayoutKind.Sequential)] public struct PROCESS_INFORMATION { public System.IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool CreateProcess(string app, string cmd, System.IntPtr pa, System.IntPtr ta, bool inherit, uint flags, System.IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
public static int Launch(string desk, string exe, string args, string dir) {
    var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(si); si.lpDesktop = desk; si.dwFlags = 1; si.wShowWindow = 7;
    PROCESS_INFORMATION pi;
    if (!CreateProcess(exe, "\"" + exe + "\" " + args, System.IntPtr.Zero, System.IntPtr.Zero, false, 0, System.IntPtr.Zero, dir, ref si, out pi)) throw new System.ComponentModel.Win32Exception();
    return pi.dwProcessId;
}
'@
# 숨은 데스크톱(사용자 화면과 다른 Windows 데스크톱)에서 띄워 창이 화면에 전혀 나오지 않게 한다. 남는 창 검사는 혹시 사용자 화면에 뜨는지 보는 안전장치.
$desk = [CU.HiddenDesk]::CreateDesktop("cu-carryover-test", [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
if ($desk -eq [IntPtr]::Zero) { throw "CreateDesktop failed" }
$visible = @()
try {
    $p = Get-Process -Id ([CU.HiddenDesk]::Launch('cu-carryover-test', (Join-Path $game 'CasualtiesUnknown.exe'), '-screen-fullscreen 0 -window-mode windowed -screen-width 320 -screen-height 180', $game))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $p.Refresh()
        $h = $p.MainWindowHandle
        if ($h -ne 0 -and -not [CU.Win]::IsIconic($h)) {
            $r = New-Object CU.Win+RECT
            [void][CU.Win]::GetWindowRect($h, [ref]$r)
            if ($r.R -gt $screen.Left -and $r.L -lt $screen.Right -and $r.B -gt $screen.Top -and $r.T -lt $screen.Bottom) { $visible += "$($r.L),$($r.T) $($r.R - $r.L)x$($r.B - $r.T) @$([int]$sw.Elapsed.TotalMilliseconds)ms" }
            [void][CU.Win]::SetWindowPos($h, [IntPtr]::Zero, -32000, -32000, 0, 0, 0x0015)   # 화면 밖으로(NOSIZE|NOZORDER|NOACTIVATE)
            [void][CU.Win]::ShowWindowAsync($h, 7)                                          # 최소화(활성화 안 함)
        }
        Start-Sleep -Milliseconds 50
    }
    if (-not $p.HasExited) { $p.Kill(); 'TIMEOUT (game killed)' }
} finally {
    Remove-Item Env:CU_CARRYOVER_TEST, Env:CU_CARRYOVER_STATE, Env:CU_CARRYOVER_RESULT, Env:CU_CARRYOVER_PERKS, Env:CU_CARRYOVER_SHOT, Env:CU_CARRYOVER_SUBSPACE -ErrorAction SilentlyContinue
    Start-Sleep 2
    if ($desk) { [void][CU.HiddenDesk]::CloseDesktop($desk) }
    Copy-Item $settingsBak $settings -Force
    $ErrorActionPreference = 'Continue'; cmd /c "reg import `"$regBak`" >nul 2>nul"
}
if ($visible.Count) { "WINDOW ON SCREEN $($visible.Count)x: " + ($visible | Select-Object -First 5) -join ' | ' } else { 'window never on screen' }
if (Test-Path $result) { Get-Content $result -Encoding UTF8 } else { 'no result file'; Get-Content (Join-Path $game 'BepInEx\LogOutput.log') -Tail 30 }
