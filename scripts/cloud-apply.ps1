# Puts the cloud re-translation into the game: pull private refs (necromancer) -> copy translation\retrans\*.tsv
# to C:\vh\necro-kr\retrans -> fix-csv.ps1 rebuilds Korean-fixed.csv -> copy to the game's Languages\Korean.csv.
# ASCII only on purpose (Windows PowerShell 5.1 misreads BOM-less UTF-8). Run: scripts\cloud-apply.bat
$ErrorActionPreference = 'Stop'
$W = 'C:\vh\necro-kr'; $Refs = "$W\refs"
git -C $Refs checkout -q necromancer
git -C $Refs pull -q origin necromancer
New-Item -ItemType Directory -Force "$W\retrans" | Out-Null
Copy-Item "$Refs\translation\retrans\*.tsv" "$W\retrans\" -Force
& (Join-Path $PSScriptRoot 'fix-csv.ps1')
Copy-Item "$W\Korean-fixed.csv" "$env:USERPROFILE\AppData\LocalLow\Psychic Software\TheNecromancer\Languages\Korean.csv" -Force
'Done: cloud translation copied into the game.'
