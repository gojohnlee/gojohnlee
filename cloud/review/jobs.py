import sys, re, json, collections, os, math, unicodedata
R='/home/user/valheim-refs'; sys.path.insert(0,R+'/cloud'); import chunk
exec(open('build.py').read().split("cnt=collections")[0])
pos={i:k for k,i in enumerate(order)}
groups=collections.defaultdict(list)
for i in order: groups[assign[i]].append(i)
def slug(c):
    s=unicodedata.normalize('NFKD',c).encode('ascii','ignore').decode().lower()
    return re.sub(r'[^a-z0-9]+','-',s).strip('-')
CAP=700; jobs=[]
chars=sorted([c for c in groups if not c.startswith('#')], key=lambda c:-len(groups[c]))
bins=[]
for c in chars:
    ids=groups[c]; n=len(ids)
    if n>=350:
        p=math.ceil(n/CAP); sz=math.ceil(n/p)
        for q in range(p): jobs.append({'name':slug(c)+('-%d'%(q+1) if p>1 else ''),'chars':[c],'ids':ids[q*sz:(q+1)*sz]})
    else:
        for b in bins:
            if len(b['ids'])+n<=CAP: b['chars'].append(c); b['ids']+=ids; break
        else: bins.append({'chars':[c],'ids':list(ids)})
for k,b in enumerate(bins): b['name']='%s'%('group%02d'%(k+1)); jobs.append(b)
# narration: pack consecutive sections to ~800
cur=None
for s in sorted(c for c in groups if c.startswith('#')):
    ids=groups[s]
    if cur and len(cur['ids'])+len(ids)<=800: cur['ids']+=ids; cur['chars'].append(s)
    else:
        cur={'chars':[s],'ids':list(ids)}; jobs.append(cur)
for j in jobs:
    if j['chars'][0].startswith('#'): j['name']='narration-'+'-'.join(c[2:] for c in (j['chars'][0],j['chars'][-1]))
for j in jobs:
    j['ids'].sort(key=lambda i:pos[i])
    L=[]; ws=sorted({pos[i]//1000 for i in j['ids']})
    narr=j['chars'][0].startswith('#')
    L.append('# 검수 입력: %s'%j['name']); L.append('관련 구간 줄거리 파일(필요할 때 읽기): '+', '.join('cloud/context/w%02d.md'%w for w in ws))
    if not narr:
        L.append('\n## 인물 소개(characters.md)')
        for c in j['chars']: L.append(prof.get(c,'### %s — characters.md에 항목 없음. 신분·나이·관계를 앞뒤 줄로 판단.'%c))
    L.append('\n## 줄 (줄번호|ID|화자|단락수|영어|지금 한국어)')
    last=None
    for i in j['ids']:
        k=pos[i]
        if not narr and len(j['chars'])>1 and assign[i]!=last: L.append('\n### ▶ %s'%assign[i]); last=assign[i]
        elif last is not None and False: pass
        n=len(re.split(r'<br>',by[i]['Korean'],flags=re.I))
        L.append('%d|%s|%s|%d|%s|%s'%(k,i,sp.get(i,''),n,chunk.plain(by[i]['EN']),done[i]))
    j['file']='rv/in-%s.txt'%j['name']
    open(j['file'],'w',encoding='utf-8').write('\n'.join(L)+'\n')
json.dump([{k:v for k,v in j.items() if k!='ids'}|{'n':len(j['ids'])} for j in jobs],open('rv/jobs.json','w'),ensure_ascii=False,indent=0)
for j in jobs: print(j['name'],len(j['ids']),os.path.getsize(j['file'])//1024,'KB',','.join(j['chars'])[:80])
print(len(jobs))
