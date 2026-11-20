# 영어 원본 JSON 틀에 번역 목록(ko/*.tsv)을 끼워 ko.json을 만든다.
# 사용: powershell -File build.ps1 <EN.json> <ko 폴더> <out ko.json> [게임버전]
# 번역 없는 줄은 영어로 남기고 개수를 알린다. 원문의 {0}·<태그> 개수가 번역과 다르면 경고.
param([string]$En, [string]$KoDir, [string]$Out, [string]$Version = '')
$enc = New-Object Text.UTF8Encoding $false
$j = [IO.File]::ReadAllText($En, [Text.Encoding]::UTF8) | ConvertFrom-Json
function Unesc([string]$s) {
  $r = New-Object Text.StringBuilder
  for ($i = 0; $i -lt $s.Length; $i++) {
    $ch = $s[$i]
    if ($ch -eq '\' -and $i + 1 -lt $s.Length) {
      $n = $s[$i + 1]; $i++
      switch ($n) { 'n' { [void]$r.Append("`n") } 't' { [void]$r.Append("`t") } '\' { [void]$r.Append('\') } default { [void]$r.Append('\').Append($n) } }
    } else { [void]$r.Append($ch) }
  }
  $r.ToString()
}
$tr = @{}
foreach ($f in Get-ChildItem $KoDir -Filter *.tsv | Sort-Object Name) {
  foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
    $t = $line.IndexOf("`t"); if ($t -lt 1) { continue }
    $tr[$line.Substring(0, $t)] = Unesc $line.Substring($t + 1)
  }
}
$script:used = 0; $script:miss = New-Object Collections.ArrayList; $script:warn = New-Object Collections.ArrayList
function Marks([string]$s) { ([regex]::Matches($s, '\{\d+\}|<[^<>]*>') | ForEach-Object { $_.Value } | Sort-Object) -join ' ' }
function Get-Tr([string]$path, [string]$orig) {
  if ([string]::IsNullOrEmpty($orig)) { return $orig }
  if ($tr.ContainsKey($path)) {
    $script:used++; $k = $tr[$path]
    if ((Marks $orig) -ne (Marks $k)) { [void]$script:warn.Add("$path  원문[$(Marks $orig)] 번역[$(Marks $k)]") }
    return $k
  }
  [void]$script:miss.Add($path); return $orig
}
$j.name = Get-Tr 'name' $j.name
$j.description = Get-Tr 'description' $j.description
if ($Version) { $j.version = $Version }
foreach ($sec in 'main', 'buildings', 'moodles', 'other') {
  foreach ($p in $j.$sec.PSObject.Properties) { $p.Value = Get-Tr "$sec.$($p.Name)" $p.Value }
}
for ($c = 0; $c -lt $j.character.Count; $c++) {
  foreach ($p in $j.character[$c].PSObject.Properties) {
    $arr = @($p.Value); for ($i = 0; $i -lt $arr.Count; $i++) { $arr[$i] = Get-Tr "character.$c.$($p.Name).$i" $arr[$i] }; $p.Value = $arr
  }
}
for ($g = 0; $g -lt $j.notes.Count; $g++) {
  $arr = @($j.notes[$g]); for ($i = 0; $i -lt $arr.Count; $i++) { $arr[$i].Item1 = Get-Tr "notes.$g.$i" $arr[$i].Item1 }
}
for ($i = 0; $i -lt $j.pdaNotes.Count; $i++) { $j.pdaNotes[$i].Item1 = Get-Tr "pdaNotes.$i" $j.pdaNotes[$i].Item1 }
foreach ($p in $j.pauseQuotes.PSObject.Properties) {
  $arr = @($p.Value); for ($i = 0; $i -lt $arr.Count; $i++) { $arr[$i] = Get-Tr "pauseQuotes.$($p.Name).$i" $arr[$i] }; $p.Value = $arr
}
$json = $j | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($Out, $json, $enc)
"번역 적용 $($script:used)줄, 영어로 남음 $($script:miss.Count)줄, 태그·자리표시자 불일치 $($script:warn.Count)줄"
$script:miss | Select-Object -First 15 | ForEach-Object { "  남음: $_" }
$script:warn | Select-Object -First 30 | ForEach-Object { "  경고: $_" }
