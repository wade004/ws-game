"""组装：裸 RGBA 帧 -> 图集 + frames.json（沿用 std_dummy_poses 的打包规则与目录命名）、display.anim_set 数据行、规格文件。

目录布局（与 std_dummy_poses.build 同形，ADR-0038/0072/0093；<stem> = 配置的 stem_prefix + [体量_] + 键点号换下划线）：

  <assets_out>/sprite_anim/<stem>/                  整身合成（默认朝向 front）；资源引用 sprite_anim.<stem>
  <assets_out>/sprite_anim/<stem>__<方向>/            整身合成的方向变体（仅 canonical 右侧档，左侧靠运行期翻转）
  <assets_out>/sprite_anim/<stem>__<方向>__body/      身体层逐层剪辑（有装备层时才出）
  <assets_out>/sprite_anim/<stem>__<方向>__<层名>/    装备层逐层剪辑（有装备层时才出）
  <assets_out>/<name>.prerender.json               规格（配置、每个资源的帧数/取样时刻/事件、键到资源的映射）
  <data_out>/display/display.anim_set.json         数据行（只替换同 id 的行，别的行原样保留）
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path

from PIL import Image
from asset_import import directions
from std_dummy_poses import build as SB

from .config import BODY_LAYER
from .plan import SPEC_SUFFIX, RenderPlan, anim_set_rows, build_spec


def raw_name(source_clip: str, slot: str, variant: str) -> str:
    return f"{source_clip}__{slot}__{variant}.rgba"


def read_raw_frames(path: Path, canvas: tuple, expected_frames: int) -> list[Image.Image]:
    w, h = canvas
    data = path.read_bytes()
    fb = w * h * 4
    if len(data) != fb * expected_frames:
        raise ValueError(f"{path.name} 大小 {len(data)} 字节，应为 {expected_frames} 帧 × {w}×{h}×4 = {fb * expected_frames}")
    return [Image.frombytes("RGBA", (w, h), data[i * fb:(i + 1) * fb]) for i in range(expected_frames)]


def clip_dir_name(stem: str, slot: str | None = None, layer: str | None = None) -> str:
    name = stem
    if slot:
        name += f"__{slot}"
    if layer:
        name += f"__{layer}"
    return name


def clean_outputs(plan: RenderPlan, assets_out: Path) -> int:
    """只删本次计划涉及的资源目录（<stem> 及 <stem>__*）与本集规格文件，不碰其它任何内容。"""
    removed = 0
    root = assets_out / "sprite_anim"
    stems = {cp.stem for cp in plan.clips}
    if root.is_dir():
        for d in sorted(root.iterdir()):
            if d.is_dir() and any(d.name == s or d.name.startswith(s + "__") for s in stems):
                shutil.rmtree(d)
                removed += 1
    spec = assets_out / (plan.config.name + SPEC_SUFFIX)
    if spec.is_file():
        spec.unlink()
        removed += 1
    return removed


def write_assets(plan: RenderPlan, raw_dir: Path, assets_out: Path, clips: list | None = None) -> int:
    """逐 剪辑×方向×层 打包写盘，返回写出的资源目录数。``clips`` 给定时只处理这一块（分块渲染用）。"""
    cfg = plan.config
    default_slot = directions.default_direction_slot(cfg.direction_count)
    n_dirs = 0
    for cp in (plan.clips if clips is None else clips):
        durations = list(cp.durations_s)
        for slot in cfg.effective_slots():
            for variant in plan.variants():
                frames = read_raw_frames(raw_dir / raw_name(cp.source_clip, slot, variant), cfg.canvas, cp.frame_count)
                if variant == "all":
                    dirs = [clip_dir_name(cp.stem, slot)]
                    if slot == default_slot:
                        dirs.append(clip_dir_name(cp.stem))
                else:
                    dirs = [clip_dir_name(cp.stem, slot, variant)]
                for name in dirs:
                    SB._write_clip(assets_out / "sprite_anim" / name, frames, durations, cp.loop, cfg.fps, cfg.canvas)
                    n_dirs += 1
    return n_dirs


def write_spec(plan: RenderPlan, assets_out: Path) -> Path:
    path = assets_out / (plan.config.name + SPEC_SUFFIX)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(build_spec(plan), indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    return path


def write_data(plan: RenderPlan, data_out: Path) -> Path:
    return SB.write_anim_set_rows(data_out, anim_set_rows(plan))


def assemble(plan: RenderPlan, raw_dir: Path, assets_out: Path, data_out: Path, clean: bool = False, log=print) -> dict:
    if clean:
        log(f"--clean：删除 {clean_outputs(plan, assets_out)} 项（仅本次计划涉及的 sprite_anim/<stem>* 与规格文件）")
    n_dirs = write_assets(plan, raw_dir, assets_out)
    spec_path = write_spec(plan, assets_out)
    data_path = write_data(plan, data_out)
    log(f"组装完成：{len(plan.clips)} 份剪辑资源 × {len(plan.config.effective_slots())} 个方向档 × {len(plan.variants())} 个层变体，"
        f"{n_dirs} 个资源目录；规格 {spec_path.as_posix()}；数据行 {data_path.as_posix()}")
    return {"dirs": n_dirs, "spec": spec_path, "data": data_path}
