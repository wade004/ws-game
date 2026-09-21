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
    dataset_assets_directory,
    dataset_data_directory,
    icon_file,
    map_decal_file,
    map_directory,
    map_ground_file,
    map_nav_hint_file,
    map_overlay_file,
    model_logical_path,
    paperdoll_layer_file,
    resolve_assets_root,
    resolve_data_root,
    resolve_path_space,
    sfx_resource_file,
    sprite_anim_atlas_file,
    sprite_anim_dir,
    sprite_anim_frames_file,
    sprite_set_atlas_file,
    sprite_set_directory,
    strip_category_prefix,
    try_get_dataset_name,
    try_parse_icon_id,
    try_parse_sprite_set_id,
    vfx_atlas_file,
    vfx_frames_file,
    vfx_resource_dir,
)
from asset_import.common import resolve_root as _common_resolve_root  # noqa: E402


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


# 消费方反馈第 76 条（ADR-0054）：资产/数据根目录约定纳入公开契约。以下用例与
# core/foundation/engine_adapter/tests/AssetRootConventionsTests.cs 对应用例使用同一组输入/期望
# 字符串，两侧各自独立实现、互相不调用；本节末尾另有三组更强的验收测试——与改动前
# common.py.resolve_root 的历史格式对齐、全仓重复实现排查记录、跨语言一致性（两侧实际计算结果
# 互相比对）。


def test_resolve_assets_root_omitted_defaults_to_repo_root_assets() -> None:
    assert resolve_assets_root(REPO_ROOT, None) == REPO_ROOT / "assets"


def test_resolve_assets_root_relative_override_resolves_against_repo_root() -> None:
    assert resolve_assets_root(REPO_ROOT, "custom_assets") == REPO_ROOT / "custom_assets"


def test_resolve_assets_root_absolute_override_used_as_is() -> None:
    # 用真实 REPO_ROOT 拼出的绝对路径作为覆盖值（而不是硬编码 POSIX 字面量"/abs/assets"）——
    # pathlib 的 is_absolute() 判定在 Windows 下要求盘符，"/abs/assets" 这种"根相对"写法在
    # Windows 上不算绝对路径，会走错分支、看似巧合地得到同一结果（因为 Path 的 / 运算符对
    # "带根但不带盘符"的右操作数也会丢弃左操作数），掩盖真实的分支覆盖。用平台相关的绝对路径
    # 才能确实覆盖到"覆盖值已是绝对路径，原样返回"这一分支。
    absolute_override = str(REPO_ROOT / "abs_assets")
    assert resolve_assets_root(REPO_ROOT, absolute_override) == Path(absolute_override)


def test_resolve_data_root_omitted_defaults_to_repo_root_data() -> None:
    assert resolve_data_root(REPO_ROOT, None) == REPO_ROOT / "data"


def test_resolve_data_root_relative_override_resolves_against_repo_root() -> None:
    assert resolve_data_root(REPO_ROOT, "custom_data") == REPO_ROOT / "custom_data"


def test_resolve_data_root_absolute_override_used_as_is() -> None:
    absolute_override = str(REPO_ROOT / "abs_data")
    assert resolve_data_root(REPO_ROOT, absolute_override) == Path(absolute_override)


def test_dataset_assets_directory() -> None:
    assert dataset_assets_directory(Path("/repo/assets"), "_sample") == Path("/repo/assets/_sample")


def test_dataset_data_directory() -> None:
    assert dataset_data_directory(Path("/repo/data"), "_sample") == Path("/repo/data/_sample")


# --- 消费方反馈第 79 条（ADR-0054）：try_get_dataset_name 是 dataset_data_directory 的逆运算。
# 与 core/foundation/engine_adapter/tests/AssetRootConventionsTests.cs 对应用例使用同一组输入/
# 期望值，两侧各自独立实现；跨语言一致性另见本文件下方经 toolchain/asset_root_probe 对照的用例。---


@pytest.mark.parametrize(
    "dataset",
    ["_sample", "_framework", "dataset_with_underscores", "dataset123"],
)
def test_try_get_dataset_name_round_trip_default_data_root(dataset: str) -> None:
    data_root = resolve_data_root(REPO_ROOT, None)
    directory = dataset_data_directory(data_root, dataset)
    assert try_get_dataset_name(data_root, directory) == dataset


@pytest.mark.parametrize("dataset", ["_sample", "dataset_with_underscores"])
def test_try_get_dataset_name_round_trip_override_data_root(dataset: str) -> None:
    data_root = resolve_data_root(REPO_ROOT, "custom_data")
    directory = dataset_data_directory(data_root, dataset)
    assert try_get_dataset_name(data_root, directory) == dataset


def test_try_get_dataset_name_round_trip_absolute_override_data_root() -> None:
    absolute_override = str(REPO_ROOT / "abs_data")
    data_root = resolve_data_root(REPO_ROOT, absolute_override)
    directory = dataset_data_directory(data_root, "_sample")
    assert try_get_dataset_name(data_root, directory) == "_sample"


def test_try_get_dataset_name_directory_deeper_than_direct_child_returns_none() -> None:
    data_root = Path("/repo/data")
    deeper = data_root / "_sample" / "inner"
    assert try_get_dataset_name(data_root, deeper) is None


def test_try_get_dataset_name_directory_is_data_root_itself_returns_none() -> None:
    data_root = Path("/repo/data")
    assert try_get_dataset_name(data_root, data_root) is None


def test_try_get_dataset_name_directory_outside_data_root_returns_none() -> None:
    data_root = Path("/repo/data")
    outside = Path("/other/thing")
    assert try_get_dataset_name(data_root, outside) is None


def test_try_get_dataset_name_mixed_separators_still_matches() -> None:
    data_root = "D:\\repo\\data"
    directory_with_forward_slashes = "D:/repo/data/_sample"
    assert try_get_dataset_name(data_root, directory_with_forward_slashes) == "_sample"


def test_try_get_dataset_name_trailing_separator_still_matches() -> None:
    data_root = Path("/repo/data")
    directory = str(data_root / "_sample") + "/"
    assert try_get_dataset_name(data_root, directory) == "_sample"


def test_try_get_dataset_name_data_root_case_differs_from_observed_directory() -> None:
    # Windows 文件系统大小写不敏感：data_root 与 dataset_data_directory 父目录段大小写不同时
    # 仍应判定为同一目录。
    data_root = "D:\\Repo\\Data"
    directory = "d:\\repo\\data\\_Sample"
    assert try_get_dataset_name(data_root, directory) == "_Sample"


def test_try_get_dataset_name_observed_directory_casing_is_preserved_verbatim() -> None:
    # 还原出的数据集名取自 dataset_data_directory 最后一段的原样字符，不做任何大小写变换、
    # 也不取自正向方法本来传入的原始大小写。
    data_root = Path("/repo/data")
    forward_directory = dataset_data_directory(data_root, "_Sample")
    observed_with_different_case = data_root / "_sAmple"

    assert try_get_dataset_name(data_root, forward_directory) == "_Sample"
    assert try_get_dataset_name(data_root, observed_with_different_case) == "_sAmple"


def test_try_get_dataset_name_empty_directory_returns_none() -> None:
    data_root = Path("/repo/data")
    assert try_get_dataset_name(data_root, "") is None


@pytest.mark.parametrize(
    "resource_ref_id, expected_atlas, expected_frames",
    [
        ("vfx.sample_burn", "vfx/sample_burn/atlas.png", "vfx/sample_burn/frames.json"),
        ("vfx.sample_cast_circle", "vfx/sample_cast_circle/atlas.png", "vfx/sample_cast_circle/frames.json"),
    ],
)
def test_vfx_atlas_and_frames_file(resource_ref_id: str, expected_atlas: str, expected_frames: str) -> None:
    assert vfx_atlas_file(resource_ref_id) == expected_atlas
    assert vfx_frames_file(resource_ref_id) == expected_frames


@pytest.mark.parametrize(
    "resource_ref_id, expected_atlas, expected_frames",
    [
        (
            "sprite_anim.sample_hero_attack",
            "sprite_anim/sample_hero_attack/atlas.png",
            "sprite_anim/sample_hero_attack/frames.json",
        ),
    ],
)
def test_sprite_anim_atlas_and_frames_file(resource_ref_id: str, expected_atlas: str, expected_frames: str) -> None:
    assert sprite_anim_atlas_file(resource_ref_id) == expected_atlas
    assert sprite_anim_frames_file(resource_ref_id) == expected_frames


@pytest.mark.parametrize(
    "sprite_set_id, expected",
    [("sprite.item.sample_blade", "sprites/item_sample_blade/atlas.png")],
)
def test_sprite_set_atlas_file(sprite_set_id: str, expected: str) -> None:
    assert sprite_set_atlas_file(sprite_set_id) == expected


# --- 与既有格式对齐测试：从改用共享函数之前的历史提交里取出 common.py 的 resolve_root 原始实现，
# 按同一公式手工推出旧结果，与新增共享函数的输出逐字节比对，证明本次改动是纯抽取（消费方反馈第
# 76 条验收标准"与改动前逐字节对齐"）。---

# common.py 改用 ref_conventions 共享函数之前的最后一次提交（本次改动的分支起点，1.50.0 发布
# 提交）；该提交在远端/本地历史中不可变，可放心作为"改动前格式"的固定锚点长期使用。
_PRE_REFACTOR_COMMON_PY_SHA = "97052ac6"


def _historical_common_py_source() -> str:
    result = subprocess.run(
        [GIT, "-C", str(REPO_ROOT), "show", f"{_PRE_REFACTOR_COMMON_PY_SHA}:toolchain/asset_import/common.py"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=30,
    )
    assert result.returncode == 0, "读取 common.py 历史版本失败：\n" + result.stderr
    return result.stdout


def _pre_refactor_resolve_root(value: str | None, repo_root: Path, default_name: str) -> Path:
    """按 <c>_historical_common_py_source</c> 钉住的原始算法手工复算（不导入历史版本模块，直接
    照抄该函数体，见下方 assert 对源码文本的钉子，源码文本变了这里也要跟着变）。"""
    if value is None:
        return repo_root / default_name
    p = Path(value)
    return p if p.is_absolute() else (repo_root / p)


@pytest.mark.skipif(GIT is None, reason="本机找不到 git，跳过历史格式对齐测试")
def test_resolve_assets_root_matches_pre_refactor_common_py_format() -> None:
    historical_source = _historical_common_py_source()
    assert "def resolve_root(value: str | None, repo_root: Path, default_name: str) -> Path:" in historical_source
    assert "if value is None:\n        return repo_root / default_name" in historical_source
    assert "return p if p.is_absolute() else (repo_root / p)" in historical_source

    repo_root = Path("/repo")
    for override in (None, "custom", "/abs/custom"):
        assert resolve_assets_root(repo_root, override) == _pre_refactor_resolve_root(override, repo_root, "assets")
        assert resolve_data_root(repo_root, override) == _pre_refactor_resolve_root(override, repo_root, "data")


def test_resolve_root_delegates_and_matches_new_functions_for_known_default_names() -> None:
    """common.py 的 resolve_root 委托给 ref_conventions 之后，对 "assets"/"data" 两个真实调用点
    使用的 default_name，结果必须与新增共享函数逐字节相等——证明委托生效，不是两套并存的实现。"""
    repo_root = Path("/repo")
    for override in (None, "custom", "/abs/custom"):
        assert _common_resolve_root(override, repo_root, "assets") == resolve_assets_root(repo_root, override)
        assert _common_resolve_root(override, repo_root, "data") == resolve_data_root(repo_root, override)


# --- 跨语言一致性测试：对同一组输入，C# 侧真实运行期计算结果（经 toolchain/asset_root_probe 子
# 进程取得）与本文件 Python 侧对应函数各自独立算出的结果逐字节相等。---

_ASSET_ROOT_PROBE_PROJ = REPO_ROOT / "toolchain" / "asset_root_probe" / "AssetRootProbe.csproj"


@pytest.fixture(scope="session")
def asset_root_probe_dll(tmp_path_factory: pytest.TempPathFactory) -> Path:
    out_dir = tmp_path_factory.mktemp("asset_root_probe_build")
    result = subprocess.run(
        [DOTNET, "build", str(_ASSET_ROOT_PROBE_PROJ), "-c", "Release", "--nologo", "-o", str(out_dir)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=300,
    )
    assert result.returncode == 0, "asset_root_probe 构建失败：\n" + result.stdout + result.stderr
    dll = out_dir / "AssetRootProbe.dll"
    assert dll.is_file(), f"未找到构建产物：{dll}"
    return dll


def _run_asset_root_probe(
    dll: Path,
    repo_root: str,
    assets_root_override: str | None,
    data_root_override: str | None,
    dataset: str,
    inverse_probe_directory: str | None = None,
) -> dict:
    result = subprocess.run(
        [
            DOTNET,
            str(dll),
            repo_root,
            assets_root_override or "",
            data_root_override or "",
            dataset,
            inverse_probe_directory or "",
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
    )
    assert result.returncode == 0, f"AssetRootProbe 运行失败（{repo_root}）：\n" + result.stderr
    return json.loads(result.stdout)


@pytest.mark.skipif(DOTNET is None, reason="本机找不到 dotnet，跳过跨语言一致性测试")
@pytest.mark.parametrize(
    "repo_root, assets_root_override, data_root_override, dataset",
    [
        (str(REPO_ROOT), None, None, "_sample"),
        (str(REPO_ROOT), "custom_assets", None, "_framework"),
        (str(REPO_ROOT), None, "custom_data", "_sample"),
        (str(REPO_ROOT), str(REPO_ROOT / "abs_assets"), str(REPO_ROOT / "abs_data"), "_sample"),
    ],
)
def test_asset_root_conventions_cross_language_consistency(
    asset_root_probe_dll: Path,
    repo_root: str,
    assets_root_override: str | None,
    data_root_override: str | None,
    dataset: str,
) -> None:
    csharp_result = _run_asset_root_probe(
        asset_root_probe_dll, repo_root, assets_root_override, data_root_override, dataset
    )
    py_repo_root = Path(repo_root)
    assert csharp_result["assets_root"] == str(resolve_assets_root(py_repo_root, assets_root_override))
    assert csharp_result["data_root"] == str(resolve_data_root(py_repo_root, data_root_override))
    py_assets_root = resolve_assets_root(py_repo_root, assets_root_override)
    py_data_root = resolve_data_root(py_repo_root, data_root_override)
    assert csharp_result["dataset_assets_dir"] == str(dataset_assets_directory(py_assets_root, dataset))
    assert csharp_result["dataset_data_dir"] == str(dataset_data_directory(py_data_root, dataset))

    # 消费方反馈第 79 条：往返路径上的逆运算——用本用例刚算出的 dataset_data_dir 做输入，两侧
    # TryGetDatasetName/try_get_dataset_name 的还原结果必须逐字节相等（且都等于原 dataset）。
    assert csharp_result["inverse_ok"] is True
    assert csharp_result["inverse_dataset"] == dataset
    py_dataset_data_dir = dataset_data_directory(py_data_root, dataset)
    assert try_get_dataset_name(py_data_root, py_dataset_data_dir) == csharp_result["inverse_dataset"]


@pytest.mark.skipif(DOTNET is None, reason="本机找不到 dotnet，跳过跨语言一致性测试")
@pytest.mark.parametrize(
    "repo_root, data_root_override, dataset, inverse_probe_directory",
    [
        # 更深子目录：两侧均判定为失败。
        (str(REPO_ROOT), None, "_sample", str(REPO_ROOT / "data" / "_sample" / "inner")),
        # 数据根之外的路径：两侧均判定为失败。
        (str(REPO_ROOT), None, "_sample", str(REPO_ROOT / "other" / "thing")),
        # 就是数据根本身：两侧均判定为失败。
        (str(REPO_ROOT), None, "_sample", str(REPO_ROOT / "data")),
        # 分隔符混用（正斜杠）：两侧均判定为成功，且还原出同一个数据集名。
        (str(REPO_ROOT), None, "_sample", str(REPO_ROOT / "data").replace("\\", "/") + "/_sample"),
        # 结尾多余分隔符：两侧均判定为成功。
        (str(REPO_ROOT), None, "_sample", str(REPO_ROOT / "data" / "_sample") + "\\"),
    ],
)
def test_try_get_dataset_name_cross_language_consistency_boundary_cases(
    asset_root_probe_dll: Path,
    repo_root: str,
    data_root_override: str | None,
    dataset: str,
    inverse_probe_directory: str,
) -> None:
    csharp_result = _run_asset_root_probe(
        asset_root_probe_dll,
        repo_root,
        None,
        data_root_override,
        dataset,
        inverse_probe_directory,
    )
    py_data_root = resolve_data_root(Path(repo_root), data_root_override)
    py_dataset = try_get_dataset_name(py_data_root, inverse_probe_directory)

    assert csharp_result["inverse_ok"] == (py_dataset is not None)
    if py_dataset is not None:
        assert csharp_result["inverse_dataset"] == py_dataset
