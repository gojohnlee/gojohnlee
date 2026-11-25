"""집품(zippoom.com) 건물 후기 수집(사용자 11-25: 집품 접속 가능해짐).
사용: python3 -I zippoom.py [최대 받을 쪽수, 기본 150]
- robots.txt가 허용한 경로만 쓴다: 사이트맵(/sitemap/리뷰/index.xml)과 건물 후기 쪽(/리뷰/...). /search·/address·/review-list는 쓰지 않는다.
- 안양·의왕·군포 건물 쪽만, 요청 사이 2초, 한 번에 최대 N쪽. 받은 쪽은 7일 동안 다시 받지 않는다(data/zippoom_cache.json).
- 결과 data/zippoom_buildings.json: 지번별 {v: good/bad, n, bad, reviews[{bad, kinds, txt, floor, date}]} — daangn_buildings.json과 같은 꼴.
  로그인(ZIPPOOM_AUTH_TOKEN)이 있으면 후기 전문, 없으면 앞부분(약 40자)과 단점 키워드로 판정한다."""
import json, os, re, sys, time, datetime, urllib.request, urllib.parse

here = os.path.dirname(os.path.abspath(__file__))
MAX = int(sys.argv[1]) if len(sys.argv) > 1 else 150
H = {'User-Agent': 'Mozilla/5.0'}
# 사용자 11-25: 사용자 계정으로 로그인해 후기 전문을 읽는다. 환경변수 ZIPPOOM_AUTH_TOKEN = 브라우저 쿠키 ZIPPOOM_AUTH_TOKEN 값 그대로(저장소·보고에 쓰지 않음).
TOK = os.environ.get('ZIPPOOM_AUTH_TOKEN', '').strip()
if TOK:
    H['Cookie'] = 'ZIPPOOM_AUTH_TOKEN=' + TOK
cut = 0   # 본문이 '...'로 잘린 후기 수(로그인이 안 먹으면 늘어남)
cp = f'{here}/data/zippoom_cache.json'
cache = json.load(open(cp)) if os.path.exists(cp) else {}
today = datetime.date.today()


def get(u):
    with urllib.request.urlopen(urllib.request.Request(u, headers=H), timeout=30) as r:
        return r.read().decode('utf-8', 'replace')


sm = get('https://zippoom.com/sitemap/%EB%A6%AC%EB%B7%B0/index.xml')
urls = [u for u in re.findall(r'<loc>([^<]+)</loc>', sm) if re.search(r'안양|의왕|군포', urllib.parse.unquote(u))]
todo = [u for u in urls if u not in cache or (today - datetime.date.fromisoformat(cache[u]['fetched'])).days >= 7
        or (TOK and not cache[u].get('login') and cache[u].get('reviews'))]   # 로그인 없이 받은 후기 쪽은 로그인 후 다시 받음
todo.sort(key=lambda u: cache.get(u, {}).get('fetched', ''))   # 안 받은 것·오래된 것부터
n = 0
for u in todo[:MAX]:
    try:
        h = get(u)
        p = json.loads(re.search(r'<script id="__NEXT_DATA__"[^>]*>(.*?)</script>', h, re.S).group(1))['props']['pageProps']
        dash = p.get('dashboard') or {}
        revs = []
        for r in (p.get('loadedReview') or []):
            if str(r.get('consComment') or '').endswith('...') or str(r.get('prosComment') or '').endswith('...'):
                cut += 1
            revs.append({k: r.get(k) for k in ('floor', 'prosComment', 'consComment', 'prosKeyword', 'consKeyword', 'createdTs', 'isLivingVerified')})
        cache[u] = {'fetched': today.isoformat(), 'addr': dash.get('oldAddress'), 'name': urllib.parse.unquote(u).split('/')[-2], 'total': dash.get('totalReviewCount'), 'login': bool(TOK), 'reviews': revs}
        n += 1
    except Exception as e:
        print('ZPERR', urllib.parse.unquote(u)[-40:], str(e)[:80])
        cache.setdefault(u, {'fetched': today.isoformat(), 'addr': None, 'reviews': []})
    time.sleep(2)
    if n % 25 == 0:
        json.dump(cache, open(cp, 'w'), ensure_ascii=False, separators=(',', ':'))   # 중간에 끊겨도 받은 만큼 남김
json.dump(cache, open(cp, 'w'), ensure_ascii=False, separators=(',', ':'))

# 판정 — daangn_region.py와 같은 기준(사용자 11-24 나쁜 후기: 소음·벌레·곰팡이·누수·담배·보증금, 11-25 소음 종류 구분)
BADKW = {'층간소음', '벽간소음', '소음', '방음', '벌레', '곰팡이', '누수', '결로', '담배', '냄새', '외풍', '바퀴벌레'}
BADW = r'벌레|바퀴|소음|시끄|방음|층간|들려|들림|경매|빌라왕|곰팡|누수|결로|보증금.{0,6}(안|못|늦)|쥐|모기|담배|녹물|하수구 ?냄새'
OKW = r'(벌레|소음|층간소음|방음|곰팡이?|누수|결로|냄새|소리)[^.,/!\n]{0,8}(없|잘 ?되|잘 ?됨|잘돼|안 ?나|못 ?들|한 ?번도|전혀|거의 없)'
FLOORW = r'층간|윗집|위층|윗층|위에서|발망치|쿵쿵'
WALLW = r'옆 ?집|옆 ?방|옆 ?호|벽간|벽|방음|말소리|대화 ?소리|통화|TV|티비|다 ?들|소리가? ?(잘|너무|다) ?들|복도'
OTHERW = r'벌레|바퀴|경매|빌라왕|곰팡|누수|결로|보증금.{0,6}(안|못|늦)|쥐|모기|담배|녹물|냄새|시끄|차 ?소리|도로|기차|지하철|공사|외풍'
# 후기 전문(사용자 계정 로그인 + 열람 권한이 있을 때 zippoom_full.js로 받은 것, data/zippoom_full.json)이 있으면 잘린 글 대신 쓴다
fp = f'{here}/data/zippoom_full.json'
FULL = json.load(open(fp)) if os.path.exists(fp) else {}
for u, f in FULL.items():
    if u in cache and f.get('reviews'):
        cache[u]['reviews'] = [{k: r.get(k) for k in ('floor', 'prosComment', 'consComment', 'prosKeyword', 'consKeyword', 'createdTs', 'isLivingVerified')} for r in f['reviews']]
        cache[u]['full'] = f.get('date')
B = {}
for u, c in cache.items():
    a = re.sub(r'^경기(도)?\s*', '', c.get('addr') or '').strip()
    if not re.search(r'\d+(-\d+)?$', a):
        continue
    for r in c['reviews']:
        kw = set(filter(None, (r.get('consKeyword') or '').split(',')))
        cons = re.sub(OKW, '', (r.get('consComment') or '').rstrip('.'))
        bad = bool(kw & BADKW) or bool(re.search(BADW, cons))
        kinds = set()
        if bad:
            if '층간소음' in kw or re.search(FLOORW, cons): kinds.add('floor')
            if kw & {'벽간소음', '방음'} or re.search(WALLW, cons): kinds.add('wall')
            if kw & (BADKW - {'층간소음', '벽간소음', '방음'}) or re.search(OTHERW, cons) or not kinds: kinds.add('other')
        txt = f"집품 {r.get('floor') or '?'}층 | 장점: {r.get('prosComment') or '-'} | 단점: {r.get('consComment') or '-'} | 단점 키워드: {r.get('consKeyword') or '-'}"
        B.setdefault(a, []).append({'bad': bad, 'kinds': sorted(kinds), 'txt': txt[:300], 'floor': r.get('floor'), 'date': str(r.get('createdTs') or '')[:10], 'name': c.get('name')})
res = {k: {'v': 'bad' if any(r['bad'] for r in v) else 'good', 'n': len(v), 'bad': sum(r['bad'] for r in v), 'reviews': v} for k, v in B.items()}
json.dump(res, open(f'{here}/data/zippoom_buildings.json', 'w'), ensure_ascii=False, indent=0)
print(f'집품 로그인 {"있음" if TOK else "없음"}, 본문 잘린 후기 {cut}개' + (' ← 로그인이 안 먹음(토큰 만료?)' if TOK and cut else ''))
print(f'집품: 지역 건물 쪽 {len(urls)}, 이번에 받음 {n}, 남은 것 {max(len(todo) - MAX, 0)}, 후기 있는 지번 {len(res)}, 나쁨 {sum(v["v"] == "bad" for v in res.values())}')
