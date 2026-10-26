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
function Write-VhUsage($Directory, $ResultJson, $Round, $Account, $Kind, $Todo, $Started, $ExitCode, $RunId) {
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
        exit_code=$ExitCode; result=$parse; is_error=$r.is_error
    }
    # Each attempt has its own immutable record; retrying the same round never overwrites an attempt.
    $path = Join-Path $Directory "usage-records\$RunId.json"
    Write-VhJson $path $record
    # CSV is a replaceable index of authoritative JSON records, including missing-result attempts.
    $rows = @(Get-ChildItem (Join-Path $Directory 'usage-records') -Filter '*.json' | ForEach-Object {
        Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    } | Sort-Object started)
    $csv = ($rows | ConvertTo-Csv -NoTypeInformation) -join "`r`n"
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
function Get-VhModelOutcome($ExitCode, [bool]$TimedOut, [string]$Events, [bool]$HasResult) {
    if ($TimedOut) { return 'TIMEOUT' }
    # Only structured errors are classified; arbitrary prose must not trigger a guessed reset time.
    $errors = @(); $completed = $false
    foreach ($line in ($Events -split '\r?\n')) {
        try {
            $e = $line | ConvertFrom-Json
            if ($e.type -eq 'turn.completed') { $completed=$true }
            if ($e.type -in @('error','turn.failed')) { $errors += ($e | ConvertTo-Json -Depth 15 -Compress) }
        } catch { }
    }
    $errorText = $errors -join "`n"
    if ($errorText -match '"(code|type)"\s*:\s*"(usage_limit_reached|rate_limit_exceeded)"') { return 'LIMIT' }
    if ($errorText -match '"(code|type)"\s*:\s*"(unauthorized|authentication_error|invalid_api_key)"') { return 'AUTH' }
    if ($ExitCode -ne 0 -or $errors.Count -gt 0) { return 'ERROR' }
    if (-not $HasResult -or -not $completed) { return 'MISSING' }
    'COMPLETE'
}
function Assert-VhReview($Review, $Task) {
    if ($Review.task_id -cne $Task.id -or $Review.commit -cne $Task.target.commit -or $Review.target_hash -cne $Task.target_hash) { throw 'Review target mismatch' }
    if ($Review.verdict -notin @('PASS','ISSUES','BLOCKED','INCOMPLETE')) { throw 'Invalid review verdict' }
    foreach ($f in @($Review.findings)) {
        if ($f.basis -notin @('code','execution','hypothesis')) { throw 'Invalid finding basis' }
        if ($f.category -eq 'environment' -and $f.blocking) { throw 'Environment failure is not a confirmed gameplay blocker' }
        if ($f.blocking -and ($f.basis -eq 'hypothesis' -or -not $f.evidence)) { throw 'Blocking finding lacks confirmed evidence' }
    }
    $blocking = @($Review.findings | Where-Object { $_.blocking })
    if (($Review.verdict -eq 'BLOCKED') -ne ($blocking.Count -gt 0)) { throw 'Inconsistent blocker verdict' }
    if ($Review.verdict -eq 'PASS' -and @($Review.findings).Count -gt 0) { throw 'PASS with findings' }
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
function Invoke-VhProcess($File, [string]$Arguments, $WorkingDirectory, $Out, $Err, [int]$TimeoutSeconds=1800, [string]$InputText) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=$File; $psi.Arguments=$Arguments; $psi.WorkingDirectory=$WorkingDirectory
    $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true
    $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    if ($InputText) { $psi.RedirectStandardInput=$true }
    $psi.StandardOutputEncoding=[Text.Encoding]::UTF8; $psi.StandardErrorEncoding=[Text.Encoding]::UTF8
    $p = [Diagnostics.Process]::Start($psi)
    Write-VhJson "$Out.process.json" @{ pid=$p.Id; started_ticks=$p.StartTime.ToUniversalTime().Ticks; executable=$File }
    # Persist bytes while the child runs, including before a parent interruption.
    $outStream=New-Object IO.FileStream($Out,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read,1,[IO.FileOptions]::WriteThrough)
    $errStream=New-Object IO.FileStream($Err,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read,1,[IO.FileOptions]::WriteThrough)
    try {
    $stdout = $p.StandardOutput.BaseStream.CopyToAsync($outStream); $stderr = $p.StandardError.BaseStream.CopyToAsync($errStream)
    if ($InputText) {
        # Windows PowerShell 5.1's StreamWriter uses the system code page; Codex requires UTF-8.
        $bytes=[Text.Encoding]::UTF8.GetBytes($InputText)
        $p.StandardInput.BaseStream.Write($bytes,0,$bytes.Length)
        $p.StandardInput.BaseStream.Flush(); $p.StandardInput.Close()
    }
    $timedOut = -not $p.WaitForExit($TimeoutSeconds*1000)
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
function ConvertTo-VhEncodedCommand([string]$Command) { [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command)) }
function Quote-VhPS([string]$Value) { "'" + $Value.Replace("'", "''") + "'" }
