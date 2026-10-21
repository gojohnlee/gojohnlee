# RimWorld 모드 자동 설치 + 실행 + 결과 수집 (Windows PowerShell)
# 사용법 (PowerShell에 한 줄 붙여넣기):
#   irm https://raw.githubusercontent.com/gojohnlee/gojohnlee/rimworld/scripts/rimworld-test.ps1 | iex
$ErrorActionPreference = 'Stop'
$Repo   = 'gojohnlee/gojohnlee'
$ZipUrl = "https://github.com/$Repo/releases/download/rimworld-latest/rimworld-mods.zip"
$AppId  = 294100

function Find-RimWorld {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { throw 'Steam 경로를 찾지 못했습니다.' }
    $libs = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libs += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' |
            ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($lib in $libs | Select-Object -Unique) {
        $dir = Join-Path $lib 'steamapps\common\RimWorld'
        if (Test-Path $dir) { return $dir }
    }
    throw 'RimWorld 설치 폴더를 찾지 못했습니다.'
}

$game = Find-RimWorld
$mods = Join-Path $game 'Mods'
Write-Host "RimWorld: $game"

# 1) 최신 빌드 다운로드 & 설치 (기존 폴더 교체)
$tmp = Join-Path $env:TEMP "rimworld-mods-$(Get-Random)"
New-Item -ItemType Directory $tmp | Out-Null
Invoke-WebRequest $ZipUrl -OutFile "$tmp\mods.zip"
Expand-Archive "$tmp\mods.zip" "$tmp\x"
$installed = @()
foreach ($m in Get-ChildItem "$tmp\x" -Directory) {
    $dst = Join-Path $mods $m.Name
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    Copy-Item $m.FullName $dst -Recurse
    $installed += $m.Name
}
Remove-Item $tmp -Recurse -Force
Write-Host "설치됨: $($installed -join ', ')"
Write-Host '처음이라면 게임 모드 메뉴에서 Harmony → 위 모드 순서로 켜 주세요.'

# 2) 게임 실행 후 종료될 때까지 대기
Start-Process "steam://rungameid/$AppId"
Write-Host '게임 실행 중... 새 게임/불러오기로 메시지를 확인한 뒤 게임을 종료하세요.'
Start-Sleep 20
while (Get-Process | Where-Object { $_.ProcessName -like 'RimWorld*' }) { Start-Sleep 3 }

# 3) 결과 수집 → 클립보드
$report = @('=== RimWorld 테스트 결과 ===')
foreach ($name in $installed) {
    $diag = Join-Path $mods "$name\diag.txt"
    $report += "--- $name diag ---"
    if (Test-Path $diag) { $report += Get-Content $diag }
    else { $report += '(diag.txt 없음: 모드가 로드되지 않았음)' }
}
$logs = @(
    "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log"
) + (Get-ChildItem "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios", "$env:APPDATA\Ludeon Studios", "$env:LOCALAPPDATA\Ludeon Studios" `
        -Recurse -Filter *.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName)
$log = $logs | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
$report += '--- log ---'
if ($log) {
    $report += "path: $log"
    $report += Select-String -Path $log -Pattern 'HelloRimWorld|\[\w+\]|Exception|Error' -Context 0,3 |
        Select-Object -First 80 | ForEach-Object { $_.ToString() }
} else { $report += '(로그 파일을 찾지 못함)' }

$text = $report -join "`r`n"
Set-Clipboard $text
Write-Host $text
Write-Host "`n결과가 클립보드에 복사됐습니다. Claude 대화에 붙여넣으세요."
