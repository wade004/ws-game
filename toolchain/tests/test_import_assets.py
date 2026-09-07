"""``toolchain/import_assets.py``（``toolchain/asset_import/`` 包）的单元测试。

只用标准库 ``unittest`` + Pillow（生成假素材图片）+ ``wave``（合成静音 wav）。全部测试数据
写在系统临时目录下（``tempfile.mkdtemp()``），不触碰仓库内的 ``assets/``/``data/``：每个用例
都显式传 ``--assets-root``/``--data-root`` 指向临时目录；``tearDownClass`` 统一清理。

运行：

```
python -m unittest toolchain.tests.test_import_assets -v
```
"""

from __future__ import annotations

import contextlib
import io
import json
import shutil
import sys
import tempfile
import unittest
import wave
from pathlib import Path

from PIL import Image

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from asset_import.cli import main as cli_main  # noqa: E402
from asset_import.common import AssetImportError  # noqa: E402
from asset_import.vfx_cmd import ATTACH_MODE_CHOICES  # noqa: E402

CANONICAL_8 = ["front", "front_side_r", "side_r", "back_side_r", "back"]


def run_cli(argv: list[str]) -> tuple[int, str]:
    """调用 CLI，返回 (returncode, 合并后的标准输出+标准错误文本)。"""
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        code = cli_main(argv)
    return code, out.getvalue()


def make_layer_image(size: tuple[int, int], color=(200, 60, 60, 255)) -> Image.Image:
    return Image.new("RGBA", size, color)


def make_padded_content_image(canvas: tuple[int, int], box: tuple[int, int, int, int]) -> Image.Image:
    """整张画布透明，只在 box=(left, top, right, bottom) 区域内填充不透明像素。"""
    img = Image.new("RGBA", canvas, (0, 0, 0, 0))
    left, top, right, bottom = box
    for y in range(top, bottom):
        for x in range(left, right):
            img.putpixel((x, y), (10, 200, 10, 255))
    return img


def build_layered_sprite_src(src_dir: Path, slots: list[str]) -> None:
    """在 src_dir 下按 <slot>/<layer>.png 布局造一套双层假精灵美术。"""
    for slot in slots:
        slot_dir = src_dir / slot
        slot_dir.mkdir(parents=True, exist_ok=True)
        make_layer_image((40, 60)).save(slot_dir / "body.png")
        make_layer_image((10, 10), color=(60, 60, 200, 255)).save(slot_dir / "hand_main.png")


def build_flat_sprite_src(
    src_dir: Path, slots: list[str], canvas=(40, 60), box=(10, 10, 30, 50)
) -> None:
    """在 src_dir 下按 <slot>.png 布局造一套单层假精灵美术（用于 trim 测试）。"""
    src_dir.mkdir(parents=True, exist_ok=True)
    for slot in slots:
        make_padded_content_image(canvas, box).save(src_dir / f"{slot}.png")


def write_json(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


class ImportAssetsTestBase(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.tmp_root = Path(tempfile.mkdtemp(prefix="import_assets_test_"))

    @classmethod
    def tearDownClass(cls) -> None:
        shutil.rmtree(cls.tmp_root, ignore_errors=True)

    def new_case_dir(self, name: str) -> Path:
        case_dir = self.tmp_root / name
        case_dir.mkdir(parents=True, exist_ok=True)
        return case_dir

    def roots(self, case_dir: Path) -> tuple[Path, Path]:
        assets_root = case_dir / "assets"
        data_root = case_dir / "data"
        return assets_root, data_root


class SpriteBasicFlowTest(ImportAssetsTestBase):
    def setUp(self) -> None:
        self.case_dir = self.new_case_dir("sprite_basic")
        self.assets_root, self.data_root = self.roots(self.case_dir)
        self.src_dir = self.case_dir / "src" / "wolf_grey"
        build_layered_sprite_src(self.src_dir, CANONICAL_8)
        # icon.png 一并提供，验证 sprite 子命令顺带登记图标。
        make_layer_image((80, 80), color=(255, 255, 0, 255)).save(self.src_dir / "icon.png")

        self.anchors_path = self.case_dir / "anchors.json"
        anchors = {
            slot: {"root": [20, 60], "hand_main": [35, 20]} for slot in CANONICAL_8
        }
        write_json(self.anchors_path, anchors)

        self.code, self.output = run_cli(
            [
                "sprite",
                str(self.src_dir),
                "--dataset",
                "_test",
                "--category",
                "creature",
                "--logical-id",
                "creature.grey_wolf_test",
                "--direction-count",
                "8",
                "--anchors",
                str(self.anchors_path),
                "--assets-root",
                str(self.assets_root),
                "--data-root",
                str(self.data_root),
            ]
        )
        self.display_map_path = self.data_root / "_test" / "display" / "display.map.json"
        if self.display_map_path.is_file():
            self.display_map = json.loads(self.display_map_path.read_text(encoding="utf-8"))
        else:
            self.display_map = None

    def test_exits_successfully(self) -> None:
        self.assertEqual(0, self.code, msg=self.output)

    def test_all_eight_direction_dirs_materialized(self) -> None:
        sprite_dir = self.assets_root / "_test" / "sprites" / "creature_wolf_grey"
        expected_dirs = {
            "front",
            "front_side_r",
            "side_r",
            "back_side_r",
            "back",
            "front_side_l",
            "side_l",
            "back_side_l",
        }
        actual_dirs = {p.name for p in sprite_dir.iterdir() if p.is_dir()}
        self.assertEqual(expected_dirs, actual_dirs)
        for slot in expected_dirs:
            self.assertTrue((sprite_dir / slot / "body.png").is_file())
            self.assertTrue((sprite_dir / slot / "hand_main.png").is_file())
        self.assertTrue((sprite_dir / "atlas.png").is_file())
        self.assertTrue((sprite_dir / "atlas.json").is_file())
        self.assertTrue((sprite_dir / "anchors.json").is_file())

    def test_icon_copied_and_referenced(self) -> None:
        icon_path = self.assets_root / "_test" / "icons" / "creature" / "wolf_grey.png"
        self.assertTrue(icon_path.is_file())
        with Image.open(icon_path) as img:
            self.assertEqual((64, 64), img.size)

    def test_display_map_row_fields(self) -> None:
        self.assertIsNotNone(self.display_map)
        self.assertEqual("display.map", self.display_map["table"])
        rows = self.display_map["rows"]
        self.assertEqual(1, len(rows))
        row = rows[0]
        self.assertEqual("display.grey_wolf_test", row["id"])
        self.assertEqual("creature", row["category"])
        self.assertEqual("creature.grey_wolf_test", row["logical_id"])
        self.assertEqual("sprite", row["kind"])
        self.assertEqual("sprite.creature.wolf_grey", row["sprite_set_id"])
        self.assertEqual(8, row["direction_count"])
        self.assertEqual(["body", "hand_main"], row["paperdoll_layers"])
        self.assertEqual("icon.creature.wolf_grey", row["icon_id"])
        self.assertEqual(0, row["sort_offset"])
        self.assertEqual("blob", row["shadow"])
        self.assertEqual(1.0, row["scale"])

    def test_mirror_pairs_has_three_entries(self) -> None:
        # 缺口 9：direction_slot/mirror_of 是 Id 类型字段，写入 display.map 时须带 "dir." 前缀
        # （见 presentation/common/contracts/DirectionSlots.cs "Id 前缀"判断记录、
        # toolchain/asset_import/common.py to_direction_slot_id）；文件名/anchors.json 标注仍是
        # 裸名字，不在本用例断言范围内。
        row = self.display_map["rows"][0]
        mirror_pairs = row["mirror_pairs"]
        self.assertEqual(3, len(mirror_pairs))
        actual = {(m["direction_slot"], m["mirror_of"], m["flip_x"]) for m in mirror_pairs}
        expected = {
            ("dir.front_side_l", "dir.front_side_r", True),
            ("dir.side_l", "dir.side_r", True),
            ("dir.back_side_l", "dir.back_side_r", True),
        }
        self.assertEqual(expected, actual)

    def test_anchor_points_converted_to_world_units(self) -> None:
        row = self.display_map["rows"][0]
        anchor_points = row["anchor_points"]
        # 默认档位是 canonical 列表第一个 = "front"；root=[20,60] / pixels_per_unit(32)。
        self.assertAlmostEqual(20 / 32, anchor_points["root"]["x"])
        self.assertAlmostEqual(60 / 32, anchor_points["root"]["y"])
        self.assertAlmostEqual(35 / 32, anchor_points["hand_main"]["x"])
        self.assertAlmostEqual(20 / 32, anchor_points["hand_main"]["y"])

    def test_check_passes_on_valid_output(self) -> None:
        code, output = run_cli(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(self.assets_root),
                "--data-root",
                str(self.data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)


class SpriteTrimTest(ImportAssetsTestBase):
    def test_trim_crops_and_shifts_anchor(self) -> None:
        case_dir = self.new_case_dir("sprite_trim")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src" / "torch"
        # 40x60 画布，内容只占 (10,10)-(30,50) 的 20x40 区域。
        build_flat_sprite_src(src_dir, CANONICAL_8, canvas=(40, 60), box=(10, 10, 30, 50))

        anchors_path = case_dir / "anchors.json"
        write_json(anchors_path, {slot: {"root": [30, 50]} for slot in CANONICAL_8})

        code, output = run_cli(
            [
                "sprite",
                str(src_dir),
                "--dataset",
                "_test",
                "--category",
                "item",
                "--logical-id",
                "item.torch_test",
                "--direction-count",
                "8",
                "--anchors",
                str(anchors_path),
                "--trim",
                "--pixels-per-unit",
                "32",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        front_png = assets_root / "_test" / "sprites" / "item_torch" / "front.png"
        with Image.open(front_png) as img:
            self.assertEqual((20, 40), img.size)

        anchors_json = json.loads((assets_root / "_test" / "sprites" / "item_torch" / "anchors.json").read_text(encoding="utf-8"))
        self.assertEqual([20, 40], anchors_json["front"]["canvas_size"])
        self.assertEqual([20, 40], anchors_json["front"]["anchors"]["root"])

        display_map = json.loads((data_root / "_test" / "display" / "display.map.json").read_text(encoding="utf-8"))
        row = display_map["rows"][0]
        self.assertAlmostEqual(20 / 32, row["anchor_points"]["root"]["x"])
        self.assertAlmostEqual(40 / 32, row["anchor_points"]["root"]["y"])


class SpriteMirrorNoneTest(ImportAssetsTestBase):
    def test_mirror_none_leaves_slots_unfilled(self) -> None:
        case_dir = self.new_case_dir("sprite_mirror_none")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src" / "goblin"
        build_layered_sprite_src(src_dir, CANONICAL_8)

        code, output = run_cli(
            [
                "sprite",
                str(src_dir),
                "--dataset",
                "_test",
                "--category",
                "creature",
                "--logical-id",
                "creature.goblin_test",
                "--mirror",
                "none",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)
        sprite_dir = assets_root / "_test" / "sprites" / "creature_goblin"
        actual_dirs = {p.name for p in sprite_dir.iterdir() if p.is_dir()}
        self.assertEqual(set(CANONICAL_8), actual_dirs)

        display_map = json.loads((data_root / "_test" / "display" / "display.map.json").read_text(encoding="utf-8"))
        row = display_map["rows"][0]
        self.assertNotIn("mirror_pairs", row)


class SpriteMissingCanonicalTest(ImportAssetsTestBase):
    def test_missing_canonical_direction_raises(self) -> None:
        case_dir = self.new_case_dir("sprite_missing_dir")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src" / "incomplete"
        build_layered_sprite_src(src_dir, ["front", "front_side_r", "side_r", "back_side_r"])  # 缺 back

        code, output = run_cli(
            [
                "sprite",
                str(src_dir),
                "--dataset",
                "_test",
                "--category",
                "creature",
                "--logical-id",
                "creature.incomplete_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(1, code)
        self.assertIn("back", output)
        self.assertFalse((data_root / "_test" / "display" / "display.map.json").is_file())


class SpriteMergeWriteTest(ImportAssetsTestBase):
    def test_second_sprite_set_is_merged_not_overwritten(self) -> None:
        case_dir = self.new_case_dir("sprite_merge")
        assets_root, data_root = self.roots(case_dir)

        for name, logical in (("wolf_a", "creature.wolf_a_test"), ("wolf_b", "creature.wolf_b_test")):
            src_dir = case_dir / "src" / name
            build_layered_sprite_src(src_dir, CANONICAL_8)
            code, output = run_cli(
                [
                    "sprite",
                    str(src_dir),
                    "--dataset",
                    "_test",
                    "--category",
                    "creature",
                    "--logical-id",
                    logical,
                    "--assets-root",
                    str(assets_root),
                    "--data-root",
                    str(data_root),
                ]
            )
            self.assertEqual(0, code, msg=output)

        display_map = json.loads((data_root / "_test" / "display" / "display.map.json").read_text(encoding="utf-8"))
        ids = [r["id"] for r in display_map["rows"]]
        self.assertEqual(["display.wolf_a_test", "display.wolf_b_test"], ids)


class DryRunTest(ImportAssetsTestBase):
    def test_dry_run_writes_nothing(self) -> None:
        case_dir = self.new_case_dir("sprite_dry_run")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src" / "ghost"
        build_layered_sprite_src(src_dir, CANONICAL_8)

        code, output = run_cli(
            [
                "sprite",
                str(src_dir),
                "--dataset",
                "_test",
                "--category",
                "creature",
                "--logical-id",
                "creature.ghost_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--dry-run",
            ]
        )
        self.assertEqual(0, code, msg=output)
        self.assertFalse(assets_root.exists())
        self.assertFalse(data_root.exists())
        self.assertIn("dry-run", output)


class CheckDetectsMissingFileTest(ImportAssetsTestBase):
    def test_check_reports_missing_frame_file(self) -> None:
        case_dir = self.new_case_dir("check_missing")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src" / "slime"
        build_layered_sprite_src(src_dir, CANONICAL_8)

        code, output = run_cli(
            [
                "sprite",
                str(src_dir),
                "--dataset",
                "_test",
                "--category",
                "creature",
                "--logical-id",
                "creature.slime_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        # 故意删掉一个已生成的帧文件，模拟资产被误删/未提交的情况。
        victim = assets_root / "_test" / "sprites" / "creature_slime" / "front" / "body.png"
        self.assertTrue(victim.is_file())
        victim.unlink()

        code, output = run_cli(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(1, code)
        self.assertIn(str(victim), output)


class CheckMirrorPairsAndAnchorTest(ImportAssetsTestBase):
    """check 子命令对 mirror_pairs 完整性、声明锚点缺失两类问题的独立重校验。"""

    def _build_valid_sprite(self, case_name: str) -> tuple[Path, Path, Path]:
        case_dir = self.new_case_dir(case_name)
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src" / "critter"
        build_layered_sprite_src(src_dir, CANONICAL_8)
        anchors_path = case_dir / "anchors.json"
        write_json(
            anchors_path,
            {slot: {"root": [20, 60], "hand_main": [35, 20]} for slot in CANONICAL_8},
        )
        code, output = run_cli(
            [
                "sprite",
                str(src_dir),
                "--dataset",
                "_test",
                "--category",
                "creature",
                "--logical-id",
                "creature.critter_test",
                "--direction-count",
                "8",
                "--anchors",
                str(anchors_path),
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)
        return assets_root, data_root, data_root / "_test" / "display" / "display.map.json"

    def _run_check(self, assets_root: Path, data_root: Path) -> tuple[int, str]:
        return run_cli(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )

    def test_missing_mirror_declaration_detected(self) -> None:
        assets_root, data_root, display_map_path = self._build_valid_sprite("mirror_missing_decl")
        data = json.loads(display_map_path.read_text(encoding="utf-8"))
        row = data["rows"][0]
        # 已落地的 front_side_l 目录仍在磁盘上，但从 mirror_pairs 里删掉它的声明。
        row["mirror_pairs"] = [
            m for m in row["mirror_pairs"] if m["direction_slot"] != "dir.front_side_l"
        ]
        write_json(display_map_path, data)

        code, output = self._run_check(assets_root, data_root)
        self.assertEqual(1, code, msg=output)
        self.assertIn("front_side_l", output)
        self.assertIn("mirror_pairs", output)

    def test_mirror_source_not_materialized_detected(self) -> None:
        assets_root, data_root, display_map_path = self._build_valid_sprite("mirror_bad_source")
        data = json.loads(display_map_path.read_text(encoding="utf-8"))
        row = data["rows"][0]
        for m in row["mirror_pairs"]:
            if m["direction_slot"] == "dir.front_side_l":
                m["mirror_of"] = "dir.nonexistent_slot"
        write_json(display_map_path, data)

        code, output = self._run_check(assets_root, data_root)
        self.assertEqual(1, code, msg=output)
        self.assertIn("nonexistent_slot", output)
        self.assertIn("未落地", output)

    def test_declared_anchor_missing_in_anchors_json_detected(self) -> None:
        assets_root, data_root, display_map_path = self._build_valid_sprite("anchor_missing")
        data = json.loads(display_map_path.read_text(encoding="utf-8"))
        row = data["rows"][0]
        row["anchor_points"]["overhead"] = {"x": 0.0, "y": 1.0}
        write_json(display_map_path, data)

        code, output = self._run_check(assets_root, data_root)
        self.assertEqual(1, code, msg=output)
        self.assertIn("overhead", output)
        self.assertIn("缺失", output)


class MapCommandTest(ImportAssetsTestBase):
    def _build_layer_src(self, src_dir: Path, layers: dict[str, tuple[int, int]]) -> None:
        src_dir.mkdir(parents=True, exist_ok=True)
        for name, size in layers.items():
            make_layer_image(size, color=(30, 60, 30, 255)).save(src_dir / f"{name}.png")

    def test_map_imports_layers_and_writes_world_row(self) -> None:
        case_dir = self.new_case_dir("map_cmd_basic")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(
            src_dir, {"ground": (16, 16), "overlay": (16, 16), "decal": (16, 16), "nav_hint": (16, 16)}
        )

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "grass_field",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        out_dir = assets_root / "_test" / "maps" / "grass_field"
        for name in ("ground", "overlay", "decal", "nav_hint"):
            self.assertTrue((out_dir / f"{name}.png").is_file())

        world_map_path = data_root / "_test" / "world" / "world.map.json"
        data = json.loads(world_map_path.read_text(encoding="utf-8"))
        self.assertEqual("world.map", data["table"])
        row = data["rows"][0]
        self.assertEqual("world.grass_field", row["id"])
        self.assertEqual("scene.grass_field", row["scene_ref"])
        self.assertEqual("nav.grass_field", row["nav_ref"])
        self.assertEqual(1, len(row["spawn_points"]))
        self.assertEqual("world.grass_field.spawn.default", row["spawn_points"][0]["id"])
        self.assertEqual({"x": 0, "y": 0}, row["spawn_points"][0]["position"])

        # 用 check --only world 交叉验证真正落地（不校验 sprite/vfx/sfx，本用例没有那些数据）。
        code, output = run_cli(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--only",
                "world",
            ]
        )
        self.assertEqual(0, code, msg=output)

    def test_map_missing_required_layer_raises(self) -> None:
        case_dir = self.new_case_dir("map_cmd_missing_layer")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(src_dir, {"ground": (16, 16)})  # 缺 overlay

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "broken_field",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(1, code)
        self.assertIn("overlay.png", output)

    def test_map_dry_run_writes_nothing(self) -> None:
        case_dir = self.new_case_dir("map_cmd_dry_run")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(src_dir, {"ground": (16, 16), "overlay": (16, 16)})

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "dry_field",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--dry-run",
            ]
        )
        self.assertEqual(0, code, msg=output)
        self.assertFalse((assets_root / "_test" / "maps" / "dry_field").exists())
        self.assertFalse((data_root / "_test" / "world" / "world.map.json").exists())

    def test_map_custom_spawn_points(self) -> None:
        case_dir = self.new_case_dir("map_cmd_spawns")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(src_dir, {"ground": (16, 16), "overlay": (16, 16)})

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "spawn_field",
                "--dataset",
                "_test",
                "--spawn",
                "1,2,90",
                "--spawn",
                "3,4",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)
        world_map_path = data_root / "_test" / "world" / "world.map.json"
        row = json.loads(world_map_path.read_text(encoding="utf-8"))["rows"][0]
        self.assertEqual(2, len(row["spawn_points"]))
        self.assertEqual("world.spawn_field.spawn.default", row["spawn_points"][0]["id"])
        self.assertEqual({"x": 1.0, "y": 2.0}, row["spawn_points"][0]["position"])
        self.assertEqual(90.0, row["spawn_points"][0]["facing"])
        self.assertEqual("world.spawn_field.spawn.1", row["spawn_points"][1]["id"])
        self.assertEqual({"x": 3.0, "y": 4.0}, row["spawn_points"][1]["position"])
        self.assertEqual(0.0, row["spawn_points"][1]["facing"])


class CheckWorldMapTest(ImportAssetsTestBase):
    def test_check_reports_missing_map_dir(self) -> None:
        case_dir = self.new_case_dir("check_world_missing_dir")
        assets_root, data_root = self.roots(case_dir)
        write_json(
            data_root / "_test" / "world" / "world.map.json",
            {
                "table": "world.map",
                "schema_version": 1,
                "rows": [
                    {
                        "id": "world.ghost_field",
                        "scene_ref": "scene.ghost_field",
                        "nav_ref": "nav.ghost_field",
                        "spawn_points": [],
                    }
                ],
            },
        )

        code, output = run_cli(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--only",
                "world",
            ]
        )
        self.assertEqual(1, code)
        self.assertIn("world.ghost_field", output)
        self.assertIn("地图分层图目录不存在", output)

    def test_check_reports_missing_required_layer_file(self) -> None:
        case_dir = self.new_case_dir("check_world_missing_layer")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        src_dir.mkdir(parents=True, exist_ok=True)
        make_layer_image((16, 16)).save(src_dir / "ground.png")
        make_layer_image((16, 16)).save(src_dir / "overlay.png")

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "half_built_field",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        # 事后误删其中一个必需分层文件，模拟资产被误删/未提交的情况。
        victim = assets_root / "_test" / "maps" / "half_built_field" / "overlay.png"
        self.assertTrue(victim.is_file())
        victim.unlink()

        code, output = run_cli(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--only",
                "world",
            ]
        )
        self.assertEqual(1, code)
        self.assertIn(str(victim), output)


class IconCommandTest(ImportAssetsTestBase):
    def test_icon_normalizes_size_and_lists_manifest(self) -> None:
        case_dir = self.new_case_dir("icon_cmd")
        assets_root, _ = self.roots(case_dir)
        src_path = case_dir / "src_icon.png"
        make_layer_image((120, 40), color=(10, 10, 10, 255)).save(src_path)

        code, output = run_cli(
            [
                "icon",
                str(src_path),
                "--dataset",
                "_test",
                "--category",
                "item",
                "--size",
                "64",
                "--assets-root",
                str(assets_root),
            ]
        )
        self.assertEqual(0, code, msg=output)
        out_path = assets_root / "_test" / "icons" / "item" / "src_icon.png"
        self.assertTrue(out_path.is_file())
        with Image.open(out_path) as img:
            self.assertEqual((64, 64), img.size)
        self.assertIn("icon.item.src_icon", output)


class VfxCommandTest(ImportAssetsTestBase):
    def test_vfx_packs_atlas_and_writes_def_row(self) -> None:
        case_dir = self.new_case_dir("vfx_cmd")
        assets_root, data_root = self.roots(case_dir)
        frames_dir = case_dir / "fire_impact_frames"
        frames_dir.mkdir(parents=True, exist_ok=True)
        for i in range(4):
            make_layer_image((20, 20), color=(255, 120, 0, 255)).save(frames_dir / f"frame_{i:04d}.png")

        code, output = run_cli(
            [
                "vfx",
                str(frames_dir),
                "--dataset",
                "_test",
                "--id",
                "vfx.fire_impact_test",
                "--category",
                "impact",
                "--attach-mode",
                "socket",
                "--fps",
                "20",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        out_dir = assets_root / "_test" / "vfx" / "fire_impact_test"
        self.assertTrue((out_dir / "atlas.png").is_file())
        self.assertFalse((out_dir / "atlas.json").exists())
        frames_json = json.loads((out_dir / "frames.json").read_text(encoding="utf-8"))
        self.assertEqual(20, frames_json["fps"])
        self.assertAlmostEqual(1 / 20, frames_json["frame_duration"])
        self.assertFalse(frames_json["loop"])
        self.assertEqual(4, len(frames_json["frames"]))
        self.assertEqual([0, 1, 2, 3], [f["index"] for f in frames_json["frames"]])
        for f in frames_json["frames"]:
            self.assertAlmostEqual(1 / 20, f["duration"])
            self.assertEqual(20, f["w"])
            self.assertEqual(20, f["h"])
        self.assertEqual(20, frames_json["frame_w"])
        self.assertEqual(20, frames_json["frame_h"])

        vfx_def = json.loads((data_root / "_test" / "vfx" / "vfx.def.json").read_text(encoding="utf-8"))
        row = vfx_def["rows"][0]
        self.assertEqual("vfx.fire_impact_test", row["id"])
        self.assertEqual("impact", row["category"])
        self.assertEqual("socket", row["attach_mode"])
        self.assertAlmostEqual(4 / 20, row["lifetime"])
        self.assertEqual("vfx.fire_impact_test", row["resource_ref"])

    def test_vfx_single_frame_boundary(self) -> None:
        """边界用例：只有 1 帧时图集尺寸应等于该帧尺寸本身，lifetime 按 1/fps 推算。"""
        case_dir = self.new_case_dir("vfx_single_frame")
        assets_root, data_root = self.roots(case_dir)
        frames_dir = case_dir / "spark_frames"
        frames_dir.mkdir(parents=True, exist_ok=True)
        make_layer_image((12, 12), color=(255, 255, 0, 255)).save(frames_dir / "frame_0000.png")

        code, output = run_cli(
            [
                "vfx",
                str(frames_dir),
                "--dataset",
                "_test",
                "--id",
                "vfx.spark_single_test",
                "--fps",
                "10",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        out_dir = assets_root / "_test" / "vfx" / "spark_single_test"
        with Image.open(out_dir / "atlas.png") as atlas:
            self.assertEqual((12, 12), atlas.size)
        frames_json = json.loads((out_dir / "frames.json").read_text(encoding="utf-8"))
        self.assertEqual(1, len(frames_json["frames"]))

        vfx_def = json.loads((data_root / "_test" / "vfx" / "vfx.def.json").read_text(encoding="utf-8"))
        row = vfx_def["rows"][0]
        self.assertAlmostEqual(1 / 10, row["lifetime"])

    def test_vfx_many_frames_with_explicit_lifetime_override(self) -> None:
        """边界用例：多帧（16 帧）+ 显式 --lifetime 时应直接采用显式值，不按帧数/fps 推算。"""
        case_dir = self.new_case_dir("vfx_many_frames")
        assets_root, data_root = self.roots(case_dir)
        frames_dir = case_dir / "burn_frames"
        frames_dir.mkdir(parents=True, exist_ok=True)
        for i in range(16):
            make_layer_image((8, 8), color=(200, 80, 0, 255)).save(frames_dir / f"frame_{i:04d}.png")

        code, output = run_cli(
            [
                "vfx",
                str(frames_dir),
                "--dataset",
                "_test",
                "--id",
                "vfx.burn_many_test",
                "--fps",
                "24",
                "--lifetime",
                "9.99",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        out_dir = assets_root / "_test" / "vfx" / "burn_many_test"
        frames_json = json.loads((out_dir / "frames.json").read_text(encoding="utf-8"))
        self.assertEqual(16, len(frames_json["frames"]))

        vfx_def = json.loads((data_root / "_test" / "vfx" / "vfx.def.json").read_text(encoding="utf-8"))
        row = vfx_def["rows"][0]
        self.assertEqual(9.99, row["lifetime"])

    def test_vfx_loop_without_explicit_lifetime_omits_lifetime_field(self) -> None:
        """新增用例：--loop 且未显式给 --lifetime 时，行里不应出现 lifetime 字段，
        frames.json 的 loop 应为 true（循环特效没有固有时长，见任务口径）。"""
        case_dir = self.new_case_dir("vfx_loop_no_lifetime")
        assets_root, data_root = self.roots(case_dir)
        frames_dir = case_dir / "aura_frames"
        frames_dir.mkdir(parents=True, exist_ok=True)
        for i in range(3):
            make_layer_image((10, 10), color=(0, 200, 200, 255)).save(frames_dir / f"frame_{i:04d}.png")

        code, output = run_cli(
            [
                "vfx",
                str(frames_dir),
                "--dataset",
                "_test",
                "--id",
                "vfx.aura_loop_test",
                "--fps",
                "10",
                "--loop",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        out_dir = assets_root / "_test" / "vfx" / "aura_loop_test"
        frames_json = json.loads((out_dir / "frames.json").read_text(encoding="utf-8"))
        self.assertTrue(frames_json["loop"])

        vfx_def = json.loads((data_root / "_test" / "vfx" / "vfx.def.json").read_text(encoding="utf-8"))
        row = vfx_def["rows"][0]
        self.assertNotIn("lifetime", row)

    def test_vfx_loop_with_explicit_lifetime_keeps_it(self) -> None:
        """新增用例：--loop 且显式给了 --lifetime 时，仍应写入该 lifetime（不因 --loop 丢弃显式值）。"""
        case_dir = self.new_case_dir("vfx_loop_explicit_lifetime")
        assets_root, data_root = self.roots(case_dir)
        frames_dir = case_dir / "shield_frames"
        frames_dir.mkdir(parents=True, exist_ok=True)
        for i in range(2):
            make_layer_image((10, 10), color=(0, 100, 200, 255)).save(frames_dir / f"frame_{i:04d}.png")

        code, output = run_cli(
            [
                "vfx",
                str(frames_dir),
                "--dataset",
                "_test",
                "--id",
                "vfx.shield_loop_test",
                "--fps",
                "10",
                "--loop",
                "--lifetime",
                "3.5",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        vfx_def = json.loads((data_root / "_test" / "vfx" / "vfx.def.json").read_text(encoding="utf-8"))
        row = vfx_def["rows"][0]
        self.assertEqual(3.5, row["lifetime"])

    def test_vfx_attach_mode_variants_round_trip(self) -> None:
        """多变体用例：三种 attach_mode 各自独立导入，category/attach_mode 均应原样落到各自的行。"""
        case_dir = self.new_case_dir("vfx_attach_mode_variants")
        assets_root, data_root = self.roots(case_dir)

        for attach_mode in ATTACH_MODE_CHOICES:
            frames_dir = case_dir / f"frames_{attach_mode}"
            frames_dir.mkdir(parents=True, exist_ok=True)
            for i in range(2):
                make_layer_image((6, 6), color=(0, 128, 255, 255)).save(frames_dir / f"frame_{i:04d}.png")

            code, output = run_cli(
                [
                    "vfx",
                    str(frames_dir),
                    "--dataset",
                    "_test",
                    "--id",
                    f"vfx.variant_{attach_mode}_test",
                    "--category",
                    "aura",
                    "--attach-mode",
                    attach_mode,
                    "--fps",
                    "12",
                    "--assets-root",
                    str(assets_root),
                    "--data-root",
                    str(data_root),
                ]
            )
            self.assertEqual(0, code, msg=output)

        vfx_def = json.loads((data_root / "_test" / "vfx" / "vfx.def.json").read_text(encoding="utf-8"))
        by_id = {row["id"]: row for row in vfx_def["rows"]}
        self.assertEqual(len(ATTACH_MODE_CHOICES), len(by_id))
        for attach_mode in ATTACH_MODE_CHOICES:
            row = by_id[f"vfx.variant_{attach_mode}_test"]
            self.assertEqual("aura", row["category"])
            self.assertEqual(attach_mode, row["attach_mode"])

    def test_dotted_and_underscored_ids_do_not_collide(self) -> None:
        """TOOL-02 复现/回归（VFX 侧）：'vfx.fire.impact' 与合法的 'vfx.fire_impact' 曾被旧的
        .replace('.', '_') 同时归一成 'fire_impact'，第二次导入会把第一次的图集/帧数据目录
        整个覆盖掉。修复后二者必须落在各自独立的目录下。"""
        case_dir = self.new_case_dir("vfx_no_collision")
        assets_root, data_root = self.roots(case_dir)

        def make_frames(name: str, color: tuple[int, int, int, int]) -> Path:
            frames_dir = case_dir / name
            frames_dir.mkdir(parents=True, exist_ok=True)
            for i in range(2):
                make_layer_image((6, 6), color=color).save(frames_dir / f"frame_{i:04d}.png")
            return frames_dir

        frames_a = make_frames("frames_dotted", (255, 0, 0, 255))
        frames_b = make_frames("frames_underscored", (0, 255, 0, 255))

        code_a, output_a = run_cli(
            [
                "vfx", str(frames_a), "--dataset", "_test", "--id", "vfx.fire.impact",
                "--assets-root", str(assets_root), "--data-root", str(data_root),
            ]
        )
        self.assertEqual(0, code_a, msg=output_a)

        code_b, output_b = run_cli(
            [
                "vfx", str(frames_b), "--dataset", "_test", "--id", "vfx.fire_impact",
                "--assets-root", str(assets_root), "--data-root", str(data_root),
            ]
        )
        self.assertEqual(0, code_b, msg=output_b)

        vfx_def = json.loads((data_root / "_test" / "vfx" / "vfx.def.json").read_text(encoding="utf-8"))
        by_id = {row["id"]: row for row in vfx_def["rows"]}
        self.assertEqual(2, len(by_id), msg=f"两条不同 id 的记录被合并/覆盖: {by_id}")
        self.assertNotEqual(
            by_id["vfx.fire.impact"]["resource_ref"], by_id["vfx.fire_impact"]["resource_ref"]
        )

        vfx_root = assets_root / "_test" / "vfx"
        subdirs = sorted(p.name for p in vfx_root.iterdir() if p.is_dir())
        self.assertEqual(2, len(subdirs), msg=f"应各自落盘独立目录: {subdirs}")

        # 用 check 交叉验证：两条记录各自的图集/帧数据都能被找到（对齐 check_cmd 的归一化口径）。
        code_check, output_check = run_cli(
            [
                "check", "--dataset", "_test", "--assets-root", str(assets_root),
                "--data-root", str(data_root), "--only", "vfx",
            ]
        )
        self.assertEqual(0, code_check, msg=output_check)


class SfxCommandTest(ImportAssetsTestBase):
    def _write_silent_wav(self, path: Path, seconds: float = 0.1, framerate: int = 8000) -> None:
        nframes = int(seconds * framerate)
        with wave.open(str(path), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(framerate)
            wf.writeframes(b"\x00\x00" * nframes)

    def test_sfx_single_file_writes_resource_ref_without_variants(self) -> None:
        """单文件用例：扁平输出 <name>_v0.wav，行写 resource_ref，不写 variants
        （sample_rate/duration_sec 只做校验打印，不进数据表字段，见任务口径）。"""
        case_dir = self.new_case_dir("sfx_cmd")
        assets_root, data_root = self.roots(case_dir)
        wav_path = case_dir / "hit.wav"
        self._write_silent_wav(wav_path)

        code, output = run_cli(
            [
                "sfx",
                str(wav_path),
                "--dataset",
                "_test",
                "--id",
                "sfx.sword_hit_test",
                "--layer",
                "combat",
                "--priority",
                "5",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        out_path = assets_root / "_test" / "sfx" / "sword_hit_test_v0.wav"
        self.assertTrue(out_path.is_file())
        self.assertFalse((assets_root / "_test" / "sfx" / "sword_hit_test").exists())

        sfx_def = json.loads((data_root / "_test" / "sfx" / "sfx.def.json").read_text(encoding="utf-8"))
        row = sfx_def["rows"][0]
        self.assertEqual("sfx.sword_hit_test", row["id"])
        self.assertEqual("combat", row["layer"])
        self.assertEqual(5, row["priority"])
        self.assertEqual("sfx.sword_hit_test_v0", row["resource_ref"])
        self.assertNotIn("variants", row)
        self.assertNotIn("sample_rate", row)
        self.assertNotIn("duration_sec", row)

    def test_sfx_multiple_files_writes_variants_including_resource_ref(self) -> None:
        """多文件用例：>= 2 个源文件时才写 variants，且 variants 必须包含 resource_ref（v0）本身。"""
        case_dir = self.new_case_dir("sfx_cmd_multi")
        assets_root, data_root = self.roots(case_dir)
        wav_a = case_dir / "hit_a.wav"
        wav_b = case_dir / "hit_b.wav"
        self._write_silent_wav(wav_a)
        self._write_silent_wav(wav_b)

        code, output = run_cli(
            [
                "sfx",
                str(wav_a),
                str(wav_b),
                "--dataset",
                "_test",
                "--id",
                "sfx.sword_hit_multi_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        self.assertTrue((assets_root / "_test" / "sfx" / "sword_hit_multi_test_v0.wav").is_file())
        self.assertTrue((assets_root / "_test" / "sfx" / "sword_hit_multi_test_v1.wav").is_file())

        sfx_def = json.loads((data_root / "_test" / "sfx" / "sfx.def.json").read_text(encoding="utf-8"))
        row = sfx_def["rows"][0]
        self.assertEqual("sfx.sword_hit_multi_test_v0", row["resource_ref"])
        self.assertEqual(
            ["sfx.sword_hit_multi_test_v0", "sfx.sword_hit_multi_test_v1"], row["variants"]
        )
        self.assertIn(row["resource_ref"], row["variants"])

    def test_sfx_rejects_non_wav_file(self) -> None:
        case_dir = self.new_case_dir("sfx_cmd_reject")
        assets_root, data_root = self.roots(case_dir)
        fake_mp3 = case_dir / "hit.mp3"
        fake_mp3.write_bytes(b"not really audio")

        code, output = run_cli(
            [
                "sfx",
                str(fake_mp3),
                "--dataset",
                "_test",
                "--id",
                "sfx.bad_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(1, code)
        self.assertIn(".wav", output)

    def test_dotted_and_underscored_ids_do_not_collide(self) -> None:
        """TOOL-02 复现/回归：'sfx.fire.hit'（strip_domain 后 'fire.hit'）与合法的
        'sfx.fire_hit'（strip_domain 后 'fire_hit'）曾被旧的 .replace('.', '_') 同时
        归一成 'fire_hit'，第二次导入会静默覆盖第一次写出的音频文件、且两条 sfx.def
        记录指向同一份物理资源。修复后二者必须各自拥有独立的音频文件与 resource_ref。"""
        case_dir = self.new_case_dir("sfx_cmd_no_collision")
        assets_root, data_root = self.roots(case_dir)
        wav_a = case_dir / "a.wav"
        wav_b = case_dir / "b.wav"
        self._write_silent_wav(wav_a)
        self._write_silent_wav(wav_b)

        code_a, output_a = run_cli(
            [
                "sfx", str(wav_a), "--dataset", "_test", "--id", "sfx.fire.hit",
                "--assets-root", str(assets_root), "--data-root", str(data_root),
            ]
        )
        self.assertEqual(0, code_a, msg=output_a)

        code_b, output_b = run_cli(
            [
                "sfx", str(wav_b), "--dataset", "_test", "--id", "sfx.fire_hit",
                "--assets-root", str(assets_root), "--data-root", str(data_root),
            ]
        )
        self.assertEqual(0, code_b, msg=output_b)

        sfx_def = json.loads((data_root / "_test" / "sfx" / "sfx.def.json").read_text(encoding="utf-8"))
        by_id = {row["id"]: row for row in sfx_def["rows"]}
        self.assertEqual(2, len(by_id), msg=f"两条不同 id 的记录被合并/覆盖: {by_id}")
        self.assertNotEqual(
            by_id["sfx.fire.hit"]["resource_ref"], by_id["sfx.fire_hit"]["resource_ref"]
        )

        audio_files = sorted((assets_root / "_test" / "sfx").glob("*.wav"))
        self.assertEqual(2, len(audio_files), msg=f"应各自落盘独立音频文件: {audio_files}")

    def test_resource_ref_collision_is_rejected_not_overwritten(self) -> None:
        """碰撞防护兜底：即使人为构造出会撞车的 resource_ref，也必须报错而不是覆盖既有资源
        （TOOL-02 验收口径）。用两次同 id 但故意在中途手工在表里塞入一条假冲突记录来触发。"""
        case_dir = self.new_case_dir("sfx_cmd_forced_collision")
        assets_root, data_root = self.roots(case_dir)
        wav_a = case_dir / "a.wav"
        self._write_silent_wav(wav_a)

        code_a, output_a = run_cli(
            [
                "sfx", str(wav_a), "--dataset", "_test", "--id", "sfx.thud_a",
                "--assets-root", str(assets_root), "--data-root", str(data_root),
            ]
        )
        self.assertEqual(0, code_a, msg=output_a)

        sfx_def_path = data_root / "_test" / "sfx" / "sfx.def.json"
        original_audio = (assets_root / "_test" / "sfx" / "thud_a_v0.wav").read_bytes()

        # 手工在表里伪造一条记录，其 resource_ref 恰好等于即将导入的新 id 天然会生成的
        # resource_ref（'sfx.thud_c_v0'），模拟"编码本应不冲突、但物理引用仍然撞车"的场景。
        table = json.loads(sfx_def_path.read_text(encoding="utf-8"))
        table["rows"].append({"id": "sfx.thud_x", "layer": "sfx", "priority": 0, "resource_ref": "sfx.thud_c_v0"})
        sfx_def_path.write_text(json.dumps(table, ensure_ascii=False, indent=2), encoding="utf-8")

        wav_c = case_dir / "c.wav"
        self._write_silent_wav(wav_c)
        code_c, output_c = run_cli(
            [
                "sfx", str(wav_c), "--dataset", "_test", "--id", "sfx.thud_c",
                "--assets-root", str(assets_root), "--data-root", str(data_root),
            ]
        )
        self.assertNotEqual(0, code_c)
        self.assertIn("碰撞", output_c)
        # 既有物理音频文件不应被覆盖，且不应新建 thud_c 的音频文件。
        self.assertEqual(original_audio, (assets_root / "_test" / "sfx" / "thud_a_v0.wav").read_bytes())
        self.assertFalse((assets_root / "_test" / "sfx" / "thud_c_v0.wav").exists())


class CheckSfxReferenceTest(ImportAssetsTestBase):
    """check 子命令对 sfx.def resource_ref/variants 引用缺失的独立重校验。"""

    def _write_silent_wav(self, path: Path, seconds: float = 0.1, framerate: int = 8000) -> None:
        nframes = int(seconds * framerate)
        with wave.open(str(path), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(framerate)
            wf.writeframes(b"\x00\x00" * nframes)

    def test_check_reports_missing_resource_ref_file(self) -> None:
        case_dir = self.new_case_dir("check_sfx_missing_ref")
        assets_root, data_root = self.roots(case_dir)
        wav_path = case_dir / "hit.wav"
        self._write_silent_wav(wav_path)

        code, output = run_cli(
            [
                "sfx",
                str(wav_path),
                "--dataset",
                "_test",
                "--id",
                "sfx.thud_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        # 事后误删已生成的音频文件，模拟资产被误删/未提交的情况。
        victim = assets_root / "_test" / "sfx" / "thud_test_v0.wav"
        self.assertTrue(victim.is_file())
        victim.unlink()

        code, output = run_cli(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--only",
                "sfx",
            ]
        )
        self.assertEqual(1, code)
        self.assertIn(str(victim), output)

    def test_check_reports_missing_variant_file(self) -> None:
        case_dir = self.new_case_dir("check_sfx_missing_variant")
        assets_root, data_root = self.roots(case_dir)
        wav_a = case_dir / "hit_a.wav"
        wav_b = case_dir / "hit_b.wav"
        self._write_silent_wav(wav_a)
        self._write_silent_wav(wav_b)

        code, output = run_cli(
            [
                "sfx",
                str(wav_a),
                str(wav_b),
                "--dataset",
                "_test",
                "--id",
                "sfx.clang_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        victim = assets_root / "_test" / "sfx" / "clang_test_v1.wav"
        self.assertTrue(victim.is_file())
        victim.unlink()

        code, output = run_cli(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--only",
                "sfx",
            ]
        )
        self.assertEqual(1, code)
        self.assertIn(str(victim), output)


class AssetImportErrorDirectTest(unittest.TestCase):
    """直接测（不经 CLI）common.merge_write_row 与 directions 模块的边界行为。"""

    def test_unsupported_direction_count_raises(self) -> None:
        from asset_import.directions import canonical_slot_names

        with self.assertRaises(AssetImportError):
            canonical_slot_names(6)

    def test_all_slot_names_length_matches_direction_count(self) -> None:
        from asset_import.directions import all_slot_names

        self.assertEqual(4, len(all_slot_names(4)))
        self.assertEqual(8, len(all_slot_names(8)))
        self.assertEqual(16, len(all_slot_names(16)))


if __name__ == "__main__":
    unittest.main()
