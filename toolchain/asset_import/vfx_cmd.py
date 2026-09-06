"""``vfx`` 子命令：序列帧目录 -> 图集(``atlas.png``) + ``frames.json`` + vfx.def 行。

字段以 ``presentation/vfx_sfx/schema/VfxSfxSchemas.cs`` 登记的 ``vfx.def`` schema 为准：
``id``/``category``/``attach_mode``（必填）、``lifetime``（可选 Number）、``resource_ref``
（必填 Id）。``frames.json`` 结构（``frame_w``/``frame_h``/``fps``/``frame_duration``/``loop``/
``frames:[{index,x,y,w,h,duration}]``）与运行时 ``ResourceKind.Effect`` 加载器
（``adapters/unity/.../UnityResourceLoader.cs`` ``TryDecodeEffect``）对齐，示例见
``assets/_placeholder/vfx/burn/frames.json``；不再写旧版 ``atlas.json``。``--loop`` 且未显式
传 ``--lifetime`` 时不写 ``lifetime``（循环特效没有固有时长）；非循环时仍按 帧数/fps 推算。
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image

from .atlas import pack_atlas
from .common import (
    find_repo_root,
    log,
    merge_write_row,
    resolve_root,
    strip_domain,
    validate_id,
    write_json_pretty,
)

ATTACH_MODE_CHOICES = ["world", "anchor", "socket", "screen"]


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("src", help="序列帧源目录（按文件名排序的一组 .png）")
    parser.add_argument("--dataset", default="_sample", help="目标数据集名，默认 _sample")
    parser.add_argument("--id", required=True, help="vfx.def 记录 id，如 vfx.fire_impact")
    parser.add_argument("--category", default="generic", help="特效类别，自由字符串，默认 generic")
    parser.add_argument(
        "--attach-mode",
        choices=ATTACH_MODE_CHOICES,
        default="world",
        help=(
            "挂点类型，默认 world；world=按世界坐标播放，anchor=挂接到 sprite 型锚点跟随，"
            "socket=挂接到 model 型挂点跟随，screen=按屏幕空间坐标播放"
        ),
    )
    parser.add_argument(
        "--lifetime",
        type=float,
        default=None,
        help="生命周期（秒）；省略且非 --loop 时用帧数/fps 推算，省略且 --loop 时不写该字段",
    )
    parser.add_argument("--fps", type=float, default=24.0, help="序列帧播放帧率，默认 24")
    parser.add_argument(
        "--loop", action="store_true", help="写入 frames.json 的 loop=true（循环播放的特效，如持续光环）"
    )
    parser.add_argument("--assets-root", default=None, help="资产根目录，默认仓库 assets/")
    parser.add_argument("--data-root", default=None, help="数据根目录，默认仓库 data/")
    parser.add_argument("--dry-run", action="store_true", help="只打印计划，不写任何文件")


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    assets_root = resolve_root(args.assets_root, repo_root, "assets")
    data_root = resolve_root(args.data_root, repo_root, "data")

    src_dir = Path(args.src).resolve()
    if not src_dir.is_dir():
        raise FileNotFoundError(f"源目录不存在: {src_dir}")

    validate_id(args.id, "vfx", "--id")
    name = strip_domain(args.id).replace(".", "_")

    frame_paths = sorted(src_dir.glob("*.png"))
    if not frame_paths:
        raise FileNotFoundError(f"源目录 '{src_dir}' 下没有任何 .png 序列帧")

    frames = {p.stem: Image.open(p).convert("RGBA") for p in frame_paths}
    atlas_img, frame_rects = pack_atlas(frames)

    # --loop 且未显式给 --lifetime 时不写该字段（循环特效没有固有时长）；
    # 非循环时（或显式给了 --lifetime）沿用旧规则：显式值优先，否则按 帧数/fps 推算。
    if args.loop and args.lifetime is None:
        lifetime = None
    else:
        lifetime = args.lifetime if args.lifetime is not None else round(len(frame_paths) / args.fps, 4)
    resource_ref = f"vfx.{name}"

    out_dir = assets_root / args.dataset / "vfx" / name
    atlas_png_path = out_dir / "atlas.png"
    frames_json_path = out_dir / "frames.json"

    frame_duration = round(1.0 / args.fps, 6) if args.fps > 0 else 0.0
    first_rect = frame_rects[frame_paths[0].stem]
    frames_list: list[dict] = []
    for idx, p in enumerate(frame_paths):
        rect = frame_rects[p.stem]
        frames_list.append(
            {
                "index": idx,
                "x": rect["x"],
                "y": rect["y"],
                "w": rect["w"],
                "h": rect["h"],
                "duration": frame_duration,
            }
        )
    frames_data = {
        "frame_w": first_rect["w"],
        "frame_h": first_rect["h"],
        "fps": args.fps,
        "frame_duration": frame_duration,
        "loop": bool(args.loop),
        "frames": frames_list,
    }

    row: dict = {
        "id": args.id,
        "category": args.category,
        "attach_mode": args.attach_mode,
    }
    if lifetime is not None:
        row["lifetime"] = lifetime
    row["resource_ref"] = resource_ref

    vfx_def_path = data_root / args.dataset / "vfx" / "vfx.def.json"

    if args.dry_run:
        log(f"计划写出特效图集: {atlas_png_path} ({len(frame_paths)} 帧)", dry_run=True)
        log(f"计划写出特效帧数据: {frames_json_path}", dry_run=True)
        log(f"计划合并写入 vfx.def 行: {row['id']} -> {vfx_def_path}", dry_run=True)
        return 0

    out_dir.mkdir(parents=True, exist_ok=True)
    atlas_img.save(atlas_png_path)
    write_json_pretty(frames_json_path, frames_data)

    merge_write_row(vfx_def_path, "vfx.def", row, key_field="id")

    log(f"已写出特效图集: {out_dir}")
    log(f"已合并写入 vfx.def 行: {row['id']}")
    return 0
