# 언어 JSON을 "경로<TAB>문장" 줄 목록으로 펼친다 (줄바꿈은 \n, 탭은 \t 로 적음).
# 사용: powershell -File flatten.ps1 <EN.json> <out.tsv>
param([string]$In, [string]$Out)
$j = [IO.File]::ReadAllText($In, [Text.Encoding]::UTF8) | ConvertFrom-Json
$sb = New-Object Text.StringBuilder
function Esc([string]$s) { $s.Replace('\', '\\').Replace("`r", '').Replace("`n", '\n').Replace("`t", '\t') }
function Add([string]$path, $val) { if ($val -is [string] -and $val.Length -gt 0) { [void]$sb.Append($path).Append("`t").Append((Esc $val)).Append("`n") } }
Add 'name' $j.name
Add 'description' $j.description
foreach ($sec in 'main', 'buildings', 'moodles', 'other') {
  foreach ($p in $j.$sec.PSObject.Properties) { Add "$sec.$($p.Name)" $p.Value }
}
for ($c = 0; $c -lt $j.character.Count; $c++) {
  foreach ($p in $j.character[$c].PSObject.Properties) {
    $arr = @($p.Value); for ($i = 0; $i -lt $arr.Count; $i++) { Add "character.$c.$($p.Name).$i" $arr[$i] }
  }
}
for ($g = 0; $g -lt $j.notes.Count; $g++) {
  $arr = @($j.notes[$g]); for ($i = 0; $i -lt $arr.Count; $i++) { Add "notes.$g.$i" $arr[$i].Item1 }
}
for ($i = 0; $i -lt $j.pdaNotes.Count; $i++) { Add "pdaNotes.$i" $j.pdaNotes[$i].Item1 }
foreach ($p in $j.pauseQuotes.PSObject.Properties) {
  $arr = @($p.Value); for ($i = 0; $i -lt $arr.Count; $i++) { Add "pauseQuotes.$($p.Name).$i" $arr[$i] }
}
[IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object Text.UTF8Encoding $false))
