#!/usr/bin/env bash
# 발헤임 플러그인 빌드 → 게임 런타임에 없는 어셈블리를 참조하지 않는지 검사 → valheim/dist에 복사.
# 필요: 게임 DLL(/home/user/valheim-refs, 비공개 저장소 gojohnlee/valheim-refs), ilspycmd(dotnet tool install -g ilspycmd).
# 0.1.0에서 System.ValueTuple 참조 때문에 게임에서 플러그인 로드가 깨졌다 → 허용 목록 밖 참조는 실패로 막는다.
set -euo pipefail
MODE="${1:-}"
if [[ -n "$MODE" && "$MODE" != "--no-dist" ]]; then echo "usage: $0 [--no-dist]" >&2; exit 2; fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/valheim/ValheimHelper/Plugin"
export PATH="$PATH:$HOME/.dotnet/tools"
# 모드 두 개(사용자 11-15): 본 모드 ValheimHelper.dll(친구들에게 주는 것) + 시험 모드 ValheimHelperTest.dll(시험 PC만). 시험 모드 빌드가 본 모드도 같이 빌드한다.
TPROJ="$ROOT/valheim/ValheimHelper/TestPlugin"
dotnet build "$TPROJ" -c Release -nologo -v q
# 본 모드에 시험·진단 클래스가 남으면 실패(연결 지점 TestHook·Diag, Core의 TestSpots만 허용)
classes=$(ilspycmd -l c "$PROJ/bin/Release/ValheimHelper.dll" 2>/dev/null | tr -d '\r' | grep -E '^Class ' || true)
echo "$classes" | grep -qx 'Class ValheimHelper.Plugin' || { echo "본 모드 클래스 목록을 못 읽음(ilspycmd -l c)" >&2; exit 1; }
left=$(echo "$classes" | grep -E 'Test|Diag|HouseCopy|HouseBase|AutoStart|Bare|Minimized' |
       grep -vxE 'Class ValheimHelper\.(TestHook|Diag|Core\.Work\.TestSpots)' || true)
if [ -n "$left" ]; then echo "본 모드에 시험·진단 클래스가 남음: $left" >&2; exit 1; fi
if grep -qa 'ValueTuple' "$TPROJ/bin/Release/ValheimHelperTest.dll"; then echo "시험 모드가 System.ValueTuple을 씀(튜플 문법)" >&2; exit 1; fi
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
# 메서드 몇 개 디컴파일 오류(ILSpy 버그, 회차 184 BlockTest·WeakTest.Run)는 참조 검사와 무관 → csproj만 있으면 됨
ilspycmd -p -o "$TMP" "$PROJ/bin/Release/ValheimHelper.dll" >/dev/null 2>&1 || true
ls "$TMP"/*.csproj >/dev/null 2>&1 || { echo "참조 검사용 디컴파일 실패(csproj 없음)" >&2; exit 1; }
bad=$(grep -o 'Reference Include="[^"]*"' "$TMP"/*.csproj | sed 's/.*="\(.*\)"/\1/' |
      grep -vE '^(0Harmony|BepInEx|assembly_.*|UnityEngine(\..*)?|Unity\..*|mscorlib|netstandard|System|System\.Core|System\.Xml|System\.Data)$' || true)
# 11-14: 튜플 필드(HashSet<(A,B)>)는 디컴파일 csproj 참조에 안 잡히는데 게임에선 System.ValueTuple을 찾다 Commands가 통째로 안 불러와짐 → DLL 안 이름으로도 막는다.
if grep -qa 'ValueTuple' "$PROJ/bin/Release/ValheimHelper.dll"; then
  bad="${bad:+$bad }System.ValueTuple(튜플 문법 — 튜플 대신 클래스·배열·집합)"
fi
if [ -n "$bad" ]; then
  echo "게임 런타임에 없을 수 있는 참조: $bad" >&2
  exit 1
fi
if [[ "$MODE" != "--no-dist" ]]; then
  cp "$PROJ/bin/Release/ValheimHelper.dll" "$TPROJ/bin/Release/ValheimHelperTest.dll" "$ROOT/valheim/dist/"
fi
echo "빌드·참조 검사 통과 (dist 복사 제외: ${MODE:-아니오})"
