#!/usr/bin/env bash
# docs/valheim/history.txt "=== 회차 기록" 줄 바로 밑(맨 위 = 최근)에 표준 입력을 끼워 넣는다.
# history.txt는 크니 읽거나 열지 말고 이것만 쓴다. 예:
#   bash scripts/vh-history-add.sh <<'EOF'
#   [루프 회차 N 2026-MM-DD] 한 일·원인·시험 결과(E00x면 그 ID와 새로 확인한 사실)
#   EOF
set -e
f="$(cd "$(dirname "$0")/.." && pwd)/docs/valheim/history.txt"
[ "$(grep -c '^=== 회차 기록' "$f")" = 1 ] || { echo "history.txt에 '=== 회차 기록' 줄이 하나가 아님" >&2; exit 1; }
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
cat > "$tmp"
grep -q '[^[:space:]]' "$tmp" || { echo "넣을 내용이 비었음" >&2; exit 1; }
[ -z "$(tail -c1 "$tmp")" ] || echo >> "$tmp"   # 끝 줄바꿈
echo >> "$tmp"                                  # 다음(더 옛) 기록과 빈 줄로 나눔
sed -i "/^=== 회차 기록/r $tmp" "$f"
echo "history.txt 맨 위에 넣음: $(head -1 "$tmp" | cut -c1-60)"
