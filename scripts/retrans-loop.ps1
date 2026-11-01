# 재번역 루프(백그라운드): 대사를 게임 흐름 순서(articy-dump)로 덩어리씩 claude -p에 맡겨 다시 옮기고, 끝날 때마다 Korean.csv를 다시 만들어 게임에 넣는다.
# 시작: scripts\retrans-start.bat 더블클릭(저장소를 받고 숨긴 창으로 켬). 자동 시작 없음(사용자 11-02). 이미 돌고 있으면 새로 켠 것은 바로 끝남.
# 멈춤: C:\vh\necro-kr\loop\stop 파일, 또는 GitHub에서 docs/retrans-stop.txt를 만들면(내용 아무거나) 덩어리 하나 끝내고 멈춘다.
# 기록: loop\loop.log(덩어리마다 토큰), 상태: docs\retrans-status.md(10덩어리마다·상태가 바뀔 때 푸시).
# 계정(발헤임 루프와 같음): 1번(기본) → 사용 한도면 2번(%USERPROFILE%\.claude-acct2) → 둘 다 한도면 30분 쉬고 다시.
param([int]$Chunk = 120, [string]$Model = 'sonnet', [int]$Max = 0)
$W = 'C:\vh\necro-kr'; $Rt = "$W\retrans"; $L = "$W\loop"; $Repo = 'C:\vh\necromancer'
$Claude = Join-Path $env:USERPROFILE '.local\bin\claude.exe'
$Lang = "$env:USERPROFILE\AppData\LocalLow\Psychic Software\TheNecromancer\Languages\Korean.csv"
New-Item -ItemType Directory -Force $L | Out-Null
$mtx = New-Object Threading.Mutex($false, 'Global\NecroRetransLoop')
if (-not $mtx.WaitOne(0)) { exit }   # 이미 돌고 있음
$Accounts = @($null, (Join-Path $env:USERPROFILE '.claude-acct2')); $Names = @('1번', '2번'); $acct = 0; $until = @([datetime]::MinValue, [datetime]::MinValue)
if (-not (Test-Path $Accounts[1])) { $Accounts = @($null); $Names = @('1번') }
$tok = @{ n = 0; sum = 0 }   # 이번 실행 덩어리당 토큰(입력+캐시+출력) 평균
function Log($m) { Add-Content "$L\loop.log" "$(Get-Date -f 'MM-dd HH:mm') $m" -Encoding utf8 }
function Plain($s) { (($s -replace '</?align[^>]*>', '') -replace '<br>|\r?\n', ' / ').Trim() }

# 줄 순서: 덤프 순서(대화 흐름) → 덤프에 없는 줄은 CSV 순서
function Rows() { $raw = [IO.File]::ReadAllText("$W\Korean-fixed.csv"); $i = 0; for ($j = 0; $j -lt 3; $j++) { $i = $raw.IndexOf("`n", $i) + 1 }; $raw.Substring($i) | ConvertFrom-Csv }
$by = @{}; $csvOrder = @(); foreach ($r in Rows) { if ($r.EN -and $r.Korean) { $by[$r.ID] = $r; $csvOrder += $r.ID } }
$speaker = @{}; $order = New-Object Collections.Generic.List[string]; $seen = @{}
foreach ($ln in [IO.File]::ReadLines("$W\dump\articy-dump.tsv")) {
  $c = $ln.Split("`t"); if ($by.ContainsKey($c[0]) -and -not $seen.ContainsKey($c[0])) { $seen[$c[0]] = 1; $speaker[$c[0]] = $c[2]; $order.Add($c[0]) }
}
foreach ($id in $csvOrder) { if (-not $seen.ContainsKey($id)) { $seen[$id] = 1; $order.Add($id) } }
Log "시작: 줄 $($order.Count)개, 덩어리 $Chunk, 모델 $Model, 계정 $($Names -join '·')"

function Done() { $d = @{}; foreach ($f in Get-ChildItem $Rt -Filter *.tsv) { foreach ($ln in [IO.File]::ReadLines($f.FullName)) { if ($ln -match '^(0x[0-9A-F]+)\t') { $d[$Matches[1]] = 1 } } }; $d }
function Sync() { git -C $Repo pull -q --rebase --autostash origin necromancer 2>$null | Out-Null }
# 상태 올리기: $now = 지금 무엇을 하는지(쉬운 말)
function Status($now) {
  $all = $order.Count; $dn = Done; $d = @($order | ? { $dn[$_] }).Count
  $avg = if ($tok.n) { "덩어리당 평균 $([Math]::Round($tok.sum / $tok.n / 1000))천 토큰($($tok.n)개 기준, 이번 실행)" } else { '아직 없음' }
  $md = "# 재번역 진행`n`n- $(Get-Date -f 'yyyy-MM-dd HH:mm') 기준 $d / $all 줄 ($([Math]::Round(100 * $d / $all))%)`n- 지금: $now`n- 토큰: $avg`n- 멈추기: 이 폴더에 retrans-stop.txt를 만든다(내용 아무거나). 다시 켜기: PC에서 scripts\retrans-start.bat`n"
  Sync
  [IO.File]::WriteAllText("$Repo\docs\retrans-status.md", $md, (New-Object Text.UTF8Encoding($false)))
  git -C $Repo add docs/retrans-status.md 2>$null; git -C $Repo commit -q -m "재번역 진행 $d/$all" 2>$null; git -C $Repo push -q origin HEAD:necromancer 2>$null
  # 번역본 백업: 비공개 gojohnlee/valheim-refs의 necromancer 가지(공개 저장소엔 게임 글을 안 올림)
  $Refs = "$W\refs"
  if (Test-Path "$Refs\.git") {
    New-Item -ItemType Directory -Force "$Refs\translation\retrans" | Out-Null
    Copy-Item "$Rt\*.tsv" "$Refs\translation\retrans\" -Force; Copy-Item "$W\Korean-fixed.csv" "$Refs\translation\" -Force
    git -C $Refs add -A translation 2>$null; git -C $Refs commit -q -m "재번역 백업 $d/$all" 2>$null; git -C $Refs push -q origin necromancer 2>$null
  }
  Log "상태 올림: $d/$all — $now"
}
Status '번역 중'

$round = 0; $lastFirst = ''; $tries = 0
while ($true) {
  Sync
  if ((Test-Path "$L\stop") -or (Test-Path "$Repo\docs\retrans-stop.txt")) { Log '멈춤 파일 → 끝'; Status '멈춤(멈춤 파일)'; break }
  if ($Max -gt 0 -and $round -ge $Max) { Log "덩어리 $Max개 끝"; break }
  $done = Done
  $skip = @{}; if (Test-Path "$L\skip.txt") { foreach ($ln in [IO.File]::ReadLines("$L\skip.txt")) { $skip[$ln.Trim()] = 1 } }
  $first = -1; for ($k = 0; $k -lt $order.Count; $k++) { $id = $order[$k]; if (-not $done[$id] -and -not $skip[$id]) { $first = $k; break } }
  if ($first -lt 0) { Log '모두 끝'; Status "다 끝남$(if (Test-Path "$L\skip.txt") { " (3번 실패로 건너뛴 줄 $(@(Get-Content "$L\skip.txt").Count)개는 loop\skip.txt)" })"; break }
  $fid = $order[$first]
  if ($fid -eq $lastFirst) { $tries++ } else { $lastFirst = $fid; $tries = 0 }
  if ($tries -ge 3) { Add-Content "$L\skip.txt" $fid; Log "건너뜀 $fid (3번 실패)"; continue }

  # 입력 덩어리: 남은 줄은 ID|화자|단락수|영어|지금 한국어, 끝난 줄은 맥락용 [완료]
  $by = @{}; foreach ($r in Rows) { $by[$r.ID] = $r }
  $want = @{}; $lines = @()
  foreach ($id in $order.GetRange($first, [Math]::Min($Chunk, $order.Count - $first))) {
    $r = $by[$id]; $sp = $speaker[$id]; $ko = Plain $r.Korean
    if ($done[$id] -or $skip[$id]) { $lines += "[완료] $id|$sp|$ko"; continue }
    $n = @($r.Korean -split '<br>').Count; $want[$id] = $n
    $lines += "$id|$sp|$n|$(Plain $r.EN)|$ko"
  }
  [IO.File]::WriteAllLines("$L\chunk.txt", [string[]](@("# ID|화자|단락수|영어|지금 한국어") + $lines), (New-Object Text.UTF8Encoding($false)))
  $out = "$Rt\auto-{0:D5}.tsv" -f $first
  $tmp = "$L\out.tsv"; Remove-Item $tmp -ErrorAction SilentlyContinue
  # claude는 루프 폴더 안에서 읽기·쓰기만(권한 건너뛰기 없이 acceptEdits + Read,Write 도구만)
  Copy-Item "$Repo\docs\translate-guide.md", "$Repo\dist\KoreanFont\names-ko.tsv" $L -Force
  $prompt = "Read translate-guide.md and follow it. Names table: names-ko.tsv. Input: chunk.txt. Write Korean translations for every line not marked [완료] to out.tsv (UTF-8, one 'ID<TAB>문장' per line, paragraph count exactly as given). Do nothing else."
  $round++; Log "덩어리 $round 시작: ${first}번째부터 $($want.Count)줄 ($fid)"
  $psi = New-Object Diagnostics.ProcessStartInfo $Claude
  # 계정: 한도 시각이 지난 것 중 지금 것 먼저, 없으면 가장 먼저 풀리는 것까지 기다림
  $free = @(0..($Accounts.Count - 1) | ? { $until[$_] -le (Get-Date) })
  if (-not $free.Count) {
    $w = $until | Sort-Object | Select-Object -First 1; Log "모든 계정 한도 → $($w.ToString('HH:mm'))까지 쉼"; Status "사용 한도로 쉼, $($w.ToString('HH:mm'))에 다시"
    Start-Sleep ([Math]::Max(60, ($w - (Get-Date)).TotalSeconds)); $round--; continue
  }
  if ($free -notcontains $acct) { $acct = $free[0]; Log "계정 $($Names[$acct])로 바꿈" }
  $psi.Arguments = "-p `"$prompt`" --model $Model --effort low --permission-mode acceptEdits --disable-slash-commands --strict-mcp-config --tools `"Read,Write`" --output-format json"
  if ($Accounts[$acct]) { $psi.EnvironmentVariables['CLAUDE_CONFIG_DIR'] = $Accounts[$acct] } else { $psi.EnvironmentVariables.Remove('CLAUDE_CONFIG_DIR') }
  $psi.WorkingDirectory = $L; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
  $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
  $p = [Diagnostics.Process]::Start($psi); $so = $p.StandardOutput.ReadToEndAsync(); $se = $p.StandardError.ReadToEndAsync()
  if (-not $p.WaitForExit(1800000)) { try { $p.Kill() } catch {}; Log '30분 넘김 → 끊음' }
  $text = $so.Result + $se.Result
  if ($text -match '(?i)(usage limit|limit reached|hit your (\w+ )?limit|limit will reset|out of extra usage)') { Log "계정 $($Names[$acct]) 사용 한도 → 30분 뒤 다시 봄"; $until[$acct] = (Get-Date).AddMinutes(30); $tries = 0; $lastFirst = ''; $round--; continue }
  $t0 = ''; try { $j = $so.Result | ConvertFrom-Json; $u = $j.usage; $n = [int]$u.input_tokens + [int]$u.cache_creation_input_tokens + [int]$u.cache_read_input_tokens + [int]$u.output_tokens
    $tok.n++; $tok.sum += $n; $t0 = ", 토큰 $n(출력 $($u.output_tokens), 캐시 읽기 $($u.cache_read_input_tokens)), 계정 $($Names[$acct])" } catch { }

  # 검사: 아는 ID, 단락 수 맞는 줄만 남긴다
  $ok = @(); $bad = 0
  if (Test-Path $tmp) { foreach ($ln in [IO.File]::ReadAllLines($tmp)) {
    if ($ln -notmatch '^(0x[0-9A-F]+)\t(.+)$') { continue }
    $id = $Matches[1]; $t = $Matches[2]
    if (-not $want.ContainsKey($id) -or $t -match '[⟨⟩〈〉《》]') { $bad++; continue }
    if (@($t -split '\s+/(?:\s+|$)').Count -ne $want[$id]) { $bad++; continue }
    $ok += "$id`t$t"; $want.Remove($id)
  } }
  if ($ok.Count) { [IO.File]::AppendAllLines($out, [string[]]$ok, (New-Object Text.UTF8Encoding($false))) }
  Log "덩어리 $round 끝: 받음 $($ok.Count), 버림 $bad, 빠짐 $($want.Count)$t0"
  if (-not $ok.Count) { Start-Sleep 60; continue }

  $r = & "$Repo\scripts\fix-csv.ps1"; Log $r
  try { Copy-Item "$W\Korean-fixed.csv" $Lang -Force } catch { Log "게임에 넣기 실패: $_" }
  if ($round % 10 -eq 0) { Status '번역 중' }
}
