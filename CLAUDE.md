# 작업 규칙 (네크로맨서 테일 한글화 — 공개 저장소 `gojohnlee/gojohnlee`의 `necromancer` 브랜치가 작업 장소. 발헤임은 `valheim`, 림월드는 `rimworld`)

## 사용자 전제 (불변 — 묻거나 의심하지 않는다)
1. 사용자는 **항상 최신본**을 쓴다. 최신본 = 아래 "최신본" 절.
2. 한글 패치 1.0 원본(NecromancerKR-1.0)은 쓰지 않는다. 대사는 모두 우리 재번역(translation/retrans)이다.

## 대화
- 답은 한국어. 사용자에게 요청할 것은 답 맨 끝에 1. 2. 3. 목록으로. 사용자는 md·txt를 직접 안 읽음 → 안내는 대화에 바로.
  결과물 링크: https://github.com/gojohnlee/gojohnlee/blob/necromancer/<경로>
- 확인한 사실과 추측을 나눠 말한다. 모르는 건 "모릅니다". 사용자의 PC·작업 방식을 넘겨짚지 않는다.
- **토큰 최소**: 명령은 묶고 출력은 짧게, 큰 파일(csv·tsv·인덱스·history)은 grep으로 필요한 부분만.
- 스스로 진행, 방식 선택은 추천안으로. 번역 표기·용어처럼 사용자가 정할 일은 "문제 보고 → 결정 받기 → 고치기" 순서.

## 저장소
- **공개 저장소**: 게임 회사 파일(게임 DLL·디컴파일 결과)과 남의 번역(한글 패치 1.0)은 올리지 않는다. 커밋 작성자 Claude <noreply@anthropic.com>, 푸시는 `necromancer`.
- 게임 DLL만 비공개 `gojohnlee/valheim-refs`의 `necromancer` 브랜치 `dll/`에 있다. 클라우드에서 모드를 빌드하거나 디컴파일할 때만 add_repo로 붙여 쓴다.
- 상태는 docs/progress.txt("지금" 최근 3개·"다음" 남은 일), 결정은 docs/decisions.txt(어긋나는 옛 줄은 지움), 끝낸 일은 docs/history.md 맨 위.

## 최신본 (게임에 들어가는 것)
- 대사: `translation/template.csv`(줄 ID·영어 원문·단락 틀만, 한국어 없음) 위에 `translation/retrans/*.tsv`를 **파일 이름순**으로 덮은 것(같은 ID는 뒤 파일이 이김).
  순서: auto-*(ChatGPT Sol 재번역) → review-*(인물별 검수) → review-zz-uncle → review-zzz-final → review-zzzz-*(용어·호칭) → zz-unify(11-03 용어·같은 문장 통일).
  tsv 꼴: `ID<TAB>문장`, 단락은 ` / `(빈 단락은 ` /  / `).
- 화면 글: `dist/KoreanFont/ui-ko.tsv`(메뉴·주문 이름 등), 이름: `dist/KoreanFont/names-ko.tsv`. 모드 DLL은 `dist/`.
- 게임에 넣기(사용자 PC): 맨 위 `설치.bat` **하나** — git pull로 최신본 받기 → 모드 설치 → fix-csv.ps1이 template.csv 위에 retrans를 이름순으로 덮어 게임 Languages\Korean.csv로 씀(클라우드 시험: dotnet tool로 pwsh 설치 후 실행).
  bat는 CP949: scripts/install.utf8.bat를 고치고 `iconv -f utf-8 -t cp949`로 설치.bat를 만든다. 모드 지우기는 scripts/제거.bat(거의 안 씀).
- 번역을 고칠 땐 새 tsv를 retrans에 더한다(이름순으로 맨 뒤에 오게). 기준: `cloud/translate-guide.md`(지침·말투 표·**용어 통일 표 — 유일본**), 이름은 names-ko.tsv.
  같은 원문 다른 번역 현황: `인덱스.txt`.

## 끝난 기록 (읽지도 고치지도 않음)
- cloud/routine.md·status.md·chunk.py·tries.json·chatgpt-*·compare/·context/·review/·review-plan.md: 11-02에 끝난 재번역·검수 작업서와 도구.
- translation/dump/(화자표 — 화자 찾을 때만 grep).

## 모드 (dist/·src/)
- 게임: Steam 1315320, Unity 6000.3.6f1 Mono. **mscorlib·System이 잘려 있음** → 흔한 API도 없을 수 있다
  (확인된 것: DateTime.ToString(string), AssemblyLoadEventArgs.LoadedAssembly 게터, Process.MainWindowHandle, AudioListener.volume, netstandard.dll).
  없는 API를 부르면 그 메서드 JIT가 통째로 실패한다 → 새 API는 ilspycmd로 게임 DLL(PC는 게임 폴더, 클라우드는 비공개 valheim-refs dll/)에 있는지 먼저 확인, 위험한 코드는 별도 메서드+try.
- 로더: BepInEx 5는 못 씀(Module.GetPEKind 없음). Doorstop(winhttp.dll, BepInEx 5.4.23.6 동봉본)이 `KoreanFont\NecroKoreanFont.dll`의
  `Doorstop.Entrypoint.Start()`를 직접 부른다. 대상 프레임워크 net472(netstandard면 게임이 꺼짐).
- 코드: src/KoreanFont/Plugin.cs 하나(폰트·UI 표·시험 동작). 빌드 `dotnet build src/KoreanFont -c Release -p:Managed=<dll 폴더>`
  (PC는 GameDir 자동, 클라우드는 비공개 valheim-refs dll/) → bin/Release/net472/NecroKoreanFont.dll을 dist/KoreanFont에 복사.
- 확인한 게임 사실은 docs/decisions.txt "확인된 게임 사실"에 적고 다시 디컴파일하지 않는다.
- 시험(PC): `powershell -File scripts/run-min.ps1 -Wait 100` — 숨겨 켜고 최소화·음소거, 언어 창 자동 OK, 30초마다 한글 글자가 폰트에 있는지 KoreanFont.log에.
  시험 전용 동작은 환경변수 NECRO_MIN=1일 때만. -screen-* 인자 금지(해상도 설정이 바뀜).
