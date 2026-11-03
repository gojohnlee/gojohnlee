import sys,re,collections
exec(open('build.py').read().split("cnt=collections")[0])
pos={i:k for k,i in enumerate(order)}
J=key.get('Jervase','Jervase')
U=re.compile('숙부|제르베스|삼촌|백부')
rows=[]
for k,i in enumerate(order):
    if k<9824: continue
    s=sp.get(i,''); t=done.get(i,'')
    if not t: continue
    narr = s in NARR or s in ('None','')
    if (narr and U.search(t)) or (s=='Mandel' and (assign.get(i)==J or U.search(t))):
        rows.append((k,i,s,len(re.split(r'<br>',by[i]['Korean'],flags=re.I)),chunk.plain(by[i]['EN']),t,assign.get(i)))
print(len(rows), collections.Counter(r[2] for r in rows), J)
open('rv/in-uncle.txt','w').write('# 숙부 높임 정리 입력\n\n## 줄 (줄번호|ID|화자|단락수|영어|지금 한국어)\n'+'\n'.join('%d|%s|%s|%d|%s|%s'%r[:6] for r in rows)+'\n')
