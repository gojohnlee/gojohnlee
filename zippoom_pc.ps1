# 회사 PC 1회용 집품 수집(사용자 11-25). 집품수집.bat이 받아서 실행한다. 토큰은 저장하지 않는다.
$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$D = "$env:LOCALAPPDATA\wonroom"; New-Item -ItemType Directory -Force $D | Out-Null; Set-Location $D
$raw = 'https://raw.githubusercontent.com/gojohnlee/gojohnlee/house'
Write-Host '1/4 수집 파일 받는 중...'
Invoke-WebRequest "$raw/zippoom_pc.js" -OutFile "$D\zippoom_pc.js" -UseBasicParsing
Invoke-WebRequest "$raw/data/zippoom_cache.json" -OutFile "$D\zippoom_cache.json" -UseBasicParsing
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
  $nd = "$D\node-v20.18.1-win-x64"
  if (-not (Test-Path "$nd\node.exe")) {
    Write-Host '2/4 Node.js 받는 중(약 30MB, 설치 없이 이 폴더에만)...'
    Invoke-WebRequest 'https://nodejs.org/dist/v20.18.1/node-v20.18.1-win-x64.zip' -OutFile "$D\node.zip" -UseBasicParsing
    Expand-Archive "$D\node.zip" $D -Force; Remove-Item "$D\node.zip"
  }
  $env:Path = "$nd;$env:Path"
}
if (-not (Test-Path "$D\node_modules\playwright-core")) {
  Write-Host '3/4 브라우저 조종 도구(Playwright) 설치 중...'
  & npm.cmd install playwright-core@1.49.1 --no-audit --no-fund --silent
  if ($LASTEXITCODE -ne 0) { throw 'Playwright 설치 실패(회사 네트워크가 npm을 막았을 수 있음)' }
}
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) '원룸'
New-Item -ItemType Directory -Force $out | Out-Null
Write-Host ''
Write-Host '4/4 집품 토큰을 붙여넣고(마우스 오른쪽 클릭) Enter를 누르세요. 화면에는 * 로 보입니다.'
$s = Read-Host -AsSecureString '토큰'
$env:T = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
$env:OUT = $out
Write-Host '수집 시작. 창을 닫지 마세요(닫아도 다시 실행하면 이어서 합니다).'
try { & node "$D\zippoom_pc.js" } finally { Remove-Item Env:T -ErrorAction SilentlyContinue }
Write-Host ''
Write-Host "결과: $out\zippoom_result.json  ← 이 파일을 Claude 세션에 올려 주세요."
Start-Process explorer.exe $out
