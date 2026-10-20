#!/usr/bin/env python3
"""바닐라 RimWorld XML Def 조회 (Core + 모든 DLC, 1.6 데이터 = 사용자 확인상 1.7과 수치 동일).

데이터 출처: https://rimworld.lattemacchiato.dev (raw-xml.json.zstd). 저작권 때문에 저장소에는 넣지 않고
캐시 폴더(기본 ~/.cache/rimworld-defs)에 내려받는다. 필요: pip install zstandard

사용법:
  python3 -I scripts/vanilla-defs.py '<defName>Mech_Scyther</defName>'   # 정규식에 맞는 Def 전체 출력
  python3 -I scripts/vanilla-defs.py --names 'Mech_'                       # 이름만 나열
"""
import json
import os
import re
import sys
import urllib.request

CACHE = os.environ.get("RW_DEFS_CACHE", os.path.expanduser("~/.cache/rimworld-defs"))
URL = "https://rimworld.lattemacchiato.dev/raw-xml.json.zstd"


def load():
    path = os.path.join(CACHE, "raw-xml.json")
    if not os.path.exists(path):
        import zstandard
        os.makedirs(CACHE, exist_ok=True)
        req = urllib.request.Request(URL, headers={"User-Agent": "Mozilla/5.0 (rimworld-defs lookup)"})
        data = urllib.request.urlopen(req, timeout=120).read()
        with open(path, "wb") as f:
            f.write(zstandard.ZstdDecompressor().stream_reader(data).read())
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def main(argv):
    if not argv:
        print(__doc__)
        return 1
    defs = load()
    if argv[0] == "--names":
        pat = re.compile(argv[1])
        for key, text in defs.items():
            m = re.search(r"<defName>([^<]+)</defName>", text) or re.search(r"\sName=\"([^\"]+)\"", text)
            name = m and m.group(1)
            if name and pat.search(name):
                print(f"{name}\t{key}")
        return 0
    pat = re.compile(argv[0])
    for key, text in defs.items():
        if pat.search(text):
            print("=====", key)
            print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
