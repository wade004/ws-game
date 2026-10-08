"""示例数据里会在世界里建视图的非生物外形必须是"分层精灵集 + paperdoll_layers"（2026-10-09）。

背景：视图层（UnityViewFactory.CreateView）对"整身精灵集（display.map 行没有 paperdoll_layers）用于非生物"发一次诊断警告——
整身外形没有静态底图、只靠动画剪辑显示，而非生物不挂默认动画，永远画不出东西。2.9.0 起示例数据里的箱子/门/存档点/弩矢/
任务标记/掉落堆就是这种组合，竖切用例靠测试出口放行了警告；这里改数据后用静态不变量守住（引擎侧的对应运行时用例是
VerticalSliceTests.SampleData_EveryWorldViewNonCreatureDisplay_CreatesViewWithoutDiagnosticWarnings）。

不变量由数据算出，不写死行数：分类 gobj / projectile、以及 logical_id 以 loot. 开头（掉落堆）的 kind=sprite 行
- 必须声明非空 paperdoll_layers；
- 精灵集目录下每个方向档位都有 <层>.png（分层布局），而不是整身布局的 <方向>.png。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "toolchain"))

from asset_import.ref_conventions import sprite_set_directory  # noqa: E402

DISPLAY_MAP = REPO_ROOT / "data" / "_sample" / "display" / "display.map.json"
ASSETS_SAMPLE = REPO_ROOT / "assets" / "_sample"


def _world_view_rows() -> list[dict]:
    rows = json.loads(DISPLAY_MAP.read_text(encoding="utf-8"))["rows"]
    picked = [
        r
        for r in rows
        if r.get("kind") == "sprite"
        and (r.get("category") in ("gobj", "projectile") or str(r.get("logical_id", "")).startswith("loot."))
    ]
    assert len(picked) >= 3, "示例数据应当至少有箱子/门/存档点这类世界物件外形，否则本不变量什么都没验"
    return picked


def test_world_view_rows_declare_paperdoll_layers() -> None:
    missing = [r["id"] for r in _world_view_rows() if not r.get("paperdoll_layers")]
    assert missing == [], f"这些非生物世界物件外形是整身精灵集（没有 paperdoll_layers），视图层会静默不画并发警告：{missing}"


def test_world_view_sprite_sets_are_layered_on_disk() -> None:
    problems: list[str] = []
    for row in _world_view_rows():
        set_dir = ASSETS_SAMPLE / sprite_set_directory(row["sprite_set_id"])
        layers = row.get("paperdoll_layers", [])
        for slot_dir in sorted(p for p in set_dir.iterdir() if p.is_dir()):
            for layer in layers:
                if not (slot_dir / f"{layer}.png").is_file():
                    problems.append(f"{row['id']}: {slot_dir.name}/{layer}.png 缺失")
        whole_body_files = [p.name for p in set_dir.glob("*.png") if p.stem != "atlas"]
        if whole_body_files:
            problems.append(f"{row['id']}: 精灵集目录里还有整身布局文件 {whole_body_files}")
        if not any(p.is_dir() for p in set_dir.iterdir()):
            problems.append(f"{row['id']}: 精灵集目录里没有任何方向档位子目录")
    assert problems == [], problems


def test_world_view_rows_share_sets_only_with_other_layered_rows() -> None:
    """复用同一个精灵集的所有行必须一致地声明分层：整身行与分层行共用一个集会让其中一类行画不出（或多画一层）。"""
    rows = json.loads(DISPLAY_MAP.read_text(encoding="utf-8"))["rows"]
    world_sets = {r["sprite_set_id"] for r in _world_view_rows()}
    inconsistent = [
        r["id"] for r in rows if r.get("sprite_set_id") in world_sets and r.get("kind") == "sprite" and not r.get("paperdoll_layers")
    ]
    assert inconsistent == [], f"这些行与世界物件外形共用分层精灵集，却没有声明 paperdoll_layers：{inconsistent}"
