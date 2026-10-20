# 작업 규칙

- 사용자는 **RimWorld 1.7** 사용. 1.7은 .NET 10 기반(`net10.0`), 1.6 이하(`net472`)와 다름.
  - 참조 패키지: `Krafs.Rimworld.Ref` 1.7.x(현재 unstable 빌드만 있음). 게임 빌드 번호에 맞출 것.
  - 모드 폴더 구조: `rimworld/<ModName>/1.7/Assemblies/`, `About/About.xml`의 supportedVersions는 1.7.
- 빌드: `./scripts/setup-dotnet.sh` 후 `dotnet build ... -c Release` (dot.net 설치 스크립트는 네트워크 정책상 막힘 → apt 사용).
- 사용자에게 요청할 것이 있으면 **항상 답변 맨 끝에 1. 2. 3. 4. 번호 목록**으로 정리.
- 답변은 한국어로.
- 현재 프로젝트: **철기병 모드** (메카 관리 모드는 제외). 기획서 `docs/ironcavalry/spec-v2.txt`, 외형 참고 이미지 `docs/ironcavalry/reference.png`. 사용자가 구현 지시하기 전에는 모드를 만들지 않는다(기획 단계).
- 새 세션은 기본 브랜치에서 시작하고 이전 대화 내용은 기억하지 못함 → 중요한 결정·상태는 이 파일에 기록한다.
- 새 모드: `./scripts/new-rimworld-mod.sh <Name> "<설명>"` (CI는 `rimworld/*` 폴더를 자동 감지해 각각 빌드).
- 사용자는 md 파일을 직접 읽지 않음 → 안내·체크리스트는 항상 대화에 바로 쓴다.
- 사용자는 경로만 적힌 파일을 열 수 없음 → 기획서 등 결과물 파일은 대화에 파일로 직접 보내고, GitHub 링크(https://github.com/gojohnlee/gojohnlee/blob/HEAD/<경로>)도 함께 준다.
- PC 테스트는 자동화 스크립트 한 줄로: `irm https://raw.githubusercontent.com/gojohnlee/gojohnlee/HEAD/scripts/rimworld-test.ps1 | iex` (설치→실행→diag.txt/로그 수집→클립보드).

# 작업 분배 (토큰 최소화)

사용자 목표: 버그 없고 매끄럽게, 토큰은 최소로. 사용자는 기본적으로 개입하지 않음 → 스스로 진행하고, 새 게임플레이 결정이 필요할 때만 묻는다.

- 메인 세션(높은 모델): 설계, 돌격 상태 관리·순접근량·자동 후퇴 이득 판정·스턴/면역, Harmony 패치 위치 선정, 1.7 API 조사, 성능 설계, 보조 에이전트 결과 최종 검토.
- `rw-standard`(Sonnet): XML Def, 습격 편성 Patch, 정형화된 C#(명령 버튼·설정·저장 필드), 텍스처 다듬기.
- `rw-simple`(Haiku): Languages 한국어 문자열, About.xml, 파일 정리, 빌드 실행·오류 로그 요약.
- 위임 여부는 메인이 판단한다. 보조 에이전트는 매번 맥락을 새로 읽으므로, 몇 줄짜리 수정이나 파일 1~2개짜리 작은 일은 메인이 직접 한다. 분량 있는 단순 작업만 넘긴다.
- 보조 에이전트에는 바꿀 파일·기획서 항목·완료 기준을 구체적으로 준다. 결과는 메인이 diff 검토 + 빌드 확인 후 커밋한다.

