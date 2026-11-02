# Korean.csv 고친 사본 만들기: 원본(참조용)은 그대로, 줄 ID마다 한국어 칸만 바꿔 새 파일로 쓴다.
param([string]$Src = 'C:\Users\temp\Downloads\NecromancerKR-1.0-full\NecromancerKR-1.0\Korean.csv',
      [string]$Out = 'C:\vh\necro-kr\Korean-fixed.csv',
      [string]$Over = 'C:\vh\necro-kr\retrans')
$raw = [IO.File]::ReadAllText($Src)
$i = 0; for ($j = 0; $j -lt 3; $j++) { $i = $raw.IndexOf("`n", $i) + 1 }
$rows = $raw.Substring($i) | ConvertFrom-Csv
$byId = @{}; foreach ($r in $rows) { $byId[$r.ID] = $r }
$orig = @{}   # 바뀐 줄의 원래 한국어 칸(맨 끝에 원문에서 한 번에 바꾼다)
function SetKo($r, $new) { if (-not $orig.ContainsKey($r.ID)) { $orig[$r.ID] = $r.Korean }; $r.Korean = $new }

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
  '0x0100000000023E51' = @(,@('제가 노력했어요, 주인님. 그녀는 꼭 당신과 이야기해야 한다고 고집했거든요.[title]<br>', '돌려보내려 했습니다, [title]. 꼭 직접 뵈어야 한다고 막무가내였습니다.'))
  '0x0100000000011E1B' = @(,@('시작했어.[he]</align><br></align>그리고 네 머리색도 제대로 돌아왔구나.<align=left>', '시작했어.</align><br><align=left>그리고 네 머리색도 제대로 돌아왔구나.</align>'))
  '0x010000000003AA0A' =@(,@('알고 있느냐, </align>?', '알고 있느냐?</align>'))
  '0x0100000000010B8F' = @(,@('저녁에 </align> 장례식이 열린다.<align=left></align>', '저녁에 장례식이 열린다.</align><br><align=left></align>'))
}
$n = 0; $miss = @()
foreach ($id in $fix.Keys) {
  $r = $byId[$id]; if (-not $r) { $miss += "$id(없음)"; continue }
  $new = $r.Korean
  foreach ($p in $fix[$id]) { if ($new.Contains($p[0])) { $new = $new.Replace($p[0], $p[1]) } else { $miss += "$id($($p[0]))" } }
  if ($new -ne $r.Korean) { SetKo $r $new; $n++ }
}
# 다시 옮긴 대사(retrans\*.tsv: ID<TAB>문장, 단락은 " / ")로 한국어 칸을 통째로 바꾼다. 단락 수(<br>로 나뉜 수)가 같을 때만.
$ov = 0; $skip = @()
foreach ($f in Get-ChildItem $Over -Filter *.tsv -ErrorAction SilentlyContinue) {
  foreach ($ln in [IO.File]::ReadAllLines($f.FullName)) {
    if ($ln.StartsWith('#') -or -not $ln.Contains("`t")) { continue }
    $id, $txt = $ln.Split("`t", 2); $r = $byId[$id]; if (-not $r) { continue }
    $paras = @($r.Korean -split '<br>')
    $parts = @($txt -split '\s/(?:\s|$)')
    if ($parts.Count -ne $paras.Count) { $skip += "$id(단락)"; continue }
    # 원래 단락에 <align=left>가 있던 것만 감싼다(메뉴 단추는 가운데 정렬이라 감싸면 글이 왼쪽으로 밀림)
    $newParas = for ($i = 0; $i -lt $parts.Count; $i++) { $t = $parts[$i].Trim(); if ($paras[$i] -match '<align=left>') { "<align=left>$t</align>" } else { $t } }
    SetKo $r (@($newParas) -join '<br>'); $ov++
  }
}
# 번역문에 남은 영어 "M'[title]"(My Lord 줄임) → "[title]"만(한국어 칸만)
foreach ($r in $rows) { if ($r.Korean -and $r.Korean.Contains("'[title]")) { $k = [regex]::Replace($r.Korean, "[Mm]'\[title\]", '[title]'); if ($k -ne $r.Korean) { SetKo $r $k } } }
# [title]은 "도련님/아가씨"라 뒤에 "님"이 붙으면 "도련님님"이 됨 → 지움
foreach ($r in $rows) { if ($r.Korean -and $r.Korean.Contains('[title]님')) { SetKo $r ($r.Korean -replace '\[title\]님', '[title]') } }
# 바뀐 줄을 원문 위치 순으로 한 번에 바꾼다(따옴표 있는/없는 칸 꼴 둘 다)
function Q($s) { '"' + $s.Replace('"', '""') + '"' }
$Ord = [StringComparison]::Ordinal
$edits = foreach ($id in $orig.Keys) {
  $line0 = $raw.IndexOf("`n" + $id + ',', $Ord); if ($line0 -lt 0) { $miss += "$id(줄)"; continue }
  $o = $orig[$id]; $hit = $null
  foreach ($c in @((Q $o), $o)) { $k = $raw.IndexOf($c, $line0, $Ord); if ($k -gt 0) { $hit = [pscustomobject]@{ At = $k; Len = $c.Length; New = (Q $byId[$id].Korean) }; break } }
  if ($hit) { $hit } else { $miss += "$id(칸)" }
}
$sb = New-Object Text.StringBuilder ($raw.Length + 1000000); $pos = 0
foreach ($e in ($edits | Sort-Object At)) { [void]$sb.Append($raw, $pos, $e.At - $pos).Append($e.New); $pos = $e.At + $e.Len }
[void]$sb.Append($raw, $pos, $raw.Length - $pos); $raw = $sb.ToString()
# 번역 도구가 남긴 자리표시(⟨1⟩, 〈1⟩, <<1>>) — 화면에 네모로 나옴
$mk = [regex]::Matches($raw, '[⟨〈]\d+⟩|<<\d+>>').Count
$raw = [regex]::Replace($raw, '[⟨〈]\d+⟩|<<\d+>>', '')
# 남은 낱개 ⟨ ⟩ 와 깨진 자모(ᄀᄀᄀ…)도 네모로 나옴
$mk += [regex]::Matches($raw, '[⟨⟩]|ᄀ+ ?').Count
$raw = [regex]::Replace($raw, '[⟨⟩]|ᄀ+ ?', '')
# 빈 기울임·굵게 태그(화면엔 안 보임) 정리
$before = $raw.Length; $raw = $raw.Replace('<i></i>', '').Replace('<b></b>', ''); $tags = ($before - $raw.Length) / 7
[IO.File]::WriteAllText($Out, $raw, (New-Object Text.UTF8Encoding($false)))
"고친 줄 $n, 다시 옮긴 줄 $ov, 자리표시 지움 $mk, 빈 태그 지움 $tags, 못 찾음: $($miss -join ', '), 건너뜀: $($skip -join ', ')"
