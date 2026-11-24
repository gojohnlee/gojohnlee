# 사용: python3 -I report.py raw/<날짜>   → 순위·신규·사라짐을 마크다운으로 출력, data/latest.json 갱신
import json,re,sys,os,subprocess,datetime,calendar
day=sys.argv[1];here=os.path.dirname(os.path.abspath(__file__))
R=json.load(open(f'{day}/recs.json'));BL=json.load(open(f'{here}/blacklist.json'))
RVp=f'{here}/reviews.json';RV=json.load(open(RVp))
BADW=r'벌레|바퀴|소음|시끄|방음|층간|들려|들림|경매|빌라왕|곰팡|누수|결로|보증금.{0,6}(안|못|늦)'
def akey(x): return re.sub(r'^경기도\s*','',x['addr'] or '').strip()
def has_jibun(x): return bool(re.search(r'\d+(-\d+)?$',akey(x)))
prevp=f'{here}/data/latest.json';prev=json.load(open(prevp)) if os.path.exists(prevp) else {'ok':{}}
def extra(x):
    t=x['txt'] or ''
    if x['id'] in BL: return '블랙리스트: '+BL[x['id']]
    if '옥탑' in t: return '옥탑방'
    if re.search(r'재래시장|시장\s*(인접|바로|앞|옆)',t): return '재래시장 인접'
    if x['af'] and x['fl']>x['af']: return '층 정보 모순'
    return None
seen=set();ok=[]
for x in sorted(R,key=lambda x:x['src']!='직방'):
    if x['res']!='OK': continue
    why=extra(x)
    if why: x['res']=why;continue
    k=(x['addr'],x['fl'],x['dep'],x['rent'])
    if k in seen: continue
    import math
    if any(y['fl']==x['fl'] and y['dep']==x['dep'] and y['rent']==x['rent'] and x.get('lat') and y.get('lat') and math.hypot((x['lat']-y['lat'])*111000,(x['lng']-y['lng'])*88000)<80 for y in ok): continue   # 직방·다방 중복(다방은 지번 비공개)
    seen.add(k);ok.append(x)
EVA={akey(y) for y in R if y['ev'] is True and has_jibun(y)}
for x in ok:
    if x['ev'] is not True and has_jibun(x) and akey(x) in EVA: x['ev']=True   # 같은 건물 다른 매물에 엘리베이터 있음
DG={}
try: DG=json.load(open(f'{here}/data/daangn_buildings.json'))   # daangn_region.py 결과(당근 후기 전체, 건물별 자동 판정)
except Exception: pass
def rvget(x):
    k=akey(x);m=RV.get(k)
    if m and m['v'] in ('good','bad'): return m          # 사람이 확인한 판정 우선
    d=DG.get(k)
    if d: return {'v':d['v'],'auto':1,'why':'당근(자동) '+str(d['n'])+'개 중 나쁨 '+str(d['bad'])+': '+' / '.join(r['txt'][:60].replace('\n',' ') for r in d['reviews'][:2])}
    return m
nok=[]
for x in ok:
    r=rvget(x)
    if r and r['v']=='bad': x['res']='나쁜 후기: '+r['why']
    else: nok.append(x)
ok=nok
def rv(x):
    r=rvget(x)
    return 'O' if r and r['v']=='good' else 'X' if r else '?'
def score(x):
    t=x['txt'] or '';s=0
    s+=3 if x['fl']>=6 else 1
    s+=1 if x['ev'] is True else 0
    s-=2 if (x['ev'] is False and not re.search(r'엘리베이터|엘레베이터|엘베|승강기',x['txt'] or '')) else 0   # 사용자: 엘베 없는 건 크다
    s+=2 if ('오피스텔' in x['type'] or x['hh']>=30 or re.search(r'경비|관리실|관리사무소',t)) else 0
    s+=2 if x['yr']>=2016 else 1 if x['yr']>=2008 else 0
    s+=2 if x['py']>=7 else 1 if x['py']>=6 else 0
    top=bool(x['af'] and x['fl']==x['af'])
    s+=(2 if x['yr']>=2008 else 1) if top else 0   # 사용자: 층간소음이 큰 단점 → 위층 없는 꼭대기층 가점(옛 건물은 누수 위험으로 작게)
    s-=1 if '대로변' in t else 0
    s+=1 if x['tot']<=60 else 0
    s+=1 if (x['hh']>=100 or re.search(r'24시',t)) else 0
    s-=1 if x['py']<5.5 else 0
    return s
for x in ok:
    x['score']=score(x);x['flags']=([x['note']] if x.get('note') else [])+(['좋은 후기: '+rvget(x).get('why','')] if rv(x)=='O' else [])+(['지번 비공개라 후기 조회 불가'] if not has_jibun(x) else [])+[f for f,c in [('꼭대기층: 위층 소음 없음, 누수·더위 확인',x['af'] and x['fl']==x['af']),('대로변','대로변' in (x['txt'] or '')),('학교 앞','계원' in (x['txt'] or '')),('엘리베이터 없음',x['ev'] is False)] if c]
ok.sort(key=lambda x:(rv(x)!='O',-x['score'],x['tot']))   # 좋은 후기 있는 곳이 맨 위
allids={x['id'] for x in R}
def next_weekday_9():
    d=datetime.date.today()+datetime.timedelta(days=1)
    while d.weekday()>=5: d+=datetime.timedelta(days=1)
    return calendar.timegm(datetime.datetime(d.year,d.month,d.day,9,0).timetuple())
TR={}
top=[x for x in ok[:15] if x.get('lat')]
if top and os.environ.get('NO_TRANSIT')!='1':
    pp=f'{day}/pts.json';op=f'{day}/transit.json'
    json.dump([[x['id'],x['lat'],x['lng']] for x in top],open(pp,'w'))
    try:
        subprocess.run(['node',f'{here}/transit.js',pp,'37.39228,126.95653',str(next_weekday_9()),op],timeout=900,capture_output=True)
        TR=json.load(open(op))
    except Exception as e: print('대중교통 조회 실패:',e)
def transit(x):
    t=TR.get(x['id'])
    if not t: return '-'
    if 'err' in t: return '조회 실패'
    m=re.search(r'(\d+시간\s*)?\d+분',t['t']);rides=[k for k in t['modes'] if k not in ('대중교통','도보','다음')]
    parts=[s.strip() for s in t['t'].split('|')];veh=re.sub(r'\s+',' ',parts[3]).strip() if len(parts)>3 else ''
    kind=rides[0] if rides else ''
    return f"{m.group(0) if m else '?'}, 환승 {max(len(rides)-1,0)}회 ({kind} {veh})"

new=[x for x in ok if x['id'] not in prev['ok']]
gone=[(i,v) for i,v in prev['ok'].items() if i not in allids]
out=[f"후기: O=좋은 후기 있음(맨 위), X=찾아봤지만 없음, ?=지번 비공개 등으로 못 찾음. 나쁜 후기가 있는 곳은 제외.\n",f"## 오늘 순위 (보증금 2천 이하, 월 실부담 70만 이하, 아크로 3km)\n","| 순위(후기 O/X/? · 전날 대비) | 매물 | 평수 | 보증금 | 월 실부담(월세+관리비+전기·가스 추정) | 회사까지(직선/도보) | 대중교통 | 입주 | 주의 | 링크 |","|---|---|---|---|---|---|---|---|---|---|"]
def arrow(x,n):
    p=prev['ok'].get(x['id'])
    if not p: return '🆕'
    if not p.get('rank'): return ''
    d=p['rank']-n
    return '–' if d==0 else (f'▲{d}' if d>0 else f'▼{-d}')
for n,x in enumerate(ok[:15],1):
    nm=f"{(x['addr'] or '').replace('경기도 ','')} {x['type'][:8]} {x['af']}층 건물의 {x['fl']}층 ({x['yr'] or '?'}년)"
    out.append(f"| {rv(x)} {n} {arrow(x,n)} | {nm} | {x['py']}평 | {x['dep']:,}만 | 약 {x['tot']}만 | {x['d']/1000:.1f}km ({round(x['d']*1.3/80)}분) | {transit(x)} | {re.sub(r'\s*이후 입주 가능|입주 가능|입주','',x.get('movein') or '?').replace('2026.','').strip() or '?'} | {', '.join(x['flags']) or '-'} | [{x['src']}]({x['link']}) |")
near=[]
seen2=set()
for x in R:
    if x in ok or not re.match(r'예산|6평 미만|용도',x['res'] or '') or x['tot']>85 or not has_jibun(x): continue
    r=rvget(x)
    if not (r and r['v']=='good'): continue
    k2=(akey(x),x['fl'],x['rent'])
    if k2 in seen2: continue
    seen2.add(k2);near.append(x)
if near:
    out.append("\n## 좋은 후기 건물인데 조건 일부 초과 (월 실부담 85만 이하)\n")
    out.append(out[2]);out.append(out[3])
    for n,x in enumerate(sorted(near,key=lambda x:x['tot']),1):
        x['flags']=['조건 초과: '+x['res'],'좋은 후기: '+rvget(x).get('why','')]
        nm=f"{(x['addr'] or '').replace('경기도 ','')} {x['type'][:8]} {x['af']}층 건물의 {x['fl']}층 ({x['yr'] or '?'}년)"
        out.append(f"| O {n} | {nm} | {x['py']}평 | {x['dep']:,}만 | 약 {x['tot']}만 | {x['d']/1000:.1f}km ({round(x['d']*1.3/80)}분) | - | {re.sub(r'\s*이후 입주 가능|입주 가능|입주','',x.get('movein') or '?').replace('2026.','').strip() or '?'} | {', '.join(x['flags'])} | [{x['src']}]({x['link']}) |")
out.append(f"\n- 새 매물(통과): {len(new)}건 / 어제 후보 중 사라짐: {len(gone)}건")
for i,v in gone: out.append(f"  - 사라짐: {v['name']} ({v['price']})")
print('\n'.join(out))
json.dump({'ok':{x['id']:{'name':f"{x['addr']} {x['fl']}층",'price':f"{x['dep']}/{x['rent']}",'link':x['link'],'rank':n} for n,x in enumerate(ok,1)}},open(prevp,'w'),ensure_ascii=False,indent=0)
