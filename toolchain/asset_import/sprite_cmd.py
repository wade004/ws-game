"""``sprite`` 子命令：精灵集源目录 -> 规范化精灵资源 + display.map 数据行。

输入约定（见任务口径与 architecture/09_表现层.md 第 3.2～3.3 节）：

```
<src_dir>/<direction_slot>/<layer>.png   多层（纸娃娃）模式
<src_dir>/<direction_slot>.png           单层模式（该精灵集不分层）
<src_dir>/icon.png                       可选，随精灵集一起登记图标
```

``<src_dir>`` 本身就是"一个精灵集源目录"，其目录名即 ``sprite_set_name``。

方向档位命名与镜像回填规则见 ``directions.py``（对应 architecture/14_资产规格书模板.md
第 2.1 节）；只需要画 canonical 档位（如 8 方向下的
``front``/``front_side_r``/``side_r``/``back_side_r``/``back`` 五个），另外 3 个镜像档位
（``front_side_l``/``side_l``/``back_side_l``）在 ``--mirror auto`` 时自动用水平翻转回填并
记录进 ``mirror_pairs``。
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image

from . import directions
from .atlas import pack_atlas
from .common import (
    AssetImportError,
    log,
    merge_write_row,
    parse_vec2_fraction,
    read_json,
    resolve_root,
    strip_domain,
    to_direction_slot_id,
    validate_id,
    write_json_pretty,
)
from .image_ops import apply_matting, flip_horizontal, parse_matting_spec, trim_transparent

FLAT_LAYER_KEY = "__flat__"
CATEGORY_CHOICES = ["creature", "item", "skill", "aura", "gobj", "projectile"]
SHADOW_CHOICES = ["none", "blob", "projected"]


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("src", help="精灵集源目录（目录名即 sprite_set_name）")
    parser.add_argument("--dataset", default="_sample", help="目标数据集名，默认 _sample")
    parser.add_argument("--category", required=True, choices=CATEGORY_CHOICES, help="逻辑对象类别")
    parser.add_argument("--logical-id", required=True, help="指向的逻辑记录 id，如 creature.grey_wolf")
    parser.add_argument(
        "--direction-count", type=int, choices=[4, 8, 16], default=8, help="方向量化档位数量，默认 8"
    )
    parser.add_argument(
        "--mirror", choices=["auto", "none"], default="auto", help="缺失档位是否按镜像规则回填，默认 auto"
    )
    parser.add_argument("--anchors", default=None, help="每方向档位锚点像素坐标的 JSON 文件路径")
    parser.add_argument(
        "--anchor-default",
        action="append",
        default=None,
        metavar="name=fx,fy",
        help="锚点缺失时的默认回填（画布比例坐标），可重复；默认 root=0.5,1.0",
    )
    parser.add_argument(
        "--matting", default="none", help="抠图方式：none|rembg|colorkey:#RRGGBB，默认 none"
    )
    parser.add_argument(
        "--colorkey-tolerance", type=int, default=32, help="colorkey 抠图的颜色容差（0~255），默认 32"
    )
    parser.add_argument("--trim", action="store_true", help="裁掉透明边并按裁剪偏移平移锚点")
    parser.add_argument("--scale", type=float, default=1.0, help="写入 display.map 的默认缩放，默认 1.0")
    parser.add_argument(
        "--shadow", choices=SHADOW_CHOICES, default="blob", help="写入 display.map 的阴影模式，默认 blob"
    )
    parser.add_argument(
        "--pixels-per-unit", type=float, default=32.0, help="锚点像素坐标换算为世界单位的除数，默认 32"
    )
    parser.add_argument(
        "--icon-size", type=int, default=64, help="随精灵集登记的 icon.png 归一化尺寸，默认 64"
    )
    parser.add_argument("--assets-root", default=None, help="资产根目录，默认仓库 assets/")
    parser.add_argument("--data-root", default=None, help="数据根目录，默认仓库 data/")
    parser.add_argument("--dry-run", action="store_true", help="只打印计划，不写任何文件")


def _discover_direction_entries(src_dir: Path, valid_slots: set[str]) -> dict[str, Path]:
    """扫描 src_dir 下的方向档位子目录/文件，返回 {slot_name: path}。"""
    entries: dict[str, Path] = {}
    for item in sorted(src_dir.iterdir()):
        if item.name == "icon.png":
            continue
        if item.is_dir():
            name = item.name
        elif item.is_file() and item.suffix.lower() == ".png":
            name = item.stem
        else:
            continue
        if name not in valid_slots:
            log(f"警告：忽略未知方向档位条目 '{item.name}'（不在 direction-count 对应的档位集合内）")
            continue
        entries[name] = item
    return entries


def _load_layer_images(entry_path: Path) -> dict[str, Image.Image]:
    """把一个方向档位条目加载为 {layer_name: Image}；单文件模式用 FLAT_LAYER_KEY 作为唯一层名。"""
    if entry_path.is_file():
        return {FLAT_LAYER_KEY: Image.open(entry_path).convert("RGBA")}
    layers: dict[str, Image.Image] = {}
    for png in sorted(entry_path.glob("*.png")):
        layers[png.stem] = Image.open(png).convert("RGBA")
    if not layers:
        raise AssetImportError(f"方向档位目录 '{entry_path}' 下没有任何 .png 层文件")
    return layers


def run(args: argparse.Namespace) -> int:
    repo_root = _repo_root()
    assets_root = resolve_root(args.assets_root, repo_root, "assets")
    data_root = resolve_root(args.data_root, repo_root, "data")

    src_dir = Path(args.src).resolve()
    if not src_dir.is_dir():
        raise AssetImportError(f"源目录不存在: {src_dir}")

    sprite_set_name = src_dir.name
    validate_id(args.logical_id, args.category, "--logical-id")

    canonical = directions.canonical_slot_names(args.direction_count)
    mirrorable = directions.mirrorable_slot_names(args.direction_count)
    all_slots = directions.all_slot_names(args.direction_count)
    default_slot = directions.default_direction_slot(args.direction_count)

    entries = _discover_direction_entries(src_dir, set(all_slots))

    missing_canonical = [name for name in canonical if name not in entries]
    if missing_canonical:
        raise AssetImportError(
            "缺少必须提供美术的方向档位（无法镜像回填）: " + ", ".join(missing_canonical)
        )

    matting_mode, matting_extra = parse_matting_spec(args.matting)

    # 载入并处理每个已提供美术的方向档位（含 canonical 与用户手绘的镜像档位）。
    processed: dict[str, dict[str, Image.Image]] = {}
    layer_names_reference: list[str] | None = None
    trim_offsets: dict[str, tuple[int, int]] = {}

    for slot_name, entry_path in entries.items():
        raw_layers = _load_layer_images(entry_path)
        layer_keys = sorted(raw_layers.keys())
        if layer_names_reference is None:
            layer_names_reference = layer_keys
        elif layer_keys != layer_names_reference:
            raise AssetImportError(
                f"方向档位 '{slot_name}' 的层集合 {layer_keys} 与其他档位 {layer_names_reference} 不一致"
            )

        processed_layers: dict[str, Image.Image] = {}
        offset = (0, 0)
        for layer_name, img in raw_layers.items():
            img = apply_matting(img, matting_mode, matting_extra, tolerance=args.colorkey_tolerance)
            if args.trim:
                img, offset = trim_transparent(img)
            processed_layers[layer_name] = img
        processed[slot_name] = processed_layers
        trim_offsets[slot_name] = offset

    is_flat = layer_names_reference == [FLAT_LAYER_KEY]
    paperdoll_layers: list[str] = [] if is_flat else list(layer_names_reference or [])

    # 镜像回填缺失的方向档位。
    mirror_pairs_recorded: list[dict] = []
    for canonical_name in mirrorable:
        mirror_name = directions.mirror_slot_name(canonical_name)
        if mirror_name in processed:
            continue  # 用户手绘了镜像档位，不覆盖。
        if args.mirror == "none":
            log(f"警告：缺少方向档位 '{mirror_name}'，--mirror none 不做镜像回填")
            continue
        source_layers = processed[canonical_name]
        mirrored_layers = {name: flip_horizontal(img) for name, img in source_layers.items()}
        processed[mirror_name] = mirrored_layers
        trim_offsets[mirror_name] = trim_offsets[canonical_name]
        mirror_pairs_recorded.append(
            {"direction_slot": mirror_name, "mirror_of": canonical_name, "flip_x": True}
        )

    materialized_slots = sorted(processed.keys())
    if set(materialized_slots) - set(all_slots):
        raise AssetImportError("内部错误：出现了未登记的方向档位")

    # 锚点：读取 --anchors，缺失用 --anchor-default 回填；镜像档位缺失时用其 canonical 档位镜像。
    anchor_defaults = _parse_anchor_defaults(args.anchor_default)
    anchors_input: dict = {}
    if args.anchors:
        anchors_path = Path(args.anchors)
        if not anchors_path.is_file():
            raise AssetImportError(f"--anchors 指向的文件不存在: {anchors_path}")
        anchors_input = read_json(anchors_path)

    # 自动镜像回填出的档位 -> 其 canonical 来源档位（见上面的镜像回填循环），供锚点镜像回退用；
    # 不再依赖旧版"档位名固定后缀"这种可从名字反推来源的命名约定，改为查表。
    mirror_source_of: dict[str, str] = {
        rec["direction_slot"]: rec["mirror_of"] for rec in mirror_pairs_recorded
    }

    per_direction_anchors: dict[str, dict] = {}
    # 先处理 canonical 档位，再处理其余档位（用户手绘或自动镜像回填的档位）：镜像回退
    # 逻辑依赖 canonical 档位的锚点已经算好，用显式的两段顺序而不是依赖字典排序巧合。
    ordered_slots = [s for s in canonical if s in processed] + [
        s for s in materialized_slots if s not in canonical
    ]
    for slot_name in ordered_slots:
        canvas_w, canvas_h = _canvas_size(processed[slot_name])
        if slot_name in anchors_input:
            raw = dict(anchors_input[slot_name])
            off_x, off_y = trim_offsets.get(slot_name, (0, 0))
            slot_anchors = {name: [px - off_x, py - off_y] for name, (px, py) in _iter_vec2(raw)}
        elif slot_name in mirror_source_of and mirror_source_of[slot_name] in per_direction_anchors:
            src_anchor = per_direction_anchors[mirror_source_of[slot_name]]
            src_w = src_anchor["canvas_size"][0]
            slot_anchors = {name: [src_w - px, py] for name, (px, py) in _iter_vec2(src_anchor["anchors"])}
        else:
            log(f"警告：方向档位 '{slot_name}' 缺少锚点数据，使用 --anchor-default 回填")
            slot_anchors = {
                name: [round(fx * canvas_w, 3), round(fy * canvas_h, 3)]
                for name, (fx, fy) in anchor_defaults.items()
            }
        per_direction_anchors[slot_name] = {"canvas_size": [canvas_w, canvas_h], "anchors": slot_anchors}

    # 打包图集：帧 key 用 "<slot>" 或 "<slot>/<layer>"。
    frames: dict[str, Image.Image] = {}
    for slot_name in materialized_slots:
        layers = processed[slot_name]
        if is_flat:
            frames[slot_name] = layers[FLAT_LAYER_KEY]
        else:
            for layer_name, img in layers.items():
                frames[f"{slot_name}/{layer_name}"] = img
    atlas_img, frame_rects = pack_atlas(frames)

    # icon.png（可选）。
    icon_id = None
    icon_src = src_dir / "icon.png"
    icon_out_path = None
    if icon_src.is_file():
        icon_img = Image.open(icon_src).convert("RGBA")
        icon_img = _fit_square(icon_img, args.icon_size)
        icon_id = f"icon.{args.category}.{sprite_set_name}"
        icon_out_path = assets_root / args.dataset / "icons" / args.category / f"{sprite_set_name}.png"

    # 输出路径。
    sprite_out_dir = assets_root / args.dataset / "sprites" / sprite_set_name
    atlas_png_path = sprite_out_dir / "atlas.png"
    atlas_json_path = sprite_out_dir / "atlas.json"
    anchors_json_path = sprite_out_dir / "anchors.json"

    display_anchor_points = {
        name: {
            "x": round(px / args.pixels_per_unit, 6),
            "y": round(py / args.pixels_per_unit, 6),
        }
        for name, (px, py) in _iter_vec2(per_direction_anchors[default_slot]["anchors"])
    }

    row: dict = {
        "id": f"display.{strip_domain(args.logical_id)}",
        "category": args.category,
        "logical_id": args.logical_id,
        "kind": "sprite",
        "sprite_set_id": f"sprite.{args.category}.{sprite_set_name}",
        "direction_count": args.direction_count,
    }
    if mirror_pairs_recorded:
        # 判断记录（缺口 9）：display.map.mirror_pairs 的 direction_slot/mirror_of 是 Id 类型字段
        # （见 core/foundation/display_info/contracts/SpriteInfo.cs MirrorPair、
        # presentation/common/contracts/DirectionSlots.cs "Id 前缀"判断记录），裸方向档位名（如
        # "front_side_l"）不含点号、不满足 Id 格式，写入前必须补上 "dir." 前缀；
        # mirror_pairs_recorded 本身（供上面 mirror_source_of 查表用）与文件名/anchors.json 标注
        # 仍然保持裸名字不变，只在这里拼进最终写入 display.map 的 row 时转换。
        row["mirror_pairs"] = [
            {
                "direction_slot": to_direction_slot_id(rec["direction_slot"]),
                "mirror_of": to_direction_slot_id(rec["mirror_of"]),
                "flip_x": rec["flip_x"],
            }
            for rec in mirror_pairs_recorded
        ]
    if paperdoll_layers:
        row["paperdoll_layers"] = paperdoll_layers
    if display_anchor_points:
        row["anchor_points"] = display_anchor_points
    if icon_id:
        row["icon_id"] = icon_id
    row["scale"] = args.scale
    row["shadow"] = args.shadow
    row["sort_offset"] = 0

    if args.dry_run:
        log(f"计划写出图集: {atlas_png_path}", dry_run=True)
        log(f"计划写出图集索引: {atlas_json_path}", dry_run=True)
        log(f"计划写出锚点文件: {anchors_json_path}", dry_run=True)
        if icon_out_path:
            log(f"计划写出图标: {icon_out_path}", dry_run=True)
        for slot_name in materialized_slots:
            for layer_name, img in processed[slot_name].items():
                out = _layer_out_path(sprite_out_dir, slot_name, layer_name, is_flat)
                log(f"计划写出帧: {out} ({img.size[0]}x{img.size[1]})", dry_run=True)
        display_map_path = data_root / args.dataset / "display" / "display.map.json"
        log(f"计划合并写入 display.map 行: {row['id']} -> {display_map_path}", dry_run=True)
        return 0

    for slot_name in materialized_slots:
        for layer_name, img in processed[slot_name].items():
            out = _layer_out_path(sprite_out_dir, slot_name, layer_name, is_flat)
            out.parent.mkdir(parents=True, exist_ok=True)
            img.save(out)

    sprite_out_dir.mkdir(parents=True, exist_ok=True)
    atlas_img.save(atlas_png_path)
    write_json_pretty(atlas_json_path, {"frames": frame_rects})
    write_json_pretty(anchors_json_path, per_direction_anchors)

    if icon_out_path:
        icon_out_path.parent.mkdir(parents=True, exist_ok=True)
        icon_img.save(icon_out_path)

    display_map_path = data_root / args.dataset / "display" / "display.map.json"
    merge_write_row(display_map_path, "display.map", row, key_field="id")

    log(f"已写出精灵集: {sprite_out_dir}")
    log(f"已合并写入 display.map 行: {row['id']}")
    return 0


def _layer_out_path(sprite_out_dir: Path, slot_name: str, layer_name: str, is_flat: bool) -> Path:
    if is_flat:
        return sprite_out_dir / f"{slot_name}.png"
    return sprite_out_dir / slot_name / f"{layer_name}.png"


def _canvas_size(layers: dict[str, Image.Image]) -> tuple[int, int]:
    widths = {img.size[0] for img in layers.values()}
    heights = {img.size[1] for img in layers.values()}
    return max(widths), max(heights)


def _iter_vec2(mapping: dict) -> list[tuple[str, tuple[float, float]]]:
    result = []
    for name, value in mapping.items():
        if isinstance(value, dict):
            result.append((name, (value["x"], value["y"])))
        else:
            result.append((name, (value[0], value[1])))
    return result


def _parse_anchor_defaults(specs: list[str] | None) -> dict[str, tuple[float, float]]:
    if not specs:
        specs = ["root=0.5,1.0"]
    result: dict[str, tuple[float, float]] = {}
    for spec in specs:
        name, fx, fy = parse_vec2_fraction(spec)
        result[name] = (fx, fy)
    return result


def _fit_square(img: Image.Image, size: int) -> Image.Image:
    """把图像等比缩放后居中贴到透明的 size x size 画布上。"""
    w, h = img.size
    scale = size / max(w, h)
    new_w, new_h = max(1, round(w * scale)), max(1, round(h * scale))
    resized = img.resize((new_w, new_h), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(resized, ((size - new_w) // 2, (size - new_h) // 2), resized)
    return canvas


def _repo_root() -> Path:
    from .common import find_repo_root

    return find_repo_root()
