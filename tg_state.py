"""날마다 이어지는 기록(직전 후보·상세 캐시·사람 판정·블랙리스트)을 GitHub 대신 텔레그램 대화방에 보관한다.
python3 -I tg_state.py load  → 고정된 기록 파일을 받아 풀어 덮어씀(없으면 저장소 것 그대로)
python3 -I tg_state.py save  → 기록을 zip으로 올리고 대화방 맨 위에 조용히 고정(알림 없음)
환경변수 TELEGRAM_BOT_TOKEN, TELEGRAM_CHAT_ID."""
import io, json, os, sys, time, uuid, zipfile, urllib.request, urllib.parse

here = os.path.dirname(os.path.abspath(__file__))
FILES = ["data/latest.json", "data/cache.json", "reviews.json", "blacklist.json"]
tok = os.environ["TELEGRAM_BOT_TOKEN"].strip()
chat = os.environ["TELEGRAM_CHAT_ID"].strip()
api = f"https://api.telegram.org/bot{tok}/"


def call(method, data=None, headers=None):
    if isinstance(data, dict):
        data = urllib.parse.urlencode(data).encode()
    req = urllib.request.Request(api + method, data=data, headers=headers or {})
    with urllib.request.urlopen(req, timeout=60) as r:
        d = json.load(r)
    if not d.get("ok"):
        raise RuntimeError(d)
    return d["result"]


def load():
    pin = call("getChat", {"chat_id": chat}).get("pinned_message") or {}
    doc = pin.get("document") or {}
    if not doc.get("file_name", "").startswith("house-state"):
        print("고정된 기록 없음 → 저장소 것 사용"); return
    path = call("getFile", {"file_id": doc["file_id"]})["file_path"]
    with urllib.request.urlopen(f"https://api.telegram.org/file/bot{tok}/{path}", timeout=60) as r:
        z = zipfile.ZipFile(io.BytesIO(r.read()))
    for n in z.namelist():
        if n in FILES:
            os.makedirs(os.path.dirname(os.path.join(here, n)) or here, exist_ok=True)
            open(os.path.join(here, n), "wb").write(z.read(n))
    print(f"기록 불러옴: {doc['file_name']} ({', '.join(z.namelist())})")


def save():
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        for n in FILES:
            p = os.path.join(here, n)
            if os.path.exists(p):
                z.write(p, n)
    name = time.strftime("house-state-%Y%m%d.zip")
    b = "----" + uuid.uuid4().hex
    body = b"".join([
        f'--{b}\r\nContent-Disposition: form-data; name="chat_id"\r\n\r\n{chat}\r\n'.encode(),
        f'--{b}\r\nContent-Disposition: form-data; name="disable_notification"\r\n\r\ntrue\r\n'.encode(),
        f'--{b}\r\nContent-Disposition: form-data; name="caption"\r\n\r\n원룸 확인 기록(자동, 지우지 마세요)\r\n'.encode(),
        f'--{b}\r\nContent-Disposition: form-data; name="document"; filename="{name}"\r\nContent-Type: application/zip\r\n\r\n'.encode(),
        buf.getvalue(), f"\r\n--{b}--\r\n".encode()])
    m = call("sendDocument", body, {"Content-Type": f"multipart/form-data; boundary={b}"})
    call("unpinAllChatMessages", {"chat_id": chat})
    call("pinChatMessage", {"chat_id": chat, "message_id": m["message_id"], "disable_notification": "true"})
    print(f"기록 저장·고정: {name}")


{"load": load, "save": save}[sys.argv[1]]()
