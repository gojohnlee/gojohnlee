#!/usr/bin/env bash
# 발헤임 플러그인 빌드 → 게임 런타임에 없는 어셈블리를 참조하지 않는지 검사 → valheim/dist에 복사.
# 필요: 게임 DLL(/home/user/valheim-refs, 비공개 저장소 gojohnlee/valheim-refs), ilspycmd(dotnet tool install -g ilspycmd).
# 0.1.0에서 System.ValueTuple 참조 때문에 게임에서 플러그인 로드가 깨졌다 → 허용 목록 밖 참조는 실패로 막는다.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/valheim/ValheimHelper/Plugin"
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet build "$PROJ" -c Release -nologo -v q
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
ilspycmd -p -o "$TMP" "$PROJ/bin/Release/ValheimHelper.dll" >/dev/null
bad=$(grep -o 'Reference Include="[^"]*"' "$TMP"/*.csproj | sed 's/.*="\(.*\)"/\1/' |
      grep -vE '^(0Harmony|BepInEx|assembly_.*|UnityEngine(\..*)?|Unity\..*|mscorlib|netstandard|System|System\.Core|System\.Xml|System\.Data)$' || true)
if [ -n "$bad" ]; then
  echo "게임 런타임에 없을 수 있는 참조: $bad" >&2
  exit 1
fi
cp "$PROJ/bin/Release/ValheimHelper.dll" "$ROOT/valheim/dist/"
echo "빌드·참조 검사 통과 → valheim/dist/ValheimHelper.dll"
