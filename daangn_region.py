# 사용: python3 -I daangn_region.py → data/daangn_buildings.json (안양 동안·만안, 의왕, 군포의 당근부동산 '살아본 후기' 전부를 건물(지번)별로 묶고 좋음/나쁨 자동 판정)
# 주소별 조회(livedReviewsByAddress)는 건물명이 붙은 주소와 안 맞아 후기를 놓침(11-24 970-6·971-1) → 지역 전체를 받는다. 약 3분.
import json,subprocess,time,sys,re,collections,os
here=os.path.dirname(os.path.abspath(__file__))
H='304737ba85360e2cc892063d12508accde16434cbb05f2cece753686696b46f6'
out=[]
for rid in [1389,1374,1643,1631]:
    after=None;n=0;t0=time.time()
    while True:
        body=json.dumps({"variables":{"regionId":rid,"first":100,"after":after},"extensions":{"persistedQuery":{"version":1,"sha256Hash":H}}})
        for k in range(3):
            r=subprocess.run(['curl','-s','-m','60','https://realty.kr.karrotmarket.com/graphql','-H','content-type: application/json','-H','x-realty-platform: realty-web','-H','referer: https://realty.daangn.com/','--data-binary',body],capture_output=True,text=True)
            try: d=json.loads(r.stdout)['data']['livedReviewsByRegion'];break
            except Exception: time.sleep(3)
        else: print('fail',rid,n);break
        for e in d['edges']:
            x=e['node'];x.pop('user',None);x['rid']=rid;out.append(x)
        n+=1;after=d['pageInfo']['endCursor']
        if not d['pageInfo']['hasNextPage'] or n>120: break
        time.sleep(0.5)
    print(rid,n,'pages',round(time.time()-t0),'s',flush=True)
BADW=r'벌레|바퀴|소음|시끄|방음|층간|들려|들림|경매|빌라왕|곰팡|누수|결로|보증금.{0,6}(안|못|늦)|쥐|모기|담배|녹물|하수구 ?냄새'
OKW=r'(벌레|소음|층간소음|방음|곰팡이?|누수|결로|냄새|소리)[^.,/!\n]{0,8}(없|잘 ?되|잘 ?됨|잘돼|안 ?나|못 ?들|한 ?번도|전혀|거의 없)'
def base(j):
    m=re.match(r'(경기도 \S+(?: \S+구)? \S+동 \d+(?:-\d+)?)',j or '');return m.group(1) if m else None
B=collections.defaultdict(list)
for x in out:
    t=(x.get('propComplex') or {}).get('type') or x.get('buildingType') or '원룸·빌라'
    k=base(x.get('jibunAddress'))
    if t=='APARTMENT' or not k: continue
    b=x.get('body') or {};neg=b.get('negative') or {}
    txt=x.get('content') or ''
    chk=re.sub(OKW,'',neg.get('content') or '' if b.get('__typename')=='LivedReviewContentV2' else txt)
    bad=bool(set(neg.get('items',[]))&{'SOUNDPROOFING','FLOOR_NOISE','PEST'}) or bool(re.search(BADW,chk)) or b.get('rating')=='BAD'
    B[k].append({'bad':bad,'txt':txt[:300],'date':x['createdAt'][:10],'name':(x.get('propComplex') or {}).get('officialName') or x.get('jibunAddress'),'type':t})
res={re.sub(r'^경기도 ','',k):{'v':'bad' if any(r['bad'] for r in v) else 'good','n':len(v),'bad':sum(r['bad'] for r in v),'reviews':v} for k,v in B.items()}
json.dump(res,open(f'{here}/data/daangn_buildings.json','w'),ensure_ascii=False,indent=0)
print('건물',len(res),'좋음',sum(r['v']=='good' for r in res.values()))
