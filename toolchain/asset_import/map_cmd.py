"""``map`` 子命令：地图分层图源目录 -> 规范化地图资产 + world.map 行。

输入约定（对应 architecture/14_资产规格书模板.md 第 9 节"场景与地图"）：

```
<src>/ground.png       地面层（框架固定项，必需）
<src>/overlay.png      前景遮挡层（框架固定项，必需，遮挡单位的近景建筑/树冠等）
<src>/decal.png        装饰层（可选，不遮挡单位的贴花/痕迹，14 第 9.1 节"装饰层"）
<src>/nav_hint.png     导航标注参考图（可选，供引擎适配层侧手工绘制导航数据时参考，
                       不是 14 第 9.1 节列出的可视资产层本身，见该节"导航与碰撞不属于本规格书
                       出图范畴"；本工具仍原样搬运，供人工标注环节使用）
```

其余文件名一律忽略并打印警告（防止误把无关文件当分层图带入交付目录）。

输出：
- 规范化落到资产根目录下（与 ``assets/_placeholder/maps/`` 同一套目录口径，见 11 第 1 节占位资产
  包）——具体相对路径格式由 ``ref_conventions.map_directory``/``map_ground_file``/
  ``map_overlay_file``/``map_decal_file``/``map_nav_hint_file`` 五个函数给出（消费方反馈第 75 条，
  [ADR-0053](../../architecture/adr/0053-地图分层图路径约定纳入公开契约.md)：路径约定的权威出处
  是这组函数与 C# 侧 ``AssetRefConventions`` 对应方法，不是本文档字符串——本文件不再重复给出格式
  字符串，避免与权威出处漂移）。
- 生成/合并一行 ``world.map`` 数据（04 第 7.1 节总索引 + 05 第 4.1 节字段定义）：``scene_ref``/
  ``nav_ref`` 按"资源引用 id = 类别前缀 + 地图名"的既有约定固定为 ``scene.<map>``/``nav.<map>``
  （与 ``adapters/unity`` 侧 ``UnityResourceLoader`` 的 ``Scene``/``NavMesh`` 种类解析规则同一套
  引用 id 命名口径，见该包 README"资源 id → 路径规则"一节；本工具不生成场景/导航资源本身——
  05 第 4.1 节"导航与碰撞...由引擎适配层侧在场景中手工绘制"，只声明引用 id 与地面/遮挡分层图）。
  ``spawn_points`` 缺省时写一条 ``<map>.spawn.default``（世界原点、朝向 0），可用 ``--spawn``
  重复传入覆盖。
- 消费方反馈第 59 条（ADR-0036）：``world.map`` 行同时写入可选的 ``image_transform``——
  ``pixels_per_unit``（``--pixels-per-unit``，默认 32，框架级默认值见 14 第 2.2 节）、
  ``origin_px``（``--origin-px``，默认 ``0,<ground.png 高度>``，即世界原点位于图片左下角，
  与"世界 Y 轴向上、图片行向下"两条坐标约定叠加后的直觉写法——地面从图片底部往上铺开）、
  ``image_size_px``（不经命令行参数，直接从 ``ground.png`` 的 PNG ``IHDR`` 块读出实际宽高，
  见 :func:`_read_png_size`；本工具不引入 Pillow 之外的第三方依赖，``IHDR`` 结构本身足够简单，
  没有必要为此新增依赖）。见 05 第 3.1.1 节坐标约定、第 4.1 节 ``image_transform`` 字段登记。
"""

from __future__ import annotations

import argparse
import shutil
import struct
from pathlib import Path

from .common import AssetImportError, find_repo_root, log, merge_write_row, resolve_root, validate_id
from .ref_conventions import (
    map_decal_file,
    map_directory,
    map_ground_file,
    map_nav_hint_file,
    map_overlay_file,
)

# 分层名 -> ref_conventions 对应相对路径函数（消费方反馈第 75 条，ADR-0053：路径格式的权威出处
# 收口到这组共享函数，本文件不再自行拼接 f"{layer}.png"）。
_LAYER_FILE_FUNCS = {
    "ground": map_ground_file,
    "overlay": map_overlay_file,
    "decal": map_decal_file,
    "nav_hint": map_nav_hint_file,
}

# PNG 文件签名（见 https://www.w3.org/TR/png/ 第 5.2 节；本工具只需要 IHDR 里的宽高两个整数，
# 不需要解码像素数据，不引入 Pillow 之外的依赖）。
_PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"

# 分层文件名（不含扩展名）-> 是否必需；顺序即扫描/打印顺序。
RECOGNIZED_LAYERS: dict[str, bool] = {
    "ground": True,
    "overlay": True,
    "decal": False,
    "nav_hint": False,
}


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("src", help="地图分层图源目录（见模块文档：ground/overlay/decal/nav_hint）")
    parser.add_argument("--dataset", default="_sample", help="目标数据集名，默认 _sample")
    parser.add_argument("--map", required=True, help="地图名（不含 domain 前缀），如 sample_field")
    parser.add_argument(
        "--spawn",
        action="append",
        default=None,
        metavar="x,y[,facing]",
        help="出生点坐标，可重复；省略则写一条 <map>.spawn.default（原点，朝向 0）",
    )
    parser.add_argument(
        "--pixels-per-unit",
        type=float,
        default=32.0,
        metavar="N",
        help="image_transform.pixels_per_unit：每个世界单位对应的图片像素数，默认 32"
        "（消费方反馈第 59 条，ADR-0036；14 第 2.2 节框架级默认值）",
    )
    parser.add_argument(
        "--origin-px",
        default=None,
        metavar="X,Y",
        help="image_transform.origin_px：世界原点在 ground.png 中的像素坐标（左上角为 0,0，"
        "行向下为正），省略则默认 0,<ground.png 高度>（世界原点位于图片左下角）",
    )
    parser.add_argument("--assets-root", default=None, help="资产根目录，默认仓库 assets/")
    parser.add_argument("--data-root", default=None, help="数据根目录，默认仓库 data/")
    parser.add_argument("--dry-run", action="store_true", help="只打印计划，不写任何文件")


def _parse_spawn(spec: str) -> tuple[float, float, float]:
    parts = spec.split(",")
    if len(parts) not in (2, 3):
        raise AssetImportError(f"--spawn 取值 '{spec}' 格式应为 x,y[,facing]")
    try:
        x = float(parts[0])
        y = float(parts[1])
        facing = float(parts[2]) if len(parts) == 3 else 0.0
    except ValueError as exc:
        raise AssetImportError(f"--spawn 取值 '{spec}' 坐标/朝向不是合法数字") from exc
    return x, y, facing


def _parse_origin_px(spec: str) -> tuple[float, float]:
    parts = spec.split(",")
    if len(parts) != 2:
        raise AssetImportError(f"--origin-px 取值 '{spec}' 格式应为 X,Y")
    try:
        return float(parts[0]), float(parts[1])
    except ValueError as exc:
        raise AssetImportError(f"--origin-px 取值 '{spec}' 不是合法数字") from exc


def _read_png_size(path: Path) -> tuple[int, int]:
    """从 PNG 文件头的 IHDR 块读出 (宽, 高) 像素，不依赖 Pillow。

    PNG 结构：8 字节签名 + 若干"长度(4) + 类型(4) + 数据 + CRC(4)"块；IHDR 恒为第一个块，
    数据段前 8 字节是大端序的宽/高各 4 字节无符号整数（见 PNG 规范第 11.2.2 节）。本工具只需要
    这两个整数，读取固定偏移的 33 字节即可，不需要解析后续块或做任何像素解码。
    """
    with path.open("rb") as f:
        header = f.read(33)
    if len(header) < 33 or header[:8] != _PNG_SIGNATURE:
        raise AssetImportError(f"'{path}' 不是合法的 PNG 文件（签名不匹配）")
    if header[12:16] != b"IHDR":
        raise AssetImportError(f"'{path}' 的第一个数据块不是 IHDR，无法读取图片尺寸")
    width, height = struct.unpack(">II", header[16:24])
    return width, height


def _discover_layers(src_dir: Path) -> dict[str, Path]:
    """扫描 src_dir 下的 <layer>.png，返回 {layer_name: path}；忽略未识别文件名并警告。"""
    found: dict[str, Path] = {}
    for item in sorted(src_dir.iterdir()):
        if not (item.is_file() and item.suffix.lower() == ".png"):
            continue
        name = item.stem
        if name not in RECOGNIZED_LAYERS:
            log(f"警告：忽略未识别的地图分层文件 '{item.name}'（合法分层名: {sorted(RECOGNIZED_LAYERS)}）")
            continue
        found[name] = item
    return found


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    assets_root = resolve_root(args.assets_root, repo_root, "assets")
    data_root = resolve_root(args.data_root, repo_root, "data")

    src_dir = Path(args.src).resolve()
    if not src_dir.is_dir():
        raise FileNotFoundError(f"源目录不存在: {src_dir}")

    map_id = f"world.{args.map}"
    validate_id(map_id, "world", "--map")

    layers = _discover_layers(src_dir)
    missing_required = [name for name, required in RECOGNIZED_LAYERS.items() if required and name not in layers]
    if missing_required:
        raise AssetImportError(
            f"源目录 '{src_dir}' 缺少必需的地图分层文件: "
            + ", ".join(f"{name}.png" for name in missing_required)
        )

    # 消费方反馈第 75 条（ADR-0053）：目录/各层文件路径改由 ref_conventions 共享函数给出，不再自行
    # 拼接 "maps" / args.map / f"{name}.png" 字面量。
    out_dir = assets_root / args.dataset / map_directory(map_id)
    plan = [(path, assets_root / args.dataset / _LAYER_FILE_FUNCS[name](map_id)) for name, path in layers.items()]

    if args.spawn:
        spawn_points = []
        for idx, spec in enumerate(args.spawn):
            x, y, facing = _parse_spawn(spec)
            spawn_id = f"{map_id}.spawn.default" if idx == 0 else f"{map_id}.spawn.{idx}"
            spawn_points.append({"id": spawn_id, "position": {"x": x, "y": y}, "facing": facing})
    else:
        spawn_points = [
            {"id": f"{map_id}.spawn.default", "position": {"x": 0, "y": 0}, "facing": 0}
        ]

    # 消费方反馈第 59 条（ADR-0036）：image_size_px 从 ground.png 实际读出（该层是 RECOGNIZED_LAYERS
    # 里的必需层，missing_required 检查已保证此时 layers["ground"] 一定存在）；--origin-px 省略时
    # 默认世界原点位于图片左下角（0, ground 高度）。
    ground_width, ground_height = _read_png_size(layers["ground"])
    origin_px = _parse_origin_px(args.origin_px) if args.origin_px else (0.0, float(ground_height))

    row = {
        "id": map_id,
        "scene_ref": f"scene.{args.map}",
        "nav_ref": f"nav.{args.map}",
        "spawn_points": spawn_points,
        "image_transform": {
            "pixels_per_unit": args.pixels_per_unit,
            "origin_px": {"x": origin_px[0], "y": origin_px[1]},
            "image_size_px": {"x": ground_width, "y": ground_height},
        },
    }

    world_map_path = data_root / args.dataset / "world" / "world.map.json"

    if args.dry_run:
        for src, dst in plan:
            log(f"计划复制地图分层图: {src} -> {dst}", dry_run=True)
        log(f"计划合并写入 world.map 行: {row['id']} -> {world_map_path}", dry_run=True)
        return 0

    out_dir.mkdir(parents=True, exist_ok=True)
    for src, dst in plan:
        shutil.copy2(src, dst)

    merge_write_row(world_map_path, "world.map", row, key_field="id")

    log(f"已复制 {len(plan)} 个地图分层文件到: {out_dir}")
    log(f"已合并写入 world.map 行: {row['id']}")
    return 0
