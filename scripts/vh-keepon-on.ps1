# 지킴이(사이드 루프) 다시 켜기(사용자 11-13). PC 안 끄기 + 텔레그램 멈춤 알림·답장 받기를 끝 시각까지 돌린다.
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-keepon-on.ps1              # 오늘 18:30까지(지났으면 12시간)
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-keepon-on.ps1 -Until 22:00 # 오늘 22:00까지(지났으면 내일)
#   ... -Until '2026-11-15 09:00'                                                             # 날짜까지
# 끄기: 끝 시각이 지나거나 루프가 할 일을 다 끝내면 스스로 꺼짐. 바로 끄려면 Stop-ScheduledTask 'VH keep on'; Disable-ScheduledTask 'VH keep on'.
param([string]$Until)
$ErrorActionPreference = 'Stop'
$Dir = Join-Path $env:USERPROFILE 'vh-loop'
$now = Get-Date
if ($Until) {
    $d = [datetime]::Parse($Until)
    if ($Until -notmatch '\d{4}-' -and $d -le $now) { $d = $d.AddDays(1) }   # 시각만 줬는데 이미 지났으면 내일
} else {
    $d = $now.Date.AddHours(18.5); if ($now -ge $d) { $d = $now.AddHours(12) }
}
if ($d -le $now) { throw "끝 시각이 이미 지났어: $d" }
[IO.File]::WriteAllText((Join-Path $Dir 'keepon-until.txt'), $d.ToString('yyyy-MM-dd HH:mm'), (New-Object Text.UTF8Encoding $false))
if (-not (Get-ScheduledTask -TaskName 'VH keep on' -ErrorAction SilentlyContinue)) { throw "작업 'VH keep on'이 없어. 처음 만들 때처럼 다시 등록해야 해." }
Stop-ScheduledTask -TaskName 'VH keep on' -ErrorAction SilentlyContinue   # 돌고 있으면 새 끝 시각으로 다시
# conhost --headless 아래 powershell은 Stop-ScheduledTask로 안 죽음(11-13 확인) → 남은 지킴이를 직접 끔(안 그러면 새 것이 뮤텍스에 막혀 바로 끝남)
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -match 'vh-keepon\.ps1' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep 2
Enable-ScheduledTask -TaskName 'VH keep on' | Out-Null
Start-ScheduledTask -TaskName 'VH keep on'
"지킴이 켬: $($d.ToString('MM-dd HH:mm'))까지"
