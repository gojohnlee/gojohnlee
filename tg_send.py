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


# 11-25 사용자: 텔레그램은 표가 안 그려져 읽기 어려움 → 마크다운 표의 행을 매물별 묶음으로 바꾼다(10개 항목은 그대로, 줄 배치만 바꿈)
def cards(t):
    out, head = [], None
    for ln in t.splitlines():
        s = ln.strip()
        if s.startswith("|") and s.endswith("|"):
            cells = [c.strip() for c in s.strip("|").split("|")]
            if all(set(c) <= set("-: ") for c in cells):
                continue
            if head is None:
                head = cells
                continue
            if len(cells) == 10:
                rk, nm, py, dep, tot, dist, tr, mv, warn, link = cells
                url = re.search(r"\((https?://[^)]+)\)", link)
                src = re.sub(r"\[|\].*", "", link)
                out += [f"▶ {rk}  {nm}",
                        f"   {py} · 보증금 {dep} · 월 {tot}",
                        f"   회사까지 {dist} · 대중교통 {tr}",
                        f"   입주 {mv}",
                        f"   주의: {warn}",
                        f"   {src}: {url.group(1) if url else link}", ""]
            else:
                out += [" / ".join(f"{h}: {c}" for h, c in zip(head, cells)), ""]
            continue
        head = None
        out.append(re.sub(r"^#+\s*", "■ ", ln))
    return "\n".join(out)
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
