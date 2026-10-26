param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\vh-harness-lib.ps1"
if (-not $OutputDirectory) { $OutputDirectory=Join-Path $env:TEMP ('vh-harness-selftest-'+[guid]::NewGuid().ToString('N')) }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$script:checks=0
function Check($Condition, $Name) { if (-not $Condition) { throw "FAIL: $Name" }; $script:checks++; Write-Output "PASS: $Name" }
function Reject([scriptblock]$Body, $Name) { $caught=$false; try { & $Body } catch { $caught=$true }; Check $caught $Name }
$one="요약: 1/1 PASS`n=== vh_npctest time ===`n판정: PASS (17/17)`n시간: 3초"
Check ((Read-VhDiag $one @('vh_npctest')).status -eq 'PASS') 'complete exact test'
Check ((Read-VhDiag '' @('vh_npctest')).status -eq 'MISSING') 'missing is not PASS'
Check ((Read-VhDiag '요약: 1/1 PASS' @('vh_npctest')).status -eq 'MISSING') 'summary alone is not PASS'
Check ((Read-VhDiag "=== vh_npctest time`nPASS first check" @('vh_npctest')).status -eq 'MISSING') 'partial PASS without terminal verdict rejected'
Check ((Read-VhDiag $one @('vh_npctest','vh_npctest')).status -eq 'MISSING') 'missing repeated test'
Check ((Read-VhDiag "$one`n$one" @('vh_npctest','vh_npctest')).status -eq 'PASS') 'duplicate sequence retained'
Check ((Read-VhDiag $one @('vh_flagtest')).status -eq 'MISSING') 'wrong test rejected'
Check ((Read-VhDiag "=== vh_a time`nFAIL bad`n판정: PASS (1/1)" @('vh_a')).status -eq 'FAIL') 'failure cannot be overwritten by PASS'
Check ((Read-VhDiag "=== vh_a time`n판정: 환경 불량 (setup)" @('vh_a')).status -eq 'ENVIRONMENT') 'environment is distinct'
Check ((Read-VhDiag "=== vh_a time`nOK incomplete" @('vh_a')).status -eq 'MISSING') 'interrupted section'
Check ((Get-VhModelOutcome 0 $false '{"type":"turn.completed"}' $true) -eq 'COMPLETE') 'structured model completion'
Check ((Get-VhModelOutcome 0 $false '' $true) -eq 'MISSING') 'final file without completion rejected'
Check ((Get-VhModelOutcome 0 $false '{"type":"turn.completed"}' $false) -eq 'MISSING') 'no final result'
Check ((Get-VhModelOutcome 1 $false '{"type":"turn.failed","error":{"code":"usage_limit_reached"}}' $false) -eq 'LIMIT') 'simulated quota error'
Check ((Get-VhModelOutcome 1 $false '{"type":"error","code":"authentication_error"}' $false) -eq 'AUTH') 'simulated auth error'
Check ((Get-VhModelOutcome 1 $false 'network broken' $false) -eq 'ERROR') 'generic error stays generic'
Check ((Get-VhModelOutcome 0 $true '' $true) -eq 'TIMEOUT') 'timeout is not completion'
$fakeTask=[pscustomobject]@{id='test';target=[pscustomobject]@{commit='abc'};target_hash='fixed'}
$review=[pscustomobject]@{task_id='wrong';commit='abc';target_hash='fixed';verdict='PASS';findings=@()}
Reject { Assert-VhReview $review $fakeTask } 'wrong task id rejected'
$review.task_id='test'; $review.commit='def'
Reject { Assert-VhReview $review $fakeTask } 'wrong commit rejected'
$review.commit='abc'; $review.target_hash='other'
Reject { Assert-VhReview $review $fakeTask } 'wrong DLL/plan identity rejected'
$review.target_hash='fixed'; $review.verdict='BLOCKED'; $review.findings=@([pscustomobject]@{basis='hypothesis';blocking=$true;evidence='guess'})
Reject { Assert-VhReview $review $fakeTask } 'hypothesis cannot block'
$review.findings[0].basis='code'; Assert-VhReview $review $fakeTask
Check $true 'confirmed blocker retained'
$review.findings[0] | Add-Member category environment
Reject { Assert-VhReview $review $fakeTask } 'environment cannot become gameplay blocker'
$taskHistory=[pscustomobject]@{groups=@([pscustomobject]@{index=0;status='RUNNING'},[pscustomobject]@{index=0;status='PASS'},[pscustomobject]@{index=1;status='FAIL'})}
$latest=@(Get-VhLatestGroups $taskHistory)
Check ($latest.Count -eq 2 -and $latest[0].status -eq 'PASS' -and $latest[1].status -eq 'FAIL') 'resume uses latest attempt per group, preserving first failure history'
$file=Join-Path $OutputDirectory 'evidence.txt'; [IO.File]::WriteAllText($file,'original')
$ev=@(Get-VhEvidence @($file)); Check (Test-VhEvidence $ev) 'original evidence reusable'
[IO.File]::WriteAllText($file,'changed'); Check (-not (Test-VhEvidence $ev)) 'changed evidence invalidated'
$failed=[pscustomobject]@{status='FAIL'; evidence=$ev; directory=$OutputDirectory}
Check ((Get-VhResumeDecision $failed) -eq 'REJECT') 'damaged failed evidence never retried into PASS'
$failed.evidence=@(Get-VhEvidence @($file))
Check ((Get-VhResumeDecision $failed) -eq 'REVIEW') 'known failure kept for review, not rerun'
$failed.status='PASS'; Check ((Get-VhResumeDecision $failed) -eq 'REUSE') 'valid completed group reused'
$failed.status='RUNNING'; Check ((Get-VhResumeDecision $failed) -eq 'RESTART_GROUP') 'interrupted group starts at beginning'
$json=Join-Path $OutputDirectory 'state.json'; Write-VhJson $json @{stage='RUNNING'}; Write-VhJson $json @{stage='COMPLETE'}
Check ((Get-Content $json -Raw | ConvertFrom-Json).stage -eq 'COMPLETE') 'atomic checkpoint replacement'
$u=Write-VhUsage $OutputDirectory '{"type":"result","usage":{"input_tokens":17,"output_tokens":3,"cache_read_input_tokens":4,"cache_creation_input_tokens":2},"num_turns":1,"duration_ms":1000,"total_cost_usd":0.1,"is_error":false}' 182 'test' 'implementation' 'fixture' (Get-Date) 0 'fixture1'
Check ($u.input_tokens -eq 17 -and $u.cache_read_tokens -eq 4) 'actual fields only'
$u=Write-VhUsage $OutputDirectory '' 182 'test' 'reply' 'fixture' (Get-Date) 1 'fixture2'
Check ($null -eq $u.input_tokens -and $u.result -eq 'missing') 'missing usage remains unknown'
Check (@(Import-Csv (Join-Path $OutputDirectory 'usage.csv')).Count -eq 2) 'reply and repeated round separate'
$echo='$m=New-Object IO.MemoryStream; [Console]::OpenStandardInput().CopyTo($m); [Console]::Write([Convert]::ToBase64String($m.ToArray()))'
$sample='발헤임 UTF-8 검증'
$pipe=Invoke-VhProcess 'powershell.exe' ('-NoProfile -EncodedCommand '+(ConvertTo-VhEncodedCommand $echo)) $PSScriptRoot "$OutputDirectory\pipe.out" "$OutputDirectory\pipe.err" 10 $sample
Check ($pipe.exit_code -eq 0 -and [IO.File]::ReadAllText("$OutputDirectory\pipe.out") -eq [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($sample))) 'UTF-8 stdin transport on Windows'
$timed=Invoke-VhProcess 'powershell.exe' '-NoProfile -Command Start-Sleep 20' $PSScriptRoot "$OutputDirectory\timeout.out" "$OutputDirectory\timeout.err" 1
Check $timed.timed_out 'controlled child timeout preserves evidence'
$fakeGame=Join-Path $OutputDirectory 'game-fixture'
[IO.Directory]::CreateDirectory((Join-Path $fakeGame 'BepInEx\plugins\ValheimHelper')) | Out-Null
$shared=Join-Path $fakeGame 'BepInEx\plugins\ValheimHelper\diag.txt'
[IO.File]::WriteAllText($shared,'FAIL original interrupted evidence')
$attempt=Join-Path $OutputDirectory 'interrupted-attempt'; [IO.Directory]::CreateDirectory($attempt) | Out-Null
Save-VhInterruptedEvidence $attempt $fakeGame
[IO.File]::WriteAllText($shared,'new run')
$recovered=Get-ChildItem $attempt -Recurse -Filter diag.txt | Select-Object -First 1
Check ([IO.File]::ReadAllText($recovered.FullName) -eq 'FAIL original interrupted evidence') 'interrupted shared diagnostic survives next run'
$liveDir=Join-Path $OutputDirectory 'live-child'; [IO.Directory]::CreateDirectory($liveDir) | Out-Null
Write-VhJson (Join-Path $liveDir 'live.process.json') @{pid=$PID;started_ticks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks}
Reject { Assert-VhChildrenStopped $liveDir } 'live previous child prevents duplicate work'
$benign="[Error  :   BepInEx] Unable to start Unity log writer`n[Info   :Valheim Helper] Valheim Helper 1 로드됨"
Check (@(Get-VhLogProblems $benign).Count -eq 0) 'known BepInEx log-writer line is not an exception'
Check (@(Get-VhLogProblems "$benign`n[Error  :Valheim Helper] boom").Count -eq 1) 'real error still makes EXCEPTION'
Check (@(Get-VhLogProblems "[Error  :   BepInEx] Unable to start Unity log writer because X").Count -eq 1) 'benign match is exact, not a prefix'
$logFile=Join-Path $OutputDirectory 'LogOutput.log'; [IO.File]::WriteAllText($logFile,"[Error  :   BepInEx] Unable to start Unity log writer`nok`nNullReferenceException: a`nNullReferenceException: a")
$digest=Get-VhLogDigest $logFile
Check ($digest -match 'line 3 x2: NullReferenceException' -and $digest -match 'known harmless') 'log digest groups repeats and labels known lines'
$doc=@('# T','## A','a1','### A1','a2','## B','b1')
Check ((Get-VhSection $doc '## A') -eq "## A`na1`n### A1`na2") 'markdown section ends at same-level heading'
$txt=@('- one [x]','  more','- two','  two more')
Check ((Get-VhSection $txt 'one') -eq "- one [x]`n  more") 'indented section ends at next item'
Reject { Get-VhSection $txt 'absent' } 'missing requirement section is an error'
$err=@('head','==========','E001 / 관찰 중 — vh_alpha FAIL','  근거: x','','E002 / 조사 중 — vh_beta FAIL','  근거: y','==========')
$ex=Get-VhErrorsExcerpt $err @() @('vh_beta')
Check ($ex -match 'E001 / 관찰' -and $ex -match '근거: y' -and $ex -notmatch '근거: x') 'errors excerpt: all headings, related entries only'
$hdir=Join-Path $OutputDirectory 'handoff-group'; [IO.Directory]::CreateDirectory("$hdir\run-0") | Out-Null
Write-VhJson "$hdir\run-0\run.json" @{ status='FAIL'; sections=@(@{name='vh_a';verdict='PASS'},@{name='vh_b';verdict='FAIL'}) }
$ht=[pscustomobject]@{ stage='FIX'; groups=@([pscustomobject]@{index=0;status='FAIL';directory=$hdir}); review=[pscustomobject]@{status='LIMIT'} }
$he=Get-VhHandoffEffect $ht
Check (-not $he.complete -and ($he.fail -join ',') -eq 'vh_b' -and $he.astra_down) 'handoff effect: failing test names and Astra limit'
$ht.stage='COMPLETE'; $ht.groups[0].status='PASS'; $ht.review.status='COMPLETE'; $he=Get-VhHandoffEffect $ht
Check ($he.complete -and $he.fail.Count -eq 0 -and -not $he.astra_down) 'handoff effect: complete'
$sum=Get-VhSummary ([pscustomobject]@{ id='t1'; base='b'; stage='FIX'; next='n'; target=[pscustomobject]@{commit='c'}; build_result=$null; review=$null
    plan=[pscustomobject]@{ purpose='p'; groups=@([pscustomobject]@{mode='sequence';tests=@('vh_a','vh_b')}) }; groups=@([pscustomobject]@{index=0;status='FAIL';directory=$hdir}) }) $hdir
Check ($sum -match 'vh_b: FAIL' -and $sum -match '실행 판정 FAIL') 'summary lists failing section and original path'
$grepo=Join-Path $OutputDirectory 'gate-repo'; $gdir=Join-Path $OutputDirectory 'gate-loop'; [IO.Directory]::CreateDirectory("$grepo\docs\valheim") | Out-Null
Check (-not (Get-VhHandoffGate $gdir $grepo '').on) 'division of labour off without switch file'
[IO.File]::WriteAllText("$grepo\docs\valheim\handoff-on.txt",'on')
foreach ($k in 1..4) { [void](Write-VhUsage $gdir '{"type":"result","usage":{"input_tokens":1}}' $k 'a' 'implementation' 't' (Get-Date) 0 "g$k") }
[void](Write-VhUsage $gdir '{"type":"result","usage":{"input_tokens":1}}' 5 'a' 'implementation' 't' (Get-Date) 0 'g5' 'handoff')
[void](Write-VhUsage $gdir '' 6 'a' 'implementation' 't' (Get-Date) 1 'g6')
Check ((Get-VhHandoffGate $gdir $grepo '').baseline -eq 4) 'baseline counts real claude-only rounds only'
[void](Write-VhUsage $gdir '{"type":"result","usage":{"input_tokens":1}}' 7 'a' 'implementation' 't' (Get-Date) 0 'g7')
Check ((Get-VhHandoffGate $gdir $grepo '').on) 'division of labour on after 5 baseline rounds'
Check (-not (Get-VhHandoffGate $gdir $grepo (Get-Date).AddHours(1).ToString('o')).on) 'Astra backoff turns it off'
Check ((Get-Content "$gdir\usage.csv" -TotalCount 1) -match '"mode"') 'usage csv keeps the mode column'
$script:waits=0
$slow=Invoke-VhProcess 'powershell.exe' '-NoProfile -Command Start-Sleep 2' $PSScriptRoot "$OutputDirectory\slow.out" "$OutputDirectory\slow.err" 1 '' { $script:waits++ }
Check ($slow.timed_out -and $script:waits -eq 0) 'short timeout ends without waiting a full minute'
$hl=Join-Path $OutputDirectory 'handoff-loop'; [IO.Directory]::CreateDirectory($hl) | Out-Null
$hv=Invoke-VhHandoff (Split-Path $PSScriptRoot -Parent) $hl 'base' 5 $null 1
Check ($null -eq $hv.Effect -and [IO.File]::ReadAllText($hv.File) -match 'PASS 아님' -and -not (Test-Path "$hl\handoff-running.json")) 'missing plan is a reported failure, not a pass'
Write-Output "Harness self-test: $script:checks PASS; evidence: $OutputDirectory"
