# 시험 목록 (회차 716 · 11-16 정리)

게임 시험 111개(TestRunner.Register 기준). 회귀 묶음은 `C:\vh\stable-dll\run.ps1 -Dll <dll> -Batches <아래 줄>`로 돌린다. -Dll 옆에 같은 커밋의 ValheimHelperTest.dll도 둔다(없으면 옛 시험 DLL이 섞임 — 회차 721: 커밋마다 `C:hstable-dll<해시>`에 dist 둘).
"마지막 PASS"는 history에서 찾은 회차 — 회귀 묶음 시험은 회차 713~715 전체 회귀에서 통과(묶음 단위 기록), 회차 716에 새로 넣은 7개(plainsfarm·pathforget·pathfollow·newerahouse·restartcraft 셋+2)는 716 PASS.

## 회귀 묶음 (run.ps1 줄 = 이름|바이옴|약식|시험|VH_RESET|집|월드)

```
B1||0|vh_npctest;vh_flagtest;vh_woodtest;vh_stonetest;vh_coppertest;vh_tintest
B2||0|vh_preptest;vh_bagtest;vh_deathtest;vh_fighttest;vh_exploretest;vh_pittest
B3||0|vh_fartest;vh_departtest;vh_crafttest;vh_peertest
B4||0|vh_nametest;vh_skilltest;vh_refilltest;vh_raidtest;vh_helptest;vh_teamtest;vh_claudetest;vh_bantertest;vh_matetest
B5||0|vh_settest;vh_portaltest;vh_portalbuildtest;vh_stucktest;vh_hoptest;vh_skiptest;vh_resisttest;vh_repairtest
B6||0|vh_crypttest;vh_hunttest;vh_cooktest;vh_cookwettest;vh_bowtest;vh_machinestucktest
B7||0|vh_farmtest;vh_basetest;vh_escorttest
B7b||1|vh_silvertest
B8a|plains|0|vh_squitotest;vh_fulingtest;vh_tartest;vh_plainsfarmtest
B8a2||0|vh_plainstest
B8b||1|vh_mistfoetest;vh_minefoetest;vh_minetest;vh_settletest;vh_blocktest;vh_weaktest
B8c|mistlands|0|vh_mistlandstest
B8d||0|vh_tidytest;vh_tidywalktest;vh_mychesttest;vh_guesttest;vh_sharetest;vh_stocktest
B9|ashlands|0|vh_ashlandstest
B9b||0|vh_ashfoetest;vh_morgentest
B10|deepnorth|0|vh_deepnorthtest
B10b||0|vh_deepfoetest;vh_frostcavetest
B11||0|vh_housetest;vh_unloadreachtest;vh_livestocktest;vh_posttest;vh_pathforget;vh_pathfollow
B11b||0|vh_guardbowtest;vh_chorestest;vh_fishtest
B11c||0|vh_newerahouse|||vh_house_TheNewEra
B13||0|vh_arrowtest;vh_cryptnighttest;vh_entrailstest;vh_gatherhelptest;vh_helpjointest
B13b||0|vh_nightexploretest;vh_nightretreattest;vh_notooltest;vh_toolbreaktest;vh_waitjointest;vh_waitlatetest;vh_chainfalltest
B13c||0|vh_houselogtest||1
R||0|vh_restart1;vh_restartcraft_load;vh_restartcraft_part;vh_restartcraft_carry
```

- B12 멀티는 따로: `scripts/vh-b12.ps1 -Dll <dll>`(jointest·guestcmdtest·guestchesttest·guestrestart1 → 서버 재시작 → guestrestart2).
- R: valheim-test.ps1이 1번째 뒤에 2번째(restart2·restartcraft2)를 VH_RESET=0 새 실행으로 알아서 붙임.
- 회귀 밖(일부러): 최종 실전 finaltest(따로 한 판, 길다) · 새 birdprobe·birdshoot(사용자 11-13) · 부하 loadtest · 다른 집 재기 otherhouse(조사용) · 집 도구 houseexport·houseimport·househere·houseground·housefind(시험 월드 만들기·찾기).

## 표

| 시험 | 지키는 것 | 묶음 | 마지막 PASS |
|---|---|---|---|
| arrowtest | 화살·볼트 제작법 값 읽기 | B13 | 713~715 |
| ashfoetest | 애쉬랜드 적 싸움 시험 | B9b | 713~715 |
| ashlandstest | 애쉬랜드 자료 시험 | B9 | 713~715 |
| bagtest | 다른 짐으로 가방이 찬 채 채집 시험 | B2 | 713~715 |
| bantertest | 일꾼 잡담 시험 | B4 | 713~715 |
| basetest | 기반 대표 시험(이어 하기·끼어들기·같은 상자·제련소 동시) | B7 | 713~715 |
| birdprobetest | 땅 앉은 새 프리팹 값 읽기 | 회귀 밖 | 536 |
| birdshoottest | 땅 앉은 새 명중 셈(낮·밤, 거리별) | 회귀 밖 | 551 |
| blocktest | 방패 막기 시험 | B8b | 713~715 |
| bowtest | 활 사냥(사슴 → 가죽) 시험 | B6 | 713~715 |
| chainfalltest | 나무 연쇄 쓰러짐 — 맞아 쓰러진 나무 통나무도 끝까지 | B13b | 713~715 |
| chorestest | 같이 캐기·바로 보충·쉴 때 먹거리·밭일·꿀 시험 | B11b | 713~715 |
| claudetest | 채팅 → Claude 호출 → 작업 실행 시험 | B4 | 713~715 |
| cooktest | 조리대 굽기 시험 | B6 | 713~715 |
| cookwettest | 조리대 굽기 비바람 시험(E014) | B6 | 713~715 |
| coppertest | 구리 채집 시험(광맥을 거점 12m 앞에 놓음, 땅속 광석 파기·구덩이 탈출) | B1 | 713~715 |
| crafttest | 제작 실행기(PlanJob) 시험 | B3 | 713~715 |
| cryptnighttest | 지하묘지 시험(⑦ 밤·좋은 차림) | B13 | 713~715 |
| crypttest | 지하묘지 시험 | B6 | 713~715 |
| deathtest | 사망·묘비·부활 시험 | B2 | 713~715 |
| deepfoetest | 딥노스 적 싸움 시험 | B10b | 713~715 |
| deepnorthtest | 딥노스 자료 시험 | B10 | 713~715 |
| departtest | 출발 전 판단·질문·대기열·저장 시험 | B3 | 713~715 |
| entrailstest | 창자 20개 사냥·다침 회복 시험 | B13 | 713~715 |
| escorttest | 호위병 동행 시험 | B7 | 713~715 |
| exploretest | 새 땅 탐험 시험 | B2 | 713~715 |
| farmtest | 밭 심기 시험 | B7 | 713~715 |
| fartest | 먼 구역 유지 시험 | B3 | 713~715 |
| fighttest | 위험 감지·싸움 시험 | B2 | 713~715 |
| finaltest | 최종 실전 시험: 이름 없는 실제 말투 명령 한꺼번에(알아들음·해냄 따로, 되돌림 한 번) | 회귀 밖 | 704 |
| fishtest | 쉴 때 낚시 시험 | B11b | 713~715 |
| flagtest | 거점 깃발 시험 | B1 | 713~715 |
| frostcavetest | 서리 동굴 내용 확인 시험 | B10b | 713~715 |
| fulingtest | 풀링 사냥(흑금속 고철) 시험 | B8a | 713~715 |
| gatherhelptest | 지원 요청 캐기 쪽 시험 | B13 | 713~715 |
| guardbowtest | 호위병 활·석궁 | B11b | 713~715 |
| guestchesttest | 멀티: 손님 상자 열기·Alt+우클릭 등록·소유권 시험 | B12 | 713~715 |
| guestcmdtest | 멀티: 손님 명령·대답·말풍선·장비·애니메이션 시험 | B12 | 713~715 |
| guestrestart1 | 멀티 재시작 1: 손님이 일 시킴·하는 중 기록 | B12 | 713~715 |
| guestrestart2 | 멀티 재시작 2: 서버 다시 켠 뒤 일 이어 함(VH_RESET=0) | B12 | 713~715 |
| guesttest | 사람끼리 동시 열기 손님 쪽 시험 | B8d | 713~715 |
| helpjointest | 지원 요청 Next 소비처 시험 | B13 | 713~715 |
| helptest | 도와줘요 시험 | B4 | 713~715 |
| hoptest | 막힘 복구: 목표 쪽 순간이동 시험 | B5 | 713~715 |
| houseexport | TheNewEra 복사본 집 조각을 파일로(시험 기본 월드로 옮기기 1단계) | 회귀 밖 | 454 |
| housefind | 월드의 기지(침대·문·작업대 무리)를 찾아 구조 요약 | 회귀 밖 | 700 |
| houseground | 기본 시험 월드 집 밑 땅을 원래 높이로 다시 고르고 저장(회차 458: 북·남쪽 바닥이 땅에서 떠 무너짐) | 회귀 밖 | 기록 없음 |
| househere | 되돌린 기본 시험 월드에 TheNewEra 집이 그대로 있는지(원본에 넣은 뒤) | 회귀 밖 | 455 |
| houseimport | TheNewEra 집을 기본 시험 월드에 다시 세움(옮기기 2단계) | 회귀 밖 | 455 |
| houselogtest | 집 곁 통나무 끝까지 쪼갬(벽·바닥 가장자리·계단·문) | B13c | 713~715 |
| housetest | 실제 집(벽·지붕·문) 안 상자·작업대 | B11 | 713~715 |
| hunttest | 사냥(멧돼지 → 고기) 시험 | B6 | 713~715 |
| jointest | 멀티: 손님 전용 서버 접속 시험 | B12 | 713~715 |
| livestocktest | 가축 확인(수치·먹이·따라오기) | B11 | 713~715 |
| loadtest | 모드 부하: 모드 켬 판 프레임 재기 | 회귀 밖 | 708 |
| machinestucktest | 설비 접근 막힘 복구(E035) | B6 | 713~715 |
| matetest | 동료 부르기 시험 | B4 | 713~715 |
| minefoetest | 감염된 광산 안 시커·진드기 만남 시험 | B8b | 713~715 |
| minetest | 감염된 광산(안개땅) 검은 핵 시험 | B8b | 713~715 |
| mistfoetest | 안개땅 적 싸움 시험 | B8b | 713~715 |
| mistlandstest | 안개땅 자료 시험 | B8c | 713~715 |
| morgentest | 애쉬랜드 모르겐 굴 용융 핵 시험 | B9b | 713~715 |
| mychesttest | 챗으로 내 개인 상자 준비 시험 | B8d | 713~715 |
| nametest | 상자 이름 → 역할 선택 시험 | B4 | 713~715 |
| newerahouse | TheNewEra 복사본 집 안 걸음(상자·화덕·2층 침대) | B11c | 716 |
| nightexploretest | 밤 탐험 명령 판단(장비별) | B13b | 713~715 |
| nightretreattest | 밤 위험 귀환·아침 이어 감 시험 | B13b | 713~715 |
| notooltest | 연장 없이 구리 채집 시험(장비 상자에 곡괭이를 안 넣음 → 맞는 연장 제작을 대기열에 걸고 구리 10개) | B13b | 713~715 |
| npctest | 일꾼 NPC 시험 | B1 | 713~715 |
| otherhouse | 다른 구조 집에서 두 바퀴 걷기(길 기억 전후) | 회귀 밖 | 700 |
| pathfollow | 길 기억 따라 걷기 시험 | B11 | 716 |
| pathforget | 길 기억 건축 바뀜·나이 지움 시험 | B11 | 716 |
| peertest | 혼자 하는 멀티(소유권·남의 채팅·손님) 시험 | B3 | 713~715 |
| pittest | 구덩이 탈출 시험(점프·경사로) | B2 | 713~715 |
| plainsfarmtest | 평원 밭(보리·아마)·록스 판단 시험 | B8a | 716 |
| plainstest | 평원 자료·출발 판단 시험 | B8a2 | 713~715 |
| portalbuildtest | 일꾼 포탈 놓기 시험 | B5 | 713~715 |
| portaltest | 포탈 시험 | B5 | 713~715 |
| posttest | 호위병 문 옆 경계·순찰 | B11 | 713~715 |
| preptest | 출발 준비(장비·음식) 시험 | B2 | 713~715 |
| raidtest | 습격 귀환 시험 | B4 | 713~715 |
| refilltest | 자동 보충(!채워) 시험 | B4 | 713~715 |
| repairtest | 장비 수리 시험 | B5 | 713~715 |
| resisttest | 저항 미드 시험 | B5 | 713~715 |
| restart1 | 재시작 시험 1: 일 중간 저장·게임 종료 | R | 715 |
| restart2 | 재시작 시험 2: 다시 켜서 이어 하기(VH_RESET=0) | R | 715 |
| restartcraft2 | 제작 재시작 2: 다시 켜서 계획 이어 하기(VH_RESET=0) | R | 716 |
| restartcraft_carry | 제작 재시작 1: 꺼낸 주괴를 든 채 종료 | R | 716 |
| restartcraft_load | 제작 재시작 1: 둘이 제련소에 넣은 직후 종료 | R | 716 |
| restartcraft_part | 제작 재시작 1: 일부 완성 때 종료 | R | 716 |
| settest | 세트메뉴 제작 시험 | B5 | 713~715 |
| settletest | 밤사이 정산(아침 정리) 시험 | B8b | 713~715 |
| sharetest | 사람끼리 동시 열기 호스트 쪽 시험 | B8d | 713~715 |
| silvertest | 은 광맥 시험 | B7b | 713~715 |
| skilltest | 스킬 성장·저장 시험 | B4 | 713~715 |
| skiptest | 멀리선 이동 생략 시험 | B5 | 713~715 |
| squitotest | 데스킷 싸움 시험 | B8a | 713~715 |
| stocktest | 상자 재료로 바로 제작 시험 | B8d | 713~715 |
| stonetest | 돌 채집 시험 | B1 | 713~715 |
| stucktest | 깃발 강제 복귀 시험 | B5 | 713~715 |
| tartest | 타르 줍기·타르 웅덩이 피하기 시험 | B8a | 713~715 |
| teamtest | 같이 싸움 시험 | B4 | 713~715 |
| tidytest | 상자 정리(아침 정산 때 한 번에) 시험 | B8d | 713~715 |
| tidywalktest | 쉴 때 걸어서 상자 정리 시험 | B8d | 713~715 |
| tintest | 주석 채집 시험(광맥을 거점 12m 앞에 놓음, 구리와 같은 경로) | B1 | 713~715 |
| toolbreaktest | 채집 중 연장 부러짐 시험(내구도 1.5 곡괭이 → 첫 휘두름에 부러짐 → 맞는 연장 제작 후 구리 10개) | B13b | 713~715 |
| unloadreachtest | 상자 앞 막힘 손 거리 넣기 | B11 | 713~715 |
| waitjointest | 기다림·합류 시험(20개) | B13b | 713~715 |
| waitlatetest | 지원 늦게 낀 판 시험(20개) | B13b | 713~715 |
| weaktest | 적 약점 시험 | B8b | 713~715 |
| woodtest | 나무 채집 시험 | B1 | 713~715 |
