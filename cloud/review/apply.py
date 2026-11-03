# usage: python3 -I apply.py <job>            -> validate out, show ? lines
#        python3 -I apply.py <job> write      -> merge picks, write review files, print samples
import sys, re, json, os, collections, random
R='/home/user/valheim-refs'; sys.path.insert(0,R+'/cloud'); import chunk
D=os.path.dirname(os.path.abspath(__file__))+'/rv'
by,order,sp,done,skip=chunk.load()
import glob
done={}
for f in sorted(glob.glob(R+'/translation/retrans/*.tsv')):
    if os.path.basename(f).startswith('review-'): continue
    for ln in open(f,encoding='utf-8-sig'):
        m=chunk.IDRE.match(ln.rstrip('\r\n'))
        if m: done[m.group(1)]=m.group(2)
assign=json.load(open(D+'/assign.json'))['assign']
jobs={j['name']:j for j in json.load(open(D+'/jobs.json'))}
import unicodedata
def slug(c):
    s=unicodedata.normalize('NFKD',c).encode('ascii','ignore').decode().lower(); return re.sub(r'[^a-z0-9]+','-',s).strip('-')
TOK=re.compile(r'\[[^\]]+\]|</?i>')
def want(i): return len(re.split(r'<br>',by[i]['Korean'],flags=re.I))
def npar(t): return len(re.split(r'\s+/(?=\s|$)',t))
def check(i,t):
    if npar(t)!=want(i): return 'para %d!=%d'%(npar(t),want(i))
    if re.search('[⟨⟩〈〉《》]',t) or '...' in t: return 'sym'
    if sorted(TOK.findall(t)) not in (sorted(TOK.findall(done[i])),sorted(TOK.findall(by[i]['EN']))): return 'tok %s vs %s'%(TOK.findall(t),TOK.findall(done[i]))
    return None
job=sys.argv[1]; J=jobs[job]; mine=set()
inf=open(D+'/in-%s.txt'%job,encoding='utf-8').read()
mine={l.split('|')[1] for l in inf.splitlines() if re.match(r'^\d+\|',l)}
fix={}; q={}; bad=[]
for ln in open(D+'/out-%s.tsv'%job,encoding='utf-8-sig'):
    c=ln.rstrip('\r\n').split('\t')
    if len(c)<2 or not c[1].strip(): continue
    i=c[0].strip()
    if i not in mine: bad.append((i,'not in job')); continue
    if c[1].strip()=='?': q[i]=c[2:]; continue
    t=c[1].strip()
    if t==done[i]: continue
    e=check(i,t)
    if e: bad.append((i,e)); continue
    fix[i]=t
pk=D+'/pick-%s.tsv'%job
if len(sys.argv)<3:
    print('fix',len(fix),'?',len(q),'bad',len(bad))
    from difflib import SequenceMatcher as SM
    for i,t in fix.items():
        r=SM(None,t,done[i]).ratio(); L=len(t)/max(1,len(done[i]))
        if r<0.25 or L<0.45 or L>2.2: print('CHECK',i,sp.get(i),'|',chunk.plain(by[i]['EN'])[:150],'\n   전:',done[i][:150],'\n   후:',t[:150])
    for b in bad[:15]: print('BAD',b)
    for i,c in q.items():
        print('\n?',i,sp.get(i),'| EN:',chunk.plain(by[i]['EN'])); print('  now:',done[i]); print('  why:',c[0] if c else '')
        for k,x in enumerate(c[1:],1): print('  %d:'%k,x)
    sys.exit()
picks={}
if os.path.exists(pk):
    for ln in open(pk,encoding='utf-8'):
        c=ln.rstrip('\n').split('\t',1)
        if len(c)<2: continue
        i,v=c; v=v.strip()
        if v=='0': fix.pop(i,None); continue
        t=q[i][int(v)] if v.isdigit() else v
        e=check(i,t)
        if e: print('PICKBAD',i,e); continue
        fix[i]=t
narr=J['chars'][0].startswith('#')
out=collections.defaultdict(dict)
for i,t in fix.items():
    name=('narration-'+job.split('narration-')[1]) if narr else slug(assign[i])
    out[name][i]=t
pos={i:k for k,i in enumerate(order)}
for name,d in out.items():
    p=R+'/translation/retrans/review-%s.tsv'%name
    old={}
    if os.path.exists(p):
        for ln in open(p,encoding='utf-8'):
            m=chunk.IDRE.match(ln.rstrip('\n'))
            if m: old[m.group(1)]=m.group(2)
    old.update(d)
    open(p,'w',encoding='utf-8',newline='\n').write(''.join('%s\t%s\n'%(i,old[i]) for i in sorted(old,key=lambda i:pos[i])))
    print('WROTE',os.path.basename(p),len(old))
json.dump(fix,open(D+'/fixed-%s.json'%job,'w'),ensure_ascii=False)
print('TOTAL',len(fix))
random.seed(job); E=lambda x:re.sub(r'\.{2,}|…','…',x)
s=[i for i in fix if len(by[i]['EN'])<160 and E(fix[i])!=E(done[i])]; random.shuffle(s)
for i in s[:5]: print('\n-',sp.get(i),'|',chunk.plain(by[i]['EN'])); print('  전:',done[i]); print('  후:',fix[i])
