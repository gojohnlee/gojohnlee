# 발헤임 모드 자동 설치 + 실행 + 결과 수집 (Windows PowerShell)
# 사용법 (PowerShell에 한 줄 붙여넣기):
#   irm https://raw.githubusercontent.com/gojohnlee/gojohnlee/valheim/scripts/valheim-test.ps1 | iex
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
Invoke-WebRequest "$Raw/valheim/dist/ValheimHelper.dll" -OutFile "$plugin\ValheimHelper.dll"
$Test = if ($env:VH_TEST) { $env:VH_TEST } else { 'vh_npctest;vh_flagtest' }   # 다른 시험: $env:VH_TEST='vh_fartest' (여러 개는 ;로)
Set-Content "$plugin\autotest.txt" $Test   # 월드에 들어가면 모드가 저절로 시험하고 게임을 끈다
Remove-Item $tmp -Recurse -Force
Write-Host 'Valheim Helper 설치됨'

# 3) 게임 실행(F5 콘솔 켬) 후 종료될 때까지 대기
Start-Process "steam://run/$AppId//-console/"
Write-Host ''
Write-Host '게임 실행 중... 월드에 들어가기만 하세요(싱글 또는 서버 시작, 테스트용 월드 권장).'
Write-Host "3초 뒤 시험($Test)이 저절로 시작되고, 1~2분 뒤 게임이 저절로 꺼집니다."
Write-Host "(수동으로 다시 하려면 F5 → $Test. 6분이 지나도 안 꺼지면 직접 꺼 주세요)"
function Test-Game { [bool](Get-Process | Where-Object { $_.ProcessName -like 'valheim*' }) }
$t = 0
while (-not (Test-Game) -and $t -lt 180) { Start-Sleep 3; $t += 3 }   # 첫 실행은 느릴 수 있음
while (Test-Game) { Start-Sleep 3 }

# 4) 결과 수집 → 클립보드
$report = @('=== Valheim 테스트 결과 ===')
$diag = Join-Path $plugin 'diag.txt'
$report += '--- diag ---'
if (Test-Path $diag) { $report += Get-Content $diag -Encoding UTF8 }
else { $report += '(diag.txt 없음: vh_fartest를 실행하지 않았거나 모드가 로드되지 않았음)' }
$log = Join-Path $game 'BepInEx\LogOutput.log'
$report += '--- BepInEx 로그 (오류·이 모드) ---'
if (Test-Path $log) {
    $report += Get-Content $log -Encoding UTF8 |
        Where-Object { $_ -match 'Error|Exception|Valheim Helper|ValheimHelper|Valheim version|BepInEx \d' } |
        Select-Object -First 150
} else { $report += '(LogOutput.log 없음: BepInEx가 로드되지 않았음)' }
$text = $report -join "`r`n"
Set-Clipboard $text
Write-Host $text
Write-Host ''
Write-Host '결과를 클립보드에 복사했습니다. 대화창에 붙여넣어 주세요.'
