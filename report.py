# 사용: python3 -I report.py raw/<날짜>   → 순위·신규·사라짐을 마크다운으로 출력, data/latest.json 갱신
import json,re,sys,os
day=sys.argv[1];here=os.path.dirname(os.path.abspath(__file__))
R=json.load(open(f'{day}/recs.json'));BL=json.load(open(f'{here}/blacklist.json'))
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
    seen.add(k);ok.append(x)
def score(x):
    t=x['txt'] or '';s=0
    s+=3 if x['fl']>=6 else 1
    s+=1 if x['ev'] is True else 0
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
    x['score']=score(x);x['flags']=([x['note']] if x.get('note') else [])+[f for f,c in [('꼭대기층: 위층 소음 없음, 누수·더위 확인',x['af'] and x['fl']==x['af']),('대로변','대로변' in (x['txt'] or '')),('학교 앞','계원' in (x['txt'] or '')),('엘리베이터 없음',x['ev'] is False)] if c]
ok.sort(key=lambda x:(-x['score'],x['tot']))
allids={x['id'] for x in R}
new=[x for x in ok if x['id'] not in prev['ok']]
gone=[(i,v) for i,v in prev['ok'].items() if i not in allids]
out=[f"## 오늘 순위 (보증금 2천 이하, 월 실부담 64만 이하, 아크로 3km)\n","| 순위(전날 대비) | 매물 | 크기 | 조건 | 월 실부담 | 주의 | 링크 |","|---|---|---|---|---|---|---|"]
def arrow(x,n):
    p=prev['ok'].get(x['id'])
    if not p: return '🆕'
    if not p.get('rank'): return ''
    d=p['rank']-n
    return '–' if d==0 else (f'▲{d}' if d>0 else f'▼{-d}')
for n,x in enumerate(ok[:15],1):
    nm=f"{(x['addr'] or '').replace('경기도 ','')} {x['type'][:8]} {x['af']}층 건물의 {x['fl']}층 ({x['yr'] or '?'}년)"
    out.append(f"| {n} {arrow(x,n)} | {nm} | {x['py']}평 | {x['dep']}/{x['rent']}+관리 {x['mc']} | 약 {x['tot']}만 | {', '.join(x['flags']) or '-'} | [{x['src']}]({x['link']}) |")
out.append(f"\n- 새 매물(통과): {len(new)}건 / 어제 후보 중 사라짐: {len(gone)}건")
for i,v in gone: out.append(f"  - 사라짐: {v['name']} ({v['price']})")
print('\n'.join(out))
json.dump({'ok':{x['id']:{'name':f"{x['addr']} {x['fl']}층",'price':f"{x['dep']}/{x['rent']}",'link':x['link'],'rank':n} for n,x in enumerate(ok,1)}},open(prevp,'w'),ensure_ascii=False,indent=0)
