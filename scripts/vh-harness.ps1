# One verification task at a time. Preparation never starts the autonomous loop.
[CmdletBinding()]
param(
    [ValidateSet('Prepare','Test','Review','Status','Summary')][string]$Action='Status',
    [string]$TaskId,
    [string]$Plan,
    [string]$BaseCommit,
    [string]$Root=(Join-Path $env:USERPROFILE 'vh-loop\harness'),
    [string]$Game='C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [switch]$FromLoop   # vh-loop.ps1 only: the loop holds Global\VhLoop and waits for this process, so the loop mutex is not taken here
)
$ErrorActionPreference='Stop'
# git output (Korean diff text) is decoded with the console code page; a hidden child started by the loop would otherwise use CP949.
try { [Console]::OutputEncoding=New-Object Text.UTF8Encoding($false) } catch { }
. "$PSScriptRoot\vh-harness-lib.ps1"
$repo=Split-Path $PSScriptRoot -Parent
$Root=[IO.Path]::GetFullPath($Root)
if ($Root.StartsWith($repo.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must be outside the repository' }
[IO.Directory]::CreateDirectory($Root) | Out-Null
$mutex=New-Object Threading.Mutex($false,'Global\VhVerificationHarness')
$owned=$false
$executionMutex=New-Object Threading.Mutex($false,'Global\VhLoop')
$executionOwned=$false
try {
    try { $owned=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned=$true }
    if (-not $owned) { throw 'Another verification harness is active' }
    if ($FromLoop) {
        $parent=(Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
        if (-not $env:VH_LOOP_PID -or [int]$env:VH_LOOP_PID -ne $parent) { throw '-FromLoop is only for the loop process that started this harness' }
    }
    if ($Action -notin @('Status','Summary')) {
        if (-not $FromLoop) {
            try { $executionOwned=$executionMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $executionOwned=$true }
            if (-not $executionOwned) { throw 'Autonomous implementation loop is active' }
        }
        if (Test-VhImplementationProcess $repo) { throw 'An implementation worker is active' }
    }
    if ($Action -eq 'Prepare') {
        if (-not $Plan -or -not $BaseCommit) { throw 'Prepare requires -Plan and -BaseCommit' }
        if (@(Invoke-VhGit $repo @('status','--porcelain')).Count) { throw 'Commit the implementation before handoff; worktree must be clean' }
        $base=Invoke-VhGit $repo @('rev-parse',"$BaseCommit^{commit}") | Select-Object -Last 1
        $p=Get-Content -LiteralPath $Plan -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $p.purpose -or -not $p.requirements -or -not $p.groups) { throw 'Plan needs purpose, requirements and groups' }
        foreach ($group in $p.groups) {
            if ($group.mode -notin @('sequence','solo','restart') -or -not $group.tests) { throw 'Invalid test group' }
            foreach ($name in $group.tests) { if ($name -notmatch '^vh_[a-z0-9]+$') { throw "Invalid test name: $name" } }
            if ($group.mode -eq 'restart') {
                if (($group.tests -join ';') -ne 'vh_restart1;vh_restart2') { throw 'Restart group must contain restart1 then restart2' }
            } elseif (($group.tests -join ';') -match 'vh_restart[12]') { throw 'Restart tests require restart group' }
        }
        foreach ($spec in @($p.requirements)) { [void](Get-VhRequirement $repo $spec) }   # fail before building, not at review time
        $TaskId=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
        $dir=Join-Path $Root $TaskId; [IO.Directory]::CreateDirectory($dir) | Out-Null
        $task=[pscustomobject][ordered]@{ schema=1; id=$TaskId; base=$base; stage='BUILD'; created=(Get-Date).ToString('o'); plan=$p; target=$null; target_hash=''; build=@(); build_result=$null; groups=@(); review=$null; next='Build and Core tests'; attempt=0 }
        Write-VhJson "$dir\state.json" $task
        $before=Get-VhSource $repo
        $build=Invoke-VhProcess 'C:\Program Files\Git\bin\bash.exe' 'scripts/valheim-build.sh --no-dist' $repo "$dir\build.log" "$dir\build.err" 600
        if ($build.exit_code -ne 0 -or $build.timed_out) { throw "Build failed: $dir\build.log" }
        $core=Invoke-VhProcess 'dotnet.exe' ('test "'+$repo+'\valheim\ValheimHelper\ValheimHelper.slnx" --logger "trx;LogFileName=core.trx" --results-directory "'+$dir+'"') $repo "$dir\core.log" "$dir\core.err" 600
        # dotnet needs the repository project path while TRX belongs to this task.
        if ($core.exit_code -ne 0 -or $core.timed_out) { throw "Core failed: $dir\core.log" }
        [xml]$trx=Get-Content "$dir\core.trx" -Raw
        if ([int]$trx.TestRun.ResultSummary.Counters.total -lt 1 -or $trx.TestRun.ResultSummary.outcome -ne 'Completed' -and $trx.TestRun.ResultSummary.outcome -ne 'Passed' -or [int]$trx.TestRun.ResultSummary.Counters.failed -ne 0) { throw 'Core TRX did not prove success' }
        $self=Invoke-VhProcess 'powershell.exe' ('-NoProfile -ExecutionPolicy Bypass -File "'+$PSScriptRoot+'\vh-harness-selftest.ps1" -OutputDirectory "'+$dir+'\selftest"') $repo "$dir\selftest.log" "$dir\selftest.err" 120
        if ($self.exit_code -ne 0 -or $self.timed_out) { throw 'Harness self-test failed' }
        $task.build_result=[pscustomobject]@{ build=$build; core=$core; core_total=[int]$trx.TestRun.ResultSummary.Counters.total; core_failed=[int]$trx.TestRun.ResultSummary.Counters.failed; harness=$self }
        $source=Get-VhSource $repo
        if ($source.hash -ne $before.hash -or $source.commit -ne $before.commit) { throw 'Build changed tracked content (including dist). Commit the tested DLL, then Prepare again.' }
        $dll=Join-Path $repo 'valheim\ValheimHelper\Plugin\bin\Release\ValheimHelper.dll'
        $managed=Join-Path $Game 'valheim_Data\Managed'
        $gameFiles=@(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | Sort-Object Name | ForEach-Object { [pscustomobject]@{ path=$_.FullName; hash=(Get-VhHash $_.FullName); version=$_.VersionInfo.FileVersion } })
        if ($gameFiles.Count -eq 0) { throw 'Game assemblies missing' }
        $baseline=@(Get-VhEvidence @(Get-ChildItem (Join-Path $env:USERPROFILE 'vh-test-base\worlds_local\te4st41') -Recurse -File | ForEach-Object FullName))
        if (-not $baseline) { throw 'Test world baseline missing' }
        $task.target=[pscustomobject]@{ commit=$source.commit; source=$source.hash; dll=$dll; dll_hash=(Get-VhHash $dll); game=$gameFiles; baseline=$baseline; runner=(Get-VhHash "$PSScriptRoot\valheim-test.ps1") }
        $task.target_hash=Get-VhTextHash (($task.target | ConvertTo-Json -Depth 10 -Compress)+($p | ConvertTo-Json -Depth 10 -Compress))
        $task.build=@(Get-VhEvidence @("$dir\build.log","$dir\build.err","$dir\core.log","$dir\core.err","$dir\core.trx","$dir\selftest.log","$dir\selftest.err"))
        $task.stage='TEST'; $task.next='Run the fixed test groups'; Write-VhJson "$dir\state.json" $task
        $changed=Invoke-VhGit $repo @('diff','--name-only',"$base..$($source.commit)")
        [IO.File]::WriteAllText("$dir\handoff.md", "Task: $TaskId`nCommit: $($source.commit)`nBase: $base`nTarget hash: $($task.target_hash)`nPurpose: $($p.purpose)`nRequirements: $($p.requirements -join ', ')`nChanged files:`n$($changed -join "`n")`nBuild/Core evidence: state.json`nTest plan: state.json (order and group boundaries are significant)`n",[Text.Encoding]::UTF8)
        Write-Output "Prepared: $TaskId ($dir)"; return
    }
    if (-not $TaskId -or $TaskId -notmatch '^[a-zA-Z0-9-]+$') { throw 'Supply a valid -TaskId' }
    $dir=Join-Path $Root $TaskId
    $task=Get-Content "$dir\state.json" -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($task.id -cne $TaskId) { throw 'Task identity mismatch' }
    if ($Action -eq 'Status') { $task | ConvertTo-Json -Depth 20; return }
    if ($Action -eq 'Summary') { $md=Get-VhSummary $task $dir; [IO.File]::WriteAllText("$dir\result.md",$md+"`n",(New-Object Text.UTF8Encoding($false))); Write-Output $md; return }
    Assert-VhChildrenStopped $dir
    if (-not $task.target) { throw 'Preparation did not finish; prepare a new task' }
    if ((Get-VhTextHash (($task.target | ConvertTo-Json -Depth 10 -Compress)+($task.plan | ConvertTo-Json -Depth 10 -Compress))) -cne $task.target_hash) { throw 'Target or plan changed after preparation' }
    if (-not (Test-VhEvidence $task.target.baseline)) { throw 'Test world baseline changed' }
    $source=Get-VhSource $repo
    if ($source.commit -cne $task.target.commit -or $source.hash -cne $task.target.source -or (Get-VhHash $task.target.dll) -cne $task.target.dll_hash -or -not (Test-VhEvidence $task.target.game) -or -not (Test-VhEvidence $task.build)) { throw 'Target or build evidence changed; prepare a new task' }
    if ($Action -eq 'Test') {
        if (Get-Process -Name 'valheim*' -ErrorAction SilentlyContinue) { throw 'A game is active; no process was terminated' }
        foreach ($pending in @(Get-ChildItem -LiteralPath $Root -Directory)) {
            if ($pending.Name -eq $TaskId -or -not (Test-Path (Join-Path $pending.FullName 'state.json'))) { continue }
            $oldTask=Get-Content (Join-Path $pending.FullName 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($unfinished in @(Get-VhLatestGroups $oldTask | Where-Object { $_.status -eq 'RUNNING' })) {
                Assert-VhChildrenStopped $pending.FullName
                Save-VhInterruptedEvidence $unfinished.directory $Game
                $unfinished.status='INTERRUPTED'; $oldTask.stage='INTERRUPTED'; $oldTask.next='Interrupted evidence preserved before another task; inspect original logs'
                Write-VhJson (Join-Path $pending.FullName 'state.json') $oldTask
            }
        }
        $loopMutex=New-Object Threading.Mutex($false,'Global\VhLoop')
        $loopOwned=$false
        try {
            if (-not $FromLoop) {
                try { $loopOwned=$loopMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $loopOwned=$true }
                if (-not $loopOwned) { throw 'Autonomous loop is active' }
            }
            for ($i=0; $i -lt @($task.plan.groups).Count; $i++) {
                $existing=@($task.groups | Where-Object { $_.index -eq $i } | Select-Object -Last 1)
                if ($existing.Count -and $existing[0].status -eq 'RUNNING') {
                    Save-VhInterruptedEvidence $existing[0].directory $Game
                    # An interrupted real execution requires a new task; never erase uncertain first-failure evidence.
                    $task.stage='INTERRUPTED'; $task.next='Inspect preserved interrupted evidence and prepare a new task from the group beginning'
                    Write-VhJson "$dir\state.json" $task
                    throw 'Interrupted attempt preserved. Prepare a new task; do not silently retry over its evidence.'
                }
                $resume=Get-VhResumeDecision $(if ($existing.Count) { $existing[0] } else { $null })
                if ($resume -eq 'REJECT') { throw 'Previous evidence is damaged or an interrupted failure exists. Preserve it and prepare a new task.' }
                if ($resume -eq 'REUSE') { continue }
                if ($resume -eq 'REVIEW') { break }
                # Interrupted sequence/restart group always starts from its original baseline.
                $task.attempt++; $attemptDir=Join-Path $dir "group-$i-attempt-$($task.attempt)"
                [IO.Directory]::CreateDirectory($attemptDir) | Out-Null
                $group=$task.plan.groups[$i]
                $record=[pscustomobject]@{ index=$i; attempt=$task.attempt; status='RUNNING'; evidence=@(); directory=$attemptDir }
                $task.groups=@($task.groups)+$record; $task.stage='TEST'; $task.next="Group $i running"; Write-VhJson "$dir\state.json" $task
                $runs=@(); if ($group.mode -eq 'sequence') { $runs+=,(@($group.tests)) } else { foreach ($t in $group.tests) { $runs+=,(@($t)) } }
                $results=@()
                for ($j=0; $j -lt $runs.Count; $j++) {
                    $runDir=Join-Path $attemptDir "run-$j"; [IO.Directory]::CreateDirectory($runDir) | Out-Null
                    $settings=@{ VH_DLL=$task.target.dll; VH_TEST=($runs[$j] -join ';'); VH_WORLD='te4st41'; VH_CHAR='vhtest'; VH_MIN='1'; VH_SOLO='0'; VH_RESET='1'; VH_QUICK='0'; VH_BIOME=''; VH_EVIDENCE_DIR=$runDir; VH_HARNESS='1'; VH_HARNESS_SCRIPTS=$PSScriptRoot }
                    if ($group.biome) { $settings.VH_BIOME=$group.biome }
                    if ($group.quick) { $settings.VH_QUICK='1' }
                    if ($group.mode -eq 'restart' -and $j -eq 1) { $settings.VH_RESET='0' }
                    $lines=@('$ErrorActionPreference=''Stop''; [Console]::OutputEncoding=[Text.Encoding]::UTF8')
                    foreach ($key in $settings.Keys) { $lines+='$env:'+ $key +'='+ (Quote-VhPS $settings[$key]) }
                    $lines+='try { & ([scriptblock]::Create([IO.File]::ReadAllText('+(Quote-VhPS "$PSScriptRoot\valheim-test.ps1")+',[Text.Encoding]::UTF8))) } catch { Write-Error $_; exit 1 }'
                    $result=Invoke-VhProcess 'powershell.exe' ('-NoProfile -ExecutionPolicy Bypass -EncodedCommand '+(ConvertTo-VhEncodedCommand ($lines -join "`n"))) $repo "$runDir\stdout.log" "$runDir\stderr.log" 2100
                    if ($result.timed_out -or $result.exit_code -ne 0 -or -not (Test-Path "$runDir\run.json")) { $results+='ABORTED'; break }
                    $run=Get-Content "$runDir\run.json" -Raw -Encoding UTF8 | ConvertFrom-Json
                    if ($run.dll_hash -cne $task.target.dll_hash) { $results+='MISMATCH'; break }
                    $results+=$run.status
                    if ($run.status -ne 'PASS' -and $group.mode -eq 'restart') { break }
                }
                $record.status='PASS'; foreach ($v in $results) { if ($v -ne 'PASS') { $record.status=$v; break } }
                if ((Get-VhSource $repo).hash -cne $task.target.source -or (Get-VhHash $task.target.dll) -cne $task.target.dll_hash -or -not (Test-VhEvidence $task.target.game)) { $record.status='MISMATCH' }
                $record.evidence=@(Get-VhEvidence @(Get-ChildItem $attemptDir -Recurse -File | ForEach-Object FullName))
                Write-VhJson "$dir\state.json" $task
                if ($record.status -ne 'PASS') { break }
            }
            $task.stage='REVIEW'; $task.next='Review diff, expected behavior and retained test evidence'; Write-VhJson "$dir\state.json" $task
            $task.groups | Select-Object index,attempt,status,directory | Format-Table; return
        } finally { if ($loopOwned) { $loopMutex.ReleaseMutex() }; $loopMutex.Dispose() }
    }
    if ($Action -eq 'Review') {
        if ($task.stage -notin @('REVIEW','COMPLETE','FIX','REVIEW_UNAVAILABLE')) { throw 'Run the planned tests first' }
        $latest=@(Get-VhLatestGroups $task)
        if (-not $latest) { throw 'No game execution evidence' }
        foreach ($g in $latest) { if (-not (Test-VhEvidence $g.evidence) -or $g.status -eq 'RUNNING') {
            $task.stage='INVALID'; $task.next='Preserve damaged test evidence; new preparation required'; Write-VhJson "$dir\state.json" $task
            throw 'Test evidence missing or changed'
        } }
        # No model at all (user 10-27: GPT dropped). All planned groups PASS -> COMPLETE; otherwise the script quotes the failing lines verbatim (path:line) so Claude does not read whole logs.
        if (@($latest | Where-Object { $_.status -ne 'PASS' }).Count -eq 0 -and $latest.Count -eq @($task.plan.groups).Count) {
            $task.review=[pscustomobject]@{ status='NOT_NEEDED'; directory=''; evidence=@(); model=''; retry_at=$null }
            $task.stage='COMPLETE'; $task.next='Record exact commit and tests (all PASS)'; Write-VhJson "$dir\state.json" $task
            Write-Output 'Review: NOT_NEEDED; stage: COMPLETE'; return
        }
        $task.attempt++; $reviewDir=Join-Path $dir "excerpt-$($task.attempt)"; [IO.Directory]::CreateDirectory($reviewDir) | Out-Null
        [IO.File]::WriteAllText("$reviewDir\excerpt.md",(Get-VhFailExcerpt $latest)+"`n",(New-Object Text.UTF8Encoding($false)))
        $task.review=[pscustomobject]@{ status='EXCERPT'; directory=$reviewDir; evidence=@(Get-VhEvidence @("$reviewDir\excerpt.md")); model=''; retry_at=$null }
        $task.stage='FIX'; $task.next='Claude: find the cause from the quoted lines and fix; preserve confirmed blockers'
        Write-VhJson "$dir\state.json" $task
        Write-Output "Review: EXCERPT; stage: FIX; $reviewDir"
    }
} finally { if ($executionOwned) { $executionMutex.ReleaseMutex() }; $executionMutex.Dispose(); if ($owned) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
