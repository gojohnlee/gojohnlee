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
