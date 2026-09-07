"""``check`` 子命令：assets/<dataset>/ 与 data/<dataset>/display|vfx|sfx|world 交叉校验。

检查项（见任务口径，是对 04 第 5 节"外形映射存在"等检查项在资产文件层面的补充，
不重复实现 toolchain/validator 里的表字段级校验）：

- ``display.map`` 引用的 ``sprite_set_id``/``icon_id`` 对应的资产文件/目录必须存在。
- 每个精灵集内，同一层（跨方向档位）尺寸必须一致。
- 锚点必须落在对应方向档位画布范围内。
- ``direction_count`` 与实际落地的方向档位数一致。
- **``mirror_pairs`` 完整性**：每个已落地、但不属于该 ``direction_count`` 下 canonical
  档位集合（``directions.canonical_slot_names``）的方向档位，必须在 ``mirror_pairs`` 中有一条
  ``direction_slot`` 与之对应的声明，且其 ``mirror_of`` 指向的档位本身已落地（有实际帧文件）。
- **声明锚点缺失**：``display.map.anchor_points`` 声明的每个 ``anchor_id``，必须能在精灵集自己的
  ``anchors.json``（默认方向档位，即 ``directions.default_direction_slot`` 对应档位）标注中找到，
  对应 14 第 11 节"缺锚点：声明需要的锚点在标注中缺失"这条校验项。
- ``vfx.def``/``sfx.def`` 引用的 ``resource_ref``/``variants`` 对应资产文件必须存在。
- **地图引用存在性**：``world.map`` 每一行引用的地图分层图（见 ``map`` 子命令、14 第 9 节）在
  ``assets/<dataset>/maps/<name>/`` 下必须存在必需分层文件（``ground.png``/``overlay.png``）。

``--only``（逗号分隔，取值 ``sprite``/``vfx``/``sfx``/``world`` 的子集，省略则四项全跑）：只跑
选定的检查域，供门禁在某个数据集的部分域尚未接入真实资产时先只校验已接入的那部分（不放宽已选中
域的判断逻辑本身，只是缩小本次运行覆盖的表范围）。

不做任何写入；只打印问题清单，返回码 0（无问题）或 1（有问题）。
"""

from __future__ import annotations

import argparse
from pathlib import Path

from . import directions
from .common import (
    DIRECTION_SLOT_ID_PREFIX,
    AssetImportError,
    find_repo_root,
    flatten_id_segment,
    read_json,
    resolve_root,
    strip_domain,
)

ALL_DOMAINS = ("sprite", "vfx", "sfx", "world")

# 地图必需分层文件（14 第 9.1 节"框架固定项"三层中的地面图/前景遮挡层；装饰层
# decal.png、导航标注 nav_hint.png 属于可选辅助分层，见 map 子命令与 14 第 9 节勘误）。
REQUIRED_MAP_LAYERS = ("ground.png", "overlay.png")


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--dataset", default="_sample", help="要校验的数据集名，默认 _sample")
    parser.add_argument("--assets-root", default=None, help="资产根目录，默认仓库 assets/")
    parser.add_argument("--data-root", default=None, help="数据根目录，默认仓库 data/")
    parser.add_argument(
        "--only",
        default=None,
        metavar="sprite,vfx,sfx,world",
        help="只跑逗号分隔的检查域子集（取值见上），省略则四项全跑",
    )


def _parse_only(value: str | None) -> set[str]:
    if value is None:
        return set(ALL_DOMAINS)
    domains = {v.strip() for v in value.split(",") if v.strip()}
    unknown = domains - set(ALL_DOMAINS)
    if unknown:
        raise AssetImportError(
            f"--only 取值 {sorted(unknown)} 不在合法域集合 {ALL_DOMAINS} 内"
        )
    return domains


def _load_rows(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    data = read_json(path)
    return data.get("rows", []) if isinstance(data, dict) else []


def _bare_slot(value: str) -> str:
    """去掉方向档位 Id 前缀（"dir."），未带前缀的裸名字原样返回，见 common.DIRECTION_SLOT_ID_PREFIX。"""
    if value.startswith(DIRECTION_SLOT_ID_PREFIX):
        return value[len(DIRECTION_SLOT_ID_PREFIX):]
    return value


def _check_mirror_pairs(row: dict, slot_names: set[str], problems: list[str]) -> None:
    """校验 mirror_pairs 完整性：非原创档位必须有声明，声明的镜像来源必须已落地。"""
    row_id = row.get("id", "?")
    direction_count = row.get("direction_count")
    if direction_count not in (4, 8, 16):
        return

    try:
        canonical = set(directions.canonical_slot_names(direction_count))
    except AssetImportError:
        return

    mirror_pairs = row.get("mirror_pairs", [])
    declared: dict[str, str] = {}
    for mp in mirror_pairs:
        target = _bare_slot(mp.get("direction_slot", ""))
        source = _bare_slot(mp.get("mirror_of", ""))
        declared[target] = source
        if source and source not in slot_names:
            problems.append(
                f"{row_id}: mirror_pairs 声明 '{target}' 的镜像来源 '{mp.get('mirror_of')}' "
                f"未落地（不在已生成的方向档位 {sorted(slot_names)} 中）"
            )

    for slot in sorted(slot_names - canonical):
        if slot not in declared:
            problems.append(
                f"{row_id}: 方向档位 '{slot}' 不是原创档位（不在 direction_count={direction_count} "
                f"的 canonical 集合 {sorted(canonical)} 内），但 mirror_pairs 未声明其镜像来源"
            )


def _check_declared_anchors(
    row: dict, anchors_data: dict, problems: list[str]
) -> None:
    """校验 display.map.anchor_points 声明的每个锚点在 anchors.json 默认档位标注中都存在。"""
    row_id = row.get("id", "?")
    anchor_points = row.get("anchor_points")
    direction_count = row.get("direction_count")
    if not anchor_points or direction_count not in (4, 8, 16):
        return

    default_slot = directions.default_direction_slot(direction_count)
    slot_entry = anchors_data.get(default_slot, {})
    actual_names = set(slot_entry.get("anchors", {}).keys())

    for anchor_id in anchor_points:
        if anchor_id not in actual_names:
            problems.append(
                f"{row_id}: display.map.anchor_points 声明的锚点 '{anchor_id}' 在方向档位 "
                f"'{default_slot}' 的 anchors.json 标注中缺失"
            )


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
    # 目录名与 sprite 子命令的落地规则、运行时 UnityResourceLoader/SpriteViewBase 的资源 id
    # 解析规则对齐：sprite_set_id 去掉首段类别前缀 "sprite." 后把剩余点号换成下划线，
    # 即 "<category>_<name>"（parts[1] + "_" + parts[2]），不是只用 parts[2]。
    name = parts[1] + "_" + parts[2]
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
    slot_names: set[str] = set()
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

        _check_mirror_pairs(row, slot_names, problems)

    anchors_data: dict = {}
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
        _check_declared_anchors(row, anchors_data, problems)

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
    # 与 vfx_cmd.py 的归一化口径保持一致（TOOL-02 判断记录），否则 check 会按旧的
    # .replace(".", "_") 算出与实际落盘目录不同的路径，误报缺失。
    name = flatten_id_segment(strip_domain(row_id)) if "." in row_id else row_id
    out_dir = assets_root / dataset / "vfx" / name
    if not (out_dir / "atlas.png").is_file():
        problems.append(f"{row_id}: resource_ref 对应图集缺失: {out_dir / 'atlas.png'}")
    # 运行时 ResourceKind.Effect 读 frames.json（非 atlas.json），见
    # UnityResourceLoader.TryDecodeEffect 与 assets/_placeholder/vfx/burn/frames.json 样例。
    if not (out_dir / "frames.json").is_file():
        problems.append(f"{row_id}: resource_ref 对应帧数据缺失: {out_dir / 'frames.json'}")


def _check_sfx_row(row: dict, assets_root: Path, dataset: str, problems: list[str]) -> None:
    """校验 resource_ref（必填）与 variants（可选）各自对应的扁平音频文件是否存在，
    并校验 variants 若存在则必须包含 resource_ref 本身（见 sfx 子命令落地口径与
    presentation/vfx_sfx/schema/VfxSfxSchemas.cs 的 sfx.def schema：resource_ref 必填 Id、
    variants 可选 Id 列表）。运行时 ResourceKind.Audio 按
    "audio/<资源引用 id 去掉 'sfx.'>.wav" 解析（扁平文件，非子目录），这里同规则校验。
    """
    row_id = row.get("id", "?")
    sfx_dir = assets_root / dataset / "sfx"

    resource_ref = row.get("resource_ref")
    if not resource_ref:
        problems.append(f"{row_id}: kind=sfx 但缺少 resource_ref")
    else:
        ref_path = sfx_dir / f"{strip_domain(resource_ref)}.wav"
        if not ref_path.is_file():
            problems.append(f"{row_id}: resource_ref '{resource_ref}' 对应音频文件缺失: {ref_path}")

    variants = row.get("variants", [])
    if variants and resource_ref and resource_ref not in variants:
        problems.append(f"{row_id}: variants {variants} 未包含 resource_ref '{resource_ref}'")
    for ref in variants:
        ref_path = sfx_dir / f"{strip_domain(ref)}.wav"
        if not ref_path.is_file():
            problems.append(f"{row_id}: variants 引用 '{ref}' 对应音频文件缺失: {ref_path}")


def _check_world_row(row: dict, assets_root: Path, dataset: str, problems: list[str]) -> None:
    """校验 world.map 一行引用的地图分层图（map 子命令产出）是否存在，见 14 第 9 节。"""
    row_id = row.get("id", "?")
    name = strip_domain(row_id) if "." in row_id else row_id
    map_dir = assets_root / dataset / "maps" / name
    if not map_dir.is_dir():
        problems.append(f"{row_id}: 地图分层图目录不存在: {map_dir}（见 import_assets.py map 子命令）")
        return
    for layer_file in REQUIRED_MAP_LAYERS:
        layer_path = map_dir / layer_file
        if not layer_path.is_file():
            problems.append(f"{row_id}: 地图分层图缺失: {layer_path}")

    scene_ref = row.get("scene_ref")
    if scene_ref is not None and scene_ref != f"scene.{name}":
        problems.append(
            f"{row_id}: scene_ref '{scene_ref}' 与地图目录名推导出的引用 id 'scene.{name}' 不一致"
        )
    nav_ref = row.get("nav_ref")
    if nav_ref is not None and nav_ref != f"nav.{name}":
        problems.append(
            f"{row_id}: nav_ref '{nav_ref}' 与地图目录名推导出的引用 id 'nav.{name}' 不一致"
        )


def _check_world_row(row: dict, assets_root: Path, dataset: str, problems: list[str]) -> None:
    """校验 world.map 一行引用的地图分层图（map 子命令产出）是否存在，见 14 第 9 节。"""
    row_id = row.get("id", "?")
    name = strip_domain(row_id) if "." in row_id else row_id
    map_dir = assets_root / dataset / "maps" / name
    if not map_dir.is_dir():
        problems.append(f"{row_id}: 地图分层图目录不存在: {map_dir}（见 import_assets.py map 子命令）")
        return
    for layer_file in REQUIRED_MAP_LAYERS:
        layer_path = map_dir / layer_file
        if not layer_path.is_file():
            problems.append(f"{row_id}: 地图分层图缺失: {layer_path}")

    scene_ref = row.get("scene_ref")
    if scene_ref is not None and scene_ref != f"scene.{name}":
        problems.append(
            f"{row_id}: scene_ref '{scene_ref}' 与地图目录名推导出的引用 id 'scene.{name}' 不一致"
        )
    nav_ref = row.get("nav_ref")
    if nav_ref is not None and nav_ref != f"nav.{name}":
        problems.append(
            f"{row_id}: nav_ref '{nav_ref}' 与地图目录名推导出的引用 id 'nav.{name}' 不一致"
        )


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    assets_root = resolve_root(args.assets_root, repo_root, "assets")
    data_root = resolve_root(args.data_root, repo_root, "data")
    only = _parse_only(args.only)

    problems: list[str] = []

    display_rows: list[dict] = []
    vfx_rows: list[dict] = []
    sfx_rows: list[dict] = []
    world_rows: list[dict] = []

    if "sprite" in only:
        display_rows = _load_rows(data_root / args.dataset / "display" / "display.map.json")
        for row in display_rows:
            if row.get("kind") == "sprite":
                _check_sprite_row(row, assets_root, args.dataset, problems)

    if "vfx" in only:
        vfx_rows = _load_rows(data_root / args.dataset / "vfx" / "vfx.def.json")
        for row in vfx_rows:
            _check_vfx_row(row, assets_root, args.dataset, problems)

    if "sfx" in only:
        sfx_rows = _load_rows(data_root / args.dataset / "sfx" / "sfx.def.json")
        for row in sfx_rows:
            _check_sfx_row(row, assets_root, args.dataset, problems)

    if "world" in only:
        world_rows = _load_rows(data_root / args.dataset / "world" / "world.map.json")
        for row in world_rows:
            _check_world_row(row, assets_root, args.dataset, problems)

    for message in problems:
        print(message)

    print(
        f"[check] dataset={args.dataset} only={','.join(sorted(only))}: 检查 {len(display_rows)} 条 "
        f"display.map / {len(vfx_rows)} 条 vfx.def / {len(sfx_rows)} 条 sfx.def / {len(world_rows)} "
        f"条 world.map，发现 {len(problems)} 个问题"
    )
    return 1 if problems else 0
