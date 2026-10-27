# 작업 규칙 (브랜치 `valheim` = 발헤임 일꾼 NPC 전용, 저장소 기본 브랜치. 림월드는 `rimworld` 브랜치에만)

- 답은 한국어. 사용자에게 요청할 것은 답 맨 끝에 1. 2. 3. 목록으로.
- 사용자는 md·txt를 직접 안 읽음(history.md만 가끔) → 안내는 대화에 바로. GitHub는 웹(PC·휴대폰)으로 봄 → 사람이 읽을 파일은 휴대폰에서도 읽히는 마크다운,
  결과물은 대화에 파일로 + 링크 https://github.com/gojohnlee/gojohnlee/blob/valheim/<경로>.
- 목표: 버그 없고 매끄럽게, **토큰 최소**(10-27: 턴마다 대화 전체를 다시 읽으니 턴 수·읽는 양을 줄임 — 명령은 묶고, 출력은 짧게, 파일은 grep으로 필요한 부분만).
  토큰·비용 건의·문의엔 docs/valheim/token-report.md(실측, PC에서 scripts/vh-token-report.ps1)의 수치와 만든 날짜로 답하고, 측정 없는 건 추정이라 밝힌다.
  사용자는 개입하지 않음 → 스스로 진행, 새 게임플레이 결정만 묻는다.
- 새 세션은 기억이 없음 → 상태는 docs/valheim/progress.txt, 결정은 decisions.txt 맞는 절(어긋나는 옛 줄은 지우고 구상서·설계서·codemap도 맞춤).
- 푸시는 `valheim`으로(사용자 승인).

# 발헤임 일꾼 NPC

- 시작: docs/valheim/UserRequest.txt 구분선 아래 요청부터(꼴은 scripts/vh-loop-request.txt) → progress.txt "지금"·"다음" → Errors.txt 제목 줄 → 코드는 codemap.txt로 찾아 그 부분만.
  decisions.txt·concept-v1.txt·design-depart-craft.txt·history.md·Errors-resolved.txt는 필요할 때 grep. 루프는 loop-plan.txt, PC 시작·게임 시험 방법은 docs/valheim/pc-howto.txt.
- 게임 DLL: PC는 설치된 게임 valheim_Data\Managed(csproj 자동), 클라우드는 비공개 gojohnlee/valheim-refs를 /home/user/valheim-refs에 clone. ilspycmd로 디컴파일,
  확인한 사실은 codemap "확인된 게임 사실"에 적고 다시 디컴파일하지 않는다. 게임 파일·디컴파일 결과는 올리지 않음, 저장소 비공개 전환 금지(dll을 raw 주소로 받음).
- 비용 0: 유료 API 없음. 위험·이동·전투는 규칙 코드, 명령 해석은 모드 → Haiku → Sonnet(호스트 PC의 Claude Code).
- 버그 최소화: 게임 무관 로직은 Core + dotnet test, 게임 내부는 실제 DLL 코드대로(추측 금지), 행동마다 시간 제한·대체 동작.
- 노력: 메인은 보통. 서브에이전트는 비싸니 아낀다 — 원인 찾기는 메인이 먼저 한 번, 그래도 모르는 버그만 `vh-debug`(증상·시험 이름·diag·로그 발췌를 붙여), 결과 diff는 메인이 검토.
- 커밋: 빌드·테스트 통과 때마다 커밋·푸시(dist DLL 포함), progress "지금"·"다음"만 고친다(남은 일만). 끝낸 일은 `bash scripts/vh-history-add.sh`로
  history.md 맨 위에(열지 않음, 쉬운 말 제목·요약·자세히), "지금"에 같은 한 줄을 맨 위로, 최근 3개만.
