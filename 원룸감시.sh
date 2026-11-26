#!/usr/bin/env bash
# 바탕화면 원룸 폴더를 살펴 Gemini가 새로 넣거나 늘어난 txt를 house 브랜치 inbox/로 올린다(원본은 안 옮김)(사용자 11-26, PC 예약 작업이 30분마다 실행).
# 출력: NONE(새것 없음) / UP <파일>(올림) / FORMAT <파일>(### 꼴 아님 — Claude가 보고 판단) / PUSHED / PUSHFAIL
SRC="${SRC:-/c/Users/temp/Desktop/원룸}"; REPO="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$SRC/올림"; SEEN="$SRC/올림/.seen"; touch "$SEEN"
now=$(date +%s); n=0; out=""
shopt -s nullglob
for f in "$SRC"/*.txt; do
  [ -s "$f" ] || continue
  [ $((now - $(stat -c %Y "$f"))) -ge 120 ] || continue      # 아직 쓰는 중일 수 있음
  h=$(sha1sum "$f" | cut -c1-40); grep -q "$h" "$SEEN" && continue
  b=$(basename "$f")
  if grep -q '^### ' "$f"; then
    cp "$f" "$REPO/inbox/$b"   # 원본은 그대로 둔다(Gemini가 이어 쓰는 중일 수 있음). 같은 이름이라 inbox엔 최신본 하나만, git은 늘어난 줄만 올린다
    out+="UP $b"$'\n'; n=$((n+1))
  else out+="FORMAT $f"$'\n'; fi
  echo "$h $b $(date +%F_%T)" >> "$SEEN"
done
[ -z "$out" ] && { echo NONE; exit 0; }
printf '%s' "$out"
if [ $n -gt 0 ]; then
  cd "$REPO" && git add inbox && git commit -qm "원룸 폴더 Gemini 결과 ${n}개 올림(PC 감시)" \
    && { git pull -q --rebase origin house; git push -q origin HEAD:house && echo PUSHED || echo PUSHFAIL; }
fi
