"""``vfx`` 子命令：序列帧目录 -> 图集 + vfx.def 行。

字段（本工具口径，架构文档 04/09 只登记了 ``vfx.def`` 表名与 display.map 的
``vfx_id`` 外键指向它，未展开字段；字段集合按任务口径固定为
``id``/``category``/``attach_mode``/``lifetime``/``resource_ref``）。
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

ATTACH_MODE_CHOICES = ["root", "socket", "world_fixed"]


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("src", help="序列帧源目录（按文件名排序的一组 .png）")
    parser.add_argument("--dataset", default="_sample", help="目标数据集名，默认 _sample")
    parser.add_argument("--id", required=True, help="vfx.def 记录 id，如 vfx.fire_impact")
    parser.add_argument("--category", default="generic", help="特效类别，自由字符串，默认 generic")
    parser.add_argument(
        "--attach-mode", choices=ATTACH_MODE_CHOICES, default="root", help="挂点类型，默认 root"
    )
    parser.add_argument("--lifetime", type=float, default=None, help="生命周期（秒）；省略则用帧数/fps 推算")
    parser.add_argument("--fps", type=float, default=24.0, help="序列帧播放帧率，默认 24")
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

    lifetime = args.lifetime if args.lifetime is not None else round(len(frame_paths) / args.fps, 4)
    resource_ref = f"vfx.{name}"

    out_dir = assets_root / args.dataset / "vfx" / name
    atlas_png_path = out_dir / "atlas.png"
    atlas_json_path = out_dir / "atlas.json"

    row = {
        "id": args.id,
        "category": args.category,
        "attach_mode": args.attach_mode,
        "lifetime": lifetime,
        "resource_ref": resource_ref,
    }

    vfx_def_path = data_root / args.dataset / "vfx" / "vfx.def.json"

    if args.dry_run:
        log(f"计划写出特效图集: {atlas_png_path} ({len(frame_paths)} 帧)", dry_run=True)
        log(f"计划写出特效图集索引: {atlas_json_path}", dry_run=True)
        log(f"计划合并写入 vfx.def 行: {row['id']} -> {vfx_def_path}", dry_run=True)
        return 0

    out_dir.mkdir(parents=True, exist_ok=True)
    atlas_img.save(atlas_png_path)
    write_json_pretty(atlas_json_path, {"fps": args.fps, "frames": frame_rects})

    merge_write_row(vfx_def_path, "vfx.def", row, key_field="id")

    log(f"已写出特效图集: {out_dir}")
    log(f"已合并写入 vfx.def 行: {row['id']}")
    return 0
