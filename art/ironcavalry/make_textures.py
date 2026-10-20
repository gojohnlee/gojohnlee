"""철기병 텍스처 생성기.

east  : 사용자 제공 원본(docs/ironcavalry/reference.png)을 몸통 중심 기준으로 재배치
south : 정면, north: 후면 — 원본과 같은 팔레트·외곽선 스타일로 직접 그림(SVG)
west  : 림월드가 east를 좌우반전해서 쓰므로 만들지 않음

사용법: python3 -I art/ironcavalry/make_textures.py
"""
import io
import os
import re
import sys

import cairosvg
from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(ROOT, "..", ".."))
OUT = os.path.join(ROOT, "Textures", "Things", "Pawn", "Mechanoid")
SIZE = 512

# 원본 팔레트
L1 = "#d5d8cc"  # 밝은 장갑
L2 = "#b1b6a7"  # 중간 밝음
L3 = "#8a9080"  # 중간
L4 = "#6b6e60"  # 어두운 장갑
D1 = "#30302f"  # 관절
HI = "#e6e9df"  # 하이라이트
OUTLINE = 10    # 외곽선 두께(바깥쪽 절반만 보임)

# 원본에서 몸통 중심(px)과 배치 반경 — 창끝까지 들어가도록
REF_CENTER = (285, 390)
REF_HALF = 525


def east():
    ref = Image.open(os.path.join(REPO, "docs", "ironcavalry", "reference.png")).convert("RGBA")
    cx, cy = REF_CENTER
    box = (cx - REF_HALF, cy - REF_HALF, cx + REF_HALF, cy + REF_HALF)
    canvas = Image.new("RGBA", (REF_HALF * 2, REF_HALF * 2), (0, 0, 0, 0))
    canvas.paste(ref, (-box[0], -box[1]), ref)
    return canvas.resize((SIZE, SIZE), Image.LANCZOS)


def mirror(points):
    return [(SIZE - x, y) for x, y in points]


def poly(points, fill, stroke=None, sw=0):
    pts = " ".join(f"{x:.1f},{y:.1f}" for x, y in points)
    s = f' stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round"' if stroke else ""
    return f'<polygon points="{pts}" fill="{fill}"{s}/>'


def ell(cx, cy, rx, ry, fill, rot=0):
    return f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" fill="{fill}" transform="rotate({rot} {cx} {cy})"/>'


def path(d, fill):
    return f'<path d="{d}" fill="{fill}"/>'


def lance(grip, tip, half_w):
    """원뿔형 창: grip 쪽이 넓고 tip으로 가늘어짐. 축 기준 좌우 두 톤."""
    (gx, gy), (tx, ty) = grip, tip
    dx, dy = tx - gx, ty - gy
    n = (dx * dx + dy * dy) ** 0.5
    px, py = -dy / n * half_w, dx / n * half_w
    a = (gx + px, gy + py)
    b = (gx - px, gy - py)
    return [poly([a, (gx, gy), (tx, ty)], L1), poly([b, (gx, gy), (tx, ty)], L3)], [a, b, (tx, ty)]


GRADIENTS = (
    '<defs>'
    '<linearGradient id="gA" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#e2e5da"/><stop offset="1" stop-color="#b6bbac"/></linearGradient>'
    '<linearGradient id="gB" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#c3c8b8"/><stop offset="1" stop-color="#8f9585"/></linearGradient>'
    '<linearGradient id="gC" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#9da392"/><stop offset="1" stop-color="#666a5c"/></linearGradient>'
    '<linearGradient id="gD" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#4a4a47"/><stop offset="1" stop-color="#262625"/></linearGradient>'
    '</defs>'
)
GA, GB, GC, GD = "url(#gA)", "url(#gB)", "url(#gC)", "url(#gD)"


def mirror_d(d):
    """SVG path의 x좌표를 좌우 반전 (M/L/Q/Z 절대좌표만 사용)."""
    def flip(m):
        x, y = float(m.group(1)), float(m.group(2))
        return f"{SIZE - x:g},{y:g}"
    return re.sub(r"(-?[\d.]+),(-?[\d.]+)", flip, d)


def pauldrons(s, sil):
    """견갑: 둥근 공 대신 위가 둥글고 아래가 각진 판."""
    for cx, fill, shade, sgn in ((226, GA, GB, -1), (286, GB, GC, 1)):
        o = sgn * 6  # 바깥쪽으로 살짝 처짐
        pts = [(cx - 20, 216), (cx - 14, 202), (cx, 197), (cx + 14, 202), (cx + 20, 216),
               (cx + 16 + max(o, 0), 230), (cx, 234), (cx - 16 + min(o, 0), 230)]
        s.append(poly(pts, fill)); sil.append(pts)
        s.append(poly([(cx - 20, 216), (cx + 20, 216), (cx + 16 + max(o, 0), 230), (cx, 234),
                       (cx - 16 + min(o, 0), 230)], shade))
        s.append(poly([(cx - 12, 203), (cx - 2, 199), (cx - 8, 210)], HI))


def south_shapes():
    s, blobs, ells, sil = [], [], [], []
    # 몸통 뒤로 이어지는 말 몸(정면에서는 상체 뒤로 보임)
    horse = "M190,300 Q186,266 222,260 L290,260 Q326,266 322,300 Q318,334 294,344 L218,344 Q194,334 190,300 Z"
    s.append(path(horse, GB)); blobs.append(horse)
    s.append(path("M194,318 Q256,344 318,318 Q314,336 294,344 L218,344 Q198,336 194,318 Z", GD))
    # 뒷다리 판갑(바깥쪽)
    rl = "M200,298 Q162,310 156,372 Q178,344 214,328 Z"
    for d, f in ((rl, GC), (mirror_d(rl), GC)):
        s.append(path(d, f)); blobs.append(d)
    # 앞다리 판갑: 바깥으로 휘어 아래로 뾰족
    fl = "M216,314 Q176,344 196,414 Q206,376 244,342 Z"
    fl_dark = "M244,342 Q206,376 196,414 Q214,382 232,362 Z"
    s.append(path(fl, GA)); blobs.append(fl)
    s.append(path(fl_dark, GD))
    s.append(path(mirror_d(fl), GB)); blobs.append(mirror_d(fl))
    s.append(path(mirror_d(fl_dark), GD))
    # 앞다리 사이 아랫배
    s.append(poly([(232, 338), (280, 338), (268, 366), (244, 366)], GD))
    # 말 가슴 판갑
    bp = [(216, 292), (296, 292), (290, 330), (256, 352), (222, 330)]
    s.append(poly(bp, GA)); sil.append(bp)
    s.append(poly([(256, 292), (296, 292), (290, 330), (256, 352)], GB))
    s.append(poly([(226, 298), (246, 298), (238, 318)], HI))
    # 허리 관절
    s.append(ell(256, 280, 15, 9, GD)); ells.append((256, 280, 15, 9))
    # 팔 관절
    arm = [(208, 226), (222, 228), (222, 260), (210, 262)]
    s += [poly(arm, GD), poly(mirror(arm), GD)]
    # 흉갑
    torso = [(236, 202), (276, 202), (284, 234), (270, 272), (242, 272), (228, 234)]
    s.append(poly(torso, GA)); sil.append(torso)
    s.append(poly([(256, 202), (276, 202), (284, 234), (270, 272), (256, 272)], GB))
    s.append(poly([(242, 244), (270, 244), (266, 272), (246, 272)], GD))
    s.append(poly([(238, 208), (252, 208), (244, 224)], HI))
    # 오른팔 건틀릿
    rg = [(290, 246), (304, 252), (292, 274), (274, 270), (280, 258)]
    s.append(poly(rg, GB)); sil.append(rg)
    # 창: 손잡이 + 원뿔
    s.append(poly([(232, 246), (240, 238), (248, 246), (240, 254)], GB))
    s.append(poly([(236, 250), (243, 256), (229, 272), (222, 266)], GD))
    lance_fill, lance_sil = lance((222, 274), (110, 420), 14)
    s += lance_fill; sil.append(lance_sil)
    s.append(poly([(215, 279), (223, 270), (176, 338)], HI))
    # 왼팔 건틀릿(창을 쥠)
    lg = [(206, 260), (220, 256), (236, 268), (228, 282), (210, 280)]
    s.append(poly(lg, GA)); sil.append(lg)
    pauldrons(s, sil)
    # 목
    s.append(poly([(242, 186), (270, 186), (274, 206), (238, 206)], GD))
    # 투구
    helm = [(238, 160), (256, 150), (274, 160), (280, 180), (268, 198), (244, 198), (232, 180)]
    s.append(poly(helm, GA)); sil.append(helm)
    s.append(poly([(256, 150), (274, 160), (280, 180), (268, 198), (256, 198)], GB))
    s.append(poly([(242, 173), (270, 173), (266, 182), (246, 182)], D1))
    s.append(poly([(253, 180), (259, 180), (258, 194), (254, 194)], D1))
    # 머리 장식
    plume = [(247, 158), (263, 156), (244, 96)]
    s.append(poly(plume, GA)); sil.append(plume)
    s.append(poly([(255, 157), (263, 156), (244, 96)], GB))
    return s, sil, blobs, ells


def north_shapes():
    s, blobs, ells, sil = [], [], [], []
    # 창(앞쪽 = 화면 위로 뻗음)
    lance_fill, lance_sil = lance((300, 262), (404, 102), 13)
    s += lance_fill; sil.append(lance_sil)
    # 앞다리(멀리, 옆으로 살짝 보임)
    fl = "M208,292 Q178,302 174,340 Q194,318 220,310 Z"
    for d in (fl, mirror_d(fl)):
        s.append(path(d, GC)); blobs.append(d)
    # 허리 관절
    s.append(ell(256, 280, 24, 12, GD)); ells.append((256, 280, 24, 12))
    # 팔 관절
    arm = [(208, 226), (222, 228), (222, 260), (210, 262)]
    s += [poly(arm, GD), poly(mirror(arm), GD)]
    # 등판
    back = [(236, 202), (276, 202), (284, 234), (270, 270), (242, 270), (228, 234)]
    s.append(poly(back, GB)); sil.append(back)
    s.append(poly([(236, 202), (256, 202), (256, 270), (242, 270), (228, 234)], GA))
    s.append(poly([(253, 206), (259, 206), (259, 268), (253, 268)], GC))
    s.append(poly([(234, 248), (278, 248), (274, 260), (238, 260)], GD))
    # 건틀릿
    lg = [(208, 260), (222, 258), (226, 272), (212, 278)]
    rg = [(290, 258), (304, 260), (300, 278), (286, 272)]
    s.append(poly(lg, GA)); s.append(poly(rg, GB)); sil += [lg, rg]
    pauldrons(s, sil)
    # 목 · 투구 뒷면
    s.append(poly([(242, 186), (270, 186), (274, 206), (238, 206)], GD))
    helm = [(238, 160), (256, 150), (274, 160), (280, 182), (256, 198), (232, 182)]
    s.append(poly(helm, GB)); sil.append(helm)
    s.append(poly([(238, 160), (256, 150), (256, 198), (232, 182)], GA))
    s.append(poly([(240, 184), (272, 184), (256, 196)], GC))
    plume = [(245, 160), (265, 158), (254, 90)]
    s.append(poly(plume, GA)); sil.append(plume)
    s.append(poly([(255, 159), (265, 158), (254, 90)], GB))
    # 말 엉덩이 판갑(가장 앞, 화면 아래쪽)
    rump = "M200,306 L214,286 L298,286 L312,306 L306,340 L288,356 L224,356 L206,340 Z"
    s.append(path(rump, GA)); blobs.append(rump)
    s.append(path("M256,286 L298,286 L312,306 L306,340 L288,356 L256,356 Z", GB))
    s.append(path("M204,320 L256,328 L308,320 L306,340 L288,356 L224,356 L206,340 Z", GC))
    s.append(poly([(216, 290), (236, 290), (222, 306)], HI))
    s.append(poly([(254, 288), (258, 288), (258, 354), (254, 354)], GD))
    # 뒷다리 판갑 + 가운데 꼬리판 (뾰족 3개)
    rl = "M212,330 Q180,348 188,410 Q202,374 240,352 Z"
    rl_dark = "M240,352 Q202,374 188,410 Q210,382 230,366 Z"
    s.append(path(rl, GA)); blobs.append(rl)
    s.append(path(rl_dark, GD))
    s.append(path(mirror_d(rl), GB)); blobs.append(mirror_d(rl))
    s.append(path(mirror_d(rl_dark), GD))
    tail = [(238, 346), (274, 346), (256, 400)]
    s.append(poly(tail, GB)); sil.append(tail)
    s.append(poly([(256, 346), (274, 346), (256, 400)], GC))
    return s, sil, blobs, ells


def render(shapes_fn):
    shapes, sil, blobs, ells = shapes_fn()
    outline = []
    for pts in sil:
        outline.append(poly(pts, "#000", "#000", OUTLINE))
    for d in blobs:
        outline.append(f'<path d="{d}" fill="#000" stroke="#000" stroke-width="{OUTLINE}" stroke-linejoin="round"/>')
    for cx, cy, rx, ry in ells:
        outline.append(f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" fill="#000" stroke="#000" stroke-width="{OUTLINE}"/>')
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">'
           + GRADIENTS + "".join(outline) + "".join(shapes) + "</svg>")
    png = cairosvg.svg2png(bytestring=svg.encode(), output_width=SIZE * 2, output_height=SIZE * 2)
    return Image.open(io.BytesIO(png)).convert("RGBA").resize((SIZE, SIZE), Image.LANCZOS)


ICON = 128
ICON_OUT = os.path.join(ROOT, "Textures", "UI", "Commands")


def icon_svg(body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{ICON}" height="{ICON}" viewBox="0 0 128 128">'
            + GRADIENTS + body + "</svg>")


def lance_icon(tip=(116, 22), grip=(30, 100), w=13):
    fill, sil = lance(grip, tip, w)
    out = poly(sil, "#000", "#000", 7) + "".join(fill)
    gx, gy = grip
    out += poly([(gx - 14, gy + 14), (gx - 4, gy + 4), (gx + 4, gy + 12), (gx - 6, gy + 22)], "#000", "#000", 6)
    out += poly([(gx - 14, gy + 14), (gx - 4, gy + 4), (gx + 4, gy + 12), (gx - 6, gy + 22)], D1)
    return out


def icon_charge():
    """돌격 명령: 비스듬한 창 + 속도선."""
    lines = ""
    for (x1, y1, x2, y2) in ((8, 64, 40, 32), (18, 84, 52, 50), (36, 104, 70, 70)):
        lines += f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="#000" stroke-width="8" stroke-linecap="round"/>'
        lines += f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{HI}" stroke-width="4" stroke-linecap="round"/>'
    return icon_svg(lines + lance_icon())


def icon_auto():
    """자동 돌격 토글: 창 + 순환 화살표."""
    arc = ('<path d="M34,30 A40,40 0 1 0 98,34" fill="none" stroke="#000" stroke-width="10" stroke-linecap="round"/>'
           '<path d="M34,30 A40,40 0 1 0 98,34" fill="none" stroke="#b6bbac" stroke-width="5" stroke-linecap="round"/>'
           + poly([(88, 18), (112, 30), (92, 50)], "#000", "#000", 6) + poly([(88, 18), (112, 30), (92, 50)], "#b6bbac"))
    return icon_svg(arc + lance_icon(tip=(100, 34), grip=(40, 92), w=11))


def render_icon(svg):
    png = cairosvg.svg2png(bytestring=svg.encode(), output_width=ICON * 2, output_height=ICON * 2)
    return Image.open(io.BytesIO(png)).convert("RGBA").resize((ICON, ICON), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    out = {
        "east": east(),
        "south": render(south_shapes),
        "north": render(north_shapes),
    }
    for k, im in out.items():
        im.save(os.path.join(OUT, f"GJL_IronCavalry_{k}.png"))
    os.makedirs(ICON_OUT, exist_ok=True)
    icons = {"GJL_IC_Charge": render_icon(icon_charge()), "GJL_IC_AutoCharge": render_icon(icon_auto())}
    for k, im in icons.items():
        im.save(os.path.join(ICON_OUT, f"{k}.png"))

    # 미리보기: 위 = 4방향 원본 크기, 아래 = 게임 화면 크기(칸당 약 64px 기준) + 아이콘
    bg = (64, 72, 60, 255)
    prev = Image.new("RGBA", (SIZE * 4, SIZE + 200), bg)
    views = [out["south"], out["east"], out["north"], out["east"].transpose(Image.FLIP_LEFT_RIGHT)]
    for i, im in enumerate(views):
        prev.alpha_composite(im, (SIZE * i, 0))
    small = 180  # drawSize 약 2.8칸 × 64px
    for i, im in enumerate(views):
        prev.alpha_composite(im.resize((small, small), Image.LANCZOS), (40 + 220 * i, SIZE + 10))
    gizmo_bg = (40, 44, 38, 255)
    for i, im in enumerate(icons.values()):
        x = 1000 + 200 * i
        tile = Image.new("RGBA", (ICON + 20, ICON + 20), gizmo_bg)
        tile.alpha_composite(im, (10, 10))
        prev.alpha_composite(tile, (x, SIZE + 20))
    prev.save(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "preview.png"))


if __name__ == "__main__":
    main()
