# naver-scan.ps1 - runs on the user's PC (Naver drops cloud IPs). Personal house-hunting scale:
# one area, max 15 list pages, 3 s between requests, no login. Writes data/naver/latest.json and pushes to branch 'house'.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File naver-scan.ps1
param([string]$Repo = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Set-Location $Repo
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$q = [ordered]@{ rletTpCd='OR:OPST'; tradTpCd='B2'; z='14'; lat='37.3923'; lon='126.9565'; btm='37.365'; lft='126.922'; top='37.420'; rgt='126.991'; wprcMax='2000'; rprcMax='68' }
$qs = ($q.GetEnumerator() | ForEach-Object { "$($_.Key)=$([uri]::EscapeDataString($_.Value))" }) -join '&'
$all = @(); $status = 'ok'
for ($p = 1; $p -le 15; $p++) {
  $wc = New-Object System.Net.WebClient
  $wc.Encoding = [Text.Encoding]::UTF8
  $wc.Headers.Add('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)')
  $wc.Headers.Add('Referer', 'https://m.land.naver.com/')
  try { $r = $wc.DownloadString("https://m.land.naver.com/cluster/ajax/articleList?$qs&page=$p") | ConvertFrom-Json }
  catch { $status = "error page $p : $($_.Exception.Message)"; break }
  if (-not $r.body) { if ($p -eq 1) { $status = "empty page 1 (code=$($r.code))" }; break }
  $all += $r.body
  if (-not $r.more) { break }
  Start-Sleep -Seconds 3
}
git pull -q --rebase origin house
New-Item -ItemType Directory -Force -Path 'data/naver' | Out-Null
$out = [pscustomobject]@{ fetched = (Get-Date -Format 's'); status = $status; count = $all.Count; items = $all }
$json = $out | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $Repo 'data/naver/latest.json'), $json, (New-Object Text.UTF8Encoding $false))
git add data/naver/latest.json
git commit -q -m "naver scan $(Get-Date -Format 'yyyy-MM-dd HH:mm'): $status, $($all.Count) items"
for ($i = 0; $i -lt 3; $i++) { git push -q origin HEAD:house; if ($LASTEXITCODE -eq 0) { break }; git pull -q --rebase origin house; Start-Sleep 5 }
"naver: $status, $($all.Count) items"
