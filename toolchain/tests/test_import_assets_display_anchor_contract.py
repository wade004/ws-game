"""导入工具 sprite 子命令写出的 ``display.map.anchor_points`` 必须符合运行期/校验器的锚点契约（消费方反馈 P2 缺口 2）。

症状：用导入工具导入任何带锚点的精灵集后，``display.map`` 行里的 ``anchor_points`` 是裸 ``{x, y}``，
而 ``DisplayInfo.ParseAnchorDef`` 与校验器要求每条锚点是 ``{parent_layer, offset}``——工具产出过不了框架自己的校验器，
游戏启动数据校验即阻断。此前 ``test_import_assets.py`` 只断言了裸 ``{x, y}`` 形状，没有任何一条用例把导入产出交给校验器。

本文件两条用例：
* 不变量：导入产出的每条锚点都有非空 ``parent_layer`` 与带 ``x``/``y`` 数值的 ``offset``（平图与纸娃娃分层两种精灵集各一）；
* 复现：把导入产出交给 ``Validator.dll`` 黑盒校验，退出码 0 且输出里没有锚点相关的 required_field 错误。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

from _dotnet_cli import get_cli_dll, run_cli as run_validator

sys.path.insert(0, str(Path(__file__).resolve().parent))
from test_import_assets import (  # noqa: E402  (需要先放开 sys.path)
    CANONICAL_8,
    build_flat_sprite_src,
    build_layered_sprite_src,
    run_cli as run_importer,
    write_json,
)


def _import(tmp_path: Path, layered: bool) -> Path:
    src = tmp_path / "src" / "critter"
    if layered:
        build_layered_sprite_src(src, CANONICAL_8)
    else:
        build_flat_sprite_src(src, CANONICAL_8)
    anchors = tmp_path / "anchors.json"
    write_json(anchors, {slot: {"root": [20, 58], "overhead": [20, 4]} for slot in CANONICAL_8})
    data_root = tmp_path / "data"
    code, output = run_importer(
        [
            "sprite", str(src), "--dataset", "_test", "--category", "creature", "--logical-id", "creature.critter_anchor",
            "--direction-count", "8", "--anchors", str(anchors),
            "--assets-root", str(tmp_path / "assets"), "--data-root", str(data_root),
        ]
    )
    assert code == 0, output
    return data_root


@pytest.mark.parametrize("layered", [True, False], ids=["paperdoll_layers", "flat"])
def test_imported_anchor_points_have_parent_layer_and_offset(tmp_path: Path, layered: bool) -> None:
    data_root = _import(tmp_path, layered)
    row = json.loads((data_root / "_test" / "display" / "display.map.json").read_text(encoding="utf-8"))["rows"][0]
    anchors = row["anchor_points"]
    assert set(anchors) == {"root", "overhead"}
    layers = row.get("paperdoll_layers", [])
    for name, value in anchors.items():
        assert isinstance(value.get("parent_layer"), str) and value["parent_layer"], f"{name} 缺 parent_layer：{value}"
        assert set(value["offset"]) == {"x", "y"}, f"{name} 的 offset 形状不对：{value}"
        assert all(isinstance(v, (int, float)) for v in value["offset"].values())
        if layers:
            assert value["parent_layer"] in layers, f"{name} 的 parent_layer 不在 paperdoll_layers 里：{value}"


@pytest.mark.parametrize("layered", [True, False], ids=["paperdoll_layers", "flat"])
def test_imported_display_map_passes_validator(tmp_path: Path, layered: bool) -> None:
    data_root = _import(tmp_path, layered)
    proc = run_validator(get_cli_dll("Validator"), ["--data-root", str(data_root / "_test")])
    assert "anchor_points" not in proc.stdout, proc.stdout[-1500:]
    assert proc.returncode == 0, proc.stdout[-1500:] + proc.stderr[-800:]
