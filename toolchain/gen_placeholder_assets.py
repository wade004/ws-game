#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_placeholder_assets.py
==========================

生成框架级"通用占位资产包"（assets/_placeholder/），供任何新游戏第一天即可用于
灰盒竖切验收。全部资源由本脚本用 Pillow + Python 标准库确定性生成，不依赖任何
外部素材/网络下载。

用法：
    python toolchain/gen_placeholder_assets.py [--out assets/_placeholder] [--seed 1]
    python toolchain/gen_placeholder_assets.py --check [--out assets/_placeholder]

--check 模式不重新生成文件，只校验目标目录下应存在的文件是否存在、图片尺寸/模式、
音效采样参数是否符合规格，用于 CI/提交门槛（呼应 architecture/11 第 2.1 节"资产校验"）。

确定性：同一 --seed 多次运行，产出字节完全一致（不使用系统时间、不依赖 set/dict
迭代顺序之外的不确定源；所有随机数来自 random.Random(种子字符串)）。

参照文档：
    architecture/04_数据与内容管线.md 第 7.1 节（DisplayInfo / display.map 字段）
    architecture/09_表现层.md 第 3.2~3.4、5 节（方向量化与镜像、纸娃娃锚点、VFX/SFX）
    architecture/11_工程规范与测试.md 第 1、2.1 节（目录约定、框架级资产交付物）
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import struct
import sys
import wave
from pathlib import Path

from PIL import Image, ImageDraw

# 允许直接以 "python toolchain/gen_placeholder_assets.py" 方式运行（不依赖 PYTHONPATH/
# 包安装），与 toolchain/import_assets.py 的惯例一致；把 toolchain/ 目录本身放进 sys.path
# 后即可直接 import 顶层的 _console 模块。
sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402

# --------------------------------------------------------------------------
# 常量与调色板
# --------------------------------------------------------------------------

PIXELS_PER_UNIT = 32
HERO_CANVAS = (64, 96)
BEAST_CANVAS = (48, 48)

# 8 方向档位命名，取自 architecture/14_资产规格书模板.md 第 2.1 节命名表（权威）；
# 右侧 5 档（_r 后缀 / 无侧向后缀的 front、back）直接绘制，左侧 3 档（_l 后缀）靠镜像回填。
HERO_DIRS_ALL = [
    "front",
    "front_side_r",
    "side_r",
    "back_side_r",
    "back",
    "back_side_l",
    "side_l",
    "front_side_l",
]
HERO_DIRS_AUTHORED = [
    "front",
    "front_side_r",
    "side_r",
    "back_side_r",
    "back",
]
HERO_MIRROR_PAIRS = [
    {"direction_slot": "front_side_l", "mirror_of": "front_side_r", "flip_x": True},
    {"direction_slot": "side_l", "mirror_of": "side_r", "flip_x": True},
    {"direction_slot": "back_side_l", "mirror_of": "back_side_r", "flip_x": True},
]
HERO_BACK_DIRS = {"back", "back_side_r", "back_side_l"}
# 各方向主手臂/武器相对躯干中线的横向偏移量（像素，正值=右偏）
HERO_OFFSET_MAG = {
    "front": 6,
    "front_side_r": 14,
    "side_r": 20,
    "back_side_r": 14,
    "back": 6,
}

BEAST_DIRS_ALL = list(HERO_DIRS_ALL)
# 每个方向的朝向单位向量（用于眼睛偏移），与 8 方向命名一一对应
BEAST_FACING_VEC = {
    "front": (0.0, 1.0),
    "front_side_r": (0.7, 0.7),
    "side_r": (1.0, 0.0),
    "back_side_r": (0.7, -0.7),
    "back": (0.0, -1.0),
    "back_side_l": (-0.7, -0.7),
    "side_l": (-1.0, 0.0),
    "front_side_l": (-0.7, 0.7),
}

COL_OUTLINE = (20, 20, 20, 255)
COL_SKIN = (222, 180, 140, 255)
COL_SKIN_BACK = (150, 120, 95, 255)
COL_SHIRT = (70, 130, 180, 255)
COL_SHIRT_BACK = (40, 90, 130, 255)
COL_PANTS = (90, 70, 50, 255)
COL_HAIR_BACK = (60, 45, 35, 255)
COL_ACCESSORY = (200, 60, 60, 255)
COL_WEAPON = (170, 170, 180, 255)
COL_WEAPON_GRIP = (110, 80, 50, 255)

COL_BEAST_BODY = (120, 170, 90, 255)
COL_BEAST_BODY_BACK = (80, 120, 60, 255)
COL_EYE_WHITE = (250, 250, 245, 255)
COL_EYE_PUPIL = (25, 20, 20, 255)

QUALITY_BORDERS = {
    "common": (150, 150, 150, 255),
    "rare": (60, 120, 220, 255),
}

SAMPLE_RATE = 44100
SFX_PEAK_DBFS = -12.0
MUSIC_PEAK_DBFS = -14.0
MUSIC_LOOP_DURATION_S = 4.0


# --------------------------------------------------------------------------
# 工具函数
# --------------------------------------------------------------------------

def rng_for(seed: int, name: str) -> random.Random:
    """按固定 seed + 资源名派生确定性 RNG，保证同 seed 下逐资源可复现且互不干扰。"""
    return random.Random(f"{seed}:{name}")


def flip_point_x(pt, width):
    x, y = pt
    return [width - x, y]


def save_png(img: "Image.Image", path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG", optimize=True)


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


# --------------------------------------------------------------------------
# Manifest：记录本次生成的每个文件，供 --check 与验收使用
# --------------------------------------------------------------------------

class Manifest:
    def __init__(self):
        self.entries = []  # list of dict

    def add(self, path: Path, kind: str, meta: dict):
        self.entries.append({
            "path": path.as_posix(),
            "kind": kind,
            "bytes": path.stat().st_size,
            "sha256": sha256_of(path),
            "meta": meta,
        })

    def write(self, out_dir: Path, seed: int):
        data = {
            "seed": seed,
            "pixels_per_unit": PIXELS_PER_UNIT,
            "file_count": len(self.entries),
            "total_bytes": sum(e["bytes"] for e in self.entries),
            "files": self.entries,
        }
        (out_dir / "MANIFEST.json").write_text(
            json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8"
        )
        return data


# ==========================================================================
# 1. sprites/placeholder_hero
# ==========================================================================

def draw_hero_body(direction: str, rng: random.Random) -> "Image.Image":
    w, h = HERO_CANVAS
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    back = direction in HERO_BACK_DIRS
    mag = HERO_OFFSET_MAG[direction]
    skin = COL_SKIN_BACK if back else COL_SKIN
    shirt = COL_SHIRT_BACK if back else COL_SHIRT

    # 腿部
    d.rectangle([24, 64, 29, 92], fill=COL_PANTS, outline=COL_OUTLINE)
    d.rectangle([35, 64, 40, 92], fill=COL_PANTS, outline=COL_OUTLINE)

    # 远侧手臂（先画，被躯干/近侧手臂压住一部分）
    far_x = 32 - mag * 0.4
    d.rectangle([far_x - 4, 34, far_x + 4, 58], fill=shirt, outline=COL_OUTLINE)

    # 躯干
    d.rectangle([22, 30, 42, 66], fill=shirt, outline=COL_OUTLINE)

    # 近侧（主手）手臂
    near_x = 32 + mag
    d.rectangle([near_x - 4, 34, near_x + 4, 58], fill=shirt, outline=COL_OUTLINE)

    # 头部
    head_cx, head_cy = 32, 20
    d.ellipse([head_cx - 10, head_cy - 10, head_cx + 10, head_cy + 10], fill=skin, outline=COL_OUTLINE)
    if not back:
        eye_dx = 3 if mag >= 0 else -3
        cx = head_cx + (eye_dx if direction != "front" else 0)
        d.ellipse([cx - 4, head_cy - 2, cx - 1, head_cy + 1], fill=COL_OUTLINE)
        if direction in ("front", "front_side_r"):
            d.ellipse([cx + 1, head_cy - 2, cx + 4, head_cy + 1], fill=COL_OUTLINE)
    else:
        d.pieslice(
            [head_cx - 10, head_cy - 10, head_cx + 10, head_cy + 10],
            190, 350, fill=COL_HAIR_BACK,
        )
    return img


def draw_hero_hand(direction: str, rng: random.Random):
    w, h = HERO_CANVAS
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    mag = HERO_OFFSET_MAG[direction]
    hx, hy = 32 + mag, 58
    ex = hx + (7 if mag >= 0 else -7)
    ey = hy + 22
    d.line([hx, hy, ex, ey], fill=COL_WEAPON, width=4)
    d.ellipse([hx - 3, hy - 3, hx + 3, hy + 3], fill=COL_WEAPON_GRIP, outline=COL_OUTLINE)
    return img, [hx, hy]


def draw_hero_head_acc(direction: str, rng: random.Random):
    w, h = HERO_CANVAS
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    mag = HERO_OFFSET_MAG[direction] * 0.3
    cx, cy = 32 + mag, 10
    d.rectangle([cx - 8, cy - 6, cx + 8, cy + 4], fill=COL_ACCESSORY, outline=COL_OUTLINE)
    return img, [cx, cy - 6]


def gen_hero(out: Path, seed: int, manifest: Manifest):
    base = out / "sprites" / "placeholder_hero"
    directions_data = {}

    for direction in HERO_DIRS_ALL:
        d_dir = base / direction
        d_dir.mkdir(parents=True, exist_ok=True)

    for direction in HERO_DIRS_AUTHORED:
        d_dir = base / direction
        rng = rng_for(seed, f"hero_body_{direction}")
        body = draw_hero_body(direction, rng)
        p = d_dir / "body.png"
        save_png(body, p)
        manifest.add(p, "image", {"size": list(HERO_CANVAS), "mode": "RGBA"})

        rng = rng_for(seed, f"hero_hand_{direction}")
        hand, hand_anchor = draw_hero_hand(direction, rng)
        p = d_dir / "hand_main.png"
        save_png(hand, p)
        manifest.add(p, "image", {"size": list(HERO_CANVAS), "mode": "RGBA"})

        rng = rng_for(seed, f"hero_head_{direction}")
        head, head_anchor = draw_hero_head_acc(direction, rng)
        p = d_dir / "head.png"
        save_png(head, p)
        manifest.add(p, "image", {"size": list(HERO_CANVAS), "mode": "RGBA"})

        root = [32, 92]
        overhead = [32, 2]
        directions_data[direction] = {
            "root": root,
            "hand_main": hand_anchor,
            "head": head_anchor,
            "overhead": overhead,
        }

    # 左侧 3 档：不落盘图片，靠镜像回填；锚点按镜像源沿 x=32 翻转写入，供导入工具/
    # 引擎运行时对镜像方向直接复用翻转后的锚点，而不必重新标注。
    w = HERO_CANVAS[0]
    for pair in HERO_MIRROR_PAIRS:
        src = directions_data[pair["mirror_of"]]
        directions_data[pair["direction_slot"]] = {
            "root": flip_point_x(src["root"], w),
            "hand_main": flip_point_x(src["hand_main"], w),
            "head": flip_point_x(src["head"], w),
            "overhead": flip_point_x(src["overhead"], w),
        }

    anchors = {
        "canvas": {"width": HERO_CANVAS[0], "height": HERO_CANVAS[1]},
        "pixels_per_unit": PIXELS_PER_UNIT,
        "direction_count": 8,
        "authored_directions": HERO_DIRS_AUTHORED,
        "layers": ["body", "hand_main", "head"],
        "directions": directions_data,
        "mirror_pairs": HERO_MIRROR_PAIRS,
    }
    p = base / "anchors.json"
    p.write_text(json.dumps(anchors, indent=2, ensure_ascii=False), encoding="utf-8")
    manifest.add(p, "json", {})


# ==========================================================================
# 2. sprites/placeholder_beast
# ==========================================================================

def draw_beast_body(direction: str, rng: random.Random) -> "Image.Image":
    w, h = BEAST_CANVAS
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    back = direction in HERO_BACK_DIRS
    body_col = COL_BEAST_BODY_BACK if back else COL_BEAST_BODY

    x0, y0, x1, y1 = 8, 10, 40, 42
    d.rectangle([x0, y0, x1, y1], fill=body_col, outline=COL_OUTLINE, width=2)
    # 微小方块纹理（确定性抖动）
    for _ in range(6):
        bx = rng.randint(x0 + 2, x1 - 6)
        by = rng.randint(y0 + 2, y1 - 6)
        shade = (body_col[0] - 15, body_col[1] - 15, body_col[2] - 15, 255)
        d.rectangle([bx, by, bx + 3, by + 3], fill=shade)

    if not back:
        fx, fy = BEAST_FACING_VEC[direction]
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2 - 4
        ox, oy = fx * 8, fy * 4
        for sx in (-6, 6):
            ex, ey = cx + sx + ox, cy + oy
            d.ellipse([ex - 3, ey - 3, ex + 3, ey + 3], fill=COL_EYE_WHITE, outline=COL_OUTLINE)
            px, py = ex + ox * 0.3, ey + oy * 0.3
            d.ellipse([px - 1, py - 1, px + 1, py + 1], fill=COL_EYE_PUPIL)
    return img


def gen_beast(out: Path, seed: int, manifest: Manifest):
    base = out / "sprites" / "placeholder_beast"
    directions_data = {}
    for direction in BEAST_DIRS_ALL:
        d_dir = base / direction
        rng = rng_for(seed, f"beast_body_{direction}")
        body = draw_beast_body(direction, rng)
        p = d_dir / "body.png"
        save_png(body, p)
        manifest.add(p, "image", {"size": list(BEAST_CANVAS), "mode": "RGBA"})
        directions_data[direction] = {"root": [24, 44]}

    anchors = {
        "canvas": {"width": BEAST_CANVAS[0], "height": BEAST_CANVAS[1]},
        "pixels_per_unit": PIXELS_PER_UNIT,
        "direction_count": 8,
        "authored_directions": BEAST_DIRS_ALL,
        "layers": ["body"],
        "directions": directions_data,
        "mirror_pairs": [],
    }
    p = base / "anchors.json"
    p.write_text(json.dumps(anchors, indent=2, ensure_ascii=False), encoding="utf-8")
    manifest.add(p, "json", {})


# ==========================================================================
# 3. sprites/placeholder_chest, placeholder_door
# ==========================================================================

def gen_chest(out: Path, seed: int, manifest: Manifest):
    base = out / "sprites" / "placeholder_chest"
    size = (48, 48)
    for state in ("closed", "open"):
        img = Image.new("RGBA", size, (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        wood = (139, 90, 43, 255)
        wood_dark = (100, 62, 28, 255)
        metal = (180, 180, 190, 255)
        d.rectangle([6, 20, 42, 42], fill=wood, outline=COL_OUTLINE, width=2)
        d.rectangle([6, 20, 42, 24], fill=wood_dark)
        if state == "closed":
            d.rectangle([6, 12, 42, 22], fill=wood, outline=COL_OUTLINE, width=2)
            d.rectangle([21, 18, 27, 26], fill=metal, outline=COL_OUTLINE)
        else:
            # 开启：盖子上掀，露出内部暖色高光
            d.polygon([(6, 20), (42, 20), (40, 4), (8, 8)], fill=wood, outline=COL_OUTLINE)
            d.rectangle([10, 22, 38, 40], fill=(240, 210, 120, 255))
            d.rectangle([21, 18, 27, 22], fill=metal, outline=COL_OUTLINE)
        p = base / f"{state}.png"
        save_png(img, p)
        manifest.add(p, "image", {"size": list(size), "mode": "RGBA"})


def gen_door(out: Path, seed: int, manifest: Manifest):
    base = out / "sprites" / "placeholder_door"
    size = (48, 64)
    for state in ("closed", "open"):
        img = Image.new("RGBA", size, (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        frame = (90, 70, 55, 255)
        panel = (130, 95, 60, 255)
        d.rectangle([2, 2, 46, 62], outline=frame, width=3)
        if state == "closed":
            d.rectangle([6, 6, 42, 58], fill=panel, outline=COL_OUTLINE)
            d.rectangle([12, 12, 36, 30], outline=COL_OUTLINE, width=1)
            d.rectangle([12, 34, 36, 52], outline=COL_OUTLINE, width=1)
            d.ellipse([32, 32, 37, 37], fill=(200, 190, 120, 255), outline=COL_OUTLINE)
        else:
            # 开启：门扇贴左侧墙收拢，露出黑色门洞
            d.rectangle([6, 6, 42, 58], fill=(10, 10, 14, 255))
            d.rectangle([2, 2, 10, 62], fill=panel, outline=COL_OUTLINE)
        p = base / f"{state}.png"
        save_png(img, p)
        manifest.add(p, "image", {"size": list(size), "mode": "RGBA"})


# ==========================================================================
# 4. icons/
# ==========================================================================

def _icon_frame(d: "ImageDraw.ImageDraw", quality: str):
    border = QUALITY_BORDERS[quality]
    d.rounded_rectangle([2, 2, 61, 61], radius=8, outline=border, width=3)


def draw_icon_blade(quality: str) -> "Image.Image":
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    _icon_frame(d, quality)
    blade = (200, 200, 210, 255)
    d.polygon([(32, 8), (38, 40), (32, 46), (26, 40)], fill=blade, outline=COL_OUTLINE)
    d.rectangle([22, 40, 42, 46], fill=(90, 70, 50, 255), outline=COL_OUTLINE)
    d.rectangle([29, 46, 35, 58], fill=(110, 80, 55, 255), outline=COL_OUTLINE)
    return img


def draw_icon_tonic(quality: str) -> "Image.Image":
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    _icon_frame(d, quality)
    d.rectangle([28, 10, 36, 18], fill=(120, 90, 60, 255), outline=COL_OUTLINE)
    d.ellipse([18, 20, 46, 52], fill=(210, 235, 245, 200), outline=COL_OUTLINE, width=2)
    d.pieslice([18, 30, 46, 52], 0, 180, fill=(60, 190, 110, 255))
    return img


def draw_icon_token(quality: str) -> "Image.Image":
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    _icon_frame(d, quality)
    d.ellipse([14, 14, 50, 50], fill=(230, 190, 70, 255), outline=COL_OUTLINE, width=2)
    d.ellipse([20, 20, 44, 44], outline=(160, 120, 30, 255), width=2)
    d.line([32, 24, 32, 40], fill=(160, 120, 30, 255), width=3)
    d.line([24, 32, 40, 32], fill=(160, 120, 30, 255), width=3)
    return img


def draw_icon_skill_strike(quality: str) -> "Image.Image":
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    _icon_frame(d, quality)
    cx, cy, r_out, r_in, spikes = 32, 32, 20, 9, 8
    pts = []
    for i in range(spikes * 2):
        r = r_out if i % 2 == 0 else r_in
        ang = math.pi * i / spikes
        pts.append((cx + r * math.sin(ang), cy - r * math.cos(ang)))
    d.polygon(pts, fill=(230, 90, 60, 255), outline=COL_OUTLINE)
    return img


def draw_icon_skill_burn(quality: str) -> "Image.Image":
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    _icon_frame(d, quality)
    d.ellipse([16, 26, 48, 54], fill=(200, 50, 20, 255), outline=COL_OUTLINE)
    d.ellipse([20, 16, 44, 44], fill=(240, 130, 30, 255))
    d.ellipse([25, 10, 39, 30], fill=(250, 200, 60, 255))
    return img


ICON_DEFS = {
    "icon_placeholder_blade": draw_icon_blade,
    "icon_placeholder_tonic": draw_icon_tonic,
    "icon_placeholder_token": draw_icon_token,
    "icon_placeholder_skill_strike": draw_icon_skill_strike,
    "icon_placeholder_skill_burn": draw_icon_skill_burn,
}


def gen_icons(out: Path, seed: int, manifest: Manifest):
    base = out / "icons"
    for name, fn in ICON_DEFS.items():
        img_common = fn("common")
        p = base / f"{name}.png"
        save_png(img_common, p)
        manifest.add(p, "image", {"size": [64, 64], "mode": "RGBA", "quality": "common"})

        img_rare = fn("rare")
        p = base / f"{name}_rare.png"
        save_png(img_rare, p)
        manifest.add(p, "image", {"size": [64, 64], "mode": "RGBA", "quality": "rare"})


# ==========================================================================
# 5. vfx/
# ==========================================================================

def draw_hit_spark_frame(i: int, rng: random.Random) -> "Image.Image":
    size = 32
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx, cy = size / 2, size / 2
    radius = 3 + i * 1.7
    alpha = max(0, 255 - i * 30)
    spikes = 8
    for k in range(spikes):
        ang = 2 * math.pi * k / spikes + 0.15 * i
        ex = cx + radius * math.cos(ang)
        ey = cy + radius * math.sin(ang)
        d.line([cx, cy, ex, ey], fill=(255, 200, 60, alpha), width=2)
    core_r = max(1.0, 5 - i * 0.5)
    d.ellipse([cx - core_r, cy - core_r, cx + core_r, cy + core_r], fill=(255, 250, 220, alpha))
    return img


def draw_burn_frame(i: int, rng: random.Random) -> "Image.Image":
    size = 32
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    wobble = 2.5 * math.sin(2 * math.pi * i / 8)
    base_cx, base_cy = size / 2, size - 6
    d.ellipse([base_cx - 9, base_cy - 8, base_cx + 9, base_cy + 4], fill=(190, 40, 15, 235))
    mid_cx = base_cx + wobble * 0.5
    d.ellipse([mid_cx - 6, base_cy - 18, mid_cx + 6, base_cy - 2], fill=(240, 120, 30, 235))
    tip_cx = base_cx + wobble
    d.ellipse([tip_cx - 3, base_cy - 26, tip_cx + 3, base_cy - 14], fill=(250, 200, 60, 220))
    return img


def draw_cast_circle_frame(i: int, rng: random.Random) -> "Image.Image":
    size = 32
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx, cy = size / 2, size / 2
    radius = 3 + i * 1.6
    alpha_by_i = [255, 255, 255, 255, 255, 255, 180, 90]
    alpha = alpha_by_i[i]
    col = (90, 200, 250, alpha)
    if radius > 1:
        d.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], outline=col, width=2)
    ticks = 8
    for k in range(ticks):
        ang = 2 * math.pi * k / ticks
        r0 = radius - 2
        r1 = radius + 2
        if r0 < 0:
            continue
        x0, y0 = cx + r0 * math.cos(ang), cy + r0 * math.sin(ang)
        x1, y1 = cx + r1 * math.cos(ang), cy + r1 * math.sin(ang)
        d.line([x0, y0, x1, y1], fill=col, width=1)
    return img


VFX_DEFS = {
    "hit_spark": {"fn": draw_hit_spark_frame, "loop": False},
    "burn": {"fn": draw_burn_frame, "loop": True},
    "cast_circle": {"fn": draw_cast_circle_frame, "loop": False},
}

VFX_FRAME_COUNT = 8
VFX_FRAME_SIZE = 32
VFX_FRAME_DURATION = 0.05


def gen_vfx(out: Path, seed: int, manifest: Manifest):
    base = out / "vfx"
    for name, spec in VFX_DEFS.items():
        d_dir = base / name
        rng = rng_for(seed, f"vfx_{name}")
        frames_img = []
        for i in range(VFX_FRAME_COUNT):
            frame = spec["fn"](i, rng)
            frames_img.append(frame)
            p = d_dir / f"frame_{i:02d}.png"
            save_png(frame, p)
            manifest.add(p, "image", {"size": [VFX_FRAME_SIZE, VFX_FRAME_SIZE], "mode": "RGBA"})

        atlas_w = VFX_FRAME_SIZE * VFX_FRAME_COUNT
        atlas = Image.new("RGBA", (atlas_w, VFX_FRAME_SIZE), (0, 0, 0, 0))
        frame_rects = []
        for i, frame in enumerate(frames_img):
            x = i * VFX_FRAME_SIZE
            atlas.paste(frame, (x, 0), frame)
            frame_rects.append({
                "index": i, "x": x, "y": 0,
                "w": VFX_FRAME_SIZE, "h": VFX_FRAME_SIZE,
                "duration": VFX_FRAME_DURATION,
            })
        p = d_dir / "atlas.png"
        save_png(atlas, p)
        manifest.add(p, "image", {"size": [atlas_w, VFX_FRAME_SIZE], "mode": "RGBA"})

        frames_json = {
            "frame_w": VFX_FRAME_SIZE,
            "frame_h": VFX_FRAME_SIZE,
            "fps": round(1 / VFX_FRAME_DURATION),
            "frame_duration": VFX_FRAME_DURATION,
            "loop": spec["loop"],
            "frames": frame_rects,
        }
        p = d_dir / "frames.json"
        p.write_text(json.dumps(frames_json, indent=2, ensure_ascii=False), encoding="utf-8")
        manifest.add(p, "json", {})


# ==========================================================================
# 6. sfx/
# ==========================================================================

def envelope_linear_fade(n, attack_n, release_n):
    env = [1.0] * n
    for i in range(min(attack_n, n)):
        env[i] = i / max(1, attack_n)
    for i in range(min(release_n, n)):
        env[n - 1 - i] = min(env[n - 1 - i], i / max(1, release_n))
    return env


def synth_noise_burst(duration_s: float, rng: random.Random, decay: float = 8.0):
    n = int(SAMPLE_RATE * duration_s)
    samples = [0.0] * n
    for i in range(n):
        t = i / SAMPLE_RATE
        env = math.exp(-decay * t)
        samples[i] = rng.uniform(-1.0, 1.0) * env
    return samples


def synth_sweep(duration_s: float, f0: float, f1: float, attack=0.05, release=0.3):
    n = int(SAMPLE_RATE * duration_s)
    samples = [0.0] * n
    env = envelope_linear_fade(n, int(n * attack), int(n * release))
    phase = 0.0
    for i in range(n):
        t = i / SAMPLE_RATE
        frac = t / duration_s if duration_s > 0 else 0
        freq = f0 + (f1 - f0) * frac
        phase += 2 * math.pi * freq / SAMPLE_RATE
        samples[i] = math.sin(phase) * env[i]
    return samples


def synth_arpeggio(duration_s: float, freqs, attack=0.02, release=0.15):
    n = int(SAMPLE_RATE * duration_s)
    samples = [0.0] * n
    step_n = n // len(freqs)
    for idx, f in enumerate(freqs):
        start = idx * step_n
        end = n if idx == len(freqs) - 1 else start + step_n
        seg_n = end - start
        env = envelope_linear_fade(seg_n, int(seg_n * attack), int(seg_n * release))
        phase = 0.0
        for i in range(seg_n):
            phase += 2 * math.pi * f / SAMPLE_RATE
            samples[start + i] = math.sin(phase) * env[i]
    return samples


def normalize_peak(samples, target_dbfs=SFX_PEAK_DBFS):
    peak = max((abs(s) for s in samples), default=0.0)
    if peak <= 1e-9:
        return samples
    target = 10 ** (target_dbfs / 20.0)
    scale = target / peak
    return [s * scale for s in samples]


def write_wav(path: Path, samples):
    path.parent.mkdir(parents=True, exist_ok=True)
    ints = [max(-32768, min(32767, int(round(s * 32767)))) for s in samples]
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(struct.pack("<%dh" % len(ints), *ints))


def gen_sfx(out: Path, seed: int, manifest: Manifest):
    base = out / "sfx"
    specs = {}

    rng = rng_for(seed, "sfx_hit_01")
    specs["hit_01"] = normalize_peak(synth_noise_burst(0.18, rng, decay=14.0))

    rng = rng_for(seed, "sfx_hit_02")
    specs["hit_02"] = normalize_peak(synth_noise_burst(0.15, rng, decay=20.0))

    specs["swing_01"] = normalize_peak(synth_sweep(0.25, 900, 220, attack=0.05, release=0.55))
    specs["cast_01"] = normalize_peak(synth_sweep(0.40, 300, 900, attack=0.1, release=0.35))
    specs["death_01"] = normalize_peak(synth_sweep(0.50, 500, 70, attack=0.02, release=0.7))
    specs["ui_click_01"] = normalize_peak(synth_sweep(0.05, 1400, 900, attack=0.02, release=0.6))
    specs["ui_open_01"] = normalize_peak(synth_sweep(0.15, 500, 850, attack=0.05, release=0.5))
    specs["pickup_01"] = normalize_peak(synth_sweep(0.15, 600, 1300, attack=0.02, release=0.6))
    specs["level_up_01"] = normalize_peak(
        synth_arpeggio(0.50, [523.25, 659.25, 783.99, 1046.50], attack=0.02, release=0.25)
    )

    for name, samples in specs.items():
        p = base / f"{name}.wav"
        write_wav(p, samples)
        duration = len(samples) / SAMPLE_RATE
        peak = max((abs(s) for s in samples), default=0.0)
        manifest.add(p, "wav", {
            "sample_rate": SAMPLE_RATE, "channels": 1, "sampwidth": 2,
            "duration_s": round(duration, 4), "peak": round(peak, 4),
        })


# ==========================================================================
# 6b. music/loop_01.wav（确定性、可无缝循环的占位背景音乐）
# ==========================================================================

def snap_freq_to_loop(freq_hz: float, duration_s: float) -> float:
    """把频率吸附到"在 duration_s 内恰好走过整数个周期"的最近取值。

    正弦波频率若满足 f * duration_s = 整数，则该正弦波在 [0, duration_s) 区间首尾的取值与
    一阶导数都完全相等（周期恰好等于循环时长的整数分之一），拼接循环播放时不会在接缝处产生
    可闻的爆音/跳变，不需要额外做首尾交叉淡化（crossfade）处理。
    """
    cycles = max(1, round(freq_hz * duration_s))
    return cycles / duration_s


def gen_music(out: Path, seed: int, manifest: Manifest):
    """生成一段确定性、可无缝循环的占位背景音乐 loop（大三和弦琶音式 pad + 缓慢音量起伏）。

    "可无缝循环"通过 ``snap_freq_to_loop`` 让每个正弦分量与音量 LFO 的频率都恰好是
    ``1 / MUSIC_LOOP_DURATION_S`` 的整数倍达成（见该函数判断记录），而不是靠首尾交叉淡化；
    同一 ``seed`` 下多次运行产出字节完全一致（``rng_for`` 派生的扰动量在吸附前加入，
    吸附后仍是整数周期，不破坏循环性，只影响音高的细微观感）。
    """
    base = out / "music"
    duration_s = MUSIC_LOOP_DURATION_S
    n = int(SAMPLE_RATE * duration_s)
    rng = rng_for(seed, "music_loop_01")

    # 根音、三音、五音、高八度根音（C3/E3/G3/C4 附近），每个分量各自独立吸附到整数周期频率。
    chord_notes = [130.81, 164.81, 196.00, 261.63]
    amps = [0.35, 0.28, 0.22, 0.16]
    detune = rng.uniform(-0.4, 0.4)
    freqs = [snap_freq_to_loop(f + detune, duration_s) for f in chord_notes]

    # 音量 LFO：循环内起伏 2 个整周期，同样天然首尾连续。
    lfo_cycles = 2

    samples = [0.0] * n
    for i in range(n):
        t = i / SAMPLE_RATE
        lfo = 0.75 + 0.25 * math.sin(2 * math.pi * lfo_cycles * t / duration_s)
        s = 0.0
        for f, a in zip(freqs, amps):
            s += a * math.sin(2 * math.pi * f * t)
        samples[i] = s * lfo * 0.5  # 整体降幅避免多分量叠加削波

    samples = normalize_peak(samples, target_dbfs=MUSIC_PEAK_DBFS)
    p = base / "loop_01.wav"
    write_wav(p, samples)
    peak = max((abs(s) for s in samples), default=0.0)
    manifest.add(p, "wav", {
        "sample_rate": SAMPLE_RATE, "channels": 1, "sampwidth": 2,
        "duration_s": round(duration_s, 4), "peak": round(peak, 4), "loop": True,
    })


# ==========================================================================
# 7. ui/
# ==========================================================================

def gen_ui(out: Path, seed: int, manifest: Manifest):
    base = out / "ui"

    # 9-slice 面板：外边框 8px + 内部填充，方便按 8px 边距做九宫格切割
    size = (64, 64)
    img = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 63, 63], fill=(30, 34, 44, 235))
    d.rectangle([8, 8, 55, 55], fill=(52, 58, 72, 235))
    d.rectangle([0, 0, 63, 63], outline=(15, 16, 20, 255), width=1)
    p = base / "panel_9slice.png"
    save_png(img, p)
    manifest.add(p, "image", {"size": list(size), "mode": "RGBA"})

    # 按钮三态
    btn_size = (96, 32)
    btn_states = {
        "button_normal": ((70, 90, 120, 255), (30, 40, 60, 255)),
        "button_hover": ((92, 115, 150, 255), (40, 55, 80, 255)),
        "button_pressed": ((50, 65, 90, 255), (20, 28, 42, 255)),
    }
    for name, (fill, border) in btn_states.items():
        img = Image.new("RGBA", btn_size, (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.rounded_rectangle([1, 1, 94, 30], radius=8, fill=fill, outline=border, width=2)
        if name == "button_pressed":
            d.line([4, 6, 92, 6], fill=(0, 0, 0, 80), width=1)
        else:
            d.line([4, 4, 92, 4], fill=(255, 255, 255, 60), width=1)
        p = base / f"{name}.png"
        save_png(img, p)
        manifest.add(p, "image", {"size": list(btn_size), "mode": "RGBA"})

    # 血条/资源条 背景与填充
    bar_size = (128, 12)
    img = Image.new("RGBA", bar_size, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, 127, 11], radius=5, fill=(28, 28, 32, 255), outline=(10, 10, 10, 255))
    p = base / "bar_bg.png"
    save_png(img, p)
    manifest.add(p, "image", {"size": list(bar_size), "mode": "RGBA"})

    img = Image.new("RGBA", bar_size, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, 127, 11], radius=5, fill=(70, 200, 90, 255))
    p = base / "bar_fill.png"
    save_png(img, p)
    manifest.add(p, "image", {"size": list(bar_size), "mode": "RGBA"})

    # 背包格
    slot_size = (48, 48)
    img = Image.new("RGBA", slot_size, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([1, 1, 46, 46], radius=6, fill=(40, 42, 50, 220), outline=(15, 15, 18, 255), width=2)
    corner = (120, 125, 140, 255)
    for x0, y0, dx, dy in [(3, 3, 1, 1), (44, 3, -1, 1), (3, 44, 1, -1), (44, 44, -1, -1)]:
        d.line([x0, y0, x0 + 6 * dx, y0], fill=corner, width=1)
        d.line([x0, y0, x0, y0 + 6 * dy], fill=corner, width=1)
    p = base / "slot.png"
    save_png(img, p)
    manifest.add(p, "image", {"size": list(slot_size), "mode": "RGBA"})


# ==========================================================================
# 8. maps/placeholder_field
# ==========================================================================

MAP_SIZE = 1024
MAP_CELL = 32


def gen_map(out: Path, seed: int, manifest: Manifest):
    base = out / "maps" / "placeholder_field"
    rng = rng_for(seed, "map_ground")

    ground = Image.new("RGB", (MAP_SIZE, MAP_SIZE), (70, 120, 60))
    gd = ImageDraw.Draw(ground)
    base_g = (70, 120, 60)
    cells = MAP_SIZE // MAP_CELL
    for cy in range(cells):
        for cx in range(cells):
            jitter = rng.randint(-8, 8)
            col = (
                max(0, min(255, base_g[0] + jitter)),
                max(0, min(255, base_g[1] + jitter)),
                max(0, min(255, base_g[2] + jitter)),
            )
            x0, y0 = cx * MAP_CELL, cy * MAP_CELL
            gd.rectangle([x0, y0, x0 + MAP_CELL - 1, y0 + MAP_CELL - 1], fill=col)
    grid_col = (50, 90, 45)
    for cx in range(0, MAP_SIZE + 1, MAP_CELL * 2):
        gd.line([cx, 0, cx, MAP_SIZE], fill=grid_col, width=1)
    for cy in range(0, MAP_SIZE + 1, MAP_CELL * 2):
        gd.line([0, cy, MAP_SIZE, cy], fill=grid_col, width=1)

    dirt_patches = [(300, 300, 80), (700, 650, 90)]
    dirt_col = (110, 90, 60)
    for dx, dy, r in dirt_patches:
        gd.ellipse([dx - r, dy - r, dx + r, dy + r], fill=dirt_col)

    ground_p = ground.convert("P", palette=Image.ADAPTIVE, colors=48)
    p = base / "ground.png"
    save_png(ground_p, p)
    manifest.add(p, "image", {"size": [MAP_SIZE, MAP_SIZE], "mode": "P"})

    # 前景遮挡：两棵树冠色块
    tree_positions = [(180, 850, 70), (860, 200, 85)]
    overlay = Image.new("RGBA", (MAP_SIZE, MAP_SIZE), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay)
    for tx, ty, r in tree_positions:
        od.ellipse([tx - r, ty - r * 0.8, tx + r, ty + r * 0.8], fill=(30, 70, 30, 255))
        od.ellipse([tx - r * 0.7, ty - r, tx + r * 0.7, ty + r * 0.2], fill=(40, 90, 40, 255))
    p = base / "overlay.png"
    save_png(overlay, p)
    manifest.add(p, "image", {"size": [MAP_SIZE, MAP_SIZE], "mode": "RGBA"})

    # 可行走区域提示：白=可行走，黑=障碍；与 overlay 树冠 + dirt patch 对应
    nav = Image.new("L", (MAP_SIZE, MAP_SIZE), 255)
    nd = ImageDraw.Draw(nav)
    for tx, ty, r in tree_positions:
        nd.ellipse([tx - r * 0.6, ty - r * 0.6, tx + r * 0.6, ty + r * 0.6], fill=0)
    nav_1bit = nav.convert("1")
    p = base / "nav_hint.png"
    save_png(nav_1bit, p)
    manifest.add(p, "image", {"size": [MAP_SIZE, MAP_SIZE], "mode": "1"})


# ==========================================================================
# 9~10. fonts/README.md, assets/_placeholder/README.md
# ==========================================================================

FONTS_README = """# fonts/ 占位字体

本目录目前为空：字体文件需要联网下载，本任务（脚本生成占位资产包）不做下载，
待用户确认后再由人工或专门的下载步骤加入本目录。

## 候选（不写具体下载链接，仅记录选型意向）

| 候选 | 许可类型 | 来源类型 | 说明 |
|---|---|---|---|
| Noto Sans SC（思源黑体同源字重之一，Google 主导） | SIL Open Font License 1.1（开源可商用，允许嵌入分发） | Google Fonts / Adobe 开源字体项目 | 覆盖中文常用字集（GB2312/GBK 常用字）、拉丁字符与常见标点，适合 UI 与对话文本；变体字重较全，可按性能需要只引入 Regular/Bold 两档裁剪子集 |

## 加入步骤（供后续人工/下载任务参考）

1. 确认许可文本随字体文件一并归档（如 `OFL.txt`）。
2. 按需做字符子集裁剪（避免整包体积过大），裁剪后的字重与格式（ttf/otf/woff2 等）在游戏接入记录中登记。
3. 更新 `../README.md` 中的资源清单与许可说明。
"""


def build_readme_text(manifest_data: dict) -> str:
    total_mb = manifest_data["total_bytes"] / (1024 * 1024)
    return f"""# 通用占位资产包

本目录存放通用占位表现资产：色块角色、方块怪、简易特效、占位音效、开源可商用字体（待加入）。
目的是让任何新游戏在第一天就能跑起来一个可玩的灰盒竖切，不必等正式美术资产到位
（呼应 [ADR-0014](../../architecture/adr/0014-资产契约导入工具与UI套件是框架交付物.md)、
[11_工程规范与测试.md](../../architecture/11_工程规范与测试.md) 第 1、2.1 节）。

全部资源由 [`toolchain/gen_placeholder_assets.py`](../../toolchain/gen_placeholder_assets.py)
用 Pillow + Python 标准库确定性生成，不含任何外部下载素材（字体除外，见 `fonts/README.md`）。

- 正式游戏的正式资产放各自游戏仓库自己的 `assets/<game>/` 下，替换占位资产时不改变
  `display.map`（见 [`../../architecture/04_数据与内容管线.md`](../../architecture/04_数据与内容管线.md) 第 7 节）
  等表引用的逻辑 id，只替换底层资源文件。
- 资产的具体规格（分辨率、格式、命名、挂点约定等）以 `architecture/` 下相关文档
  （04 第 7 节、09 表现层第 3.2~3.4、5 节、14_资产规格书模板）为准，本文件只做资源清单与引用建议。

## 目录树

```
assets/_placeholder/
  README.md                        本文件
  MANIFEST.json                    本次生成的文件清单（路径/字节数/sha256/尺寸等）
  smoke/                           ComfyUI 冒烟记录（历史遗留，本工具不触碰）
  sprites/
    placeholder_hero/              8 方向纸娃娃角色（右 5 档直接绘制，左 3 档靠镜像回填）
      <direction>/body.png, hand_main.png, head.png   （仅 5 个已绘制方向含图片）
      anchors.json                 每方向 root/hand_main/head/overhead 像素锚点 + mirror_pairs
    placeholder_beast/              8 方向单层方块怪
      <direction>/body.png
      anchors.json
    placeholder_chest/              closed.png / open.png
    placeholder_door/               closed.png / open.png
  icons/                            5 种图标 × common/rare 两种品质配色
  vfx/
    hit_spark/  burn/  cast_circle/
      frame_00.png..frame_07.png, atlas.png, frames.json
  sfx/                               9 个短音效 .wav（44.1kHz/16bit/单声道，峰值 -12dBFS）
  music/                             loop_01.wav（4 秒确定性可无缝循环 BGM，峰值 -14dBFS）
  ui/                                九宫格面板、按钮三态、血条背景/填充、背包格
  maps/placeholder_field/           ground.png / overlay.png / nav_hint.png
  fonts/README.md                   字体候选说明（本任务不下载字体文件）
```

## 建议引用 id / 资源引用名

以下为供 `display.map`（04 第 7.1 节）与 `vfx.def`/`sfx.def`（09 第 5 节）参照的建议命名；
`sprite_set_id`/`icon_id` 是自由字符串资源引用（不受 Id 域名规则约束），`vfx_id`/`sfx_id`
是 `vfx`/`sfx` 域下的内容 Id（须符合 04 第 2 节 id 规范）。

| 资源 | 类型 | 建议引用名 |
|---|---|---|
| 色块英雄纸娃娃 | `sprite_set_id` | `sprite.creature.placeholder_hero` |
| 方块怪 | `sprite_set_id` | `sprite.creature.placeholder_beast` |
| 宝箱物件 | `sprite_set_id` | `sprite.gobj.placeholder_chest` |
| 门物件 | `sprite_set_id` | `sprite.gobj.placeholder_door` |
| 武器图标 | `icon_id` | `icon.item.placeholder_blade` |
| 药水图标 | `icon_id` | `icon.item.placeholder_tonic` |
| 代币图标 | `icon_id` | `icon.item.placeholder_token` |
| 打击技能图标 | `icon_id` | `icon.skill.placeholder_strike` |
| 灼烧技能图标 | `icon_id` | `icon.skill.placeholder_burn` |
| 命中打击特效 | `vfx_id` | `vfx.placeholder_hit_spark` |
| 灼烧循环特效 | `vfx_id` | `vfx.placeholder_burn` |
| 施法法阵特效 | `vfx_id` | `vfx.placeholder_cast_circle` |
| 打击音效（两变体） | `sfx_id` | `sfx.placeholder_hit_01`、`sfx.placeholder_hit_02` |
| 挥击音效 | `sfx_id` | `sfx.placeholder_swing_01` |
| 施法音效 | `sfx_id` | `sfx.placeholder_cast_01` |
| 死亡音效 | `sfx_id` | `sfx.placeholder_death_01` |
| UI 点击音效 | `sfx_id` | `sfx.placeholder_ui_click_01` |
| UI 开启音效 | `sfx_id` | `sfx.placeholder_ui_open_01` |
| 拾取音效 | `sfx_id` | `sfx.placeholder_pickup_01` |
| 升级音效 | `sfx_id` | `sfx.placeholder_level_up_01` |
| 循环背景音乐 | 直接按路径引用（无内容 Id） | `music/loop_01.wav` |
| 占位地图（草地场景） | 直接按路径引用（无内容 Id） | `maps/placeholder_field/{{ground,overlay,nav_hint}}.png` |
| UI 套件基础皮肤 | 直接按路径引用（无内容 Id） | `ui/{{panel_9slice,button_normal,button_hover,button_pressed,bar_bg,bar_fill,slot}}.png` |

## 重新生成命令

```
python toolchain/gen_placeholder_assets.py --out assets/_placeholder --seed 1
python toolchain/gen_placeholder_assets.py --out assets/_placeholder --check
```

同一 `--seed` 多次运行产出字节完全一致（确定性生成，不依赖系统时间/外部素材）。

## 许可

本目录全部图片/音效资源均由脚本程序化生成，不含任何第三方素材，视同 **CC0（公有领域等效）授权**，
可在任意项目中自由使用、修改、再分发，无需署名。`fonts/` 除外——字体候选见其自身 `README.md`，
待正式加入时另行登记其原始许可（预期 SIL OFL 1.1）。

## 当前生成状态

- 文件总数：{manifest_data['file_count']}
- 总体积：约 {total_mb:.2f} MB
- 详细清单见同目录 `MANIFEST.json`（每个文件的路径、字节数、sha256、尺寸/时长等元信息）
"""


# ==========================================================================
# --check 模式
# ==========================================================================

def expected_files(out: Path):
    """返回 (相对路径, 期望校验) 列表，用于 --check。"""
    items = []

    base = out / "sprites" / "placeholder_hero"
    for direction in HERO_DIRS_AUTHORED:
        for layer in ("body", "hand_main", "head"):
            items.append((base / direction / f"{layer}.png", ("image", HERO_CANVAS, "RGBA")))
    items.append((base / "anchors.json", ("json", None, None)))

    base = out / "sprites" / "placeholder_beast"
    for direction in BEAST_DIRS_ALL:
        items.append((base / direction / "body.png", ("image", BEAST_CANVAS, "RGBA")))
    items.append((base / "anchors.json", ("json", None, None)))

    base = out / "sprites" / "placeholder_chest"
    for state in ("closed", "open"):
        items.append((base / f"{state}.png", ("image", (48, 48), "RGBA")))

    base = out / "sprites" / "placeholder_door"
    for state in ("closed", "open"):
        items.append((base / f"{state}.png", ("image", (48, 64), "RGBA")))

    base = out / "icons"
    for name in ICON_DEFS:
        items.append((base / f"{name}.png", ("image", (64, 64), "RGBA")))
        items.append((base / f"{name}_rare.png", ("image", (64, 64), "RGBA")))

    base = out / "vfx"
    for name in VFX_DEFS:
        d_dir = base / name
        for i in range(VFX_FRAME_COUNT):
            items.append((d_dir / f"frame_{i:02d}.png", ("image", (VFX_FRAME_SIZE, VFX_FRAME_SIZE), "RGBA")))
        items.append((d_dir / "atlas.png", ("image", (VFX_FRAME_SIZE * VFX_FRAME_COUNT, VFX_FRAME_SIZE), "RGBA")))
        items.append((d_dir / "frames.json", ("json", None, None)))

    base = out / "sfx"
    for name in ("hit_01", "hit_02", "swing_01", "cast_01", "death_01",
                 "ui_click_01", "ui_open_01", "pickup_01", "level_up_01"):
        items.append((base / f"{name}.wav", ("wav", None, None)))

    # music/loop_01.wav：比短音效长得多（循环 BGM），wav 校验的时长上限单独放宽到
    # MUSIC_LOOP_DURATION_S 的 2 倍（留冗余，见 4 元组第 4 项 max_duration，省略则默认 0.6 秒，
    # 与既有 sfx 校验口径一致，见 run_check）。
    items.append(
        (out / "music" / "loop_01.wav", ("wav", None, None, MUSIC_LOOP_DURATION_S * 2))
    )

    base = out / "ui"
    items.append((base / "panel_9slice.png", ("image", (64, 64), "RGBA")))
    for name in ("button_normal", "button_hover", "button_pressed"):
        items.append((base / f"{name}.png", ("image", (96, 32), "RGBA")))
    items.append((base / "bar_bg.png", ("image", (128, 12), "RGBA")))
    items.append((base / "bar_fill.png", ("image", (128, 12), "RGBA")))
    items.append((base / "slot.png", ("image", (48, 48), "RGBA")))

    base = out / "maps" / "placeholder_field"
    items.append((base / "ground.png", ("image", (MAP_SIZE, MAP_SIZE), None)))
    items.append((base / "overlay.png", ("image", (MAP_SIZE, MAP_SIZE), "RGBA")))
    items.append((base / "nav_hint.png", ("image", (MAP_SIZE, MAP_SIZE), None)))

    items.append((out / "fonts" / "README.md", ("text", None, None)))
    items.append((out / "README.md", ("text", None, None)))
    items.append((out / "MANIFEST.json", ("json", None, None)))

    return items


def run_check(out: Path) -> int:
    items = expected_files(out)
    ok = 0
    fail = 0
    for path, spec in items:
        kind = spec[0]
        if not path.exists():
            print(f"[MISSING] {path}")
            fail += 1
            continue
        try:
            if kind == "image":
                _, expect_size, expect_mode = spec
                with Image.open(path) as im:
                    im.load()
                    if expect_size is not None and im.size != tuple(expect_size):
                        print(f"[BAD SIZE] {path} got {im.size} want {expect_size}")
                        fail += 1
                        continue
                    if expect_mode is not None and im.mode != expect_mode:
                        print(f"[BAD MODE] {path} got {im.mode} want {expect_mode}")
                        fail += 1
                        continue
            elif kind == "wav":
                # spec 第 4 项（可选）覆盖时长上限，默认 0.6 秒（短音效口径）；music/loop_01.wav
                # 传了更宽的上限，见 expected_files。
                max_duration = spec[3] if len(spec) > 3 else 0.6
                with wave.open(str(path), "rb") as wf:
                    if wf.getframerate() != SAMPLE_RATE or wf.getnchannels() != 1 or wf.getsampwidth() != 2:
                        print(f"[BAD WAV PARAMS] {path}")
                        fail += 1
                        continue
                    dur = wf.getnframes() / wf.getframerate()
                    if dur <= 0 or dur > max_duration:
                        print(f"[BAD WAV DURATION] {path} duration={dur:.3f}s (max {max_duration:.3f}s)")
                        fail += 1
                        continue
            elif kind == "json":
                json.loads(path.read_text(encoding="utf-8"))
            elif kind == "text":
                if path.stat().st_size == 0:
                    print(f"[EMPTY] {path}")
                    fail += 1
                    continue
            ok += 1
        except Exception as e:  # noqa: BLE001
            print(f"[ERROR] {path}: {e}")
            fail += 1

    print(f"--check 完成：通过 {ok} / {len(items)}，失败 {fail}")
    return 0 if fail == 0 else 1


# ==========================================================================
# 主流程
# ==========================================================================

def clean_manifest_files(out: Path) -> int:
    """--clean：仅删除本脚本当前版本清单（``expected_files``）内、且已存在于 ``out`` 下的文件。

    加固约束：本函数只对 ``expected_files(out)`` 枚举出的具体文件路径调用
    ``Path.unlink()``——即本次生成即将写出的那批文件本身；不删除任何目录，不触碰清单之外的
    任何文件（旧命名残留目录、``smoke/`` 等其他子目录一律不受影响）。用于在改动生成逻辑
    （如目录/文件名变化）后避免残留旧版同名文件与新内容混杂；跨命名版本的历史遗留目录（例如
    改名前的方向档位目录）不在本函数职责内，需按变更说明单独手工确认后删除。
    """
    removed = 0
    for path, _spec in expected_files(out):
        if path.is_file():
            path.unlink()
            removed += 1
    return removed


def run_generate(out: Path, seed: int, clean: bool = False) -> int:
    out.mkdir(parents=True, exist_ok=True)
    if clean:
        removed = clean_manifest_files(out)
        print(f"--clean：已删除清单内已存在文件 {removed} 个（不含目录、不含清单外文件）")
    manifest = Manifest()

    gen_hero(out, seed, manifest)
    gen_beast(out, seed, manifest)
    gen_chest(out, seed, manifest)
    gen_door(out, seed, manifest)
    gen_icons(out, seed, manifest)
    gen_vfx(out, seed, manifest)
    gen_sfx(out, seed, manifest)
    gen_music(out, seed, manifest)
    gen_ui(out, seed, manifest)
    gen_map(out, seed, manifest)

    fonts_dir = out / "fonts"
    fonts_dir.mkdir(parents=True, exist_ok=True)
    # 判断记录：若本目录已经有真实字体文件落地（联网下载 + 用户确认后手工加入，见该目录
    # README.md 自己的记录），说明 FONTS_README 这份"待下载占位说明"已过期——不能无条件覆盖，
    # 否则会用本脚本内置的旧占位文案冲掉已手工维护好的真实字体信息（曾经出的一次真实回归）。
    # 只有目录下还没有任何真实字体文件时才写占位说明；已有真实字体文件时保留其现有 README.md
    # 原样不动（不在 manifest 清单里重复登记它——manifest 只登记本脚本自己生成/接管的文件）。
    has_real_font = any(fonts_dir.glob("*.otf")) or any(fonts_dir.glob("*.ttf"))
    if not has_real_font:
        (fonts_dir / "README.md").write_text(FONTS_README, encoding="utf-8")
        manifest.add(fonts_dir / "README.md", "text", {})

    # 先写一次 manifest 拿到统计数据用于 README，再补写 README 自身条目
    manifest_data = manifest.write(out, seed)
    readme_text = build_readme_text(manifest_data)
    (out / "README.md").write_text(readme_text, encoding="utf-8")
    manifest.add(out / "README.md", "text", {})
    manifest_data = manifest.write(out, seed)  # 覆盖，纳入 README.md 自身条目

    total_mb = manifest_data["total_bytes"] / (1024 * 1024)
    print(f"生成完成：{manifest_data['file_count']} 个文件，约 {total_mb:.2f} MB，输出目录 {out}")
    print(f"清单：{(out / 'MANIFEST.json').as_posix()}")
    return 0


def main():
    # Windows 控制台默认代码页通常不是 UTF-8，本文件打印的说明/错误消息（如
    # "--check 完成：通过 N / M"）会因此乱码甚至 UnicodeEncodeError 崩溃；入口最先调用
    # toolchain/_console.py 的 ensure_utf8_stdio()，与 toolchain/validate_data.py、
    # toolchain/gen_event_constants.py、toolchain/asset_import 包保持一致（此前 ae3f667
    # 曾在此内联同一段 reconfigure 循环，现收敛为共用入口，去重）。
    ensure_utf8_stdio()

    parser = argparse.ArgumentParser(description="生成/校验框架级通用占位资产包")
    parser.add_argument("--out", default="assets/_placeholder", help="输出目录（默认 assets/_placeholder）")
    parser.add_argument("--seed", type=int, default=1, help="确定性种子（默认 1）")
    parser.add_argument("--check", action="store_true", help="只校验已生成文件是否齐全、尺寸/参数是否正确")
    parser.add_argument(
        "--clean",
        action="store_true",
        help="生成前先删除清单（expected_files）内已存在的文件，仅限清单内文件，不删除目录/清单外文件",
    )
    args = parser.parse_args()

    out = Path(args.out)
    if args.check:
        return run_check(out)
    return run_generate(out, args.seed, clean=args.clean)


if __name__ == "__main__":
    sys.exit(main())
