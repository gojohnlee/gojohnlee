"""보고 파일을 텔레그램으로 보낸다. 사용: python3 -I tg_send.py <보고.txt>
토큰: 환경변수 TELEGRAM_BOT_TOKEN. 받는 사람: TELEGRAM_CHAT_ID, 없으면 봇에 최근 말 건 개인 대화(getUpdates).
4000자씩 나눠 보낸다(텔레그램 한 메시지 한도 4096자). 실패하면 종료 코드 1."""
import json, os, sys, urllib.request, urllib.parse

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
