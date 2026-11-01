# Korean.csv 고친 사본 만들기: 원본(참조용)은 그대로, 줄 ID마다 한국어 칸만 바꿔 새 파일로 쓴다.
param([string]$Src = 'C:\Users\temp\Downloads\NecromancerKR-1.0-full\NecromancerKR-1.0\Korean.csv',
      [string]$Out = 'C:\vh\necro-kr\Korean-fixed.csv')
$raw = [IO.File]::ReadAllText($Src)
$i = 0; for ($j = 0; $j -lt 3; $j++) { $i = $raw.IndexOf("`n", $i) + 1 }
$rows = $raw.Substring($i) | ConvertFrom-Csv
$byId = @{}; foreach ($r in $rows) { $byId[$r.ID] = $r }

# ID별 (찾을 말, 바꿀 말). 같은 줄에 여러 개 가능.
$fix = [ordered]@{
  '0x01000000000137BF' = @(,@('시종경비병', '도시 경비병'))
  '0x010000000002BCF6' = @(,@('시종경비대', '도시 경비대'))
  '0x010000000003028D' = @(,@('시종들', '경비병들'))
  '0x010000000003029B' = @(,@('시종들', '경비병들'))
  '0x01000000000302D7' = @(,@('시종들', '경비병들'))
  '0x0100000000030311' = @(,@('시종들', '경비병들'))
  '0x010000000003AA19' = @(,@('시종대', '도시 경비대'))
  '0x0100000000025870' = @(,@('시종', '재상'))
  '0x01000000000258C9' = @(,@('시종', '재상'))
  '0x01000000000258D7' = @(,@('시종', '재상'))
  '0x0100000000015A63' = @(,@('크라운다', '크라운이다'))
  '0x010000000002A379' = @(,@('크라운다', '크라운이다'))
  '0x0100000000010B8F' = @(,@('저녁에 </align> 장례식이 열린다.<align=left></align>', '저녁에 장례식이 열린다.</align><br><align=left></align>'))
}
function Q($s) { '"' + $s.Replace('"', '""') + '"' }   # CSV 칸 꼴
$n = 0; $miss = @()
foreach ($id in $fix.Keys) {
  $r = $byId[$id]; if (-not $r) { $miss += "$id(없음)"; continue }
  $old = $r.Korean; $new = $old
  foreach ($p in $fix[$id]) { if ($new.Contains($p[0])) { $new = $new.Replace($p[0], $p[1]) } else { $miss += "$id($($p[0]))" } }
  if ($new -eq $old) { continue }
  # 원문에서 그 줄의 한국어 칸을 찾아 바꾼다(따옴표 있는/없는 꼴 둘 다)
  $line0 = $raw.IndexOf($id + ','); if ($line0 -lt 0) { $miss += "$id(줄)"; continue }
  $cands = @((Q $old), $old)
  $done = $false
  foreach ($c in $cands) { $k = $raw.IndexOf($c, $line0); if ($k -gt 0) { $raw = $raw.Substring(0, $k) + (Q $new) + $raw.Substring($k + $c.Length); $done = $true; break } }
  if ($done) { $n++ } else { $miss += "$id(칸)" }
}
# 빈 기울임·굵게 태그(화면엔 안 보임) 정리
$before = $raw.Length; $raw = $raw.Replace('<i></i>', '').Replace('<b></b>', ''); $tags = ($before - $raw.Length) / 7
[IO.File]::WriteAllText($Out, $raw, (New-Object Text.UTF8Encoding($false)))
"고친 줄 $n, 빈 태그 지움 $tags, 못 찾음: $($miss -join ', ')"
