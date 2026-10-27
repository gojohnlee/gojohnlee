# Shared by the stopped loop and the standalone verification harness. No side effects on import.
function Write-VhJson($Path, $Value) {
    $parent = Split-Path $Path -Parent
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $tmp = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllText($tmp, ($Value | ConvertTo-Json -Depth 30), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($tmp, $Path, [NullString]::Value) }
    else { [IO.File]::Move($tmp, $Path) }
}
function Get-VhHash($Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing evidence: $Path" }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
function Get-VhTextHash([string]$Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '') }
    finally { $sha.Dispose() }
}
function Invoke-VhGit($Repo, [string[]]$Arguments) {
    $result = @(& git -C $Repo @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" }
    $result
}
function Get-VhSource($Repo) {
    $head = (Invoke-VhGit $Repo @('rev-parse','HEAD') | Select-Object -Last 1)
    $paths = @(Invoke-VhGit $Repo @('-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard') | Sort-Object -Unique)
    $entries = foreach ($p in $paths) {
        $full = Join-Path $Repo $p
        if (Test-Path -LiteralPath $full -PathType Leaf) { "$p`t$(Get-VhHash $full)" } else { "$p`tMISSING" }
    }
    [pscustomobject]@{ commit=$head; hash=(Get-VhTextHash ($entries -join "`n")); files=@($entries) }
}
function Write-VhUsage($Directory, $ResultJson, $Round, $Account, $Kind, $Todo, $Started, $ExitCode, $RunId, $Mode='claude-only') {
    $r = $null; $parse = 'missing'
    if ($ResultJson) {
        try { $r = $ResultJson | ConvertFrom-Json; if ($r.type -ne 'result') { throw 'Not a result' }; $parse = 'present' }
        catch { $r = $null; $parse = 'invalid' }
    }
    $record = [pscustomobject][ordered]@{
        run_id=$RunId; started=$Started.ToString('o'); round=$Round; account=$Account; kind=$Kind; todo=$Todo
        input_tokens=$r.usage.input_tokens; output_tokens=$r.usage.output_tokens
        cache_read_tokens=$r.usage.cache_read_input_tokens; cache_write_tokens=$r.usage.cache_creation_input_tokens
        cost_usd=$r.total_cost_usd; turns=$r.num_turns; reported_ms=$r.duration_ms
        elapsed_minutes=[math]::Round(((Get-Date)-$Started).TotalMinutes,3)
        exit_code=$ExitCode; result=$parse; is_error=$r.is_error; mode=$Mode
    }
    # Each attempt has its own immutable record; retrying the same round never overwrites an attempt.
    $path = Join-Path $Directory "usage-records\$RunId.json"
    Write-VhJson $path $record
    # CSV is a replaceable index of authoritative JSON records, including missing-result attempts.
    $rows = @(Get-ChildItem (Join-Path $Directory 'usage-records') -Filter '*.json' | ForEach-Object {
        Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    } | Sort-Object started)
    # Fixed columns: the first record must not decide them (older records have no mode = claude-only).
    $columns = @('run_id','started','round','account','kind','mode','todo','input_tokens','output_tokens','cache_read_tokens','cache_write_tokens','cost_usd','turns','reported_ms','elapsed_minutes','exit_code','result','is_error')
    $csv = ($rows | Select-Object $columns | ConvertTo-Csv -NoTypeInformation) -join "`r`n"
    $target = Join-Path $Directory 'usage.csv'; $tmp = "$target.$RunId.tmp"
    [IO.File]::WriteAllText($tmp, $csv+"`r`n", (New-Object Text.UTF8Encoding($true)))
    if (Test-Path $target) { [IO.File]::Replace($tmp,$target,[NullString]::Value) } else { [IO.File]::Move($tmp,$target) }
    $record
}
function Read-VhDiag([string]$Text, [string[]]$Expected) {
    $sections = @(); $current = $null
    foreach ($line in ($Text -split '\r?\n')) {
        if ($line -match '^=== (\S+)') {
            $current = [pscustomobject]@{ name=$Matches[1]; verdict='MISSING'; evidence=@(); line=0 }
            $sections += $current
        }
        elseif ($current) {
            if ($line -match '^판정: (PASS|FAIL|환경 불량)(?:\s|$)') {
                $v = $Matches[1]
                if ($v -eq '환경 불량') { $current.verdict='ENVIRONMENT' }
                elseif ($current.verdict -ne 'FAIL') { $current.verdict=$v }
            }
            if ($line -match '^FAIL(?:\s|:|$)') { $current.verdict='FAIL' }
            if ($line -match '^FAIL|^판정:|^시간:|^시작 상태:|환경 불량|Exception') { $current.evidence += $line }
        }
    }
    $status = 'PASS'
    if ($sections.Count -ne $Expected.Count) { $status='MISSING' }
    for ($i=0; $i -lt [math]::Min($sections.Count,$Expected.Count); $i++) {
        if ($sections[$i].name -ne $Expected[$i]) { $status='MISSING' }
    }
    if ($status -eq 'PASS') {
        foreach ($s in $sections) {
            if ($s.verdict -eq 'MISSING') { $status='MISSING'; break }
            if ($s.verdict -eq 'ENVIRONMENT') { $status='ENVIRONMENT' }
            if ($s.verdict -eq 'FAIL' -and $status -eq 'PASS') { $status='FAIL' }
        }
    }
    [pscustomobject]@{ status=$status; sections=@($sections) }
}
function Test-VhEvidence($Evidence) {
    if (@($Evidence).Count -eq 0) { return $false }
    foreach ($e in $Evidence) {
        if (-not (Test-Path -LiteralPath $e.path -PathType Leaf)) { return $false }
        if ((Get-VhHash $e.path) -cne $e.hash) { return $false }
    }
    $true
}
function Get-VhEvidence([string[]]$Paths) {
    foreach ($p in $Paths) { [pscustomobject]@{ path=$p; hash=(Get-VhHash $p) } }
}
function Get-VhLatestGroups($Task) {
    @($Task.groups | Group-Object index | ForEach-Object { $_.Group | Select-Object -Last 1 } | Sort-Object index)
}
function Get-VhResumeDecision($Record) {
    if (-not $Record) { return 'RUN' }
    if ($Record.status -eq 'RUNNING') {
        foreach ($p in @(Get-ChildItem -LiteralPath $Record.directory -Recurse -Filter run.json -ErrorAction SilentlyContinue)) {
            try { $r=Get-Content $p.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return 'REJECT' }
            if ($r.status -ne 'PASS') { return 'REJECT' }
        }
        return 'RESTART_GROUP'
    }
    if (-not (Test-VhEvidence $Record.evidence)) { return 'REJECT' }
    if ($Record.status -eq 'PASS') { return 'REUSE' }
    'REVIEW'
}
function Test-VhImplementationProcess($Repo) {
    $configured=Join-Path $env:USERPROFILE '.local\bin\claude.exe'
    # Claude Desktop and its UI helpers are unrelated to the configured loop worker.
    @(Get-CimInstance Win32_Process -Filter "Name='claude.exe'" | Where-Object {
        $_.ExecutablePath -ieq $configured -or $_.CommandLine -match 'vh-loop-prompt\.txt|vh-reply-prompt\.txt' -or $_.CommandLine -like "*$Repo*"
    }).Count -gt 0
}
function Invoke-VhProcess($File, [string]$Arguments, $WorkingDirectory, $Out, $Err, [int]$TimeoutSeconds=1800, [string]$InputText, [scriptblock]$OnWait) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=$File; $psi.Arguments=$Arguments; $psi.WorkingDirectory=$WorkingDirectory
    $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true
    $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    if ($InputText) { $psi.RedirectStandardInput=$true }
    $psi.StandardOutputEncoding=[Text.Encoding]::UTF8; $psi.StandardErrorEncoding=[Text.Encoding]::UTF8
    # .NET Framework writes Console.InputEncoding's preamble to redirected stdin at start; a UTF-8 (65001) console would prefix a BOM.
    $prevIn = $null
    if ($InputText) { try { $prevIn = [Console]::InputEncoding; [Console]::InputEncoding = New-Object Text.UTF8Encoding($false) } catch { $prevIn = $null } }
    try { $p = [Diagnostics.Process]::Start($psi) } finally { if ($prevIn) { try { [Console]::InputEncoding = $prevIn } catch { } } }
    Write-VhJson "$Out.process.json" @{ pid=$p.Id; started_ticks=$p.StartTime.ToUniversalTime().Ticks; executable=$File }
    # Persist bytes while the child runs, including before a parent interruption.
    $outStream=New-Object IO.FileStream($Out,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read,1,[IO.FileOptions]::WriteThrough)
    $errStream=New-Object IO.FileStream($Err,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read,1,[IO.FileOptions]::WriteThrough)
    try {
    $stdout = $p.StandardOutput.BaseStream.CopyToAsync($outStream); $stderr = $p.StandardError.BaseStream.CopyToAsync($errStream)
    if ($InputText) {
        # Windows PowerShell 5.1's StreamWriter uses the system code page; the child expects UTF-8.
        $bytes=[Text.Encoding]::UTF8.GetBytes($InputText)
        $p.StandardInput.BaseStream.Write($bytes,0,$bytes.Length)
        $p.StandardInput.BaseStream.Flush(); $p.StandardInput.Close()
    }
    # OnWait runs about once a minute while the child is alive (the loop's 15-minute backup and reply check).
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds); $timedOut = $false
    while ($true) {
        $left = [math]::Max(0, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if ($p.WaitForExit([int][math]::Min(60000, $left))) { break }
        if ([DateTime]::UtcNow -ge $deadline) { $timedOut = $true; break }
        if ($OnWait) { & $OnWait }
    }
    if ($timedOut) { & taskkill /PID $p.Id /T /F | Out-Null; [void]$p.WaitForExit(10000) }
    if (-not $stdout.Wait(10000) -or -not $stderr.Wait(10000)) { throw 'Child output pipe did not close; evidence incomplete' }
    [pscustomobject]@{ exit_code=$p.ExitCode; timed_out=$timedOut }
    } finally { $outStream.Dispose(); $errStream.Dispose(); $p.Dispose() }
}
function Assert-VhChildrenStopped($Directory) {
    foreach ($file in @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter '*.process.json')) {
        $record=Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $process=Get-Process -Id $record.pid -ErrorAction SilentlyContinue
        if ($process -and $process.StartTime.ToUniversalTime().Ticks -eq $record.started_ticks) { throw "Previous child is still running: $($record.pid). No duplicate execution started." }
    }
}
function Save-VhInterruptedEvidence($Directory,$Game) {
    $recovery=Join-Path $Directory ('interrupted-'+[guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($recovery) | Out-Null
    foreach ($relative in @('BepInEx\plugins\ValheimHelper\diag.txt','BepInEx\LogOutput.log')) {
        $file=Join-Path $Game $relative
        if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $recovery }
    }
    Write-VhJson (Join-Path $recovery 'recovery.json') @{ status='INTERRUPTED'; note='Recovered shared logs before retry; ownership/freshness not proven. Original attempt remains incomplete.'; at=(Get-Date).ToString('o') }
}
# Known harmless lines (exact form only). BepInEx prints this on every start on this PC (history: "BepInEx 기본 메시지라 무시").
$script:VhKnownBenignLog = @('^\[Error\s*:\s*BepInEx\] Unable to start Unity log writer\s*$')
function Test-VhKnownBenign([string]$Line) { foreach ($k in $script:VhKnownBenignLog) { if ($Line -match $k) { return $true } }; $false }
function Get-VhLogProblems([string]$Text) {
    @(($Text -split '\r?\n') | Where-Object { $_ -match '\[Error\s*:|Exception' -and -not (Test-VhKnownBenign $_) })
}
# Error lines grouped by identical text with first line number and count; known harmless lines are labelled, never dropped.
function Get-VhLogDigest([string]$Path, [int]$Max=60) {
    $groups = [ordered]@{}; $n = 0
    foreach ($l in [IO.File]::ReadAllLines($Path)) {
        $n++
        if ($l -notmatch '\[Error\s*:|Exception') { continue }
        $k = $l.Trim()
        if ($groups.Contains($k)) { $groups[$k].count++ } else { $groups[$k] = [pscustomobject]@{ first=$n; count=1 } }
    }
    $out = @(foreach ($k in $groups.Keys) { "line $($groups[$k].first) x$($groups[$k].count): $k$(if (Test-VhKnownBenign $k) { ' [known harmless BepInEx message]' })" })
    if ($out.Count -gt $Max) { $out = @($out[0..($Max-1)]) + "... $($out.Count-$Max) more distinct error lines; read the original log" }
    if (-not $out.Count) { $out = @('(no Error/Exception lines)') }
    $out -join "`n"
}
# Requirement "path: needle" -> the block starting at the first line containing needle
# (markdown heading: until the next heading of the same or higher level; otherwise: until the next line indented no deeper).
function Get-VhSection([string[]]$Lines, [string]$Needle, [int]$Max=120) {
    $i = 0
    while ($i -lt $Lines.Count -and $Lines[$i].IndexOf($Needle, [StringComparison]::Ordinal) -lt 0) { $i++ }
    if ($i -ge $Lines.Count) { throw "Requirement section not found: $Needle" }
    $out = @($Lines[$i])
    $heading = if ($Lines[$i] -match '^(#+)\s') { $Matches[1].Length } else { 0 }
    $indent = ($Lines[$i] -replace '^(\s*).*$', '$1').Length
    for ($j = $i + 1; $j -lt $Lines.Count; $j++) {
        $l = $Lines[$j]
        if ($heading) { if ($l -match '^(#+)\s' -and $Matches[1].Length -le $heading) { break } }
        elseif ($l.Trim() -and ($l -replace '^(\s*).*$', '$1').Length -le $indent) { break }
        if ($out.Count -ge $Max) { $out += "... (section cut at $Max lines; read the original)"; break }
        $out += $l
    }
    $out -join "`n"
}
function Get-VhRequirement($Repo, [string]$Spec) {
    $parts = $Spec -split ':', 2
    $relative = $parts[0].Trim()
    $full = [IO.Path]::GetFullPath((Join-Path $Repo $relative))
    if (-not $full.StartsWith($Repo.TrimEnd('\')+'\', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Missing/outside requirement: $relative" }
    $lines = [IO.File]::ReadAllLines($full, [Text.Encoding]::UTF8)
    if ($parts.Count -gt 1 -and $parts[1].Trim()) { return "REQUIREMENT: $relative / $($parts[1].Trim())`n" + (Get-VhSection $lines $parts[1].Trim()) }
    $text = $lines -join "`n"
    if ([Text.Encoding]::UTF8.GetByteCount($text) -gt 30000) { throw "Requirement file is large; name a section as 'path: heading text': $relative" }
    "REQUIREMENT FILE: $relative`n$text"
}
# Open Errors.txt entries: every heading line, plus full entries named in the plan or mentioning a planned test.
function Get-VhErrorsExcerpt([string[]]$Lines, [string[]]$Ids, [string[]]$Tests) {
    $entries = @(); $cur = $null
    foreach ($l in $Lines) {
        if ($l -match '^(E\d{3}) / ') { $cur = [pscustomobject]@{ id=$Matches[1]; lines=@() }; $entries += $cur }
        elseif ($l -match '^={10,}') { $cur = $null }
        if ($cur) { $cur.lines += $l }
    }
    $out = @('OPEN ERRORS (headings only; full entries below when related):') + @($entries | ForEach-Object { $_.lines[0] })
    foreach ($e in $entries) {
        $text = $e.lines -join "`n"
        $related = ($Ids -contains $e.id) -or @($Tests | Where-Object { $_ -and $text -match "\b$([regex]::Escape($_))\b" }).Count
        if ($related) { $out += ''; $out += $text }
    }
    $out -join "`n"
}
# Failing lines quoted verbatim as "path:line: text" — no model (user 10-27: GPT dropped). For each non-PASS section of diag.txt:
# FAIL/판정/환경 불량/Exception lines with one line before and after, up to $Max per section (no hit: the last $Max lines);
# plus the grouped Error/Exception lines of LogOutput.log when it has real problems. Picking lines is not judging the cause.
function Get-VhFailExcerpt($Groups, [int]$Max=15) {
    $o = @('# 실패 발췌(스크립트가 원문 그대로 옮김 — 원인 판단 아님)')
    foreach ($g in @($Groups | Where-Object { $_.status -ne 'PASS' })) {
        $o += "## 묶음 $($g.index): $($g.status) — $($g.directory)"
        $fail = @()
        foreach ($f in @(Get-ChildItem -LiteralPath $g.directory -Recurse -Filter run.json -ErrorAction SilentlyContinue)) {
            try { $r = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $o += "- run.json 못 읽음: $($f.FullName)"; continue }
            foreach ($s in @($r.sections | Where-Object { $_.verdict -ne 'PASS' })) { $fail += $s.name; $o += "- $($s.name): $($s.verdict)" }
        }
        foreach ($d in @(Get-ChildItem -LiteralPath $g.directory -Recurse -Filter diag.txt -ErrorAction SilentlyContinue)) {
            $lines = [IO.File]::ReadAllLines($d.FullName, [Text.Encoding]::UTF8); $sec = ''; $hit = [ordered]@{}
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^=== (\S+)') { $sec = $Matches[1]; continue }
                if (-not $sec -or ($fail.Count -and $fail -notcontains $sec) -or $lines[$i] -notmatch '^FAIL|^판정:|환경 불량|Exception') { continue }
                if (-not $hit.Contains($sec)) { $hit[$sec] = New-Object Collections.Generic.List[int] }
                foreach ($k in ($i - 1)..($i + 1)) { if ($k -ge 0 -and $k -lt $lines.Count -and -not $hit[$sec].Contains($k) -and $hit[$sec].Count -lt $Max) { $hit[$sec].Add($k) } }
            }
            foreach ($s in $hit.Keys) { $o += "### $s"; foreach ($k in $hit[$s]) { $o += "$($d.FullName):$($k + 1): $($lines[$k])" } }
            if (-not $hit.Count -and $lines.Count) {
                $o += "### $($d.FullName): 실패 줄 못 찾음 — 끝 $Max줄"
                for ($k = [math]::Max(0, $lines.Count - $Max); $k -lt $lines.Count; $k++) { $o += "$($d.FullName):$($k + 1): $($lines[$k])" }
            }
        }
        foreach ($l in @(Get-ChildItem -LiteralPath $g.directory -Recurse -Filter LogOutput.log -ErrorAction SilentlyContinue)) {
            if (@(Get-VhLogProblems ([IO.File]::ReadAllText($l.FullName))).Count) { $o += "### 로그 오류 줄(같은 줄은 묶음): $($l.FullName)"; $o += Get-VhLogDigest $l.FullName 30 }
        }
    }
    $o -join "`n"
}
# What one verification means to the loop. A harness FAIL counts like a round's "LOOP: FAIL <test>".
function Get-VhHandoffEffect($Task) {
    $fail = @()
    foreach ($g in @(Get-VhLatestGroups $Task)) {
        if ($g.status -ne 'FAIL') { continue }
        foreach ($f in @(Get-ChildItem -LiteralPath $g.directory -Recurse -Filter run.json -ErrorAction SilentlyContinue)) {
            try { $r = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { continue }
            foreach ($s in @($r.sections)) { if ($s.verdict -eq 'FAIL') { $fail += $s.name } }
        }
    }
    $review = if ($Task.review) { "$($Task.review.status)" } else { '' }
    [pscustomobject]@{ stage="$($Task.stage)"; complete=($Task.stage -eq 'COMPLETE'); fail=@($fail | Select-Object -Unique); review=$review }
}
# Division of labour is on only when the user's switch file exists and 5+ real baseline rounds are recorded.
function Get-VhHandoffGate($Directory, $Repo) {
    $sw = Join-Path $Repo 'docs\valheim\handoff-on.txt'
    $on = Test-Path -LiteralPath $sw
    # "기준 생략" in the switch file: the user asked to try division of labour before the 5-round baseline (10-26 trial run)
    $skip = $on -and ([IO.File]::ReadAllText($sw) -match '기준 생략')
    $baseline = @(Get-ChildItem -LiteralPath (Join-Path $Directory 'usage-records') -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
        try { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
    } | Where-Object { $_.kind -eq 'implementation' -and $_.result -eq 'present' -and $_.mode -ne 'handoff' }).Count
    $why = if (-not $on) { 'off: docs/valheim/handoff-on.txt 없음' }
        elseif ($baseline -lt 5 -and -not $skip) { "off: 기준 회차 $baseline/5" }
        else { 'on' }
    [pscustomobject]@{ on=($why -eq 'on'); why=$why; baseline=$baseline }
}
# Short result for the next Claude round: statuses, failing sections, original evidence paths and review findings.
function Get-VhSummary($Task, $Dir) {
    $o = @("# 검증 결과 $($Task.id)", "- 커밋: $($Task.target.commit) (기준 $($Task.base))", "- 단계: $($Task.stage) / 다음: $($Task.next)", "- 계획: $($Task.plan.purpose)")
    if ($Task.build_result) { $o += "- 빌드·Core: Core $($Task.build_result.core_total)개 중 실패 $($Task.build_result.core_failed)" }
    for ($i = 0; $i -lt @($Task.plan.groups).Count; $i++) {
        $pg = $Task.plan.groups[$i]
        $g = @($Task.groups | Where-Object { $_.index -eq $i } | Select-Object -Last 1)
        $o += "- 묶음 $i ($($pg.mode): $(@($pg.tests) -join '→')$(if ($pg.biome) { ", $($pg.biome)" })): $(if ($g.Count) { $g[0].status } else { '안 함' })"
        if (-not $g.Count) { continue }
        foreach ($f in @(Get-ChildItem -LiteralPath $g[0].directory -Recurse -Filter run.json -ErrorAction SilentlyContinue)) {
            try { $r = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $o += "  - run.json 못 읽음: $($f.FullName)"; continue }
            foreach ($s in @($r.sections)) { $o += "  - $($s.name): $($s.verdict) — $((@($s.evidence) | Select-Object -First 6) -join ' / ')" }
            if ($r.status -ne 'PASS') { $o += "  - 실행 판정 $($r.status), 원본: $($f.DirectoryName) (diag.txt·LogOutput.log)" }
            if ($r.exit_hang) { $o += '  - 게임 종료 멈춤(저장 뒤, 강제 종료) — 판정과 별개, Errors.txt E038 발생 횟수에 더함' }
        }
        foreach ($x in @(Get-ChildItem -LiteralPath $g[0].directory -Directory -Filter 'interrupted-*' -ErrorAction SilentlyContinue)) { $o += "  - 중단 증거: $($x.FullName)" }
    }
    if ($Task.review -and $Task.review.status -eq 'NOT_NEEDED') { $o += '- 실패 발췌: 필요 없음(모두 PASS)' }
    elseif ($Task.review -and $Task.review.status -eq 'EXCERPT') {
        $x = Join-Path "$($Task.review.directory)" 'excerpt.md'
        $o += "- 실패 발췌(스크립트, 원문 그대로): $x"
        if (Test-Path -LiteralPath $x) { $o += ([IO.File]::ReadAllText($x, [Text.Encoding]::UTF8)).TrimEnd() }
    } elseif ($Task.review) { $o += "- 발췌: $($Task.review.status) — $($Task.review.directory)" }
    else { $o += '- 실패 발췌: 안 함' }
    $o += "- 원본 폴더: $Dir"
    $o -join "`n"
}
# Loop side of one handoff: Prepare -> Test -> Review -> Summary, each its own harness process (-FromLoop; the caller is the parent).
# Harness failures never throw here: the result file always says what happened and nothing missing becomes PASS.
# handoff-running.json marks an unfinished handoff so a restarted loop tells the next round instead of silently dropping it.
function Invoke-VhHandoff($Repo, $Dir, $Base, [int]$TestMinutes, [scriptblock]$OnWait, $Round) {
    $scripts = Join-Path $Repo 'scripts'; $root = Join-Path $Dir 'harness'
    $plan = Join-Path $Dir 'handoff-plan.json'; $file = Join-Path $Dir 'verify-result.md'; $pending = Join-Path $Dir 'handoff-running.json'
    $env:VH_LOOP_PID = "$PID"
    $steps = [ordered]@{}
    $run = {
        param($name, $argText, $minutes)
        $o = Join-Path $Dir "handoff-$name.log"; $t = Get-Date
        $r = Invoke-VhProcess 'powershell.exe' ("-NoProfile -ExecutionPolicy Bypass -File `"$scripts\vh-harness.ps1`" -FromLoop $argText") $Repo $o "$o.err" ($minutes * 60) '' $OnWait
        $steps[$name] = [pscustomobject]@{ exit_code=$r.exit_code; timed_out=$r.timed_out; minutes=[math]::Round(((Get-Date) - $t).TotalMinutes, 2) }
        [pscustomobject]@{ ok=($r.exit_code -eq 0 -and -not $r.timed_out); text=(([IO.File]::ReadAllText($o) + "`n" + [IO.File]::ReadAllText("$o.err")).Trim()) }
    }
    $tail = { param($text, $n) (@($text -split '\r?\n' | Where-Object { $_.Trim() }) | Select-Object -Last $n | ForEach-Object { "  $_" }) -join "`n" }
    $id = ''; $effect = $null
    Write-VhJson $pending @{ round=$Round; base=$Base; started=(Get-Date).ToString('o'); task='' }
    try {
        if (-not (Test-Path -LiteralPath $plan)) {
            $md = "# 검증 인계 실패`n- 회차가 $plan 을 남기지 않음 → 빌드·게임 시험·리뷰 안 함(PASS 아님)"
        } else {
            $prep = & $run 'prepare' "-Action Prepare -BaseCommit $Base -Plan `"$plan`"" 30
            if ($prep.text -match 'Prepared: (\S+)') { $id = $Matches[1] }
            $keep = if ($id) { Join-Path $root "$id\plan.json" } else { Join-Path $Dir "handoff-plan.failed-$(Get-Date -Format 'yyyyMMdd-HHmmss').json" }
            Move-Item -LiteralPath $plan -Destination $keep -Force   # one plan is used once
            if (-not $id) {
                $md = "# 검증 인계 실패(준비 단계: 계획·작업 트리·빌드·Core)`n- 기준 커밋 $Base, 계획 $keep`n- 출력 끝:`n$(& $tail $prep.text 25)`n- 원본: $Dir\handoff-prepare.log(.err)"
            } else {
                Write-VhJson $pending @{ round=$Round; base=$Base; started=(Get-Date).ToString('o'); task=$id }
                $test = & $run 'test' "-Action Test -TaskId $id" $TestMinutes
                $rev = & $run 'review' "-Action Review -TaskId $id" 45
                $sum = & $run 'summary' "-Action Summary -TaskId $id" 5
                $md = if ($sum.ok) { $sum.text } else { "# 검증 결과 요약 못 만듦 $id`n$(& $tail $sum.text 10)" }
                if (-not $test.ok) { $md += "`n- 시험 단계 오류(PASS 아님):`n$(& $tail $test.text 8)" }
                if (-not $rev.ok) { $md += "`n- 발췌 단계 오류:`n$(& $tail $rev.text 5)" }
                $task = Get-Content -LiteralPath (Join-Path $root "$id\state.json") -Raw -Encoding UTF8 | ConvertFrom-Json
                $effect = Get-VhHandoffEffect $task
                Write-VhJson (Join-Path $Dir "handoff-records\$id.json") ([ordered]@{ round=$Round; task=$id; base=$Base; commit=$task.target.commit; stage=$task.stage; review=$effect.review; fail=$effect.fail; steps=$steps })
            }
        }
        [IO.File]::WriteAllText($file, $md + "`n", (New-Object Text.UTF8Encoding($false)))
        [pscustomobject]@{ File=$file; Id=$id; Effect=$effect; Steps=$steps }
    } finally { Remove-Item -LiteralPath $pending -ErrorAction SilentlyContinue }
}
function ConvertTo-VhEncodedCommand([string]$Command) { [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command)) }
function Quote-VhPS([string]$Value) { "'" + $Value.Replace("'", "''") + "'" }
