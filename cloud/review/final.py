# 마무리: 말줄임표·표기 통일 → review-zzz-final.tsv (가장 나중에 적용)
import sys,re,os;R='/home/user/valheim-refs';sys.path.insert(0,R+'/cloud');import chunk
by,order,sp,done,skip=chunk.load()
pos={i:k for k,i in enumerate(order)}
def jong(w): c=ord(w[-1])-0xAC00; return 0<=c<11172 and c%28!=0
P={'이':'가','가':'이','은':'는','는':'은','을':'를','를':'을','과':'와','와':'과','이랑':'랑','랑':'이랑','이라는':'라는','라는':'이라는','이나':'나','나':'이나','으로':'로','로':'으로'}
def sub(t,a,b):
    def f(m):
        p=m.group(1) or ''
        if p and jong(a)!=jong(b) and p in P: p=P[p]
        return b+p
    return re.sub(re.escape(a)+r'(이라는|라는|이랑|으로|이나|은|는|이|가|을|를|과|와|랑|로|나)?(?=[^가-힣]|$|원|의|에|도|만|까지|조차|처럼|보다|하고|들)' ,f,t)
REP=[('수의단','수의회'),('슈라우드','수의회'),('황금단','황금회'),('베르니체','베르니스'),('스가랴서','즈가리야서'),('까마중','벨라도나'),('가지풀','벨라도나'),('세네갈 아카시아','세네갈리아 세네갈'),('세네갈아카시아','세네갈리아 세네갈')]
VZ={'0x0100000000025A59','0x0100000000026F91'}
VEIL={'0x01000000000412DD','0x0100000000020D5D'}
out={};log=[]
for i,t in done.items():
    n=t
    for a,b in REP:
        if a in n:
            n=sub(n,a,b).replace(a,b)
            if jong(a) and not jong(b):
                for x,y in [('이라','라'),('이야','야'),('이었','였'),('이지','지'),('이다','다'),('이고','고'),('이니','니'),('이에요','예요'),('이나','나')]: n=n.replace(b+x,b+y)
    if '대신' in n and ('Vizier' in chunk.plain(by[i]['EN']) or i in VZ): n=n.replace('대신','재상')
    if ('숙부' in n or '백부' in n):
        for a in ('숙부님','숙부','백부님','백부'):
            if a in n:
                n=sub(n,a,'삼촌').replace(a,'삼촌')
                if not jong(a):
                    for x,y in [('였','이었'),('예요','이에요'),('라고','이라고'),('라서','이라서'),('라도','이라도'),('야','이야')]: n=n.replace('삼촌'+x,'삼촌'+y)
    if '수의회' in n:
        for x,y in [('수의회가','검은 베일이'),('수의회는','검은 베일은'),('수의회를','검은 베일을'),('수의회와','검은 베일과'),('수의회랑','검은 베일이랑'),('수의회라','검은 베일이라'),('수의회이든','검은 베일이든'),('수의회였','검은 베일이었'),('수의회야','검은 베일이야'),('수의회예요','검은 베일이에요'),('수의회원','검은 베일 단원'),('수의회','검은 베일')]: n=n.replace(x,y)
    if i in VEIL: n=n.replace('베일','장막')
    n=re.sub(r'\.{2,}','…',n); n=n.replace('……','…')
    if n!=t:
        out[i]=n
        if '...' not in t or any(a in t for a,_ in REP): log.append((t,n))
p=R+'/translation/retrans/review-zzz-final.tsv'
open(p,'w',encoding='utf-8',newline='\n').write(''.join('%s\t%s\n'%(i,out[i]) for i in sorted(out,key=lambda i:pos.get(i,10**9))))
print('lines',len(out))
import difflib
for a,b in log:
    if a.replace('...','…').replace('……','…')==b: continue
    d=[f'[{a[x1:x2]}→{b[y1:y2]}]' for op,x1,x2,y1,y2 in difflib.SequenceMatcher(None,a,b).get_opcodes() if op!='equal']
    print(' '.join(d)[:160])
