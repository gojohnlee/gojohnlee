# PC 검증 하네스

Claude가 구현한 커밋을 **스크립트가** 빌드·Core·게임 시험하고 **Astra(gpt-6-astra, PC Codex 앱의 codex exec)** 가 리뷰하는 장치입니다. 규칙은 decisions.txt 2절 "GPT 검증 분업"을 따릅니다.
처음 만든 것은 PC Codex(10-26, 커밋 553e59d~c9dfd23)이고, 루프 연결과 마무리는 PC Claude 세션(10-26)이 했습니다.

## 지금 상태

- **꺼져 있음.** 루프가 회차마다 스위치를 봅니다. `docs/valheim/handoff-on.txt`가 있고, 분업 전 실제 회차 사용량 기록이 5개 이상이고, Astra 쉼이 아닐 때만 켜집니다. 상태는 status.txt "분업" 줄에 나옵니다.
- 켜지면 회차에 `VH_HANDOFF=1`이 갑니다. 회차는 게임 시험을 직접 하지 않고 시험 계획(`%USERPROFILE%\vh-loop\handoff-plan.json`)을 남긴 뒤 `LOOP: HANDOFF`로 끝냅니다(scripts/vh-loop-handoff.txt).
- 그러면 루프가 `Invoke-VhHandoff`로 준비(빌드·Core·자가 시험) → 게임 시험 → Astra 리뷰 → 요약을 차례로 돌립니다. 모델 세션은 기다리지 않습니다. 요약은 다음 회차에 `VH_VERIFY`(vh-loop-handoff.txt)로 넘어갑니다.
- **Astra 한도·오류(사용자 10-26):** 그 검증은 "Astra 못 씀"이 되고 다음 Claude 회차가 직접 리뷰합니다. 한도 문구의 "try again at" 시각까지 분업을 끕니다(문구에 없으면 3·6·12·24시간 뒤 다시 시도). 그동안은 예전처럼 Claude가 시험·리뷰를 다 합니다.
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
- **Review:** 실행 파일은 PATH에서 찾고, 없으면 `%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe` 중 가장 최근 것을 씁니다. 읽기 전용 고정 JSON입니다. 작업 ID·커밋·대상 해시가 다르면 무효입니다. 소스·DLL·증거가 바뀌면 MUTATION입니다.
- **리뷰 자료:** 대상 확인 정보, diff 앞뒤 30줄, 지정 절, 관련 Errors 항목, 같은 줄을 묶은 로그 요약만 넣습니다(10-26: 32만 → 고정분 약 9KB + diff).

## 로컬 기록 (공개 Git에 안 올림)

- `%USERPROFILE%\vh-loop\usage-records\<실행-ID>.json` + `usage.csv`: Claude 회차·답 전용 세션의 실제 수치입니다. 없는 수치는 null입니다. `mode`는 claude-only(기준)와 handoff로 나뉩니다.
- `%USERPROFILE%\vh-loop\harness\<작업-ID>\`: state.json, handoff.md, result.md(요약), 빌드·Core 로그, 묶음별 diag·LogOutput·run.json, review-N(packet·events·usage).
- `%USERPROFILE%\vh-loop\handoff-records\<작업-ID>.json`: 단계별 걸린 분과 Astra 사용량입니다(분업 전후 비교용).

## 남은 일

1. 루프를 다시 켜서 실제 회차 사용량 기록 5개 이상 모으기(지금 0개 — 루프가 10-25부터 멈춤).
2. progress ★ 순서대로 진행하다 분업 ★에서 기준을 정리하고 `handoff-on.txt`를 만들기 → 첫 실제 HANDOFF 왕복과 FIX 반환을 확인.
3. 디컴파일 캐시·검색 색인·로컬 모델은 측정해서 필요할 때만 넣습니다(지금은 안 넣음).
