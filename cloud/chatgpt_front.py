"""ChatGPT Sol retranslation of dialogue-order indices 0 through 4645 inclusive.

Uses separate work/progress files so the main translation loop can keep running.
Each applied ID replaces every active occurrence to avoid filename precedence
silently restoring an older translation. Archived retrans-old files stay intact.
"""
import collections
import json
from pathlib import Path
import re
import sys

import chunk

ROOT = Path(chunk.R)
WORK = ROOT / 'cloud/work/chatgpt-front'
PROGRESS = ROOT / 'cloud/chatgpt-front.json'
STATUS = ROOT / 'cloud/chatgpt-front.md'
START, END, SIZE = 0, 4645, 120


def progress():
    if PROGRESS.exists():
        state = json.loads(PROGRESS.read_text(encoding='utf-8'))
        state['resume_main_translation_after_completion'] = True
        return state
    return {'translator': 'ChatGPT Sol', 'model': 'gpt-6.1-sol',
            'start': START, 'end_inclusive': END, 'completed_ids': [],
            'resume_main_translation_after_completion': True}


def write_progress(state):
    PROGRESS.write_text(json.dumps(state, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    STATUS.write_text(
        '# ChatGPT Sol 앞부분 재번역\n\n'
        '- 담당: **ChatGPT Sol (`gpt-6.1-sol`)**. Claude 작업이 아님.\n'
        '- 범위: `cloud/chunk.py` 대사 순서의 **0~4645번째 줄**, 양 끝 포함 4,646줄.\n'
        f"- 완료: **{len(state['completed_ids'])}/4646줄**.\n"
        '- 영어 원문에서 새로 번역하며, 최신 `translate-guide.md`를 따름.\n'
        '- 전체 재번역 루프와 별도 작업 폴더·진행 기록을 사용함.\n'
        '- 이 범위는 ChatGPT가 담당하므로 다른 세션에서 중복 번역하지 않음.\n' +
        ('- 후속 작업 완료: 전체 26,752줄 번역 완료, 미번역 0줄.\n'
         if state.get('main_translation_completed') else
         '- 사용자 후속 지시: 이 범위를 마친 뒤 전체 목록의 미번역 부분부터 계속 번역함. 다른 번역 세션은 종료됨.\n') +
        '- 기존 `translation/retrans-old/`는 보존하고, 활성 번역의 같은 ID를 모두 갱신함.\n'
        '- 새 커밋 작성자: `ChatGPT <noreply@openai.com>`. 과거 커밋 작성자는 유지함.\n',
        encoding='utf-8')


def next_block():
    by, order, speaker, _, _ = chunk.load()
    state = progress()
    done = set(state['completed_ids'])
    first = next((n for n in range(START, END + 1) if order[n] not in done), None)
    write_progress(state)
    if first is None:
        print('ALLDONE ChatGPT Sol 4646/4646')
        return
    last = min(first + SIZE - 1, END)
    want, english, lines = {}, {}, ['# ID|화자|단락수|영어']
    for n in range(first, last + 1):
        ident = order[n]
        if ident in done:
            continue
        row = by[ident]
        want[ident] = len(re.split(r'<br>', row['Korean'], flags=re.I))
        english[ident] = chunk.plain(row['EN'])
        lines.append(f"{ident}|{speaker.get(ident, '')}|{want[ident]}|{english[ident]}")
    WORK.mkdir(parents=True, exist_ok=True)
    header = chunk.context(first, last, {speaker.get(i, '') for i in want})
    (WORK / 'chunk.txt').write_text(header + '\n'.join(lines) + '\n', encoding='utf-8')
    (WORK / 'want.json').write_text(json.dumps({'first': first, 'last': last,
                                              'want': want, 'english': english}), encoding='utf-8')
    (WORK / 'out.tsv').unlink(missing_ok=True)
    print(f'CHUNK ChatGPT Sol {first}~{last} {len(want)}')


def apply_block():
    job = json.loads((WORK / 'want.json').read_text(encoding='utf-8'))
    translated, errors = {}, []
    tokens = lambda s: collections.Counter(re.findall(r'\[[^\]]+\]', s))
    for line in (WORK / 'out.tsv').read_text(encoding='utf-8-sig').splitlines():
        if '\t' not in line:
            errors.append('malformed row')
            continue
        ident, text = line.split('\t', 1)
        if ident in translated or ident not in job['want']:
            errors.append('duplicate/unexpected ' + ident)
            continue
        translated[ident] = text
        source = job['english'][ident]
        if not text.strip() or len(text.split(' / ')) != job['want'][ident]:
            errors.append('empty/paragraphs ' + ident)
        if tokens(source) != tokens(text):
            errors.append('tokens ' + ident)
        if any(source.count(tag) != text.count(tag) for tag in ('<i>', '</i>')):
            errors.append('emphasis ' + ident)
        if '...' in text or re.search('[⟨⟩〈〉《》]', text):
            errors.append('punctuation ' + ident)
    errors += ['missing ' + i for i in job['want'] if i not in translated]
    if errors:
        raise SystemExit('VALIDATION FAILED: ' + ', '.join(errors))
    # Validate the entire block before changing any active translation file.
    found, changes = set(), []
    for path in sorted((ROOT / 'translation/retrans').glob('*.tsv')):
        lines = path.read_text(encoding='utf-8-sig').splitlines()
        touched = False
        for n, line in enumerate(lines):
            ident = line.split('\t', 1)[0]
            if ident in translated:
                found.add(ident)
                replacement = ident + '\t' + translated[ident]
                if replacement != line:
                    lines[n] = replacement
                    touched = True
        if touched:
            changes.append((path, '\n'.join(lines) + '\n'))
    missing = [i for i in job['want'] if i not in found]
    if missing:
        path = ROOT / 'translation/retrans/chatgpt-front.tsv'
        existing = next((text for p, text in changes if p == path),
                        path.read_text(encoding='utf-8-sig') if path.exists() else '')
        changes = [(p, text) for p, text in changes if p != path]
        changes.append((path, existing.rstrip('\n') + ('\n' if existing else '') +
                        ''.join(i + '\t' + translated[i] + '\n' for i in missing)))
    for path, text in changes:
        path.write_text(text, encoding='utf-8')
    state = progress()
    state['completed_ids'] = list(dict.fromkeys(state['completed_ids'] + list(job['want'])))
    write_progress(state)
    print(f"APPLIED ChatGPT Sol {job['first']}~{job['last']}: {len(translated)}줄, "
          f"누락 0 | {len(state['completed_ids'])}/4646")


if __name__ == '__main__':
    {'next': next_block, 'apply': apply_block}[sys.argv[1]]()
