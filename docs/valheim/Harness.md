# PC 검증 하네스

Claude가 구현한 커밋을 **스크립트가** 빌드·Core·게임 시험하는 장치입니다. 모델은 쓰지 않습니다(사용자 10-27: GPT 뺌). 시험이 하나라도 실패하면 스크립트가 실패 줄을 원문 그대로 발췌합니다.
처음 만든 것은 PC Codex(10-26, 커밋 553e59d~c9dfd23)이고, 루프 연결과 마무리는 PC Claude 세션(10-26)이 했습니다.

## 지금 상태

- **꺼짐(사용자 10-27, 다시 켜지 않음):** 한 쌍 실측에서 토큰이 안 줄었고 위험만 늘어 `handoff-on.txt`를 지움. 코드는 휴면으로 남김. 아래는 켰을 때의 동작입니다.
- 루프가 회차마다 스위치를 봅니다. `docs/valheim/handoff-on.txt`가 있고, 분업 전 실제 회차 사용량 기록이 5개 이상일 때만 켜집니다. 상태는 status.txt "분업" 줄에 나옵니다.
- 켜지면 회차에 `VH_HANDOFF=1`이 갑니다. 회차는 게임 시험을 직접 하지 않고 시험 계획(`%USERPROFILE%\vh-loop\handoff-plan.json`)을 남긴 뒤 `LOOP: HANDOFF`로 끝냅니다(scripts/vh-loop-handoff.txt).
- 그러면 루프가 `Invoke-VhHandoff`로 준비(빌드·Core·자가 시험) → 게임 시험 → 실패 발췌 → 요약을 차례로 돌립니다. 모델 세션은 기다리지 않습니다. 요약은 다음 회차에 `VH_VERIFY`(vh-loop-handoff.txt)로 넘어갑니다.
- 셈: 구현 회차와 그 검증을 한 묶음으로 봅니다. 검증이 통과하면 다음 회차가 `[끝]`을 적을 때 진척으로 칩니다. 통과 못 하면 "끝낸 것 없음"이 됩니다. 같은 시험이 검증에서 3번 연속 FAIL이면 멈춥니다.
- 인계 도중 루프가 꺼지면 다음 회차에 "결과 없음, PASS 아님"을 알립니다(`handoff-running.json`).

## 손으로 돌리기

Windows PowerShell 5.1에서 저장소 루트 기준으로 실행합니다. 루프가 돌고 있으면 거부됩니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness-selftest.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness.ps1 -Action Prepare -BaseCommit <구현-이전-SHA> -Plan scripts\vh-harness-smoke.json
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness.ps1 -Action Test -TaskId <ID>
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness.ps1 -Action Review -TaskId <ID>
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\vh-harness.ps1 -Action Summary -TaskId <ID>
```

- **Prepare:** 깨끗한 커밋에서 빌드(`--no-dist`)·참조 검사·Core·자가 시험을 합니다. 실행할 DLL·게임 DLL·시험 월드 원본의 해시를 고정합니다. 계획의 `requirements`는 `"경로: 절 제목 글자"` 꼴이고, 그 절만 리뷰 자료에 들어갑니다.
- **Test:** 전용 te4st41/vhtest만 씁니다.
  - `sequence`: 한 게임에서 순서대로 돕니다.
  - `solo`: 시험마다 원본을 복원합니다.
  - `restart`: vh_restart1 → vh_restart2이고, 두 번째는 원본을 복원하지 않습니다.
  - 중단된 묶음은 새 작업으로 처음부터 다시 합니다.
  - 알려진 무해 줄(BepInEx "Unable to start Unity log writer")은 오류로 치지 않습니다. 정확히 그 줄만입니다.
  - 저장 뒤 게임 종료가 멈추면(E038) 2분 뒤 끄고 `exit_hang`만 표시합니다.
- **Review(이름만 남음, 실제로는 실패 발췌):** 모델을 부르지 않습니다. 모두 PASS면 NOT_NEEDED로 COMPLETE. 아니면 `excerpt-N\excerpt.md`에 PASS 아닌 섹션의 diag 줄(FAIL·판정·환경 불량·Exception과 앞뒤 한 줄, 섹션당 15줄, 못 찾으면 끝 15줄)과 LogOutput.log 오류 줄(같은 줄 묶음)을 `경로:줄: 원문`으로 적고 FIX. 요약(result.md)에 그대로 들어갑니다.

## 로컬 기록 (공개 Git에 안 올림)

- `%USERPROFILE%\vh-loop\usage-records\<실행-ID>.json` + `usage.csv`: Claude 회차·답 전용 세션의 실제 수치입니다. 없는 수치는 null입니다. `mode`는 claude-only(기준)와 handoff로 나뉩니다.
- `%USERPROFILE%\vh-loop\harness\<작업-ID>\`: state.json, handoff.md, result.md(요약), 빌드·Core 로그, 묶음별 diag·LogOutput·run.json, excerpt-N(실패 발췌).
- `%USERPROFILE%\vh-loop\handoff-records\<작업-ID>.json`: 단계별 걸린 분입니다(분업 전후 비교용).

## 남은 일

1. 루프를 다시 켜서 실제 회차 사용량 기록 5개 이상 모으기(지금 0개 — 루프가 10-25부터 멈춤).
2. progress ★ 순서대로 진행하다 분업 ★에서 기준을 정리하고 `handoff-on.txt`를 만들기 → 첫 실제 HANDOFF 왕복과 FIX 반환을 확인.
3. 디컴파일 캐시·검색 색인·로컬 모델은 측정해서 필요할 때만 넣습니다(지금은 안 넣음).
