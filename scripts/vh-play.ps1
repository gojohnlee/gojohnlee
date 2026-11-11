# 직접 플레이 시작 (사용자 11-11): 루프를 쉬게 하고 → 돌던 시험이 끝나길 기다리고 → 플레이용 빌드(태그 valheim-stable) DLL을 넣고
# → 게임을 켜고 → 게임이 꺼지면 쉼 파일을 지울지 묻는다.
# 사용법: powershell -ExecutionPolicy Bypass -File scripts\vh-play.ps1 [-Hours 12]
#   쉼 = docs/valheim/loop-pause.txt(첫 줄 다시 시작할 시각, vh-loop.ps1 Get-PauseUntil). 돌던 회차는 끝까지 하지만 새 시험은
#   모르는 게임(이 스크립트가 켠 게임)을 보면 끄지 않고 미룬다(valheim-test.ps1).
param([int]$Hours = 12)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot -Parent
$AppId = 892970
function G { git -C $Repo @args 2>&1 }

# 1) 쉼 파일 + 커밋 한 줄(회차가 git add -A로 쓸어 가지 않게)
$pause = 'docs/valheim/loop-pause.txt'
$until = (Get-Date).AddHours($Hours).ToString('yyyy-MM-dd HH:mm')
[IO.File]::WriteAllText((Join-Path $Repo $pause), "$until`n", (New-Object Text.UTF8Encoding $false))
G add $pause | Out-Null
G commit -q -m "Valheim 직접 플레이: 루프 쉼 $until까지" -- $pause | Out-Null
G push -q origin valheim | Out-Null
Write-Host "루프 쉼: $until까지 새 회차 안 함"

# 2) 돌던 시험 끝 기다림(잠금 = %TEMP%\valheim-test.lock 안의 PID, valheim-test.ps1)
$lock = Join-Path $env:TEMP 'valheim-test.lock'
$w = 0
while ($true) {
    $lp = Get-Content $lock -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $lp -or -not (Get-Process -Id $lp -ErrorAction SilentlyContinue)) { break }
    if ($w -eq 0) { Write-Host "시험(PID $lp)이 도는 중 — 끝날 때까지 기다림" }
    Start-Sleep 5; $w += 5
}
if (Get-Process valheim -ErrorAction SilentlyContinue) { Write-Host '게임이 이미 켜져 있음 — 끈 뒤 다시 실행하세요.'; exit 1 }

# 3) 게임 폴더 찾기(valheim-test.ps1 Find-Valheim과 같음)
$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
if (-not $steam) { throw 'Steam 경로를 찾지 못했습니다.' }
$libs = @($steam)
$vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
if (Test-Path $vdf) { $libs += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' } }
$game = $libs | Select-Object -Unique | ForEach-Object { Join-Path $_ 'steamapps\common\Valheim' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $game) { throw 'Valheim 설치 폴더를 찾지 못했습니다.' }
$plugin = Join-Path $game 'BepInEx\plugins\ValheimHelper'

# 4) 플레이용 DLL(태그 valheim-stable의 dist) 넣기 + 자동 시험 파일 지우기
G fetch -q -f origin 'refs/tags/valheim-stable:refs/tags/valheim-stable' | Out-Null
$ref = 'valheim-stable'
G rev-parse -q --verify "$ref^{commit}" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host '플레이용 빌드(태그 valheim-stable)가 아직 없음 — 전체 회귀를 다 통과한 빌드가 없다는 뜻.'
    if ((Read-Host '시험 전일 수 있는 최신(valheim) 빌드로 할까요? (y/N)') -ne 'y') { exit 1 }
    $ref = 'origin/valheim'
}
New-Item -ItemType Directory $plugin -Force | Out-Null
$tmp = Join-Path $env:TEMP 'vh-play.dll'
cmd /c "git -C `"$Repo`" show ${ref}:valheim/dist/ValheimHelper.dll > `"$tmp`""
if ($LASTEXITCODE -ne 0 -or (Get-Item $tmp).Length -lt 10000) { throw "$ref 의 DLL을 꺼내지 못했습니다." }
Copy-Item $tmp "$plugin\ValheimHelper.dll" -Force
Remove-Item "$plugin\autotest.txt" -ErrorAction SilentlyContinue
Write-Host "DLL: $ref $(G rev-parse --short "$ref^{commit}")"

# 5) 게임 실행(Steam으로) → 꺼질 때까지 기다림
Start-Process "steam://rungameid/$AppId"
$w = 0
while (-not (Get-Process valheim -ErrorAction SilentlyContinue) -and $w -lt 180) { Start-Sleep 2; $w += 2 }
Write-Host '게임 실행 중 — 끄면 이어서 묻습니다.'
while (Get-Process valheim -ErrorAction SilentlyContinue) { Start-Sleep 10 }

# 6) 쉼 파일 지울지
if ((Read-Host '루프를 다시 돌릴까요(쉼 파일 지움)? (Y/n)') -ne 'n') {
    G rm -q $pause | Out-Null
    G commit -q -m 'Valheim 직접 플레이 끝: 루프 쉼 풂' -- $pause | Out-Null
    G push -q origin valheim | Out-Null
    Write-Host '쉼 풂 — 루프가 다음 확인 때 다시 돔.'
} else { Write-Host "쉼 그대로: $until에 저절로 다시 돔." }
