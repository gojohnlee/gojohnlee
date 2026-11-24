import json,glob,re,math,sys,os
day=sys.argv[1];T=(37.39228,126.95653)
def dist(a,c):
    R=6371000;p1,p2=math.radians(a[0]),math.radians(c[0]);dl=math.radians(c[1]-a[1]);dp=p2-p1
    return round(2*R*math.asin(math.sqrt(math.sin(dp/2)**2+math.cos(p1)*math.cos(p2)*math.sin(dl/2)**2)))
bad=('근린','숙박','교육','업무','공장','고시','기숙','여관','상가','다중생활')
def yrof(s): return int((re.findall(r'(19\d\d|20\d\d)',str(s))+['0'])[0])
def flo(s,af=0):
    m=re.match(r'(\d+)',s or '')
    if m: return int(m.group(1))
    s=s or ''
    return (max(af-1,1) if '고' in s else max(af//2,1) if '중' in s else 1) if af else (9 if '고' in s else 5 if '중' in s else 0)
recs=[]
for f in glob.glob(f'{day}/zb/*.json'):
    it=json.load(open(f));p=it['price'];mc=it.get('manageCost') or {};ex=mc.get('notIncludes') or []
    L=it['location'];txt=(it.get('title') or '')+' '+(it.get('description') or '')
    recs.append(dict(src='직방',id=str(it['itemId']),link=f"https://sp.zigbang.com/share/{'officetel' if it.get('serviceType')=='오피스텔' else 'oneroom'}/{it['itemId']}",dep=p['deposit'],rent=p['rent'],mc=mc.get('amount') or 0,util=sum({'전기':3,'가스':3,'수도':1}.get(x,0) for x in ex),
      type=it.get('residenceType') or '',af=flo(str(it['floor']['allFloors'])),fl=flo(str(it['floor']['floor']),flo(str(it['floor']['allFloors']))),yr=yrof(it.get('approveDate')),area=(it.get('area') or {}).get('전용면적M2') or 0,
      ev=bool(it.get('elevator')),hh=0,addr=it.get('jibunAddress') or (it.get('addressOrigin') or {}).get('localText'),d=dist(T,(L['lat'],L['lng'])),txt=txt,title=it.get('title'),sales=it.get('salesType')))
for f in glob.glob(f'{day}/db/*.json'):
    r=json.load(open(f))['room'];mm=re.match(r'(\d+)/(\d+)',(r.get('price_title') or '').replace(',',''))
    if not mm: continue
    loc=r.get('location');lat,lng=(loc[1],loc[0]) if isinstance(loc,list) else (loc['lat'],loc['lng'])
    mc=re.findall(r'[\d.]+',str(r.get('maintenance_cost_str')));hh=re.findall(r'\d+',str(r.get('household_num_str')))
    fs=str(r.get('building_floor_str'))
    recs.append(dict(src='다방',id=str(r['seq']),link=r.get('shorten_url'),dep=int(mm.group(1)),rent=int(mm.group(2)),mc=float(mc[0]) if mc else 0,util=6,
      type=','.join(r.get('building_use_types_str') or []) if isinstance(r.get('building_use_types_str'),list) else str(r.get('building_use_types_str')),af=flo(fs),fl=flo(r.get('room_floor_str'),flo(fs)),yr=yrof(r.get('building_approval_date_str')),area=r.get('room_size') or 0,
      ev=r.get('elevator_str')=='있음',hh=int(hh[0]) if hh else 0,addr=r.get('full_jibun_address_str'),d=dist(T,(lat,lng)),txt=(r.get('title') or '')+' '+(r.get('memo') or ''),title=r.get('title'),sales='월세'))
def judge(x):
    x['tot']=round(x['rent']+x['mc']+x['util']);x['py']=round(x['area']/3.3058,1)
    if x['sales']!='월세' or x['dep']>2000: return '조건밖'
    if x['d']>3000: return '3km밖'
    if any(b in x['type'] for b in bad): return '용도'
    if re.search(r'전입.{0,4}(불가|안됩|안 됩)',x['txt']): return '전입불가'
    if x['fl']<3: return '저층'
    if x['yr'] and x['yr']<2008 and not (x['yr']>=1995 and x['ev'] and ('오피스텔' in x['type'] or x['hh']>=30 or x['af']>=7)): return '노후'
    if x['tot']>64: return '예산'
    return 'OK'
for x in recs: x['res']=judge(x)
json.dump(recs,open(f'{day}/recs.json','w'),ensure_ascii=False)
