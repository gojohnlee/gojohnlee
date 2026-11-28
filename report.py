# 사용: python3 -I report.py raw/<날짜>   → 순위·신규·사라짐을 마크다운으로 출력, data/latest.json 갱신
import json,re,sys,os,subprocess,datetime,calendar,collections
day=sys.argv[1];here=os.path.dirname(os.path.abspath(__file__))
R=json.load(open(f'{day}/recs.json'));BL=json.load(open(f'{here}/blacklist.json'))
RVp=f'{here}/reviews.json';RV=json.load(open(RVp))
BADW=r'벌레|바퀴|소음|시끄|방음|층간|들려|들림|경매|빌라왕|곰팡|누수|결로|보증금.{0,6}(안|못|늦)'
def movein(x):
    return re.sub(r'\s*이후 입주 가능|입주 가능|입주', '', x.get('movein') or '?').replace('2026.', '').strip() or '?'


def akey(x): return re.sub(r'^경기도\s*','',x['addr'] or '').strip()
def has_jibun(x): return bool(re.search(r'\d+(-\d+)?$',akey(x)))
prevp=f'{here}/data/latest.json';prev=json.load(open(prevp)) if os.path.exists(prevp) else {'ok':{}}
PROXY={'노후','소음(대로변)'}   # 11-26: 저층(2층) 규칙 없앰
PEN={'안양시 동안구 호계동 970-6':(-100,'바퀴벌레 후기 2/2명'),'안양시 동안구 호계동 996-2':(-100,'고시원(사용자 11-26 확인)'),'의왕시 내손동 739-5':(-100,'고가도로 바로 옆이라 창문 못 엶(사용자 11-26)')}
# 사용자가 후기를 읽고 살린 건물(후기 기준 제외를 무시). 11-25: 관양동 1447-8 외풍·날벌레 1명은 감수
KEEP={'안양시 동안구 관양동 1447-8':'후기 1개 중 외풍·날벌레 1명(감수)'}
# 사용자 11-26: 후기 좋은 건물이라 조건 일부를 눈감고 넣어 봄(사용자가 직접 확인). 지번: (눈감는 조건, 주의 문구)
KEEPF={'안양시 동안구 호계동 1110-2':({'엘리베이터 없음'},'옥탑방·엘리베이터 없음(확인용으로 넣음)'),
       '안양시 만안구 안양동 387-9':({'예산'},'예산 초과(확인용으로 넣음)')}
KEEPTOP={'안양시 동안구 호계동 1110-2'}   # 11-26: 층간소음 후기 있는 건물이라 꼭대기층만 살림
for _k,(_f,_n) in KEEPF.items(): KEEP.setdefault(_k,_n)
def extra(x):
    t=x['txt'] or ''
    if x['id'] in BL: return '블랙리스트: '+BL[x['id']]
    # 사용자 11-25: 옥탑·재래시장은 상관없음(후기로 판단) → 거르지 않음
    if x['af'] and x['fl']>x['af']: return '층 정보 모순'
    return None
seen=set();ok=[]
for x in sorted(R,key=lambda x:x['src']!='직방'):
    # 사용자 11-25: 저층(2층)·노후·대로변은 소음·벌레·설비를 짐작하는 대리 지표일 뿐 → 후기가 있으면 후기로 판단(verdict), 없으면 제외
    kf=KEEPF.get(akey(x)) if has_jibun(x) else None
    if kf and akey(x) in KEEPTOP and not (x['af'] and x['fl']==x['af']): kf=None
    if kf and x['res']!='OK' and set(x['fails'])-kf[0]<=PROXY: x['res']='OK';x['fails']=[f for f in x['fails'] if f not in kf[0]]
    if x['res']!='OK' and not (x.get('fails') and set(x['fails'])<=PROXY): continue
    x['proxy']=[f for f in (x.get('fails') or []) if f in PROXY]
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
# 사용자 11-25: 집품 후기도 쓴다(zippoom.py 결과). 같은 지번이면 당근과 합쳐 하나라도 나쁘면 나쁨.
try:
    for k,z in json.load(open(f'{here}/data/zippoom_buildings.json')).items():
        d=DG.get(k)
        if d:
            rv_=d['reviews']+z['reviews'];DG[k]={'v':'bad' if any(r['bad'] for r in rv_) else 'good','n':len(rv_),'bad':sum(r['bad'] for r in rv_),'reviews':rv_}
        else: DG[k]=z
except Exception: pass
def rvget(x):
    k=akey(x);m=RV.get(k)
    if m and m['v'] in ('good','bad'): return m          # 사람이 확인한 판정 우선
    d=DG.get(k)
    if d: return {'v':d['v'],'auto':1,'reviews':d['reviews'],'why':('당근·집품' if any(r['txt'].startswith('집품') for r in d['reviews']) and any(not r['txt'].startswith('집품') for r in d['reviews']) else '집품' if d['reviews'] and d['reviews'][0]['txt'].startswith('집품') else '당근')+'(자동) '+str(d['n'])+'개 중 나쁨 '+str(d['bad'])+': '+' / '.join(r['txt'][:60].replace('\n',' ') for r in d['reviews'][:2])}
    return m
import math
BADPTS=[(y['lat'],y['lng'],akey(y)) for y in R if y.get('lat') and has_jibun(y) and (rvget(y) or {}).get('v')=='bad']
def near_bad(x):   # 지번 비공개 매물: 60m 안에 나쁜 후기 건물이 있으면 같은 건물이거나 같은 골목으로 본다
    if has_jibun(x) or not x.get('lat'): return None
    for la,ln,k in BADPTS:
        if math.hypot((x['lat']-la)*111000,(x['lng']-ln)*88000)<60: return k
# 사용자 11-25: 위층 소음 후기만 있는 건물은 꼭대기층 매물이면 빼지 않는다(옆집·벽 소음, 벌레 등이 하나라도 있으면 뺀다)
FLOORW=r'층간|윗집|위층|윗층|위에서|발망치|쿵쿵'
WALLW=r'옆 ?집|옆 ?방|옆 ?호|벽|방음|말소리|대화 ?소리|통화|TV|티비|다 ?들|소리가? ?(잘|너무|다) ?들|복도|화장실'
OTHERW=r'벌레|바퀴|경매|빌라왕|곰팡|누수|결로|보증금|쥐|모기|담배|녹물|냄새|시끄|소음 ?심|차 ?소리|도로|기차|지하철|공사|문 ?여닫'
def is_top(x): return bool(x['af'] and x['fl']==x['af'])
def floor_only(r):
    bads=[set(v.get('kinds') or ['other']) for v in (r.get('reviews') or []) if v.get('bad')]
    if bads: return all(b=={'floor'} for b in bads)
    w=r.get('why') or ''
    return bool(re.search(FLOORW,w)) and not re.search(WALLW,w) and not re.search(OTHERW,w)
# 사용자 11-25 규칙(③): 후기 전체(당근+집품) 중 옆집·벽 소리를 쓴 사람이 2명 이상이고 30% 이상이면 제외.
# 층간소음도 같은 기준이되 꼭대기층 매물은 남김. 벌레는 케이스별이라 제외하지 않고 주의 칸에 수만 표시.
OKW2=r'(?:옆 ?집 ?|옆 ?방 ?)?(벌레|소음|층간소음|방음|곰팡이?|누수|결로|냄새|소리)[^.,/!\n]{0,8}(없|잘 ?되|잘 ?됨|잘돼|안 ?나|못 ?들|한 ?번도|전혀|거의 없)'
WALL2=r'옆 ?집|같은 ?층|옆 ?방|벽간|벽 ?소음|방음|말소리|화장실.{0,10}(소리|들)|다 ?들려|소리가? ?(다|너무|잘) ?들'
FLOOR2=r'층간|윗집|위층|윗층|발망치|쿵쿵'
BUG2=r'벌레|바퀴|모기|개미'
OUT2=r'차 ?소리|차량 ?소음|도로 ?소음|대로변|큰 ?길|오토바이|기차 ?소리|지하철 ?소리|유흥|술집'
FIX2=r'수압|싱크|배수|하수구|녹물|온수|보일러|누수|곰팡|결로|물이 ?(새|샌|안 ?나)|(배수|변기|싱크).{0,6}막|역류'   # 사용자 11-25: 오래된 건물은 설비(수압·싱크대 등)를 본다   # 11-26 사용자: 춥고 더운 건(외풍·단열·웃풍) 상관없음
SER2=r'경매|빌라왕|보증금.{0,6}(안|못|늦)|담배|(보증금|전세금).{0,12}(걸림|안 ?돌려|안 ?줌|못 ?받)|나가야 ?(보증금|전세금)'   # 11-26: 509-18 '보증금받는데 1년 반', '전세금 안 돌려주려고함'
def stats(k):
    d=DG.get(k)
    if not d or not d.get('reviews'): return None
    n=w=f=b=sv=o=fx=0;ry=set();rn=0
    for r in d['reviews']:
        t=r['txt'];c=t.split('단점:',1)[1] if '단점:' in t else t
        kw=t.split('단점 키워드:',1)[1] if '단점 키워드:' in t else ''
        c=re.sub(OKW2,'',c);n+=1
        # 11-26: '윗집 의자 끄는 소리가 잘 들려요'가 옆집 소음으로 잘못 세지던 것 — 위층 얘기 문장은 옆집이라 밝히지 않으면 옆집 판정에서 뺌
        cw=' '.join(z for z in re.split(r'[.!?\n…]+',c) if not re.search(FLOOR2,z) or re.search(r'옆 ?집|옆 ?방|벽간|같은 ?층',z))
        w+=bool(re.search(WALL2,cw)) or '벽간소음' in kw
        f+=bool(re.search(FLOOR2,c)) or '층간소음' in kw
        b+=bool(re.search(BUG2,c)) or '벌레' in kw
        c2=re.sub(r'(바퀴|바선생|박휘)[^.!?\n]{0,12}(없|안 ?나|한 ?번도|못 ?봤|본 ?적 ?없)','',c)
        if re.search(r'바퀴|바선생|박휘',c2): rn+=1;ry.add((r.get('date') or '')[:4])
        sv+=bool(re.search(SER2,c))
        o+=bool(re.search(OUT2,c))
        fx+=bool(re.search(FIX2,c))
    return dict(n=n,w=w,f=f,b=b,sv=sv,o=o,fx=fx,rn=rn,ry=sorted(y for y in ry if y))
def hit(c,n): return c/n>=0.3 and (c>=2 or n<=3)   # 11-26 사용자: 20%→30%(여의도자이처럼 후기 많은 건물이 다 걸러져서)   # 후기 3개 이하인 건물은 1명 불만도 제외(11-25: 불만이 없어야 함)
def verdict(x):
    """(제외 사유 또는 None, 주의 문구 또는 None)"""
    k=akey(x);st=stats(k) if has_jibun(x) else None
    if has_jibun(x) and k in KEEP and not (k in KEEPTOP and not is_top(x)): return None,'사용자가 살림: '+KEEP[k]
    if has_jibun(x) and k in PEN: return f"사용자 제외: {PEN[k][1]}",None   # 11-25 사용자: 아예 빼라(번호 바꿔 다시 올려도 건물 지번으로 뺌)
    if st:
        n=st['n']
        if st['rn']>=2 and len(st['ry'])>=2: return f"바퀴벌레 후기 여러 해({'·'.join(st['ry'])}, {st['rn']}/{n}명)",None   # 사용자 11-25: 여러 해에 걸친 바퀴벌레는 소거법으로 뺀다
        if hit(st['w'],n): return f"옆집·벽 소음 후기 {st['w']}/{n}명",None
        if hit(st['f'],n) and not is_top(x): return f"층간소음 후기 {st['f']}/{n}명",None
        if hit(st['o'],n): return f"바깥 소음(차·기차·유흥가) 후기 {st['o']}/{n}명",None   # 사용자 11-24: 소음 없음 필수
        if hit(st['fx'],n): return f"설비(수압·배수·누수·곰팡이·결로 등) 후기 {st['fx']}/{n}명",None
        parts=[f"후기 {n}개"]+[f"{lab} {st[key]}명" for key,lab in (('w','옆집·벽 소리'),('f','층간소음'),('o','바깥 소음'),('fx','설비'),('b','벌레'),('sv','담배·보증금')) if st[key]]
        if hit(st['f'],n) and is_top(x): parts.append('꼭대기층이라 위층 소음은 없음')
        if x.get('proxy'): parts.append('·'.join(x['proxy']).replace('소음(대로변)','대로변')+'이지만 후기상 문제 적음')
        return None,', '.join(parts)
    m=RV.get(k)
    if m and m.get('v')=='bad': return '나쁜 후기: '+m.get('why',''),None
    if x.get('proxy'): return '후기가 없어 확인 불가: '+'·'.join(x['proxy']),None
    return None,None
EXCL={akey(y) for y in R if y.get('lat') and has_jibun(y) and verdict(y)[0]}
BADPTS=[(y['lat'],y['lng'],akey(y)) for y in R if y.get('lat') and akey(y) in EXCL]
# 11-28: 나쁜 건물에 오늘 매물이 없어도 좌표를 알면(직방 상세 캐시) 근처 지번 비공개 매물을 거른다(산본동 259-16 매물이 내려가자 옆 매물이 다시 올라온 일)
try:
    for _v in json.load(open(f'{here}/data/cache.json')).values():
        _d=_v.get('data') or {};_a=re.sub(r'^경기(도)?\s*','',_d.get('jibunAddress') or '').strip();_l=_d.get('location') or {}
        if _a and _l.get('lat') and _a not in EXCL and re.search(r'\d+(-\d+)?$',_a) and _a not in KEEP and verdict({'addr':_a,'af':1,'fl':0})[0]:
            EXCL.add(_a);BADPTS.append((_l['lat'],_l['lng'],_a))
except Exception as e: print('나쁜 건물 좌표 불러오기 실패:',e)
nok=[]
for x in ok:
    nb=near_bad(x);why,note=verdict(x)
    if nb: x['res']='나쁜 후기 건물 60m 안: '+nb
    elif why: x['res']=why
    else:
        if note: x['note']=((x['note']+', ') if x.get('note') else '')+note
        nok.append(x)
ok=nok
def rv(x):
    """O=후기 있고 옆집·층간 소음 불만 없음(꼭대기층이면 층간 무시), △=후기 있고 일부 불만, X=찾아봤지만 없음, ?=지번 비공개 등"""
    st=stats(akey(x)) if has_jibun(x) else None
    if st:   # 사용자 11-25: 후기가 많은 건물이 불리하지 않게 비율로. 각 소음 불만이 10% 미만이면 O
        n=st['n'];return 'O' if st['w']/n<0.1 and st['o']/n<0.1 and st['fx']/n<0.1 and (st['f']/n<0.1 or is_top(x)) else '△'
    m=RV.get(akey(x))
    if m and m.get('v')=='good': return 'O'
    return 'X' if has_jibun(x) else '?'   # 11-25 고침: 지번이 있는데 어디에도 후기가 없으면 X(전에는 reviews.json에 없으면 ?로 나옴)
# 사용자가 직접 뺀 건물(지번별). 11-25: 970-6 바퀴벌레 후기 2/2명 → 처음엔 -100, 곧 '아예 빼라'
def score(x):
    t=x['txt'] or '';s=PEN.get(akey(x),(0,))[0] if has_jibun(x) else 0
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
# 사용자 11-25: 장점도 보여 달라 → 후기 장점 키워드(많이 나온 순)와 첫 후기 장점 앞부분
def pros(x):
    d=DG.get(akey(x)) if has_jibun(x) else None
    if not d or not d.get('reviews'): return None
    kw=collections.Counter();first=''
    for r in d['reviews']:
        t=r['txt'];pr=t.split('장점:',1)[1].split('단점:')[0] if '장점:' in t else ('' if r.get('bad') else t)   # 11-26: 당근 후기는 장점 칸이 없어 좋은 후기 본문을 씀
        pr=re.sub(r'\s+',' ',pr).strip(' |')
        if not first and pr and pr!='-': first=pr
        if '장점 키워드:' in t: kw.update(k.strip() for k in t.split('장점 키워드:',1)[1].split('|')[0].split(',') if k.strip() and k.strip()!='없음')
    ks='·'.join(k for k,_ in kw.most_common(4))
    one=re.split(r'(?<=[.!?])\s',first)[0][:70].replace(',','·')
    v=' / '.join(v for v in (ks,one) if v)
    return '장점: '+v if v else ''
for x in ok:
    x['score']=score(x);x['flags']=[p for p in [pros(x)] if p]+([f"감점 {PEN[akey(x)][0]}({PEN[akey(x)][1]})"] if has_jibun(x) and akey(x) in PEN else [])+([x['note']] if x.get('note') else [])+(['지번 비공개라 후기 조회 불가'] if not has_jibun(x) else [])+[f for f,c in [('꼭대기층: 위층 소음 없음, 누수·더위 확인',x['af'] and x['fl']==x['af']),('대로변','대로변' in (x['txt'] or '')),('학교 앞','계원' in (x['txt'] or '')),('엘리베이터 없음',x['ev'] is False)] if c]
GRP={'O':0,'△':0,'X':1}   # 사용자 11-25: 후기 있는 집·후기 없는 집·지번 비공개(후기 확인 불가)끼리 묶는다
ok.sort(key=lambda x:(GRP.get(rv(x),2),x['score']<=-50,rv(x)!='O',-x['score'],x['tot']))   # 사용자 감점(-100) 받은 곳은 맨 아래   # 좋은 후기 있는 곳이 맨 위
allids={x['id'] for x in R}
def next_weekday_9():
    d=datetime.date.today()+datetime.timedelta(days=1)
    while d.weekday()>=5: d+=datetime.timedelta(days=1)
    return calendar.timegm(datetime.datetime(d.year,d.month,d.day,9,0).timetuple())
# 버스 시간은 건물 위치가 같으면 안 바뀜 → 위치(소수 4자리, 약 10m)별로 data/transit_cache.json에 30일 보관하고 새 위치만 찾는다(11-25: 전에는 매번 15곳을 다시 찾아 몇 분씩 걸림)
TR={};tcp=f'{here}/data/transit_cache.json'
TC=json.load(open(tcp)) if os.path.exists(tcp) else {}
TC={k:v for k,v in TC.items() if (datetime.date.today()-datetime.date.fromisoformat(v['d'])).days<30}
def tkey(x): return f"{x['lat']:.4f},{x['lng']:.4f}"
top=[x for x in ok[:15] if x.get('lat')]
need=[x for x in top if tkey(x) not in TC]
if need and os.environ.get('NO_TRANSIT')!='1':
    pp=f'{day}/pts.json';op=f'{day}/transit.json'
    json.dump([[x['id'],x['lat'],x['lng']] for x in need],open(pp,'w'))
    try:
        subprocess.run(['node',f'{here}/transit.js',pp,'37.39228,126.95653',str(next_weekday_9()),op],timeout=900,capture_output=True)
        got=json.load(open(op))
        for x in need:
            if x['id'] in got and 'err' not in got[x['id']]: TC[tkey(x)]={'d':datetime.date.today().isoformat(),'r':got[x['id']]}
            elif x['id'] in got: TR[x['id']]=got[x['id']]   # 조회 실패는 보관하지 않고 다음에 다시 찾음
    except Exception as e: print('대중교통 조회 실패:',e)
    json.dump(TC,open(tcp,'w'),ensure_ascii=False,separators=(',',':'))
for x in top:
    if tkey(x) in TC: TR[x['id']]=TC[tkey(x)]['r']
print(f'대중교통: 상위 {len(top)}곳 중 새로 찾음 {len(need) if os.environ.get("NO_TRANSIT")!="1" else 0}곳')
def transit(x):
    t=TR.get(x['id'])
    if not t: return '-'
    if 'err' in t: return '조회 실패'
    m=re.search(r'(\d+시간\s*)?\d+분',t['t']);rides=[k for k in t['modes'] if k not in ('대중교통','도보','다음')]
    parts=[s.strip() for s in t['t'].split('|')];veh=re.sub(r'\s+',' ',parts[3]).strip() if len(parts)>3 else ''
    kind=rides[0] if rides else ''
    veh=re.sub(r'[\ue000-\uf8ff]','',veh).strip()   # 구글 지도 아이콘 글꼴 문자(텔레그램에 '稭'처럼 깨져 보임)
    return f"{m.group(0) if m else '?'}, 환승 {max(len(rides)-1,0)}회 ({kind} {veh})"

new=[x for x in ok if x['id'] not in prev['ok']]
gone=[(i,v) for i,v in prev['ok'].items() if i not in allids]
out=[f"후기: O=후기 있고 소음 불만 10% 미만(맨 위), △=후기 있고 일부 불만(주의 칸에 수), X=찾아봤지만 없음, ?=지번 비공개 등으로 못 찾음. 옆집·벽, 층간(꼭대기층 제외), 바깥(차·기차·유흥가) 소음을 쓴 사람이 각각 2명 이상·30% 이상이면 제외.\n",f"## 오늘 순위 (보증금 2천 이하, 월 실부담 70만 이하, 아크로 3km)\n","| 순위(후기 O/X/? · 전날 대비) | 매물 | 평수 | 보증금 | 월 실부담(월세+관리비+전기·가스 추정) | 회사까지(직선/도보) | 대중교통 | 입주 | 주의 | 링크 |","|---|---|---|---|---|---|---|---|---|---|"]
def arrow(x,n):
    p=prev['ok'].get(x['id'])
    if not p: return '🆕'
    if not p.get('rank'): return ''
    d=p['rank']-n
    return '–' if d==0 else (f'▲{d}' if d>0 else f'▼{-d}')
H2,H3=out[2],out[3];del out[2:4];lastg=None
GNAME=['후기 있는 집','후기 없는 집(당근·집품 모두 0개)','지번 비공개라 후기 확인 불가']
for n,x in enumerate(ok[:15],1):
    g=GRP.get(rv(x),2)
    if g!=lastg: out+=[f"\n### {GNAME[g]}\n",H2,H3];lastg=g
    nm=f"{(x['addr'] or '').replace('경기도 ','')} {x['type'][:8]} {x['af']}층 건물의 {x['fl']}층 ({x['yr'] or '?'}년)"
    out.append(f"| {rv(x)} {n} {arrow(x,n)} | {nm} | {x.get('pyd',x['py']):.1f}평 | {x['dep']:,}만 | 약 {x['tot']}만 | {x['d']/1000:.1f}km ({round(x['d']*1.3/80)}분) | {transit(x)} | {movein(x)} | {', '.join(x['flags']) or '-'} | [{x['src']}]({x['link']}) |")
near=[]
seen2=set()
for x in R:
    hard=set(x.get('fails') or [])-PROXY
    if x in ok or not hard or not (hard<={'예산','용도'} and ('용도' not in hard or '생활숙박' in x['type'])) or x['tot']>85 or not has_jibun(x): continue
    x['proxy']=[f for f in (x.get('fails') or []) if f in PROXY]   # 사용자 11-24: 다중생활·근린 등은 보고에서 아예 빼라
    if rv(x)!='O' or verdict(x)[0]: continue
    k2=(akey(x),x['fl'],x['rent'])
    if k2 in seen2: continue
    seen2.add(k2);near.append(x)
if near:
    out.append("\n## 좋은 후기 건물인데 조건 일부 초과 (월 실부담 85만 이하)\n")
    out.append(H2);out.append(H3)
    for n,x in enumerate(sorted(near,key=lambda x:x['tot']),1):
        x['flags']=['조건 초과: '+'·'.join(sorted(set(x.get('fails') or [])-PROXY))]+([verdict(x)[1]] if verdict(x)[1] else [])+([RV[akey(x)]['why']] if RV.get(akey(x),{}).get('why') and RV[akey(x)].get('v')=='good' else [])
        nm=f"{(x['addr'] or '').replace('경기도 ','')} {x['type'][:8]} {x['af']}층 건물의 {x['fl']}층 ({x['yr'] or '?'}년)"
        out.append(f"| O {n} | {nm} | {x.get('pyd',x['py']):.1f}평 | {x['dep']:,}만 | 약 {x['tot']}만 | {x['d']/1000:.1f}km ({round(x['d']*1.3/80)}분) | - | {movein(x)} | {', '.join(x['flags'])} | [{x['src']}]({x['link']}) |")
out.append(f"\n- 새 매물(통과): {len(new)}건 / 어제 후보 중 사라짐: {len(gone)}건")
for i,v in gone: out.append(f"  - 사라짐: {v['name']} ({v['price']})")
print('\n'.join(out))
json.dump({'ok':{x['id']:{'name':f"{x['addr']} {x['fl']}층",'price':f"{x['dep']}/{x['rent']}",'link':x['link'],'rank':n} for n,x in enumerate(ok,1)}},open(prevp,'w'),ensure_ascii=False,indent=0)
