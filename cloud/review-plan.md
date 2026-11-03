# 인물별 말투·말맛 검수 — 새 세션용 작업서 (사용자 결정 2026-11-02)

**시작 조건**: 사용자가 "검수 시작"류로 시킬 때. 그 전 상태 = ChatGPT Sol이 전체 번역 + 앞부분(0~4,645번째 줄, 지침 보강 전 번역)
재번역까지 끝냄. 확인: `cloud/status.md`가 100% 근처이고, `translation/retrans-old/`의 앞부분이 새 auto 파일로 다시 덮였는지.
**하지 않기로 한 것**: 기계 검사 단계(금지어 등은 검수 안에서 함께), 전체 Opus 재번역, 두 모델 이중 번역.

## 자료 (이 저장소 = 비공개 gojohnlee/valheim-refs, 브랜치 necromancer)
- 접근: 세션에 저장소가 없으면 add_repo(gojohnlee/valheim-refs) 후 `git clone --depth 1 -b necromancer`. 공개 저장소(gojohnlee/gojohnlee)에는 게임 글을 올리지 않는다.
- 영어 원문: `translation/Korean-fixed.csv`(앞 3줄 머리말 뒤 CSV, 열 ID·EN·Korean). 줄 순서·화자: `translation/dump/articy-dump.tsv`(열 ID·종류·화자…).
  순서 만드는 코드는 `cloud/chunk.py`의 `load()`(by, order, speaker, done, skip) — 그대로 import해서 쓴다.
- 현재 한국어: `translation/retrans/*.tsv`(ID<TAB>문장, 단락 " / "). 같은 ID가 여러 파일에 있으면 **파일 이름 순서로 뒤 파일이 이김**
  (PC의 scripts/fix-csv.ps1이 Get-ChildItem 이름순으로 덮어씀) → 검수 결과는 `review-<인물>.tsv`로 두면 auto-*·인물 통일본보다 뒤라 이긴다.
- 기준: `cloud/translate-guide.md`(지침·정해진 말투 표 — 최우선), `cloud/context/characters.md`(인물 125명: 관계 변화·말투), `cloud/context/wNN.md`(1,000줄 구간 줄거리).
- 비교 근거: `cloud/compare/16265-비교.md` — Sol은 처음 만난 사람에게 반말·말장난을 밋밋하게, Sonnet은 "그녀/당신"을 쓰는 버릇.

## 절차
1. 인물별로 줄을 모은다(이야기 순서): 화자 = 그 인물인 줄 + 바로 앞뒤에 그 인물이 있는 Mandel(주인공) 줄. `줄번호|화자|영어|지금 한국어` 꼴 파일.
   순서: characters.md의 대사 많은 인물부터. 주인공 서술(Narration)은 마지막에 1,000줄 구간별로(말맛·번역투만).
2. 인물마다 서브에이전트 하나(Agent, model "sonnet"). 지시:
   - 기준 = translate-guide.md + characters.md의 그 인물 항목.
   - 말투: 관계 단계마다 체·호칭이 맞는지, 갑자기 바뀌는 줄, 처음 만남은 해요체, "그녀"·"당신" 금지.
   - 말맛: 영어의 말장난·관용구·비꼼·사투리·말버릇(ain't, dove, fishy 같은 이중 의미)을 밋밋하게 풀어 버린 줄은 우리말 말맛을 살려 다시 쓴다(뜻이 같아도 고칠 대상).
   - 연애·성적 묘사(성인끼리)는 원문 세기 그대로 둔다. 검수하다 그 줄을 순화하고 싶어지거나 다루기 어려우면 **고치지 말고 건너뛴다**(기존 번역 유지, 출력에 넣지 않음). 순화·삭제는 고친 것으로 치지 않는다(사용자 11-02).
   - 출력: 고칠 줄만 `ID<TAB>고친 문장`(단락 수·[토큰]·<i> 그대로). 우리말로 살리기 어려우면 `ID<TAB>?<TAB>이유`.
3. `?` 줄만 모아 메인(Opus)이 후보 2~3개 중 고른다.
4. 결과를 `translation/retrans/review-<인물>.tsv`로 저장, 단락 수 검사(chunk.py apply의 검사와 같게), 커밋·푸시. 인물마다 고친 줄 몇 개를 영어와 맞대어 사용자에게 보고.
5. 다 끝나면 사용자가 PC에서 scripts\cloud-apply.bat로 게임에 넣는다.
