#!/usr/bin/env bash
# docs/valheim/history.md "## 회차 기록" 줄 바로 밑(맨 위 = 최근)에 표준 입력을 끼워 넣는다.
# history.md는 크니 읽거나 열지 말고 이것만 쓴다. 사용자가 GitHub에서 읽으므로 이 꼴로(쉬운 말, 한 항목 한두 줄):
#   bash scripts/vh-history-add.sh <<'EOF'
#   ### 회차 N · MM-dd · 제목(무엇을 했나, 짧게)
#
#   **요약**
#
#   - progress "지금"에 넣는 것과 같은 한 줄
#
#   **자세히**
#
#   - 원인·고친 것·시험 결과(E00x면 그 ID와 새로 확인한 사실)
#   EOF
# 사용자 요청은 이 꼴(scripts/vh-loop-request.txt). 끝나면 scripts/vh-history-note.sh로 같은 항목에 **끝**을 붙인다:
#   ### 요청 · MM-dd HH:mm · <요청 이름>
#
#   > 요청 원문(줄마다 "> ")
#
#   **받음** · MM-dd HH:mm 회차 N        (질문이면 **답**)
#
#   - 생각 · 할 일 순서
set -e
f="$(cd "$(dirname "$0")/.." && pwd)/docs/valheim/history.md"
[ "$(grep -c '^## 회차 기록$' "$f")" = 1 ] || { echo "history.md에 '## 회차 기록' 줄이 하나가 아님" >&2; exit 1; }
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
printf '\n' > "$tmp"
cat >> "$tmp"
grep -q '[^[:space:]]' "$tmp" || { echo "넣을 내용이 비었음" >&2; exit 1; }
[ -z "$(tail -c1 "$tmp")" ] || echo >> "$tmp"   # 끝 줄바꿈
sed -i "/^## 회차 기록$/r $tmp" "$f"
echo "history.md 맨 위에 넣음: $(sed -n 2p "$tmp" | cut -c1-60)"
