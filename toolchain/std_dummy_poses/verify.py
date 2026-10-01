"""自检：必备键 × 全方向档、事件标记（04 §8）、帧数规则、循环连续、步幅、画布裁切、数据行与规格一致。

退出码语义：有任一"错误"即失败；"警告"（推荐键缺项、清单外残留文件）只打印。
"""

from __future__ import annotations

import json
import math
import re
from pathlib import Path

from PIL import Image, ImageDraw

from . import config as C
from .build import SPEC_FILE, clip_dir, event_pct
from .poses import pose_at
from .skeleton import POSE_KEYS, build_parts

# 04 §3 清单的独立判据（不从 config 推导，避免"自己验自己"）：
REQUIRED_KEYS = ["idle", "move.walk", "move.run", "attack", "hit", "death",
                 "attack.unarmed", "attack.1h", "attack.2h"]
RECOMMENDED_KEYS = ["idle.combat", "move.run.combat", "attack.unarmed.02", "attack.1h.02", "attack.2h.02",
                    "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup", "cast", "dodge", "jump"]

# 武器层逐层剪辑清单的独立判据（不从 config 推导）：持械角色播这些无族状态键时武器层必须随身体帧走
# （ADR-0072：缺逐层剪辑的层维持静态图，这正是要消除的遗留）。
STATE_KEYS_WITH_WEAPON_LAYER = ["hit", "hit.light", "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup",
                                "death", "jump", "cast", "dodge"]

_STATES = {"idle", "move", "attack", "cast", "hit", "death", "jump", "dodge"}
_GAITS = {"walk", "run", "sprint"}
_FAMILIES = {"unarmed", "1h", "2h", "polearm", "bow", "staff", "dual", "shield"}
_HIT_SUFFIX = {"light", "heavy", "knockback", "knockdown", "getup"}


def parse_key(key: str) -> dict | None:
    """04 §2.1：<state>[.<gait>][.<stance>][.<family>][.<variant>]；变体位这里只认攻击段号（两位数字）与 hit 的后缀。"""
    parts = key.split(".")
    if parts[0] not in _STATES:
        return None
    out = {"state": parts[0], "gait": None, "stance": None, "family": None, "variant": None}
    rest = parts[1:]
    if parts[0] == "hit" and rest and rest[0] in _HIT_SUFFIX:
        out["variant"] = rest[0]
        rest = rest[1:]
    if parts[0] == "move" and rest and rest[0] in _GAITS:
        out["gait"] = rest[0]
        rest = rest[1:]
    if rest and rest[0] == "combat":
        out["stance"] = "combat"
        rest = rest[1:]
    if rest and rest[0] in _FAMILIES:
        out["family"] = rest[0]
        rest = rest[1:]
    if rest and re.fullmatch(r"\d\d", rest[0]):
        out["variant"] = rest[0]
        rest = rest[1:]
    return out if not rest else None


class Report:
    def __init__(self):
        self.errors: list[str] = []
        self.warnings: list[str] = []
        self.counts: dict[str, int] = {}

    def err(self, msg):
        self.errors.append(msg)

    def warn(self, msg):
        self.warnings.append(msg)

    def tick(self, name, n=1):
        self.counts[name] = self.counts.get(name, 0) + n


def _load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def _ev(clip_spec: dict, name: str) -> list[float]:
    return [e["time_pct"] for e in clip_spec["events"] if e["name"] == name]


def verify(assets_out: Path, data_out: Path, deep_images: bool = True) -> Report:
    r = Report()
    spec_path = assets_out / SPEC_FILE
    if not spec_path.is_file():
        r.err(f"缺规格文件 {spec_path}")
        return r
    spec = _load(spec_path)
    clips = spec["clips"]
    params = spec["params"]
    fps = params["fps"]
    slots = spec["directions"]["canonical"]
    dc = params["direction_count"]

    # --- 方向档与既有导入工具的命名一致 ---
    try:
        from asset_import.directions import all_slot_names, canonical_slot_names, mirror_slot_name  # type: ignore
        if canonical_slot_names(dc) != slots:
            r.err(f"canonical 档位与 asset_import.directions 不一致：{slots} vs {canonical_slot_names(dc)}")
        mirrors = [m["direction_slot"] for m in spec["directions"]["mirror_pairs"]]
        if sorted(slots + mirrors) != sorted(all_slot_names(dc)):
            r.err("canonical + 镜像档位集合与 all_slot_names 不一致")
        for m in spec["directions"]["mirror_pairs"]:
            if mirror_slot_name(m["mirror_of"]) != m["direction_slot"]:
                r.err(f"镜像命名不一致：{m}")
        r.tick("方向档命名核对")
    except ImportError:
        r.warn("未能导入 asset_import.directions，跳过方向档命名核对（需要 toolchain 在 sys.path）")
    if len(slots) + len(spec["directions"]["mirror_pairs"]) != dc:
        r.err(f"方向档总数 {len(slots)}+{len(spec['directions']['mirror_pairs'])} != direction_count {dc}")

    # --- 04 §3 键清单 ---
    for k in REQUIRED_KEYS:
        if k not in clips:
            r.err(f"必备键缺失：{k}")
    for k in RECOMMENDED_KEYS:
        if k not in clips:
            r.warn(f"推荐键缺失：{k}")
    for k in clips:
        if parse_key(k) is None:
            r.err(f"键不符合 04 §2.1 语法：{k}")
    r.tick("键语法与清单", len(clips))
    for k in STATE_KEYS_WITH_WEAPON_LAYER:
        if k in clips and C.LAYER_WEAPON not in clips[k].get("layers", []):
            r.err(f"{k} 缺武器层（{C.LAYER_WEAPON}）逐层剪辑：持械角色播放时武器层会是静态图，不随身体帧走")

    # --- 数据行与规格一致 ---
    data_path = data_out / "display" / "display.anim_set.json"
    if not data_path.is_file():
        r.err(f"缺数据文件 {data_path}")
    else:
        doc = _load(data_path)
        rows = [x for x in doc.get("rows", []) if x.get("id") == spec["anim_set_id"]]
        if len(rows) != 1:
            r.err(f"数据文件缺行 {spec['anim_set_id']}")
        else:
            row = rows[0]
            if not spec["anim_set_id"].startswith("display.anim_set.std_dummy_"):
                r.err("anim_set id 前缀不是 display.anim_set.std_dummy_")
            if set(row["clips"]) != set(clips):
                r.err(f"数据行键集合与规格不一致：差 {sorted(set(row['clips']) ^ set(clips))}")
            for k, v in row["clips"].items():
                if k in clips and (v["resource_ref"] != clips[k]["resource_ref"] or v["events"] != clips[k]["events"]):
                    r.err(f"数据行与规格不一致：{k}")
            r.tick("数据行核对", len(row["clips"]))

    # --- 逐键：事件、帧数规则、循环连续、步幅 ---
    defs = {c.key: c for c in C.build_clip_defs()}
    for key, e in clips.items():
        c = defs.get(key)
        if c is None:
            r.err(f"规格里有 config 没有的键 {key}")
            continue
        total = e["total_ms"]
        plan = C.phase_frame_plan(c, fps)
        if e["frames_per_phase"] != [n for _a, n, _b in plan] or e["frame_count"] != sum(n for _a, n, _b in plan):
            r.err(f"{key} 帧数与参数推算不一致")
        for ev in e["events"]:
            if not (0.0 <= ev["time_pct"] <= 1.0):
                r.err(f"{key} 事件 {ev['name']} time_pct 越界 {ev['time_pct']}")
        info = parse_key(key)
        if e.get("weapon_layer_family") != c.weapon_layer_family:
            r.err(f"{key} 规格的 weapon_layer_family={e.get('weapon_layer_family')!r} 与 config 的 {c.weapon_layer_family!r} 不一致")
        if info["state"] == "attack" and key != "dodge":
            a0, a1, hits = _ev(e, "active_start"), _ev(e, "active_end"), _ev(e, "hit")
            if len(a0) != 1 or len(a1) != 1 or not hits:
                r.err(f"{key} 攻击类缺 active_start/active_end/hit")
            elif not (a0[0] < a1[0]) or any(not (a0[0] <= h <= a1[0]) for h in hits):
                r.err(f"{key} hit 不在判定相内：active=[{a0[0]},{a1[0]}] hit={hits}")
            w, a, rr = (p["ms"] for p in e["phases"])
            exp = {"active_start": event_pct(w, total), "active_end": event_pct(w + a, total)}
            if (a0 and abs(a0[0] - exp["active_start"]) > 1e-4) or (a1 and abs(a1[0] - exp["active_end"]) > 1e-4):
                r.err(f"{key} 判定相标记与三相毫秒数不一致")
            fam = e["family"]
            if fam in C.PHASES_SEG1 and e["phases"][0]["ms"] and key == C.attack_key(fam, 1):
                if tuple(p["ms"] for p in e["phases"]) != C.PHASES_SEG1[fam]:
                    r.err(f"{key} 三相与配置起点不一致")
        if info["state"] == "move" and info["gait"] in ("walk", "run"):
            fs = _ev(e, "footstep")
            if len(fs) < 2:
                r.err(f"{key} 缺 footstep（至少 2 次/循环）")
            if "step_displacement_bh" not in e:
                r.err(f"{key} 缺每步位移")
        if key == "dodge":
            s0, s1 = _ev(e, "invuln_start"), _ev(e, "invuln_end")
            if len(s0) != 1 or len(s1) != 1 or not (0.0 <= s0[0] < s1[0] <= 1.0):
                r.err("dodge 无敌窗口标记缺失或越界")
            else:
                m0, m1 = _ev(e, "motion_start"), _ev(e, "motion_end")
                if not m0 or not m1 or not (m0[0] <= s0[0] and s1[0] <= m1[0]):
                    r.err("dodge 无敌窗口应落在 motion 窗口内")
        if c.loop:
            p0, p1 = pose_at(c, 0.0), pose_at(c, float(total))
            dmax = max(abs(p0[k] - p1[k]) for k in POSE_KEYS)
            if dmax > 1e-6:
                r.err(f"{key} 循环首尾姿势不连续（最大差 {dmax:.6f}）")
            r.tick("循环连续")
        if info["state"] == "move" and info["gait"] in ("walk", "run") and not c.combat and c.family is None:
            # 接触姿势两脚前后间距 ≈ 标称每步位移（容差 5%）
            _parts, j = build_parts(pose_at(c, 0.0), None)
            sep = abs(j["ankle_m"][2] - j["ankle_o"][2])
            step = C.step_displacement_bh(info["gait"])
            if abs(sep - step) > 0.05 * step:
                r.err(f"{key} 接触姿势脚间距 {sep:.3f} 与每步位移 {step:.3f} 偏差超 5%")
            r.tick("步幅核对")

    # --- 文件与图像 ---
    for key, e in clips.items():
        if e.get("alias_of"):
            if e["resource_ref"] != clips[e["alias_of"]]["resource_ref"]:
                r.err(f"{key} 别名资源引用与目标不一致")
            continue
        ref = e["resource_ref"]
        want = [("整身", clip_dir(assets_out, ref))]
        for slot in slots:
            if params["composite_direction_variants"]:
                want.append((f"整身/{slot}", clip_dir(assets_out, ref, slot)))
            for layer in e["layers"]:
                want.append((f"{layer}/{slot}", clip_dir(assets_out, ref, slot, layer)))
        for tag, d in want:
            _check_clip_dir(r, key, tag, d, e, fps, deep_images)

    # --- 清单外残留 ---
    root = assets_out / "sprite_anim"
    if root.is_dir():
        expected = set()
        for key, e in clips.items():
            if e.get("alias_of"):
                continue
            expected.add(clip_dir(assets_out, e["resource_ref"]).name)
            for slot in slots:
                if params["composite_direction_variants"]:
                    expected.add(clip_dir(assets_out, e["resource_ref"], slot).name)
                for layer in e["layers"]:
                    expected.add(clip_dir(assets_out, e["resource_ref"], slot, layer).name)
        for d in root.iterdir():
            if d.is_dir() and d.name.startswith(C.STEM_PREFIX) and d.name not in expected:
                r.warn(f"清单外残留目录：{d.name}")

    # --- 路径推导与既有契约一致 ---
    try:
        from asset_import.ref_conventions import sprite_anim_dir  # type: ignore
        for key, e in clips.items():
            if clip_dir(Path("."), e["resource_ref"]).as_posix() != "./" + sprite_anim_dir(e["resource_ref"]) and \
                    clip_dir(Path("."), e["resource_ref"]).as_posix() != sprite_anim_dir(e["resource_ref"]):
                r.err(f"{key} 目录与 ref_conventions.sprite_anim_dir 不一致")
        r.tick("路径契约核对")
    except ImportError:
        r.warn("未能导入 asset_import.ref_conventions，跳过路径契约核对")
    return r


def _distinct_frames(im: Image.Image, frames: list[dict]) -> int:
    """图集里内容互不相同的帧数（按帧矩形像素字节去重）。"""
    seen = set()
    for f in frames:
        seen.add(im.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])).tobytes())
    return len(seen)


def _check_clip_dir(r: Report, key: str, tag: str, d: Path, e: dict, fps: int, deep: bool) -> None:
    atlas, fj = d / "atlas.png", d / "frames.json"
    if not atlas.is_file() or not fj.is_file():
        r.err(f"{key} [{tag}] 缺 atlas.png/frames.json：{d.name}")
        return
    doc = _load(fj)
    frames = doc.get("frames", [])
    if len(frames) != e["frame_count"]:
        r.err(f"{key} [{tag}] 帧数 {len(frames)} != 推算 {e['frame_count']}")
    if doc.get("fps") != fps or bool(doc.get("loop")) != bool(e["loop"]):
        r.err(f"{key} [{tag}] frames.json 的 fps/loop 与规格不一致")
    fw, fh = C.CANVAS
    if doc.get("frame_w") != fw or doc.get("frame_h") != fh:
        r.err(f"{key} [{tag}] 帧尺寸不一致")
    total = sum(f.get("duration", 0.0) for f in frames) * 1000.0
    if abs(total - e["total_ms"]) > 0.5:
        r.err(f"{key} [{tag}] 时长合计 {total:.2f} ms != {e['total_ms']} ms")
    r.tick("资源目录", 1)
    if not deep:
        return
    with Image.open(atlas) as im:
        im.load()
        for f in frames:
            if f["x"] + f["w"] > im.width or f["y"] + f["h"] > im.height:
                r.err(f"{key} [{tag}] 帧矩形越出图集")
                return
        # 画布裁切：任何不透明像素触碰帧边缘即视为被裁切
        alpha = im.getchannel("A")
        for f in frames:
            box = (f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])
            fa = alpha.crop(box)
            w, h = fa.size
            px = fa.load()
            edge = [px[x, 0] for x in range(w)] + [px[x, h - 1] for x in range(w)] + \
                   [px[0, y] for y in range(h)] + [px[w - 1, y] for y in range(h)]
            if any(v > 0 for v in edge):
                r.err(f"{key} [{tag}] 第 {f['index']} 帧触碰画布边缘（被裁切）")
                break
        if "body" in tag or "整身" in tag:
            if alpha.getbbox() is None:
                r.err(f"{key} [{tag}] 图集全透明")
        if tag.startswith(C.LAYER_WEAPON + "/"):
            if alpha.getbbox() is None:
                r.err(f"{key} [{tag}] 武器层图集全透明")
            else:
                # 武器层随身体帧走：身体层有多于一种帧内容时，武器层也必须有多于一种（否则是"静态图重复 N 帧"）
                body_d = d.parent / d.name.replace("__" + C.LAYER_WEAPON, "__" + C.LAYER_BODY)
                weapon_distinct = _distinct_frames(im, frames)
                body_distinct = None
                if (body_d / "atlas.png").is_file():
                    with Image.open(body_d / "atlas.png") as bim:
                        bim.load()
                        body_distinct = _distinct_frames(bim, _load(body_d / "frames.json").get("frames", []))
                if body_distinct and body_distinct > 1 and weapon_distinct <= 1:
                    r.err(f"{key} [{tag}] 武器层 {len(frames)} 帧内容完全相同（静态图），身体层有 {body_distinct} 种不同帧：武器层没有随身体帧走")
                r.tick("武器层随帧核对")


def contact_sheet(assets_out: Path, out_path: Path, columns: int = 8) -> Path:
    """每键一格（整身合成，取代表帧：攻击取 hit 所在帧，循环取 1/4 处，其余取中间帧），带键名。只留本地。"""
    spec = _load(assets_out / SPEC_FILE)
    keys = list(spec["clips"].keys())
    fw, fh = C.CANVAS
    label_h = 14
    rows = (len(keys) + columns - 1) // columns
    sheet = Image.new("RGBA", (columns * fw, rows * (fh + label_h)), (58, 58, 58, 255))
    dr = ImageDraw.Draw(sheet)
    for i, key in enumerate(keys):
        e = spec["clips"][key]
        # 取第二个 canonical 档位（8 方向下 front_side_r，三分之四侧视，比正面更易读）；无方向变体时退回整身默认朝向
        d = clip_dir(assets_out, e["resource_ref"], spec["directions"]["canonical"][1])
        if not (d / "frames.json").is_file():
            d = clip_dir(assets_out, e["resource_ref"])
        doc = _load(d / "frames.json")
        n = len(doc["frames"])
        hit = _ev(e, "hit")
        if hit:
            acc, idx = 0.0, n - 1
            for j, f in enumerate(doc["frames"]):
                acc += f["duration"] * 1000.0
                if acc / e["total_ms"] >= hit[0]:
                    idx = j
                    break
        elif e["loop"]:
            idx = n // 4
        else:
            idx = n // 2
        f = doc["frames"][idx]
        with Image.open(d / "atlas.png") as im:
            cell = im.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])).convert("RGBA")
        x, y = (i % columns) * fw, (i // columns) * (fh + label_h)
        sheet.alpha_composite(cell, (x, y + label_h))
        dr.text((x + 2, y + 1), key, fill=(235, 235, 235, 255))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path)
    return out_path


def print_report(r: Report, log=print) -> int:
    for w in r.warnings:
        log(f"[WARN] {w}")
    for e in r.errors:
        log(f"[ERROR] {e}")
    summary = "，".join(f"{k} {v}" for k, v in sorted(r.counts.items()))
    log(f"自检完成：错误 {len(r.errors)}，警告 {len(r.warnings)}（{summary}）")
    return 0 if not r.errors else 1
