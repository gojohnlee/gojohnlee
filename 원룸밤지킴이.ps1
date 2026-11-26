# 원룸 밤 지킴이(사용자 11-26: "자는 동안 절전 들어가지 않게, 스크립트 다 끝나면 컴터 꺼").
# - 사는 동안 Windows 유휴 절전을 막는다(SetThreadExecutionState). 설정은 안 바꿈.
# - 10분마다 원룸 폴더의 txt 중 가장 최근에 바뀐 시각을 본다. Gemini가 한 번이라도 썼고(시작 뒤 바뀐 파일 있음)
#   마지막으로 바뀐 지 90분이 지나면 = Gemini가 끝났다고 보고: 원룸감시.sh로 마지막 올림 → 5분 뒤 컴퓨터 끄기.
# - 아침 -Until 시각까지 Gemini가 아무것도 안 쓰면 끄지 않고 절전 막기만 풀고 끝낸다.
param([string]$Until = '09:00', [int]$IdleMin = 90)
$Src = Join-Path $env:USERPROFILE 'Desktop\원룸'; $Repo = $PSScriptRoot
$Log = Join-Path $Src '올림\밤지킴이.log'
function Log($m) { Add-Content -Encoding utf8 $Log "$(Get-Date -Format 'MM-dd HH:mm') $m" }
Add-Type -Namespace WR -Name Power -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
$start = Get-Date; $end = [datetime]::Parse($Until); if ($end -le $start) { $end = $end.AddDays(1) }
Log "시작(끝 시각 $end, Gemini 멈춤 판단 ${IdleMin}분)"
while ((Get-Date) -lt $end) {
  [void][WR.Power]::SetThreadExecutionState([uint32]'0x80000001')   # 절전 막기
  $last = (Get-ChildItem $Src -Filter *.txt -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
  if ($last -and $last -gt $start -and ((Get-Date) - $last).TotalMinutes -ge $IdleMin) {
    $r = & 'C:\Program Files\Git\bin\bash.exe' -c "cd /c/vh/house && bash 원룸감시.sh" 2>&1 | Out-String
    Log "Gemini 멈춘 지 ${IdleMin}분(마지막 $last) → 마지막 올림: $($r.Trim() -replace "`n",' / ') → 5분 뒤 끄기"
    & shutdown /s /t 300 /c "원룸 Gemini 작업이 끝나 5분 뒤 컴퓨터를 끕니다. 취소: shutdown /a"
    exit 0
  }
  Start-Sleep 600
}
[void][WR.Power]::SetThreadExecutionState([uint32]'0x80000000')
Log "끝 시각까지 Gemini 새 결과 없음 → 끄지 않고 절전 막기만 풂"
