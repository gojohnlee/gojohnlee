# python3 -I uncle_apply.py <n> [write]
import sys
src=open('apply.py').read().split("job=sys.argv[1]")[0]
exec(src)
n=sys.argv[1]
mine={l.split('|')[1] for l in open(D+'/in-uncle-%s.txt'%n) if re.match(r'^\d+\|',l) and l.split('|')[3]!='0'}
fix={};bad=[]
for ln in open(D+'/out-uncle-%s.tsv'%n,encoding='utf-8-sig'):
    c=ln.rstrip('\r\n').split('\t')
    if len(c)<2 or not c[1].strip(): continue
    i=c[0].strip(); t=c[1].strip()
    if i not in mine: bad.append((i,'not in job')); continue
    e=check(i,t)
    if e: bad.append((i,e)); continue
    fix[i]=t
print('fix',len(fix),'bad',len(bad)); [print('BAD',b) for b in bad]
if len(sys.argv)>2:
    p=R+'/translation/retrans/review-zz-uncle.tsv'; old={}
    if os.path.exists(p):
        for ln in open(p,encoding='utf-8'):
            m=chunk.IDRE.match(ln.rstrip('\n'))
            if m: old[m.group(1)]=m.group(2)
    old.update(fix); pos={i:k for k,i in enumerate(order)}
    open(p,'w',encoding='utf-8',newline='\n').write(''.join('%s\t%s\n'%(i,old[i]) for i in sorted(old,key=lambda i:pos[i])))
    print('WROTE',len(old))
