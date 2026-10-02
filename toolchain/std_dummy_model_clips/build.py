"""生成：规格文件 std_dummy_model_clips.json（骨架、可视块体、逐剪辑关键帧与时间标记）与 display.anim_set 数据行。

规格文件是 model 型假人集的**唯一机器可读来源**：引擎侧编辑器生成脚本（Assets/Editor/GenerateStdDummyModelAssets.cs）读它生成
预制体、动画控制器与 .anim 剪辑；自检读它核对数据行、帧数、事件、骨骼路径、关节角限与已入库的引擎资产。

关键帧规则：每个剪辑的关键帧取在 sprite 版帧边界上（相内帧均分，边界与三相毫秒数精确对齐），含终点，关键帧数 = sprite 版帧数 + 1；
循环剪辑的终点关键帧等于起点（首尾连续由自检断言）。时间标记（事件）与 sprite 版同一函数产出（``std_dummy_poses.build.clip_events``）。
"""

from __future__ import annotations

import json
from pathlib import Path

from std_dummy_poses import config as SC
from std_dummy_poses.build import clip_events, write_anim_set_rows
from std_dummy_poses.poses import pose_at

from . import config as C
from . import rig


def key_times_ms(clip: SC.ClipDef, fps: int) -> list[float]:
    """关键帧时刻（毫秒）：每相按 sprite 版帧数均分，含终点。"""
    times = [0.0]
    start = 0.0
    for _name, n, dur in SC.phase_frame_plan(clip, fps):
        for i in range(1, n + 1):
            times.append(round(start + dur * i, 6))
        start += dur * n
    times[-1] = float(clip.total_ms)
    return times


def effective_pose(clip: SC.ClipDef, t_ms: float) -> dict:
    """某时刻的姿势。无族状态剪辑（hit.*/death/jump/cast/dodge）与 sprite 版武器层同规则：武器朝向取固定携带俯仰角。"""
    p = pose_at(clip, t_ms)
    if clip.weapon_layer_family and not clip.has_weapon:
        p = dict(p, wp=SC.STATE_CLIP_WEAPON_PITCH)
    # 铰链关节（肘、膝）不允许反向过伸：sprite 版的受击/倒地姿势里肘角会甩到负值（二维剪影里看不出来），
    # 骨骼模型上就是手臂反折，所以 model 型在取样处夹到铰链范围；其它关节照 sprite 版原值。
    for name, (lo, hi) in C.HINGE_CLAMP.items():
        v = p[name]
        if v < lo or v > hi:
            p = dict(p, **{name: min(hi, max(lo, v))})
    # 体量组的偏移（SC.MASS_TIERS）已由 pose_at 施加（M4-D 起两版同一处）；偏移后的角度仍受 SOURCE_ANGLE_LIMITS 自检。
    return p


def anim_events(events: list[dict]) -> list[dict]:
    """烘进 .anim 的事件 = 数据行事件 + 引擎侧别名事件（同时刻，见 config.ENGINE_EVENT_ALIASES）。"""
    out = [dict(e) for e in events]
    for e in events:
        alias = C.ENGINE_EVENT_ALIASES.get(e["name"])
        # 数据行事件自己已带别名事件（同名同时刻，手感落地 M3-D 起 sprite 版同源写入 hit_frame）时不重复烘。
        if alias and not any(x["name"] == alias and abs(x["time_pct"] - e["time_pct"]) < 1e-9 for x in events):
            out.append({"name": alias, "time_pct": e["time_pct"]})
    return out


def _round_q(q: rig.Quat) -> list[float]:
    return [round(v + 0.0, C.ROT_DECIMALS) + 0.0 for v in q]


def clip_tracks(clip: SC.ClipDef, times: list[float]) -> list[dict]:
    """逐骨骼轨迹：旋转骨骼 -> {path, rot:[x,y,z,w,...]}；髋另有 {path, pos:[x,y,z,...]}（世界单位）。四元数保持前后半球连续。"""
    rot_series: dict[str, list[rig.Quat]] = {b: [] for b in C.ROT_BONES}
    pos_series: list[list[float]] = []
    for t in times:
        pose = effective_pose(clip, t)
        mats = rig.bone_matrices(pose)
        for b in C.ROT_BONES:
            q = rig.mat_to_quat(mats[b])
            seq = rot_series[b]
            if seq and rig.quat_dot(seq[-1], q) < 0.0:
                q = (-q[0], -q[1], -q[2], -q[3])
            seq.append(q)
        h = rig.hips_position_bh(pose)
        pos_series.append([round(v * C.BODY_HEIGHT_UNITS, C.POS_DECIMALS) + 0.0 for v in h])
    tracks: list[dict] = [{"path": C.bone_path("hips"), "pos": [v for p in pos_series for v in p]}]
    for b in C.ROT_BONES:
        tracks.append({"path": C.bone_path(b), "rot": [v for q in rot_series[b] for v in _round_q(q)]})
    return tracks


def build_skeleton() -> dict:
    s = C.BODY_HEIGHT_UNITS
    bones = [{"name": n, "path": C.bone_path(n), "parent": p, "pos": [round(v * s, C.POS_DECIMALS) + 0.0 for v in rest]}
             for n, p, rest in C.BONES]
    visuals = [{"bone": C.bone_path(b), "name": name,
                "center": [round(v * s, C.POS_DECIMALS) + 0.0 for v in center],
                "size": [round(v * s, C.POS_DECIMALS) + 0.0 for v in size]}
               for b, name, center, size in C.VISUALS]
    return {"root": C.ROOT_NAME, "bones": bones, "visuals": visuals,
            "rotation_bones": [C.bone_path(b) for b in C.ROT_BONES],
            "position_bones": [C.bone_path("hips")]}


def mass_clip_defs(mass: str) -> list[SC.ClipDef]:
    """体量组的剪辑定义：主集**全部**键（含别名键，别名目标同样指向组内剪辑，键名不变）各加体量标记（M4-D：覆盖全部键）。"""
    return SC.mass_clip_defs(mass)


def clip_entry(c: SC.ClipDef, fps: int, with_blend: bool = False) -> dict:
    entry: dict = {
        "key": c.key,
        "resource_ref": C.clip_resource_ref(c.key, c.alias_of, c.mass),
        "tier": c.tier,
        "family": c.family,
        "loop": c.loop,
        "phases": [{"name": n, "ms": ms} for n, ms in c.phases],
        "frames_per_phase": [n for _a, n, _d in SC.phase_frame_plan(c, fps)],
        "frame_count": sum(n for _a, n, _d in SC.phase_frame_plan(c, fps)),
        "total_ms": c.total_ms,
        "events": clip_events(c),
    }
    if with_blend:
        # 切入该键的交叉淡入时长（数据行 blend_ms，M4-D）；只写在主集行，体量组按键继承
        entry["blend_ms"] = SC.blend_ms_for(c)
    if c.alias_of:
        entry["alias_of"] = c.alias_of
    else:
        times = key_times_ms(c, fps)
        entry["state"] = C.clip_state_name(c.key, c.mass)
        entry["weapon_layer_family"] = c.weapon_layer_family
        entry["anim_events"] = anim_events(entry["events"])
        entry["times_ms"] = times
        entry["tracks"] = clip_tracks(c, times)
    if c.gait:
        step = SC.step_displacement_bh(c.gait, c.stride_factor)
        entry["step_displacement_bh"] = round(step, 4)
        entry["cycle_displacement_bh"] = round(step * 2, 4)
        entry["cycle_displacement_units"] = round(step * 2 * C.BODY_HEIGHT_UNITS, 4)
    return entry


def build_spec(fps: int = C.FPS) -> dict:
    clips = [clip_entry(c, fps, with_blend=True) for c in SC.build_clip_defs()]
    mass_groups = [{"id": C.mass_anim_set_id(m), "mass": m, "extends": C.ANIM_SET_ID,
                    "clips": [clip_entry(c, fps) for c in mass_clip_defs(m)]} for m in SC.MASS_TIERS]
    return {
        "generator": "toolchain/gen_std_dummy_model_clips.py",
        "doc": "architecture/手感设计/04_姿势与动画契约.md 第 6.1 节",
        "anim_set_id": C.ANIM_SET_ID,
        "model_ref": C.MODEL_REF,
        "frame_rate": fps,
        "params": {
            "fps": fps,
            "frame_ms": round(1000.0 / fps, 6),
            "body_height_units": C.BODY_HEIGHT_UNITS,
            "reference_base_speed_body_heights_per_s": SC.REFERENCE_BASE_SPEED_BH_PER_S,
            "walk_speed_ratio": SC.WALK_SPEED_RATIO,
            "run_speed_ratio": SC.RUN_SPEED_RATIO,
            "sprint_speed_ratio": SC.SPRINT_SPEED_RATIO,
            "frame_count_rule": "每相 max(1, floor(ms*fps/1000+0.5))；相内帧均分；关键帧取在帧边界上且含终点（关键帧数 = 帧数 + 1），与 sprite 版同一帧划分",
            "convention": C.CONVENTION,
            "bone_rot_limits_deg": C.BONE_ROT_LIMITS_DEG,
            "engine_event_aliases": C.ENGINE_EVENT_ALIASES,
            "mass_profiles_deg": C.mass_profiles(),
            "mass_tiers": {"main": SC.MASS_MAIN_TIER, "tiers": {m: dict(v) for m, v in SC.MASS_TIERS.items()},
                           "react_cap": SC.MASS_REACT_CAP},
        },
        "skeleton": build_skeleton(),
        "clips": clips,
        "blends": [{"from": a, "to": b, "blend_ms": ms} for a, b, ms in SC.BLEND_PAIRS],
        "mass_groups": mass_groups,
    }


# --------------------------------------------------------------------------
# 规格文件的紧凑 JSON 写法：数值数组一行，其余缩进（体积与 diff 可读性）
# --------------------------------------------------------------------------

def _is_scalar(v) -> bool:
    return v is None or isinstance(v, (bool, int, float, str))


def _dump(v, level: int, out: list[str]) -> None:
    pad = "  " * level
    if isinstance(v, dict):
        if not v:
            out.append("{}")
            return
        out.append("{\n")
        items = list(v.items())
        for i, (k, x) in enumerate(items):
            out.append(f"{pad}  {json.dumps(k, ensure_ascii=False)}: ")
            _dump(x, level + 1, out)
            out.append(",\n" if i < len(items) - 1 else "\n")
        out.append(f"{pad}}}")
    elif isinstance(v, list):
        if all(_is_scalar(x) for x in v):
            out.append("[" + ", ".join(json.dumps(x, ensure_ascii=False) for x in v) + "]")
            return
        if not v:
            out.append("[]")
            return
        out.append("[\n")
        for i, x in enumerate(v):
            out.append(f"{pad}  ")
            _dump(x, level + 1, out)
            out.append(",\n" if i < len(v) - 1 else "\n")
        out.append(f"{pad}]")
    else:
        out.append(json.dumps(v, ensure_ascii=False))


def dumps_spec(spec: dict) -> str:
    out: list[str] = []
    _dump(spec, 0, out)
    return "".join(out) + "\n"


def write_spec(assets_out: Path, spec: dict) -> Path:
    path = assets_out / C.SPEC_FILE
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(dumps_spec(spec), encoding="utf-8", newline="\n")
    return path


def _row_clip(e: dict) -> dict:
    out = {"resource_ref": e["resource_ref"], "events": e["events"]}
    if "blend_ms" in e:
        out["blend_ms"] = e["blend_ms"]
    return out


def build_data_row(spec: dict) -> dict:
    row = {"id": spec["anim_set_id"], "clips": {e["key"]: _row_clip(e) for e in spec["clips"]}}
    if spec.get("blends"):
        row["blends"] = [dict(b) for b in spec["blends"]]
    return row


def build_mass_data_rows(spec: dict) -> list[dict]:
    """体量档数据行：中体量（主档）= 主集本身，给空覆盖行；其余档 ``extends`` 主集，覆盖全部键（04 §7）。
    切入混合时长（blend_ms / blends）只在主集行声明，体量组按键继承（同键同值，不重复）。"""
    main = spec["anim_set_id"]
    rows = [{"id": f"{main}_{spec['params']['mass_tiers']['main']}", "extends": main, "clips": {}}]
    rows += [{"id": g["id"], "extends": g["extends"], "clips": {e["key"]: _row_clip(e) for e in g["clips"]}}
             for g in spec["mass_groups"]]
    return rows


def write_data_file(data_out: Path, spec: dict) -> Path:
    """把本行与体量组行并入 display.anim_set.json（与 sprite 版同一个文件，按 id 排序，不动别的行）。"""
    return write_anim_set_rows(data_out, [build_data_row(spec)] + build_mass_data_rows(spec))


def generate(assets_out: Path, data_out: Path, fps: int = C.FPS, log=print) -> dict:
    spec = build_spec(fps)
    spec_path = write_spec(assets_out, spec)
    data_path = write_data_file(data_out, spec)
    n_assets = sum(1 for e in spec["clips"] if "alias_of" not in e)
    n_mass = sum(1 for g in spec["mass_groups"] for e in g["clips"] if "alias_of" not in e)
    log(f"生成完成：{len(spec['clips'])} 个剪辑键（{n_assets} 份剪辑资产）+ {len(spec['mass_groups'])} 个体量组（{n_mass} 份剪辑资产），骨骼 {len(spec['skeleton']['bones'])} 根，fps={fps}")
    log(f"规格：{spec_path.as_posix()}；数据行：{data_path.as_posix()}")
    log("引擎侧资产（预制体/控制器/.anim）由编辑器生成脚本按规格生成："
        "Unity -batchmode -executeMethod Adapter.Unity.EditorTools.GenerateStdDummyModelAssets.GenerateAndExit")
    return spec
