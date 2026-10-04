#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""演示场景美术出图脚本（只走本地 ComfyUI）。

    python gen_art.py starts  --raw-dir <本地目录> [--char hero,grunt,brute]      # 角色三视图（Qwen 文生图 + 参考图编辑）
    python gen_art.py submit  --raw-dir <本地目录> --char hero [--views side] [--only run,idle]   # 把动作片段提交进 ComfyUI 队列（不等待）
    python gen_art.py collect --raw-dir <本地目录>                                   # 取回已完成片段的帧

原始大图与视频帧只留本地（--raw-dir），不入库；后处理见 build_art.py。
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import comfy  # noqa: E402
import showcase_spec as S  # noqa: E402

GREEN = (0, 255, 0)
WAN_SIZE = 640


def green_start(src_rgba: Path, dst: Path, size: int = WAN_SIZE, fill: float = 0.52, foot: float = 0.80) -> None:
    """把角色（RGBA）规整地放到绿幕画布上：包围盒高度占画布 fill、水平居中、脚底落在 foot 高度处。"""
    im = Image.open(src_rgba).convert("RGBA")
    bb = im.getchannel("A").point(lambda a: 255 if a > 24 else 0).getbbox()
    crop = im.crop(bb)
    scale = size * fill / crop.height
    crop = crop.resize((max(1, round(crop.width * scale)), max(1, round(crop.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), GREEN + (255,))
    x = (size - crop.width) // 2
    y = round(size * foot) - crop.height
    canvas.alpha_composite(crop, (x, y))
    canvas.convert("RGB").save(dst)


def cmd_starts(args) -> None:
    raw = Path(args.raw_dir)
    for name in args.char.split(","):
        spec = S.CHARACTERS[name]
        d = raw / name
        d.mkdir(parents=True, exist_ok=True)
        front = d / "front.png"
        if not front.exists():
            comfy.qwen(S.STYLE + " " + spec["desc"] + "viewed from the front, facing the camera.", spec["front_seed"], str(front))
        ref = comfy.upload(str(front), f"show_{name}_front.png")
        for view in ("side", "back"):
            out = d / f"{view}.png"
            if not out.exists():
                tail = spec.get("edit_side_prompt") if view == "side" and spec.get("edit_side_prompt") else S.EDIT_VIEW[view]
                comfy.qwen(S.EDIT_BASE + tail, spec["edit_seeds"][view], str(out), refs=[ref])
        for view in S.VIEWS:
            green_start(d / f"{view}.png", d / f"{view}_green.png")
        print("起始图完成：", name)


def clip_seed(char: str, view: str, key: str, add: int = 0) -> int:
    return S.SEEDS_BASE + add + sum(ord(c) * (i + 3) for i, c in enumerate(char + view + key))


def cmd_submit(args) -> None:
    raw = Path(args.raw_dir)
    char = args.char
    views = args.views.split(",") if args.views else list(S.VIEWS)
    only = set(args.only.split(",")) if args.only else None
    for view in views:
        for clip in S.CLIPS[char]:
            key = clip["key"]
            if only and key not in only:
                continue
            if clip["prompt"].startswith("(派生"):
                continue  # 由 build_art 派生，不送视频模型
            cdir = raw / char / view / key
            job = cdir / "job.json"
            if job.exists() and not args.force:
                continue
            start = clip.get("start", "stand")
            if start == "stand":
                start_path = raw / char / f"{view}_green.png"
            else:
                prev = start.split(":", 1)[1]
                frames = sorted((raw / char / view / prev).glob("f*.png"))
                if not frames:
                    print("跳过（前序片段未完成）", char, view, key)
                    continue
                start_path = frames[-1]
            prompt = S.WAN_BASE + clip["prompt"].replace("{fwd}", S.VIEW_FWD[view])
            prompt += " The character is " + S.VIEW_FACING[view] + "."
            seed = clip_seed(char, view, key, clip.get("seed_add", 0))
            name = comfy.upload(str(start_path), f"show_{char}_{view}_{key}.png")
            wf = comfy.wan_wf(name, prompt, comfy.WAN_NEG, seed, args.size, args.size, clip["length"], prefix=f"show_w_{char}_{view}_{key}")
            pid = comfy.post(wf)
            cdir.mkdir(parents=True, exist_ok=True)
            job.write_text(json.dumps({"pid": pid, "prompt": prompt, "seed": seed, "length": clip["length"], "start": str(start_path)}, ensure_ascii=False, indent=1), encoding="utf-8")
            print("已提交", char, view, key, pid)


def cmd_collect(args) -> None:
    raw = Path(args.raw_dir)
    import requests

    pending = 0
    got = 0
    for job in sorted(raw.rglob("job.json")):
        cdir = job.parent
        if (cdir / "f000.png").exists():
            continue
        info = json.loads(job.read_text(encoding="utf-8"))
        h = requests.get(comfy.HOST + "/history/" + info["pid"]).json()
        if info["pid"] not in h or h[info["pid"]].get("status", {}).get("completed") is None:
            pending += 1
            continue
        hist = h[info["pid"]]
        if hist["status"].get("status_str") != "success":
            print("失败", cdir, json.dumps(hist["status"].get("messages"), ensure_ascii=False)[:500])
            continue
        for i, im in enumerate(comfy.images_of(hist)):
            (cdir / f"f{i:03d}.png").write_bytes(comfy.fetch(im))
        got += 1
    print(f"取回 {got} 个片段，仍在队列 {pending} 个")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest="cmd", required=True)
    for n in ("starts", "submit", "collect"):
        p = sub.add_parser(n)
        p.add_argument("--raw-dir", required=True)
        if n != "collect":
            p.add_argument("--char", default="hero,grunt,brute" if n == "starts" else "hero")
        if n == "submit":
            p.add_argument("--views", default=None)
            p.add_argument("--only", default=None)
            p.add_argument("--force", action="store_true")
            p.add_argument("--size", type=int, default=WAN_SIZE)
    args = ap.parse_args()
    {"starts": cmd_starts, "submit": cmd_submit, "collect": cmd_collect}[args.cmd](args)
    return 0


if __name__ == "__main__":
    sys.exit(main())
