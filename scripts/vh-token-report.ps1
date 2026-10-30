# 토큰 측정(사용자 10-27): Claude Code가 PC에 남기는 대화 기록(.claude*\projects\*\*.jsonl)에서 회차마다
# 턴별 대화 크기와, 도구 결과 하나가 대화를 얼마나 키워 그 뒤 턴마다 다시 읽혔는지(누적 = 늘어난 양 × 남은 턴 수)를 센다.
# 모델 호출 없음, 읽기만. 결과는 docs/valheim/token-report.md(공개 저장소 — 명령·경로는 앞부분만, 사용자 폴더는 %USERPROFILE%로).
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-token-report.ps1 [-Days 7] [-ProjectLike '*gojohnlee*']
param(
    [int]$Days = 7,
    [string]$ProjectLike = '*gojohnlee*',
    [string[]]$Roots = @((Join-Path $env:USERPROFILE '.claude\projects'), (Join-Path $env:USERPROFILE '.claude-acct2\projects')),
    [string]$Records = (Join-Path $env:USERPROFILE 'vh-loop\usage-records'),
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\valheim\token-report.md'),
    [int]$Top = 15,
    # 루프 모델 Opus 5.5(사용자 10-27) API 가격 배수, 일반 입력 = 1. 구독 한도가 이 비율인지는 모름.
    [double]$ReadX = 0.05, [double]$WriteX = 1.25, [double]$OutX = 5
)
$ErrorActionPreference = 'Stop'
$home0 = $env:USERPROFILE
function Clean([string]$t, [int]$n = 60) {
    if (-not $t) { return '' }
    if ($home0) { $t = $t.Replace($home0, '%USERPROFILE%') }
    $t = ($t -replace '\s+', ' ').Trim().Replace('|', '/')
    if ($t.Length -gt $n) { $t = $t.Substring(0, $n) + '…' }
    $t
}
function Len($c) {
    if ($null -eq $c) { return 0 }
    if ($c -is [string]) { return $c.Length }
    $n = 0; foreach ($x in @($c)) { if ($x.text) { $n += $x.text.Length } elseif ($x -is [string]) { $n += $x.Length } }; $n
}
# 도구 호출 하나를 묶음 이름으로: Bash는 명령 앞부분, Read·Edit·Grep은 파일(경로 끝 두 칸).
function Key($name, $in) {
    switch ($name) {
        { $_ -in @('Bash','PowerShell') } { $c = "$($in.command)" -replace '^\s*((cd|export) [^;&]+(;|&&)\s*)+', ''; return "$name " + (Clean $c 45) }
        { $_ -in @('Read','Edit','Write') } { $p = "$($in.file_path)" -split '[\\/]'; return "$name " + (($p | Select-Object -Last 2) -join '/') }
        'Grep' { return "Grep " + (Clean "$($in.pattern) @ $(("$($in.path)" -split '[\\/]' | Select-Object -Last 1))" 45) }
        'Agent' { return "Agent " + (Clean "$($in.subagent_type) $($in.description)" 45) }
        default { return "$name" }
    }
}
function Read-Session($file) {
    $calls = New-Object System.Collections.ArrayList; $byId = @{}; $results = @{}; $first = $null; $sidechain = $false
    foreach ($line in [IO.File]::ReadLines($file)) {
        if (-not $line) { continue }
        try { $e = $line | ConvertFrom-Json } catch { continue }
        if (-not $first -and $e.timestamp) { $first = [datetime]$e.timestamp }
        if ($e.isSidechain) { $sidechain = $true }
        $m = $e.message
        if ($e.type -eq 'assistant' -and $m.usage) {
            # 내용 블록마다 줄이 따로지만 같은 message.id = 같은 API 호출 하나
            $id = if ($m.id) { $m.id } else { "$($calls.Count)" }
            if (-not $byId.ContainsKey($id)) {
                $u = $m.usage
                $c = [pscustomobject]@{ ctx = [double]$u.input_tokens + [double]$u.cache_read_input_tokens + [double]$u.cache_creation_input_tokens
                    inp = [double]$u.input_tokens; read = [double]$u.cache_read_input_tokens; write = [double]$u.cache_creation_input_tokens; out = [double]$u.output_tokens; tools = New-Object System.Collections.ArrayList }
                $byId[$id] = $c; [void]$calls.Add($c)
            }
            foreach ($b in @($m.content)) { if ($b.type -eq 'tool_use') { [void]$byId[$id].tools.Add([pscustomobject]@{ id = $b.id; key = (Key $b.name $b.input) }) } }
        }
        elseif ($e.type -eq 'user' -and $m.content -and -not ($m.content -is [string])) {
            foreach ($b in @($m.content)) { if ($b.type -eq 'tool_result') { $results[$b.tool_use_id] = Len $b.content } }
        }
    }
    [pscustomobject]@{ file = $file; first = $first; sidechain = $sidechain; calls = $calls; results = $results }
}
# 호출 i와 i+1 사이에 늘어난 대화(= i의 출력 + 도구 결과 + 시스템 알림)를 i의 도구들에 결과 길이 비율로 나눔.
# 누적 = 늘어난 양 × (그 뒤 호출 수): 그만큼의 턴이 그것을 다시 읽었다.
function Measure-Session($s) {
    $n = $s.calls.Count; $items = @()
    for ($i = 0; $i -lt $n - 1; $i++) {
        $c = $s.calls[$i]; $d = [math]::Max(0, $s.calls[$i + 1].ctx - $c.ctx); $later = $n - $i - 1
        $tools = @($c.tools); $tot = 0; foreach ($t in $tools) { $tot += [double]$s.results[$t.id] }
        if (-not $tools.Count) { $items += [pscustomobject]@{ key = '(도구 없음: 출력·알림)'; grow = $d; cost = $d * $later; turn = $i + 1 }; continue }
        foreach ($t in $tools) {
            $share = if ($tot -gt 0) { [double]$s.results[$t.id] / $tot } else { 1.0 / $tools.Count }
            $items += [pscustomobject]@{ key = $t.key; grow = $d * $share; cost = $d * $share * $later; turn = $i + 1 }
        }
    }
    $items
}
function K($x) { if ($x -ge 10000) { '{0:N1}만' -f ($x / 10000) } else { '{0:N0}' -f $x } }

$since = (Get-Date).AddDays(-$Days)
$recs = @(Get-ChildItem -LiteralPath $Records -Filter *.json -ErrorAction SilentlyContinue | ForEach-Object { try { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { } })
$files = @(foreach ($r in $Roots) { Get-ChildItem -LiteralPath $r -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like $ProjectLike } |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter *.jsonl -ErrorAction SilentlyContinue } | Where-Object { $_.LastWriteTime -ge $since } })
$sessions = @(foreach ($f in $files) { $s = Read-Session $f.FullName; if ($s.calls.Count) { $s } }) | Sort-Object first
$o = @("# 토큰 측정 보고", '', "만든 때 $((Get-Date).ToString('yyyy-MM-dd HH:mm')) · 최근 ${Days}일 · 세션 $($sessions.Count)개(대화 기록 $($files.Count)개) · scripts/vh-token-report.ps1", '',
    ('읽는 법: **대화 크기** = 그 턴에 모델이 다시 읽은 양(입력+캐시 읽기+캐시 쓰기). **누적** = 도구 결과 하나가 늘린 양 × 그 뒤 턴 수 — 그 출력 때문에 회차 전체에서 더 읽은 양. 토큰은 Claude가 보고한 실측, 도구별 나눔만 결과 글자 수 비율(추정). **입력값 환산** = Opus 5.5 API 가격 배수(일반 입력 1, 캐시 읽기 ' + $ReadX + ', 쓰기 ' + $WriteX + ', 출력 ' + $OutX + ')로 더한 것 — 구독 한도가 같은 비율인지는 모름. 출력에는 생각(effort) 토큰도 들어감.'), '',
    '## 회차별', '', '| 시작 | 회차 | 종류 | 턴 | 첫 턴 크기 | 5턴째 크기 | 마지막 크기 | 다시 읽은 합(캐시 읽기) | 캐시 쓰기 | 출력 | 입력값 환산(읽기·쓰기·출력 몫) |', '|---|---|---|---|---|---|---|---|---|---|---|')
$all = @(); $agg = @{}
foreach ($s in $sessions) {
    $rec = $recs | Where-Object { $_.started -and $s.first -and [math]::Abs(([datetime]$_.started - $s.first).TotalMinutes) -le 5 } | Select-Object -First 1
    $kind = if ($s.sidechain) { '서브에이전트' } elseif ($rec) { "$($rec.kind)/$($rec.mode)" } else { '-' }
    $c = $s.calls; $n = $c.Count
    $sumIn = 0; $sumRead = 0; $sumWrite = 0; $sumOut = 0; foreach ($x in $c) { $sumIn += $x.inp; $sumRead += $x.read; $sumWrite += $x.write; $sumOut += $x.out }
    $wR = $sumRead * $ReadX; $wW = $sumWrite * $WriteX; $wO = $sumOut * $OutX; $wT = $sumIn + $wR + $wW + $wO
    $mix = if ($wT) { '{0:N0}·{1:N0}·{2:N0}%' -f (100 * $wR / $wT), (100 * $wW / $wT), (100 * $wO / $wT) } else { '-' }
    $o += "| $(if ($s.first) { $s.first.ToLocalTime().ToString('MM-dd HH:mm') }) | $(if ($rec) { $rec.round } else { '-' }) | $kind | $n | $(K $c[0].ctx) | $(if ($n -ge 5) { K $c[4].ctx } else { '-' }) | $(K $c[$n-1].ctx) | $(K $sumRead) | $(K $sumWrite) | $(K $sumOut) | $(K $wT) ($mix) |"
    foreach ($it in @(Measure-Session $s)) {
        $all += [pscustomobject]@{ when = $(if ($s.first) { $s.first.ToLocalTime().ToString('MM-dd HH:mm') }); round = $(if ($rec) { $rec.round } else { '-' }); key = $it.key; grow = $it.grow; cost = $it.cost; turn = $it.turn }
        $g = if ($it.key -like '(*') { $it.key } else { ($it.key -split ' ')[0] }; if ($it.key -match '^(Bash|PowerShell) (\S+)') { $g = "$($Matches[1]) $($Matches[2])" }
        if (-not $agg.ContainsKey($g)) { $agg[$g] = [pscustomobject]@{ n = 0; grow = 0.0; cost = 0.0 } }
        $agg[$g].n++; $agg[$g].grow += $it.grow; $agg[$g].cost += $it.cost
    }
}
$total = 0; foreach ($x in $all) { $total += $x.cost }
$o += ''; $o += "## 무엇이 대화를 키웠나(종류별 합, 누적 큰 순)"; $o += ''; $o += '| 종류 | 호출 수 | 늘린 양 합 | 누적 | 누적 비율 |'; $o += '|---|---|---|---|---|'
foreach ($k in ($agg.Keys | Sort-Object { $agg[$_].cost } -Descending | Select-Object -First $Top)) {
    $o += "| $k | $($agg[$k].n) | $(K $agg[$k].grow) | $(K $agg[$k].cost) | $(if ($total) { '{0:P0}' -f ($agg[$k].cost / $total) }) |"
}
$o += ''; $o += "## 비싼 호출 ${Top}개(누적 큰 순)"; $o += ''; $o += '| 시작 | 회차 | 턴 | 호출 | 늘린 양 | 누적 |'; $o += '|---|---|---|---|---|---|'
foreach ($x in ($all | Sort-Object cost -Descending | Select-Object -First $Top)) { $o += "| $($x.when) | $($x.round) | $($x.turn) | $($x.key) | $(K $x.grow) | $(K $x.cost) |" }
[IO.Directory]::CreateDirectory((Split-Path $Out -Parent)) | Out-Null
[IO.File]::WriteAllText($Out, ($o -join "`n") + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Output "token report: $($sessions.Count) sessions -> $Out"
