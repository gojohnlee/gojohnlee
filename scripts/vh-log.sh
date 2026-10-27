#!/usr/bin/env bash
# 게임 로그 요약: 오류·경고·예외·FAIL(출처 무관) + 모드 줄만, 줄당 200자, 연속 중복은 (xN), 예외 뒤 스택 3줄.
# 쓰기: bash scripts/vh-log.sh [줄수=60] [추가 grep 패턴]
# 원인을 못 찾으면 그때만 grep -n 으로 해당 줄 앞뒤를 직접 본다.
N="${1:-60}"; PAT="${2:-}"
L="${VH_LOG:-/c/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/LogOutput.log}"
[ -f "$L" ] || { echo "로그 없음: $L"; exit 1; }
echo "== $(date -r "$L" '+%m-%d %H:%M') $(wc -l < "$L")줄 · Error $(grep -c '^\[Error' "$L") · Warning $(grep -c '^\[Warning' "$L") · Exception $(grep -c 'Exception' "$L") · FAIL $(grep -c 'FAIL' "$L")"
awk -v pat="$PAT" '
  {
    if (/^\[(Error|Fatal|Warning)/ || /Exception|FAIL|Valheim Helper/ || (pat != "" && $0 ~ pat)) { keep=1; stack=($0 ~ /Exception/) ? 3 : 0 }
    else if (stack > 0 && /^[ \t]/) { keep=1; stack-- }
    else { keep=0; stack=0 }
  }
  keep {
    s = substr($0, 1, 200)
    if (s == prev) { cnt++; next }
    if (prev != "") print prev (cnt > 1 ? "  (x" cnt ")" : "")
    prev = s; cnt = 1
  }
  END { if (prev != "") print prev (cnt > 1 ? "  (x" cnt ")" : "") }
' "$L" | tail -n "$N"
