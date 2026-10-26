# PC 검증 하네스

사용자 요청 2026-10-26: 기존 루프를 다시 켜지 않고 준비한다. `scripts/vh-loop.ps1`은 다음 회차부터 실제 Claude result의 사용량을 기록한다. 기준 5~10회차 측정 및 호위병 전 선행 시험 순서는 그대로다.

## 준비된 명령

Windows PowerShell 5.1에서 저장소 루트 기준:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness-selftest.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness.ps1 -Action Prepare -BaseCommit <구현-이전-SHA> -Plan scripts\vh-harness-smoke.json
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness.ps1 -Action Test -TaskId <출력된-ID>
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness.ps1 -Action Review -TaskId <같은-ID>
```

Prepare는 깨끗한 커밋에서 빌드·참조 검사·Core 시험을 직접 실행한다. 빌드가 dist를 바꾸면 새 DLL을 커밋한 뒤 다시 준비한다. 기존 구현 작업·게임 프로세스가 돌고 있으면 중단하며 이를 강제 종료하지 않는다.

Test는 전용 te4st41/vhtest만 쓴다. plan의 `sequence`는 한 게임에서 순서·중복을 유지하고, `solo`는 각각 원본을 복원한다. `restart`는 `["vh_restart1","vh_restart2"]`로, 두 번째에만 원본 복원을 끈다. 중단된 묶음은 처음부터 재실행하며, 결과와 원본 해시가 유효한 완료 묶음은 재사용한다. FAIL을 재시도하려면 새 작업 ID를 준비하여 최초 기록을 남긴다.

Review는 로컬 ChatGPT 로그인으로 `gpt-6-astra`를 호출한다. 읽기 전용·고정 JSON 출력이며 실행 전후 소스·DLL·증거 해시를 비교한다. 응답의 작업 ID·커밋·검증 해시가 다르면 무효다. 한도/인증/실행 오류/시간 초과/결과 누락은 별도 상태로, 성공으로 간주하지 않는다. 알 수 없는 한도 해제 시각은 만들지 않는다. 검증 장애 때 다음 작업자는 기존 Claude 방식으로 검토하며 확인된 차단 결함을 유지한다.

## 로컬 기록

- `%USERPROFILE%\vh-loop\usage-records\<실행-ID>.json`: 회차·답 전용 시도별 원본 수치. 없는 수치는 null. `usage.csv`는 이 기록의 색인이다.
- `%USERPROFILE%\vh-loop\harness\<작업-ID>\state.json`: 기준/대상 커밋, 소스·실행 DLL·게임 DLL·시험 원본 해시, 계획, 단계와 다음 행동.
- 같은 작업 폴더의 `handoff.md`, 빌드/Core 로그·TRX, 묶음별 diag/LogOutput/실행 로그, Astra 이벤트·최종 JSON. 원본은 공개 Git에 올리지 않는다.
- `COMPLETE`는 지정한 시험 묶음과 리뷰만 완료했다는 뜻이다. 전체 모드 완성이나 전체 회귀 완료가 아니다.

## 활성화 전에 남은 일

현재는 사용량 수집을 기존 루프에 연결하고 독립 검증 명령을 준비한 단계다. **무인 루프의 `LOOP: HANDOFF` 자동 전환은 아직 연결하지 않았다.** 따라서 다음 Claude 회차는 기존 방식대로 게임 시험까지 한다.

1. 실제 회차에서 사용량 기록을 5~10회 모으고 종류별 기준을 만든다. 모의 시험 기록은 기준에 넣지 않는다.
2. progress의 10-26 변경분 시험 → 호위병 순서를 유지한다.
3. 자동 인계·Claude 수정 반환을 하나의 논리 회차로 연결하고 기존 연속 FAIL/무진척 규칙을 종단 검증한다. 그 전에는 자동 분업 완료로 표시하지 않는다.
4. 필요 구간 디컴파일 캐시·변경분 검색 색인·로그 중복 압축은 이후 측정에 따라 추가한다. 이번 준비에 벡터 DB·로컬 LLM은 도입하지 않았다.

Codex CLI 형식은 설치된 `codex exec --help` 및 [공식 비대화형 실행 문서](https://learn.chatgpt.com/docs/non-interactive-mode)를 확인했다.
