"""``check`` 子命令：assets/<dataset>/ 与 data/<dataset>/display|vfx|sfx 交叉校验。

检查项（见任务口径，是对 04 第 5 节"外形映射存在"等检查项在资产文件层面的补充，
不重复实现 toolchain/validator 里的表字段级校验）：

- ``display.map`` 引用的 ``sprite_set_id``/``icon_id`` 对应的资产文件/目录必须存在。
- 每个精灵集内，同一层（跨方向档位）尺寸必须一致。
- 锚点必须落在对应方向档位画布范围内。
- ``direction_count`` 与实际落地的方向档位数一致。
- ``vfx.def``/``sfx.def`` 引用的 ``resource_ref``/``variants`` 对应资产文件必须存在。

不做任何写入；只打印问题清单，返回码 0（无问题）或 1（有问题）。
"""

from __future__ import annotations

import argparse
from pathlib import Path

from .common import find_repo_root, read_json, resolve_root, strip_domain


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--dataset", default="_sample", help="要校验的数据集名，默认 _sample")
    parser.add_argument("--assets-root", default=None, help="资产根目录，默认仓库 assets/")
    parser.add_argument("--data-root", default=None, help="数据根目录，默认仓库 data/")


def _load_rows(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    data = read_json(path)
    return data.get("rows", []) if isinstance(data, dict) else []


def _check_sprite_row(row: dict, assets_root: Path, dataset: str, problems: list[str]) -> None:
    sprite_set_id = row.get("sprite_set_id")
    row_id = row.get("id", "?")
    if not sprite_set_id:
        problems.append(f"{row_id}: kind=sprite 但缺少 sprite_set_id")
        return
    parts = sprite_set_id.split(".", 2)
    if len(parts) != 3:
        problems.append(f"{row_id}: sprite_set_id '{sprite_set_id}' 格式不是 sprite.<category>.<name>")
        return
    name = parts[2]
    set_dir = assets_root / dataset / "sprites" / name
    if not set_dir.is_dir():
        problems.append(f"{row_id}: sprite_set_id '{sprite_set_id}' 对应目录不存在: {set_dir}")
        return

    atlas_json_path = set_dir / "atlas.json"
    anchors_json_path = set_dir / "anchors.json"
    if not atlas_json_path.is_file():
        problems.append(f"{row_id}: 缺少 {atlas_json_path}")
    if not anchors_json_path.is_file():
        problems.append(f"{row_id}: 缺少 {anchors_json_path}")
    if not (set_dir / "atlas.png").is_file():
        problems.append(f"{row_id}: 缺少 {set_dir / 'atlas.png'}")

    frame_rects: dict[str, dict] = {}
    if atlas_json_path.is_file():
        atlas_data = read_json(atlas_json_path)
        frame_rects = atlas_data.get("frames", {})

        slot_names = {key.split("/", 1)[0] for key in frame_rects}
        expected_count = row.get("direction_count")
        if expected_count is not None and len(slot_names) != expected_count:
            problems.append(
                f"{row_id}: direction_count={expected_count} 但实际落地 {len(slot_names)} 个方向档位: "
                + ", ".join(sorted(slot_names))
            )

        by_layer: dict[str, dict[str, tuple[int, int]]] = {}
        for key, rect in frame_rects.items():
            slot, _, layer = key.partition("/")
            layer_key = layer if layer else "*"
            by_layer.setdefault(layer_key, {})[slot] = (rect["w"], rect["h"])
        for layer_key, sizes_by_slot in by_layer.items():
            distinct_sizes = set(sizes_by_slot.values())
            if len(distinct_sizes) > 1:
                problems.append(
                    f"{row_id}: 层 '{layer_key}' 在不同方向档位尺寸不一致: {sizes_by_slot}"
                )

        for slot in slot_names:
            expected_files = [k for k in frame_rects if k == slot or k.startswith(f"{slot}/")]
            for key in expected_files:
                layer = key.partition("/")[2]
                out_path = (
                    set_dir / f"{slot}.png" if not layer else set_dir / slot / f"{layer}.png"
                )
                if not out_path.is_file():
                    problems.append(f"{row_id}: 帧文件缺失: {out_path}")

    if anchors_json_path.is_file():
        anchors_data = read_json(anchors_json_path)
        for slot_name, entry in anchors_data.items():
            canvas = entry.get("canvas_size", [0, 0])
            cw, ch = canvas[0], canvas[1]
            for anchor_name, coord in entry.get("anchors", {}).items():
                x, y = coord[0], coord[1]
                if not (0 <= x <= cw and 0 <= y <= ch):
                    problems.append(
                        f"{row_id}: 方向档位 '{slot_name}' 锚点 '{anchor_name}' "
                        f"({x}, {y}) 超出画布范围 ({cw}x{ch})"
                    )

    icon_id = row.get("icon_id")
    if icon_id:
        icon_parts = icon_id.split(".", 2)
        if len(icon_parts) == 3:
            icon_path = assets_root / dataset / "icons" / icon_parts[1] / f"{icon_parts[2]}.png"
            if not icon_path.is_file():
                problems.append(f"{row_id}: icon_id '{icon_id}' 对应文件不存在: {icon_path}")
        else:
            problems.append(f"{row_id}: icon_id '{icon_id}' 格式不是 icon.<category>.<name>")


def _check_vfx_row(row: dict, assets_root: Path, dataset: str, problems: list[str]) -> None:
    row_id = row.get("id", "?")
    name = strip_domain(row_id).replace(".", "_") if "." in row_id else row_id
    out_dir = assets_root / dataset / "vfx" / name
    if not (out_dir / "atlas.png").is_file():
        problems.append(f"{row_id}: resource_ref 对应图集缺失: {out_dir / 'atlas.png'}")
    if not (out_dir / "atlas.json").is_file():
        problems.append(f"{row_id}: resource_ref 对应图集索引缺失: {out_dir / 'atlas.json'}")


def _check_sfx_row(row: dict, assets_root: Path, dataset: str, problems: list[str]) -> None:
    row_id = row.get("id", "?")
    name = strip_domain(row_id).replace(".", "_") if "." in row_id else row_id
    out_dir = assets_root / dataset / "sfx" / name
    variants = row.get("variants", [])
    for idx in range(len(variants)):
        variant_path = out_dir / f"v{idx}.wav"
        if not variant_path.is_file():
            problems.append(f"{row_id}: variants[{idx}] 对应音频文件缺失: {variant_path}")


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    assets_root = resolve_root(args.assets_root, repo_root, "assets")
    data_root = resolve_root(args.data_root, repo_root, "data")

    problems: list[str] = []

    display_rows = _load_rows(data_root / args.dataset / "display" / "display.map.json")
    for row in display_rows:
        if row.get("kind") == "sprite":
            _check_sprite_row(row, assets_root, args.dataset, problems)

    vfx_rows = _load_rows(data_root / args.dataset / "vfx" / "vfx.def.json")
    for row in vfx_rows:
        _check_vfx_row(row, assets_root, args.dataset, problems)

    sfx_rows = _load_rows(data_root / args.dataset / "sfx" / "sfx.def.json")
    for row in sfx_rows:
        _check_sfx_row(row, assets_root, args.dataset, problems)

    for message in problems:
        print(message)

    print(
        f"[check] dataset={args.dataset}: 检查 {len(display_rows)} 条 display.map / "
        f"{len(vfx_rows)} 条 vfx.def / {len(sfx_rows)} 条 sfx.def，发现 {len(problems)} 个问题"
    )
    return 1 if problems else 0
