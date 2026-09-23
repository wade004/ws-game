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
import re
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

from asset_import import check_cmd  # noqa: E402
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


def run_cli_split(argv: list[str]) -> tuple[int, str, str]:
    """调用 CLI，分别返回 (returncode, 标准输出文本, 标准错误文本)（消费方反馈第 62 条：
    --json 模式下 stdout 应只有一个 JSON 文档，其余日志改走 stderr，需要分开断言）。"""
    out = io.StringIO()
    err = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = cli_main(argv)
    return code, out.getvalue(), err.getvalue()


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


# ---------------------------------------------------------------------------
# 字面量形式漂移回归通用工具（任务记录"补测试洞"）：077bf765 新增的 4 个"字节级幂等性"
# 用例（见本文件靠后 MapCommandTest.test_map_import_is_idempotent_byte_for_byte 等，已
# 改名并更新 docstring）经反证实验证实对本 bug 本身无防护力——它们只是把同一版本代码连跑
# 两次比较字节，天然自洽（同一套（哪怕有漂移的）序列化逻辑跑两次结果必然一致），测不出
# "新写出的字面量形式与手写惯例/历史提交不一致"这个真实症状。这里补一层通用扫描工具，
# 直接在原始文本上找漂移形式，不先 json.load 再判断——json.load 会把 "32.0" 和 "32" 都解析
# 成同一个 Python 值（32 == 32.0 为真），丢失字面量形式本身，同样测不出这类 bug。

# 匹配"数值上是整数"但写成非整数字面量形式的 JSON 数字：小数点+全零小数部分（如
# "32.0"/"1024.00"），或指数记法（如 "1e3"/"2E+5"/"3.0e2"，无论底数本身是否整数——指数记法
# 本身就是要避免的漂移形式）。``(?<![\w.])``/``(?![\w.])`` 做词边界，避免匹配到更长数字串的
# 子串（如 "160.0" 里误判出 "60.0"）。
_INT_VALUED_FLOAT_LITERAL_RE = re.compile(
    r"(?<![\w.])-?\d+(?:\.0+(?:[eE][+-]?\d+)?|[eE][+-]?\d+)(?![\w.])"
)


def _mask_json_string_contents(text: str) -> str:
    """把 JSON 文本里字符串字面量*内部*的字符替换为占位符 ``x``（定界引号本身与结构字符原样
    保留），正确处理转义引号（``\\"``）不会提前把字符串判定为结束。返回值与输入等长，位置
    一一对应。

    目的：让数字字面量正则只在"值位置"生效，不被字符串内容里碰巧出现的数字形式误判（如
    ``"ref": "sprite/hero_2.0"``、``"note": "v1.0 released"``）。
    """
    out: list[str] = []
    in_string = False
    escape = False
    for ch in text:
        if in_string:
            if escape:
                out.append("x")
                escape = False
            elif ch == "\\":
                out.append("x")
                escape = True
            elif ch == '"':
                out.append('"')
                in_string = False
            else:
                out.append("x")
        else:
            if ch == '"':
                out.append('"')
                in_string = True
            else:
                out.append(ch)
    return "".join(out)


def find_int_valued_float_literals(text: str) -> list[str]:
    """扫描 JSON 原始文本（跳过字符串内容），返回所有"数值上是整数却写成非整数字面量形式"的
    匹配子串（如 ``["32.0", "1024.00", "1e3"]``）；无问题时返回空列表。"""
    return _INT_VALUED_FLOAT_LITERAL_RE.findall(_mask_json_string_contents(text))


def assert_no_int_valued_float_literal_drift(testcase: unittest.TestCase, path: Path) -> None:
    """断言 ``path`` 指向的文件里不存在字面量形式漂移（数值上是整数的 float 却写成
    "32.0"/"1e3" 等非整数形式），用于驱动各写表子命令的回归测试。"""
    text = path.read_text(encoding="utf-8")
    matches = find_int_valued_float_literals(text)
    testcase.assertEqual(
        [], matches, msg=f"{path} 存在字面量形式漂移（数值上是整数却写成非整数 float 形式）: {matches}"
    )


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

    def test_display_map_import_same_version_rerun_produces_identical_bytes(self) -> None:
        """同版本重跑稳定性（不是字面量形式回归闸门——已用反证实验证实）：同一份输入用同一
        版本代码连续导入两次，display.map.json 第二次写出的字节内容必须与第一次（setUp 里
        已跑过一次）完全一致。注意：本用例只能防"重跑本身不稳定"（如依赖字典枚举顺序/浮点
        运算不确定性），不能防"新写出的字面量形式与手写惯例/历史提交不一致"——同一版本的
        （哪怕带漂移的）序列化逻辑连跑两次结果天然一致，测不出这类问题。把 common.py 回退到
        引入 _normalize_json_literals 之前的版本后本用例仍然通过，证明了这一点。字面量形式
        本身的回归闸门见 SpriteLiteralDriftRegressionTest（原始文本扫描 + 显式整数值 float
        参数驱动）。"""
        first_bytes = self.display_map_path.read_bytes()

        code, output = run_cli(
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
        self.assertEqual(0, code, msg=output)
        second_bytes = self.display_map_path.read_bytes()

        self.assertEqual(first_bytes, second_bytes)


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

        # 消费方反馈第 59 条（ADR-0036）：默认 image_transform——ppu 32，origin_px 默认位于图片
        # 左下角（0, ground.png 高度），image_size_px 从 ground.png 实际读出。
        self.assertEqual(32, row["image_transform"]["pixels_per_unit"])
        self.assertEqual({"x": 0, "y": 16}, row["image_transform"]["origin_px"])
        self.assertEqual({"x": 16, "y": 16}, row["image_transform"]["image_size_px"])

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

    def test_map_custom_pixels_per_unit_and_origin_px(self) -> None:
        case_dir = self.new_case_dir("map_cmd_image_transform")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(src_dir, {"ground": (64, 48), "overlay": (64, 48)})

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "transform_field",
                "--dataset",
                "_test",
                "--pixels-per-unit",
                "16",
                "--origin-px",
                "10,5",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        world_map_path = data_root / "_test" / "world" / "world.map.json"
        raw_text = world_map_path.read_text(encoding="utf-8")
        row = json.loads(raw_text)["rows"][0]
        self.assertEqual(16.0, row["image_transform"]["pixels_per_unit"])
        self.assertEqual({"x": 10.0, "y": 5.0}, row["image_transform"]["origin_px"])
        self.assertEqual({"x": 64, "y": 48}, row["image_transform"]["image_size_px"])

        # 字面量形式回归（本 bug 的盲区）：assertEqual(16.0, row[...]) 测不出字面量形式，因为
        # Python 里 16 == 16.0 为真；此处直接断言写出的原始文本，覆盖 --pixels-per-unit/
        # --origin-px 这两个 type=float 的命令行参数，数值上是整数时必须写成整数字面量
        # （"16"/"10"/"5"），不能写成 "16.0"/"10.0"/"5.0"（见 common.py 的
        # _normalize_json_literals 判断记录）。用带引号的字段名前缀 + 数字 + 非数字字符的写法，
        # 避免误匹配 "16" 是 "160" 前缀之类的子串问题。
        self.assertIn('"pixels_per_unit": 16,', raw_text)
        self.assertNotIn('"pixels_per_unit": 16.0', raw_text)
        self.assertIn('"origin_px": {"x": 10, "y": 5}', raw_text)
        self.assertNotIn("10.0", raw_text)
        self.assertNotIn("5.0", raw_text)

        # 用 check --only world 交叉验证真正落地（image_transform 是可选字段，不应导致 check 失败）。
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

    def test_map_import_same_version_rerun_produces_identical_bytes(self) -> None:
        """同版本重跑稳定性（不是字面量形式回归闸门——已用反证实验证实）：同一份输入用同一
        版本代码连续导入两次，world.map.json 第二次写出的字节内容必须与第一次完全一致。
        注意：本用例只能防"重跑本身不稳定"，不能防"新写出的字面量形式与手写惯例/历史提交
        不一致"这个本 bug 的真实症状——同一版本的（哪怕带漂移的）序列化逻辑连跑两次结果天然
        一致；把 common.py 回退到引入 _normalize_json_literals 之前的版本后本用例仍然通过，
        证明了这一点。字面量形式本身的回归闸门见 MapLiteralDriftRegressionTest（原始文本扫描
        + 显式整数值 float 参数驱动）。"""
        case_dir = self.new_case_dir("map_cmd_idempotent")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(src_dir, {"ground": (64, 48), "overlay": (64, 48)})

        argv = [
            "map",
            str(src_dir),
            "--map",
            "idempotent_field",
            "--dataset",
            "_test",
            "--pixels-per-unit",
            "16",
            "--origin-px",
            "10,5",
            "--assets-root",
            str(assets_root),
            "--data-root",
            str(data_root),
        ]
        world_map_path = data_root / "_test" / "world" / "world.map.json"

        code, output = run_cli(argv)
        self.assertEqual(0, code, msg=output)
        first_bytes = world_map_path.read_bytes()

        code, output = run_cli(argv)
        self.assertEqual(0, code, msg=output)
        second_bytes = world_map_path.read_bytes()

        self.assertEqual(first_bytes, second_bytes)

    def test_map_dry_run_still_computes_image_size_without_writing(self) -> None:
        case_dir = self.new_case_dir("map_cmd_dry_run_image_size")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(src_dir, {"ground": (32, 32), "overlay": (32, 32)})

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "dry_transform_field",
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
        self.assertFalse((data_root / "_test" / "world" / "world.map.json").exists())


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

    def test_vfx_import_same_version_rerun_produces_identical_bytes(self) -> None:
        """同版本重跑稳定性（不是字面量形式回归闸门——已用反证实验证实）：同一份输入用同一
        版本代码连续导入两次，vfx.def.json 第二次写出的字节内容必须与第一次完全一致。注意：
        本用例只能防"重跑本身不稳定"，不能防"新写出的字面量形式与手写惯例/历史提交不一致"
        这个本 bug 的真实症状——同一版本的（哪怕带漂移的）序列化逻辑连跑两次结果天然一致；
        把 common.py 回退到引入 _normalize_json_literals 之前的版本后本用例仍然通过，证明了
        这一点。字面量形式本身的回归闸门见 VfxLiteralDriftRegressionTest（原始文本扫描 +
        显式整数值 float 参数驱动）。"""
        case_dir = self.new_case_dir("vfx_idempotent")
        assets_root, data_root = self.roots(case_dir)
        frames_dir = case_dir / "spark_frames"
        frames_dir.mkdir(parents=True, exist_ok=True)
        for i in range(4):
            make_layer_image((8, 8), color=(255, 200, 0, 255)).save(frames_dir / f"frame_{i:04d}.png")

        argv = [
            "vfx",
            str(frames_dir),
            "--dataset",
            "_test",
            "--id",
            "vfx.idempotent_test",
            "--fps",
            "20",
            "--lifetime",
            "2",
            "--assets-root",
            str(assets_root),
            "--data-root",
            str(data_root),
        ]
        vfx_def_path = data_root / "_test" / "vfx" / "vfx.def.json"

        code, output = run_cli(argv)
        self.assertEqual(0, code, msg=output)
        first_bytes = vfx_def_path.read_bytes()

        code, output = run_cli(argv)
        self.assertEqual(0, code, msg=output)
        second_bytes = vfx_def_path.read_bytes()

        self.assertEqual(first_bytes, second_bytes)


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

    def test_sfx_import_same_version_rerun_produces_identical_bytes(self) -> None:
        """同版本重跑稳定性（不是字面量形式回归闸门）：同一份输入用同一版本代码连续导入两次，
        sfx.def.json 第二次写出的字节内容必须与第一次完全一致。注意：sfx 子命令的表字段目前
        没有任何 float 类型的命令行参数流入（priority 是 type=int），本身就不存在本 bug 描述
        的那类字面量漂移攻击面，因此本用例本来就不承担"字面量形式回归闸门"的职责，只保留其
        "重跑本身稳定"这一独立价值；其余三个同名用例（map/display.map/vfx）在改名前确实曾被
        误认为能防字面量漂移，已一并订正 docstring，见 assert_no_int_valued_float_literal_drift
        判断记录。"""
        case_dir = self.new_case_dir("sfx_idempotent")
        assets_root, data_root = self.roots(case_dir)
        wav_path = case_dir / "hit.wav"
        self._write_silent_wav(wav_path)

        argv = [
            "sfx",
            str(wav_path),
            "--dataset",
            "_test",
            "--id",
            "sfx.idempotent_test",
            "--layer",
            "combat",
            "--priority",
            "5",
            "--assets-root",
            str(assets_root),
            "--data-root",
            str(data_root),
        ]
        sfx_def_path = data_root / "_test" / "sfx" / "sfx.def.json"

        code, output = run_cli(argv)
        self.assertEqual(0, code, msg=output)
        first_bytes = sfx_def_path.read_bytes()

        code, output = run_cli(argv)
        self.assertEqual(0, code, msg=output)
        second_bytes = sfx_def_path.read_bytes()

        self.assertEqual(first_bytes, second_bytes)

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


class CheckJsonOutputTest(ImportAssetsTestBase):
    """消费方反馈第 62 条：check --json 的输出契约测试。"""

    def _build_valid_sprite(self, case_name: str) -> tuple[Path, Path]:
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
        return assets_root, data_root

    def test_json_output_is_single_document_with_no_issues_on_valid_dataset(self) -> None:
        assets_root, data_root = self._build_valid_sprite("json_ok")

        code, stdout, stderr = run_cli_split(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--json",
            ]
        )
        self.assertEqual(0, code)
        # stdout 只能有一个 JSON 文档：整体按行 strip 后必须恰好一行非空。
        lines = [line for line in stdout.splitlines() if line.strip()]
        self.assertEqual(1, len(lines), msg=stdout)
        doc = json.loads(lines[0])
        self.assertEqual("import_assets.check", doc["tool"])
        self.assertEqual("_test", doc["dataset"])
        self.assertEqual(["display_anim", "sfx", "sprite", "vfx", "world"], doc["domains"])
        self.assertTrue(doc["ok"])
        self.assertEqual({"error": 0, "warning": 0}, doc["counts"])
        self.assertEqual([], doc["issues"])
        # 人类可读的汇总行改走 stderr，不出现在 stdout。
        self.assertIn("[check]", stderr)

    def test_json_output_issue_fields_complete_on_broken_dataset(self) -> None:
        assets_root, data_root = self._build_valid_sprite("json_broken")
        victim = assets_root / "_test" / "sprites" / "creature_critter" / "front" / "body.png"
        self.assertTrue(victim.is_file())
        victim.unlink()

        code, stdout, stderr = run_cli_split(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--json",
            ]
        )
        self.assertEqual(1, code)
        lines = [line for line in stdout.splitlines() if line.strip()]
        self.assertEqual(1, len(lines), msg=stdout)
        doc = json.loads(lines[0])
        self.assertFalse(doc["ok"])
        self.assertEqual(1, doc["counts"]["error"])
        self.assertEqual(1, len(doc["issues"]))
        issue = doc["issues"][0]
        for key in ("severity", "table", "record_key", "field_path", "check", "message", "path"):
            self.assertIn(key, issue)
        self.assertEqual("error", issue["severity"])
        self.assertEqual("display.map", issue["table"])
        self.assertEqual(check_cmd.CHECK_SPRITE_FRAME_FILE_MISSING, issue["check"])
        self.assertEqual(str(victim), issue["path"])
        self.assertIn(str(victim), issue["message"])
        # 文本模式下同一条问题的 message 逐字必须相同，只是渲染方式不同（拼成 "id: message"）。
        self.assertIn(str(victim), stderr)

    def test_text_mode_output_unchanged_when_json_flag_absent(self) -> None:
        """回归：不加 --json 时，文本输出（含格式、走 stdout 而非 stderr）与改造前逐字节一致。"""
        assets_root, data_root = self._build_valid_sprite("text_unchanged")
        victim = assets_root / "_test" / "sprites" / "creature_critter" / "front" / "body.png"
        victim.unlink()

        code, stdout, stderr = run_cli_split(
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
        self.assertEqual("", stderr)
        self.assertIn(f"display.critter_test: 帧文件缺失: {victim}", stdout)
        self.assertIn("[check] dataset=_test", stdout)
        self.assertNotIn("{", stdout.split("[check]")[0])

    def _seed_other_domain_rows(self, data_root: Path) -> dict[str, list[dict]]:
        """在 _build_valid_sprite 已产出的 display.map 之外，给其余六张表各写入若干行

        （行内容只保证不触发异常，不保证通过检查——本组用例只关心 ``_load_rows`` 读到的
        行数，与该行是否有问题无关，见 check_cmd 模块 docstring"判断记录（--json 顶层新增
        domain_counts 字段）"）。返回按表名分组的行列表，供用例按"数据集里实际的记录数"
        算期望值，不写死裸数。
        """
        rows_by_table: dict[str, list[dict]] = {
            "vfx.def": [{"id": f"vfx.item_{i}"} for i in range(2)],
            "sfx.def": [{"id": f"sfx.item_{i}"} for i in range(3)],
            "world.map": [{"id": "world.demo"}],
            "display.anim_set": [
                {"id": f"display.anim_set.item_{i}", "clips": {}} for i in range(2)
            ],
            "display.weapon_style": [{"id": "display.weapon_style.item_0"}],
            "display.equip_visual": [{"id": "display.equip_visual.item_0"}],
        }
        write_json(data_root / "_test" / "vfx" / "vfx.def.json", {"rows": rows_by_table["vfx.def"]})
        write_json(data_root / "_test" / "sfx" / "sfx.def.json", {"rows": rows_by_table["sfx.def"]})
        write_json(data_root / "_test" / "world" / "world.map.json", {"rows": rows_by_table["world.map"]})
        write_json(
            data_root / "_test" / "display" / "display.anim_set.json",
            {"rows": rows_by_table["display.anim_set"]},
        )
        write_json(
            data_root / "_test" / "display" / "display.weapon_style.json",
            {"rows": rows_by_table["display.weapon_style"]},
        )
        write_json(
            data_root / "_test" / "display" / "display.equip_visual.json",
            {"rows": rows_by_table["display.equip_visual"]},
        )
        return rows_by_table

    def test_json_output_domain_counts_match_loaded_row_counts_and_fields_backward_compatible(
        self,
    ) -> None:
        """消费方反馈第 74 条：--json 顶层新增 domain_counts，数值口径是各表 rows 数组长度

        （与文本汇总行同源），期望值由数据集里实际写入的行数算出，不写死裸数；同时验证
        既有六个顶层字段（tool/dataset/domains/ok/counts/issues）纯加法未受影响——用它们
        彼此间的既有约束（ok 与 issues 是否为空一致、counts 与 issues 里各 severity 计数
        一致）重新核验，而不是抄一份旧的期望值。
        """
        assets_root, data_root = self._build_valid_sprite("domain_counts_full")
        rows_by_table = self._seed_other_domain_rows(data_root)

        display_map_path = data_root / "_test" / "display" / "display.map.json"
        expected_display_map_count = len(
            json.loads(display_map_path.read_text(encoding="utf-8"))["rows"]
        )
        expected_domain_counts = {"display.map": expected_display_map_count}
        expected_domain_counts.update(
            {table: len(rows) for table, rows in rows_by_table.items()}
        )

        code, stdout, stderr = run_cli_split(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--json",
            ]
        )
        lines = [line for line in stdout.splitlines() if line.strip()]
        self.assertEqual(1, len(lines), msg=stdout)
        doc = json.loads(lines[0])

        # 新字段：值等于按数据集实际行数算出的期望值（不是写死的裸数）。
        self.assertEqual(expected_domain_counts, doc["domain_counts"])

        # 向后兼容：既有字段仍在，且相互间的既有约束仍成立（规则校验，不是抄一份旧期望值）。
        self.assertEqual("import_assets.check", doc["tool"])
        self.assertEqual("_test", doc["dataset"])
        self.assertEqual(sorted(check_cmd.DEFAULT_DOMAINS), doc["domains"])
        self.assertIn("issues", doc)
        recomputed_error = sum(1 for i in doc["issues"] if i["severity"] == "error")
        recomputed_warning = sum(1 for i in doc["issues"] if i["severity"] == "warning")
        self.assertEqual({"error": recomputed_error, "warning": recomputed_warning}, doc["counts"])
        self.assertEqual(len(doc["issues"]) == 0, doc["ok"])
        self.assertEqual(0 if doc["ok"] else 1, code)

    def test_json_output_domain_counts_omits_keys_for_domains_excluded_by_only(self) -> None:
        """--only 排除的域，domain_counts 对应表键必须整体不出现——即使磁盘上该表确有数据

        （本用例复用 test_..._backward_compatible 同一份种子数据证明这一点：vfx/sfx/
        display_anim 三个域被 --only 排除后，即便 vfx.def.json 等文件里明明有行，
        domain_counts 里也不能出现 "vfx.def" 等键，区分"未跑该域"与"跑了但 0 条"）。
        """
        assets_root, data_root = self._build_valid_sprite("domain_counts_only_subset")
        self._seed_other_domain_rows(data_root)

        display_map_path = data_root / "_test" / "display" / "display.map.json"
        expected_display_map_count = len(
            json.loads(display_map_path.read_text(encoding="utf-8"))["rows"]
        )
        world_map_path = data_root / "_test" / "world" / "world.map.json"
        expected_world_map_count = len(
            json.loads(world_map_path.read_text(encoding="utf-8"))["rows"]
        )

        code, stdout, stderr = run_cli_split(
            [
                "check",
                "--dataset",
                "_test",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
                "--only",
                "sprite,world",
                "--json",
            ]
        )
        lines = [line for line in stdout.splitlines() if line.strip()]
        self.assertEqual(1, len(lines), msg=stdout)
        doc = json.loads(lines[0])

        self.assertEqual(["sprite", "world"], doc["domains"])
        self.assertEqual(
            {"display.map": expected_display_map_count, "world.map": expected_world_map_count},
            doc["domain_counts"],
        )
        for excluded_table in (
            "vfx.def",
            "sfx.def",
            "display.anim_set",
            "display.weapon_style",
            "display.equip_visual",
        ):
            self.assertNotIn(excluded_table, doc["domain_counts"])


class CheckWorldRowUnitTest(unittest.TestCase):
    """直接单元测试 _check_world_row（不经 CLI），消费方反馈第 62/64 条要求覆盖。"""

    def setUp(self) -> None:
        self.tmp_root = Path(tempfile.mkdtemp(prefix="check_world_row_test_"))
        self.addCleanup(shutil.rmtree, self.tmp_root, ignore_errors=True)
        self.assets_root = self.tmp_root / "assets"
        self.dataset = "_test"

    def _map_dir(self, name: str) -> Path:
        d = self.assets_root / self.dataset / "maps" / name
        d.mkdir(parents=True, exist_ok=True)
        return d

    def test_reports_missing_map_dir(self) -> None:
        row = {"id": "world.forest_glade"}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_world_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual(1, len(problems))
        issue = problems[0]
        self.assertEqual(check_cmd.CHECK_WORLD_MAP_DIR_MISSING, issue.check)
        self.assertEqual("world.map", issue.table)
        self.assertEqual("world.forest_glade", issue.record_key)
        self.assertEqual("error", issue.severity)

    def test_reports_missing_required_layer(self) -> None:
        map_dir = self._map_dir("forest_glade")
        (map_dir / "ground.png").write_bytes(b"\x89PNG")
        # 缺 overlay.png。
        row = {"id": "world.forest_glade"}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_world_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual(1, len(problems))
        self.assertEqual(check_cmd.CHECK_WORLD_MAP_LAYER_MISSING, problems[0].check)
        self.assertTrue(problems[0].path.endswith("overlay.png"))

    def test_passes_when_layers_present_and_refs_consistent(self) -> None:
        map_dir = self._map_dir("forest_glade")
        (map_dir / "ground.png").write_bytes(b"\x89PNG")
        (map_dir / "overlay.png").write_bytes(b"\x89PNG")
        row = {
            "id": "world.forest_glade",
            "scene_ref": "scene.forest_glade",
            "nav_ref": "nav.forest_glade",
        }
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_world_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual([], problems)

    def test_reports_scene_ref_mismatch(self) -> None:
        map_dir = self._map_dir("forest_glade")
        (map_dir / "ground.png").write_bytes(b"\x89PNG")
        (map_dir / "overlay.png").write_bytes(b"\x89PNG")
        row = {"id": "world.forest_glade", "scene_ref": "scene.somewhere_else"}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_world_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual(1, len(problems))
        self.assertEqual(check_cmd.CHECK_WORLD_MAP_SCENE_REF_MISMATCH, problems[0].check)
        self.assertEqual("scene_ref", problems[0].field_path)


class CheckDisplayAnimRowUnitTest(unittest.TestCase):
    """ADR-0038 决策 6 后半验收：直接单元测试 _check_anim_set_row/_check_weapon_style_row/
    _check_equip_visual_row（不经 CLI），覆盖资产根相对（sprite_anim/paperdoll）存在/缺失、引擎侧
    逻辑路径（anim/model）跳过存在性检查、遗留前缀兜底、未知类别前缀四类场景。"""

    def setUp(self) -> None:
        self.tmp_root = Path(tempfile.mkdtemp(prefix="check_display_anim_row_test_"))
        self.addCleanup(shutil.rmtree, self.tmp_root, ignore_errors=True)
        self.assets_root = self.tmp_root / "assets"
        self.dataset = "_test"

    def _write_sprite_anim(self, name: str) -> None:
        out_dir = self.assets_root / self.dataset / "sprite_anim" / name
        out_dir.mkdir(parents=True, exist_ok=True)
        (out_dir / "atlas.png").write_bytes(b"\x89PNG")
        (out_dir / "frames.json").write_text("{}", encoding="utf-8")

    def _write_equip_layer(self, name: str, direction: str, layer_name: str) -> None:
        """ADR-0071 决策 1：sprite 型 mesh_ref 落地布局——与身体纸娃娃层同一套
        sprites/<name>/<direction>/<layer>.png 三级目录，见 ref_conventions.paperdoll_equip_layer_file。"""
        out_dir = self.assets_root / self.dataset / "sprites" / name / direction
        out_dir.mkdir(parents=True, exist_ok=True)
        (out_dir / f"{layer_name}.png").write_bytes(b"\x89PNG")

    def test_anim_set_engine_logical_path_skips_existence_check(self) -> None:
        row = {"id": "display.anim_set.placeholder_biped", "clips": {"idle": {"resource_ref": "anim.idle"}}}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_anim_set_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual([], problems)

    def test_anim_set_sprite_anim_missing_reports_atlas_and_frames(self) -> None:
        row = {"id": "display.anim_set.sample_hero", "clips": {"idle": {"resource_ref": "sprite_anim.sample_hero_idle"}}}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_anim_set_row(row, self.assets_root, self.dataset, problems)
        checks = {p.check for p in problems}
        self.assertEqual(
            {check_cmd.CHECK_DISPLAY_ANIM_SPRITE_ANIM_ATLAS_MISSING, check_cmd.CHECK_DISPLAY_ANIM_SPRITE_ANIM_FRAMES_JSON_MISSING},
            checks,
        )
        for p in problems:
            self.assertEqual("display.anim_set", p.table)
            self.assertEqual("clips[idle].resource_ref", p.field_path)

    def test_anim_set_sprite_anim_present_no_issue(self) -> None:
        self._write_sprite_anim("sample_hero_idle")
        row = {"id": "display.anim_set.sample_hero", "clips": {"idle": {"resource_ref": "sprite_anim.sample_hero_idle"}}}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_anim_set_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual([], problems)

    def test_weapon_style_auto_attack_anim_and_cast_override(self) -> None:
        row = {
            "id": "display.weapon_style.sample_sword",
            "auto_attack_anim": "sprite_anim.missing_swing",
            "cast_anim_override": {"skill.sample_burn": "sprite_anim.missing_cast"},
        }
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_weapon_style_row(row, self.assets_root, self.dataset, problems)
        field_paths = {p.field_path for p in problems}
        self.assertIn("auto_attack_anim", field_paths)
        self.assertIn("cast_anim_override[skill.sample_burn]", field_paths)

    def test_equip_visual_mesh_ref_paperdoll_missing_then_present(self) -> None:
        # ADR-0071 决策 1：sprite 型 mesh_ref 语义变更为"装备层资源集引用"，本域按
        # EQUIP_LAYER_CHECK_DIRECTIONS 三个方向档位各自核对层文件，不再是单个扁平文件；
        # slot_id 推导出的层名 "head" 见 SpriteViewBase.LayerNameFromSlotId 同一规则。
        row = {
            "id": "display.equip_visual.sample_hero_hat", "mode": "slot_mesh", "slot_id": "slot.head",
            "mesh_ref": "paperdoll.item.sample_hero_hat_test",
        }
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_equip_visual_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual(3, len(problems))
        for p in problems:
            self.assertEqual(check_cmd.CHECK_DISPLAY_ANIM_EQUIP_LAYER_FILE_MISSING, p.check)

        for direction in check_cmd.EQUIP_LAYER_CHECK_DIRECTIONS:
            self._write_equip_layer("item_sample_hero_hat_test", direction, "head")
        problems2: list[check_cmd.CheckIssue] = []
        check_cmd._check_equip_visual_row(row, self.assets_root, self.dataset, problems2)
        self.assertEqual([], problems2)

    def test_equip_visual_mesh_ref_paperdoll_missing_slot_id_reports_ref_category_invalid(self) -> None:
        row = {"id": "display.equip_visual.sample_hero_hat", "mode": "slot_mesh", "mesh_ref": "paperdoll.item.sample_hero_hat_test"}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_equip_visual_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual(1, len(problems))
        self.assertEqual(check_cmd.CHECK_DISPLAY_ANIM_REF_CATEGORY_INVALID, problems[0].check)
        self.assertEqual("slot_id", problems[0].field_path)

    def test_equip_visual_mesh_ref_model_logical_path_skips(self) -> None:
        row = {"id": "display.equip_visual.sample_model_helmet", "mesh_ref": "model.placeholder_biped"}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_equip_visual_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual([], problems)

    def test_equip_visual_mesh_ref_legacy_sprite_prefix_reports_asset_missing(self) -> None:
        # 遗留前缀兜底分支覆盖（ADR-0038 决策 4 落地期曾有 data/_sample 真实场景命中本分支；数据
        # 迁移任务已把该行改为 paperdoll 前缀，见 check_cmd 模块 docstring"判断记录"——本用例改用
        # 合成数据继续覆盖该兜底分支本身的行为，不再对应任何现存真实数据行）。
        row = {"id": "display.equip_visual.sample_hero_hat_legacy", "mesh_ref": "sprite.item.sample_hero_hat_test"}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_equip_visual_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual(1, len(problems))
        self.assertEqual(check_cmd.CHECK_DISPLAY_ANIM_ASSET_MISSING, problems[0].check)

    def test_unknown_category_reports_ref_category_invalid(self) -> None:
        row = {"id": "display.equip_visual.sample_bogus", "mesh_ref": "bogus.thing"}
        problems: list[check_cmd.CheckIssue] = []
        check_cmd._check_equip_visual_row(row, self.assets_root, self.dataset, problems)
        self.assertEqual(1, len(problems))
        self.assertEqual(check_cmd.CHECK_DISPLAY_ANIM_REF_CATEGORY_INVALID, problems[0].check)


class DisplayAnimDomainDefaultsTest(unittest.TestCase):
    """display_anim 域数据迁移任务转正（ADR-0038 决策 6 后半"判断记录"）：此前暂不进默认覆盖集合的
    唯一理由（data/_sample 遗留 sprite 前缀 mesh_ref 行）已随数据迁移消除，现与 ALL_DOMAINS 等同，
    省略 --only 时随其余四项一并跑。"""

    def test_display_anim_in_all_domains_and_default(self) -> None:
        self.assertIn("display_anim", check_cmd.ALL_DOMAINS)
        self.assertIn("display_anim", check_cmd.DEFAULT_DOMAINS)
        self.assertEqual(set(check_cmd.ALL_DOMAINS), set(check_cmd.DEFAULT_DOMAINS))

    def test_parse_only_none_returns_default_domains_including_display_anim(self) -> None:
        self.assertEqual(set(check_cmd.DEFAULT_DOMAINS), check_cmd._parse_only(None))
        self.assertIn("display_anim", check_cmd._parse_only(None))

    def test_parse_only_explicit_display_anim_accepted(self) -> None:
        self.assertEqual({"display_anim"}, check_cmd._parse_only("display_anim"))

    def test_cli_default_run_now_covers_display_anim(self) -> None:
        case_dir = Path(tempfile.mkdtemp(prefix="check_display_anim_cli_test_"))
        self.addCleanup(shutil.rmtree, case_dir, ignore_errors=True)
        assets_root = case_dir / "assets"
        data_root = case_dir / "data"
        write_json(data_root / "_test" / "display" / "display.equip_visual.json", {
            "table": "display.equip_visual", "schema_version": 1,
            "rows": [{"id": "display.equip_visual.sample", "mesh_ref": "bogus.thing"}],
        })

        # display_anim 已纳入 DEFAULT_DOMAINS：省略 --only 的默认调用现在也会跑到这条非法类别前缀。
        code, output = run_cli([
            "check", "--dataset", "_test",
            "--assets-root", str(assets_root), "--data-root", str(data_root),
        ])
        self.assertEqual(1, code, msg=output)

        code, stdout, _stderr = run_cli_split([
            "check", "--dataset", "_test",
            "--assets-root", str(assets_root), "--data-root", str(data_root),
            "--only", "display_anim", "--json",
        ])
        self.assertEqual(1, code, msg=stdout)
        lines = [line for line in stdout.splitlines() if line.strip()]
        self.assertEqual(1, len(lines), msg=stdout)
        doc = json.loads(lines[0])
        self.assertEqual(1, len(doc["issues"]))
        self.assertEqual(check_cmd.CHECK_DISPLAY_ANIM_REF_CATEGORY_INVALID, doc["issues"][0]["check"])


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


class LiteralDriftScanHelperTest(unittest.TestCase):
    """直接测试字面量漂移扫描工具本身（find_int_valued_float_literals），覆盖其正确性边界
    ——这是后面所有"驱动子命令 + 扫原始文本"用例的地基，必须先证明扫描器本身可靠，尤其是
    "不误判字符串内容里的数字子串"这一条（否则扫描器本身就会产生假阳性/假阴性）。"""

    def test_detects_integer_valued_float_in_value_position(self) -> None:
        text = '{"a": 32.0, "b": {"x": 0.0, "y": 1024.0}, "c": -16.0}'
        self.assertEqual(["32.0", "0.0", "1024.0", "-16.0"], find_int_valued_float_literals(text))

    def test_ignores_digits_inside_string_values(self) -> None:
        # 字符串内容里碰巧出现的数字形式不应被误判（如资源引用/备注里恰好含 "2.0" 子串）。
        text = '{"ref": "sprite/hero_2.0", "note": "v1.0 released", "x": 5}'
        self.assertEqual([], find_int_valued_float_literals(text))

    def test_escaped_quote_inside_string_does_not_break_masking(self) -> None:
        text = r'{"note": "quote \" then 2.0 stays inside string", "x": 5}'
        self.assertEqual([], find_int_valued_float_literals(text))

    def test_detects_exponential_forms(self) -> None:
        text = '{"a": 1e3, "b": 2E+5, "c": 3.0e2}'
        self.assertEqual(["1e3", "2E+5", "3.0e2"], find_int_valued_float_literals(text))

    def test_non_integer_float_is_not_flagged(self) -> None:
        text = '{"a": 1.5, "b": 0.25, "c": -3.75, "d": 12.125}'
        self.assertEqual([], find_int_valued_float_literals(text))

    def test_plain_integers_are_not_flagged(self) -> None:
        text = '{"a": 32, "b": -16, "c": 0, "d": 1024}'
        self.assertEqual([], find_int_valued_float_literals(text))

    def test_does_not_match_substring_of_longer_number(self) -> None:
        # "160.0" 不应被误判出内部子串 "60.0"。
        text = '{"a": 160.0}'
        self.assertEqual(["160.0"], find_int_valued_float_literals(text))


class MapLiteralDriftRegressionTest(ImportAssetsTestBase):
    """字面量漂移回归闸门（补 077bf765 幂等性用例测不出的洞）：map 子命令用显式整数值 float
    参数驱动（--pixels-per-unit/--origin-px/--spawn 三个 type=float 命令行路径），断言写出的
    world.map.json 原始文本里不出现 "32.0" 这类漂移形式，同时断言真正的非整数 float 原样保留
    （防止将来有人把规范化写成截断）。已用反证实验确认：把 common.py 回退到 077bf765^
    （_normalize_json_literals 引入前的版本）后，本类两个用例均会失败（结论见任务报告）。"""

    def _build_layer_src(self, src_dir: Path, layers: dict[str, tuple[int, int]]) -> None:
        src_dir.mkdir(parents=True, exist_ok=True)
        for name, size in layers.items():
            make_layer_image(size, color=(30, 60, 30, 255)).save(src_dir / f"{name}.png")

    def test_integer_valued_float_args_produce_integer_literals(self) -> None:
        case_dir = self.new_case_dir("map_literal_drift_int")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(src_dir, {"ground": (64, 48), "overlay": (64, 48)})

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "literal_int_field",
                "--dataset",
                "_test",
                "--pixels-per-unit",
                "32.0",
                "--origin-px",
                "0.0,1024.0",
                "--spawn",
                "3.0,4.0,90.0",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        world_map_path = data_root / "_test" / "world" / "world.map.json"
        assert_no_int_valued_float_literal_drift(self, world_map_path)

        raw_text = world_map_path.read_text(encoding="utf-8")
        self.assertIn('"pixels_per_unit": 32,', raw_text)
        self.assertIn('"origin_px": {"x": 0, "y": 1024}', raw_text)
        self.assertIn('"position": {"x": 3, "y": 4}, "facing": 90', raw_text)

    def test_non_integer_float_args_are_preserved_as_float(self) -> None:
        case_dir = self.new_case_dir("map_literal_drift_frac")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src"
        self._build_layer_src(src_dir, {"ground": (64, 48), "overlay": (64, 48)})

        code, output = run_cli(
            [
                "map",
                str(src_dir),
                "--map",
                "literal_frac_field",
                "--dataset",
                "_test",
                "--pixels-per-unit",
                "16.5",
                "--origin-px",
                "1.25,2.75",
                "--spawn",
                "3.5,4.25,12.5",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        world_map_path = data_root / "_test" / "world" / "world.map.json"
        # 先确认这些非整数值本身没有被上面的整数化规则误伤（回归防线：别把规范化写成截断）。
        assert_no_int_valued_float_literal_drift(self, world_map_path)

        raw_text = world_map_path.read_text(encoding="utf-8")
        self.assertIn('"pixels_per_unit": 16.5,', raw_text)
        self.assertIn('"origin_px": {"x": 1.25, "y": 2.75}', raw_text)
        self.assertIn('"position": {"x": 3.5, "y": 4.25}, "facing": 12.5', raw_text)


class SpriteLiteralDriftRegressionTest(ImportAssetsTestBase):
    """字面量漂移回归闸门（补 077bf765 幂等性用例测不出的洞）：sprite 子命令用显式整数值
    float 参数驱动（--scale/--pixels-per-unit 两个 type=float 命令行路径，落到 display.map
    表），断言写出的 display.map.json 原始文本里不出现漂移形式，同时断言真正的非整数 float
    原样保留。已用反证实验确认：把 common.py 回退到 077bf765^ 后，本类两个用例均会失败
    （结论见任务报告）。"""

    def test_integer_valued_float_args_produce_integer_literals(self) -> None:
        case_dir = self.new_case_dir("sprite_literal_drift_int")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src" / "wolf_literal_int"
        build_layered_sprite_src(src_dir, CANONICAL_8)

        anchors_path = case_dir / "anchors.json"
        # root=[32,64] / --pixels-per-unit 16.0 -> anchor_points.root = {x:2.0, y:4.0}，
        # 数值上是整数，规范化后应写成 {"x": 2, "y": 4}。
        write_json(anchors_path, {slot: {"root": [32, 64]} for slot in CANONICAL_8})

        code, output = run_cli(
            [
                "sprite",
                str(src_dir),
                "--dataset",
                "_test",
                "--category",
                "creature",
                "--logical-id",
                "creature.wolf_literal_int_test",
                "--anchors",
                str(anchors_path),
                "--scale",
                "2.0",
                "--pixels-per-unit",
                "16.0",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        display_map_path = data_root / "_test" / "display" / "display.map.json"
        assert_no_int_valued_float_literal_drift(self, display_map_path)

        raw_text = display_map_path.read_text(encoding="utf-8")
        self.assertIn('"scale": 2,', raw_text)
        self.assertIn('"root": {"x": 2, "y": 4}', raw_text)

    def test_non_integer_float_args_are_preserved_as_float(self) -> None:
        case_dir = self.new_case_dir("sprite_literal_drift_frac")
        assets_root, data_root = self.roots(case_dir)
        src_dir = case_dir / "src" / "wolf_literal_frac"
        build_layered_sprite_src(src_dir, CANONICAL_8)

        anchors_path = case_dir / "anchors.json"
        # root=[10,20] / --pixels-per-unit 默认 32.0 -> anchor_points.root = {x:0.3125, y:0.625}
        # （10/32、20/32 均精确不循环，round 不影响结果），真正的非整数值，必须原样保留。
        write_json(anchors_path, {slot: {"root": [10, 20]} for slot in CANONICAL_8})

        code, output = run_cli(
            [
                "sprite",
                str(src_dir),
                "--dataset",
                "_test",
                "--category",
                "creature",
                "--logical-id",
                "creature.wolf_literal_frac_test",
                "--anchors",
                str(anchors_path),
                "--scale",
                "1.5",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        display_map_path = data_root / "_test" / "display" / "display.map.json"
        assert_no_int_valued_float_literal_drift(self, display_map_path)

        raw_text = display_map_path.read_text(encoding="utf-8")
        self.assertIn('"scale": 1.5,', raw_text)
        self.assertIn('"root": {"x": 0.3125, "y": 0.625}', raw_text)


class VfxLiteralDriftRegressionTest(ImportAssetsTestBase):
    """字面量漂移回归闸门（补 077bf765 幂等性用例测不出的洞）：vfx 子命令用显式整数值 float
    参数驱动（--lifetime，type=float 命令行路径，写入 vfx.def.json 的 lifetime 字段）。只扫描
    vfx.def.json（merge_write_row/_row_json 唯一落点）；frames.json 走 write_json_pretty 旁路，
    077bf765 提交说明已明确排除在本次修复范围外（该旁路已提交数据里存在大量 "fps": 20.0 等
    整数值 float，规范化会改动已提交样例数据字面量，留给设计层另行拍板），不在本用例断言
    范围内。已用反证实验确认：把 common.py 回退到 077bf765^ 后，本类两个用例均会失败
    （结论见任务报告）。"""

    def _build_frames(self, frames_dir: Path) -> None:
        frames_dir.mkdir(parents=True, exist_ok=True)
        for i in range(2):
            make_layer_image((8, 8), color=(255, 120, 0, 255)).save(frames_dir / f"frame_{i:04d}.png")

    def test_integer_valued_lifetime_produces_integer_literal(self) -> None:
        case_dir = self.new_case_dir("vfx_literal_drift_int")
        assets_root, data_root = self.roots(case_dir)
        frames_dir = case_dir / "frames"
        self._build_frames(frames_dir)

        code, output = run_cli(
            [
                "vfx",
                str(frames_dir),
                "--dataset",
                "_test",
                "--id",
                "vfx.literal_int_test",
                "--lifetime",
                "2.0",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        vfx_def_path = data_root / "_test" / "vfx" / "vfx.def.json"
        assert_no_int_valued_float_literal_drift(self, vfx_def_path)

        raw_text = vfx_def_path.read_text(encoding="utf-8")
        self.assertIn('"lifetime": 2,', raw_text)

    def test_non_integer_lifetime_is_preserved_as_float(self) -> None:
        case_dir = self.new_case_dir("vfx_literal_drift_frac")
        assets_root, data_root = self.roots(case_dir)
        frames_dir = case_dir / "frames"
        self._build_frames(frames_dir)

        code, output = run_cli(
            [
                "vfx",
                str(frames_dir),
                "--dataset",
                "_test",
                "--id",
                "vfx.literal_frac_test",
                "--lifetime",
                "3.5",
                "--assets-root",
                str(assets_root),
                "--data-root",
                str(data_root),
            ]
        )
        self.assertEqual(0, code, msg=output)

        vfx_def_path = data_root / "_test" / "vfx" / "vfx.def.json"
        assert_no_int_valued_float_literal_drift(self, vfx_def_path)

        raw_text = vfx_def_path.read_text(encoding="utf-8")
        self.assertIn('"lifetime": 3.5,', raw_text)


if __name__ == "__main__":
    unittest.main()
