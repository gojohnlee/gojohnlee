# 자율 주둔 용병단 (Mercenary Camp)

직접 조종하지 않는 이웃 용병단 모드. RimWorld **1.7**, Harmony 필요. DLC(로열티·바이오테크·아노말리)는 있으면 분기가 열리고, 없어도 됩니다.

## 하는 일

- **계약**: 보급소를 짓고 첫 의뢰를 수락하면 용병 4명이 캠프에 주둔. 식량·약은 창고에서 알아서 가져감(금지한 물건은 안 건드림).
- **전투**: 지정한 활동 구역 안의 적만 상대, 구역 밖 추격 없음, 손실이 쌓이면 캠프로 후퇴·재정비.
- **퍽 8분기 × 5단계**: 병력·정예·포병·공병·정찰 + 변칙(아노말리)·메카(바이오테크)·사이킥(로열티). 계약 점수 또는 창고 물자로 구매.
- **교리 16개**: 분기마다 2단계부터 2개씩, 둘 다 사서 같이 적용 가능(점수 3점).
- **연구 탭 '용병단'** 14개: 3~5단계와 교리를 엶. 일부 퍽은 바닐라 연구(박격포·IED·포탑·차지샷·메카텍)도 필요.
- **자유 설계**: '야전 캠프 설계' 연구 후 건축 메뉴 '용병단' 탭에서 침상·막사 벽·포대·방어선·경계 초소·집결지·우리·정비고를 직접 배치 → 용병이 평시에 시공. 한도는 퍽이 정함.

## 개발

- 소스: `Source/GJL_MercenaryCamp` (출력 DLL 이름은 `MercenaryCamp.dll`, `1.7/Assemblies`로 빌드)
- 빌드: `dotnet build Source/GJL_MercenaryCamp/GJL_MercenaryCamp.csproj -c Release`
  - 게임 참조는 NuGet `Krafs.Rimworld.Ref`(1.7.4985)라서 게임 DLL 없이 빌드됩니다. 빌드 DLL은 커밋하지 않고 CI 릴리스 `rimworld-latest`에 들어갑니다.
  - 게임 코드를 디컴파일해 읽을 때만 실제 게임 DLL이 필요(공개 저장소에는 올리지 않음).
- 인게임 자동 시험(89개 검사): `tests/GJL_MercenaryCamp_QA`를 PC의 `RimWorld\Mods\GJL_MercenaryCamp_QA`에 두고
  `powershell -ExecutionPolicy Bypass -File Mods\GJL_MercenaryCamp_QA\Tools\run-qa.ps1` (별도 프로필, 음소거, 약 2분).
