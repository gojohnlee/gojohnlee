#!/usr/bin/env bash
# 사용법: ./scripts/new-rimworld-mod.sh MyModName ["모드 설명"]
# HelloRimWorld 템플릿을 복사해 새 1.7 모드를 만든다.
set -euo pipefail
NAME="${1:?모드 이름(영문 PascalCase)을 주세요}"
DESC="${2:-$NAME}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/rimworld/HelloRimWorld"
DST="$ROOT/rimworld/$NAME"
[ -e "$DST" ] && { echo "이미 있음: $DST"; exit 1; }

mkdir -p "$DST"
cp -r "$SRC/." "$DST/"
rm -rf "$DST"/Source/*/obj "$DST"/Source/*/bin; find "$DST" -name "*.dll" -delete
mv "$DST/Source/HelloRimWorld" "$DST/Source/$NAME"
mv "$DST/Source/$NAME/HelloRimWorld.csproj" "$DST/Source/$NAME/$NAME.csproj"
mv "$DST/Source/$NAME/HelloRimWorldMod.cs" "$DST/Source/$NAME/${NAME}Mod.cs"

LOWER="$(echo "$NAME" | tr '[:upper:]' '[:lower:]')"
grep -rl HelloRimWorld "$DST" | xargs sed -i "s/HelloRimWorld/$NAME/g"
sed -i "s/gojohnlee\.hellorimworld/gojohnlee.$LOWER/; s#<name>Hello RimWorld</name>#<name>$NAME</name>#; s#<description>.*</description>#<description>$DESC</description>#" "$DST/About/About.xml"
sed -i "s/Hello RimWorld 모드가 작동 중입니다!/$NAME 로드 완료/" "$DST/Source/$NAME/${NAME}Mod.cs"
echo "생성: $DST"
