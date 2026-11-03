import sys, re, json, collections, os
R='/home/user/valheim-refs'; sys.path.insert(0,R+'/cloud'); import chunk
by,order,sp,done,skip=chunk.load()
txt=open(R+'/cloud/context/characters.md',encoding='utf-8').read()
secs=re.split(r'(?m)^(?=### )',txt); prof={}; key={}
for x in secs:
    m=re.match(r'### (.*?)\((.+?)\)',x)
    if not m: continue
    k=m.group(2).split(',')[0].strip(); prof[k]=x.strip()
    for n in m.group(2).split(','): key[n.strip()]=k
LOOSE={'Mandel',''}; NARR={'Narration','PrologueMain'}
def ck(i):
    s=sp.get(i,'');
    if s in LOOSE or s in NARR: return None
    return key.get(s,s)
assign={}
N=len(order)
for k,i in enumerate(order):
    c=ck(i)
    if c: assign[i]=c; continue
    s=sp.get(i,'')
    if s in LOOSE:
        got=None
        for d in range(1,4):
            if k-d>=0:
                j=order[k-d]; 
                if sp.get(j,'') in NARR: break
                if ck(j): got=ck(j); break
        if not got:
            for d in range(1,4):
                if k+d<N:
                    j=order[k+d]
                    if sp.get(j,'') in NARR: break
                    if ck(j): got=ck(j); break
        assign[i]=got or ('#N%02d'%(k//1000))
    else: assign[i]='#N%02d'%(k//1000)
cnt=collections.Counter(assign.values())
json.dump({'assign':assign},open('rv/assign.json','w'))
chars=[(c,v) for c,v in cnt.most_common() if not c.startswith('#')]
print('chars',len(chars)); print(chars[:40]); print(sum(v for c,v in chars if v<150), 'minor lines', sum(1 for c,v in chars if v<150))
print(sorted((c,v) for c,v in cnt.items() if c.startswith('#')))
