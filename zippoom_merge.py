"""사용: python3 -I zippoom_merge.py <Gemini 결과.txt>   (사용자 11-26)
사용자가 Gemini(크롬, 집품 로그인)로 받아 준 후기 전문 파일을 data/zippoom_buildings.json에 합친다.
꼴: ### 지번주소 / 건물이름 | 평점 | 후기수 | 쪽주소 / - 날짜 | 층 | 장점: .. | 단점: .. | 단점 키워드: ..
기존보다 후기가 많거나 처음 보는 건물만 바꾼다. 끝나면 zippoom_have_full.txt에 넣고 zippoom_todo.txt·gemini/addresses.txt에서 지운다."""
import json, re, sys, os
here = os.path.dirname(os.path.abspath(__file__)); D = f'{here}/data'
t = open(sys.argv[1], encoding='utf-8').read()
z = json.load(open(f'{D}/zippoom_buildings.json')); f = json.load(open(f'{D}/zippoom_found.json'))
done = set(); got = up = same = 0
for b in re.split(r'^### ', t, flags=re.M)[1:]:
    L = b.strip('\n').split('\n'); k = re.sub(r'^경기(도)?\s*', '', L[0]).strip(); got += 1
    name = L[1].split('|')[0].strip() if len(L) > 1 else ''
    R = []
    for l in L:
        if not l.startswith('- '): continue
        p = [x.strip() for x in l[2:].split('|')]
        dan = re.search(r'단점:\s*(.*?)(\s*\|\s*단점 키워드:|$)', l)
        bad = bool(dan and dan.group(1).strip() not in ('', '-', '없음'))
        fl = p[1] if len(p) > 1 else '?'
        R.append(dict(bad=bad, kinds=[], txt='집품 ' + fl + ' | ' + ' | '.join(p[2:]), floor=fl, date=p[0], name=name + '(사용자 Gemini)'))
    old = z.get(k, {}).get('n') or 0
    if R and (len(R) > old or k not in f):
        z[k] = {'v': 'bad' if any(r['bad'] for r in R) else 'good', 'n': len(R), 'bad': sum(r['bad'] for r in R), 'reviews': R}; up += 1
        f[k] = {'url': '', 'reviews': len(R), 'full': len(R), 'date': __import__('datetime').date.today().isoformat(), 'how': '사용자 Gemini(크롬)'}
    else: same += 1
    if R: done.add(k)
json.dump(z, open(f'{D}/zippoom_buildings.json', 'w'), ensure_ascii=False, indent=0)
json.dump(f, open(f'{D}/zippoom_found.json', 'w'), ensure_ascii=False, indent=0)
hp = f'{D}/zippoom_have_full.txt'; have = set(open(hp).read().split('\n')) | done; have.discard('')
open(hp, 'w').write('\n'.join(sorted(have)) + '\n')
tp = f'{D}/zippoom_todo.txt'; todo = [l for l in open(tp).read().split('\n') if l and l.split('|')[0].strip() not in have]
open(tp, 'w').write('\n'.join(todo) + '\n')
open(f'{here}/gemini/addresses.txt', 'w').write('\n'.join(sorted({l.split('|')[0].strip() for l in todo})) + '\n')
print(f'건물 {got}곳 받음: 반영 {up}, 그대로 {same} / 끝난 곳 {len(have)} / 남은 곳 {len(todo)}')
