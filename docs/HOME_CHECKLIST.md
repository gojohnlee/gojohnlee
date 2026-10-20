# 집(PC)에서 할 일 체크리스트

폰에서 미리 세팅해 둔 것을 PC에서 확인하는 순서입니다. 끝나면 결과를 Claude 대화에 그대로 붙여넣으면 됩니다.

1. **빌드 번호 확인**: RimWorld 메인 메뉴 좌하단 숫자 (예: `1.7.4982 rev...`)
2. **브랜치 확인**: Steam → RimWorld 속성 → 베타 → 선택된 브랜치 (없음 / unstable 등)
3. **Harmony 모드 확인**: 모드 목록에 Harmony가 있는지, 1.7 지원 표시가 있는지
4. **테스트 모드 설치**
   - GitHub → Actions → 최신 "RimWorld mod build" → `HelloRimWorld` 아티팩트 다운로드
   - 압축 해제 → `HelloRimWorld` 폴더를 `RimWorld/Mods/` 에 넣기
   - 모드 순서: Harmony → HelloRimWorld
   - 게임 시작/불러오기 → "Hello RimWorld 모드가 작동 중입니다!" 메시지 확인
5. **로그 위치** (오류 시 붙여넣기)
   - Windows: `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`
   - 1.7에서 경로가 바뀌었으면 게임 내 개발자 모드 로그 창 내용을 복사
