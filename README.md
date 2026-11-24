# 원룸 찾기 도구 (범계·평촌 일대)

직방·다방 월세 매물을 모아 조건에 맞는 것만 순위로 보여 준다.

## 새 세션에서 돌리는 법 (Claude용)
```bash
git fetch origin house && git worktree add /tmp/house origin/house && cd /tmp/house
D=raw/$(date +%m%d); python3 -I scan.py $D && python3 -I judge.py $D && python3 -I report.py $D
```
- 네트워크 허용 필요: `apis.zigbang.com`, `sp.zigbang.com`, `www.dabangapp.com` (기본 환경에 등록돼 있음).
- `report.py`가 `data/latest.json`(직전 후보)과 비교해 🆕 새 매물·사라진 매물을 표시하고 latest.json을 갱신한다. 끝나면 `data/`와 `blacklist.json` 변경을 커밋해 `house` 브랜치에 푸시(먼저 `git pull --rebase origin house` — PC도 이 브랜치에 푸시함).
- 상세 캐시 `data/cache.json`: 상세는 처음 볼 때·7일 지났을 때·다방 가격이 바뀌었을 때만 새로 받는다(하루 상세 요청 약 300 → 수십). 14일 안 보인 매물은 지운다.
- 스크립트 순위는 1차 거름이다. 상위 매물의 설명을 읽고 장난질·모순을 사람 눈으로 다시 판정한 뒤 보고한다.

## 네이버부동산 (PC에서만) — ⚠️ 2026-11-24 현재 작동 안 함
2026-11-24 회사 PC에서 시험: 옛 주소 m.land.naver.com/cluster/ajax/articleList는 지역코드를 붙여도 `null`, new.land.naver.com은 토큰 없음, 네이버부동산은 fin.land.naver.com(네이버페이 부동산)으로 옮겨 감. `naver-scan.ps1`은 옛 주소를 쓰므로 쓸모없다. 새 사이트의 내부 주소는 확인하지 못함 → 네이버는 사용자가 보낸 캡처로 판정한다.

네이버는 클라우드 서버 IP의 접속을 끊는다(2026-11-24 확인). 차단을 위장·우회하지 않고, 사용자 PC의 평소 인터넷으로 개인 규모(한 지역, 목록 최대 15쪽, 요청 간격 3초, 로그인 없음)만 받는다.
- `naver-scan.ps1`: PC에서 목록을 받아 `data/naver/latest.json`으로 저장·푸시. `judge.py`가 2일 이내 것이면 합친다(관리비 8만 가정, 연식·엘리베이터 미확인 표시).
- 응답 형식은 클라우드에서 시험하지 못했다(가짜 데이터로 합치기만 시험). 첫 PC 실행의 status·count로 확인할 것.
- PC 설치(PowerShell에 붙여넣기, 1회):
```powershell
git clone -b house https://github.com/gojohnlee/gojohnlee.git $HOME\house-search
$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File $HOME\house-search\naver-scan.ps1"
Register-ScheduledTask -TaskName 'House naver scan' -Action $a -Trigger (New-ScheduledTaskTrigger -Daily -At 6:30) -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries) -Force
Start-ScheduledTask 'House naver scan'
```
  지우기: `Unregister-ScheduledTask 'House naver scan' -Confirm:$false`

## 보고 표 형식 (고정 — 사용자 2026-11-24: 마음대로 열을 늘리거나 줄이지 말 것)
| 순위(전날 대비) | 매물 | 평수 | 보증금 | 월 실부담(월세+관리비+전기·가스 추정) | 회사까지(직선/도보) | 대중교통(평일 9시 도착, 시간·환승) | 입주 | 주의 | 링크 |
- 관리비는 따로 쓰지 않고 월 실부담에 합친다. 매물 칸은 "○층 건물의 ○층 (준공년도)"로 풀어 쓴다.
- 대중교통: `transit.js`가 구글 지도 길찾기(다음 평일 9시 도착)를 실제 브라우저(Playwright)로 열어 상위 15곳의 첫 추천 경로를 읽는다. 결과는 raw/에만 두고 저장소에 올리지 않는다. 필요한 허용 주소: www.google.com 등 구글 지도 10개(기본 환경에 등록). 카카오맵 내부 주소는 'Gate Token Rejected'로 막혀 쓰지 않음(우회 금지). 막히거나 보안문자가 뜨면 '조회 실패'로 두고 우회하지 않는다.

## 버스 혼잡도: 하지 않음 (2026-11-24 사용자 중단)
공공데이터 혼잡도·재차인원은 버스 번호로 고를 수 없고 코드 대조표가 없어 쓰지 않는다. 자동 확인에서 시도하지 말 것.

## 사용자 조건 (2026-11-24 기준)
- 위치: 평촌아크로타워(관양동, 범계역 590m) = 회사(추정, 2026-11-24 "회사와의 거리" 요청) 기준 3km 이내. 뛰어서 다님. 보고에 회사까지 거리: 직선 km, 실제 길은 직선×1.3으로 보고 도보 80m/분으로 분 환산(추정). 달리기 시간은 넣지 않는다(사용자 요청).
- 필수: 벌레 없음, 소음 없음. 희망: 월 실부담 60만 안팎(70만대는 부담), 6평 이상이면 좋음.
- 보증금: 1천은 편함, 2천까지 가능. 대출 필요 없음. 주차는 중요하지 않음. 이사 시기 무관(입주일로 거르지 않음; 빈방·오래 안 나간 방은 협상 여지로 표시).
- 세액공제·청년월세 대상 아님(소득 기준) → 계산에 넣지 않음.

## 거르는 기준
| 단계 | 기준 |
|---|---|
| 제외 | 보증금 2천 초과, 3km 밖, 근생·숙박·고시원·기숙사·업무·다중생활시설, 전입 불가, 1~2층, 옥탑, 재래시장 인접, 층 정보 모순, blacklist.json |
| 연식 | 2008년 이후. 그 전이면 1995년 이후 + 엘리베이터 + (오피스텔 또는 30세대 이상 또는 7층 이상 건물)일 때만 |
| 예산 | 월세+관리비+별도 전기·가스·수도(추정) ≤ 64만 |
| 가점 | 꼭대기층(위층 소음 없음, 2008년 이후 +2·그 전 +1 — 사용자: 층간소음이 큰 단점), 6층 이상, 엘리베이터, 오피스텔·30세대 이상·경비/관리실, 100세대 이상·24시 경비, 새 건물, 6~7평 이상, 60만 이하 |
| 감점 | 엘리베이터 없음 -2(설명에 엘리베이터가 있다고 적혔으면 감점 대신 모순 확인 — 사용자: 엘베 없는 건 크다), 대로변, 5.5평 미만 |

## 장난질로 본 사례
- 기숙사·전입 불가를 오피스텔/공동주택으로 표기, 부가세 별도를 가격 칸에서 숨김
- 제목 가격과 설명 가격이 다름(미끼), 같은 건물인데 매물마다 연식·용도가 다름
- 옵션에 세탁기가 있는데 설명은 "공용", 전용 5.7평을 "6평"으로 표기
