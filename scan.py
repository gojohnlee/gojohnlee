import json,urllib.request,urllib.parse,math,os,re,sys,glob
day=sys.argv[1];os.makedirs(f'{day}/zb',exist_ok=True);os.makedirs(f'{day}/db',exist_ok=True)
T=(37.39228,126.95653)
def dist(a,c):
    R=6371000;p1,p2=math.radians(a[0]),math.radians(c[0]);dl=math.radians(c[1]-a[1]);dp=p2-p1
    return round(2*R*math.asin(math.sqrt(math.sin(dp/2)**2+math.cos(p1)*math.cos(p2)*math.sin(dl/2)**2)))
def get(u,h):
    return json.loads(urllib.request.urlopen(urllib.request.Request(u,headers=h),timeout=25).read())
HZ={'User-Agent':'Mozilla/5.0'}
m=urllib.parse.quote("월세");ids={}
for kind in['onerooms','officetels','villas']:
  for g in ['wydk8','wydk9','wydkb','wydkc']:
    u=f"https://apis.zigbang.com/house/property/v1/items/{kind}?geohash={g}&depositMin=0&depositMax=2000&rentMin=0&rentMax=68&salesTypes[0]={m}&domain=zigbang&checkAnyItemWithoutFilter=true"
    try: d=get(u,HZ)
    except Exception as e: print('ZERR',kind,g,e);continue
    for it in d.get('items',[]):
        dd=dist(T,(it['lat'],it['lng']))
        if dd<=3000: ids[str(it['id'])]=dd
for i in ids:
    p=f'{day}/zb/{i}.json'
    if os.path.exists(p): continue
    try: d=get(f"https://apis.zigbang.com/v3/items/{i}?version=&domain=zigbang",HZ);json.dump(d.get('item',d),open(p,'w'),ensure_ascii=False)
    except Exception as e: print('ZERR',i,e)
HD={'User-Agent':'Mozilla/5.0','D-Api-Version':'5.0.0','D-Call-Type':'web','csrf':'token','Referer':'https://www.dabangapp.com/'}
bbox={"sw":{"lat":37.365,"lng":126.922},"ne":{"lat":37.420,"lng":126.991}}
f={"sellingTypeList":["MONTHLY_RENT"],"depositRange":{"min":0,"max":2000},"priceRange":{"min":0,"max":68},"isIncludeMaintenance":False,"pyeongRange":{"min":0,"max":999999},"useApprovalDateRange":{"min":0,"max":999999},"roomFloorList":["GROUND_FIRST","GROUND_SECOND_OVER","SEMI_BASEMENT","ROOFTOP"],"roomTypeList":["ONE_ROOM","TWO_ROOM"],"dealTypeList":["AGENT"],"canParking":False,"isShortLease":False,"hasElevator":False,"hasPano":False,"isDivision":False,"isDuplex":False}
L=[]
for page in range(1,30):
    u="https://www.dabangapp.com/api/v5/room-list/category/one-two/bbox?"+urllib.parse.urlencode({"bbox":json.dumps(bbox),"filters":json.dumps(f),"page":page,"useMap":"naver","zoom":14})
    try: res=get(u,HD)['result']
    except Exception as e: print('DERR',e);break
    L+=res.get('roomList') or []
    if not res.get('hasMore'): break
for x in L:
    p=f"{day}/db/{x['id']}.json"
    if os.path.exists(p): continue
    try:
        d=get(f"https://www.dabangapp.com/api/3/new-room/detail?api_version=3.5.0&call_type=web&room_id={x['id']}&version=1",HD)
        for k in('agent','contact','other_rooms'): d.pop(k,None)
        json.dump(d,open(p,'w'),ensure_ascii=False)
    except Exception as e: print('DERR',x['id'],e)
print('zigbang',len(ids),'dabang',len(L))
