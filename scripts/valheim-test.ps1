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
# 다른 세션의 시험이 돌고 있으면 그 게임을 끄지 않고 끝날 때까지 기다린다(회차 189·190: 두 실행이 서로 게임을 꺼 시험이 끊김).
# 잠금 = %TEMP%\valheim-test.lock 안의 PID(이 스크립트를 돌리는 PowerShell). 그 프로세스가 없으면 남은 잠금으로 보고 무시한다.
$lock = Join-Path $env:TEMP 'valheim-test.lock'
$lw = 0
while ((Test-Path $lock) -and $lw -lt 2400) {
    $lp = (Get-Content $lock -ErrorAction SilentlyContinue | Select-Object -First 1) -as [int]
    if (-not $lp -or $lp -eq $PID -or -not (Get-Process -Id $lp -ErrorAction SilentlyContinue)) { break }
    if ($lw -eq 0) { Write-Host "다른 시험(PID $lp)이 도는 중 — 끝날 때까지 기다림(최대 40분)" }
    Start-Sleep 15; $lw += 15
}
if ($lw -ge 2400) { throw "다른 시험(PID $lp)이 40분 넘게 안 끝남 — 이번 시험 안 함" }
Set-Content $lock $PID
# 지난 시험에서 멈춘 게임이 남아 있으면 새 게임이 안 뜨고 아래 대기가 끝나지 않는다(2026-10-21 루프 회차 9).
# 끄는 것은 이 스크립트가 띄운 게임(%TEMP%\valheim-test-game.txt의 PID·시작 시각)만. 모르는 게임 = 사용자가 직접 플레이 중 →
# 끄지 않고 시험을 미룬다(exit 3, %TEMP%\valheim-test-deferred.txt → 루프가 status.txt에 한 줄, 사용자 11-11).
# Steam을 관리자로 켜 두면 게임도 관리자라 여기서 못 끈다 → 바로 알리고 멈춘다.
$gameFile = Join-Path $env:TEMP 'valheim-test-game.txt'
$deferFile = Join-Path $env:TEMP 'valheim-test-deferred.txt'
$g = (Get-Content $gameFile -ErrorAction SilentlyContinue | Select-Object -First 1) -split ' '
$ours = if ($g.Count -ge 2) { Get-Process -Id ($g[0] -as [int]) -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -eq 'valheim' -and $_.StartTime.Ticks -eq ($g[1] -as [long]) } }
$old = @(Get-Process | Where-Object { $_.ProcessName -eq 'valheim' })
$foreign = @($old | Where-Object { -not $ours -or $_.Id -ne $ours.Id })
if ($foreign) {
    Set-Content $deferFile "$(Get-Date -Format 'MM-dd HH:mm') PID $($foreign.Id -join ',')" -Encoding UTF8
    Remove-Item $lock -ErrorAction SilentlyContinue
    Write-Host "시험 미룸: 이 스크립트가 띄우지 않은 게임(PID $($foreign.Id -join ','))이 켜져 있음 = 직접 플레이 중으로 봄. 게임을 끈 뒤 다시."
    exit 3
}
Remove-Item $deferFile -ErrorAction SilentlyContinue
if ($old) {
    try { $old | Stop-Process -Force -ErrorAction Stop; Start-Sleep 5; Write-Host '남아 있던 시험 게임을 껐습니다.' }
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
if ($env:VH_NOMOD -eq '1') { Remove-Item "$plugin\ValheimHelper.dll" -ErrorAction SilentlyContinue; Write-Host '모드 끔: ValheimHelper.dll을 plugins에서 뺌(부하 비교용, 다음 실행이 다시 넣음)' }   # 회차 641 — 기본안 (가), decisions 부하 비교
elseif ($env:VH_DLL) { Copy-Item $env:VH_DLL "$plugin\ValheimHelper.dll" -Force }   # PC 세션: 방금 빌드한 DLL
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
while ((Get-Process | Where-Object { $_.ProcessName -eq 'valheim' }) -and $w -lt 60) { Start-Sleep 2; $w += 2 }
Remove-Item "$plugin\diag.txt" -ErrorAction SilentlyContinue

# 시험 월드·캐릭터 원본 되돌리기(사용자 결정 2026-10-22): 실행마다 한 번, 게임이 완전히 꺼진 뒤, 시험 전용 경로일 때만.
# 월드 원본은 %USERPROFILE%\vh-test-base(첫 실행 때 지금 월드를 떠 둠). 캐릭터는 지워서 모드가 새로 만들게(= 깨끗한 원본).
# 끄기: $env:VH_RESET='0'(실제 재시작 시험의 2번째 실행은 1번째가 저장한 월드·캐릭터를 그대로 불러와야 함).
if ($env:VH_RESET -ne '0') {
    $save = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim'
    $okWorlds = @('te4st41') + @(($env:VH_TEST_WORLDS -split ';') | Where-Object { $_ })   # 시험 월드로 확인된 이름만
    if ($okWorlds -notcontains $World -or $Char -notmatch '^vhtest') { throw "원본 되돌리기 중단: 시험 전용 월드·캐릭터가 아님(월드 $World, 캐릭터 $Char). 되돌리지 않으려면 VH_RESET=0" }
    if (Get-Process | Where-Object { $_.ProcessName -eq 'valheim' }) { throw '원본 되돌리기 중단: 게임이 아직 켜져 있음' }
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
    # 일꾼 기억 파일(월드 폴더 곁 <월드>.vh-memory, Brain.MemoryPath)도 원본대로: 원본에 있으면 복사, 없으면 지움
    if (Test-Path -LiteralPath "$resolvedWorld.vh-memory") { Remove-Item -LiteralPath "$resolvedWorld.vh-memory" -Force }
    if (Test-Path -LiteralPath "$base.vh-memory") { Copy-Item -LiteralPath "$base.vh-memory" "$resolvedWorld.vh-memory" }
    Get-ChildItem (Join-Path $save 'characters_local') -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "$Char.fch*" -or $_.Name -like "${Char}_backup*.fch" } | Remove-Item -Force   # 게임 자동 백업도
    Write-Host "시험 월드 $World 원본으로 되돌림, 시험 캐릭터 $Char 새로"
}
# 멀티 시험: $env:VH_JOIN='127.0.0.1:2456'(+ $env:VH_JOINPASS, 기본 vhtest1)이면 월드 대신 전용 서버(scripts/vh-server.ps1 start)에 손님으로 들어간다.
$Enter = if ($env:VH_JOIN) { "join=$($env:VH_JOIN);pass=$(if ($env:VH_JOINPASS) { $env:VH_JOINPASS } else { 'vhtest1' });" } else { "world=$World;" }
Set-Content "$plugin\autotest.txt" ("${Enter}char=$Char;" +$(if ($Min) { 'min;' } else { '' }) + $(if ($env:VH_QUICK -eq '1') { 'quick;' } else { '' }) + $(if ("$env:VH_SPEED" -match '^[0-9.]+$') { "speed=$($env:VH_SPEED);" } else { 'speed=3;' }) + $(if ($env:VH_FLOOR -eq '1') { 'floor;' } else { '' }) + $(if ($env:VH_HOUSE -eq '1') { 'house;' } else { '' }) + $(if ($env:VH_BIOME) { "biome=$($env:VH_BIOME);" } else { '' }) + $(if ($env:VH_SPOT) { "spot=$($env:VH_SPOT);" } else { '' }) + $Run)   # 늪 회귀 $env:VH_BIOME='swamp'(밭 시험 등은 원래 자리). 물가 $env:VH_BIOME='meadows' + $env:VH_SPOT='shore'. 약식 시험 $env:VH_QUICK='1'(긴 시험만 짧게, FAIL이면 정식으로 다시). 모드가 월드에 들어가 저절로 시험하고 게임을 끈다
# 배속(사용자 11-13): 자동 시험은 기본 3배(speed=3). $env:VH_SPEED="1"이면 1배(최종 실전 시험·1배와 결과 다른 시험 확인). 손님 있는 시험은 게임이 1배로 묶음
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
    Start-Process "steam://rungameid/$AppId"
}
Write-Host ''
Write-Host "게임 실행 중... 시험 월드($World)로 저절로 들어갑니다. 손대지 않아도 됩니다."
Write-Host '(그 월드가 없으면 직접 테스트용 월드에 들어가 주세요)'
Write-Host "3초 뒤 시험($Run)이 저절로 시작되고, 시험이 끝나면(최대 15분) 게임이 저절로 꺼집니다."
Write-Host "(20분이 지나도 안 꺼지면 직접 꺼 주세요)"
function Test-Game { [bool](Get-Process | Where-Object { $_.ProcessName -eq 'valheim' }) }
$t = 0
if ($Min) {
    # 모드가 올라오기 전(유니티 시작 화면)에도 창을 바로 내린다. 그 뒤는 모드가 첫 시험 전까지 내려 둔다.
    Add-Type -Namespace VH -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
'@
    $seen = 0
    while ($t -lt 180 -and $seen -lt 20) {
        $p = Get-Process | Where-Object { $_.ProcessName -eq 'valheim' -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($p) { $seen++; if (-not [VH.Win]::IsIconic($p.MainWindowHandle)) { [void][VH.Win]::ShowWindowAsync($p.MainWindowHandle, 6) } }
        Start-Sleep -Milliseconds 250; $t += 0.25
    }
}
while (-not (Test-Game) -and $t -lt 180) { Start-Sleep 3; $t += 3 }   # 첫 실행은 느릴 수 있음
$gp = Get-Process | Where-Object { $_.ProcessName -eq 'valheim' } | Select-Object -First 1
$ourId = if ($gp) { $gp.Id; Set-Content $gameFile "$($gp.Id) $($gp.StartTime.Ticks)" }   # 다음 시험의 '모르는 게임' 판단용
$cpu0 = if ($gp) { $gp.TotalProcessorTime.TotalSeconds }; $wall0 = Get-Date   # 외부 부하: 게임 CPU 시간(모드 끔에도 남는 재는 값, 회차 642)
$t = 0; $exitHang = $false; $fatalLine = $null
$gameLog = Join-Path $game 'BepInEx\LogOutput.log'
# 상한 기본 30분. 긴 시험(최종 실전 시험 등)은 $env:VH_MAXMIN="분"(사용자 11-13: 최종 시험은 30분보다 확실히 김)
$maxMin = if ("$env:VH_MAXMIN" -match '^\d+$' -and [int]$env:VH_MAXMIN -gt 0) { [int]$env:VH_MAXMIN } else { 30 }
while ((Test-Game) -and $t -lt $maxMin * 60) {   # 상한 넘으면 멈춘 것으로 보고 끈다(못 끄면 다음 시험 시작 때 알림)
    Start-Sleep 3; $t += 3
    # 종료 시작("Game - OnApplicationQuit", 6번 다 있음 — ZNet OnDestroy는 없는 실행도 있었음)이 이번 실행 로그에 찍힌 뒤 로그가 2분 넘게 멈춰 있으면
    # 종료 멈춤(10-26 세 번, 응답 없음) → 30분 기다리지 않고 끈다(E038). 멈춘 게임이 로그를 쥐고 있어 Select-String은 못 읽음 → Get-Content.
    $gp = Get-Process | Where-Object { $_.ProcessName -eq 'valheim' } | Select-Object -First 1
    if ($gp) { $cpuLast = $gp.TotalProcessorTime.TotalSeconds; $wallLast = Get-Date }   # 마지막으로 본 값(게임은 시험 끝에 꺼져 뒤에 찾으면 없음, 회차 643)
    $li = Get-Item $gameLog -ErrorAction SilentlyContinue
    if ($gp -and $li -and $li.LastWriteTime -gt $gp.StartTime -and ((Get-Date) - $li.LastWriteTime).TotalSeconds -gt 120 -and
        @((Get-Content $gameLog -Encoding UTF8 -ErrorAction SilentlyContinue) -match 'Game - OnApplicationQuit').Count) { $exitHang = $true; break }
    # 모드 코드가 아예 안 불러와지는 오류(11-14 System.ValueTuple로 채팅 명령 전부 먹통 — 시험은 끝까지 기다려 봐야 FAIL)
    # → 15초마다 이번 실행 로그 끝을 보고, 보이면 바로 끄고 FAIL로 알린다(사용자 11-14: 실패면 기다리지 말고 고쳐서 다시).
    if ($gp -and $li -and $li.LastWriteTime -gt $gp.StartTime -and $t % 15 -eq 0) {
        $fatal = @(Get-Content $gameLog -Encoding UTF8 -Tail 400 -ErrorAction SilentlyContinue |
            Select-String -Pattern 'TypeLoadException|Could not load (file or assembly|type)|MissingMethodException|MissingFieldException' | Select-Object -First 1)
        if ($fatal.Count) { $fatalLine = "$($fatal[0].Line)".Trim(); break }
    }
}
if ($fatalLine) {
    Get-Process | Where-Object { $_.ProcessName -eq 'valheim' -and (-not $ourId -or $_.Id -eq $ourId) } | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process | Where-Object { $_.ProcessName -eq 'valheim' -and (-not $ourId -or $_.Id -eq $ourId) } | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
}
$runTimedOut = Test-Game
$cpuLine = $null
if ($cpu0 -and $cpuLast) {
    $wallS = ($wallLast - $wall0).TotalSeconds; $cpuS = $cpuLast - $cpu0
    if ($wallS -gt 0) { $cpuLine = '[외부 부하] 게임 CPU {0:N0}% (1코어=100%, 벽시계 {1:N0}초, 모드 {2})' -f (100 * $cpuS / $wallS), $wallS, $(if ($env:VH_NOMOD -eq '1') { '끔' } else { '켬' }) }
}
if ($runTimedOut) {
    Get-Process | Where-Object { $_.ProcessName -eq 'valheim' -and (-not $ourId -or $_.Id -eq $ourId) } | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process | Where-Object { $_.ProcessName -eq 'valheim' -and (-not $ourId -or $_.Id -eq $ourId) } | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue   # 로그 파일을 놓을 때까지
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
    # 로그 오류 셈(사용자 11-13 18:00): 알려진 Unity·BepInEx 잡음은 뺌(목록은 여기 한 곳), 예외는 스택(아래 20줄)에 ValheimHelper가 있을 때만 우리 오류
    $noise = @('Unable to start Unity log writer')
    $errs = @(); $noiseN = 0
    for ($i = 0; $i -lt $all.Count; $i++) {
        $l = $all[$i]
        if ($l -notmatch 'Error|Exception') { continue }
        if (@($noise | Where-Object { $l.Contains($_) }).Count -gt 0) { $noiseN++; continue }
        if ($l -match 'Exception' -and $l -notmatch 'ValheimHelper') {
            $ours = $false
            for ($j = $i + 1; $j -lt [Math]::Min($all.Count, $i + 21); $j++) {
                if ($all[$j] -match '^\[(Info|Message|Warning|Error|Debug|Fatal)') { break }
                if ($all[$j] -match 'ValheimHelper') { $ours = $true; break }
            }
            if (-not $ours) { $noiseN++; continue }
        }
        $errs += $l
    }
    $kinds = @($errs | ForEach-Object { $_.Trim() } | Select-Object -Unique)
    $report += "로그 오류 $($errs.Count)줄(종류 $($kinds.Count), 잡음 $($noiseN)줄 뺌)"
    foreach ($e in ($kinds | Select-Object -First 8)) {
        if ($e.Length -gt 200) { $e = $e.Substring(0, 200) + '...' }
        $report += "  $e"
    }
} else { $report += '(LogOutput.log 없음: BepInEx가 로드되지 않았음)' }
if ($env:VH_JOIN) {   # 멀티 시험: 서버 쪽 모드 로그의 오류도(손님이 들어온 동안 서버 null 예외 0인지)
    $slog = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Valheim dedicated server\BepInEx\LogOutput.log'
    if (Test-Path $slog) {
        $serrs = @(Get-Content $slog -Encoding UTF8 | Where-Object { $_ -match 'Error|Exception' } | ForEach-Object { $_.Trim() })
        $report += "서버 로그 오류 $($serrs.Count)줄"
        foreach ($e in ($serrs | Select-Object -Unique -First 5)) { $report += "  $(if ($e.Length -gt 200) { $e.Substring(0, 200) + '...' } else { $e })" }
    } else { $report += "(서버 LogOutput.log 없음: $slog)" }
}
if ($cpuLine) { $report += $cpuLine }
$report += "자세히(FAIL일 때만): $diag 에서 '=== 시험이름' 부분, 오류는 $log"
if ($fatalLine) { $report += "FAIL 모드 치명 오류로 바로 끔(끝까지 기다리지 않음): $fatalLine — 고쳐서 다시 시험" }
if ($exitHang) { $report += '게임 종료 멈춤: 종료 시작(OnApplicationQuit) 뒤 로그가 2분 멈춰 안 꺼져 강제 종료(Errors.txt E038 — 발생 횟수 더하기). 시험 판정은 그대로' }
elseif ($runTimedOut) { $report += "게임이 ${maxMin}분 안에 안 꺼져 강제 종료(더 길게: `$env:VH_MAXMIN) — 중단(PASS로 치지 않음)" }
if ($Solo -and (Test-Path $diag)) { Copy-Item $diag "$plugin\diag-$Run.txt" -Force; $report += "  (단독 diag: $plugin\diag-$Run.txt)" }
$reports += ($report -join "`r`n")
}   # foreach $Run
$text = $reports -join "`r`n`r`n"
Set-Clipboard $text
Write-Host $text
Write-Host ''
Write-Host '결과를 클립보드에 복사했습니다. 대화창에 붙여넣어 주세요.'
if ((Get-Content $lock -ErrorAction SilentlyContinue | Select-Object -First 1) -eq "$PID") { Remove-Item $lock -ErrorAction SilentlyContinue }
