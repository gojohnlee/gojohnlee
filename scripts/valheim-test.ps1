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
    if ($env:VH_HARNESS -eq '1') { throw 'Harness refuses to terminate an existing game.' }
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
# 깨끗한 단독 시험(사용자 결정 2026-10-22): $env:VH_SOLO='1'이면 시험마다 따로 원본을 되돌리고 게임을 새로 켠다.
# 기본은 연속 묶음(실행마다 한 번 되돌리고 A→B→B를 한 게임에서 이어 돌림 — 앞 시험이 뒤 시험을 망치는지 보는 용도).
$Solo = $env:VH_SOLO -eq '1'
if ($Solo -and $env:VH_RESET -eq '0') { throw '단독 시험은 원본 되돌리기가 필요함(VH_SOLO=1과 VH_RESET=0은 같이 못 씀)' }
$runs = if ($Solo) { @($Test -split ';' | Where-Object { $_ }) } else { @($Test) }
$resolvedTmp=[IO.Path]::GetFullPath($tmp)
$tempRoot=[IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\'
if (-not $resolvedTmp.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolvedTmp -Leaf) -notmatch '^valheim-test-\d+$') { throw 'Unsafe temporary cleanup path' }
Remove-Item -LiteralPath $resolvedTmp -Recurse -Force
$reports = @()
foreach ($Run in $runs) {
$runStarted = Get-Date
$w = 0   # 앞 실행의 게임 프로세스가 완전히 끝날 때까지(되돌리기 조건)
while ((Get-Process | Where-Object { $_.ProcessName -like 'valheim*' }) -and $w -lt 60) { Start-Sleep 2; $w += 2 }
Remove-Item "$plugin\diag.txt" -ErrorAction SilentlyContinue

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
    # Verify recursive replacement targets before touching the test save.
    $worldRoot = [IO.Path]::GetFullPath((Join-Path $save 'worlds_local')).TrimEnd('\') + '\'
    $resolvedWorld = [IO.Path]::GetFullPath($wdir)
    if (-not $resolvedWorld.StartsWith($worldRoot, [StringComparison]::OrdinalIgnoreCase) -or $World -notmatch '^[a-zA-Z0-9_-]+$' -or $Char -notmatch '^vhtest[a-z0-9_-]*$') { throw 'Unsafe test save path' }
    if (Test-Path $wdir) { Remove-Item -LiteralPath $resolvedWorld -Recurse -Force }
    Copy-Item $base $wdir -Recurse
    Get-ChildItem (Join-Path $save 'characters_local') -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "$Char.fch*" -or $_.Name -like "${Char}_backup*.fch" } | Remove-Item -Force   # 게임 자동 백업도
    Write-Host "시험 월드 $World 원본으로 되돌림, 시험 캐릭터 $Char 새로"
}
Set-Content "$plugin\autotest.txt" ("world=$World;char=$Char;" +$(if ($Min) { 'min;' } else { '' }) + $(if ($env:VH_QUICK -eq '1') { 'quick;' } else { '' }) + $(if ($env:VH_BIOME) { "biome=$($env:VH_BIOME);" } else { '' }) + $Run)   # 늪 회귀 $env:VH_BIOME='swamp'(밭 시험 등은 원래 자리). 약식 시험 $env:VH_QUICK='1'(긴 시험만 짧게, FAIL이면 정식으로 다시). 모드가 월드에 들어가 저절로 시험하고 게임을 끈다
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
# steam://rungameid로 켜면 Steam이 "게임을 시작하는 중" 창을 잠깐 띄운다(2026-10-22 사용자) → Steam이 켜져 있으면 valheim.exe를 직접 켠다.
# 게임 폴더의 steam_appid.txt 덕에 Steam을 거쳐 다시 켜지지 않고, BepInEx(winhttp.dll)도 그대로 올라온다. Steam이 꺼져 있으면 예전처럼 Steam으로.
if ((Get-Process steam -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $game 'steam_appid.txt'))) {
    $runGameProcess = Start-Process (Join-Path $game 'valheim.exe') -WorkingDirectory $game -PassThru -WindowStyle Hidden
} else {
    if ($env:VH_HARNESS -eq '1') { throw 'Start Steam first; harness requires a directly observed game process.' }
    Start-Process "steam://rungameid/$AppId"
}
Write-Host ''
Write-Host "게임 실행 중... 시험 월드($World)로 저절로 들어갑니다. 손대지 않아도 됩니다."
Write-Host '(그 월드가 없으면 직접 테스트용 월드에 들어가 주세요)'
Write-Host "3초 뒤 시험($Run)이 저절로 시작되고, 시험이 끝나면(최대 15분) 게임이 저절로 꺼집니다."
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
$t = 0; $exitHang = $false
$gameLog = Join-Path $game 'BepInEx\LogOutput.log'
while ((Test-Game) -and $t -lt 1800) {   # 30분 넘으면 멈춘 것으로 보고 끈다(못 끄면 다음 시험 시작 때 알림)
    Start-Sleep 3; $t += 3
    # 저장 끝(ZNet OnDestroy, 정상이면 1~2초 뒤 꺼짐)이 이번 실행 로그에 찍힌 뒤 로그가 2분 넘게 멈춰 있으면 종료 멈춤(10-26 두 번: 마지막 줄은
    # "Steam manager on destroy"·"Stopping build thread"로 달랐음, 응답 없음) → 30분 기다리지 않고 끈다(E038).
    $gp = Get-Process | Where-Object { $_.ProcessName -like 'valheim*' } | Select-Object -First 1
    $li = Get-Item $gameLog -ErrorAction SilentlyContinue
    if ($gp -and $li -and $li.LastWriteTime -gt $gp.StartTime -and ((Get-Date) - $li.LastWriteTime).TotalSeconds -gt 120 -and
        (Select-String -LiteralPath $gameLog -SimpleMatch 'ZNet OnDestroy' -Quiet -ErrorAction SilentlyContinue)) { $exitHang = $true; break }
}
$runTimedOut = Test-Game
if ($runTimedOut) {
    Get-Process | Where-Object { $_.ProcessName -like 'valheim*' } | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process | Where-Object { $_.ProcessName -like 'valheim*' } | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue   # 로그 파일을 놓을 때까지
}

# 4) 결과 → 짧은 요약만 화면·클립보드에(토큰 아끼기, 2026-10-21). 자세한 건 diag.txt·LogOutput.log를 FAIL일 때만 연다.
#    요약: diag 첫 줄, 시험별 판정·시간, FAIL 줄, 모드 로드 줄, 로그 오류 줄 수와 종류별 첫 8개.
$diag = Join-Path $plugin 'diag.txt'
$log = Join-Path $game 'BepInEx\LogOutput.log'
$report = @("=== Valheim 테스트 결과 ===$(if ($Solo) { " (단독: $Run)" })")
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
if ($exitHang) { $report += '게임 종료 멈춤: 저장 끝(ZNet OnDestroy) 뒤 2분 넘게 안 꺼져 강제 종료(Errors.txt E038 — 발생 횟수 더하기). 시험 판정은 그대로' }
elseif ($runTimedOut) { $report += '게임이 30분 안에 안 꺼져 강제 종료 — 중단(PASS로 치지 않음)' }
if ($Solo -and (Test-Path $diag)) { Copy-Item $diag "$plugin\diag-$Run.txt" -Force; $report += "  (단독 diag: $plugin\diag-$Run.txt)" }
$reports += ($report -join "`r`n")
if ($env:VH_EVIDENCE_DIR) {
    # Copy originals before any retry can clear them. Harness invokes one run per process.
    . (Join-Path $env:VH_HARNESS_SCRIPTS 'vh-harness-lib.ps1')
    $evidenceDir = $env:VH_EVIDENCE_DIR
    $diagText = ''; $logText = ''
    # 복사본을 읽는다: 강제로 끈 게임이 원본을 아직 쥐고 있으면 ReadAllText가 실패해 run.json이 안 생겼음(10-26)
    if (Test-Path $diag) { Copy-Item -LiteralPath $diag -Destination (Join-Path $evidenceDir 'diag.txt'); $diagText = [IO.File]::ReadAllText((Join-Path $evidenceDir 'diag.txt')) }
    if (Test-Path $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $evidenceDir 'LogOutput.log'); $logText = [IO.File]::ReadAllText((Join-Path $evidenceDir 'LogOutput.log')) }
    $parsed = Read-VhDiag $diagText @($Run -split ';' | Where-Object { $_ })
    $status = $parsed.status
    # 저장이 끝난 뒤의 종료 멈춤(exit_hang)은 모드 동작과 무관 → 시험 판정을 그대로 두고 run.json에 표시만(PC 세션 10-26 기본안, E038)
    if ($runGameProcess) { $runGameProcess.WaitForExit(); if ($runGameProcess.ExitCode -ne 0 -and -not $exitHang) { $status='ABORTED' } }
    if ($logText -notmatch 'Valheim Helper .* 로드됨') { $status = 'ENVIRONMENT' }
    if (@(Get-VhLogProblems $logText).Count) { $status = 'EXCEPTION' }   # 알려진 무해 줄(BepInEx log writer)만 뺀다
    if ($runTimedOut -and -not $exitHang) { $status = 'ABORTED' }
    Write-VhJson (Join-Path $evidenceDir 'run.json') ([ordered]@{
        status=$status; started=$runStarted.ToString('o'); ended=(Get-Date).ToString('o'); tests=$Run
        dll_hash=(Get-VhHash (Join-Path $plugin 'ValheimHelper.dll')); world=$World; character=$Char
        reset=($env:VH_RESET -ne '0'); quick=($env:VH_QUICK -eq '1'); biome=$env:VH_BIOME; exit_hang=$exitHang; sections=$parsed.sections
    })
}
}   # foreach $Run
$text = $reports -join "`r`n`r`n"
if ($env:VH_HARNESS -ne '1') { Set-Clipboard $text }
Write-Host $text
Write-Host ''
if ($env:VH_HARNESS -ne '1') { Write-Host '결과를 클립보드에 복사했습니다. 대화창에 붙여넣어 주세요.' }
