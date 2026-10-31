# 같은 PC 전용 서버(Valheim Dedicated Server, 앱 896660)로 멀티 시험.
# 사용: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vh-server.ps1 <install|start|stop|status> [-Dll <ValheimHelper.dll>]
#  install: 게임 폴더의 BepInEx(core·config 제외 plugins)·doorstop을 서버 폴더로 복사, 플러그인 DLL 복사(기본 valheim/dist).
#  start  : -nographics -batchmode, 공개 안 함, 월드·저장은 %TEMP%\vh-server(평소 월드와 따로). 로그는 %TEMP%\vh-server\server.log(Unity 전체)·서버 BepInEx\LogOutput.log(모드).
#  손님 접속: 127.0.0.1:2456, 비번 아래 $Pass.
param([Parameter(Position=0)][string]$Action = 'status', [string]$Dll = '')
$ErrorActionPreference = 'Stop'
$steam = 'C:\Program Files (x86)\Steam\steamapps\common'
$Game = Join-Path $steam 'Valheim'
$Srv = Join-Path $steam 'Valheim dedicated server'
$Save = Join-Path $env:TEMP 'vh-server'
$World = 'VhTest'; $Pass = 'vhtest1'; $Port = 2456
$repo = Split-Path $PSScriptRoot -Parent

function Get-Srv { Get-Process valheim_server -ErrorAction SilentlyContinue }

if (-not (Test-Path (Join-Path $Srv 'valheim_server.exe'))) { Write-Output "NO_SERVER: $Srv 없음(Steam에서 Valheim Dedicated Server 설치 필요)"; exit 2 }

switch ($Action) {
  'install' {
    if (Get-Srv) { Write-Output 'server running — stop first'; exit 1 }
    foreach ($f in 'winhttp.dll', 'doorstop_config.ini') { Copy-Item (Join-Path $Game $f) $Srv -Force }
    Copy-Item (Join-Path $Game 'doorstop_libs') $Srv -Recurse -Force
    foreach ($d in 'core', 'patchers') {
      $dst = Join-Path $Srv "BepInEx\$d"; New-Item -ItemType Directory -Force $dst | Out-Null
      Copy-Item (Join-Path $Game "BepInEx\$d\*") $dst -Recurse -Force
    }
    $pl = Join-Path $Srv 'BepInEx\plugins\ValheimHelper'; New-Item -ItemType Directory -Force $pl | Out-Null
    if (-not $Dll) { $Dll = Join-Path $repo 'valheim\dist\ValheimHelper.dll' }
    Copy-Item $Dll $pl -Force
    Write-Output "installed: $Dll"
  }
  'start' {
    if (Get-Srv) { Write-Output 'already running'; exit 0 }
    New-Item -ItemType Directory -Force $Save | Out-Null
    $env:SteamAppId = '892970'
    $a = @('-nographics', '-batchmode', '-name', 'VhTest', '-port', "$Port", '-world', $World, '-password', $Pass, '-public', '0', '-savedir', "`"$Save`"", '-logFile', "`"$Save\server.log`"")
    $p = Start-Process (Join-Path $Srv 'valheim_server.exe') -ArgumentList $a -WorkingDirectory $Srv -WindowStyle Minimized -PassThru
    Write-Output "started pid $($p.Id)"
  }
  'stop' {
    $p = Get-Srv
    if (-not $p) { Write-Output 'not running'; exit 0 }
    # 창 닫기 신호 → 서버가 월드를 저장하고 끝남. 60초 안에 안 끝나면 강제.
    $p | ForEach-Object { [void]$_.CloseMainWindow() }
    $p | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue
    if (Get-Srv) { Get-Srv | Stop-Process -Force; Write-Output 'stopped (forced)' } else { Write-Output 'stopped' }
  }
  default {
    $p = Get-Srv
    Write-Output ("running: " + [bool]$p + "  plugin: " + (Test-Path (Join-Path $Srv 'BepInEx\plugins\ValheimHelper\ValheimHelper.dll')))
  }
}
