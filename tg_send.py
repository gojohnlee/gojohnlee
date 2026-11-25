"""보고 파일을 텔레그램으로 보낸다. 사용: python3 -I tg_send.py <보고.txt>
토큰: 환경변수 TELEGRAM_BOT_TOKEN. 받는 사람: TELEGRAM_CHAT_ID, 없으면 봇에 최근 말 건 개인 대화(getUpdates).
4000자씩 나눠 보낸다(텔레그램 한 메시지 한도 4096자). 실패하면 종료 코드 1."""
import re, json, os, sys, urllib.request, urllib.parse

tok = os.environ.get("TELEGRAM_BOT_TOKEN", "").strip()
if not tok:
    sys.exit("TELEGRAM_BOT_TOKEN 없음")
api = f"https://api.telegram.org/bot{tok}/"


def call(method, **p):
    data = urllib.parse.urlencode(p).encode()
    with urllib.request.urlopen(api + method, data=data, timeout=30) as r:
        d = json.load(r)
    if not d.get("ok"):
        raise RuntimeError(d)
    return d["result"]


chat = os.environ.get("TELEGRAM_CHAT_ID", "").strip()
if not chat:
    for u in reversed(call("getUpdates")):
        c = (u.get("message") or u.get("edited_message") or {}).get("chat", {})
        if c.get("type") == "private":
            chat = str(c["id"])
            break
if not chat:
    sys.exit("받는 사람 없음: 텔레그램에서 봇에게 아무 말이나 먼저 보내야 함")

text = open(sys.argv[1], encoding="utf-8").read().strip() or "(빈 보고)"


# 11-25 사용자: 텔레그램은 표가 안 그려지고 기호(O·△·X·?)·긴 주소가 읽기 어려움 → 매물마다 쉬운 말 6줄로 바꾼다
def won(v):
    n = int(re.sub(r"\D", "", v) or 0)
    return f"{n // 1000}천" if n >= 1000 and n % 1000 == 0 else f"{n:,}만"
def card(cells):
    rk, nm, py, dep, tot, dist, tr, mv, warn, link = cells
    m = re.match(r"(\S)\s+(\d+)\s*(.*)", rk); sym, no, mv_rank = (m.groups() if m else ("", rk, ""))
    ad = re.search(r"(\S+동)\s+([\d-]+)\s", nm + " ")
    fl = re.search(r"(\d+)층 건물의 (\d+)층", nm); yr = re.search(r"\((\d{4})년\)", nm)
    where = f"{ad.group(1)} {ad.group(2)}" if ad else (re.search(r"(\S+동)", nm).group(1) + " (지번 비공개)" if re.search(r"\S+동", nm) else nm)
    flo = (f"꼭대기 {fl.group(2)}층" if fl.group(1) == fl.group(2) else f"{fl.group(2)}층 (전체 {fl.group(1)}층)") if fl else ""
    chg = " · 새 매물" if "🆕" in mv_rank else (f" · {mv_rank}" if re.search(r"[▲▼]", mv_rank) else "")
    l1 = f"{no}위  {where} · {flo} · {py}" + (f" ({yr.group(1)}년)" if yr else "") + chg
    mvs = "즉시 입주" if mv.startswith("즉시") else f"입주 {mv}"
    l2 = f"월 {tot.replace('약 ', '')} · 보증금 {won(dep)} · {mvs}"
    t = re.match(r"(\d+(?:시간\s*)?\d*분), 환승 (\d+)회 \((\S+)\s*([^)]*)\)", tr)
    bus = (f"{t.group(3)} {t.group(4).strip()} {t.group(1)}" + (f" (환승 {t.group(2)}회)" if t.group(2) != "0" else "")) if t else "버스 정보 없음"
    l3 = f"회사 {dist.split()[0]} · {bus}"
    parts = [x.strip() for x in re.split(r",\s*(?![^()]*\))", warn) if x.strip() and x.strip() != "-"]
    cnt = next((x for x in parts if re.match(r"후기 \d+개", x)), None)
    probs = [x.replace("명", "명") for x in parts if re.search(r"\d+명$", x)]
    good = [warn.split("좋음:", 1)[1].strip()] if "좋음:" in warn else []
    if good: parts = [x for x in parts if x not in good[0]]
    keep = next((x for x in parts if x.startswith("사용자가 살림:")), None)
    if keep: rv = keep.replace("사용자가 살림: ", "") + " — 사용자가 살린 집"; parts = [x for x in parts if x != keep]; probs = []; cnt = None
    elif sym == "?": rv = "후기: 지번 비공개라 못 찾음"
    elif sym == "X": rv = "후기: 없음(당근·집품 모두)"
    else:
        rv = (f"{cnt} 중 " if cnt else "후기: ") + (", ".join(p.replace("벌레", "벌레 봤다는 사람") for p in probs) if probs else "소음·벌레 불만 없음")
        if good: rv += " / 좋은 점: " + good[0]
    warns = []
    for x in parts:
        if x in probs or x == cnt or "좋음:" in x: continue
        if x.startswith("꼭대기층:"): warns.append("꼭대기층이라 누수·더위 확인"); continue
        if x.startswith("조건 초과:"): warns.append(x.replace("조건 초과:", "넘는 조건:")); continue
        if "후기 조회 불가" in x or "위층 소음 없음" in x or x == "누수·더위 확인": continue
        warns.append(x)
    url = re.search(r"\((https?://[^)]+)\)", link)
    out = [l1, l2, l3, rv] + (["⚠ " + ", ".join(dict.fromkeys(warns))] if warns else []) + [url.group(1) if url else link, ""]
    return out
def cards(t):
    out = []
    for ln in t.splitlines():
        s = ln.strip()
        if s.startswith("후기: O="): continue
        if s.startswith("|") and s.endswith("|"):
            cells = [c.strip() for c in s.strip("|").split("|")]
            if all(set(c) <= set("-: ") for c in cells) or cells[0].startswith("순위"): continue
            out += card(cells) if len(cells) == 10 else [" / ".join(cells), ""]
            continue
        s = re.sub(r"^## 오늘 순위.*", "【오늘 추천】 월 70만 이하 · 보증금 2천 이하 · 회사 3km 안", s)
        s = re.sub(r"^## 좋은 후기 건물인데.*", "【후기 좋은데 조건을 조금 넘는 곳】 월 85만 이하", s)
        s = re.sub(r"^## 벌레 후기 원문.*", "【벌레 후기 원문】", s)
        s = re.sub(r"^###\s*(.*)", r"〔\1〕", s)
        s = re.sub(r"^##\s*(.*)", r"【\1】", s)
        s = re.sub(r"^- 새 매물\(통과\): (\d+)건 / 어제 후보 중 사라짐: (\d+)건", r"새 매물 \1건 · 사라진 매물 \2건", s)
        out.append(s)
    return re.sub(r"\n{3,}", "\n\n", "\n".join(out)).strip()
text = cards(text)
parts, buf = [], ""
for line in text.splitlines(keepends=True):
    while len(line) > 4000:
        parts.append(line[:4000]); line = line[4000:]
    if len(buf) + len(line) > 4000:
        parts.append(buf); buf = ""
    buf += line
if buf.strip():
    parts.append(buf)
for p in parts:
    call("sendMessage", chat_id=chat, text=p, disable_web_page_preview="true")
print(f"텔레그램 전송 {len(parts)}개, chat {chat[:3]}***")
