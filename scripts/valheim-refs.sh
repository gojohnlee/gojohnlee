#!/usr/bin/env bash
# 발헤임 참조 DLL(공개 NuGet, 함수 이름만 있음)과 Jötunn을 받아 API 이름 목록을 만든다.
# 게임 파일이 아니라 공개 패키지라서 저장소에 올리지 않고 매번 받는다.
# 사용: ./scripts/valheim-refs.sh [출력 폴더]   (버전 바꾸기: VH_VER=… JT_VER=… ./scripts/valheim-refs.sh)
set -euo pipefail
OUT="${1:-${TMPDIR:-/tmp}/valheim-refs}"
VH_VER="${VH_VER:-0.221.4}"
JT_VER="${JT_VER:-2.30.2}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$OUT"

fetch() { # <nuget id> <버전>
  local dir="$OUT/$1"
  mkdir -p "$dir"
  curl -sS -o "$dir/pkg.nupkg" "https://api.nuget.org/v3-flatcontainer/$1/$2/$1.$2.nupkg"
  unzip -q -o "$dir/pkg.nupkg" -d "$dir/x"
}

fetch valheimgamelibs "$VH_VER"
fetch jotunnlib "$JT_VER"
dotnet run --project "$ROOT/tools/apidump" -c Release -- "$OUT/valheimgamelibs/x/lib/assembly_valheim.dll" > "$OUT/valheim-api.txt"
dotnet run --project "$ROOT/tools/apidump" -c Release -- "$OUT/jotunnlib/x/lib/net462/Jotunn.dll" > "$OUT/jotunn-api.txt"
echo "완료: $OUT/valheim-api.txt, $OUT/jotunn-api.txt"
