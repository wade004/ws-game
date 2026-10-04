#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""演示场景静态美术出图（地面砖、场景道具、训练木桩、特效源图），只走本地 ComfyUI（Qwen Image 2.1）。

    python gen_static.py submit  --raw-dir <本地目录>     # 提交进 ComfyUI 队列（不等待）
    python gen_static.py collect --raw-dir <本地目录>     # 取回已完成的图

原始大图只留本地，不入库；后处理见 build_art.py。
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import comfy  # noqa: E402

TILE = ("Top-down hand-painted 2D game ground texture, seamless tileable, flat even lighting, painterly with subtle detail, no objects, no characters, no text, no border, ")
PROP = ("Hand-painted 2D action RPG environment prop sprite, painterly digital illustration with clean dark outlines and cel shading, vibrant colors, "
        "seen from a high three-quarter top-down game camera about 40 degrees above, single object centered with empty margin around, no ground, no cast shadow, no text. ")
FX = ("Hand-painted 2D game visual effect sprite, bright glowing painterly style, single effect centered, pure transparent background, no characters, no text. ")

# name -> (prompt, seed, rgba)
ITEMS = {
    "floor_stone": (TILE + "worn grey-blue stone flagstones with thin dark mortar gaps, a few small cracks and faint moss patches.", 101, False),
    "floor_stone2": (TILE + "worn grey-blue stone flagstones with thin dark mortar gaps, slightly lighter, scattered small pebbles and dust.", 102, False),
    "floor_dirt": (TILE + "dark rocky packed earth arena ground, mottled brown and grey soil with scattered pebbles, small cracks and faint scuff marks, rich texture.", 133, False),
    "prop_pillar": (PROP + "A tall broken ancient stone pillar with a carved base, mossy, cracked top.", 111, True),
    "prop_crate": (PROP + "A sturdy wooden supply crate with iron straps.", 112, True),
    "prop_barrel": (PROP + "A wooden barrel with iron hoops.", 113, True),
    "prop_brazier": (PROP + "A standing iron brazier bowl with a bright orange fire burning in it.", 114, True),
    "prop_rocks": (PROP + "A cluster of mossy grey boulders.", 115, True),
    "prop_dummy": (PROP + "A wooden training dummy: a thick wooden post with a straw-stuffed round torso wrapped in rope, a crossbar for arms, a burlap sack head, standing on a small wooden base.", 116, True),
    "fx_slash": (FX + "A bright white and cyan crescent sword slash arc streak, curved energy swoosh, tapered ends, glowing edge, wide horizontal crescent.", 121, True),
    "fx_spark": (FX + "A bright yellow-white impact hit spark burst, sharp radiating star-shaped flash with a few flying sparks.", 122, True),
    "fx_dust": (FX + "A soft brown-grey dust puff cloud kicked up from the ground, billowing, painterly.", 123, True),
    "fx_ring": (FX + "A white and light-blue circular shockwave ring, thin glowing ring burst with faint motion lines.", 124, True),
}


def cmd_submit(args) -> None:
    raw = Path(args.raw_dir) / "static"
    raw.mkdir(parents=True, exist_ok=True)
    for name, (prompt, seed, rgba) in ITEMS.items():
        job = raw / (name + ".json")
        if job.exists() and not args.force:
            continue
        wf = comfy.qwen_wf(prompt, seed, 1024, 1024, (), 25, rgba, "show_s_" + name)
        pid = comfy.post(wf, front=True)
        job.write_text(json.dumps({"pid": pid, "prompt": prompt, "seed": seed, "rgba": rgba}, ensure_ascii=False, indent=1), encoding="utf-8")
        print("submitted", name, pid)


def cmd_collect(args) -> None:
    import requests

    raw = Path(args.raw_dir) / "static"
    pending = got = 0
    for job in sorted(raw.glob("*.json")):
        out = job.with_suffix(".png")
        if out.exists():
            continue
        info = json.loads(job.read_text(encoding="utf-8"))
        h = requests.get(comfy.HOST + "/history/" + info["pid"]).json()
        if info["pid"] not in h or h[info["pid"]].get("status", {}).get("completed") is None:
            pending += 1
            continue
        out.write_bytes(comfy.fetch(comfy.images_of(h[info["pid"]])[0]))
        got += 1
    print(f"collected {got}, pending {pending}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest="cmd", required=True)
    for n in ("submit", "collect"):
        p = sub.add_parser(n)
        p.add_argument("--raw-dir", required=True)
        if n == "submit":
            p.add_argument("--force", action="store_true")
    args = ap.parse_args()
    {"submit": cmd_submit, "collect": cmd_collect}[args.cmd](args)
    return 0


if __name__ == "__main__":
    sys.exit(main())
