"""框架占位界面皮肤包（``assets/_placeholder/ui/skin/default/``），纯代码绘制，风格对齐
``assets/_placeholder/ui/``（gen_placeholder_assets.py 的朴素深色面板）。文件清单取自机器可读清单
``asset_import/skin_manifest.json``（与导入校验、人读清单同源）——这里按它列出的相对路径逐个出图，所以清单与校验不会漂移。
占位皮肤只出清单里"必备 + 占位皮肤必备"的元素（可选元素缺省不带，运行期回落到框架默认，保持占位皮肤逐位不变）；
:func:`generate_reference_pack` 出一整套"全元素"的确定性程序皮肤（每个元素唯一颜色），供完整性用例与导入校验用例当合格基线。"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

from . import config as C

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from asset_import import skin_manifest, skin_pack  # noqa: E402

BG = (40, 42, 50, 220)
EDGE = (15, 15, 18, 255)
CORNER = (120, 125, 140, 255)
QUALITY_COLORS = {"std_common": (170, 175, 185, 255), "std_rare": (70, 130, 220, 255)}
DEFAULT_QUALITY_COLOR = (150, 150, 150, 255)

THEME = {
    "colors": {
        "panel": "#343a48", "panel_edge": "#0f1014", "text": "#e6e8ee", "text_dim": "#9aa0ae",
        "highlight": "#ffd866", "disabled": "#5a5e6a", "ok": "#5ac878", "blocked": "#dc5a50",
    },
    "font": "fonts/noto_sans_cjk_sc.otf",
}


def _slot_base(size=(48, 48)):
    img = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([1, 1, size[0] - 2, size[1] - 2], radius=6, fill=BG, outline=EDGE, width=2)
    return img, d


def _corners(d, w, h):
    for x0, y0, dx, dy in [(3, 3, 1, 1), (w - 4, 3, -1, 1), (3, h - 4, 1, -1), (w - 4, h - 4, -1, -1)]:
        d.line([x0, y0, x0 + 6 * dx, y0], fill=CORNER, width=1)
        d.line([x0, y0, x0, y0 + 6 * dy], fill=CORNER, width=1)


def draw_slot_frame(name: str) -> Image.Image:
    img, d = _slot_base()
    _corners(d, 48, 48)
    if name == "_default":
        return img
    if name == "_highlight":
        d.rounded_rectangle([1, 1, 46, 46], radius=6, outline=(255, 216, 102, 255), width=3)
    elif name == "_disabled":
        d.rounded_rectangle([1, 1, 46, 46], radius=6, fill=(20, 20, 24, 200), outline=(60, 62, 70, 255), width=2)
        d.line([8, 8, 40, 40], fill=(90, 94, 104, 255), width=2)
    elif name == "_drag_hover":
        d.rounded_rectangle([1, 1, 46, 46], radius=6, outline=(90, 200, 120, 255), width=3)
        d.rectangle([6, 6, 41, 41], fill=(90, 200, 120, 60))
    else:  # 槽位轮廓暗示：主手画竖线（剑），其余画方块（甲）
        if "hand" in name:
            d.line([24, 10, 24, 36], fill=(80, 84, 96, 255), width=3)
            d.line([18, 30, 30, 30], fill=(80, 84, 96, 255), width=3)
        else:
            d.rectangle([14, 12, 34, 36], outline=(80, 84, 96, 255), width=2)
    return img


def draw_quality_frame(name: str) -> Image.Image:
    img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([1, 1, 46, 46], radius=6, outline=QUALITY_COLORS.get(name, DEFAULT_QUALITY_COLOR), width=3)
    return img


def draw_drag(name: str) -> Image.Image:
    img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if name == "ghost":
        d.rounded_rectangle([4, 4, 43, 43], radius=6, fill=(255, 255, 255, 90), outline=(255, 255, 255, 160), width=2)
    elif name == "target_ok":
        d.rounded_rectangle([1, 1, 46, 46], radius=6, outline=(90, 200, 120, 255), width=3)
    else:
        d.rounded_rectangle([1, 1, 46, 46], radius=6, outline=(220, 90, 80, 255), width=3)
        d.line([10, 10, 37, 37], fill=(220, 90, 80, 255), width=3)
        d.line([37, 10, 10, 37], fill=(220, 90, 80, 255), width=3)
    return img


def draw_tooltip(name: str) -> Image.Image:
    if name == "background":
        img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.rectangle([0, 0, 63, 63], fill=(24, 26, 34, 240))
        d.rectangle([0, 0, 63, 63], outline=(110, 116, 132, 255), width=2)
        return img
    if name == "divider":
        img = Image.new("RGBA", (64, 4), (0, 0, 0, 0))
        ImageDraw.Draw(img).line([0, 1, 63, 1], fill=(110, 116, 132, 255), width=2)
        return img
    img = Image.new("RGBA", (96, 16), (0, 0, 0, 0))
    ImageDraw.Draw(img).rectangle([0, 0, 95, 15], fill=(255, 255, 255, 14))
    return img


def draw_preview_background() -> Image.Image:
    img = Image.new("RGBA", (160, 192), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([1, 1, 158, 190], radius=8, fill=(28, 30, 38, 235), outline=EDGE, width=2)
    d.ellipse([40, 158, 120, 178], fill=(14, 15, 20, 200))
    return img


def _save(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG", optimize=True)


def generate_skin(assets_out: Path) -> list[str]:
    """写占位皮肤包全部文件，返回写出的包内相对路径（升序）。"""
    base = assets_out / skin_pack.SKIN_ROOT / C.SKIN_NAME
    slots = [s[0] for s in C.SLOTS]
    qualities = [q[0] for q in C.QUALITIES]
    written: list[str] = []
    for x in skin_manifest.expand(slots, qualities):
        if x.requirement == skin_manifest.REQ_OPTIONAL:
            continue
        rel = x.path
        parts = rel.split("/")
        stem = parts[-1][:-4] if rel.endswith(".png") else None
        if rel == skin_pack.THEME_FILE:
            (base / rel).parent.mkdir(parents=True, exist_ok=True)
            (base / rel).write_text(json.dumps(THEME, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
        elif parts[0] == "slot_frame":
            _save(draw_slot_frame(stem), base / rel)
        elif parts[0] == "quality_frame":
            _save(draw_quality_frame(stem), base / rel)
        elif parts[0] == "drag":
            _save(draw_drag(stem), base / rel)
        elif parts[0] == "tooltip":
            _save(draw_tooltip(stem), base / rel)
        elif parts[0] == "paperdoll_preview":
            _save(draw_preview_background(), base / rel)
        else:
            raise ValueError(f"皮肤清单出现生成器不认识的项: {rel}")
        written.append(rel)
    return sorted(written)


# ---------------------------------------------------------------------------
# 全元素参照皮肤（确定性程序绘制；每个元素一个由元素 id 与种子决定的唯一颜色）
# ---------------------------------------------------------------------------

REFERENCE_SIZES = {"cell": (48, 48), "nine_slice": (32, 32), "tooltip/divider.png": (64, 4), "tooltip/row.png": (96, 16),
                   "paperdoll_preview/background.png": (160, 192)}


def element_color(path: str, seed: int = 0) -> tuple[int, int, int]:
    """元素在参照皮肤里的唯一颜色（路径 + 种子的稳定散列，三个通道都避开 0/255 附近以免与透明/纯白混淆）。"""
    import hashlib

    h = hashlib.sha256(f"{seed}:{path}".encode("utf-8")).digest()
    return tuple(40 + h[i] % 176 for i in range(3))  # type: ignore[return-value]


def reference_size(x: "skin_manifest.ExpandedElement") -> tuple[int, int]:
    if x.path in REFERENCE_SIZES:
        return REFERENCE_SIZES[x.path]
    size = x.element.size or {}
    if size.get("group") in REFERENCE_SIZES:
        return REFERENCE_SIZES[size["group"]]
    if x.element.nine_slice:
        return REFERENCE_SIZES["nine_slice"]
    return (48, 48)


def draw_reference(x: "skin_manifest.ExpandedElement", seed: int = 0) -> Image.Image:
    """画一个元素的参照图：框类元素是 3 像素描边的镂空圆角框（中心透明、四角透明），其余元素是整幅实色；颜色 = :func:`element_color`。"""
    w, h = reference_size(x)
    r, g, b = element_color(x.path, seed)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    hollow = x.element.alpha in (skin_manifest.ALPHA_HAS_TRANSPARENCY, skin_manifest.ALPHA_TRANSPARENT_CENTER)
    if hollow:
        d.rounded_rectangle([0, 0, w - 1, h - 1], radius=6, outline=(r, g, b, 255), width=3)
    else:
        d.rectangle([0, 0, w - 1, h - 1], fill=(r, g, b, 255))
    return img


def reference_theme(seed: int = 0) -> dict:
    tokens = skin_manifest.token_spec()
    colors = {}
    for spec in tokens["colors"]:
        r, g, b = element_color("theme/" + spec["key"], seed)
        colors[spec["key"]] = f"#{r:02x}{g:02x}{b:02x}"
    doc: dict = {"colors": colors, "font": "fonts/reference_font.otf"}
    for spec in tokens["numbers"]:
        doc[spec["key"]] = int(spec["default"])
    return doc


def generate_reference_pack(pack_dir: Path, slots: list[str], qualities: list[str], *, seed: int = 0) -> list[str]:
    """把清单里**全部**元素（必备 + 可选 + 占位皮肤必备）写成一个皮肤包目录，返回写出的包内相对路径。"""
    pack_dir = Path(pack_dir)
    written: list[str] = []
    for x in skin_manifest.expand(slots, qualities):
        if x.path == skin_pack.THEME_FILE:
            (pack_dir / x.path).parent.mkdir(parents=True, exist_ok=True)
            (pack_dir / x.path).write_text(json.dumps(reference_theme(seed), indent=2) + "\n", encoding="utf-8", newline="\n")
        else:
            _save(draw_reference(x, seed), pack_dir / x.path)
        written.append(x.path)
    return sorted(written)
