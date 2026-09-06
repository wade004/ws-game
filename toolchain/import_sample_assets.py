#!/usr/bin/env python3
"""``data/_sample`` 占位素材导入驱动脚本。

把 ``assets/_placeholder/`` 的占位素材（外加一个 Pillow 现生成的存档点占位图）经
``toolchain/import_assets.py`` 的真实子命令（``sprite``/``icon``/``vfx``/``sfx``/``map``）导入为
``data/_sample`` 三张表（``display.map``/``vfx.def``/``sfx.def``）已引用的 ``assets/_sample/``
资产，并把导入产出的引用字段回写进 ``data/_sample`` 既有行（行的 ``id``/``logical_id``/
``category`` 一律不变，只改 ``sprite_set_id``/``icon_id``/``mirror_pairs``/``resource_ref``/
``variants``/``lifetime``/``priority`` 这些资源引用或工具派生字段）。

用法::

    python toolchain/import_sample_assets.py [--assets-root X] [--data-root Y] [--placeholder-root Z]

默认 ``--assets-root`` 为仓库 ``assets/``、``--data-root`` 为仓库 ``data/``、
``--placeholder-root`` 为仓库 ``assets/_placeholder/``。数据集固定为 ``_sample``。

来源素材 -> 子命令 -> 产物 -> 回写字段对应表：

======================================= ========= ========================================= =========================================================
来源素材（placeholder-root 下 / 现生成）  子命令     产物（assets-root 下）                     回写到 data/_sample 的字段
======================================= ========= ========================================= =========================================================
sprites/placeholder_hero/{5 个 canonical  sprite    sprites/creature_sample_hero/...           display.map.sample_hero:
档位}/{body,hand_main,head}.png +                                                             sprite_set_id/mirror_pairs/icon_id
anchors.json（提取 5 档位）
sprites/placeholder_beast/{5 个 canonical sprite    sprites/creature_sample_beast/...          display.map.sample_beast:
档位}/body.png + anchors.json                                                                 sprite_set_id/mirror_pairs/icon_id
icons/icon_placeholder_blade.png         sprite    sprites/item_sample_blade/...               display.map.sample_blade 与
（复制为 3 个方向档位的单层精灵）                                                              display.map.sample_bolt（共用精灵集）:
                                                                                               sprite_set_id/mirror_pairs
sprites/placeholder_chest/closed.png     sprite    sprites/gobj_sample_chest/...               display.map.sample_chest 与
（复制为 3 个方向档位）                                                                        display.map.sample_loot_pile（共用精灵集）:
                                                                                               sprite_set_id/mirror_pairs/icon_id
sprites/placeholder_door/closed.png      sprite    sprites/gobj_sample_door/...                display.map.sample_door:
（复制为 3 个方向档位）                                                                        sprite_set_id/mirror_pairs/icon_id
Pillow 现生成 32x48 占位立柱              sprite    sprites/gobj_sample_save_point/...          display.map.sample_save_point:
（复制为 3 个方向档位）                                                                        sprite_set_id/mirror_pairs/icon_id
hero front 层 body+hand_main+head 合成图  icon      icons/creature/sample_hero.png             display.map.sample_hero.icon_id
beast front/body.png                     icon      icons/creature/sample_beast.png             display.map.sample_beast.icon_id
chest closed.png                         icon      icons/gobj/sample_chest.png                 display.map.sample_chest/
                                                                                               sample_loot_pile.icon_id
door closed.png                          icon      icons/gobj/sample_door.png                  display.map.sample_door.icon_id
（同上）存档点占位图                      icon      icons/gobj/sample_save_point.png            display.map.sample_save_point.icon_id
vfx/cast_circle/frame_00..07.png         vfx       vfx/sample_cast_circle/{atlas.png,frames.json} vfx.def.sample_cast_circle（--id 直写实表，整行覆盖）
vfx/hit_spark/frame_00..07.png           vfx       vfx/sample_hit_spark/{atlas.png,frames.json}   vfx.def.sample_hit_spark（同上）
vfx/burn/frame_00..07.png                vfx       vfx/sample_burn/{atlas.png,frames.json}        vfx.def.sample_burn（同上）
sfx/hit_01.wav + sfx/hit_02.wav          sfx       sfx/sample_hit_v0.wav + _v1.wav                sfx.def.sample_hit（--id 直写实表，整行覆盖）
sfx/cast_01.wav                          sfx       sfx/sample_cast_v0.wav                         sfx.def.sample_cast（同上）
sfx/ui_click_01.wav                      sfx       sfx/sample_ui_click_v0.wav                     sfx.def.sample_ui_click（同上）
maps/placeholder_field/*.png             map       maps/sample_field/*.png                        world.map.sample_field（--map 直写实表，整行覆盖，幂等）
======================================= ========= ========================================= =========================================================

``sprite`` 子命令按 ``--logical-id`` 派生的 display.map 行 id（``display.<去掉 domain 前缀>``，
如 ``display.sample_hero``）与 ``data/_sample`` 既有行 id（``display.map.sample_hero``）不在
同一命名空间，不能直接合并写入真实表——本脚本因此对 sprite 相关的 5 条命令一律使用临时数据根
（``--data-root`` 指向 ``tempfile`` 临时目录），运行结束后从临时表里读出工具产出的
``sprite_set_id``/``mirror_pairs``，按上表映射手工回写进真实 ``data/_sample/display/display.map.json``
的对应既有行（``icon_id`` 同一步按上表直接置入/删除）；``vfx``/``sfx``/``map`` 三类命令的
``--id``/``--map`` 与真实表已有行 id 完全同名，直接对真实数据根运行、让 ``merge_write_row``
整行覆盖即可（``vfx.def``/``sfx.def``/``world.map`` schema 字段本就与工具行字段一一对应，整行
覆盖不会丢字段）。

返回码：0 成功；1 失败（某条子命令返回非 0，或既有行缺失，或收尾 ``check`` 失败）；
2 参数错误。
"""

from __future__ import annotations

import argparse
import sys
import tempfile
from pathlib import Path

# 允许直接以 "python toolchain/import_sample_assets.py" 方式运行，与
# toolchain/import_assets.py/toolchain/gen_event_constants.py 的惯例一致：把 toolchain/ 目录
# 本身放进 sys.path 后即可 import 顶层 _console 与 asset_import 包。
sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

from asset_import.cli import main as import_main  # noqa: E402
from asset_import.common import (  # noqa: E402
    AssetImportError,
    find_repo_root,
    read_json,
    write_envelope,
    write_json_pretty,
)

DATASET = "_sample"

# 每个方向档位数量下的 canonical 档位名（与 toolchain/asset_import/directions.py 一致，
# 这里只是取用，不重新定义规则）。
CANONICAL_8 = ["front", "front_side_r", "side_r", "back_side_r", "back"]
CANONICAL_4 = ["front", "side_r", "back"]

# display.map 既有行 id -> (临时表里工具产出行的 id, icon_id 或 None)。
DISPLAY_MAP_PATCHES: dict[str, tuple[str, str | None]] = {
    "display.map.sample_hero": ("display.sample_hero", "icon.creature.sample_hero"),
    "display.map.sample_beast": ("display.sample_beast", "icon.creature.sample_beast"),
    "display.map.sample_blade": ("display.sample_blade", None),
    "display.map.sample_chest": ("display.sample_chest", "icon.gobj.sample_chest"),
    "display.map.sample_door": ("display.sample_door", "icon.gobj.sample_door"),
    "display.map.sample_save_point": ("display.sample_save_point", "icon.gobj.sample_save_point"),
    "display.map.sample_bolt": ("display.sample_blade", None),
    "display.map.sample_loot_pile": ("display.sample_chest", "icon.gobj.sample_chest"),
}


def _run(argv: list[str]) -> None:
    """调用 import_assets 的子命令；打印 argv，非 0 返回码直接失败退出 1。"""
    print(f"[import_sample_assets] $ python toolchain/import_assets.py {' '.join(argv)}")
    code = import_main(argv)
    if code != 0:
        print(f"[import_sample_assets] 子命令失败（返回码 {code}）: {argv}", file=sys.stderr)
        sys.exit(1)


def _copy_flat_slots(dst_dir: Path, src_file: Path, slots: list[str]) -> None:
    """把同一张源图复制为若干方向档位的单层（flat）精灵文件。"""
    dst_dir.mkdir(parents=True, exist_ok=True)
    img = Image.open(src_file).convert("RGBA")
    for slot in slots:
        img.save(dst_dir / f"{slot}.png")


def _copy_dir_slots(dst_dir: Path, src_root: Path, slots: list[str]) -> None:
    """把 src_root/<slot>/*.png 的多层（纸娃娃）方向档位原样复制到 dst_dir/<slot>/*.png。"""
    for slot in slots:
        src_slot_dir = src_root / slot
        if not src_slot_dir.is_dir():
            raise AssetImportError(f"占位素材缺少方向档位目录: {src_slot_dir}")
        dst_slot_dir = dst_dir / slot
        dst_slot_dir.mkdir(parents=True, exist_ok=True)
        for png in sorted(src_slot_dir.glob("*.png")):
            Image.open(png).convert("RGBA").save(dst_slot_dir / png.name)


def _extract_anchors(anchors_json_path: Path, slots: list[str]) -> dict:
    """从占位素材的 anchors.json 的 "directions" 子对象里只取指定的档位子集，
    转成 --anchors 参数期望的 {"<slot>": {"<anchor>": [x, y]}} 格式。"""
    data = read_json(anchors_json_path)
    directions = data["directions"]
    return {slot: directions[slot] for slot in slots}


def _gen_save_point_image(size: tuple[int, int] = (32, 48)) -> Image.Image:
    """确定性生成存档点占位图：青色立柱 + 底座（固定颜色，不用随机），
    用于 sample_save_point 没有对应占位素材源图的场景。"""
    col_outline = (20, 20, 24, 255)
    col_pillar = (70, 200, 210, 255)
    col_pillar_dark = (40, 150, 165, 255)
    col_base = (90, 90, 100, 255)

    w, h = size
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 底座。
    d.rectangle([2, h - 10, w - 3, h - 2], fill=col_base, outline=col_outline, width=1)
    # 立柱主体。
    d.rectangle([w // 2 - 6, 6, w // 2 + 6, h - 10], fill=col_pillar, outline=col_outline, width=1)
    # 立柱侧面暗面（简单光影）。
    d.rectangle([w // 2 + 2, 6, w // 2 + 6, h - 10], fill=col_pillar_dark)
    # 顶部光点。
    d.ellipse([w // 2 - 3, 2, w // 2 + 3, 8], fill=(220, 250, 250, 255), outline=col_outline)
    return img


def _compose_hero_icon_source(hero_front_dir: Path) -> Image.Image:
    """把 hero front 档位的 body/hand_main/head 三层按此顺序 alpha_composite 叠成一张整图。"""
    base = Image.open(hero_front_dir / "body.png").convert("RGBA")
    canvas = Image.new("RGBA", base.size, (0, 0, 0, 0))
    canvas = Image.alpha_composite(canvas, base)
    for layer_name in ("hand_main", "head"):
        layer_img = Image.open(hero_front_dir / f"{layer_name}.png").convert("RGBA")
        canvas = Image.alpha_composite(canvas, layer_img)
    return canvas


def _patch_display_map(data_root: Path, tmp_data_root: Path) -> None:
    """把临时数据根里工具产出的 sprite_set_id/mirror_pairs（+ icon_id）回写进真实
    data/_sample/display/display.map.json 的既有行；既有行必须存在，否则报错退出。"""
    tmp_display_path = tmp_data_root / DATASET / "display" / "display.map.json"
    tool_data = read_json(tmp_display_path)
    tool_rows_by_id = {row["id"]: row for row in tool_data.get("rows", [])}

    real_display_path = data_root / DATASET / "display" / "display.map.json"
    real_data = read_json(real_display_path)
    real_rows = real_data.get("rows", [])
    real_rows_by_id = {row["id"]: row for row in real_rows}

    missing = [real_id for real_id in DISPLAY_MAP_PATCHES if real_id not in real_rows_by_id]
    if missing:
        raise AssetImportError(
            "data/_sample/display/display.map.json 缺少既有行，无法回写: " + ", ".join(missing)
        )

    for real_id, (tool_id, icon_id) in DISPLAY_MAP_PATCHES.items():
        if tool_id not in tool_rows_by_id:
            raise AssetImportError(
                f"临时表 {tmp_display_path} 缺少工具产出行 '{tool_id}'（既有行 '{real_id}' 依赖它）"
            )
        tool_row = tool_rows_by_id[tool_id]
        real_row = real_rows_by_id[real_id]

        real_row["sprite_set_id"] = tool_row["sprite_set_id"]
        if "mirror_pairs" in tool_row:
            real_row["mirror_pairs"] = tool_row["mirror_pairs"]
        else:
            real_row.pop("mirror_pairs", None)
        if icon_id:
            real_row["icon_id"] = icon_id
        else:
            real_row.pop("icon_id", None)

    sorted_rows = [real_rows_by_id[k] for k in sorted(real_rows_by_id)]
    write_envelope(
        real_display_path, "display.map", real_data.get("schema_version", 1), sorted_rows
    )
    print(f"[import_sample_assets] 已回写 {len(DISPLAY_MAP_PATCHES)} 条 display.map 行: {real_display_path}")


def run(assets_root: Path, data_root: Path, placeholder_root: Path) -> int:
    with tempfile.TemporaryDirectory(prefix="import_sample_assets_") as tmp:
        tmp_root = Path(tmp)
        tmp_data_root = tmp_root / "data"
        stage = tmp_root / "stage"
        stage.mkdir(parents=True, exist_ok=True)

        common_sprite_args = [
            "--dataset",
            DATASET,
            "--pixels-per-unit",
            "32",
            "--mirror",
            "auto",
            "--assets-root",
            str(assets_root),
            "--data-root",
            str(tmp_data_root),
        ]

        # --- 1. 精灵集 ---------------------------------------------------------------

        # sample_hero：多层（body/hand_main/head），8 方向，5 个 canonical 档位取自占位素材。
        hero_src = placeholder_root / "sprites" / "placeholder_hero"
        hero_stage = stage / "sample_hero"
        _copy_dir_slots(hero_stage, hero_src, CANONICAL_8)
        hero_anchors = _extract_anchors(hero_src / "anchors.json", CANONICAL_8)
        hero_anchors_path = stage / "sample_hero.anchors.json"
        write_json_pretty(hero_anchors_path, hero_anchors)
        _run(
            [
                "sprite",
                str(hero_stage),
                "--category",
                "creature",
                "--logical-id",
                "creature.sample_hero",
                "--direction-count",
                "8",
                "--anchors",
                str(hero_anchors_path),
                "--shadow",
                "blob",
            ]
            + common_sprite_args
        )

        # sample_beast：单层（body），8 方向，5 个 canonical 档位取自占位素材。
        beast_src = placeholder_root / "sprites" / "placeholder_beast"
        beast_stage = stage / "sample_beast"
        _copy_dir_slots(beast_stage, beast_src, CANONICAL_8)
        beast_anchors = _extract_anchors(beast_src / "anchors.json", CANONICAL_8)
        beast_anchors_path = stage / "sample_beast.anchors.json"
        write_json_pretty(beast_anchors_path, beast_anchors)
        _run(
            [
                "sprite",
                str(beast_stage),
                "--category",
                "creature",
                "--logical-id",
                "creature.sample_beast",
                "--direction-count",
                "8",
                "--anchors",
                str(beast_anchors_path),
                "--shadow",
                "blob",
            ]
            + common_sprite_args
        )

        # sample_blade：item，4 方向，单层，源图是一张图标复制成 3 个方向档位。
        blade_stage = stage / "sample_blade"
        _copy_flat_slots(blade_stage, placeholder_root / "icons" / "icon_placeholder_blade.png", CANONICAL_4)
        _run(
            [
                "sprite",
                str(blade_stage),
                "--category",
                "item",
                "--logical-id",
                "item.sample_blade",
                "--direction-count",
                "4",
                "--shadow",
                "none",
            ]
            + common_sprite_args
        )

        # sample_chest：gobj，4 方向，单层。
        chest_stage = stage / "sample_chest"
        _copy_flat_slots(
            chest_stage, placeholder_root / "sprites" / "placeholder_chest" / "closed.png", CANONICAL_4
        )
        _run(
            [
                "sprite",
                str(chest_stage),
                "--category",
                "gobj",
                "--logical-id",
                "gobj.sample_chest",
                "--direction-count",
                "4",
                "--shadow",
                "blob",
            ]
            + common_sprite_args
        )

        # sample_door：gobj，4 方向，单层。
        door_stage = stage / "sample_door"
        _copy_flat_slots(
            door_stage, placeholder_root / "sprites" / "placeholder_door" / "closed.png", CANONICAL_4
        )
        _run(
            [
                "sprite",
                str(door_stage),
                "--category",
                "gobj",
                "--logical-id",
                "gobj.sample_door",
                "--direction-count",
                "4",
                "--shadow",
                "none",
            ]
            + common_sprite_args
        )

        # sample_save_point：gobj，4 方向，单层；无占位源图，用 Pillow 确定性生成。
        save_point_img = _gen_save_point_image()
        generated_dir = stage / "generated"
        generated_dir.mkdir(parents=True, exist_ok=True)
        save_point_src = generated_dir / "sample_save_point.png"
        save_point_img.save(save_point_src)
        save_point_stage = stage / "sample_save_point"
        _copy_flat_slots(save_point_stage, save_point_src, CANONICAL_4)
        _run(
            [
                "sprite",
                str(save_point_stage),
                "--category",
                "gobj",
                "--logical-id",
                "gobj.sample_save_point",
                "--direction-count",
                "4",
                "--shadow",
                "none",
            ]
            + common_sprite_args
        )

        # --- 2. 图标 -------------------------------------------------------------------

        icons_creature_dir = stage / "icons" / "creature"
        icons_creature_dir.mkdir(parents=True, exist_ok=True)
        hero_icon_src = icons_creature_dir / "sample_hero.png"
        _compose_hero_icon_source(hero_stage / "front").save(hero_icon_src)
        beast_icon_src = icons_creature_dir / "sample_beast.png"
        Image.open(beast_stage / "front" / "body.png").convert("RGBA").save(beast_icon_src)
        _run(
            [
                "icon",
                str(hero_icon_src),
                str(beast_icon_src),
                "--category",
                "creature",
                "--dataset",
                DATASET,
                "--assets-root",
                str(assets_root),
            ]
        )

        icons_gobj_dir = stage / "icons" / "gobj"
        icons_gobj_dir.mkdir(parents=True, exist_ok=True)
        chest_icon_src = icons_gobj_dir / "sample_chest.png"
        Image.open(chest_stage / "front.png").convert("RGBA").save(chest_icon_src)
        door_icon_src = icons_gobj_dir / "sample_door.png"
        Image.open(door_stage / "front.png").convert("RGBA").save(door_icon_src)
        save_point_icon_src = icons_gobj_dir / "sample_save_point.png"
        Image.open(save_point_src).convert("RGBA").save(save_point_icon_src)
        _run(
            [
                "icon",
                str(chest_icon_src),
                str(door_icon_src),
                str(save_point_icon_src),
                "--category",
                "gobj",
                "--dataset",
                DATASET,
                "--assets-root",
                str(assets_root),
            ]
        )

        # --- 3. 特效（真实数据根，直接整行覆盖同名行） -----------------------------------

        vfx_common = [
            "--dataset",
            DATASET,
            "--fps",
            "20",
            "--assets-root",
            str(assets_root),
            "--data-root",
            str(data_root),
        ]

        cast_circle_stage = stage / "cast_circle"
        cast_circle_stage.mkdir(parents=True, exist_ok=True)
        for png in sorted((placeholder_root / "vfx" / "cast_circle").glob("frame_*.png")):
            Image.open(png).convert("RGBA").save(cast_circle_stage / png.name)
        _run(
            [
                "vfx",
                str(cast_circle_stage),
                "--id",
                "vfx.sample_cast_circle",
                "--category",
                "cast",
                "--attach-mode",
                "anchor",
                "--lifetime",
                "1.2",
            ]
            + vfx_common
        )

        hit_spark_stage = stage / "hit_spark"
        hit_spark_stage.mkdir(parents=True, exist_ok=True)
        for png in sorted((placeholder_root / "vfx" / "hit_spark").glob("frame_*.png")):
            Image.open(png).convert("RGBA").save(hit_spark_stage / png.name)
        _run(
            [
                "vfx",
                str(hit_spark_stage),
                "--id",
                "vfx.sample_hit_spark",
                "--category",
                "impact",
                "--attach-mode",
                "world",
            ]
            + vfx_common
        )

        burn_stage = stage / "burn"
        burn_stage.mkdir(parents=True, exist_ok=True)
        for png in sorted((placeholder_root / "vfx" / "burn").glob("frame_*.png")):
            Image.open(png).convert("RGBA").save(burn_stage / png.name)
        _run(
            [
                "vfx",
                str(burn_stage),
                "--id",
                "vfx.sample_burn",
                "--category",
                "dot",
                "--attach-mode",
                "anchor",
                "--loop",
            ]
            + vfx_common
        )

        # --- 4. 音效（真实数据根，直接整行覆盖同名行） -----------------------------------

        sfx_common = [
            "--dataset",
            DATASET,
            "--assets-root",
            str(assets_root),
            "--data-root",
            str(data_root),
        ]
        _run(
            [
                "sfx",
                str(placeholder_root / "sfx" / "hit_01.wav"),
                str(placeholder_root / "sfx" / "hit_02.wav"),
                "--id",
                "sfx.sample_hit",
                "--layer",
                "combat",
                "--priority",
                "5",
            ]
            + sfx_common
        )
        _run(
            [
                "sfx",
                str(placeholder_root / "sfx" / "cast_01.wav"),
                "--id",
                "sfx.sample_cast",
                "--layer",
                "combat",
                "--priority",
                "3",
            ]
            + sfx_common
        )
        _run(
            [
                "sfx",
                str(placeholder_root / "sfx" / "ui_click_01.wav"),
                "--id",
                "sfx.sample_ui_click",
                "--layer",
                "ui",
            ]
            + sfx_common
        )

        # --- 5. 地图（真实数据根，幂等，唯一再生入口） -----------------------------------

        _run(
            [
                "map",
                str(placeholder_root / "maps" / "placeholder_field"),
                "--map",
                "sample_field",
                "--dataset",
                DATASET,
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )

        # --- 6. 回写 display.map ---------------------------------------------------------

        _patch_display_map(data_root, tmp_data_root)

    # --- 7. 收尾自检：跑一次 check，非 0 直接失败 -----------------------------------------

    print("[import_sample_assets] 运行收尾 check ...")
    check_code = import_main(
        [
            "check",
            "--dataset",
            DATASET,
            "--assets-root",
            str(assets_root),
            "--data-root",
            str(data_root),
        ]
    )
    if check_code != 0:
        print(f"[import_sample_assets] 收尾 check 失败（返回码 {check_code}）", file=sys.stderr)
        return 1

    print("[import_sample_assets] 全部完成。")
    return 0


def main(argv: list[str] | None = None) -> int:
    ensure_utf8_stdio()

    repo_root = find_repo_root()
    parser = argparse.ArgumentParser(
        description=(
            "把 assets/_placeholder/ 的占位素材经 import_assets.py 的真实子命令导入为 "
            "data/_sample 引用的 assets/_sample/ 资产，并回写引用字段（id 不变）。"
        )
    )
    parser.add_argument(
        "--assets-root", default=None, help="资产根目录，默认仓库 assets/"
    )
    parser.add_argument(
        "--data-root", default=None, help="数据根目录，默认仓库 data/"
    )
    parser.add_argument(
        "--placeholder-root", default=None, help="占位素材根目录，默认仓库 assets/_placeholder/"
    )
    try:
        args = parser.parse_args(argv)
    except SystemExit as exc:
        return exc.code if isinstance(exc.code, int) else 2

    assets_root = Path(args.assets_root).resolve() if args.assets_root else repo_root / "assets"
    data_root = Path(args.data_root).resolve() if args.data_root else repo_root / "data"
    placeholder_root = (
        Path(args.placeholder_root).resolve() if args.placeholder_root else repo_root / "assets" / "_placeholder"
    )

    if not placeholder_root.is_dir():
        print(f"错误: 占位素材根目录不存在: {placeholder_root}", file=sys.stderr)
        return 2

    try:
        return run(assets_root, data_root, placeholder_root)
    except AssetImportError as exc:
        print(f"错误: {exc}", file=sys.stderr)
        return 1
    except FileNotFoundError as exc:
        print(f"错误: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
