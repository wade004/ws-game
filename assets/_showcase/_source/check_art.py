#!/usr/bin/env python3
"""演示场景美术资产检查：每个帧图集的每一帧，主体（不透明连通域）必须只有一个。

规则：对每一帧取 alpha > ALPHA_T 的像素做 8 连通标记，面积最大的连通域是「主体」。
- 帧不得为空。
- 其它连通域（掉落的武器、剑光碎片等合法分离部件）面积不得超过主体的 SECOND_RATIO；
  超过即视为同一帧里叠了第二个角色（两个地精叠在一起时第二块与主体同量级）。
- 另设上限：主体面积不得超过 MAX_BODY_RATIO 倍帧内同片段中位主体面积（防止异常放大/叠图）。

用法：python check_art.py            检查 sprite_anim 下全部片段（含方向变体），有违规则退出码 1
      python check_art.py --list     逐片段打印统计
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent / "sprite_anim"
ALPHA_T = 24
SECOND_RATIO = 0.25
MAX_BODY_RATIO = 3.0
STRUCT = np.ones((3, 3), dtype=int)


def check_clip(d: Path) -> tuple[list[str], int]:
    meta = json.loads((d / "frames.json").read_text(encoding="utf-8"))
    atlas = np.array(Image.open(d / "atlas.png").convert("RGBA"))
    problems: list[str] = []
    mains: list[tuple[int, float]] = []
    for f in meta["frames"]:
        a = atlas[f["y"]:f["y"] + f["h"], f["x"]:f["x"] + f["w"], 3] > ALPHA_T
        lab, n = ndimage.label(a, structure=STRUCT)
        if n == 0:
            problems.append(f"{d.name}#{f['index']}: 空帧")
            continue
        areas = sorted((float(x) for x in ndimage.sum(a, lab, index=range(1, n + 1))), reverse=True)
        mains.append((f["index"], areas[0]))
        if len(areas) > 1 and areas[1] > SECOND_RATIO * areas[0]:
            problems.append(f"{d.name}#{f['index']}: 第二连通域 {int(areas[1])} 超过主体 {int(areas[0])} 的 {SECOND_RATIO:.0%}（疑似两个角色叠在一帧）")
    if mains:
        med = float(np.median([m[1] for m in mains]))
        for idx, ar in mains:
            if ar > MAX_BODY_RATIO * med:
                problems.append(f"{d.name}#{idx}: 主体面积 {int(ar)} 超过片段中位 {int(med)} 的 {MAX_BODY_RATIO}x")
    return problems, len(meta["frames"])


def main() -> int:
    verbose = "--list" in sys.argv
    clips = sorted(p for p in ROOT.iterdir() if (p / "frames.json").exists())
    bad: list[str] = []
    total_frames = 0
    for d in clips:
        probs, n = check_clip(d)
        total_frames += n
        bad.extend(probs)
        if verbose:
            print(f"{d.name}: {n} 帧 {'违规 ' + str(len(probs)) if probs else 'OK'}")
    print(f"检查 {len(clips)} 个片段目录、{total_frames} 帧，违规 {len(bad)} 处")
    for b in bad:
        print("  " + b)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
