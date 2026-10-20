# 작업 규칙

- 사용자는 **RimWorld 1.7** 사용. 1.7은 .NET 10 기반(`net10.0`), 1.6 이하(`net472`)와 다름.
  - 참조 패키지: `Krafs.Rimworld.Ref` 1.7.x(현재 unstable 빌드만 있음). 게임 빌드 번호에 맞출 것.
  - 모드 폴더 구조: `rimworld/<ModName>/1.7/Assemblies/`, `About/About.xml`의 supportedVersions는 1.7.
- 빌드: `./scripts/setup-dotnet.sh` 후 `dotnet build ... -c Release` (dot.net 설치 스크립트는 네트워크 정책상 막힘 → apt 사용).
- 사용자에게 요청할 것이 있으면 **항상 답변 맨 끝에 1. 2. 3. 4. 번호 목록**으로 정리.
- 답변은 한국어로.
- 새 모드: `./scripts/new-rimworld-mod.sh <Name> "<설명>"` (CI는 `rimworld/*` 폴더를 자동 감지해 각각 빌드).
- 사용자가 PC에서 확인할 항목은 `docs/HOME_CHECKLIST.md`에 모아둔다.
