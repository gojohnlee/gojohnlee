#!/usr/bin/env bash
# docs/valheim/history.md에서 "### 요청 · ... · <이름>"(또는 "### 질문 · ...", 루프가 토큰 줄을 붙일 때 "### 회차 N · ...") 항목(맨 위 = 최근 것)을 찾아 그 항목 끝(다음 ### · ## · <details 줄 바로 위)에
# 표준 입력을 끼워 넣는다. 요청 일을 끝낸 회차가 "**끝**"을 같은 항목에 붙일 때 쓴다(history.md는 읽거나 열지 않음).
#   bash scripts/vh-history-note.sh "<요청 이름 앞부분>" <<'EOF'
#   **끝** · MM-dd HH:mm 회차 N
#
#   - 한 일 · 남은 것 · 못 한 이유
#   EOF
set -e
f="$(cd "$(dirname "$0")/.." && pwd)/docs/valheim/history.md"
key="$1"
[ -n "$key" ] || { echo "요청 이름을 주세요" >&2; exit 1; }
start="$(grep -nE -- '^### (요청 · |질문 · |회차 [0-9]+ · )' "$f" | grep -F -- "$key" | head -1 | cut -d: -f1)"
[ -n "$start" ] || { echo "history.md에 '### 요청(질문) · … $key' 항목이 없음" >&2; exit 1; }
end="$(awk -v s="$start" 'NR > s && /^(### |## |<details)/ { print NR; exit }' "$f")"
[ -n "$end" ] || end=$(( $(wc -l < "$f") + 1 ))
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
[ -z "$(sed -n "$((end - 1))p" "$f" | tr -d '[:space:]')" ] || printf '\n' > "$tmp"
cat >> "$tmp"
grep -q '[^[:space:]]' "$tmp" || { echo "넣을 내용이 비었음" >&2; exit 1; }
[ -z "$(tail -c1 "$tmp")" ] || echo >> "$tmp"   # 끝 줄바꿈
printf '\n' >> "$tmp"                           # 다음 항목과 빈 줄
sed -i "$((end - 1))r $tmp" "$f"
echo "history.md $(sed -n "${start}p" "$f" | cut -c1-60) 항목 끝에 넣음"
