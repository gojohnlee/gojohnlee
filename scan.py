# 사용: python3 -I scan.py raw/<날짜>
# 직방·다방에서 아크로 3km 안 월세(보증금≤2000, 월세≤68) 목록을 받고, 상세는 data/cache.json에 없는 것·7일 지난 것·가격 바뀐 것만 새로 받는다.
import json,urllib.request,urllib.error,urllib.parse,math,os,sys,time,datetime
RMAX=int(os.environ.get("RENT_MAX","68"))   # 기본 68, 넓혀 볼 때 RENT_MAX=85
here=os.path.dirname(os.path.abspath(__file__));day=sys.argv[1]
os.makedirs(f'{day}/zb',exist_ok=True);os.makedirs(f'{day}/db',exist_ok=True)
T=(37.39228,126.95653);TODAY=datetime.date.today();REFRESH=7;DROP=14
cp=f'{here}/data/cache.json';cache=json.load(open(cp)) if os.path.exists(cp) else {}
ZK=['itemId','price','manageCost','location','title','description','serviceType','residenceType','floor','approveDate','area','elevator','jibunAddress','addressOrigin','salesType','roomDirection','options','parkingCountText','moveinDate']
DK=['seq','id','shorten_url','price_title','maintenance_cost_str','location','building_use_types_str','room_floor_str','building_floor_str','building_approval_date_str','room_size','elevator_str','household_num_str','full_jibun_address_str','title','memo','direction_str','room_options','moving_date']
def dist(a,c):
    R=6371000;p1,p2=math.radians(a[0]),math.radians(c[0]);dl=math.radians(c[1]-a[1]);dp=p2-p1
    return round(2*R*math.asin(math.sqrt(math.sin(dp/2)**2+math.cos(p1)*math.cos(p2)*math.sin(dl/2)**2)))
def get(u,h):
    for t in range(3):
        time.sleep(0.3+2*t)
        try: return json.loads(urllib.request.urlopen(urllib.request.Request(u,headers=h),timeout=25).read())
        except urllib.error.HTTPError: raise
        except Exception:
            if t==2: raise
def fresh(key,price=None):
    c=cache.get(key)
    if not c: return False
    if price is not None and c.get('price')!=price: return False
    if key[0]=='d' and 'moving_date' not in c['data'].get('room',{}): return False   # 입주일 필드 추가 전 캐시
    return (TODAY-datetime.date.fromisoformat(c['fetched'])).days<REFRESH
n_new=0
HZ={'User-Agent':'Mozilla/5.0'};m=urllib.parse.quote("월세");ids={}
for kind in['onerooms','officetels','villas']:
  for g in ['wydk8','wydk9','wydkb','wydkc']:
    u=f"https://apis.zigbang.com/house/property/v1/items/{kind}?geohash={g}&depositMin=0&depositMax=2000&rentMin=0&rentMax={RMAX}&salesTypes[0]={m}&domain=zigbang&checkAnyItemWithoutFilter=true"
    try: d=get(u,HZ)
    except Exception as e: print('ZERR',kind,g,e);continue
    for it in d.get('items',[]):
        if dist(T,(it['lat'],it['lng']))<=3000: ids[str(it['id'])]=1
for i in ids:
    k='z'+i
    if not fresh(k):
        try:
            it=get(f"https://apis.zigbang.com/v3/items/{i}?version=&domain=zigbang",HZ);it=it.get('item',it)
            cache[k]={'fetched':TODAY.isoformat(),'data':{x:it.get(x) for x in ZK}};n_new+=1
        except Exception as e: print('ZERR',i,e);continue
    cache[k]['seen']=TODAY.isoformat()
    json.dump(cache[k]['data'],open(f'{day}/zb/{i}.json','w'),ensure_ascii=False)
HD={'User-Agent':'Mozilla/5.0','D-Api-Version':'5.0.0','D-Call-Type':'web','csrf':'token','Referer':'https://www.dabangapp.com/'}
bbox={"sw":{"lat":37.365,"lng":126.922},"ne":{"lat":37.420,"lng":126.991}}
f={"sellingTypeList":["MONTHLY_RENT"],"depositRange":{"min":0,"max":2000},"priceRange":{"min":0,"max":RMAX},"isIncludeMaintenance":False,"pyeongRange":{"min":0,"max":999999},"useApprovalDateRange":{"min":0,"max":999999},"roomFloorList":["GROUND_FIRST","GROUND_SECOND_OVER","SEMI_BASEMENT","ROOFTOP"],"roomTypeList":["ONE_ROOM","TWO_ROOM"],"dealTypeList":["AGENT"],"canParking":False,"isShortLease":False,"hasElevator":False,"hasPano":False,"isDivision":False,"isDuplex":False}
L=[]
for page in range(1,30):
    u="https://www.dabangapp.com/api/v5/room-list/category/one-two/bbox?"+urllib.parse.urlencode({"bbox":json.dumps(bbox),"filters":json.dumps(f),"page":page,"useMap":"naver","zoom":14})
    try: res=get(u,HD)['result']
    except Exception as e: print('DERR',e);break
    L+=res.get('roomList') or []
    if not res.get('hasMore'): break
for x in L:
    k='d'+x['id']
    if not fresh(k,x.get('priceTitle')):
        try:
            d=get(f"https://www.dabangapp.com/api/3/new-room/detail?api_version=3.5.0&call_type=web&room_id={x['id']}&version=1",HD)['room']
            cache[k]={'fetched':TODAY.isoformat(),'price':x.get('priceTitle'),'data':{'room':{y:d.get(y) for y in DK}}};n_new+=1
        except Exception as e: print('DERR',x['id'],e);continue
    cache[k]['seen']=TODAY.isoformat()
    json.dump(cache[k]['data'],open(f"{day}/db/{x['id']}.json",'w'),ensure_ascii=False)
cache={k:v for k,v in cache.items() if (TODAY-datetime.date.fromisoformat(v.get('seen',v['fetched']))).days<DROP}
json.dump(cache,open(cp,'w'),ensure_ascii=False,separators=(',',':'))
print('zigbang',len(ids),'dabang',len(L),'상세 새로 받음',n_new,'캐시',len(cache))
