#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""参考皮肤包后处理：把 ComfyUI 出的原始 RGBA 图（gen_images.py）加工成 skin.reference_fantasy 的全部文件 + 装备图标。

    python assets/_reference_fantasy/_source/build_pack.py --raw-dir <gen_images 的 --raw-dir> [--out assets/_reference_fantasy]

做的事（全部确定性、可重复，门禁不跑本脚本；产物入库，由 toolchain/tests/test_reference_skin_pack.py 守住）：
- alpha 边缘清理：脏 alpha 清零 / 近实心拉满，半透明边缘用最近实心像素的颜色覆盖（去紫边），缩放一律在预乘 alpha 空间做；
- 框类元素四向镜像对称化；格子组（槽位框/品质框/拖拽态）统一边长 spec.cell；品质框中心区强制全透明；
- 九宫格底图（面板/提示框/按钮）由 refimg.nine_slice_from_art 装配：边框像素 = theme 数值令牌，边与中心沿拉伸方向恒定；
- 按钮五态、槽位框状态、拖拽态由同一张底图做调色/光晕派生，alpha 轮廓逐像素一致（态间对齐）；
- 图标：统一 spec.icon.size 画布与留白，主体最大边不超过 spec.icon.max_extent；
- theme.json：颜色取自成品图（面板底色/边框色）与固定的文字/状态色，数值令牌取 spec.tokens。
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import refimg as R  # noqa: E402

SPEC = json.loads((HERE / "pack_spec.json").read_text(encoding="utf-8"))
GOLD = (255, 214, 120)
GREEN = (110, 232, 140)
RED = (236, 96, 84)


def raw(raw_dir: Path, key: str) -> np.ndarray:
    seed = SPEC["chosen"][key]
    path = raw_dir / f"{key}__s{seed}.png"
    if not path.is_file():
        raise SystemExit(f"缺原始图 {path}（先跑 gen_images.py）")
    return R.load_rgba(path)


def prep(img: np.ndarray) -> np.ndarray:
    """清脏 alpha、去紫边、裁到主体包围盒。"""
    img = R.clean_alpha(img)
    img = R.defringe(img)
    return R.autocrop(img)


def square_symmetric(img: np.ndarray, size: int, *, quad: bool = True) -> np.ndarray:
    """主体裁成正方形（长宽比相差不大时直接拉到正方形，否则透明补边），四向镜像对称，缩到 size。"""
    h, w = img.shape[:2]
    if abs(w - h) / max(w, h) <= 0.12:
        img = R.resize_premul(img, max(w, h) // 2 * 2, max(w, h) // 2 * 2)
    else:
        img = R.pad_square(img)
        s = img.shape[0] // 2 * 2
        img = R.resize_premul(img, s, s)
    if quad:
        img = R.mirror_quad(img)
    return R.resize_premul(img, size, size)


def finish(img: np.ndarray) -> np.ndarray:
    return R.clean_alpha(img, lo=6.0, hi=250.0)


# ---------------------------------------------------------------------------
# 槽位框、品质框、拖拽态
# ---------------------------------------------------------------------------

def make_slot_base(raw_dir: Path, cell: int) -> np.ndarray:
    base = square_symmetric(prep(raw(raw_dir, "slot_base")), cell)
    base = R.apply_mask(base, R.rounded_mask(cell, cell, cell * 0.10))      # 圆角：保证有透明像素，也让四角干净
    return finish(base)


def make_glyph(raw_dir: Path, slot: str, cell: int) -> np.ndarray:
    key = "glyph_" + slot
    g = prep(raw(raw_dir, key))
    g = R.fit_into(g, cell, cell, int(cell * 0.46), int(cell * 0.46))
    g = R.adjust(g, brightness=0.85, saturation=0.3, tint=(150, 122, 74), tint_amount=0.35)     # 槽位占位图标：去饱和压暗的浮雕感，保留细节
    g[..., 3] = g[..., 3] * 0.8
    return g


def make_slot_frames(raw_dir: Path, cell: int) -> dict[str, np.ndarray]:
    base = make_slot_base(raw_dir, cell)
    out: dict[str, np.ndarray] = {"_default": base}
    for slot in SPEC["slots"]:
        out[slot] = finish(R.alpha_over(base, make_glyph(raw_dir, slot, cell)))
    out["_highlight"] = finish(R.add_glow(R.adjust(base, brightness=1.12), GOLD, 0.85, 2.6))
    out["_pressed"] = finish(R.add_glow(R.adjust(base, brightness=0.78), (0, 0, 0), 0.55, 3.2))
    out["_selected"] = finish(R.add_glow(R.adjust(base, brightness=1.08), (255, 236, 170), 0.95, 3.6))
    out["_disabled"] = finish(R.adjust(base, brightness=0.5, saturation=0.15))
    out["_drag_hover"] = finish(R.add_glow(R.adjust(base, brightness=1.05), GREEN, 0.9, 3.2))
    return out


def clear_center(img: np.ndarray) -> np.ndarray:
    """品质框中心区（中间一半边长）强制全透明（品质框叠在图标上，不能盖住图标）。"""
    out = img.copy()
    h, w = out.shape[:2]
    out[h // 4 - 1:h - h // 4 + 1, w // 4 - 1:w - w // 4 + 1, 3] = 0.0
    return out


def make_quality_frame(raw_dir: Path, name: str, cell: int) -> np.ndarray:
    img = square_symmetric(prep(raw(raw_dir, "quality_" + name[len("std_"):])), cell)
    if name == SPEC["qualities"][0]:
        img = R.adjust(img, brightness=1.05, saturation=0.06)               # 普通档：模型出不了纯灰钢，后处理去色成素钢
    img = clear_center(img)
    return finish(R.apply_mask(img, R.rounded_mask(cell, cell, cell * 0.08)))


def recolor_glow(img: np.ndarray, color) -> np.ndarray:
    """拖拽目标叠层：去色后按 color 上色（金属质感保留，明暗不变），中心保持透明。"""
    out = R.adjust(img, saturation=0.0)
    lum = R.luminance(out[..., :3])[..., None] / 255.0
    tint = np.array(color, dtype=np.float32)
    out[..., :3] = np.clip(tint * (0.35 + 0.9 * lum), 0, 255)
    return out


def make_drag(raw_dir: Path, cell: int, base: np.ndarray) -> dict[str, np.ndarray]:
    glow = clear_center(square_symmetric(prep(raw(raw_dir, "glow_frame")), cell))
    ghost = R.adjust(base, brightness=1.15, saturation=0.2)
    ghost[..., 3] = ghost[..., 3] * 0.6
    ok = recolor_glow(glow, GREEN)
    blocked = recolor_glow(glow, RED)
    # 不可放置：再画一个柔和的红色斜杠（色盲友好，不只靠颜色区分）
    yy, xx = np.mgrid[0:cell, 0:cell].astype(np.float32)
    d = np.abs((xx - cell / 2) + (yy - cell / 2)) / np.sqrt(2.0)
    bar = np.clip(1.0 - (d - cell * 0.035) / 1.5, 0.0, 1.0)
    inside = (np.hypot(xx - cell / 2, yy - cell / 2) < cell * 0.30).astype(np.float32)
    slash = np.zeros((cell, cell, 4), dtype=np.float32)
    slash[..., :3] = np.array(RED, dtype=np.float32)
    slash[..., 3] = 255.0 * 0.85 * bar * inside
    blocked = R.alpha_over(blocked, slash)
    return {"ghost": finish(ghost), "target_ok": finish(ok), "target_blocked": finish(blocked)}


# ---------------------------------------------------------------------------
# 面板 / 提示框 / 按钮 / 预览区
# ---------------------------------------------------------------------------

def make_panel(raw_dir: Path) -> np.ndarray:
    art = R.mirror_diag(square_symmetric(prep(raw(raw_dir, "panel_art")), 512))
    w, h = SPEC["sizes"]["panel"]
    img = R.nine_slice_from_art(art, w, h, SPEC["tokens"]["panel_border"], corner_frac=0.16, center_alpha=255.0, center_blur=6.0)
    return finish(R.apply_mask(img, np.ones((h, w), np.float32)))


def make_tooltip_bg(raw_dir: Path) -> np.ndarray:
    art = R.mirror_diag(square_symmetric(prep(raw(raw_dir, "tooltip_art")), 512))
    w, h = SPEC["sizes"]["tooltip"]
    img = R.nine_slice_from_art(art, w, h, SPEC["tokens"]["tooltip_border"], corner_frac=0.12, center_alpha=238.0, center_blur=5.0)
    return finish(img)


def make_divider(raw_dir: Path) -> np.ndarray:
    art = R.mirror_lr(prep(raw(raw_dir, "divider_art")))
    w, h = SPEC["sizes"]["divider"]
    return finish(R.fit_into(art, w, h, w, h))


def make_row(raw_dir: Path) -> np.ndarray:
    w, h = SPEC["sizes"]["row"]
    x = np.linspace(-1.0, 1.0, w, dtype=np.float32)[None, :].repeat(h, axis=0)
    a = np.clip(1.0 - np.abs(x) ** 2.2, 0.0, 1.0) * 34.0
    img = np.zeros((h, w, 4), np.float32)
    img[..., :3] = np.array((255, 240, 205), np.float32)
    img[..., 3] = a
    line = np.clip(1.0 - np.abs(x) ** 1.4, 0.0, 1.0) * 90.0
    img[h - 1, :, :3] = np.array(GOLD, np.float32)
    img[h - 1, :, 3] = line[h - 1]
    return finish(img)


def make_button_states(raw_dir: Path) -> dict[str, np.ndarray]:
    art = R.mirror_quad(prep(raw(raw_dir, "button_art")))
    art = R.resize_premul(art, art.shape[1] // 2 * 2, art.shape[0] // 2 * 2)
    w, h = SPEC["sizes"]["button"]
    normal = finish(R.nine_slice_from_art(art, w, h, SPEC["tokens"]["button_border"], corner_frac=0.22, center_alpha=255.0, center_blur=3.0))
    b = SPEC["tokens"]["button_border"]
    return {
        "normal": normal,
        "hover": finish(R.reslice(R.add_glow(R.adjust(normal, brightness=1.14), GOLD, 0.35, 2.0), b)),
        "pressed": finish(R.reslice(R.add_glow(R.adjust(normal, brightness=0.8), (0, 0, 0), 0.5, 2.6), b)),
        "disabled": finish(R.reslice(R.adjust(normal, brightness=0.55, saturation=0.12), b)),
        "selected": finish(R.reslice(R.add_glow(R.adjust(normal, brightness=1.06), (255, 236, 170), 0.9, 2.6), b)),
    }


def make_preview(raw_dir: Path) -> np.ndarray:
    art = prep(raw(raw_dir, "preview_bg"))
    w, h = SPEC["sizes"]["preview"]
    img = R.resize_premul(art, w, h)
    return finish(img)


# ---------------------------------------------------------------------------
# 图标
# ---------------------------------------------------------------------------

def make_icon(raw_dir: Path, name: str) -> np.ndarray:
    size, ext = SPEC["icon"]["size"], SPEC["icon"]["max_extent"]
    art = prep(R.erode_alpha(raw(raw_dir, "icon_" + name), 1.0))
    return finish(R.fit_into(art, size, size, ext, ext))


# ---------------------------------------------------------------------------
# 纸娃娃静态层图（sprites/item_<物品>/<朝向>/<层>.png）
# ---------------------------------------------------------------------------

PLACEHOLDER_SPRITES = HERE.parent.parent / "_placeholder" / "sprites"
DIR_WIDTH = {"front": 1.0, "front_side_r": 0.85, "side_r": 0.6, "back_side_r": 0.85, "back": 1.0}
PD = SPEC["paperdoll"]


def make_layer(art: np.ndarray, item: dict, direction: str, hand_anchor) -> np.ndarray:
    """把图标美术放到纸娃娃静态层画布里（144x144，与身体静态层按画布中心对齐，见 pack_spec.paperdoll 注释）。
    武器：转正（握柄朝上）、握点落在身体该方向的 hand_main 锚点；胸甲：盖住躯干。"""
    off_x = (PD["canvas"] - PD["body_canvas"][0]) // 2
    off_y = (PD["canvas"] - PD["body_canvas"][1]) // 2
    weapon = "length" in item
    if weapon:
        art = R.autocrop(R.rotate_premul(art, R.principal_angle_deg(art)))
    if direction.startswith("back"):
        art = R.adjust(R.flip_lr(art), brightness=0.82)
    ah, aw = art.shape[:2]
    if weapon:
        nh = item["length"]
        nw = max(2, int(round(aw * nh / ah * DIR_WIDTH[direction] * item.get("thick", 1.0))))
        x = hand_anchor[0] + off_x - nw / 2
        y = hand_anchor[1] + off_y - item["grip"] * nh
    else:
        nh = item["size"]
        nw = max(2, int(round(aw * nh / ah * DIR_WIDTH[direction])))
        x0, y0, x1, y1 = PD["torso"]
        x = (x0 + x1) / 2 + off_x - nw / 2
        y = (y0 + y1) / 2 + off_y - nh / 2
    img = R.resize_premul(art, nw, nh)
    canvas = np.zeros((PD["canvas"], PD["canvas"], 4), np.float32)
    ox, oy = int(round(x)), int(round(y))
    canvas[oy:oy + nh, ox:ox + nw] = img
    return finish(canvas)


def grip_anchors(spec: dict, anchors: dict) -> dict:
    """武器集 anchors.json：每个方向的握点（层图像素坐标，原点左上）= 身体该方向 hand_main 锚点 + 画布偏移（见 make_layer）。
    预览区按它把层的握点对到身体的 hand_main 上（框架契约：skin_manifest 的 paperdoll.layer_anchor）。"""
    off_x = (PD["canvas"] - PD["body_canvas"][0]) // 2
    off_y = (PD["canvas"] - PD["body_canvas"][1]) // 2
    return {"canvas": {"width": PD["canvas"], "height": PD["canvas"]},
            "directions": {d: {"grip": [anchors[d]["hand_main"][0] + off_x, anchors[d]["hand_main"][1] + off_y]}
                           for d in DIR_WIDTH}}


def layer_files(raw_dir: Path) -> dict[str, np.ndarray]:
    anchors = json.loads((PLACEHOLDER_SPRITES / "placeholder_hero" / "anchors.json").read_text(encoding="utf-8"))["directions"]
    out: dict[str, np.ndarray] = {}
    for item, spec in PD["items"].items():
        art = prep(R.erode_alpha(raw(raw_dir, "icon_" + item), 1.0))
        for d in DIR_WIDTH:
            out[f"sprites/item_{item}/{d}/{spec['layer']}.png"] = make_layer(art, spec, d, anchors[d]["hand_main"])
    return out


# ---------------------------------------------------------------------------
# theme
# ---------------------------------------------------------------------------

def hexcolor(rgb) -> str:
    return "#%02x%02x%02x" % tuple(int(v) for v in rgb)


def make_theme(panel_img: np.ndarray) -> dict:
    h, w = panel_img.shape[:2]
    panel = R.dominant_color(panel_img)
    edge = R.dominant_color(panel_img, (w // 2 - 4, 1, w // 2 + 4, 6))
    return {
        "colors": {
            "panel": hexcolor(panel),
            "panel_edge": hexcolor(edge),
            "text": "#f0e4c8",
            "text_dim": "#b3a58a",
            "highlight": "#f2c14e",
            "disabled": "#6d6a66",
            "ok": "#6fcf7c",
            "blocked": "#e0584c",
        },
        "font": SPEC["font"],
        "panel_border": SPEC["tokens"]["panel_border"],
        "tooltip_border": SPEC["tokens"]["tooltip_border"],
        "button_border": SPEC["tokens"]["button_border"],
    }


# ---------------------------------------------------------------------------
# 装配
# ---------------------------------------------------------------------------

def build(raw_dir: Path, out: Path) -> list[str]:
    skin = out / "ui" / "skin" / SPEC["pack"]
    written: list[str] = []

    def put(rel: str, img: np.ndarray, base: Path = skin) -> None:
        R.save_png(img, base / rel)
        written.append(str((base / rel).relative_to(out)).replace("\\", "/"))

    cell = SPEC["cell"]
    slots = make_slot_frames(raw_dir, cell)
    for name, img in slots.items():
        put(f"slot_frame/{name}.png", img)
    for q in SPEC["qualities"]:
        put(f"quality_frame/{q}.png", make_quality_frame(raw_dir, q, cell))
    put("quality_frame/_default.png", make_quality_frame(raw_dir, SPEC["qualities"][0], cell))
    for name, img in make_drag(raw_dir, cell, slots["_default"]).items():
        put(f"drag/{name}.png", img)
    put("tooltip/background.png", make_tooltip_bg(raw_dir))
    put("tooltip/divider.png", make_divider(raw_dir))
    put("tooltip/row.png", make_row(raw_dir))
    put("paperdoll_preview/background.png", make_preview(raw_dir))
    panel = make_panel(raw_dir)
    put("panel/background.png", panel)
    for state, img in make_button_states(raw_dir).items():
        put(f"button/{state}.png", img)
    theme = make_theme(panel)
    (skin / "theme.json").parent.mkdir(parents=True, exist_ok=True)
    (skin / "theme.json").write_text(json.dumps(theme, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    written.append(f"ui/skin/{SPEC['pack']}/theme.json")
    for item in ("std_sword_1h", "std_greatsword", "std_dagger", "std_bow", "std_staff", "std_chestplate"):
        put(f"icons/item/{item}.png", make_icon(raw_dir, item), out)
    for rel, img in layer_files(raw_dir).items():
        put(rel, img, out)
    anchors = json.loads((PLACEHOLDER_SPRITES / "placeholder_hero" / "anchors.json").read_text(encoding="utf-8"))["directions"]
    for item, spec in PD["items"].items():
        if "length" not in spec:
            continue            # 非武器层（胸甲）按画布居中叠放，不声明握点
        path = out / "sprites" / f"item_{item}" / "anchors.json"
        path.write_text(json.dumps(grip_anchors(spec, anchors), indent=2) + "\n", encoding="utf-8", newline="\n")
        written.append(str(path.relative_to(out)).replace("\\", "/"))
    return sorted(written)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--raw-dir", required=True)
    ap.add_argument("--out", default=str(HERE.parent))
    args = ap.parse_args(argv)
    files = build(Path(args.raw_dir), Path(args.out))
    print(f"wrote {len(files)} files under {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
