"""``bake-motion`` 子命令：动画剪辑的根位移采样 -> ``skill.motion_curve`` 行（手感设计/02 第 4 节，ADR-0147）。

根运动驱动（``timeline.motion.driver: root_motion``）已删除：逻辑层位移权威只在逻辑，不读剪辑、不读骨骼。想要"位移形状来自动画"的作者
走这条导入路径——把剪辑沿前进方向的根位移采样（时间 -> 累计距离）烘焙成归一化断点表，写进数据表 ``skill.motion_curve``；
技能时间线的动作位移用 ``curve: custom:<行 id>`` 引用它，运行期运动层按固定步求值（与内建曲线同一条路径）。

输入文件（JSON）::

    {"clip": "anim.<剪辑资源引用>", "samples": [{"t_ms": 0, "distance": 0.0}, {"t_ms": 50, "distance": 0.12}, ...]}

``distance`` 是该时刻起算的累计前进距离（世界单位，可由引擎侧导出脚本从根骨轨迹求得），不要求等间隔采样。烘焙规则（判断记录）：

- 时间归一到 [0,1]（首采样为 0、末采样为 1），距离按总位移归一到 [0,1]；总位移必须为正（没有位移的剪辑不需要曲线）。
- 距离取累计最大值（曲线只增不减：倒退的采样噪声被抹平；真要往回退的位移不是"一次动作位移曲线"能表达的，拆成两个动作位移）。
- 断点数超过 ``--max-points``（缺省 32）时按 x 等间隔重采样，端点恒保留；断点 x 严格递增。
- 行里同时记 ``source_clip``（来源剪辑）与 ``source_distance``（总位移，世界单位），供作者核对动作位移的 ``distance`` 与剪辑一致；
  运行期不读这两个字段。
"""

from __future__ import annotations

import argparse
from pathlib import Path

from .common import AssetImportError, find_repo_root, log, merge_write_row, read_json, resolve_root

TABLE = "skill.motion_curve"
ID_PREFIX = "skill.motion_curve."
DEFAULT_MAX_POINTS = 32
ROUND_DIGITS = 6


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("samples", help="根位移采样文件（JSON，见模块说明）")
    parser.add_argument("--id", required=True, help="曲线行 id，形如 skill.motion_curve.<名>")
    parser.add_argument("--dataset", default="_sample", help="目标数据集名，默认 _sample")
    parser.add_argument("--data-root", default=None, help="数据根目录，默认仓库 data/")
    parser.add_argument("--max-points", type=int, default=DEFAULT_MAX_POINTS, help="断点数上限，默认 32（至少 2）")
    parser.add_argument("--dry-run", action="store_true", help="只打印计划，不写任何文件")


def bake_points(samples: list[dict], max_points: int = DEFAULT_MAX_POINTS) -> tuple[list[dict], float]:
    """采样 -> (归一化断点列表, 总位移)。规则见模块说明。"""
    if max_points < 2:
        raise AssetImportError("--max-points 至少为 2（两个端点）")
    pairs = []
    for i, s in enumerate(samples):
        if not isinstance(s, dict) or not isinstance(s.get("t_ms"), (int, float)) or not isinstance(s.get("distance"), (int, float)):
            raise AssetImportError(f"samples[{i}] 必须是 {{t_ms: 数字, distance: 数字}}")
        pairs.append((float(s["t_ms"]), float(s["distance"])))
    if len(pairs) < 2:
        raise AssetImportError("samples 至少要有 2 个采样")
    pairs.sort(key=lambda p: p[0])
    t0, d0 = pairs[0]
    duration = pairs[-1][0] - t0
    if not duration > 0:
        raise AssetImportError("samples 的时间跨度必须为正")

    cumulative: list[tuple[float, float]] = []
    best = 0.0
    last_t = None
    for t, d in pairs:
        if last_t is not None and t == last_t:
            continue  # 同一时刻只取第一个采样
        last_t = t
        best = max(best, d - d0)
        cumulative.append(((t - t0) / duration, best))
    total = cumulative[-1][1]
    if not total > 0:
        raise AssetImportError("剪辑没有前进位移（总位移 <= 0），不需要位移曲线")
    normalized = [(x, min(1.0, y / total)) for x, y in cumulative]

    if len(normalized) > max_points:
        resampled = []
        for i in range(max_points):
            x = i / (max_points - 1)
            resampled.append((x, _interpolate(normalized, x)))
        normalized = resampled
    normalized[0] = (0.0, 0.0)
    normalized[-1] = (1.0, 1.0)
    points = [{"x": round(x, ROUND_DIGITS), "y": round(y, ROUND_DIGITS)} for x, y in normalized]
    return points, total


def _interpolate(points: list[tuple[float, float]], x: float) -> float:
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        if x0 <= x <= x1:
            return y0 if x1 == x0 else y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    return points[-1][1]


def build_row(row_id: str, document: dict, max_points: int = DEFAULT_MAX_POINTS) -> dict:
    if not row_id.startswith(ID_PREFIX) or row_id == ID_PREFIX:
        raise AssetImportError(f"--id 必须形如 {ID_PREFIX}<名>，实际: {row_id}")
    if not isinstance(document, dict) or not isinstance(document.get("samples"), list):
        raise AssetImportError("采样文件必须是 {clip, samples: [...]} 对象")
    points, total = bake_points(document["samples"], max_points)
    row: dict = {"id": row_id, "points": points}
    if document.get("clip"):
        row["source_clip"] = document["clip"]
    row["source_distance"] = round(total, ROUND_DIGITS)
    return row


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    data_root = resolve_root(args.data_root, repo_root, "data")
    src = Path(args.samples)
    if not src.is_file():
        raise FileNotFoundError(f"采样文件不存在: {src}")
    row = build_row(args.id, read_json(src), args.max_points)
    table_path = data_root / args.dataset / "skill" / f"{TABLE}.json"
    if args.dry_run:
        log(f"计划写入 {table_path}: {args.id}（{len(row['points'])} 个断点，总位移 {row['source_distance']}）", dry_run=True)
        return 0
    merge_write_row(table_path, TABLE, row)
    log(f"已写入 {table_path}: {args.id}（{len(row['points'])} 个断点，总位移 {row['source_distance']}）")
    print(f"[bake-motion] {args.id} -> {table_path}")
    return 0
