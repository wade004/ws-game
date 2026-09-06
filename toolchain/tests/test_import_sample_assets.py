"""``toolchain/import_sample_assets.py`` 的单元测试。

把仓库 ``data/_sample`` 的 ``display``/``vfx``/``sfx``/``world`` 四个子目录复制到临时数据根，
配合临时资产根与指向仓库 ``assets/_placeholder`` 的 ``--placeholder-root`` 运行驱动脚本，断言
产出符合任务口径（id 不变、字段三段式、资源文件落地）。不触碰仓库内的 ``assets/``/``data/``。

运行：

```
python -m unittest toolchain.tests.test_import_sample_assets -v
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
from pathlib import Path

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

import import_sample_assets  # noqa: E402

REPO_ROOT = TOOLCHAIN_DIR.parent
REAL_PLACEHOLDER_ROOT = REPO_ROOT / "assets" / "_placeholder"
REAL_SAMPLE_DATA_ROOT = REPO_ROOT / "data" / "_sample"

EXPECTED_DISPLAY_ROWS = {
    "display.map.sample_hero": ("creature.sample_hero", "creature"),
    "display.map.sample_beast": ("creature.sample_beast", "creature"),
    "display.map.sample_blade": ("item.sample_blade", "item"),
    "display.map.sample_chest": ("gobj.sample_chest", "gobj"),
    "display.map.sample_door": ("gobj.sample_door", "gobj"),
    "display.map.sample_save_point": ("gobj.sample_save_point", "gobj"),
    "display.map.sample_bolt": ("projectile.sample_bolt", "projectile"),
    "display.map.sample_loot_pile": ("loot.generic_pile", "item"),
}


def run_main(argv: list[str]) -> tuple[int, str]:
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        code = import_sample_assets.main(argv)
    return code, out.getvalue()


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


@unittest.skipUnless(REAL_PLACEHOLDER_ROOT.is_dir(), "仓库 assets/_placeholder 不存在，跳过")
class ImportSampleAssetsTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.tmp_root = Path(tempfile.mkdtemp(prefix="import_sample_assets_test_"))
        cls.assets_root = cls.tmp_root / "assets"
        cls.data_root = cls.tmp_root / "data"

        # 只搬 display/vfx/sfx/world 四个子目录（脚本回写/校验会用到的既有表），不触碰仓库本身。
        for sub in ("display", "vfx", "sfx", "world"):
            src = REAL_SAMPLE_DATA_ROOT / sub
            if src.is_dir():
                shutil.copytree(src, cls.data_root / "_sample" / sub)

        cls.code, cls.output = run_main(
            [
                "--assets-root",
                str(cls.assets_root),
                "--data-root",
                str(cls.data_root),
                "--placeholder-root",
                str(REAL_PLACEHOLDER_ROOT),
            ]
        )

    @classmethod
    def tearDownClass(cls) -> None:
        shutil.rmtree(cls.tmp_root, ignore_errors=True)

    def test_returns_success(self) -> None:
        self.assertEqual(0, self.code, msg=self.output)

    def test_display_map_ids_and_logical_ids_unchanged(self) -> None:
        display_map = read_json(self.data_root / "_sample" / "display" / "display.map.json")
        rows_by_id = {row["id"]: row for row in display_map["rows"]}
        self.assertEqual(set(EXPECTED_DISPLAY_ROWS), set(rows_by_id))
        for row_id, (logical_id, category) in EXPECTED_DISPLAY_ROWS.items():
            row = rows_by_id[row_id]
            self.assertEqual(logical_id, row["logical_id"], msg=row_id)
            self.assertEqual(category, row["category"], msg=row_id)

    def test_sprite_set_ids_are_three_segment_and_assets_exist(self) -> None:
        display_map = read_json(self.data_root / "_sample" / "display" / "display.map.json")
        rows_by_id = {row["id"]: row for row in display_map["rows"]}
        for row_id in EXPECTED_DISPLAY_ROWS:
            row = rows_by_id[row_id]
            sprite_set_id = row["sprite_set_id"]
            parts = sprite_set_id.split(".")
            self.assertEqual(3, len(parts), msg=f"{row_id}: {sprite_set_id}")
            self.assertEqual("sprite", parts[0], msg=row_id)
            set_dir_name = f"{parts[1]}_{parts[2]}"
            atlas_json = self.assets_root / "_sample" / "sprites" / set_dir_name / "atlas.json"
            self.assertTrue(atlas_json.is_file(), msg=str(atlas_json))

    def test_vfx_rows_have_frames_json(self) -> None:
        vfx_def = read_json(self.data_root / "_sample" / "vfx" / "vfx.def.json")
        rows_by_id = {row["id"]: row for row in vfx_def["rows"]}
        expected_ids = {"vfx.sample_cast_circle", "vfx.sample_hit_spark", "vfx.sample_burn"}
        self.assertEqual(expected_ids, set(rows_by_id))
        for row_id, row in rows_by_id.items():
            resource_ref = row["resource_ref"]
            name = resource_ref.split(".", 1)[1]
            out_dir = self.assets_root / "_sample" / "vfx" / name
            self.assertTrue((out_dir / "frames.json").is_file(), msg=row_id)
            self.assertTrue((out_dir / "atlas.png").is_file(), msg=row_id)

    def test_vfx_sample_burn_has_no_lifetime(self) -> None:
        vfx_def = read_json(self.data_root / "_sample" / "vfx" / "vfx.def.json")
        rows_by_id = {row["id"]: row for row in vfx_def["rows"]}
        self.assertNotIn("lifetime", rows_by_id["vfx.sample_burn"])

    def test_sfx_sample_hit_variants_contains_resource_ref(self) -> None:
        sfx_def = read_json(self.data_root / "_sample" / "sfx" / "sfx.def.json")
        rows_by_id = {row["id"]: row for row in sfx_def["rows"]}
        row = rows_by_id["sfx.sample_hit"]
        self.assertIn(row["resource_ref"], row["variants"])

    def test_final_check_passes(self) -> None:
        from asset_import.cli import main as import_main

        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            code = import_main(
                [
                    "check",
                    "--dataset",
                    "_sample",
                    "--assets-root",
                    str(self.assets_root),
                    "--data-root",
                    str(self.data_root),
                ]
            )
        self.assertEqual(0, code, msg=out.getvalue())


if __name__ == "__main__":
    unittest.main()
