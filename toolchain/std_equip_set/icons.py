"""占位装备图标（64x64 RGBA，纯代码绘制）：不含品质框、四周留透明边距（14 第 7 节、08 第 2 节）。"""

from __future__ import annotations

from PIL import Image, ImageDraw

from .config import ICON_SIZE, PALETTE

P = PALETTE
OL = P["outline"]


def _blank():
    img = Image.new("RGBA", (ICON_SIZE, ICON_SIZE), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def _sword(blade_w: int, blade_top: int, guard_w: int):
    img, d = _blank()
    cx = 32
    d.polygon([(cx, blade_top), (cx + blade_w, blade_top + 10), (cx + blade_w, 40), (cx - blade_w, 40),
               (cx - blade_w, blade_top + 10)], fill=P["metal"], outline=OL)
    d.line([cx, blade_top + 6, cx, 38], fill=P["metal_dark"], width=1)
    d.rectangle([cx - guard_w, 40, cx + guard_w, 45], fill=P["gold"], outline=OL)
    d.rectangle([cx - 3, 45, cx + 3, 56], fill=P["leather"], outline=OL)
    d.ellipse([cx - 4, 54, cx + 4, 59], fill=P["gold"], outline=OL)
    return img


def sword_1h():
    return _sword(4, 7, 10)


def greatsword():
    return _sword(6, 6, 14)


def dagger():
    img, d = _blank()
    cx = 32
    d.polygon([(cx, 14), (cx + 4, 24), (cx + 4, 38), (cx - 4, 38), (cx - 4, 24)], fill=P["metal"], outline=OL)
    d.rectangle([cx - 8, 38, cx + 8, 42], fill=P["gold"], outline=OL)
    d.rectangle([cx - 3, 42, cx + 3, 54], fill=P["leather"], outline=OL)
    return img


def bow():
    img, d = _blank()
    d.arc([14, 8, 46, 56], 100, 260, fill=P["wood"], width=4)
    d.arc([14, 8, 46, 56], 100, 260, fill=P["wood_dark"], width=1)
    d.line([24, 10, 24, 54], fill=(235, 235, 220, 255), width=1)
    d.line([24, 32, 50, 32], fill=P["wood_dark"], width=2)
    d.polygon([(50, 32), (45, 29), (45, 35)], fill=P["metal"], outline=OL)
    return img


def staff():
    img, d = _blank()
    d.line([22, 56, 40, 20], fill=P["wood_dark"], width=5)
    d.line([22, 56, 40, 20], fill=P["wood"], width=3)
    d.ellipse([34, 8, 48, 22], fill=P["gem"], outline=OL, width=2)
    d.ellipse([38, 11, 42, 15], fill=(235, 250, 255, 255))
    return img


def chestplate():
    img, d = _blank()
    d.polygon([(14, 14), (26, 10), (38, 10), (50, 14), (52, 26), (46, 30), (46, 54), (18, 54), (18, 30), (12, 26)],
              fill=P["armor"], outline=OL)
    d.line([32, 12, 32, 52], fill=P["metal_dark"], width=2)
    d.rectangle([18, 40, 46, 44], fill=P["leather"], outline=OL)
    d.ellipse([29, 38, 35, 46], fill=P["gold"], outline=OL)
    return img


DRAW = {
    "sword_1h": sword_1h, "greatsword": greatsword, "dagger": dagger, "bow": bow, "staff": staff,
    "chestplate": chestplate,
}


def draw_icon(kind: str) -> Image.Image:
    return DRAW[kind]()
