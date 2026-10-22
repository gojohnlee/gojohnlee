# 발헤임 모드 자동 설치 + 실행 + 결과 수집 (Windows PowerShell)
# 사용법 (PowerShell에 한 줄 붙여넣기):
#   irm https://raw.githubusercontent.com/gojohnlee/gojohnlee/valheim/scripts/valheim-test.ps1 | iex
# PC의 Claude Code 세션: $env:VH_DLL='<빌드한 ValheimHelper.dll>'; powershell -ExecutionPolicy Bypass -File scripts\valheim-test.ps1
#   (화면과 클립보드에는 짧은 요약만, 전체는 <게임>\BepInEx\plugins\ValheimHelper\diag.txt)
# 발헤임 창은 기본으로 최소화·소리 끔. 게임 화면을 보려면 앞에 $env:VH_MIN='0'; 을 붙인다.
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
# 지난 시험에서 멈춘 게임이 남아 있으면 새 게임이 안 뜨고 아래 대기가 끝나지 않는다(2026-10-21 루프 회차 9).
# Steam을 관리자로 켜 두면 게임도 관리자라 여기서 못 끈다 → 바로 알리고 멈춘다.
$old = @(Get-Process | Where-Object { $_.ProcessName -like 'valheim*' })
if ($old) {
    try { $old | Stop-Process -Force -ErrorAction Stop; Start-Sleep 5; Write-Host '남아 있던 게임을 껐습니다.' }
    catch { throw "남아 있던 게임(PID $($old.Id -join ','))을 끌 수 없습니다(액세스 거부 = Steam이 관리자 실행). 작업 관리자에서 끄고 Steam을 일반 권한으로 다시 켜 주세요." }
}
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
$Test = if ($env:VH_TEST) { $env:VH_TEST } else { 'vh_npctest;vh_flagtest;vh_woodtest;vh_stonetest' }   # 다른 시험: $env:VH_TEST='vh_fartest' (여러 개는 ;로). 묶음 1 새 시험: vh_departtest·vh_peertest
$World = if ($env:VH_WORLD) { $env:VH_WORLD } else { 'te4st41' }   # 시험 월드(메인 메뉴에서 자동으로 들어감)
$Min = $env:VH_MIN -ne '0'   # 최소화 시험(기본, 2026-10-21 사용자): 창 최소화·소리 끔·백그라운드 30fps. 게임 창을 보려면 $env:VH_MIN='0'
$Char = if ($env:VH_CHAR) { $env:VH_CHAR.ToLower() } else { 'vhtest' }   # 시험 전용 캐릭터(사용자 결정 2026-10-22, 없으면 모드가 만듦). 사용자 캐릭터는 안 씀

# 시험 월드·캐릭터 원본 되돌리기(사용자 결정 2026-10-22): 실행마다 한 번, 게임이 완전히 꺼진 뒤, 시험 전용 경로일 때만.
# 월드 원본은 %USERPROFILE%\vh-test-base(첫 실행 때 지금 월드를 떠 둠). 캐릭터는 지워서 모드가 새로 만들게(= 깨끗한 원본).
# 끄기: $env:VH_RESET='0'(실제 재시작 시험의 2번째 실행은 1번째가 저장한 월드·캐릭터를 그대로 불러와야 함).
if ($env:VH_RESET -ne '0') {
    $save = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim'
    $okWorlds = @('te4st41') + @(($env:VH_TEST_WORLDS -split ';') | Where-Object { $_ })   # 시험 월드로 확인된 이름만
    if ($okWorlds -notcontains $World -or $Char -notmatch '^vhtest') { throw "원본 되돌리기 중단: 시험 전용 월드·캐릭터가 아님(월드 $World, 캐릭터 $Char). 되돌리지 않으려면 VH_RESET=0" }
    if (Get-Process | Where-Object { $_.ProcessName -like 'valheim*' }) { throw '원본 되돌리기 중단: 게임이 아직 켜져 있음' }
    $wdir = Join-Path $save "worlds_local\$World"
    $base = Join-Path $env:USERPROFILE "vh-test-base\worlds_local\$World"
    if (-not (Test-Path $base)) {
        if (-not (Test-Path (Join-Path $wdir '*.ok'))) { throw "원본 되돌리기 중단: 원본도 없고 월드 $wdir 도 온전하지 않음" }
        New-Item -ItemType Directory (Split-Path $base) -Force | Out-Null
        Copy-Item $wdir $base -Recurse
        Write-Host "시험 월드 원본을 떠 둠: $base"
    }
    if (Test-Path $wdir) { Remove-Item $wdir -Recurse -Force }
    Copy-Item $base $wdir -Recurse
    Get-ChildItem (Join-Path $save 'characters_local') -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "$Char.fch*" -or $_.Name -like "${Char}_backup*.fch" } | Remove-Item -Force   # 게임 자동 백업도
    Write-Host "시험 월드 $World 원본으로 되돌림, 시험 캐릭터 $Char 새로"
}
Set-Content "$plugin\autotest.txt" ("world=$World;char=$Char;" +$(if ($Min) { 'min;' } else { '' }) + $Test)   # 모드가 월드에 들어가 저절로 시험하고 게임을 끈다
Remove-Item $tmp -Recurse -Force
Write-Host 'Valheim Helper 설치됨'

# BepInEx 콘솔(검은 창)이 게임과 같이 뜨지 않게 끈다(로그는 LogOutput.log에 그대로 남는다). 설정 파일은 첫 실행 뒤에 생긴다.
$cfg = Join-Path $game 'BepInEx\config\BepInEx.cfg'
if (Test-Path $cfg) {
    $c = [IO.File]::ReadAllText($cfg)
    # [^\[]: 다음 섹션(디스크 로그 등)의 Enabled로 넘어가지 않게
    $c2 = $c -replace '(\[Logging\.Console\][^\[]*?Enabled\s*=\s*)true', '${1}false'
    if ($c2 -ne $c) { [IO.File]::WriteAllText($cfg, $c2); Write-Host 'BepInEx 콘솔 창 끔' }
}

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
$t = 0
while ((Test-Game) -and $t -lt 1500) { Start-Sleep 3; $t += 3 }   # 25분 넘으면 멈춘 것으로 보고 끈다(못 끄면 다음 시험 시작 때 알림)
if (Test-Game) { Get-Process | Where-Object { $_.ProcessName -like 'valheim*' } | Stop-Process -Force -ErrorAction SilentlyContinue }

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
