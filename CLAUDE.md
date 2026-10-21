# 림월드(철기병) 유지보수 안내 (2026-10-21 기본 브랜치 변경)

- 저장소 기본 브랜치는 이제 `valheim`(발헤임 전용). 림월드 작업 브랜치는 이 브랜치 `ccr-e7be70c2-61zhas`.
  다른 `ccr-…` 브랜치(ccr-792e9e5e-c1w0yh, ccr-9c010643-eim06w)는 예전 세션 브랜치라 기준으로 쓰지 않는다.
- 사용자가 림월드 수정을 요청하면:
  1. 새 세션은 `valheim`에서 시작하므로 `git fetch origin ccr-e7be70c2-61zhas && git checkout ccr-e7be70c2-61zhas` 후 이 파일부터 읽는다.
  2. 작업·커밋·푸시는 이 브랜치로 한다. `valheim` 브랜치에는 림월드 내용을 넣지 않는다(반대로 여기엔 발헤임 내용을 넣지 않는다).
  3. 첫 작업으로 `raw.githubusercontent.com/gojohnlee/gojohnlee/HEAD/` 주소를 `…/ccr-e7be70c2-61zhas/`로 바꾼다.
     `HEAD`가 이제 `valheim`을 가리켜 림월드 테스트 한 줄 명령이 404가 난다. 고칠 곳: 이 파일, README.md, scripts/rimworld-test.ps1 3번 줄.
     GitHub 링크 `blob/HEAD/<경로>`도 `blob/ccr-e7be70c2-61zhas/<경로>`로 준다.
  4. CI(rimworld-build.yml)는 `rimworld/**`가 바뀐 push마다 빌드하고 `rimworld-latest` 릴리스를 갱신한다(테스트 스크립트가 이 릴리스 zip을 받음).
     Actions 수동 실행 버튼은 기본 브랜치에 워크플로가 없으면 안 보일 수 있다 → push로 빌드한다.
- 저장소 전체 비공개 전환 금지(발헤임 dll을 raw 주소로 받기 때문).

# 작업 규칙

- 사용자는 **RimWorld 1.7** 사용. 1.7은 .NET 10 기반(`net10.0`), 1.6 이하(`net472`)와 다름.
  - 참조 패키지: `Krafs.Rimworld.Ref` 1.7.x(현재 unstable 빌드만 있음). 게임 빌드 번호에 맞출 것.
  - 모드 폴더 구조: `rimworld/<ModName>/1.7/Assemblies/`, `About/About.xml`의 supportedVersions는 1.7.
- 빌드: `./scripts/setup-dotnet.sh` 후 `dotnet build ... -c Release` (dot.net 설치 스크립트는 네트워크 정책상 막힘 → apt 사용).
- 사용자에게 요청할 것이 있으면 **항상 답변 맨 끝에 1. 2. 3. 4. 번호 목록**으로 정리.
- 답변은 한국어로.
- 현재 프로젝트: **철기병 모드** (메카 관리 모드는 제외). 기획서 `docs/ironcavalry/spec-v2.txt`(끝에 구현 메모), 외형 참고 `docs/ironcavalry/reference.png`. 모드 위치 `rimworld/IronCavalry`. 1차 구현 완료, 게임 내 검증 대기.
- 발헤임 일꾼 NPC 작업은 `valheim` 브랜치에서만 한다(`git fetch origin valheim && git checkout valheim`, 그 브랜치의 CLAUDE.md와 `docs/valheim/progress.txt`부터 읽기). 푸시도 `valheim` 브랜치로 한다(사용자 승인). 이 브랜치에는 발헤임 내용을 넣지 않는다.
- 바닐라 XML Def 조회: `python3 -I scripts/vanilla-defs.py '<정규식>'` (추측 대신 이걸로 확인). 1.7 게임 API는 Krafs.Rimworld.Ref 참조 어셈블리로 빌드해 확인. 게임 API 이름·특성 확인 도구는 `tools/` (README 참고).
- 새 세션은 기본 브랜치(`valheim`)에서 시작하고 이전 대화 내용은 기억하지 못함 → 중요한 결정·상태는 이 파일에 기록한다.
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


# 다음 세션 인수인계 (2026-10-20)

사용자가 "철기병 테스트 시작"이라고 하면:
1. 사용자에게 PC PowerShell에서 `irm https://raw.githubusercontent.com/gojohnlee/gojohnlee/HEAD/scripts/rimworld-test.ps1 | iex` 실행을 안내(철기병 + 테스트 모드 설치·실행·로그 수집 → 클립보드). 모드 순서: Harmony → Iron Cavalry.
2. 붙여넣은 로그에서 빨간 오류부터 처리. 그다음 기획서 A13 검증 항목을 개발자 모드로 확인
   (철기병 소환, C 단축키 돌격, 개발자 모드 로그 `[IronCavalry] … 명중 True/False`, 스턴, 체력 탭·전투 기록 한국어, 그림 크기 drawSize 3.6).
3. 결과·수정 사항은 기획서 끝 "구현 메모"에 이어 적는다.
- 미확정 기본안(사용자 미답): 적 등장 가중치 = 센티페드 블래스터의 절반(5), 메카 클러스터 제외.
- 사용자가 PC 화면 제어를 허용하면 직접 실행·확인해도 된다.
