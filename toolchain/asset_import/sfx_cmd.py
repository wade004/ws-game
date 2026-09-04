"""``sfx`` 子命令：音频文件 -> 复制到 assets/<dataset>/sfx/ + sfx.def 行。

只支持无压缩音频（标准库 ``wave`` 能读的 PCM wav），用于做采样率/时长的基本校验；
压缩格式（mp3/ogg 等）一律拒绝，提示改用 wav。

字段（本工具口径，架构文档同 vfx.def 一样只登记了表名，未展开字段；按任务口径固定为
``id``/``layer``/``priority``/``variants``，``variants`` 是
``[{resource_ref, duration_sec, sample_rate}]`` 列表——一个 sfx id 下可以有多个随机变体）。
"""

from __future__ import annotations

import argparse
import shutil
import wave
from pathlib import Path

from .common import (
    AssetImportError,
    find_repo_root,
    log,
    merge_write_row,
    resolve_root,
    strip_domain,
    validate_id,
)


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("src", nargs="+", help="音频源文件（.wav，无压缩 PCM），可传多个作为随机变体")
    parser.add_argument("--dataset", default="_sample", help="目标数据集名，默认 _sample")
    parser.add_argument("--id", required=True, help="sfx.def 记录 id，如 sfx.sword_hit")
    parser.add_argument("--layer", default="sfx", help="播放层，自由字符串，默认 sfx")
    parser.add_argument("--priority", type=int, default=0, help="播放优先级，默认 0")
    parser.add_argument("--assets-root", default=None, help="资产根目录，默认仓库 assets/")
    parser.add_argument("--data-root", default=None, help="数据根目录，默认仓库 data/")
    parser.add_argument("--dry-run", action="store_true", help="只打印计划，不写任何文件")


def _read_wav_info(path: Path) -> tuple[int, float]:
    """用标准库 wave 读取采样率与时长，做基本校验；非法/压缩格式抛 AssetImportError。"""
    try:
        with wave.open(str(path), "rb") as wf:
            framerate = wf.getframerate()
            nframes = wf.getnframes()
    except (wave.Error, EOFError) as exc:
        raise AssetImportError(
            f"音频文件 '{path}' 不是合法的无压缩 wav（标准库 wave 无法解析）: {exc}"
        ) from exc
    if framerate <= 0:
        raise AssetImportError(f"音频文件 '{path}' 采样率非法: {framerate}")
    duration = round(nframes / framerate, 4)
    return framerate, duration


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    assets_root = resolve_root(args.assets_root, repo_root, "assets")
    data_root = resolve_root(args.data_root, repo_root, "data")

    validate_id(args.id, "sfx", "--id")
    name = strip_domain(args.id).replace(".", "_")

    src_paths = [Path(p) for p in args.src]
    for p in src_paths:
        if not p.is_file():
            raise FileNotFoundError(f"音频源文件不存在: {p}")
        if p.suffix.lower() != ".wav":
            raise AssetImportError(f"音频源文件 '{p}' 后缀不是 .wav（只支持无压缩音频）")

    out_dir = assets_root / args.dataset / "sfx" / name

    variants = []
    plan_lines = []
    for idx, p in enumerate(src_paths):
        framerate, duration = _read_wav_info(p)
        out_name = f"v{idx}.wav"
        out_path = out_dir / out_name
        resource_ref = f"sfx.{name}_v{idx}"
        variants.append(
            {
                "resource_ref": resource_ref,
                "sample_rate": framerate,
                "duration_sec": duration,
            }
        )
        plan_lines.append((p, out_path))

    row = {
        "id": args.id,
        "layer": args.layer,
        "priority": args.priority,
        "variants": variants,
    }

    sfx_def_path = data_root / args.dataset / "sfx" / "sfx.def.json"

    if args.dry_run:
        for src, dst in plan_lines:
            log(f"计划复制音频: {src} -> {dst}", dry_run=True)
        log(f"计划合并写入 sfx.def 行: {row['id']} -> {sfx_def_path}", dry_run=True)
        return 0

    out_dir.mkdir(parents=True, exist_ok=True)
    for src, dst in plan_lines:
        shutil.copy2(src, dst)

    merge_write_row(sfx_def_path, "sfx.def", row, key_field="id")

    log(f"已复制 {len(plan_lines)} 个音频变体到: {out_dir}")
    log(f"已合并写入 sfx.def 行: {row['id']}")
    return 0
