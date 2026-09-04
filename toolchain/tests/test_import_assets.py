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
        sprite_dir = self.assets_root / "_test" / "sprites" / "wolf_grey"
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
        row = self.display_map["rows"][0]
        mirror_pairs = row["mirror_pairs"]
        self.assertEqual(3, len(mirror_pairs))
        actual = {(m["direction_slot"], m["mirror_of"], m["flip_x"]) for m in mirror_pairs}
        expected = {
            ("front_side_l", "front_side_r", True),
            ("side_l", "side_r", True),
            ("back_side_l", "back_side_r", True),
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

        front_png = assets_root / "_test" / "sprites" / "torch" / "front.png"
        with Image.open(front_png) as img:
            self.assertEqual((20, 40), img.size)

        anchors_json = json.loads((assets_root / "_test" / "sprites" / "torch" / "anchors.json").read_text(encoding="utf-8"))
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
        sprite_dir = assets_root / "_test" / "sprites" / "goblin"
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
        victim = assets_root / "_test" / "sprites" / "slime" / "front" / "body.png"
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
        self.assertTrue((out_dir / "atlas.json").is_file())

        vfx_def = json.loads((data_root / "_test" / "vfx" / "vfx.def.json").read_text(encoding="utf-8"))
        row = vfx_def["rows"][0]
        self.assertEqual("vfx.fire_impact_test", row["id"])
        self.assertEqual("impact", row["category"])
        self.assertEqual("socket", row["attach_mode"])
        self.assertAlmostEqual(4 / 20, row["lifetime"])
        self.assertEqual("vfx.fire_impact_test", row["resource_ref"])


class SfxCommandTest(ImportAssetsTestBase):
    def _write_silent_wav(self, path: Path, seconds: float = 0.1, framerate: int = 8000) -> None:
        nframes = int(seconds * framerate)
        with wave.open(str(path), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(framerate)
            wf.writeframes(b"\x00\x00" * nframes)

    def test_sfx_copies_variants_and_writes_def_row(self) -> None:
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

        out_path = assets_root / "_test" / "sfx" / "sword_hit_test" / "v0.wav"
        self.assertTrue(out_path.is_file())

        sfx_def = json.loads((data_root / "_test" / "sfx" / "sfx.def.json").read_text(encoding="utf-8"))
        row = sfx_def["rows"][0]
        self.assertEqual("sfx.sword_hit_test", row["id"])
        self.assertEqual("combat", row["layer"])
        self.assertEqual(5, row["priority"])
        self.assertEqual(1, len(row["variants"]))
        self.assertEqual(8000, row["variants"][0]["sample_rate"])
        self.assertAlmostEqual(0.1, row["variants"][0]["duration_sec"], places=2)

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
