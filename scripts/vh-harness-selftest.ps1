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
$file=Join-Path $OutputDirectory 'evidence.txt'; [IO.File]::WriteAllText($file,'original')
$ev=@(Get-VhEvidence @($file)); Check (Test-VhEvidence $ev) 'original evidence reusable'
[IO.File]::WriteAllText($file,'changed'); Check (-not (Test-VhEvidence $ev)) 'changed evidence invalidated'
$json=Join-Path $OutputDirectory 'state.json'; Write-VhJson $json @{stage='RUNNING'}; Write-VhJson $json @{stage='COMPLETE'}
Check ((Get-Content $json -Raw | ConvertFrom-Json).stage -eq 'COMPLETE') 'atomic checkpoint replacement'
$u=Write-VhUsage $OutputDirectory '{"type":"result","usage":{"input_tokens":17,"output_tokens":3,"cache_read_input_tokens":4,"cache_creation_input_tokens":2},"num_turns":1,"duration_ms":1000,"total_cost_usd":0.1,"is_error":false}' 182 'test' 'implementation' 'fixture' (Get-Date) 0 'fixture1'
Check ($u.input_tokens -eq 17 -and $u.cache_read_tokens -eq 4) 'actual fields only'
$u=Write-VhUsage $OutputDirectory '' 182 'test' 'reply' 'fixture' (Get-Date) 1 'fixture2'
Check ($null -eq $u.input_tokens -and $u.result -eq 'missing') 'missing usage remains unknown'
Check (@(Import-Csv (Join-Path $OutputDirectory 'usage.csv')).Count -eq 2) 'reply and repeated round separate'
Write-Output "Harness self-test: $script:checks PASS; evidence: $OutputDirectory"
