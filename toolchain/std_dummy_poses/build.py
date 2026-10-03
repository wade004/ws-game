"""生成：序列帧（图集 + frames.json）、display.anim_set 数据行、规格文件 std_dummy_poses.json。

目录布局（与 ADR-0025/0038/0054/0072/0093 一致，路径推导复用 toolchain/asset_import/ref_conventions）：

  <assets_out>/sprite_anim/std_dummy_<键>/                     整身合成（默认朝向 front）；资源引用 sprite_anim.std_dummy_<键>
  <assets_out>/sprite_anim/std_dummy_<键>__<方向>/             整身合成的方向变体（ADR-0093，仅 canonical 右侧档，左侧靠翻转）
  <assets_out>/sprite_anim/std_dummy_<键>__<方向>__body/       身体层逐层剪辑（ADR-0072 第一级：方向+层名）
  <assets_out>/sprite_anim/std_dummy_<键>__<方向>__hand_main/  武器层逐层剪辑（1h/2h 族剪辑，以及无族的 hit.*/death/jump/cast/dodge——武器随身体帧走）
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


def clip_entry(c: C.ClipDef, fps: int) -> dict:
    """规格里一个剪辑键的条目（主集与体量组同一写法）。"""
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
        "layers": [C.LAYER_BODY] + ([C.LAYER_WEAPON] if c.weapon_layer_family else []),
        "weapon_layer_family": c.weapon_layer_family,
        "events": clip_events(c),
    }
    if c.alias_of:
        entry["alias_of"] = c.alias_of
    if c.gait:
        step = C.step_displacement_bh(c.gait, c.stride_factor)
        entry["step_displacement_bh"] = round(step, 4)
        entry["cycle_displacement_bh"] = round(step * 2, 4)
    # 手感落地 M3-D 追加字段：只在新键上出现（既有键的规格条目逐字节不变）
    if c.variant:
        entry["variant"] = c.variant
    if c.transition:
        entry["transition"] = c.transition
    if c.stride_factor != 1.0:
        entry["stride_factor"] = c.stride_factor
    if c.air:
        entry["air"] = True
    return entry


def build_spec(direction_count: int, fps: int, composite_dirs: bool) -> dict:
    clips = C.build_clip_defs()
    slots = C.canonical_slots(direction_count)
    spec_clips = {c.key: clip_entry(c, fps) for c in clips}
    anim_set_id = f"display.anim_set.{C.ANIM_SET_NAME}"
    mass_groups = [{"id": C.mass_anim_set_id(anim_set_id, m), "mass": m, "extends": anim_set_id,
                    "clips": {c.key: clip_entry(c, fps) for c in C.mass_clip_defs(m)}} for m in C.MASS_TIERS]
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
            "sprint_speed_ratio": C.SPRINT_SPEED_RATIO,
            "frame_count_rule": "每相 max(1, floor(ms*fps/1000+0.5))；相内帧时长均分（相边界与三相毫秒数精确对齐）",
        },
        "directions": {
            "canonical": slots,
            "yaw_deg": C.slot_yaw_deg(direction_count),
            "mirror_pairs": C.mirror_pairs(direction_count),
        },
        "clips": spec_clips,
        # 体量档（M4-D）：主档 = 中体量 = 主集，其余档逐档一组、覆盖全部键（含别名键）；偏移表见 config.MASS_TIERS
        "mass_tiers": {"main": C.MASS_MAIN_TIER, "tiers": {m: dict(v) for m, v in C.MASS_TIERS.items()},
                       "react_cap": C.MASS_REACT_CAP, "composite_direction_variants": composite_dirs},
        "mass_groups": mass_groups,
    }


def _clip_row_entry(e: dict) -> dict:
    return {"resource_ref": e["resource_ref"], "events": e["events"]}


def build_data_row(spec: dict) -> dict:
    return {"id": spec["anim_set_id"], "clips": {k: _clip_row_entry(e) for k, e in spec["clips"].items()}}


def build_mass_data_rows(spec: dict) -> list[dict]:
    """体量档数据行：主档（中）= 主集本身，给一个空覆盖行（``extends`` 主集、无 ``clips``），使三档对游戏同形；
    其余档逐档一行，覆盖全部键（含别名键），``extends`` 主集（04 §7）。"""
    main = spec["anim_set_id"]
    rows = [{"id": C.mass_anim_set_id(main, spec["mass_tiers"]["main"]), "extends": main, "clips": {}}]
    for g in spec["mass_groups"]:
        rows.append({"id": g["id"], "extends": g["extends"],
                     "clips": {k: _clip_row_entry(e) for k, e in g["clips"].items()}})
    return rows


def _render_anim_set_file(rows: list[dict]) -> str:
    """display.anim_set.json：外层 2 空格缩进，每个剪辑一行（与 data/_sample 同风格，字段顺序按 schema：id, [extends], clips）。"""
    lines = ['{', '  "table": "display.anim_set",', '  "schema_version": 1,', '  "rows": [']
    for r, row in enumerate(rows):
        lines += ['    {', f'      "id": {json.dumps(row["id"])},']
        if row.get("extends"):
            lines.append(f'      "extends": {json.dumps(row["extends"])},')
        lines.append('      "clips": {')
        items = list(row["clips"].items())
        for i, (key, val) in enumerate(items):
            ev = ", ".join('{ "name": %s, "time_pct": %s }' % (json.dumps(e["name"]), json.dumps(e["time_pct"]))
                           for e in val["events"])
            ev = f"[{ev}]" if ev else "[]"
            comma = "," if i < len(items) - 1 else ""
            blend = f', "blend_ms": {json.dumps(val["blend_ms"])}' if "blend_ms" in val else ""
            lines.append(f'        {json.dumps(key)}: {{ "resource_ref": {json.dumps(val["resource_ref"])}, "events": {ev}{blend} }}{comma}')
        blends = row.get("blends")
        if blends:
            lines += ['      },', '      "blends": [']
            for i, b in enumerate(blends):
                comma = "," if i < len(blends) - 1 else ""
                lines.append('        { "from": %s, "to": %s, "blend_ms": %s }%s'
                             % (json.dumps(b["from"]), json.dumps(b["to"]), json.dumps(b["blend_ms"]), comma))
            lines.append('      ]')
        else:
            lines.append('      }')
        lines.append('    }' + ("," if r < len(rows) - 1 else ""))
    lines += ['  ]', '}']
    return "\n".join(lines) + "\n"


def write_anim_set_rows(data_out: Path, new_rows: list[dict]) -> Path:
    """把 ``new_rows`` 并入 display.anim_set.json：同 id 的行替换，别的行原样保留，按 id 排序输出（确定性）。

    sprite 型与 model 型两个生成器各写自己的一行到同一个文件，互不覆盖对方；已有行若带 id/extends/clips/blends 之外的字段则拒绝改写
    （本写法不认识那些字段，改写会丢数据）。"""
    path = data_out / "display" / "display.anim_set.json"
    rows: dict[str, dict] = {}
    if path.is_file():
        for row in json.loads(path.read_text(encoding="utf-8")).get("rows", []):
            extra = set(row) - {"id", "extends", "clips", "blends"}
            if extra:
                raise ValueError(f"{path} 里的行 {row.get('id')} 带有本写法不认识的字段 {sorted(extra)}，拒绝改写")
            rows[row["id"]] = row
    for row in new_rows:
        rows[row["id"]] = row
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(_render_anim_set_file([rows[k] for k in sorted(rows)]), encoding="utf-8", newline="\n")
    return path


def write_data_file(data_out: Path, spec: dict) -> Path:
    """sprite 版数据行并入 display.anim_set.json（见 write_anim_set_rows）。"""
    return write_anim_set_rows(data_out, [build_data_row(spec)] + build_mass_data_rows(spec))


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
    n_dirs = _write_clip_set(assets_out, list(clips.values()), slots, slot_yaw, fps, composite_dirs)
    # 体量组（M4-D 起每档全部键；M4-W5 起整身合成同样出各方向变体，与主集同一开关）：体积估算与取舍见 README 判断记录 11。
    n_mass = 0
    for m in C.MASS_TIERS:
        n_mass += _write_clip_set(assets_out, C.mass_clip_defs(m), slots, slot_yaw, fps, composite_dirs)
    _write_json(assets_out / SPEC_FILE, spec)
    data_path = write_data_file(data_out, spec)
    n_res = sum(1 for c in clips.values() if not c.alias_of)
    log(f"生成完成：{len(clips)} 个剪辑键（{n_res} 份资源）+ {len(C.MASS_TIERS)} 个体量组（各 {n_res} 份资源），"
        f"{n_dirs + n_mass} 个资源目录（体量组 {n_mass}），方向档 {direction_count}（canonical {len(slots)} 档），fps={fps}")
    log(f"规格：{(assets_out / SPEC_FILE).as_posix()}；数据行：{data_path.as_posix()}")
    return spec


def _write_clip_set(assets_out: Path, clips: list, slots: list, slot_yaw: dict, fps: int, composite_dirs: bool) -> int:
    n_dirs = 0
    for c in clips:
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
            if c.weapon_layer_family:
                # 无族状态剪辑的姿势没有持握角：武器层用统一的携带俯仰角（见 config.STATE_CLIP_WEAPON_PITCH）
                wposes = poses if c.has_weapon else [dict(p, wp=C.STATE_CLIP_WEAPON_PITCH) for p in poses]
                wp = [render(p, yaw, c.weapon_layer_family, "weapon", clamp_weapon_to_ground=not c.has_weapon)
                      for p in wposes]
                _write_clip(clip_dir(assets_out, c.resource_ref, slot, C.LAYER_WEAPON), wp, durations_s, c.loop, fps)
                n_dirs += 1
    return n_dirs
