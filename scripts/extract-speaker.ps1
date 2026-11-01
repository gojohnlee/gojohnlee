# Dump one speaker's lines (id|EN|KO, tags stripped except <i>, paragraphs " / ") for re-translation. Local files only (contain game text).
param([Parameter(Mandatory)][string]$Speaker, [string]$Name = $Speaker.ToLower(),
      [string]$Dump = 'C:\vh\necro-kr\dump\articy-dump.tsv', [string]$Csv = 'C:\vh\necro-kr\Korean-fixed.csv',
      [string]$OutDir = 'C:\vh\necro-kr\retrans')
New-Item -ItemType Directory -Force $OutDir | Out-Null
$ids = @{}; foreach ($l in [IO.File]::ReadAllLines($Dump)) { $c = $l.Split("`t"); if ($c.Count -gt 2 -and $c[2] -eq $Speaker) { $ids[$c[0]] = 1 } }
$raw = [IO.File]::ReadAllText($Csv); $i = 0; for ($j = 0; $j -lt 3; $j++) { $i = $raw.IndexOf("`n", $i) + 1 }
function S($s) { $s = [regex]::Replace($s, '<br>|\r?\n', ' / '); [regex]::Replace($s, '<(?!/?i>)[^>]*>', '').Trim() }
$out = New-Object Collections.Generic.List[string]
foreach ($r in ($raw.Substring($i) | ConvertFrom-Csv)) { if ($ids.ContainsKey($r.ID)) { $out.Add("$($r.ID)|$(S $r.EN)|$(S $r.Korean)") } }
[IO.File]::WriteAllLines("$OutDir\$Name-src.txt", $out, (New-Object Text.UTF8Encoding($false)))
"$Speaker : $($out.Count) lines -> $OutDir\$Name-src.txt"
