# Casualties Unknown 한국어 패치 (비공식)

게임 **Casualties Unknown**(Steam)의 모든 글자(메뉴·아이템·상태·튜토리얼·캐릭터 대사·쪽지·PDA 기록·일시정지 문구, 5,696줄)를 한국어로 옮긴 파일입니다.
게임에 원래 있는 언어 선택 기능을 그대로 쓰므로 모드 로더가 필요 없습니다.

> 공식 번역이 아닙니다. 게임 버전 **1.0.2** 기준입니다.
> 커뮤니티 한국어 번역(개발사 저장소 [scavgame-locale](https://github.com/Orsoniks/scavgame-locale/blob/main/release/ko-KR.json), 파일 이름 `ko-KR.json`)이 따로 있습니다. 둘을 함께 넣어도 되고, 메뉴에서 이 패치는 **한국어 (Claude 번역)** 으로 보입니다.

## 설치

1. 이 저장소의 [`casualties/ko.json`](ko.json) 파일을 받습니다.
   (휴대폰이면 PC에서 받아야 합니다. 파일 화면 오른쪽 위 **다운로드(↓)** 버튼)
2. Steam에서 게임을 오른쪽 클릭 → **관리 → 로컬 파일 보기**를 누릅니다.
3. 열린 폴더에서 `CasualtiesUnknown_Data` → `Lang` 폴더로 들어갑니다.
   (`EN.json`, `zh-CN.json`이 있는 곳)
4. 받은 `ko.json`을 그 `Lang` 폴더에 넣습니다.
5. 게임을 켜고 **설정 → Language(언어)** 에서 **한국어 (Claude 번역)** 을 고릅니다.

## 되돌리기 (영어로)

- 가장 쉬운 방법: 게임 **설정 → Language** 에서 **English**를 고릅니다.
- 완전히 지우려면: 위 3번의 `Lang` 폴더에서 `ko.json`을 지웁니다.
  - 지운 뒤 게임이 영어로 안 돌아오면 설정에서 English를 한 번 골라 주세요.
- Steam 게임 **속성 → 설치된 파일 → 게임 파일 무결성 확인**은 원래 파일만 고치고 `ko.json`은 남겨 둡니다. 지우려면 위처럼 직접 지우세요.

## 알아 둘 점

- **게임 안 확인은 아직 안 했습니다.** 글꼴은 게임에 들어 있는 Unifont(한글 포함)로 나올 것으로 보지만, 한글이 네모(□)로 보이면 알려 주세요.
- 게임이 업데이트되면 새로 생긴 문장은 영어로 나오거나, 버전이 다르다는 경고가 뜰 수 있습니다.
- 용어는 [`glossary.md`](glossary.md)에 정리했습니다(예: Stamina=기력, Limb=팔다리, Dressing=드레싱).
- 캐릭터 말투: 실험체(반말, 겁 많음), 밀키(딱딱하고 분석적인 말투), 듄(끊어지는 말 "여-기...").

## 만드는 법 (고칠 사람용)

- 번역 원본: `ko/*.tsv` (한 줄에 `키<탭>번역`, 줄바꿈은 `\n`)
- 빌드: `tools/build.ps1`이 게임의 `EN.json` 구조에 번역을 채워 `ko.json`을 만듭니다.
  ```powershell
  powershell -ExecutionPolicy Bypass -File casualties/tools/build.ps1 -En "<게임>\CasualtiesUnknown_Data\Lang\EN.json" -KoDir casualties/ko -Out casualties/ko.json -Version 1.0.2
  ```
- 영어 원문 추출: `tools/flatten.ps1` (게임 파일에서 직접 뽑으며, 원문은 저장소에 올리지 않습니다)

---

# 회차 보너스 모드 (Carryover, 로그라이트)

죽거나 엔딩을 본 회차에서 **새로 얻은 능력치(힘·회복력·지능)만큼** 다음 새 회차를 높여 시작합니다.

- **직전 한 회차에서 얻은 양만** 받습니다. 쌓이지 않습니다.
- 예: 1회차에서 3·3·3을 올리고 죽으면 → 2회차는 +3·+3·+3으로 시작합니다.
- 2회차에 바로 죽으면 새로 얻은 게 0이므로 → 3회차는 +0으로 시작합니다. 물려받은 보너스는 얻은 양에 넣지 않습니다.
- 능력치가 줄었으면 0으로 칩니다.
- 보너스가 있으면 캐릭터 선택 화면에 `[STR] 12 (+3)`처럼 보이고, 회차를 시작할 때 알림이 뜹니다.
- 셈하지 않는 경우:
  - 튜토리얼
  - 죽거나 엔딩을 보지 않고 끝낸 회차(메뉴로 나가기·새로 시작). 이때는 그 전 보너스가 그대로 남습니다.
- 이어하기(저장 불러오기)는 같은 회차로 이어서 셉니다.
- 게임 세이브는 건드리지 않습니다. 상태는 `BepInEx\config\casualties.carryover.txt` 파일 하나에만 둡니다.

## 설치

1. BepInEx 5(Windows x64, 5.4.21 이상)를 [공식 릴리스](https://github.com/BepInEx/BepInEx/releases)에서 받습니다.
   - `BepInEx_win_x64_5.4.x.zip` 파일입니다.
   - 게임 폴더(`CasualtiesUnknown.exe`가 있는 곳)에 압축을 풉니다.
   - 게임 폴더는 Steam에서 게임 우클릭 → 관리 → 로컬 파일 보기로 열 수 있습니다.
2. [CasualtiesCarryover.dll](carryover/dist/CasualtiesCarryover.dll)을 받아 게임 폴더의 `BepInEx\plugins\` 폴더에 넣습니다. 폴더가 없으면 만듭니다.
3. 게임을 켜면 됩니다. 잘 들어갔는지는 `BepInEx\LogOutput.log`에 `Casualties Carryover`가 있는지 보면 압니다.

## 되돌리기

- **이 모드만 끄기:** `BepInEx\plugins\CasualtiesCarryover.dll`을 지웁니다. 보너스 기록까지 지우려면 `BepInEx\config\casualties.carryover.txt`도 지웁니다.
- **모드 전체를 지우고 원래 게임으로:** 게임 폴더에서 `BepInEx` 폴더, `winhttp.dll`, `doorstop_config.ini`를 지웁니다.
  - 세이브와 설정은 원래 것 그대로입니다.
  - Steam 파일 무결성 검사로는 이 파일들이 지워지지 않으니 직접 지워야 합니다.

## 만드는 법 (고칠 사람용)

- 계산 로직: `carryover/Core/CarryoverState.cs`
  - 게임과 분리되어 있습니다.
  - 테스트: `cd carryover/Tests; dotnet test`
- 플러그인: `carryover/Plugin`
  - 빌드: `dotnet build -c Release`. 설치된 게임의 `Managed` DLL을 참조합니다.
  - 패치 위치: `WorldGeneration.Start`(회차 시작), `PlayerCamera.EndSequence`(죽음·엔딩), `PreRunScript.UpdateCharacterText`(선택 화면 표시)
- 게임 안 자동 시험: `carryover/test.ps1`
  - 시험은 조용히 돕니다: 작은 창을 바로 최소화하고 소리를 끕니다.
  - 상태 파일은 시험용을 따로 씁니다.
  - 게임 설정 레지스트리는 시험 뒤 원래대로 되돌립니다.
