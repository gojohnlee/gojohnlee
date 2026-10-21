# 발헤임 모드 자동 설치 + 실행 + 결과 수집 (Windows PowerShell)
# 사용법 (PowerShell에 한 줄 붙여넣기):
#   irm https://raw.githubusercontent.com/gojohnlee/gojohnlee/valheim/scripts/valheim-test.ps1 | iex
# PC의 Claude Code 세션: $env:VH_DLL='<빌드한 ValheimHelper.dll>'; powershell -ExecutionPolicy Bypass -File scripts\valheim-test.ps1
#   (화면과 클립보드에는 짧은 요약만, 전체는 <게임>\BepInEx\plugins\ValheimHelper\diag.txt)
# 다른 게임을 하면서 시험: 앞에 $env:VH_MIN='1'; 을 붙이면 발헤임 창을 최소화하고 소리를 끈다.
$ErrorActionPreference = 'Stop'
$Raw   = 'https://raw.githubusercontent.com/gojohnlee/gojohnlee/valheim'
$AppId = 892970

function Find-Valheim {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { throw 'Steam 경로를 찾지 못했습니다.' }
    $libs = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libs += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' |
            ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($lib in $libs | Select-Object -Unique) {
        $dir = Join-Path $lib 'steamapps\common\Valheim'
        if (Test-Path $dir) { return $dir }
    }
    throw 'Valheim 설치 폴더를 찾지 못했습니다.'
}

$game = Find-Valheim
Write-Host "Valheim: $game"
$tmp = Join-Path $env:TEMP "valheim-test-$(Get-Random)"
New-Item -ItemType Directory $tmp | Out-Null

# 1) BepInEx (없을 때만 설치)
if (-not (Test-Path (Join-Path $game 'BepInEx\core\BepInEx.dll'))) {
    $pkg = Invoke-RestMethod 'https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/'
    Invoke-WebRequest $pkg.latest.download_url -OutFile "$tmp\bep.zip"
    Expand-Archive "$tmp\bep.zip" "$tmp\bep"
    Copy-Item "$tmp\bep\BepInExPack_Valheim\*" $game -Recurse -Force
    Write-Host "BepInEx $($pkg.latest.version_number) 설치됨"
}

# 2) 이 모드 (항상 최신으로 교체)
$plugin = Join-Path $game 'BepInEx\plugins\ValheimHelper'
New-Item -ItemType Directory $plugin -Force | Out-Null
Remove-Item "$plugin\diag.txt" -ErrorAction SilentlyContinue
if ($env:VH_DLL) { Copy-Item $env:VH_DLL "$plugin\ValheimHelper.dll" -Force }   # PC 세션: 방금 빌드한 DLL
else { Invoke-WebRequest "$Raw/valheim/dist/ValheimHelper.dll" -OutFile "$plugin\ValheimHelper.dll" }
$Test = if ($env:VH_TEST) { $env:VH_TEST } else { 'vh_npctest;vh_flagtest;vh_woodtest;vh_stonetest' }   # 다른 시험: $env:VH_TEST='vh_fartest' (여러 개는 ;로)
$World = if ($env:VH_WORLD) { $env:VH_WORLD } else { 'te4st41' }   # 시험 월드(메인 메뉴에서 자동으로 들어감)
$Min = $env:VH_MIN -eq '1'   # 최소화 시험: 창 최소화·소리 끔·백그라운드 30fps (시험 동안 다른 게임을 할 때 $env:VH_MIN='1')
Set-Content "$plugin\autotest.txt" ("world=$World;" + $(if ($Min) { 'min;' } else { '' }) + $Test)   # 모드가 월드에 들어가 저절로 시험하고 게임을 끈다
Remove-Item $tmp -Recurse -Force
Write-Host 'Valheim Helper 설치됨'

# 3) 게임 실행 후 종료될 때까지 대기
# 실행 인자(-console)를 붙이면 Steam이 "사용자 지정 인수" 경고창을 띄운다 → 인자 없이 실행(시험은 콘솔 없이 돈다).
Start-Process "steam://rungameid/$AppId"
Write-Host ''
Write-Host "게임 실행 중... 시험 월드($World)로 저절로 들어갑니다. 손대지 않아도 됩니다."
Write-Host '(그 월드가 없으면 직접 테스트용 월드에 들어가 주세요)'
Write-Host "3초 뒤 시험($Test)이 저절로 시작되고, 시험이 끝나면(최대 15분) 게임이 저절로 꺼집니다."
Write-Host "(20분이 지나도 안 꺼지면 직접 꺼 주세요)"
function Test-Game { [bool](Get-Process | Where-Object { $_.ProcessName -like 'valheim*' }) }
$t = 0
if ($Min) {
    # 모드가 올라오기 전(유니티 시작 화면)에도 창을 바로 내린다. 그 뒤는 모드가 첫 시험 전까지 내려 둔다.
    Add-Type -Namespace VH -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
'@
    $seen = 0
    while ($t -lt 180 -and $seen -lt 20) {
        $p = Get-Process | Where-Object { $_.ProcessName -like 'valheim*' -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($p) { $seen++; if (-not [VH.Win]::IsIconic($p.MainWindowHandle)) { [void][VH.Win]::ShowWindowAsync($p.MainWindowHandle, 6) } }
        Start-Sleep -Milliseconds 250; $t += 0.25
    }
}
while (-not (Test-Game) -and $t -lt 180) { Start-Sleep 3; $t += 3 }   # 첫 실행은 느릴 수 있음
while (Test-Game) { Start-Sleep 3 }

# 4) 결과 → 짧은 요약만 화면·클립보드에(토큰 아끼기, 2026-10-21). 자세한 건 diag.txt·LogOutput.log를 FAIL일 때만 연다.
#    요약: diag 첫 줄, 시험별 판정·시간, FAIL 줄, 모드 로드 줄, 로그 오류 줄 수와 종류별 첫 8개.
$diag = Join-Path $plugin 'diag.txt'
$log = Join-Path $game 'BepInEx\LogOutput.log'
$report = @('=== Valheim 테스트 결과 ===')
if (Test-Path $diag) {
    $lines = @(Get-Content $diag -Encoding UTF8)
    $report += $lines[0]
    $name = $null; $verdict = ''; $time = ''; $fails = @()
    foreach ($l in ($lines | Select-Object -Skip 1)) {
        if ($l.StartsWith('=== ')) {
            if ($name) { $report += "  $name $verdict $time" }
            $name = ($l.Substring(4) -split ' ')[0]; $verdict = '(판정 없음)'; $time = ''
        }
        elseif ($l.StartsWith('판정: ')) { $verdict = $l.Substring(4) }
        elseif ($l.StartsWith('시간: ')) { $time = '(' + $l.Substring(4) + ')' }
        elseif ($l.StartsWith('FAIL')) { $fails += "  [$name] $l" }
    }
    if ($name) { $report += "  $name $verdict $time" }
    if ($fails.Count -gt 0) { $report += @($fails | Select-Object -First 30) }
} else { $report += '(diag.txt 없음: 시험이 안 돌았거나 모드가 로드되지 않았음)' }
if (Test-Path $log) {
    $all = @(Get-Content $log -Encoding UTF8)
    $loaded = $all | Where-Object { $_ -match 'Valheim Helper .* 로드됨' } | Select-Object -Last 1
    if ($loaded) { $report += $loaded.Trim() } else { $report += '모드 로드 줄 없음(BepInEx가 모드를 못 올렸을 수 있음)' }
    $errs = @($all | Where-Object { $_ -match 'Error|Exception' })
    $kinds = @($errs | ForEach-Object { $_.Trim() } | Select-Object -Unique)
    $report += "로그 오류 $($errs.Count)줄(종류 $($kinds.Count))"
    foreach ($e in ($kinds | Select-Object -First 8)) {
        if ($e.Length -gt 200) { $e = $e.Substring(0, 200) + '...' }
        $report += "  $e"
    }
} else { $report += '(LogOutput.log 없음: BepInEx가 로드되지 않았음)' }
$report += "자세히(FAIL일 때만): $diag 에서 '=== 시험이름' 부분, 오류는 $log"
$text = $report -join "`r`n"
Set-Clipboard $text
Write-Host $text
Write-Host ''
Write-Host '결과를 클립보드에 복사했습니다. 대화창에 붙여넣어 주세요.'
