# 전체 회귀 B12 = 멀티 묶음(전용 서버 + 손님). 플레이용 빌드(valheim-stable) 회귀에서 B1~B11 다음에 돌린다.
# 사용: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vh-b12.ps1 [-Dll <ValheimHelper.dll>]
#  서버 install(-Dll) → start → 손님 vh_jointest;vh_guestcmdtest;vh_guestchesttest;vh_guestrestart1 → 서버 stop·start → VH_RESET=0 vh_guestrestart2 → stop.
#  출력은 시험 줄·FAIL·Error만. 시작 때 시험 서버 월드(VhTest)를 지우고 새로.
param([string]$Dll = '')
$repo = Split-Path $PSScriptRoot -Parent
$srv = Join-Path $PSScriptRoot 'vh-server.ps1'
$test = [scriptblock]::Create([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'valheim-test.ps1'), [Text.Encoding]::UTF8))   # BOM 없는 UTF-8 → 5.1에서 직접 부르면 깨짐
$log = Join-Path $env:TEMP 'vh-server\server.log'
if (-not $Dll) { $Dll = Join-Path $repo 'valheim\dist\ValheimHelper.dll' }

function Start-Srv {
  & $srv start | Out-Null
  for ($i = 0; $i -lt 60; $i++) {   # 서버가 Steam에 붙을 때까지(최대 3분)
    Start-Sleep 3
    if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'Game server connected' -Quiet)) { return }
  }
  Write-Output 'B12: 서버 준비 줄 못 봄(3분) — 그대로 진행'
}
function Run-Test([string]$tests, [string]$reset) {
  $env:VH_TEST = $tests; $env:VH_DLL = $Dll
  if ($reset) { $env:VH_RESET = $reset } else { Remove-Item Env:VH_RESET -ErrorAction SilentlyContinue }
  "## $tests $(Get-Date -f HH:mm)"
  & $test *>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match '^\s+vh_|FAIL|Error' } | Select-Object -First 30
}

Remove-Item Env:VH_BIOME, Env:VH_QUICK -ErrorAction SilentlyContinue
& $srv stop | Out-Null
# 시험 전용 서버 월드를 새로(앞 시험 일꾼·상자가 남으면 guestcmd "일꾼 2명"·guestchest "상자 주인"이 꼬일 수 있음, 회차 432)
Remove-Item (Join-Path $env:TEMP 'vh-server\worlds_local\VhTest*') -Recurse -Force -ErrorAction SilentlyContinue
& $srv install -Dll $Dll | Out-Null
Start-Srv
$env:VH_JOIN = '127.0.0.1:2456'
try {
  Run-Test 'vh_jointest;vh_guestcmdtest;vh_guestchesttest;vh_guestrestart1' ''
  & $srv stop | Out-Null
  Start-Srv
  Run-Test 'vh_guestrestart2' '0'
} finally {
  Remove-Item Env:VH_JOIN, Env:VH_RESET -ErrorAction SilentlyContinue
  & $srv stop | Out-Null
}
