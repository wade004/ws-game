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
- 规范化落到 ``assets/<dataset>/maps/<map>/<layer>.png``（与 ``assets/_placeholder/maps/`` 同一套
  目录口径，见 11 第 1 节占位资产包）。
- 生成/合并一行 ``world.map`` 数据（04 第 7.1 节总索引 + 05 第 4.1 节字段定义）：``scene_ref``/
  ``nav_ref`` 按"资源引用 id = 类别前缀 + 地图名"的既有约定固定为 ``scene.<map>``/``nav.<map>``
  （与 ``adapters/unity`` 侧 ``UnityResourceLoader`` 的 ``Scene``/``NavMesh`` 种类解析规则同一套
  引用 id 命名口径，见该包 README"资源 id → 路径规则"一节；本工具不生成场景/导航资源本身——
  05 第 4.1 节"导航与碰撞...由引擎适配层侧在场景中手工绘制"，只声明引用 id 与地面/遮挡分层图）。
  ``spawn_points`` 缺省时写一条 ``<map>.spawn.default``（世界原点、朝向 0），可用 ``--spawn``
  重复传入覆盖。
"""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path

from .common import AssetImportError, find_repo_root, log, merge_write_row, resolve_root, validate_id

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

    out_dir = assets_root / args.dataset / "maps" / args.map
    plan = [(path, out_dir / f"{name}.png") for name, path in layers.items()]

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

    row = {
        "id": map_id,
        "scene_ref": f"scene.{args.map}",
        "nav_ref": f"nav.{args.map}",
        "spawn_points": spawn_points,
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
