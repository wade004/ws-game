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

import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from asset_import.ref_conventions import (  # noqa: E402
    KNOWN_CATEGORIES,
    AssetRefPathSpace,
    anim_clip_logical_path,
    icon_file,
    map_decal_file,
    map_directory,
    map_ground_file,
    map_nav_hint_file,
    map_overlay_file,
    model_logical_path,
    paperdoll_layer_file,
    resolve_path_space,
    sfx_resource_file,
    sprite_anim_dir,
    sprite_set_directory,
    strip_category_prefix,
    try_parse_icon_id,
    try_parse_sprite_set_id,
    vfx_resource_dir,
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


# 消费方反馈第 65 条：以下四组用例与
# core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs 对应用例使用同一组输入/期望
# 字符串，两侧各自独立实现、互相不调用，任一侧改动规则而另一侧未同步会被各自语言的测试独立捕获
# （同本文件顶部"跨语言对照"判断记录）。


@pytest.mark.parametrize(
    "resource_ref, expected",
    [
        ("vfx.sample_cast_circle", "vfx/sample_cast_circle"),
        ("vfx.fire_impact", "vfx/fire_impact"),
    ],
)
def test_vfx_resource_dir_matches_vfx_cmd_output_path(resource_ref: str, expected: str) -> None:
    assert vfx_resource_dir(resource_ref) == expected


@pytest.mark.parametrize(
    "resource_ref, expected",
    [
        ("sfx.sword_hit_v0", "sfx/sword_hit_v0.wav"),
        ("sfx.sword_hit_v1", "sfx/sword_hit_v1.wav"),
    ],
)
def test_sfx_resource_file_matches_sfx_cmd_output_path(resource_ref: str, expected: str) -> None:
    assert sfx_resource_file(resource_ref) == expected


@pytest.mark.parametrize(
    "resource_ref, expected",
    [
        ("anim.idle", "GameFoundation/anim_clips/idle"),
        ("anim.attack", "GameFoundation/anim_clips/attack"),
    ],
)
def test_anim_clip_logical_path_matches_unity_resource_loader(resource_ref: str, expected: str) -> None:
    assert anim_clip_logical_path(resource_ref) == expected


@pytest.mark.parametrize(
    "resource_ref, expected",
    [
        ("model.placeholder_biped", "GameFoundation/models/placeholder_biped"),
    ],
)
def test_model_logical_path_matches_unity_resource_loader(resource_ref: str, expected: str) -> None:
    assert model_logical_path(resource_ref) == expected


# ADR-0038 决策 2/4：以下用例与 core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs
# 对应用例使用同一组输入/期望字符串，两侧各自独立实现、互相不调用（同本文件顶部"跨语言对照"判断记录）。


@pytest.mark.parametrize(
    "resource_ref, expected",
    [
        ("sprite_anim.sample_hero_idle", "sprite_anim/sample_hero_idle"),
        ("sprite_anim.sample_hero_attack", "sprite_anim/sample_hero_attack"),
    ],
)
def test_sprite_anim_dir_matches_vfx_resource_dir_style(resource_ref: str, expected: str) -> None:
    assert sprite_anim_dir(resource_ref) == expected


@pytest.mark.parametrize(
    "resource_ref, expected",
    [
        ("paperdoll.item.sample_hero_hat_test", "paperdoll/item_sample_hero_hat_test.png"),
        ("paperdoll.item.sample_cloak", "paperdoll/item_sample_cloak.png"),
    ],
)
def test_paperdoll_layer_file_returns_flat_png_under_own_root(resource_ref: str, expected: str) -> None:
    assert paperdoll_layer_file(resource_ref) == expected


@pytest.mark.parametrize(
    "resource_ref, expected_space, expected_path",
    [
        ("sprite.creature.wolf_grey", AssetRefPathSpace.ASSET_ROOT_RELATIVE, "sprites/creature_wolf_grey"),
        ("icon.item.sample_blade", AssetRefPathSpace.ASSET_ROOT_RELATIVE, "icons/item/sample_blade.png"),
        ("vfx.sample_cast_circle", AssetRefPathSpace.ASSET_ROOT_RELATIVE, "vfx/sample_cast_circle"),
        ("sfx.sword_hit_v0", AssetRefPathSpace.ASSET_ROOT_RELATIVE, "sfx/sword_hit_v0.wav"),
        ("sprite_anim.sample_hero_idle", AssetRefPathSpace.ASSET_ROOT_RELATIVE, "sprite_anim/sample_hero_idle"),
        (
            "paperdoll.item.sample_hero_hat_test",
            AssetRefPathSpace.ASSET_ROOT_RELATIVE,
            "paperdoll/item_sample_hero_hat_test.png",
        ),
        ("anim.idle", AssetRefPathSpace.ENGINE_LOGICAL_PATH, "GameFoundation/anim_clips/idle"),
        ("model.placeholder_biped", AssetRefPathSpace.ENGINE_LOGICAL_PATH, "GameFoundation/models/placeholder_biped"),
    ],
)
def test_resolve_path_space_dispatches_by_category_prefix(
    resource_ref: str, expected_space: AssetRefPathSpace, expected_path: str
) -> None:
    space, path = resolve_path_space(resource_ref)
    assert space == expected_space
    assert path == expected_path


def test_resolve_path_space_unknown_category_raises_with_legal_set() -> None:
    with pytest.raises(ValueError) as excinfo:
        resolve_path_space("bogus.sample_thing")
    message = str(excinfo.value)
    assert "bogus" in message
    for known in KNOWN_CATEGORIES:
        assert known in message


def test_resolve_path_space_no_dot_raises_with_legal_set() -> None:
    with pytest.raises(ValueError) as excinfo:
        resolve_path_space("nodothere")
    assert "nodothere" in str(excinfo.value)


# 消费方反馈第 75 条（ADR-0053）：地图分层图路径约定纳入公开契约。以下用例与
# core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs 对应用例使用同一组输入/期望
# 字符串，两侧各自独立实现、互相不调用（同本文件顶部"跨语言对照"判断记录）；本节末尾另有两组更强的
# 验收测试——跨语言一致性（两侧实际计算结果互相比对）、与 map_cmd.py 改动前格式的历史对齐。


@pytest.mark.parametrize(
    "map_id, expected",
    [
        ("world.sample_field", "maps/sample_field"),
        ("world.another_map", "maps/another_map"),
    ],
)
def test_map_directory_matches_map_cmd_py_out_dir(map_id: str, expected: str) -> None:
    assert map_directory(map_id) == expected


@pytest.mark.parametrize(
    "map_id, expected",
    [
        ("world.sample_field", "maps/sample_field/ground.png"),
        ("world.another_map", "maps/another_map/ground.png"),
    ],
)
def test_map_ground_file_matches_map_cmd_py_output_path(map_id: str, expected: str) -> None:
    assert map_ground_file(map_id) == expected


@pytest.mark.parametrize(
    "map_id, expected",
    [("world.sample_field", "maps/sample_field/overlay.png")],
)
def test_map_overlay_file_matches_map_cmd_py_output_path(map_id: str, expected: str) -> None:
    assert map_overlay_file(map_id) == expected


@pytest.mark.parametrize(
    "map_id, expected",
    [("world.sample_field", "maps/sample_field/decal.png")],
)
def test_map_decal_file_matches_map_cmd_py_output_path(map_id: str, expected: str) -> None:
    assert map_decal_file(map_id) == expected


@pytest.mark.parametrize(
    "map_id, expected",
    [("world.sample_field", "maps/sample_field/nav_hint.png")],
)
def test_map_nav_hint_file_matches_map_cmd_py_output_path(map_id: str, expected: str) -> None:
    assert map_nav_hint_file(map_id) == expected


# --- 与既有格式对齐测试：从 map_cmd.py 改用共享函数之前的历史提交里取出原始格式，证明本次改动是
# 纯抽取，没有借机改动格式本身（消费方反馈第 75 条验收标准）。---

GIT = shutil.which("git")

# map_cmd.py 改用 ref_conventions 共享函数之前的最后一次提交（1.49.0 发布提交，本次改动的分支
# 起点）；该提交在远端/本地历史中不可变，可放心作为"改动前格式"的固定锚点长期使用。
_PRE_REFACTOR_MAP_CMD_PY_SHA = "e8b48aa1"


def _historical_map_cmd_py_source() -> str:
    result = subprocess.run(
        [GIT, "-C", str(REPO_ROOT), "show", f"{_PRE_REFACTOR_MAP_CMD_PY_SHA}:toolchain/asset_import/map_cmd.py"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=30,
    )
    assert result.returncode == 0, "读取 map_cmd.py 历史版本失败：\n" + result.stderr
    return result.stdout


@pytest.mark.skipif(GIT is None, reason="本机找不到 git，跳过历史格式对齐测试")
@pytest.mark.parametrize("map_name", ["sample_field", "another_map"])
def test_map_layer_paths_match_pre_refactor_map_cmd_py_format(map_name: str) -> None:
    """与既有格式对齐测试：核实改动前 map_cmd.py 确实是
    ``out_dir = assets_root / args.dataset / "maps" / args.map`` +
    ``out_dir / f"{name}.png"`` 这一形状（钉住前提，防止本测试因源码后续被进一步改写而失去意义），
    再按同一公式手工推出旧格式路径（资产根/dataset 两段前缀之外的剩余部分），与新增共享函数的
    输出逐字节比对。
    """
    historical_source = _historical_map_cmd_py_source()
    assert 'out_dir = assets_root / args.dataset / "maps" / args.map' in historical_source
    assert 'plan = [(path, out_dir / f"{name}.png") for name, path in layers.items()]' in historical_source

    map_id = f"world.{map_name}"
    pre_refactor_directory = f"maps/{map_name}"
    assert map_directory(map_id) == pre_refactor_directory
    for layer_func in (map_ground_file, map_overlay_file, map_decal_file, map_nav_hint_file):
        layer_name = layer_func.__name__[len("map_"):-len("_file")]
        pre_refactor_path = f"{pre_refactor_directory}/{layer_name}.png"
        assert layer_func(map_id) == pre_refactor_path


# --- 跨语言一致性测试：对同一组输入，C# 侧真实运行期计算结果（经 toolchain/map_ref_probe 子进程
# 取得）与本文件 Python 侧对应函数各自独立算出的结果逐字节相等——断言的是两侧实际算出来的字符串
# 相等，不是各自跟一个硬编码常量比（消费方反馈第 75 条验收标准）。沿用
# test_abi_surface_compare.py 已确立的"构建一个最小消费方工程、经子进程拿真实运行期结果"惯例。---

DOTNET = shutil.which("dotnet")
_MAP_REF_PROBE_PROJ = REPO_ROOT / "toolchain" / "map_ref_probe" / "MapRefProbe.csproj"


@pytest.fixture(scope="session")
def map_ref_probe_dll(tmp_path_factory: pytest.TempPathFactory) -> Path:
    """构建一次 toolchain/map_ref_probe（session 级缓存，避免每个用例都重新 dotnet build）。"""
    out_dir = tmp_path_factory.mktemp("map_ref_probe_build")
    result = subprocess.run(
        [DOTNET, "build", str(_MAP_REF_PROBE_PROJ), "-c", "Release", "--nologo", "-o", str(out_dir)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=300,
    )
    assert result.returncode == 0, "map_ref_probe 构建失败：\n" + result.stdout + result.stderr
    dll = out_dir / "MapRefProbe.dll"
    assert dll.is_file(), f"未找到构建产物：{dll}"
    return dll


def _run_map_ref_probe(dll: Path, map_id: str) -> dict:
    result = subprocess.run(
        [DOTNET, str(dll), map_id],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
    )
    assert result.returncode == 0, f"MapRefProbe 运行失败（{map_id}）：\n" + result.stderr
    return json.loads(result.stdout)


@pytest.mark.skipif(DOTNET is None, reason="本机找不到 dotnet，跳过跨语言一致性测试")
@pytest.mark.parametrize("map_name", ["sample_field", "another_map", "zone_a"])
def test_map_layer_paths_cross_language_consistency(map_ref_probe_dll: Path, map_name: str) -> None:
    map_id = f"world.{map_name}"
    csharp_result = _run_map_ref_probe(map_ref_probe_dll, map_id)
    assert csharp_result["directory"] == map_directory(map_id)
    assert csharp_result["ground"] == map_ground_file(map_id)
    assert csharp_result["overlay"] == map_overlay_file(map_id)
    assert csharp_result["decal"] == map_decal_file(map_id)
    assert csharp_result["nav_hint"] == map_nav_hint_file(map_id)
