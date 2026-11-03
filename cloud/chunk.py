#!/usr/bin/env python3
"""Cloud re-translation helper (port of scripts/retrans-loop.ps1 in gojohnlee/gojohnlee necromancer branch).
  python3 cloud/chunk.py next   -> writes cloud/work/chunk.txt + want.json, prints 'CHUNK <first> <n>' or 'ALLDONE'
  python3 cloud/chunk.py apply  -> checks cloud/work/out.tsv, appends good lines to translation/retrans/auto-<first>.tsv,
                                   updates cloud/status.md, prints a one-line summary
Order = articy dump order (dialogue flow), then CSV order. Done = any ID in translation/retrans/*.tsv. Skip = translation/skip.txt."""
import csv, glob, io, json, os, re, sys, time
os.environ['TZ'] = 'Asia/Seoul'; time.tzset()
R = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
T = os.path.join(R, 'translation'); W = os.path.join(R, 'cloud', 'work'); CHUNK = 120
IDRE = re.compile(r'^([^\t\r\n]+)\t(.*)$')

def rows():
    raw = open(os.path.join(T, 'Korean-fixed.csv'), encoding='utf-8-sig', newline='').read()
    i = 0
    for _ in range(3): i = raw.index('\n', i) + 1
    return list(csv.DictReader(io.StringIO(raw[i:], newline='')))

def plain(s): return re.sub(r'<br>|\r?\n', ' / ', re.sub(r'</?align[^>]*>', '', s, flags=re.I), flags=re.I).strip()

def load():
    by = {}; csv_order = []
    for r in rows():
        if r.get('EN') and r.get('Korean'): by[r['ID']] = r; csv_order.append(r['ID'])
    speaker = {}; order = []; seen = set()
    for ln in open(os.path.join(T, 'dump', 'articy-dump.tsv'), encoding='utf-8-sig'):
        c = ln.rstrip('\r\n').split('\t')
        if c[0] in by and c[0] not in seen: seen.add(c[0]); speaker[c[0]] = c[2] if len(c) > 2 else ''; order.append(c[0])
    for i in csv_order:
        if i not in seen: seen.add(i); order.append(i)
    done = {}
    for f in sorted(glob.glob(os.path.join(T, 'retrans', '*.tsv'))):
        for ln in open(f, encoding='utf-8-sig'):
            m = IDRE.match(ln.rstrip('\r\n'))
            if m: done[m.group(1)] = m.group(2)
    sp = os.path.join(T, 'skip.txt')
    skip = {l.strip() for l in open(sp, encoding='utf-8-sig')} if os.path.exists(sp) else set()
    return by, order, speaker, done, skip

def status(order, done, skip, note):
    d = sum(1 for i in order if i in done); a = len(order)
    with open(os.path.join(R, 'cloud', 'status.md'), 'w', encoding='utf-8') as f:
        f.write(f"# 재번역 진행(클라우드)\n\n- {time.strftime('%Y-%m-%d %H:%M', time.localtime())} 기준 {d} / {a} 줄 ({round(100*d/a)}%), 건너뜀 {len(skip)}줄\n- 마지막: {note}\n")
        translator = os.environ.get('NECROMANCER_TRANSLATOR')
        if translator:
            f.write(f'- 이번 작업 수행: {translator}\n')
    return d, a

C = os.path.join(R, 'cloud', 'context')
def context(a, b, speakers):
    """Story summary of the 1000-line windows this chunk touches + profiles (cloud/context/characters.md, '### 한글(Speaker)') of its speakers."""
    out = []
    for w in sorted({a // 1000, b // 1000}):
        p = os.path.join(C, 'w%02d.md' % w)
        if os.path.exists(p): out.append(open(p, encoding='utf-8').read().strip())
    p = os.path.join(C, 'characters.md')
    if os.path.exists(p):
        secs = re.split(r'(?m)^(?=### )', open(p, encoding='utf-8').read())
        pick = [x.strip() for x in secs if (m := re.match(r'### .*\((.+?)\)', x)) and any(n.strip() in speakers for n in m.group(1).split(','))]
        if pick: out.append('## 이 덩어리에 나오는 인물\n' + '\n\n'.join(pick))
    if not out: return ''
    return '# 배경 자료(번역하지 말 것): 이 덩어리 줄번호 %d~%d\n\n' % (a, b) + '\n\n'.join(out) + '\n\n# 번역할 줄\n' % ()

def cmd_next():
    by, order, speaker, done, skip = load()
    first = next((k for k, i in enumerate(order) if i not in done and i not in skip), -1)
    if first < 0: status(order, done, skip, '다 끝남'); print('ALLDONE'); return
    os.makedirs(W, exist_ok=True); want = {}; lines = ['# ID|화자|단락수|영어']
    for i in order[first:first + CHUNK]:
        r = by[i]; sp = speaker.get(i, '')
        if i in done or i in skip: lines.append(f"[완료] {i}|{sp}|{done.get(i) or plain(r['Korean'])}"); continue
        n = len(re.split(r'<br>', r['Korean'], flags=re.I)); want[i] = n
        lines.append(f"{i}|{sp}|{n}|{plain(r['EN'])}")   # 옛 기계 번역은 주지 않음(베껴 쓰는 것을 막음, 11-02)
    head = context(first, min(first + CHUNK, len(order)) - 1, {speaker.get(i, '') for i in order[first:first + CHUNK]})
    open(os.path.join(W, 'chunk.txt'), 'w', encoding='utf-8').write(head + '\n'.join(lines) + '\n')
    json.dump({'first': first, 'fid': order[first], 'want': want}, open(os.path.join(W, 'want.json'), 'w'))
    if os.path.exists(os.path.join(W, 'out.tsv')): os.remove(os.path.join(W, 'out.tsv'))
    print(f'CHUNK {first} {len(want)}')

def cmd_apply():
    j = json.load(open(os.path.join(W, 'want.json'))); want = j['want']; ok = []; bad = 0
    p = os.path.join(W, 'out.tsv')
    if os.path.exists(p):
        for ln in open(p, encoding='utf-8-sig'):
            m = IDRE.match(ln.rstrip('\r\n'))
            if not m or not m.group(2).strip(): continue
            i, t = m.group(1), m.group(2)
            if i not in want or re.search('[⟨⟩〈〉《》]', t): bad += 1; continue
            if len(re.split(r'\s+/(?=\s|$)', t)) != want[i]: bad += 1; continue
            ok.append(f'{i}\t{t}'); del want[i]
    tf = os.path.join(R, 'cloud', 'tries.json'); tries = json.load(open(tf)) if os.path.exists(tf) else {}
    if ok:
        with open(os.path.join(T, 'retrans', 'auto-%05d.tsv' % j['first']), 'a', encoding='utf-8', newline='\n') as f: f.write('\n'.join(ok) + '\n')
        tries.pop(j['fid'], None)
    else:
        tries[j['fid']] = tries.get(j['fid'], 0) + 1
        if tries[j['fid']] >= 3:   # same first line failed 3 times -> skip it (like the PC loop)
            with open(os.path.join(T, 'skip.txt'), 'a', encoding='utf-8') as f: f.write(j['fid'] + '\n')
            del tries[j['fid']]
    json.dump(tries, open(tf, 'w'))
    by, order, speaker, done, skip = load()
    note = f"{j['first']}번째 줄부터: 받음 {len(ok)}, 버림 {bad}, 빠짐 {len(want)}"
    d, a = status(order, done, skip, note)
    print(f'APPLIED {note} | {d}/{a}')

if __name__ == '__main__':
    {'next': cmd_next, 'apply': cmd_apply}[sys.argv[1]]()
