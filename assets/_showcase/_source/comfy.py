#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""演示场景美术：本地 ComfyUI 的最小封装（Qwen Image 2.1 文生图/参考图编辑、Wan 2.2 I2V 图生视频）。只走 127.0.0.1:8188。"""
import io, json, os, time, urllib.request, urllib.parse, uuid
import requests
from PIL import Image

HOST = "http://127.0.0.1:8188"
WRAP_PRE = "This is an RGBA format image with transparency. "
WRAP_SUF = " The image has an alpha channel and a transparent background."


def post(prompt, front=False):
    body = {"prompt": prompt, "client_id": uuid.uuid4().hex}
    if front:
        body["front"] = True  # 插到队列最前
    r = requests.post(HOST + "/prompt", json=body)
    if r.status_code != 200:
        raise RuntimeError("提交失败 %s %s" % (r.status_code, r.text[:2000]))
    return r.json()["prompt_id"]


def wait(pid, poll=2.0, timeout=3600):
    t0 = time.time()
    while True:
        h = requests.get(HOST + "/history/" + pid).json()
        if pid in h and h[pid].get("status", {}).get("completed") is not None:
            st = h[pid]["status"]
            if st.get("status_str") != "success":
                raise RuntimeError("执行失败：" + json.dumps(st.get("messages"), ensure_ascii=False)[:3000])
            return h[pid]
        if time.time() - t0 > timeout:
            raise TimeoutError(pid)
        time.sleep(poll)


def fetch(img):
    q = urllib.parse.urlencode({"filename": img["filename"], "subfolder": img.get("subfolder", ""), "type": img.get("type", "output")})
    return requests.get(HOST + "/view?" + q).content


def upload(path, name=None):
    name = name or os.path.basename(path)
    with open(path, "rb") as f:
        r = requests.post(HOST + "/upload/image", files={"image": (name, f, "image/png")}, data={"overwrite": "true", "type": "input"})
    r.raise_for_status()
    return r.json()["name"]


def images_of(hist, node=None):
    out = []
    for nid, o in hist["outputs"].items():
        if node is not None and nid != node:
            continue
        for im in o.get("images", []):
            out.append(im)
    return out


# ---------------------------------------------------------------- Qwen Image 2.1
def qwen_wf(prompt, seed, w, h, refs=(), steps=25, rgba=True, prefix="show", neg=""):
    p = (WRAP_PRE + prompt + WRAP_SUF) if rgba else prompt
    wf = {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": "qwen_image_2.1_bf16.safetensors", "weight_dtype": "default"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen3vl_8b_int8_convrot.safetensors", "type": "qwen_image", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "qwen_image_2.1_vae_bf16.safetensors"}},
    }
    enc = {"clip": ["2", 0], "prompt": p, "negative_prompt": neg, "resolution": 1024}
    for i, ref in enumerate(refs):
        wf["l%d" % i] = {"class_type": "LoadImage", "inputs": {"image": ref}}
        enc["images.image_%d" % (i + 1)] = ["l%d" % i, 0]
    if refs:
        enc["vae"] = ["3", 0]
    wf["4"] = {"class_type": "TextEncodeQwenImage21", "inputs": enc}
    wf["5"] = {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}}
    wf["6"] = {"class_type": "KSampler", "inputs": {"model": ["1", 0], "positive": ["4", 0], "negative": ["4", 1], "latent_image": ["5", 0],
                                                    "seed": seed, "steps": steps, "cfg": 1, "sampler_name": "euler", "scheduler": "simple", "denoise": 1}}
    wf["7"] = {"class_type": "VAEDecode", "inputs": {"samples": ["6", 0], "vae": ["3", 0]}}
    wf["8"] = {"class_type": "SaveImage", "inputs": {"images": ["7", 0], "filename_prefix": prefix}}
    return wf


def qwen(prompt, seed, out, w=1024, h=1024, refs=(), rgba=True, steps=25, neg=""):
    """refs 是已上传到 ComfyUI input 目录的文件名。返回 PIL RGBA 图并存到 out。"""
    wf = qwen_wf(prompt, seed, w, h, refs, steps, rgba, "show_q", neg)
    pid = post(wf)
    hist = wait(pid)
    im = images_of(hist)[0]
    data = fetch(im)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "wb") as f:
        f.write(data)
    return Image.open(io.BytesIO(data))


# ---------------------------------------------------------------- Wan 2.2 I2V（lightx2v 4 步）
def wan_wf(start_name, prompt, neg, seed, w, h, length, prefix="show_w", steps=4, shift=5.0):
    half = steps // 2
    return {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": "wan2.2_i2v_high_noise_14B_fp8_scaled.safetensors", "weight_dtype": "default"}},
        "2": {"class_type": "UNETLoader", "inputs": {"unet_name": "wan2.2_i2v_low_noise_14B_fp8_scaled.safetensors", "weight_dtype": "default"}},
        "3": {"class_type": "LoraLoaderModelOnly", "inputs": {"model": ["1", 0], "lora_name": "wan2.2_i2v_lightx2v_4steps_lora_v1_high_noise.safetensors", "strength_model": 1.0}},
        "4": {"class_type": "LoraLoaderModelOnly", "inputs": {"model": ["2", 0], "lora_name": "wan2.2_i2v_lightx2v_4steps_lora_v1_low_noise.safetensors", "strength_model": 1.0}},
        "5": {"class_type": "ModelSamplingSD3", "inputs": {"model": ["3", 0], "shift": shift}},
        "6": {"class_type": "ModelSamplingSD3", "inputs": {"model": ["4", 0], "shift": shift}},
        "7": {"class_type": "CLIPLoader", "inputs": {"clip_name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors", "type": "wan", "device": "default"}},
        "8": {"class_type": "VAELoader", "inputs": {"vae_name": "wan_2.1_vae.safetensors"}},
        "9": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["7", 0], "text": prompt}},
        "10": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["7", 0], "text": neg}},
        "11": {"class_type": "LoadImage", "inputs": {"image": start_name}},
        "12": {"class_type": "WanImageToVideo", "inputs": {"positive": ["9", 0], "negative": ["10", 0], "vae": ["8", 0], "width": w, "height": h, "length": length, "batch_size": 1, "start_image": ["11", 0]}},
        "13": {"class_type": "KSamplerAdvanced", "inputs": {"model": ["5", 0], "add_noise": "enable", "noise_seed": seed, "steps": steps, "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple",
                                                            "positive": ["12", 0], "negative": ["12", 1], "latent_image": ["12", 2], "start_at_step": 0, "end_at_step": half, "return_with_leftover_noise": "enable"}},
        "14": {"class_type": "KSamplerAdvanced", "inputs": {"model": ["6", 0], "add_noise": "disable", "noise_seed": 0, "steps": steps, "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple",
                                                            "positive": ["12", 0], "negative": ["12", 1], "latent_image": ["13", 0], "start_at_step": half, "end_at_step": 10000, "return_with_leftover_noise": "disable"}},
        "15": {"class_type": "VAEDecode", "inputs": {"samples": ["14", 0], "vae": ["8", 0]}},
        "16": {"class_type": "SaveImage", "inputs": {"images": ["15", 0], "filename_prefix": prefix}},
    }


WAN_NEG = ("static, still image, blurry, low quality, jpeg artifacts, camera movement, zoom, pan, shaky camera, scene change, background change, "
           "extra limbs, deformed, distorted, text, watermark, logo, multiple characters, shadow on ground, floor, 色调艳丽，过曝，静态，细节模糊不清，字幕，画面静止，整体发灰，最差质量，低质量")


def wan(start_path, prompt, seed, out_dir, w=640, h=640, length=33, neg=WAN_NEG, tag="clip"):
    """图生视频：返回帧文件路径列表（PNG，RGB）。"""
    name = upload(start_path, "show_" + os.path.basename(start_path))
    wf = wan_wf(name, prompt, neg, seed, w, h, length, prefix="show_w_" + tag)
    t0 = time.time()
    pid = post(wf)
    hist = wait(pid, poll=3.0)
    os.makedirs(out_dir, exist_ok=True)
    paths = []
    for i, im in enumerate(images_of(hist)):
        p = os.path.join(out_dir, "f%03d.png" % i)
        with open(p, "wb") as f:
            f.write(fetch(im))
        paths.append(p)
    print("wan %s: %d 帧, %.0f 秒" % (tag, len(paths), time.time() - t0))
    return paths
