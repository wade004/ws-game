"""``toolchain/format_data.py --schema-order`` 回归测试（消费方反馈 E11 根治，2026-09-10，见
architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E11）。

背景：示例数据里记录字段的书写顺序与对应 ``TableSchema.Fields`` 的登记顺序经常不一致。根治：新增
``toolchain/format_data.py --schema-order``，调用 ``toolchain/validator --list-tables --json``
（消费方反馈 E11 同一处改动新增的 ``fields`` 数组，见 ``toolchain/validator/Program.cs``
``PrintJson`` 判断记录）拿到每张表的字段登记顺序，按该顺序重排每条记录的字段（未登记字段保持原相对
顺序追加在末尾）；``--check`` 模式只检查、不写文件，有差异时以退出码 1 结束。

本文件复用其它 ``toolchain/tests`` 用例的"拷贝成独立模块 + monkeypatch ``subprocess.run``"手法，
不依赖真实安装 dotnet；构造一个已知字段顺序错乱的临时数据文件，断言重排结果与 ``--check`` 判定
行为符合预期。

运行：``python -m pytest toolchain/tests/test_format_data_schema_order.py -q`` 或
``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import importlib.util
import json
import shutil
from pathlib import Path
from types import SimpleNamespace

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]


def _load_format_data_copy(module_dir: Path):
    module_path = module_dir / "format_data.py"
    spec = importlib.util.spec_from_file_location("format_data_copy_under_test", module_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _make_toolchain_copy(tmp_root: Path) -> Path:
    toolchain_copy = tmp_root / "toolchain_copy"
    toolchain_copy.mkdir(parents=True)
    shutil.copy(TOOLCHAIN_DIR / "format_data.py", toolchain_copy / "format_data.py")
    shutil.copy(TOOLCHAIN_DIR / "_console.py", toolchain_copy / "_console.py")
    (toolchain_copy / "validator").mkdir()
    return toolchain_copy


FIELD_ORDER_MAP = {
    "test.thing": ["id", "name_key", "kind", "value", "description"],
}


def _fake_validator_run(cmd, cwd=None, capture_output=False, text=False, encoding=None, errors=None):
    assert "--list-tables" in cmd
    assert "--json" in cmd
    payload = {
        "tables": 1,
        "records": 1,
        "errors": 0,
        "warnings": 0,
        "blocking": False,
        "tables_list": [
            {"name": table, "record_count": 1, "fields": fields}
            for table, fields in FIELD_ORDER_MAP.items()
        ],
        "issues": [],
        "overrides": [],
        "disabled_optional_rules": [],
        "enabled_optional_rules": [],
    }
    return SimpleNamespace(returncode=0, stdout=json.dumps(payload), stderr="")


def _make_data_root_with_scrambled_order(tmp_root: Path) -> Path:
    data_root = tmp_root / "data_root"
    (data_root / "test").mkdir(parents=True)
    # 字段顺序故意打乱：value 在 id 之前，kind 缺失（未提供，跳过），description 在 name_key 之前。
    row = {
        "value": 42,
        "id": "test.thing.sample",
        "description": "示例",
        "name_key": "l10n.test.thing.sample.name",
    }
    envelope = {"table": "test.thing", "schema_version": 1, "rows": [row]}
    (data_root / "test" / "test.thing.json").write_text(
        json.dumps(envelope, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
    )
    return data_root


def test_check_mode_detects_scrambled_field_order_without_writing(tmp_path: Path) -> None:
    toolchain_copy = _make_toolchain_copy(tmp_path)
    data_root = _make_data_root_with_scrambled_order(tmp_path)
    module = _load_format_data_copy(toolchain_copy)
    # 判断记录：不能写 `module.subprocess.run = _fake_validator_run`——`subprocess` 是 Python
    # 解释器内所有模块共享的同一个全局模块对象，那样写会把真实的 `subprocess.run` 在整个进程
    # 范围内永久替换掉（这个测试文件按字母序比 test_get_framework_*/test_powershell_* 等真正
    # 需要跑真实 PowerShell 子进程的用例更早收集执行，泄漏出去的假 `subprocess.run` 会让那些
    # 用例全部因签名不匹配而失败——曾经实测复现过整个 toolchain/tests 套件级联失败）。改为只重新
    # 绑定本次动态加载出的这个模块实例自己的 `subprocess` 名字，指向一个只有 `run` 属性的轻量
    # 命名空间对象，不触碰真正的全局 subprocess 模块。
    module.subprocess = SimpleNamespace(run=_fake_validator_run)

    original_text = (data_root / "test" / "test.thing.json").read_text(encoding="utf-8")

    exit_code = module.main(["--schema-order", "--check", "--data-root", str(data_root)])

    assert exit_code == 1
    # --check 不应该改动文件内容。
    assert (data_root / "test" / "test.thing.json").read_text(encoding="utf-8") == original_text


def test_schema_order_rewrites_fields_in_declared_order(tmp_path: Path) -> None:
    toolchain_copy = _make_toolchain_copy(tmp_path)
    data_root = _make_data_root_with_scrambled_order(tmp_path)
    module = _load_format_data_copy(toolchain_copy)
    # 判断记录：不能写 `module.subprocess.run = _fake_validator_run`——`subprocess` 是 Python
    # 解释器内所有模块共享的同一个全局模块对象，那样写会把真实的 `subprocess.run` 在整个进程
    # 范围内永久替换掉（这个测试文件按字母序比 test_get_framework_*/test_powershell_* 等真正
    # 需要跑真实 PowerShell 子进程的用例更早收集执行，泄漏出去的假 `subprocess.run` 会让那些
    # 用例全部因签名不匹配而失败——曾经实测复现过整个 toolchain/tests 套件级联失败）。改为只重新
    # 绑定本次动态加载出的这个模块实例自己的 `subprocess` 名字，指向一个只有 `run` 属性的轻量
    # 命名空间对象，不触碰真正的全局 subprocess 模块。
    module.subprocess = SimpleNamespace(run=_fake_validator_run)

    exit_code = module.main(["--schema-order", "--data-root", str(data_root)])
    assert exit_code == 0

    rewritten = json.loads((data_root / "test" / "test.thing.json").read_text(encoding="utf-8"))
    row = rewritten["rows"][0]
    # 已登记字段（id/name_key/value/description，kind 缺失跳过）按 schema 顺序排列在前。
    assert list(row.keys()) == ["id", "name_key", "value", "description"]
    assert row["id"] == "test.thing.sample"
    assert row["value"] == 42

    # 重排后再跑一次 --check 应该判定为一致（幂等）。
    exit_code_check = module.main(["--schema-order", "--check", "--data-root", str(data_root)])
    assert exit_code_check == 0


def test_unregistered_field_kept_and_appended_at_end() -> None:
    """未在 schema 登记顺序里出现的字段（如行级 override/final 元字段）应保持原有相对顺序，
    整体追加在已登记字段之后，不丢弃。
    """
    row = {"extra_unregistered": "z", "value": 1, "id": "test.thing.a", "another_extra": "y"}
    field_order = ["id", "name_key", "value"]

    import sys

    sys.path.insert(0, str(TOOLCHAIN_DIR))
    try:
        import format_data as fd_module  # type: ignore
    finally:
        sys.path.pop(0)

    reordered = fd_module._reorder_row(row, field_order)

    assert list(reordered.keys()) == ["id", "value", "extra_unregistered", "another_extra"]


def test_check_mode_no_changes_needed_returns_zero(tmp_path: Path) -> None:
    toolchain_copy = _make_toolchain_copy(tmp_path)
    data_root = tmp_path / "data_root"
    (data_root / "test").mkdir(parents=True)
    row = {"id": "test.thing.sample", "name_key": "l10n.x", "value": 1, "description": "d"}
    envelope = {"table": "test.thing", "schema_version": 1, "rows": [row]}
    (data_root / "test" / "test.thing.json").write_text(
        json.dumps(envelope, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
    )
    module = _load_format_data_copy(toolchain_copy)
    # 判断记录：不能写 `module.subprocess.run = _fake_validator_run`——`subprocess` 是 Python
    # 解释器内所有模块共享的同一个全局模块对象，那样写会把真实的 `subprocess.run` 在整个进程
    # 范围内永久替换掉（这个测试文件按字母序比 test_get_framework_*/test_powershell_* 等真正
    # 需要跑真实 PowerShell 子进程的用例更早收集执行，泄漏出去的假 `subprocess.run` 会让那些
    # 用例全部因签名不匹配而失败——曾经实测复现过整个 toolchain/tests 套件级联失败）。改为只重新
    # 绑定本次动态加载出的这个模块实例自己的 `subprocess` 名字，指向一个只有 `run` 属性的轻量
    # 命名空间对象，不触碰真正的全局 subprocess 模块。
    module.subprocess = SimpleNamespace(run=_fake_validator_run)

    exit_code = module.main(["--schema-order", "--check", "--data-root", str(data_root)])
    assert exit_code == 0


if __name__ == "__main__":
    import sys as _sys

    import pytest

    _sys.exit(pytest.main([__file__, "-q"]))
