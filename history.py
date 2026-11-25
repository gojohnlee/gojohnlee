"""사용: python3 -I history.py raw/<날짜>   (judge.py 다음, report.py 전에)
1) 최근 7일 안에 목록에서 사라진 방을 data/cache.json(매물별 마지막으로 본 날 'seen')에서 찾고,
   오늘 후보마다 '700m 안, 평수 ±1평, 월 실부담 ±8만'인 사라진 방 수를 recs.json의 note(보고 '주의' 칸)에 붙인다.
   사라짐 = 계약 또는 광고 내림(구분 불가). 오늘 받은 매물 수가 전날의 70% 미만이면 수집 오류로 보고 건너뜀.
2) data/history.json에 매물별 처음·마지막 본 날과 가격·크기를 쌓는다(나중 분석용, 사용자 11-25)."""
import json, os, sys, math, shutil, subprocess, tempfile, datetime

day = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))
today = datetime.date.today()
cache = json.load(open(f'{here}/data/cache.json'))
recs = json.load(open(f'{day}/recs.json'))
DAYS, NEAR, DPY, DTOT = 7, 700, 1.0, 8

seen = {k: datetime.date.fromisoformat(v.get('seen', v['fetched'])) for k, v in cache.items()}
n_today = sum(1 for d in seen.values() if d == today)
prev_days = [d for d in seen.values() if d < today]
last_prev = max(prev_days) if prev_days else None
n_prev = sum(1 for d in seen.values() if d == last_prev)
gone_keys = [k for k, d in seen.items() if d < today and (today - d).days <= DAYS]


def m(a, b):
    return math.hypot((a['lat'] - b['lat']) * 111000, (a['lng'] - b['lng']) * 88000)


gone = []
if last_prev and n_today < 0.7 * n_prev:
    print(f'사라진 방 비교 건너뜀: 오늘 {n_today}건 < 전날 {n_prev}건의 70% (수집 오류 의심)')
elif gone_keys:
    tmp = tempfile.mkdtemp()
    os.makedirs(f'{tmp}/zb'); os.makedirs(f'{tmp}/db')
    for k in gone_keys:
        json.dump(cache[k]['data'], open(f"{tmp}/{'zb' if k[0] == 'z' else 'db'}/{k[1:]}.json", 'w'), ensure_ascii=False)
    subprocess.run([sys.executable, '-I', f'{here}/judge.py', tmp], check=True, stdout=subprocess.DEVNULL)
    uniq = {}
    cur = [x for x in recs if x.get('lat')]
    for g in json.load(open(f'{tmp}/recs.json')):
        # 같은 방이 번호만 바꿔 다시 올라온 것(80m 안·같은 층·평수 ±0.3)은 사라진 게 아님(11-25 확인: 호계 970-6 등)
        if g.get('lat') and any(m(g, x) <= 80 and x.get('fl') == g.get('fl') and abs(x['py'] - g['py']) <= 0.3 for x in cur):
            continue
        if g.get('lat') and g.get('sales') == '월세':
            uniq.setdefault((round(g['lat'], 4), round(g['lng'], 4), g['py'], g['tot']), g)
    gone = list(uniq.values())
    shutil.rmtree(tmp)

hit = 0
for x in recs:
    if not x.get('lat') or 'tot' not in x:
        continue
    n = sum(1 for g in gone if m(x, g) <= NEAR and abs(g['py'] - x['py']) <= DPY and abs(g['tot'] - x['tot']) <= DTOT)
    if n:
        hit += 1
        x['note'] = ((x['note'] + ', ') if x.get('note') else '') + f'근처 비슷한 방 {n}곳 최근 {DAYS}일 새 사라짐(계약 또는 광고 내림)'
json.dump(recs, open(f'{day}/recs.json', 'w'), ensure_ascii=False)

hp = f'{here}/data/history.json'
H = json.load(open(hp)) if os.path.exists(hp) else {}
t = today.isoformat()
for x in recs:
    k = ('z' if x['src'] == '직방' else 'd' if x['src'] == '다방' else 'n') + str(x['id'])
    h = H.setdefault(k, {'first': t})
    h.update(last=t, dep=x['dep'], rent=x['rent'], tot=x.get('tot'), py=x.get('py'), lat=x.get('lat'), lng=x.get('lng'), res=x.get('res'))
json.dump(H, open(hp, 'w'), ensure_ascii=False, separators=(',', ':'))
print(f'사라진 방(최근 {DAYS}일) {len(gone)}곳, 비슷한 방이 사라진 후보 {hit}곳, 기록 {len(H)}건')
