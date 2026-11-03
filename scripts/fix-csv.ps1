# 게임용 Korean.csv 만들기: translation/template.csv(줄 ID·영어 원문·단락 틀만, 한국어 없음) 위에
# translation/retrans/*.tsv(ID<TAB>문장, 단락 " / ")를 파일 이름순으로 덮는다. 같은 ID는 뒤 파일이 이긴다. 단락 수가 다르면 그 줄은 건너뛴다.
param([string]$Src = (Join-Path $PSScriptRoot '..\translation\template.csv'),
      [string]$Out = (Join-Path $PSScriptRoot '..\Korean.csv'),
      [string]$Over = (Join-Path $PSScriptRoot '..\translation\retrans'))
$raw = [IO.File]::ReadAllText($Src)
$i = 0; for ($j = 0; $j -lt 3; $j++) { $i = $raw.IndexOf("`n", $i) + 1 }
$head = $raw.Substring(0, $i)
$rows = @($raw.Substring($i) | ConvertFrom-Csv)
$byId = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)   # [master]·[Master]처럼 대소문자만 다른 ID가 있음
foreach ($r in $rows) { $byId[$r.ID] = $r }

$names = [string[]]@(Get-ChildItem $Over -Filter *.tsv | ForEach-Object { $_.Name })
[Array]::Sort($names, [StringComparer]::Ordinal)
$ov = 0; $skip = @()
foreach ($nm in $names) {
  foreach ($ln in [IO.File]::ReadAllLines((Join-Path $Over $nm))) {
    if ($ln.StartsWith('#') -or -not $ln.Contains("`t")) { continue }
    $id, $txt = $ln.Split("`t", 2); $r = $null; if (-not $byId.TryGetValue($id, [ref]$r)) { continue }
    $paras = @($r.Korean -split '<br>')
    $parts = @($txt -split '\s/(?:\s|$)')
    if ($parts.Count -ne $paras.Count) { $skip += "$id(단락)"; continue }
    # 틀의 단락에 <align=left>가 있던 것만 감싼다(메뉴 단추는 가운데 정렬이라 감싸면 글이 왼쪽으로 밀림)
    $newParas = for ($k = 0; $k -lt $parts.Count; $k++) { $t = $parts[$k].Trim(); if ($paras[$k] -match '<align=left>') { "<align=left>$t</align>" } else { $t } }
    $r.Korean = @($newParas) -join '<br>'; $ov++
  }
}
foreach ($r in $rows) {
  if (-not $r.Korean) { continue }
  # 번역문에 남은 영어 "M'[title]"(My Lord 줄임) → "[title]"
  if ($r.Korean.Contains("'[title]")) { $r.Korean = [regex]::Replace($r.Korean, "[Mm]'\[title\]", '[title]') }
  # [title]은 "도련님/아가씨"라 뒤에 "님"이 붙으면 "도련님님"이 됨 → 지움
  if ($r.Korean.Contains('[title]님')) { $r.Korean = $r.Korean -replace '\[title\]님', '[title]' }
  # 빈 단락 틀만 남은 칸(번역 없음)은 비운다
  if (($r.Korean -replace '<br>|</?align[^>]*>', '') -eq '') { $r.Korean = '' }
}
function Q($s) { if ($null -eq $s) { return '' }; if ($s.IndexOfAny([char[]]@(',', '"', "`n", "`r")) -ge 0) { return '"' + $s.Replace('"', '""') + '"' }; $s }
$sb = New-Object Text.StringBuilder ($raw.Length * 3); [void]$sb.Append($head).Append("ID,EN,Korean`n")
foreach ($r in $rows) { [void]$sb.Append((Q $r.ID)).Append(',').Append((Q $r.EN)).Append(',').Append((Q $r.Korean)).Append("`n") }
$csv = $sb.ToString()
# 번역 도구가 남긴 자리표시(⟨1⟩, 〈1⟩, <<1>>)·낱개 ⟨ ⟩·깨진 자모 — 화면에 네모로 나옴
$mk = [regex]::Matches($csv, '[⟨〈]\d+⟩|<<\d+>>|[⟨⟩]|ᄀ+ ?').Count
$csv = [regex]::Replace($csv, '[⟨〈]\d+⟩|<<\d+>>|[⟨⟩]|ᄀ+ ?', '')
$before = $csv.Length; $csv = $csv.Replace('<i></i>', '').Replace('<b></b>', ''); $tags = ($before - $csv.Length) / 7
[IO.File]::WriteAllText($Out, $csv, (New-Object Text.UTF8Encoding($false)))
"다시 옮긴 줄 $ov, 자리표시 지움 $mk, 빈 태그 지움 $tags, 건너뜀: $($skip.Count) $($skip -join ', ')"
