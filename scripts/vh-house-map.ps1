# behavior-house.txt(0.25초 걸음 기록, Core WalkTrace.Line 꼴) → 층별 위에서 본 그림 PNG.
# 쓰기: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vh-house-map.ps1 [-In <txt>] [-Out <png>]
# 기본 In = 게임 plugins\ValheimHelper\behavior-house.txt, Out = docs/valheim/house/<날짜>-<회차>.png
# 층 = 높이(y)를 정렬해 2m 넘게 벌어지는 곳에서 나눔. 색: 서기 회색·걷기 초록·뛰기 파랑, 벽 0.3m 안 빨간 점, 문 열림 주황 점,
# 순간이동(한 칸 3m 넘게) 보라 점선, 시작 ○ 끝 ■. 격자 1m(5m마다 진하게). 기준값은 Core WalkTrace와 같게.
param(
  [string]$In = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\plugins\ValheimHelper\behavior-house.txt",
  [string]$Out = ""
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
if (-not $Out) {
  $n = (Select-String -Path "$root\docs\valheim\status.txt" -Pattern '^회차:\s*(\d+)' -Encoding UTF8 | Select-Object -First 1).Matches[0].Groups[1].Value
  $Out = "$root\docs\valheim\house\$(Get-Date -Format 'yyyy-MM-dd')-$n.png"
}
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$inv = [Globalization.CultureInfo]::InvariantCulture
$head = @(); $s = @(); $k = 0   # $k = 단계(머리 "… 줄 N"마다 하나 — 단계 사이는 잇지 않음)
foreach ($l in [IO.File]::ReadAllLines($In, [Text.Encoding]::UTF8)) {
  if ($l.StartsWith('#')) { if ($l -match ' 줄 \d+$') { $k++ }; if (-not $l.StartsWith('# t ')) { $head += $l.TrimStart('#', ' ') }; continue }
  $p = $l.Split(' ')
  if ($p.Length -lt 10) { continue }
  $s += [pscustomobject]@{ X = [double]::Parse($p[1], $inv); Y = [double]::Parse($p[2], $inv); Z = [double]::Parse($p[3], $inv)
    V = [double]::Parse($p[4], $inv); Wall = [double]::Parse($p[6], $inv); Door = [int]$p[7]; K = $k }
}
if ($s.Count -lt 2) { Write-Output "줄 없음: $In"; exit 1 }

# 층 나누기
$ys = $s | ForEach-Object { $_.Y } | Sort-Object
$cuts = @(); for ($i = 1; $i -lt $ys.Count; $i++) { if ($ys[$i] - $ys[$i - 1] -gt 2) { $cuts += ($ys[$i] + $ys[$i - 1]) / 2 } }
function FloorOf($y) { $f = 0; foreach ($c in $cuts) { if ($y -gt $c) { $f++ } }; $f }
$floors = $cuts.Count + 1

$minX = ($s | Measure-Object X -Minimum).Minimum - 2; $maxX = ($s | Measure-Object X -Maximum).Maximum + 2
$minZ = ($s | Measure-Object Z -Minimum).Minimum - 2; $maxZ = ($s | Measure-Object Z -Maximum).Maximum + 2
$panel = 560; $scale = [Math]::Min(($panel - 20) / ($maxX - $minX), ($panel - 20) / ($maxZ - $minZ))
$top = 30 + 16 * $head.Count
$bmp = New-Object Drawing.Bitmap ([Math]::Max($panel * $floors, 980)), ($panel + $top + 24)
$g = [Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::White)
$font = New-Object Drawing.Font 'Malgun Gothic', 9
$y0 = 8; foreach ($h in $head) { $g.DrawString($h, $font, [Drawing.Brushes]::Black, 6, $y0); $y0 += 16 }
$g.DrawString('서기 회색 · 걷기 초록 · 뛰기 파랑 · 벽 0.3m 빨강 · 문 열림 주황 · 순간이동 보라 점선 · 시작 ○ 끝 ■ · 격자 1m(위 = 북)', $font, [Drawing.Brushes]::DimGray, 6, $panel + $top + 4)
function PX($f, $x) { $f * $panel + 10 + ($x - $minX) * $scale }
function PZ($z) { $top + 10 + ($maxZ - $z) * $scale }   # 위 = +z(북)

$thin = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(235, 235, 235)), 1
$thick = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(200, 200, 200)), 1
for ($f = 0; $f -lt $floors; $f++) {
  $lo = if ($f -eq 0) { $ys[0] } else { $cuts[$f - 1] }; $hi = if ($f -eq $floors - 1) { $ys[-1] } else { $cuts[$f] }
  $g.DrawString(("{0}층 (높이 {1:0.0}~{2:0.0})" -f ($f + 1), $lo, $hi), $font, [Drawing.Brushes]::Black, $f * $panel + 10, $top - 14)
  for ($x = [Math]::Ceiling($minX); $x -le $maxX; $x++) { $pen = if ($x % 5 -eq 0) { $thick } else { $thin }; $g.DrawLine($pen, (PX $f $x), (PZ $maxZ), (PX $f $x), (PZ $minZ)) }
  for ($z = [Math]::Ceiling($minZ); $z -le $maxZ; $z++) { $pen = if ($z % 5 -eq 0) { $thick } else { $thin }; $g.DrawLine($pen, (PX $f $minX), (PZ $z), (PX $f $maxX), (PZ $z)) }
  if ($f -gt 0) { $g.DrawLine([Drawing.Pens]::Black, $f * $panel, $top, $f * $panel, $top + $panel) }
}
$penOf = @{ 0 = (New-Object Drawing.Pen ([Drawing.Color]::Gray), 2); 1 = (New-Object Drawing.Pen ([Drawing.Color]::ForestGreen), 2); 2 = (New-Object Drawing.Pen ([Drawing.Color]::RoyalBlue), 2) }
$tele = New-Object Drawing.Pen ([Drawing.Color]::Magenta), 2; $tele.DashStyle = 'Dash'
for ($i = 1; $i -lt $s.Count; $i++) {
  $a = $s[$i - 1]; $b = $s[$i]; $fa = FloorOf $a.Y; $fb = FloorOf $b.Y
  if ($a.K -ne $b.K) { continue }   # 단계 바뀜 — 시험이 옮긴 것이라 잇지 않음
  $step = [Math]::Sqrt(($b.X - $a.X) * ($b.X - $a.X) + ($b.Z - $a.Z) * ($b.Z - $a.Z) + ($b.Y - $a.Y) * ($b.Y - $a.Y))
  if ($step -gt 3) { $g.DrawLine($tele, (PX $fa $a.X), (PZ $a.Z), (PX $fb $b.X), (PZ $b.Z)); continue }
  $k = if ($b.V -lt 0.3) { 0 } elseif ($b.V -lt 2.5) { 1 } else { 2 }
  $g.DrawLine($penOf[$k], (PX $fb $a.X), (PZ $a.Z), (PX $fb $b.X), (PZ $b.Z))
}
foreach ($p in $s) {
  $f = FloorOf $p.Y; $x = PX $f $p.X; $z = PZ $p.Z
  if ($p.Wall -le 0.3) { $g.FillEllipse([Drawing.Brushes]::Red, $x - 3, $z - 3, 6, 6) }
  if ($p.Door -ne 0) { $g.FillEllipse([Drawing.Brushes]::Orange, $x - 2, $z - 2, 4, 4) }
}
$a = $s[0]; $b = $s[-1]
$g.DrawEllipse((New-Object Drawing.Pen ([Drawing.Color]::Black), 2), (PX (FloorOf $a.Y) $a.X) - 5, (PZ $a.Z) - 5, 10, 10)
$g.FillRectangle([Drawing.Brushes]::Black, (PX (FloorOf $b.Y) $b.X) - 4, (PZ $b.Z) - 4, 8, 8)
$bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
Write-Output "그림 $Out (층 $floors, 줄 $($s.Count))"
