# 작업 규칙 (브랜치 `necromancer` = 네크로맨서 테일 한글 모드 전용. 발헤임은 `valheim`, 림월드는 `rimworld`)

- 답은 한국어. 사용자에게 요청할 것은 답 맨 끝에 1. 2. 3. 목록으로. 사용자는 md·txt를 직접 안 읽음 → 안내는 대화에 바로.
  결과물 링크: https://github.com/gojohnlee/gojohnlee/blob/necromancer/<경로>
- **토큰 최소**: 명령은 묶고 출력은 짧게, 큰 파일은 grep -n으로 필요한 부분만. 사용자는 개입하지 않음 → 스스로 진행, 방식 선택은 추천안으로(묻지 않음).
- 새 세션은 기억이 없음 → 상태는 docs/progress.txt("지금"·"다음"), 결정은 docs/decisions.txt, 끝낸 일은 docs/history.md 맨 위(쉬운 말 제목·요약).
- **저장소는 공개**: 게임 파일·디컴파일 결과·한글 번역 원본 패키지(NecromancerKR-1.0)는 올리지 않는다. 그 패키지는 참조만(고치지 않음).
- 커밋: 빌드 통과·게임 시험 통과 때마다 커밋·푸시(dist DLL 포함), progress "지금"·"다음"만 고친다.

# 네크로맨서 테일 한글 모드

- 게임: Steam 1315320, Unity 6000.3.6f1 Mono. **mscorlib·System이 잘려 있음** → 흔한 API도 없을 수 있다
  (확인된 것: DateTime.ToString(string), AssemblyLoadEventArgs.LoadedAssembly 게터, Process.MainWindowHandle, AudioListener.volume, netstandard.dll).
  없는 API를 부르면 그 메서드 JIT가 통째로 실패한다 → 새 API는 ilspycmd로 게임 DLL에 있는지 먼저 확인, 위험한 코드는 별도 메서드+try.
- 로더: BepInEx 5는 이 게임에서 못 씀(Module.GetPEKind 없음). Doorstop(winhttp.dll, BepInEx 5.4.23.6 동봉본)이 `KoreanFont\NecroKoreanFont.dll`의
  `Doorstop.Entrypoint.Start()`를 직접 부른다. 대상 프레임워크 net472(netstandard면 게임이 꺼짐).
- 코드: src/KoreanFont/Plugin.cs 하나(폰트·UI 표·시험 동작). UI 번역 표는 dist/KoreanFont/ui-ko.tsv. 빌드 `dotnet build src/KoreanFont -c Release` → bin/Release/net472/NecroKoreanFont.dll을 dist/KoreanFont와 게임 폴더에 복사.
- 게임 DLL 디컴파일은 C:\vh\necro-kr\decomp\acs(로컬, 올리지 않음). 확인한 사실은 decisions.txt "확인된 게임 사실"에.
- 시험: `powershell -File scripts/run-min.ps1 -Wait 100` — Steam 창 없이 숨겨 켜고 최소화·음소거(끝나면 해제), 언어 창 자동 OK,
  30초마다 화면의 한글 글자가 폰트에 다 있는지 검사해 KoreanFont.log에 적는다. 시험 전용 동작은 환경변수 NECRO_MIN=1일 때만.
  시험에 -screen-* 인자 금지(게임이 해상도를 레지스트리에 저장해 사용자 설정이 바뀜).
