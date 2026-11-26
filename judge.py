import json,glob,re,math,sys,os,datetime
day=sys.argv[1];T=(37.39228,126.95653)
def dist(a,c):
    R=6371000;p1,p2=math.radians(a[0]),math.radians(c[0]);dl=math.radians(c[1]-a[1]);dp=p2-p1
    return round(2*R*math.asin(math.sqrt(math.sin(dp/2)**2+math.cos(p1)*math.cos(p2)*math.sin(dl/2)**2)))
bad=('근린','숙박','교육','업무','공장','고시','기숙','여관','상가','다중생활')
def yrof(s): return int((re.findall(r'(19\d\d|20\d\d)',str(s))+['0'])[0])
HIGH1=r'필로티|높은\s*1\s*층|1\.5\s*층|반\s*층\s*(위|올라)|도로보다\s*높|지대가?\s*높|계단\s*(올라|위)|2\s*층\s*(같은|수준|높이)|1\s*층\s*같지\s*않'
def flo(s,af=0):
    s=s or ''
    if re.search(r'지하|지층|^B',s): return 0   # 사용자 11-26: 반지하·지하는 뺀다(예전엔 '반지하'가 1층으로 읽혔음)
    m=re.match(r'(\d+)',s)
    if m: return int(m.group(1))
    return (max(af-1,1) if '고' in s else max(af//2,1) if '중' in s else 1) if af else (9 if '고' in s else 5 if '중' in s else 0)
recs=[]
for f in glob.glob(f'{day}/zb/*.json'):
    it=json.load(open(f));p=it['price'];mc=it.get('manageCost') or {};ex=mc.get('notIncludes') or []
    L=it['location'];txt=(it.get('title') or '')+' '+(it.get('description') or '')
    recs.append(dict(src='직방',id=str(it['itemId']),link=f"https://sp.zigbang.com/share/{'officetel' if it.get('serviceType')=='오피스텔' else 'oneroom'}/{it['itemId']}",dep=p['deposit'],rent=p['rent'],mc=mc.get('amount') or 0,util=sum({'전기':3,'가스':3,'수도':1}.get(x,0) for x in ex),
      type=it.get('residenceType') or '',af=flo(str(it['floor']['allFloors'])),fl=flo(str(it['floor']['floor']),flo(str(it['floor']['allFloors']))),yr=yrof(it.get('approveDate')),area=(it.get('area') or {}).get('전용면적M2') or 0,
      ev=bool(it.get('elevator')),hh=0,addr=it.get('jibunAddress') or (it.get('addressOrigin') or {}).get('localText'),d=dist(T,(L['lat'],L['lng'])),lat=L['lat'],lng=L['lng'],txt=txt,title=it.get('title'),sales=it.get('salesType'),movein=it.get('moveinDate') or ''))
for f in glob.glob(f'{day}/db/*.json'):
    r=json.load(open(f))['room'];mm=re.match(r'(\d+)/(\d+)',(r.get('price_title') or '').replace(',',''))
    if not mm: continue
    loc=r.get('location');lat,lng=(loc[1],loc[0]) if isinstance(loc,list) else (loc['lat'],loc['lng'])
    mc=re.findall(r'[\d.]+',str(r.get('maintenance_cost_str')));hh=re.findall(r'\d+',str(r.get('household_num_str')))
    fs=str(r.get('building_floor_str'))
    recs.append(dict(src='다방',id=str(r['seq']),link=r.get('shorten_url'),dep=int(mm.group(1)),rent=int(mm.group(2)),mc=float(mc[0]) if mc else 0,util=6,
      type=','.join(r.get('building_use_types_str') or []) if isinstance(r.get('building_use_types_str'),list) else str(r.get('building_use_types_str')),af=flo(fs),fl=flo(r.get('room_floor_str'),flo(fs)),yr=yrof(r.get('building_approval_date_str')),area=r.get('room_size') or 0,
      ev=r.get('elevator_str')=='있음',hh=int(hh[0]) if hh else 0,addr=r.get('full_jibun_address_str'),d=dist(T,(lat,lng)),lat=lat,lng=lng,txt=(r.get('title') or '')+' '+(r.get('memo') or ''),title=r.get('title'),sales='월세',movein=r.get('moving_date') or ''))
np=os.path.join(os.path.dirname(os.path.abspath(__file__)),'data','naver','latest.json')
if os.path.exists(np):
    N=json.load(open(np,encoding='utf-8-sig'))
    age=(datetime.datetime.now()-datetime.datetime.fromisoformat(str(N.get('fetched'))[:19])).days
    items=N.get('items') or []
    if isinstance(items,dict): items=[items]
    print('naver',N.get('status'),len(items),'건,',age,'일 전 수집')
    for a in (items if age<=2 else []):
        try:
            fs=str(a.get('flrInfo') or '');f1,f2=(fs.split('/')+['',''])[:2]
            af=flo(f2);txt=' '.join([str(a.get('atclNm') or ''),str(a.get('atclFetrDesc') or ''),' '.join(a.get('tagList') or [])])
            recs.append(dict(src='네이버',id='n'+str(a['atclNo']),link=f"https://m.land.naver.com/article/info/{a['atclNo']}",dep=int(a.get('prc') or 0),rent=int(a.get('rentPrc') or 0),mc=8,util=6,
              type=str(a.get('rletTpNm') or ''),fl=flo(f1,af) if f1[:1]!='B' else 0,af=af,yr=0,area=float(a.get('spc2') or 0),ev=None,hh=0,addr=str(a.get('bildNm') or a.get('atclNm') or ''),
              d=dist(T,(float(a['lat']),float(a['lng']))),lat=float(a['lat']),lng=float(a['lng']),txt=txt,title=a.get('atclNm'),sales='월세' if a.get('tradTpCd')=='B2' else '기타',note='관리비·연식·엘리베이터 미확인'))
        except Exception as e: print('naver skip',a.get('atclNo'),e)
def judge(x):
    """걸린 사유를 전부 x['fails']에 모으고 첫 사유를 돌려준다(사용자 11-25: 저층·노후·대로변은 후기로 판단하는 '대리 지표'라 report.py가 후기와 함께 다시 본다)"""
    x['tot']=round(x['rent']+x['mc']+x['util']);x['py']=round(x['area']/3.3058,1);x['pyd']=int(x['area']/3.3058*10+1e-9)/10  # 사용자 11-26: 보고 표시는 소수 첫째 자리 버림(23㎡=6.96→6.9). 거름·점수는 반올림 그대로(19.8㎡=광고 6평이 5.9로 빠지지 않게)
    f=[]
    if x['sales']!='월세' or x['dep']>2000: f.append('조건밖')
    if x['d']>3000: f.append('3km밖')
    if any(b in x['type'] for b in bad):
        # 사용자 11-24: 근린생활·다중생활이라도 전입 가능이면 후보에 넣는다(주의 칸에 '서류상 집 아님' 표시)
        if re.search(r'근린|다중생활|상가',x['type']):   # 전입 불가가 적힌 건 아래 전입불가 규칙이 거른다
            ok_=re.search(r'전입\s*(신고)?\s*(가능|ok|OK|O\b|됩니다|돼요)',x['txt'] or '')
            x['note']=((x.get('note')+', ') if x.get('note') else '')+('서류상 집 아님(전입 가능 표기)' if ok_ else '서류상 집 아님(전입 가능한지 중개사에게 확인)')
        else: f.append('용도')
    if re.search(r'전입\s*(신고)?\s*[은이는가]?\s*(불가|안\s*됩|안\s*됨|x|X|×|안\s*돼)',x['txt']): f.append('전입불가')
    # 사용자 11-26: 1·2층도 보여 준다. 반지하·지하는 뺀다. 1층은 길보다 높은 1층(0층 개념: 필로티·계단 올라가는 1층)만 — 설명에 적힌 것만 통과
    if x['fl']<1: f.append('반지하·지하')
    elif x['fl']==1:
        if re.search(HIGH1,x['txt'] or ''): x['note']=((x.get('note')+', ') if x.get('note') else '')+'길보다 높은 1층(설명에 적힘)'
        else: f.append('1층(길보다 높은지 모름)')
    if x['yr'] and x['yr']<2000 and not (x['yr']>=1995 and x['ev'] and ('오피스텔' in x['type'] or x['hh']>=30 or x['af']>=7)): f.append('노후')
    if x['tot']>70: f.append('예산')
    if x['py']<6: f.append('6평 미만')
    if '대로변' in (x['txt'] or ''): f.append('소음(대로변)')
    if x['ev'] is False and not re.search('엘리베이터|엘레베이터|엘베|승강기',x['txt'] or ''): f.append('엘리베이터 없음')
    x['fails']=f
    return f[0] if f else 'OK'
for x in recs: x['res']=judge(x)
json.dump(recs,open(f'{day}/recs.json','w'),ensure_ascii=False)
