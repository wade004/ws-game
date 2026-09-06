"""``sfx`` 子命令：音频文件 -> 复制到 assets/<dataset>/sfx/ + sfx.def 行。

只支持无压缩音频（标准库 ``wave`` 能读的 PCM wav），用于做采样率/时长的基本校验；
压缩格式（mp3/ogg 等）一律拒绝，提示改用 wav。

字段以 ``presentation/vfx_sfx/schema/VfxSfxSchemas.cs`` 登记的 ``sfx.def`` schema 为准：
``id``/``layer``（必填）、``priority``（可选 Int）、``variants``（可选 Id 列表）、
``resource_ref``（必填 Id）。本命令固定把 ``resource_ref`` 写成第一个源文件（``v0``）对应的
引用；只有传入 >= 2 个源文件（多个随机变体）时才写 ``variants``（含 ``resource_ref`` 本身在内
的全部变体 Id 列表），单文件不写 ``variants``。采样率/时长只用于本命令自身的 PCM 合法性校验，
打印到日志，不再落进数据表字段（schema 未登记这两个字段）。资产落地为扁平文件
``assets/<dataset>/sfx/<name>_v<N>.wav``（不再是子目录），与运行时 ``ResourceKind.Audio``
的解析规则 ``audio/<资源引用 id 去掉 'sfx.'>.wav`` 对齐（见 ``UnityResourceLoader``）。
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

    # 扁平输出：assets/<dataset>/sfx/<name>_v<N>.wav（不再是 sfx/<name>/v<N>.wav 子目录），
    # 与运行时 ResourceKind.Audio "audio/<资源引用 id 去掉 'sfx.'>.wav" 的扁平解析规则对齐。
    out_dir = assets_root / args.dataset / "sfx"

    resource_refs: list[str] = []
    plan_lines: list[tuple[Path, Path]] = []
    for idx, p in enumerate(src_paths):
        framerate, duration = _read_wav_info(p)
        resource_ref = f"sfx.{name}_v{idx}"
        out_path = out_dir / f"{name}_v{idx}.wav"
        resource_refs.append(resource_ref)
        plan_lines.append((p, out_path))
        # 采样率/时长只做基本校验，不进数据表字段（sfx.def schema 未登记这两个字段），打印供人工核对。
        log(f"音频变体 {resource_ref}: 采样率={framerate}Hz 时长={duration}s")

    row: dict = {
        "id": args.id,
        "layer": args.layer,
        "priority": args.priority,
    }
    if len(resource_refs) >= 2:
        row["variants"] = resource_refs
    row["resource_ref"] = resource_refs[0]

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
