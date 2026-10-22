# 작업 규칙 (이 브랜치 `valheim` = 발헤임 일꾼 NPC 전용)

- 답변은 한국어. 사용자에게 요청할 것이 있으면 항상 답변 맨 끝에 1. 2. 3. 번호 목록으로 정리.
- 사용자는 md·txt 파일을 직접 읽지 않음(history.md만 가끔 읽음) → 안내·체크리스트는 대화에 바로 쓴다.
  GitHub는 앱이 아니라 웹 브라우저로 본다(PC·휴대폰) → 사람이 읽을 파일은 마크다운으로, 휴대폰 화면에서도 읽히게.
  결과물 파일은 대화에 파일로 보내고 GitHub 링크(https://github.com/gojohnlee/gojohnlee/blob/valheim/<경로>)도 준다.
- 사용자 목표: 버그 없고 매끄럽게, 토큰은 최소로. 사용자는 기본적으로 개입하지 않음 → 스스로 진행하고, 새 게임플레이 결정이 필요할 때만 묻는다.
- 새 세션은 이전 대화를 기억하지 못함 → 중요한 상태는 docs/valheim/progress.txt, 결정은 docs/valheim/decisions.txt 끝에 기록.
- 이 브랜치가 저장소 기본 브랜치(2026-10-21). 림월드(철기병 등) 작업과 파일은 `rimworld` 브랜치에만 있다(그쪽 CLAUDE.md). 이 브랜치에는 발헤임만 둔다.

# 발헤임 일꾼 NPC

- 이 브랜치에서만 작업하고 푸시도 `valheim`으로(사용자 승인, 세션 작업 브랜치와 별개).
- 새 세션 시작: docs/valheim/UserRequest.txt에 UserReply.txt 답("요청:")이 없는 요청이 있으면 그 답부터 써서 푸시(형식은 scripts/vh-loop-prompt.txt 0번).
  그다음 docs/valheim/progress.txt(지금·다음)와 Errors.txt(미해결 문제) → 코드는 codemap.txt에서 찾아 그 부분만 읽는다. 파일은 통째로 읽지 말고 grep.
  기획 결정은 decisions.txt, 구상서 concept-v1.txt·설계서 design-depart-craft.txt는 그 장만, 지난 기록 history.md와 해결된 문제 Errors-resolved.txt는 필요할 때만 grep.
  무인 루프 설계는 loop-plan.txt. PC 세션 시작 순서·게임 시험 방법은 progress.txt 맨 위.
- 게임 DLL: PC 세션은 설치된 게임의 valheim_Data\Managed(csproj 자동). 클라우드 세션은 비공개 저장소 gojohnlee/valheim-refs → add_repo 후 /home/user/valheim-refs에 clone.
  ilspycmd로 디컴파일해 읽는다. 확인한 게임 사실은 codemap.txt "확인된 게임 사실"에 적고 다시 디컴파일하지 않는다.
- 게임 파일·디컴파일 결과는 저장소에 올리지 않는다. 저장소 전체 비공개 전환 금지(발헤임 dll을 raw.githubusercontent 주소로 받기 때문).
- 비용 0 원칙: 유료 API 없음. 위험 판단·이동·전투는 규칙 코드, 명령 해석은 모드 → Haiku → Sonnet 3단계(호스트 PC의 Claude Code). 자세한 건 decisions.txt.
- 버그 최소화: 게임 무관 로직은 Core로 분리해 dotnet test, 게임 내부는 최신 DLL 실제 코드를 읽고 맞춤(추측 금지), 행동마다 시간 제한·대체 동작, 단계마다 코드 리뷰.
- 노력 수준: 메인 세션은 보통. 게임 시험 FAIL·로그 예외·이상 동작처럼 원인 찾기가 필요한 버그는 `vh-debug`(엑스트라 하이)에 증상(시험 이름·diag·로그 발췌)을 붙여 넘기고,
  결과는 메인이 diff 검토 후 커밋. 분량 있는 정형 작업은 `vh-standard`/`vh-simple`. 몇 줄짜리 명백한 수정은 메인이 직접.
- 커밋: 빌드·테스트 통과 때마다 커밋·푸시하고 progress.txt의 "지금"과 "다음"만 고친다. dist DLL도 커밋.
  기록: 끝낸 일은 history.md 맨 위 "회차 기록"에 `bash scripts/vh-history-add.sh`로 넣는다(history.md는 읽거나 열지 않음, 맨 위가 최근,
  사용자가 GitHub에서 읽으니 쉬운 말로 제목·요약·자세히 — 꼴은 그 스크립트 머리 주석).
  progress "지금"에는 같은 요약 한 줄을 맨 위에 넣고 최근 3개만 둔다(넘친 맨 아래 것은 지움 — history에 이미 있음).
