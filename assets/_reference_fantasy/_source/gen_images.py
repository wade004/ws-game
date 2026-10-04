#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""参考皮肤包出图脚本：把 prompts.json 里的任务提交给本地 ComfyUI（Qwen Image 2.1，原生 RGBA），原始图落到 --raw-dir。

    python assets/_reference_fantasy/_source/gen_images.py --raw-dir <本地目录> [--only key,key] [--force] [--host http://127.0.0.1:8188]

只走本地 ComfyUI（项目规则：图像生成一律走本地）；原始 1024 图只留本地，不入库。产物文件名 ``<key>__s<seed>.png``，已存在的跳过（--force 重出）。
门禁不跑本脚本；后处理见 build_pack.py。
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent


def workflow(spec: dict, prompt: str, seed: int, w: int, h: int, prefix: str) -> dict:
    wf = spec["workflow"]
    return {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": wf["unet"], "weight_dtype": "default"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": wf["clip"], "type": "qwen_image", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": wf["vae"]}},
        "4": {"class_type": "TextEncodeQwenImage21", "inputs": {"clip": ["2", 0], "prompt": prompt, "negative_prompt": "", "resolution": 1024}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
        "6": {"class_type": "KSampler", "inputs": {"model": ["1", 0], "positive": ["4", 0], "negative": ["4", 1], "latent_image": ["5", 0],
                                                    "seed": seed, "steps": wf["steps"], "cfg": wf["cfg"], "sampler_name": wf["sampler"],
                                                    "scheduler": wf["scheduler"], "denoise": 1}},
        "7": {"class_type": "VAEDecode", "inputs": {"samples": ["6", 0], "vae": ["3", 0]}},
        "8": {"class_type": "SaveImage", "inputs": {"images": ["7", 0], "filename_prefix": prefix}},
    }


def post(host: str, prompt: dict) -> str:
    req = urllib.request.Request(host + "/prompt", json.dumps({"prompt": prompt}).encode(), {"Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(req).read())["prompt_id"]


def wait(host: str, pid: str) -> dict:
    while True:
        hist = json.loads(urllib.request.urlopen(host + "/history/" + pid).read())
        if pid in hist and hist[pid].get("status", {}).get("completed") is not None:
            return hist[pid]
        time.sleep(1.5)


def fetch(host: str, image: dict) -> bytes:
    q = urllib.parse.urlencode({"filename": image["filename"], "subfolder": image.get("subfolder", ""), "type": image.get("type", "output")})
    return urllib.request.urlopen(host + "/view?" + q).read()


def full_prompt(spec: dict, job: dict) -> str:
    w = spec["wrapper"]
    return w["prefix"] + spec["style"][job["style"]] + " " + job["text"] + w["suffix"]


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--raw-dir", required=True)
    ap.add_argument("--only", default=None, help="逗号分隔的任务 key 前缀")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--host", default="http://127.0.0.1:8188")
    ap.add_argument("--prompts", default=str(HERE / "prompts.json"))
    args = ap.parse_args(argv)

    spec = json.loads(Path(args.prompts).read_text(encoding="utf-8"))
    raw = Path(args.raw_dir)
    raw.mkdir(parents=True, exist_ok=True)
    only = [s for s in (args.only or "").split(",") if s]
    done = 0
    for job in spec["jobs"]:
        if only and not any(job["key"].startswith(p) for p in only):
            continue
        for seed in job["seeds"]:
            dst = raw / f"{job['key']}__s{seed}.png"
            if dst.exists() and not args.force:
                continue
            t0 = time.time()
            pid = post(args.host, workflow(spec, full_prompt(spec, job), seed, job["w"], job["h"], "refskin_" + job["key"]))
            res = wait(args.host, pid)
            if res["status"]["status_str"] != "success":
                print("FAILED", job["key"], seed, res["status"], file=sys.stderr)
                continue
            for out in res["outputs"].values():
                for im in out.get("images", []):
                    dst.write_bytes(fetch(args.host, im))
            done += 1
            print(f"{dst.name} {time.time() - t0:.1f}s", flush=True)
    print(f"done {done}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
