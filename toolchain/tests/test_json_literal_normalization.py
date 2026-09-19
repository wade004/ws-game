"""``toolchain/asset_import/common.py`` 的 ``_normalize_json_literals`` 单元测试。

背景：``map_cmd.py`` 等子命令的 ``--pixels-per-unit``/``--origin-px`` 等命令行参数声明为
``type=float``，经 ``json.dumps`` 按 Python 运行时类型直接序列化后会写出 ``32.0`` 这样的字面量，
与既有数据/人工填写的 ``32`` 形式不一致，制造无意义 diff、破坏"同一输入重复导入产出字节相同
文件"的幂等性（复现命令与根因链路见任务记录）。``_normalize_json_literals`` 在写盘前统一把
"数值上是整数"的 float 收敛为整数字面量；本文件只测这一个纯函数本身的边界，子命令级别的落地
效果（原始文本断言、幂等性）见 ``test_import_assets.py``。

运行：

```
python -m unittest toolchain.tests.test_json_literal_normalization -v
```
"""

from __future__ import annotations

import math
import sys
import unittest
from pathlib import Path

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from asset_import.common import _MAX_SAFE_INT_FLOAT, _normalize_json_literals  # noqa: E402


class NormalizeJsonLiteralsTest(unittest.TestCase):
    def test_integer_valued_float_becomes_int(self) -> None:
        self.assertEqual(32, _normalize_json_literals(32.0))
        self.assertIsInstance(_normalize_json_literals(32.0), int)
        self.assertEqual(0, _normalize_json_literals(0.0))
        self.assertEqual(-16, _normalize_json_literals(-16.0))

    def test_non_integer_float_is_kept_as_float(self) -> None:
        value = _normalize_json_literals(1.2)
        self.assertEqual(1.2, value)
        self.assertIsInstance(value, float)
        self.assertIsInstance(_normalize_json_literals(0.4), float)
        self.assertIsInstance(_normalize_json_literals(9.99), float)

    def test_plain_int_is_unchanged(self) -> None:
        value = _normalize_json_literals(32)
        self.assertEqual(32, value)
        self.assertIsInstance(value, int)

    def test_bool_is_never_converted_to_int(self) -> None:
        # bool 是 int 的子类，必须原样保留为 True/False，绝不能被整数分支处理成 1/0。
        true_value = _normalize_json_literals(True)
        false_value = _normalize_json_literals(False)
        self.assertIs(True, true_value)
        self.assertIs(False, false_value)
        self.assertIsInstance(true_value, bool)
        self.assertIsInstance(false_value, bool)

    def test_bool_survives_inside_nested_structures(self) -> None:
        result = _normalize_json_literals({"loop": True, "flags": [True, False, 1.0]})
        self.assertIs(True, result["loop"])
        self.assertEqual([True, False, 1], result["flags"])
        self.assertIs(True, result["flags"][0])
        self.assertIs(False, result["flags"][1])
        self.assertIsInstance(result["flags"][2], int)
        self.assertNotIsInstance(result["flags"][2], bool)

    def test_nan_and_infinities_are_kept_unchanged(self) -> None:
        nan = _normalize_json_literals(float("nan"))
        self.assertTrue(math.isnan(nan))
        self.assertEqual(float("inf"), _normalize_json_literals(float("inf")))
        self.assertEqual(float("-inf"), _normalize_json_literals(float("-inf")))

    def test_huge_integer_valued_float_is_kept_unchanged(self) -> None:
        # 绝对值 >= 2**53 时 float64 精度已不可靠，不应假装能精确转换成整数字面量。
        huge = float(_MAX_SAFE_INT_FLOAT)
        result = _normalize_json_literals(huge)
        self.assertIsInstance(result, float)
        self.assertEqual(huge, result)

        just_below = float(_MAX_SAFE_INT_FLOAT - 2)
        below_result = _normalize_json_literals(just_below)
        self.assertIsInstance(below_result, int)
        self.assertEqual(_MAX_SAFE_INT_FLOAT - 2, below_result)

        negative_huge = -huge
        self.assertIsInstance(_normalize_json_literals(negative_huge), float)

    def test_nested_dict_and_list_are_recursively_normalized(self) -> None:
        row = {
            "id": "world.sample_field",
            "image_transform": {
                "pixels_per_unit": 32.0,
                "origin_px": {"x": 0.0, "y": 1024.0},
                "image_size_px": {"x": 1024, "y": 1024},
            },
            "spawn_points": [
                {"position": {"x": 1.0, "y": 2.0}, "facing": 90.0},
                {"position": {"x": 3.5, "y": 4.0}, "facing": 0.0},
            ],
        }
        result = _normalize_json_literals(row)
        self.assertEqual(32, result["image_transform"]["pixels_per_unit"])
        self.assertIsInstance(result["image_transform"]["pixels_per_unit"], int)
        self.assertEqual({"x": 0, "y": 1024}, result["image_transform"]["origin_px"])
        self.assertEqual({"x": 1024, "y": 1024}, result["image_transform"]["image_size_px"])
        self.assertEqual({"x": 1, "y": 2}, result["spawn_points"][0]["position"])
        self.assertEqual(90, result["spawn_points"][0]["facing"])
        self.assertEqual({"x": 3.5, "y": 4}, result["spawn_points"][1]["position"])
        self.assertIsInstance(result["spawn_points"][1]["position"]["x"], float)
        self.assertEqual(0, result["spawn_points"][1]["facing"])

    def test_tuple_elements_are_normalized_and_result_is_a_list(self) -> None:
        result = _normalize_json_literals((1.0, 2.5, "x"))
        self.assertEqual([1, 2.5, "x"], result)
        self.assertIsInstance(result, list)

    def test_strings_and_none_are_unchanged(self) -> None:
        self.assertEqual("32.0", _normalize_json_literals("32.0"))
        self.assertIsNone(_normalize_json_literals(None))

    def test_does_not_mutate_input_dict_in_place(self) -> None:
        original = {"a": 1.0, "nested": {"b": 2.0}}
        snapshot_nested_id = id(original["nested"])
        result = _normalize_json_literals(original)

        # 输入 dict 本身的值必须原封不动保留为 float（未被就地改写）。
        self.assertIsInstance(original["a"], float)
        self.assertEqual(1.0, original["a"])
        self.assertIsInstance(original["nested"]["b"], float)
        self.assertEqual(2.0, original["nested"]["b"])

        # 返回的是新对象，不是原对象的引用（包括嵌套 dict）。
        self.assertIsNot(original, result)
        self.assertIsNot(original["nested"], result["nested"])
        self.assertNotEqual(snapshot_nested_id, id(result["nested"]))

        self.assertEqual(1, result["a"])
        self.assertEqual({"b": 2}, result["nested"])

    def test_does_not_mutate_input_list_in_place(self) -> None:
        original = [1.0, {"x": 2.0}]
        result = _normalize_json_literals(original)

        self.assertIsInstance(original[0], float)
        self.assertIsInstance(original[1]["x"], float)
        self.assertIsNot(original, result)
        self.assertIsNot(original[1], result[1])
        self.assertEqual([1, {"x": 2}], result)


if __name__ == "__main__":
    unittest.main()
