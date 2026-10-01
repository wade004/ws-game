"""生成：序列帧（图集 + frames.json）、display.anim_set 数据行、规格文件 std_dummy_poses.json。

目录布局（与 ADR-0025/0038/0054/0072/0093 一致，路径推导复用 toolchain/asset_import/ref_conventions）：

  <assets_out>/sprite_anim/std_dummy_<键>/                     整身合成（默认朝向 front）；资源引用 sprite_anim.std_dummy_<键>
  <assets_out>/sprite_anim/std_dummy_<键>__<方向>/             整身合成的方向变体（ADR-0093，仅 canonical 右侧档，左侧靠翻转）
  <assets_out>/sprite_anim/std_dummy_<键>__<方向>__body/       身体层逐层剪辑（ADR-0072 第一级：方向+层名）
  <assets_out>/sprite_anim/std_dummy_<键>__<方向>__hand_main/  武器层逐层剪辑（仅 1h/2h 族剪辑）
  <assets_out>/std_dummy_poses.json                            规格（参数、每键相位/帧数/事件/位移、清单）
  <data_out>/display/display.anim_set.json                     框架级数据行
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path

from PIL import Image

from . import config as C
from .poses import pose_at
from .skeleton import render

SPEC_FILE = "std_dummy_poses.json"
ATLAS_MAX_WIDTH = 2048


def _strip_category(ref: str) -> str:
    return ref.partition(".")[2].replace(".", "_")


def clip_dir(assets_out: Path, resource_ref: str, direction: str | None = None, layer: str | None = None) -> Path:
    """sprite_anim 资源目录。名称规则：<去类别前缀的引用>[__方向][__层]（ADR-0038 决策 2、ADR-0072、ADR-0093）。"""
    name = _strip_category(resource_ref)
    if direction:
        name += f"__{direction}"
    if layer:
        name += f"__{layer}"
    return assets_out / "sprite_anim" / name


def frame_times(clip: C.ClipDef, fps: int) -> list[tuple[float, float, float]]:
    """[(帧起点 ms, 帧时长 ms, 取样时刻 ms)]。循环剪辑在帧起点取样（第 0 帧即接触/起点姿势），
    非循环剪辑在帧中点取样。"""
    out = []
    t = 0.0
    for _name, n, dur in C.phase_frame_plan(clip, fps):
        for _i in range(n):
            sample = t if clip.loop else t + dur / 2.0
            out.append((t, dur, sample))
            t += dur
    return out


def _pack_frames(frames: list[Image.Image]):
    fw, fh = C.CANVAS
    cols = max(1, ATLAS_MAX_WIDTH // fw)
    rows = (len(frames) + cols - 1) // cols
    atlas = Image.new("RGBA", (min(len(frames), cols) * fw, rows * fh), (0, 0, 0, 0))
    rects = []
    for i, im in enumerate(frames):
        x, y = (i % cols) * fw, (i // cols) * fh
        atlas.paste(im, (x, y))
        rects.append((x, y))
    return atlas, rects


def _write_json(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def _write_clip(out_dir: Path, frames: list[Image.Image], durations_s: list[float], loop: bool, fps: int) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    atlas, rects = _pack_frames(frames)
    atlas.save(out_dir / "atlas.png", format="PNG", optimize=True)
    fw, fh = C.CANVAS
    doc = {
        "frame_w": fw,
        "frame_h": fh,
        "fps": fps,
        "frame_duration": round(1.0 / fps, 6),
        "loop": loop,
        "frames": [
            {"index": i, "x": x, "y": y, "w": fw, "h": fh, "duration": durations_s[i]}
            for i, (x, y) in enumerate(rects)
        ],
    }
    _write_json(out_dir / "frames.json", doc)


def event_pct(ms: float, total_ms: float) -> float:
    return round(ms / total_ms, 4)


def clip_events(clip: C.ClipDef) -> list[dict]:
    total = float(clip.total_ms)
    return [{"name": n, "time_pct": event_pct(ms, total)} for n, ms in C.events_for(clip)]


def build_spec(direction_count: int, fps: int, composite_dirs: bool) -> dict:
    clips = C.build_clip_defs()
    slots = C.canonical_slots(direction_count)
    spec_clips = {}
    for c in clips:
        plan = C.phase_frame_plan(c, fps)
        entry = {
            "resource_ref": c.resource_ref,
            "tier": c.tier,
            "family": c.family,
            "loop": c.loop,
            "phases": [{"name": n, "ms": ms} for n, ms in c.phases],
            "frames_per_phase": [n for _n, n, _d in plan],
            "frame_count": sum(n for _n, n, _d in plan),
            "total_ms": c.total_ms,
            "layers": [C.LAYER_BODY] + ([C.LAYER_WEAPON] if c.has_weapon else []),
            "events": clip_events(c),
        }
        if c.alias_of:
            entry["alias_of"] = c.alias_of
        if c.gait:
            step = C.step_displacement_bh(c.gait)
            entry["step_displacement_bh"] = round(step, 4)
            entry["cycle_displacement_bh"] = round(step * 2, 4)
        spec_clips[c.key] = entry
    return {
        "generator": "toolchain/gen_std_dummy_poses.py",
        "doc": "architecture/手感设计/04_姿势与动画契约.md 第 6.2 节",
        "anim_set_id": f"display.anim_set.{C.ANIM_SET_NAME}",
        "params": {
            "fps": fps,
            "frame_ms": round(1000.0 / fps, 6),
            "direction_count": direction_count,
            "composite_direction_variants": composite_dirs,
            "canvas": list(C.CANVAS),
            "pixels_per_unit": C.PIXELS_PER_UNIT,
            "body_height_px": C.BODY_HEIGHT_PX,
            "root_px": list(C.ROOT_PX),
            "layers": {"body": C.LAYER_BODY, "weapon": C.LAYER_WEAPON},
            "reference_base_speed_body_heights_per_s": C.REFERENCE_BASE_SPEED_BH_PER_S,
            "walk_speed_ratio": C.WALK_SPEED_RATIO,
            "run_speed_ratio": C.RUN_SPEED_RATIO,
            "frame_count_rule": "每相 max(1, floor(ms*fps/1000+0.5))；相内帧时长均分（相边界与三相毫秒数精确对齐）",
        },
        "directions": {
            "canonical": slots,
            "yaw_deg": C.slot_yaw_deg(direction_count),
            "mirror_pairs": C.mirror_pairs(direction_count),
        },
        "clips": spec_clips,
    }


def build_data_row(spec: dict) -> dict:
    clips = {}
    for key, e in spec["clips"].items():
        clips[key] = {"resource_ref": e["resource_ref"], "events": e["events"]}
    return {"id": spec["anim_set_id"], "clips": clips}


def write_data_file(data_out: Path, spec: dict) -> Path:
    """display.anim_set.json：外层 2 空格缩进，每个剪辑一行（与 data/_sample 同风格，字段顺序按 schema：id, clips）。"""
    row = build_data_row(spec)
    lines = ['{', '  "table": "display.anim_set",', '  "schema_version": 1,', '  "rows": [', '    {',
             f'      "id": {json.dumps(row["id"])},', '      "clips": {']
    items = list(row["clips"].items())
    for i, (key, val) in enumerate(items):
        ev = ", ".join('{ "name": %s, "time_pct": %s }' % (json.dumps(e["name"]), json.dumps(e["time_pct"]))
                       for e in val["events"])
        ev = f"[{ev}]" if ev else "[]"
        comma = "," if i < len(items) - 1 else ""
        lines.append(f'        {json.dumps(key)}: {{ "resource_ref": {json.dumps(val["resource_ref"])}, "events": {ev} }}{comma}')
    lines += ['      }', '    }', '  ]', '}']
    path = data_out / "display" / "display.anim_set.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return path


def clean_outputs(assets_out: Path) -> int:
    """只删 sprite_anim/std_dummy_* 目录与规格文件，不碰其它任何内容。"""
    removed = 0
    root = assets_out / "sprite_anim"
    if root.is_dir():
        for d in sorted(root.iterdir()):
            if d.is_dir() and d.name.startswith(C.STEM_PREFIX):
                shutil.rmtree(d)
                removed += 1
    spec = assets_out / SPEC_FILE
    if spec.is_file():
        spec.unlink()
        removed += 1
    return removed


def generate(assets_out: Path, data_out: Path, direction_count: int = C.DEFAULT_DIRECTION_COUNT,
             fps: int = C.FPS, composite_dirs: bool = True, clean: bool = False, log=print) -> dict:
    if clean:
        log(f"--clean：删除 {clean_outputs(assets_out)} 项（仅 sprite_anim/{C.STEM_PREFIX}* 与规格文件）")
    spec = build_spec(direction_count, fps, composite_dirs)
    clips = {c.key: c for c in C.build_clip_defs()}
    slot_yaw = C.slot_yaw_deg(direction_count)
    slots = C.canonical_slots(direction_count)
    n_dirs = 0
    for key, c in clips.items():
        if c.alias_of:
            continue
        times = frame_times(c, fps)
        durations_s = [round(dur / 1000.0, 6) for _t0, dur, _s in times]
        weapon = c.family if c.has_weapon else None
        poses = [pose_at(c, s) for _t0, _d, s in times]
        # 整身合成（front）：sprite_anim.std_dummy_<键>
        flat = [render(p, slot_yaw["front"], weapon) for p in poses]
        _write_clip(clip_dir(assets_out, c.resource_ref), flat, durations_s, c.loop, fps)
        n_dirs += 1
        for slot in slots:
            yaw = slot_yaw[slot]
            if composite_dirs:
                imgs = flat if slot == "front" else [render(p, yaw, weapon) for p in poses]
                _write_clip(clip_dir(assets_out, c.resource_ref, slot), imgs, durations_s, c.loop, fps)
                n_dirs += 1
            body = [render(p, yaw, weapon, "body") for p in poses]
            _write_clip(clip_dir(assets_out, c.resource_ref, slot, C.LAYER_BODY), body, durations_s, c.loop, fps)
            n_dirs += 1
            if weapon:
                wp = [render(p, yaw, weapon, "weapon") for p in poses]
                _write_clip(clip_dir(assets_out, c.resource_ref, slot, C.LAYER_WEAPON), wp, durations_s, c.loop, fps)
                n_dirs += 1
    _write_json(assets_out / SPEC_FILE, spec)
    data_path = write_data_file(data_out, spec)
    log(f"生成完成：{len(clips)} 个剪辑键（{sum(1 for c in clips.values() if not c.alias_of)} 份资源），"
        f"{n_dirs} 个资源目录，方向档 {direction_count}（canonical {len(slots)} 档），fps={fps}")
    log(f"规格：{(assets_out / SPEC_FILE).as_posix()}；数据行：{data_path.as_posix()}")
    return spec
