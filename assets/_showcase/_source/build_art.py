#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""演示场景美术后处理：把本地原始产物（ComfyUI 出的绿幕视频帧与静态图）整理成入库用的序列帧图集、精灵集锚点、场景贴图与特效序列。

    python build_art.py chars  --raw-dir <本地原始目录> [--char hero,grunt,brute] [--preview <目录>]
    python build_art.py static --raw-dir <本地原始目录>
    python build_art.py all    --raw-dir <本地原始目录>

输出写到 assets/_showcase/（sprites / sprite_anim / vfx / icons）；动画集数据行由 build_data.py 生成。门禁不跑本脚本，入库的是它的产物。
后处理全部是确定性的像素运算（绿幕抠像去溢色、按运动能量找关键帧、循环对齐、重排时间轴、缩放入画布、打包），不引入任何其它生成。
"""
from __future__ import annotations

import argparse
import glob
import json
import math
import os
import shutil
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import showcase_spec as S  # noqa: E402
import showcase_poses as P  # noqa: E402

OUT = HERE.parent  # assets/_showcase
SRC_FILL = 0.52     # gen_art.green_start 里角色高度占 640 画布的比例
SRC_SIZE = 640
SRC_FOOT = (SRC_SIZE / 2, SRC_SIZE * 0.80)
FPS = 20
PPU = 150           # 全部角色统一的像素/世界单位（英雄 140 像素 ≈ 0.93 单位高）
FOOT_FRAC = 0.88    # 脚底在画布里的纵向位置
SLOTS = {"front": "front", "side": "side_r", "back": "back"}
PIN_X_POSES = {"idle", "idle_combat", "run", "sprint", "combo1", "combo2", "combo3", "slam", "charge", "dodge", "swing"}


# ───────── 抠像 ─────────

def key_green(rgb: np.ndarray) -> np.ndarray:
    """纯绿背景抠像：按到纯绿的距离估 alpha，只在边缘一圈内做去溢色（角色自身的绿色皮肤不动）。返回 RGBA uint8。"""
    f = rgb.astype(np.float32)
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    dist = np.sqrt(r * r + (255.0 - g) ** 2 + b * b)
    a = np.clip((dist - 55.0) / (125.0 - 55.0), 0.0, 1.0)
    # 去掉与背景连通的"孤立绿渣"：只保留最大连通的前景块与其附近的小块（武器光效/披风飘起）
    solid = a > 0.5
    solid = ndimage.binary_opening(solid, structure=np.ones((3, 3)))
    lab, n = ndimage.label(solid)
    if n > 1:
        sizes = ndimage.sum(solid, lab, range(1, n + 1))
        keep = np.zeros(n + 1, dtype=bool)
        keep[1:] = sizes >= max(60, sizes.max() * 0.004)
        solid = keep[lab]
    near = ndimage.binary_dilation(solid, structure=np.ones((3, 3)), iterations=3)
    a = np.where(near, a, 0.0)
    # 边缘带：alpha<1 且邻近透明的像素，把多出来的绿色压回红蓝的水平（去溢色）
    edge = (a < 0.999) & near
    edge = ndimage.binary_dilation(edge, structure=np.ones((3, 3)), iterations=1) & (a > 0)
    cap = np.maximum(r, b) + 6.0
    g2 = np.where(edge, np.minimum(g, cap), g)
    out = np.stack([r, g2, b, a * 255.0], axis=-1)
    return np.clip(out + 0.5, 0, 255).astype(np.uint8)


# ───────── 度量 ─────────

def small_mask(rgba: np.ndarray, size: int = 160) -> np.ndarray:
    im = Image.fromarray(rgba[..., 3]).resize((size, size), Image.BILINEAR)
    return np.asarray(im) > 110


def anchor_of(rgba: np.ndarray) -> tuple[float, float]:
    """身体锚点：开运算去掉细长的剑/披风后，取最低点与最低 1/3 区域的横向质心（原图 640 坐标）。"""
    m = rgba[..., 3] > 128
    core = ndimage.binary_opening(m, structure=np.ones((11, 11)))
    if core.sum() < 200:
        core = m
    ys, xs = np.nonzero(core)
    if len(ys) == 0:
        return SRC_FOOT
    bottom = float(ys.max())
    top = float(ys.min())
    band = ys >= bottom - max(8.0, (bottom - top) * 0.33)
    return float(xs[band].mean()), bottom


def smooth(a: np.ndarray, w: int) -> np.ndarray:
    if w <= 1 or len(a) < 3:
        return a
    k = np.ones(w) / w
    pad = w // 2
    p = np.pad(a, (pad, pad), mode="edge")
    return np.convolve(p, k, mode="valid")[: len(a)]


def energies(masks: list[np.ndarray]) -> np.ndarray:
    e = np.zeros(len(masks), dtype=np.float32)
    for i in range(1, len(masks)):
        e[i] = float(np.logical_xor(masks[i], masks[i - 1]).sum())
    e[0] = e[1] if len(e) > 1 else 0
    return smooth(e, 3)


def frame_distance(a: np.ndarray, b: np.ndarray) -> float:
    return float(np.abs(a - b).mean())


def small_rgb(rgba: np.ndarray, size: int = 64) -> np.ndarray:
    im = Image.fromarray(rgba).resize((size, size), Image.BILINEAR)
    arr = np.asarray(im).astype(np.float32)
    return arr[..., :3] * (arr[..., 3:4] / 255.0)


# ───────── 取帧规划 ─────────

def plan_loop(frames_small: list[np.ndarray], n_out: int, lo: int, hi: int) -> list[float]:
    """循环剪辑：找首尾最接近的一段 [i, j)，按帧数均匀取样。"""
    n = len(frames_small)
    best = None
    for i in range(0, max(1, n // 3)):
        for j in range(i + lo, min(n, i + hi + 1)):
            d = frame_distance(frames_small[i], frames_small[j]) / (1.0 + 0.002 * (j - i))
            if best is None or d < best[0]:
                best = (d, i, j)
    if best is None:
        i, j = 0, n - 1
    else:
        _, i, j = best
    return [i + (j - i) * k / n_out for k in range(n_out)]


def plan_attack(e: np.ndarray, n_total: int, n_out: int, strike_idx: int) -> list[float]:
    n_src = len(e)
    s_raw = int(np.argmax(e[2: n_src - 2])) + 2
    peak = e[s_raw]
    a0 = s_raw
    while a0 > 0 and e[a0] > 0.22 * peak:
        a0 -= 1
    a0 = min(a0, max(0, s_raw - 3))
    a0 = max(a0, s_raw - 12)
    end = n_src - 1
    while end > s_raw + 2 and e[end] < 0.10 * peak:
        end -= 1
    end = min(n_src - 1, end + 2)
    out: list[float] = []
    for k in range(n_out):
        if k < strike_idx:
            out.append(a0 + (s_raw - a0) * k / max(1, strike_idx))
        else:
            out.append(s_raw + (end - s_raw) * (k - strike_idx) / max(1, n_out - 1 - strike_idx))
    return out


def plan_react(e: np.ndarray, n_out: int) -> list[float]:
    n_src = len(e)
    peak = float(e.max()) if e.max() > 0 else 1.0
    a0 = 0
    while a0 < n_src - 2 and e[a0 + 1] < 0.20 * peak:
        a0 += 1
    end = n_src - 1
    while end > a0 + 3 and e[end] < 0.07 * peak:
        end -= 1
    end = min(n_src - 1, end + 3)
    return [a0 + (end - a0) * k / max(1, n_out - 1) for k in range(n_out)]


# ───────── 角色 ─────────

def load_raw(raw: Path, char: str, view: str, key: str) -> list[np.ndarray]:
    files = sorted(glob.glob(str(raw / char / view / key / "f*.png")))
    return [key_green(np.asarray(Image.open(f).convert("RGB"))) for f in files]


def compose_frame(rgba: np.ndarray, anchor: tuple[float, float], scale: float, canvas: int) -> Image.Image:
    img = Image.fromarray(rgba)
    w = max(1, round(img.width * scale))
    h = max(1, round(img.height * scale))
    img = img.resize((w, h), Image.LANCZOS)
    out = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    ox = round(canvas / 2 - anchor[0] * scale)
    oy = round(canvas * FOOT_FRAC - anchor[1] * scale)
    out.alpha_composite(img, (ox, oy)) if (0 <= ox and 0 <= oy and ox + w <= canvas and oy + h <= canvas) else paste_clipped(out, img, ox, oy)
    return out


def paste_clipped(dst: Image.Image, src: Image.Image, ox: int, oy: int) -> None:
    sx0, sy0 = max(0, -ox), max(0, -oy)
    dx0, dy0 = max(0, ox), max(0, oy)
    w = min(src.width - sx0, dst.width - dx0)
    h = min(src.height - sy0, dst.height - dy0)
    if w <= 0 or h <= 0:
        return
    dst.alpha_composite(src.crop((sx0, sy0, sx0 + w, sy0 + h)), (dx0, dy0))


def quantize(im: Image.Image, colors: int = 192) -> Image.Image:
    """颜色量化到 colors 色后仍存成 RGBA PNG：色数少让 deflate 压得更小，但文件必须是 RGBA(6)——框架资源加载器的后台解码只认 RGB/RGBA，
    调色板 PNG（颜色类型 3）会回退到主线程解码、有长帧风险。"""
    q = im.quantize(colors=colors, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
    return q.convert("RGBA")


def write_atlas(frames: list[Image.Image], durations: list[float], loop: bool, out_dir: Path, do_quantize: bool = True) -> int:
    fw, fh = frames[0].size
    atlas = Image.new("RGBA", (fw * len(frames), fh), (0, 0, 0, 0))
    rows = []
    for i, f in enumerate(frames):
        atlas.paste(f, (i * fw, 0))
        rows.append({"index": i, "x": i * fw, "y": 0, "w": fw, "h": fh, "duration": round(durations[i], 4)})
    out_dir.mkdir(parents=True, exist_ok=True)
    save = quantize(atlas) if do_quantize else atlas
    save.save(out_dir / "atlas.png", optimize=True)
    doc = {"frame_w": fw, "frame_h": fh, "fps": FPS, "frame_duration": round(1.0 / FPS, 4), "loop": loop, "frames": rows}
    (out_dir / "frames.json").write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8", newline="\n")
    return (out_dir / "atlas.png").stat().st_size


def build_char(raw: Path, char: str, preview: Path | None) -> dict:
    spec = S.CHARACTERS[char]
    canvas = spec["canvas"]
    scale = spec["height_px"] / (SRC_SIZE * SRC_FILL)
    poses = P.POSES[char]
    clips = P.clip_table(char)  # name -> dict(pose, ms, loop, strike)
    cache: dict[tuple[str, str], dict] = {}
    total = 0
    for view, slot in SLOTS.items():
        for cname, c in clips.items():
            pose = c["pose"]
            src_view = view
            frames = load_raw(raw, char, src_view, pose) if (raw / char / src_view / pose / "f000.png").exists() else None
            if frames is None:
                # 没有该视图的片段：回退到侧视图（敌人只做了必要视图的受击等）
                frames = load_raw(raw, char, "side", pose) if (raw / char / "side" / pose / "f000.png").exists() else None
            if frames is None:
                continue
            key = (src_view if (raw / char / src_view / pose / "f000.png").exists() else "side", pose)
            if key not in cache:
                masks = [small_mask(f) for f in frames]
                anc = np.array([anchor_of(f) for f in frames], dtype=np.float32)
                smalls = [small_rgb(f) for f in frames]
                cache[key] = {"frames": frames, "e": energies(masks), "anc": anc, "smalls": smalls}
            d = cache[key]
            n_out = max(1, round(c["ms"] / (1000.0 / FPS)))
            kind = c["kind"]
            if kind == "loop":
                idx = plan_loop(d["smalls"], n_out, lo=P.LOOP_RANGE[pose][0], hi=P.LOOP_RANGE[pose][1])
            elif kind == "attack":
                idx = plan_attack(d["e"], len(d["frames"]), n_out, max(1, round(c["strike"] * n_out)))
            else:
                idx = plan_react(d["e"], n_out)
            pin = pose in PIN_X_POSES
            anc = d["anc"].copy()
            if pin:
                ax = smooth(anc[:, 0], 5)
                ay = np.full(len(anc), float(np.median(anc[:, 1])))
                anc = np.stack([ax, ay], axis=1)
            else:
                anc = np.tile(np.array(SRC_FOOT, dtype=np.float32), (len(anc), 1))
            out_frames = []
            for t in idx:
                i0 = int(math.floor(t + 1e-6))
                i0 = max(0, min(len(d["frames"]) - 1, i0))
                out_frames.append(compose_frame(d["frames"][i0], (float(anc[i0][0]), float(anc[i0][1])), scale, canvas))
            dur = [1.0 / FPS] * len(out_frames)
            total += write_atlas(out_frames, dur, c["loop"], OUT / "sprite_anim" / f"show_{char}_{cname}__{slot}")
            if preview is not None:
                preview.mkdir(parents=True, exist_ok=True)
                sheet = Image.new("RGBA", (canvas // 2 * len(out_frames), canvas // 2), (70, 74, 84, 255))
                for i, f in enumerate(out_frames):
                    sheet.alpha_composite(f.resize((canvas // 2, canvas // 2), Image.LANCZOS), (i * canvas // 2, 0))
                sheet.convert("RGB").save(preview / f"{char}_{view}_{cname}.png")
    # 静态精灵集：每个方向取待机第 0 帧
    sdir = OUT / "sprites" / f"creature_show_{char}"
    for view, slot in SLOTS.items():
        a = OUT / "sprite_anim" / f"show_{char}_{P.static_clip(char)}__{slot}"
        if not (a / "atlas.png").exists():
            continue
        fr = json.loads((a / "frames.json").read_text(encoding="utf-8"))["frames"][0]
        im = Image.open(a / "atlas.png").convert("RGBA").crop((fr["x"], fr["y"], fr["x"] + fr["w"], fr["y"] + fr["h"]))
        (sdir / slot).mkdir(parents=True, exist_ok=True)
        quantize(im).save(sdir / slot / "body.png", optimize=True)
    root = [canvas / 2, round(canvas * FOOT_FRAC, 1)]
    dirs = {}
    for name in ("front", "front_side_r", "side_r", "back_side_r", "back", "front_side_l", "side_l", "back_side_l"):
        dirs[name] = {"root": root, "overhead": [canvas / 2, round(canvas * FOOT_FRAC - spec["height_px"] - 4, 1)]}
    anchors = {
        "canvas": {"width": canvas, "height": canvas},
        "pixels_per_unit": PPU,
        "direction_count": 8,
        "authored_directions": list(SLOTS.values()),
        "layers": ["body"],
        "directions": dirs,
    }
    sdir.mkdir(parents=True, exist_ok=True)
    (sdir / "anchors.json").write_text(json.dumps(anchors, indent=2) + "\n", encoding="utf-8", newline="\n")
    return {"bytes": total}


# ───────── 静态图与特效 ─────────

def trim(im: Image.Image, pad: int = 2) -> Image.Image:
    a = im.getchannel("A").point(lambda v: 255 if v > 12 else 0)
    bb = a.getbbox()
    if bb is None:
        return im
    return im.crop((max(0, bb[0] - pad), max(0, bb[1] - pad), min(im.width, bb[2] + pad), min(im.height, bb[3] + pad)))


def make_tileable(im: Image.Image, size: int = 256) -> Image.Image:
    """把一张地面图做成可平铺：缩到 size，十字偏移后用羽化的十字带混合接缝（不引入新内容，只是把中心区域盖到边缘）。"""
    base = im.convert("RGB").resize((size, size), Image.LANCZOS)
    arr = np.asarray(base).astype(np.float32)
    shifted = np.roll(arr, (size // 2, size // 2), axis=(0, 1))
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    # 权重：原图在中心高、在边缘低；偏移图相反
    wx = 1.0 - np.abs(xx - size / 2) / (size / 2)
    wy = 1.0 - np.abs(yy - size / 2) / (size / 2)
    w = np.clip(np.minimum(wx, wy) * 2.0, 0.0, 1.0)[..., None]
    w = w * w * (3 - 2 * w)
    out = arr * w + shifted * (1 - w)
    return Image.fromarray(np.clip(out + 0.5, 0, 255).astype(np.uint8))


def save_png(im: Image.Image, path: Path, quant: bool = True) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    (quantize(im.convert("RGBA")) if quant else im).save(path, optimize=True)


def vfx_slash(src: Image.Image, out_dir: Path) -> int:
    im = trim(src.convert("RGBA"))
    a = np.asarray(im.getchannel("A")) > 60
    ys, xs = np.nonzero(a)
    pts = np.stack([xs, ys], axis=1).astype(np.float32)
    # 弦：最远两点；弧的鼓出方向 = 质心相对弦中点
    sub = pts[:: max(1, len(pts) // 600)]
    d = ((sub[:, None, :] - sub[None, :, :]) ** 2).sum(-1)
    i, j = np.unravel_index(np.argmax(d), d.shape)
    p, q = sub[i], sub[j]
    mid = (p + q) / 2
    cen = pts.mean(0)
    bulge = cen - mid
    ang = math.degrees(math.atan2(-bulge[1], bulge[0]))  # 图像 y 向下，取数学角
    rot = im.rotate(-(0 - ang) if False else ang, resample=Image.BICUBIC, expand=True)  # 旋转后鼓出方向朝 +x（向右）
    rot = trim(rot)
    size = 256
    scale = min(size * 0.9 / rot.width, size * 0.95 / rot.height)
    rot = rot.resize((max(1, round(rot.width * scale)), max(1, round(rot.height * scale))), Image.LANCZOS)
    frames = []
    n = 7
    arr = np.asarray(rot).astype(np.float32)
    h, w = arr.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    # 沿弧参数 t：以弧心（鼓出反方向很远处）的极角排序；这里用 y 作为扫掠方向近似（弧大体沿纵向展开）
    t = (yy - 0) / max(1.0, h - 1)
    for k in range(n):
        head = (k + 1) / 4.0                     # 扫掠头位置 0..1.75
        tail = max(0.0, (k - 2) / 4.0)           # 拖尾消散
        vis = ((t <= head) & (t >= tail - 0.0001)).astype(np.float32)
        soft = np.clip((head - t) * 6.0, 0, 1) * np.clip((t - tail) * 6.0 + (1 if tail <= 0 else 0), 0, 1)
        alpha = arr[..., 3] * soft * (1.0 if k < 5 else 1.0 - (k - 4) * 0.45)
        fr = arr.copy()
        fr[..., 3] = np.clip(alpha, 0, 255)
        canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        canvas.alpha_composite(Image.fromarray(fr.astype(np.uint8)), ((size - w) // 2, (size - h) // 2))
        frames.append(canvas)
    return write_vfx(frames, [0.03] * len(frames), False, out_dir, root=[size * 0.2, size / 2], ppu=170)


def write_vfx(frames: list[Image.Image], durations: list[float], loop: bool, out_dir: Path, root: list[float], ppu: float) -> int:
    fw, fh = frames[0].size
    atlas = Image.new("RGBA", (fw * len(frames), fh), (0, 0, 0, 0))
    rows = []
    for i, f in enumerate(frames):
        atlas.paste(f, (i * fw, 0))
        rows.append({"index": i, "x": i * fw, "y": 0, "w": fw, "h": fh, "duration": round(durations[i], 4)})
    out_dir.mkdir(parents=True, exist_ok=True)
    quantize(atlas, 224).save(out_dir / "atlas.png", optimize=True)
    doc = {"frame_w": fw, "frame_h": fh, "fps": FPS, "frame_duration": 0.05, "loop": loop, "pixels_per_unit": ppu, "root": root, "frames": rows}
    (out_dir / "frames.json").write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8", newline="\n")
    return (out_dir / "atlas.png").stat().st_size


def vfx_spark(src: Image.Image, out_dir: Path) -> int:
    base = trim(src.convert("RGBA"))
    size = 192
    frames = []
    scales = [0.35, 0.75, 1.0, 1.08, 1.12, 1.15]
    alphas = [1.0, 1.0, 0.95, 0.75, 0.45, 0.18]
    rots = [0, 8, 14, 20, 26, 30]
    for s, al, r in zip(scales, alphas, rots):
        sc = size * 0.92 / max(base.size) * s
        im = base.resize((max(1, round(base.width * sc)), max(1, round(base.height * sc))), Image.LANCZOS).rotate(r, resample=Image.BICUBIC, expand=True)
        arr = np.asarray(im).copy()
        arr[..., 3] = (arr[..., 3] * al).astype(np.uint8)
        canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        im = Image.fromarray(arr)
        canvas.alpha_composite(im, ((size - im.width) // 2, (size - im.height) // 2)) if im.width <= size and im.height <= size else paste_clipped(canvas, im, (size - im.width) // 2, (size - im.height) // 2)
        frames.append(canvas)
    return write_vfx(frames, [0.05] * len(frames), False, out_dir, root=[size / 2, size / 2], ppu=190)


def vfx_dust(src: Image.Image, out_dir: Path) -> int:
    base = trim(src.convert("RGBA"))
    size = 192
    frames = []
    n = 8
    for k in range(n):
        u = k / (n - 1)
        s = 0.45 + 0.75 * (1 - (1 - u) ** 2)
        al = (1.0 if u < 0.25 else 1.0 - (u - 0.25) / 0.75) * 0.9
        sc = size * 0.7 / max(base.size) * s
        im = base.resize((max(1, round(base.width * sc)), max(1, round(base.height * sc))), Image.LANCZOS)
        arr = np.asarray(im).copy()
        arr[..., 3] = (arr[..., 3] * al).astype(np.uint8)
        im = Image.fromarray(arr)
        canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        y = size - 12 - im.height - int(14 * u)
        paste_clipped(canvas, im, (size - im.width) // 2, y)
        frames.append(canvas)
    return write_vfx(frames, [0.06] * n, False, out_dir, root=[size / 2, size - 12], ppu=190)


def vfx_ring(src: Image.Image, out_dir: Path) -> int:
    base = trim(src.convert("RGBA"))
    size = 256
    frames = []
    n = 6
    for k in range(n):
        u = k / (n - 1)
        s = 0.3 + 1.1 * (1 - (1 - u) ** 2.2)
        al = 1.0 if u < 0.35 else max(0.0, 1.0 - (u - 0.35) / 0.65)
        sc = size * 0.55 / max(base.size) * s
        im = base.resize((max(1, round(base.width * sc)), max(1, round(base.height * sc))), Image.LANCZOS)
        im = im.resize((im.width, max(1, round(im.height * 0.62))), Image.LANCZOS)  # 压成地面椭圆（俯视斜 40 度）
        arr = np.asarray(im).copy()
        arr[..., 3] = (arr[..., 3] * al).astype(np.uint8)
        im = Image.fromarray(arr)
        canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        paste_clipped(canvas, im, (size - im.width) // 2, (size - im.height) // 2)
        frames.append(canvas)
    return write_vfx(frames, [0.05] * n, False, out_dir, root=[size / 2, size / 2], ppu=170)


def build_static(raw: Path) -> int:
    st = raw / "static"
    total = 0

    def load(name: str) -> Image.Image:
        return Image.open(st / (name + ".png")).convert("RGBA")

    for name in ("floor_stone", "floor_stone2", "floor_dirt"):
        if (st / (name + ".png")).exists():
            save_png(make_tileable(load(name)), OUT / "icons" / "show" / (name + ".png"), quant=False)
            total += (OUT / "icons" / "show" / (name + ".png")).stat().st_size
    for name, height in (("prop_pillar", 300), ("prop_crate", 160), ("prop_barrel", 150), ("prop_brazier", 180), ("prop_rocks", 200), ("prop_dummy", 190)):
        if (st / (name + ".png")).exists():
            im = trim(load(name))
            sc = height / im.height
            im = im.resize((max(1, round(im.width * sc)), height), Image.LANCZOS)
            save_png(im, OUT / "icons" / "show" / (name + ".png"))
            total += (OUT / "icons" / "show" / (name + ".png")).stat().st_size
    for name, fn in (("fx_slash", vfx_slash), ("fx_spark", vfx_spark), ("fx_dust", vfx_dust), ("fx_ring", vfx_ring)):
        if (st / (name + ".png")).exists():
            total += fn(load(name), OUT / "vfx" / ("show_" + name.split("_", 1)[1]))
    # 训练木桩精灵集（单图：五个方向共用，镜像表把其余方向都指向它）
    if (st / "prop_dummy.png").exists():
        im = trim(load("prop_dummy"))
        canvas = 192
        sc = 150 / im.height
        im = im.resize((max(1, round(im.width * sc)), 150), Image.LANCZOS)
        c = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
        c.alpha_composite(im, ((canvas - im.width) // 2, round(canvas * FOOT_FRAC) - 150))
        sdir = OUT / "sprites" / "creature_show_dummy"
        (sdir / "front").mkdir(parents=True, exist_ok=True)
        quantize(c).save(sdir / "front" / "body.png", optimize=True)
        root = [canvas / 2, round(canvas * FOOT_FRAC, 1)]
        dirs = {n: {"root": root, "overhead": [canvas / 2, round(canvas * FOOT_FRAC - 154, 1)]} for n in ("front", "front_side_r", "side_r", "back_side_r", "back", "front_side_l", "side_l", "back_side_l")}
        (sdir / "anchors.json").write_text(json.dumps({"canvas": {"width": canvas, "height": canvas}, "pixels_per_unit": PPU, "direction_count": 8, "authored_directions": ["front"], "layers": ["body"], "directions": dirs}, indent=2) + "\n", encoding="utf-8", newline="\n")
        write_atlas([c], [0.2], True, OUT / "sprite_anim" / "show_dummy_idle__front")
    return total


def make_base_dirs() -> int:
    """资源引用 sprite_anim.<名> 要求 <名>/atlas.png + frames.json 存在（导入器核对与加载器的无方向入口）；方向变体在 <名>__<方向>/。
    判断记录：站着不动的靶子（逻辑里没有朝向变化）运行时一直取无方向入口，所以它必须是完整动画，不能只放首帧
    （只放首帧时小怪的待机、受击、击倒、起身全是静止图）。无方向入口取侧面视图（没有侧面取正面）的逐字节拷贝：
    与方向变体内容相同，版本库里是同一个对象，不额外占库体积。"""
    import shutil

    root = OUT / "sprite_anim"
    made = 0
    for d in sorted(root.glob("*__*")):
        name, _, view = d.name.rpartition("__")
        if view not in ("side_r", "front") or (view == "front" and (root / f"{name}__side_r").exists()):
            continue
        base = root / name
        base.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(d / "atlas.png", base / "atlas.png")
        shutil.copyfile(d / "frames.json", base / "frames.json")
        made += 1
    return made


def derive_grunt_floor_clips(raw: Path) -> None:
    """判断记录：小怪的"击倒/起身"视频每次都在画面里多长出第二只小怪（模型把"摔出去"画成了分身），换提示词与种子仍复现；
    所以小怪的击倒取"死亡"片段的倒地过程，起身取它的倒放（确定性派生，不再依赖视频模型的这两段）。"""
    import shutil

    for view in ("front", "side", "back"):
        src = raw / "grunt" / view / "death"
        frames = sorted(src.glob("f*.png"))
        if not frames:
            continue
        for key, order in (("knockdown", frames), ("getup", list(reversed(frames)))):
            dst = raw / "grunt" / view / key
            if dst.exists():
                shutil.rmtree(dst)
            dst.mkdir(parents=True)
            for i, f in enumerate(order):
                shutil.copyfile(f, dst / f"f{i:03d}.png")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("cmd", choices=["chars", "static", "all"])
    ap.add_argument("--raw-dir", required=True)
    ap.add_argument("--char", default="hero,grunt,brute")
    ap.add_argument("--preview", default=None)
    args = ap.parse_args()
    raw = Path(args.raw_dir)
    if args.cmd in ("chars", "all"):
        for ch in args.char.split(","):
            if ch == "grunt":
                derive_grunt_floor_clips(raw)
            r = build_char(raw, ch, Path(args.preview) if args.preview else None)
            print(ch, "atlas bytes", r["bytes"])
    if args.cmd in ("static", "all"):
        print("static bytes", build_static(raw))
    print("base dirs", make_base_dirs())
    return 0


if __name__ == "__main__":
    sys.exit(main())
