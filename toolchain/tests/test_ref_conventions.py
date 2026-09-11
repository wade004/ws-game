"""``toolchain/asset_import/ref_conventions.py``（消费方反馈第 32 条，ADR-0025）的单元测试。

判断记录（跨语言对照）：本文件与 C# 侧
``core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs`` 使用同一组样例（取自
``toolchain/import_sample_assets.py`` 产出的真实 ``data/_sample/display/display.map.json``
行）、断言同一个期望字符串；两侧各自独立实现同一条规则，不互相调用，任一侧改动规则而另一侧未
同步会被各自语言的测试独立捕获。

运行：

```
python -m pytest toolchain/tests/test_ref_conventions.py -v
```
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from asset_import.ref_conventions import (  # noqa: E402
    icon_file,
    sprite_set_directory,
    strip_category_prefix,
    try_parse_icon_id,
    try_parse_sprite_set_id,
)


@pytest.mark.parametrize(
    "value, expected",
    [
        ("sprite.creature.wolf_grey", "creature_wolf_grey"),
        ("icon.item.sample_blade", "item_sample_blade"),
        ("vfx.sample_cast_circle", "sample_cast_circle"),
    ],
)
def test_strip_category_prefix(value: str, expected: str) -> None:
    assert strip_category_prefix(value) == expected


def test_strip_category_prefix_no_dot_returns_original() -> None:
    assert strip_category_prefix("nodothere") == "nodothere"


@pytest.mark.parametrize(
    "sprite_set_id, expected",
    [
        ("sprite.creature.wolf_grey", "sprites/creature_wolf_grey"),
        ("sprite.item.sample_blade", "sprites/item_sample_blade"),
        ("sprite.gobj.sample_chest", "sprites/gobj_sample_chest"),
    ],
)
def test_sprite_set_directory_matches_sprite_cmd_output_path(sprite_set_id: str, expected: str) -> None:
    assert sprite_set_directory(sprite_set_id) == expected


@pytest.mark.parametrize(
    "icon_id, expected",
    [
        ("icon.creature.sample_beast", "icons/creature/sample_beast.png"),
        ("icon.creature.sample_hero", "icons/creature/sample_hero.png"),
        ("icon.gobj.sample_chest", "icons/gobj/sample_chest.png"),
        ("icon.gobj.sample_door", "icons/gobj/sample_door.png"),
        ("icon.gobj.sample_save_point", "icons/gobj/sample_save_point.png"),
    ],
)
def test_icon_file_matches_icon_cmd_output_path(icon_id: str, expected: str) -> None:
    assert icon_file(icon_id) == expected


def test_icon_file_missing_name_segment_raises_value_error() -> None:
    with pytest.raises(ValueError):
        icon_file("icon.onlycategory")


@pytest.mark.parametrize(
    "relative_directory, expected_id",
    [
        ("sprites/creature_wolf_grey", "sprite.creature.wolf_grey"),
        ("sprites/item_sample_blade", "sprite.item.sample_blade"),
        ("/sprites/gobj_sample_chest/", "sprite.gobj.sample_chest"),
    ],
)
def test_try_parse_sprite_set_id_round_trips(relative_directory: str, expected_id: str) -> None:
    assert try_parse_sprite_set_id(relative_directory) == expected_id


@pytest.mark.parametrize(
    "relative_directory",
    ["", "vfx/sample_cast_circle", "sprites/noUnderscoreHere"],
)
def test_try_parse_sprite_set_id_malformed_returns_none(relative_directory: str) -> None:
    assert try_parse_sprite_set_id(relative_directory) is None


@pytest.mark.parametrize(
    "relative_file_path, expected_id",
    [
        ("icons/creature/sample_beast.png", "icon.creature.sample_beast"),
        ("icons/gobj/sample_chest.png", "icon.gobj.sample_chest"),
        ("/icons/item/sample_blade.png", "icon.item.sample_blade"),
    ],
)
def test_try_parse_icon_id_round_trips(relative_file_path: str, expected_id: str) -> None:
    assert try_parse_icon_id(relative_file_path) == expected_id


@pytest.mark.parametrize(
    "relative_file_path",
    ["", "sprites/creature_wolf_grey", "icons/onlyonesegment.png", "icons/a/b/c.png"],
)
def test_try_parse_icon_id_malformed_returns_none(relative_file_path: str) -> None:
    assert try_parse_icon_id(relative_file_path) is None


@pytest.mark.parametrize(
    "sprite_set_id_text",
    ["sprite.creature.wolf_grey", "sprite.item.sample_blade", "sprite.gobj.sample_chest"],
)
def test_sprite_set_directory_round_trips_through_try_parse(sprite_set_id_text: str) -> None:
    directory = sprite_set_directory(sprite_set_id_text)
    assert try_parse_sprite_set_id(directory) == sprite_set_id_text


@pytest.mark.parametrize(
    "icon_id_text",
    ["icon.creature.sample_beast", "icon.item.sample_blade", "icon.gobj.sample_save_point"],
)
def test_icon_file_round_trips_through_try_parse(icon_id_text: str) -> None:
    file_path = icon_file(icon_id_text)
    assert try_parse_icon_id(file_path) == icon_id_text
