"""``toolchain/validate_data.py --json`` 合并输出结构回归测试（消费方反馈 E8 根治，2026-09-10，
见 architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E8）。

背景：``validate_data.py`` 此前没有 ``--json`` 出口，调用方（编辑器等工具）想拿两道校验的结构化
结果只能自己解析人类可读文本。根治：新增 ``--json``，把第一道骨架检查结果与第二道
``toolchain/validator --json`` 输出合并为一份 JSON 打印到标准输出（人类可读诊断消息改打印到标准
错误），字段结构见脚本文件头 docstring：
``{"skeleton": {...}, "validator": {...} | null | {raw_output, parse_error, returncode},
"exit_code": int}``。

本文件复用 ``test_validate_data_validator_project_path.py``/
``test_validate_data_precompiled_validator.py`` 的"拷贝成独立模块 + monkeypatch
``shutil.which``/``subprocess.run``"手法，不依赖真实安装 dotnet，只断言标准输出上打印的 JSON
结构与字段语义。

运行：``python -m pytest toolchain/tests/test_validate_data_json_output.py -q`` 或
``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import importlib.util
import json
import shutil
import tempfile
from pathlib import Path
from types import SimpleNamespace

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]


def _load_validate_data_copy(module_dir: Path):
    module_path = module_dir / "validate_data.py"
    spec = importlib.util.spec_from_file_location("validate_data_copy_under_test_json", module_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _make_toolchain_copy(tmp_root: Path) -> Path:
    toolchain_copy = tmp_root / "toolchain_copy"
    toolchain_copy.mkdir(parents=True)
    shutil.copy(TOOLCHAIN_DIR / "validate_data.py", toolchain_copy / "validate_data.py")
    shutil.copy(TOOLCHAIN_DIR / "_console.py", toolchain_copy / "_console.py")
    (toolchain_copy / "validator").mkdir()
    return toolchain_copy


def _make_valid_data_root(tmp_root: Path) -> Path:
    data_root = tmp_root / "data_root"
    data_root.mkdir()
    (data_root / "test.thing.json").write_text(
        json.dumps({"table": "test.thing", "schema_version": 1, "rows": []}),
        encoding="utf-8",
    )
    return data_root


def _make_invalid_data_root(tmp_root: Path) -> Path:
    """一个骨架检查会报错的数据根（table 字段与文件名不一致）。"""
    data_root = tmp_root / "bad_data_root"
    data_root.mkdir()
    (data_root / "test.thing.json").write_text(
        json.dumps({"table": "test.wrong_name", "schema_version": 1, "rows": []}),
        encoding="utf-8",
    )
    return data_root


class _Tmp:
    def __init__(self, base: Path):
        self.base = base


def test_json_flag_emits_single_json_object_with_expected_shape(tmp_path, capsys) -> None:
    toolchain_copy = _make_toolchain_copy(tmp_path)
    data_root = _make_valid_data_root(tmp_path)
    module = _load_validate_data_copy(toolchain_copy)

    def fake_which(name):
        return "/fake/dotnet"

    validator_json_payload = {
        "tables": 1,
        "records": 0,
        "errors": 0,
        "warnings": 0,
        "blocking": False,
        "issues": [],
        "overrides": [],
    }

    def fake_run(cmd, cwd=None, capture_output=False, text=False, encoding=None, errors=None):
        assert "--json" in cmd, "validate_data.py --json 应把 --json 透传给 validator"
        return SimpleNamespace(
            returncode=0,
            stdout=json.dumps(validator_json_payload),
            stderr="",
        )

    # 判断记录：不能写 module.shutil.which = .../module.subprocess.run = ...——shutil/subprocess 是解释器内所有模块共享的同一个全局模块对象，那样写会把真实的 shutil.which/subprocess.run 在整个进程范围内永久替换掉，泄漏给按字母序更晚收集执行、真正需要跑真实
    # PowerShell 子进程的用例（test_get_framework_*/test_powershell_* 等），曾经实测复现过
    # 整个 toolchain/tests 套件级联失败。改为只重新绑定本次动态加载出的这个模块实例自己的
    # shutil/subprocess 名字，指向只带所需属性的轻量命名空间对象，不触碰真正的全局模块。
    module.shutil = SimpleNamespace(which=fake_which)
    module.subprocess = SimpleNamespace(run=fake_run)

    exit_code = module.main(["--data-root", str(data_root), "--json"])
    assert exit_code == 0

    captured = capsys.readouterr()
    # 标准输出应当只有一份可解析的 JSON（不能混入任何人类可读诊断文本）。
    result = json.loads(captured.out)
    assert set(result.keys()) == {"skeleton", "validator", "exit_code"}
    assert result["exit_code"] == 0
    assert result["skeleton"]["files_checked"] == 1
    assert result["skeleton"]["error_count"] == 0
    assert result["skeleton"]["errors"] == []
    assert result["validator"] == validator_json_payload

    # 人类可读的诊断消息应改走标准错误，不出现在标准输出里。
    assert "第一道" in captured.err or "骨架检查" in captured.err


def test_json_flag_collects_skeleton_errors(tmp_path, capsys) -> None:
    toolchain_copy = _make_toolchain_copy(tmp_path)
    data_root = _make_invalid_data_root(tmp_path)
    module = _load_validate_data_copy(toolchain_copy)

    # 判断记录：不能写 module.shutil.which = .../module.subprocess.run = ...——shutil/subprocess 是解释器内所有模块共享的同一个全局模块对象，那样写会把真实的 shutil.which/subprocess.run 在整个进程范围内永久替换掉，泄漏给按字母序更晚收集执行、真正需要跑真实
    # PowerShell 子进程的用例（test_get_framework_*/test_powershell_* 等），曾经实测复现过
    # 整个 toolchain/tests 套件级联失败。改为只重新绑定本次动态加载出的这个模块实例自己的
    # shutil/subprocess 名字，指向只带所需属性的轻量命名空间对象，不触碰真正的全局模块。
    module.shutil = SimpleNamespace(which=lambda name: "/fake/dotnet")
    module.subprocess = SimpleNamespace(run=lambda *a, **k: SimpleNamespace(
        returncode=0, stdout=json.dumps({"tables": 0, "records": 0, "errors": 0, "warnings": 0, "blocking": False}), stderr=""
    ))

    exit_code = module.main(["--data-root", str(data_root), "--json"])
    # 骨架检查有错误 -> 整体退出码非 0（即便 validator 本身报告干净）。
    assert exit_code == 1

    result = json.loads(capsys.readouterr().out)
    assert result["exit_code"] == 1
    assert result["skeleton"]["error_count"] == 1
    assert len(result["skeleton"]["errors"]) == 1
    assert "message" in result["skeleton"]["errors"][0]
    assert "path" in result["skeleton"]["errors"][0]


def test_json_flag_with_skip_dotnet_has_null_validator(tmp_path, capsys) -> None:
    toolchain_copy = _make_toolchain_copy(tmp_path)
    data_root = _make_valid_data_root(tmp_path)
    module = _load_validate_data_copy(toolchain_copy)

    exit_code = module.main(["--data-root", str(data_root), "--json", "--skip-dotnet"])
    assert exit_code == 0

    result = json.loads(capsys.readouterr().out)
    assert result["validator"] is None
    assert result["skeleton"]["files_checked"] == 1


def test_json_flag_validator_non_json_output_falls_back_to_raw(tmp_path, capsys) -> None:
    """validator 子进程没能输出合法 JSON（例如现场编译失败打印了一堆构建日志）时，"validator"
    字段应降级为 {raw_output, parse_error, returncode}，而不是让整个 --json 调用崩溃。
    """
    toolchain_copy = _make_toolchain_copy(tmp_path)
    data_root = _make_valid_data_root(tmp_path)
    module = _load_validate_data_copy(toolchain_copy)

    # 判断记录：不能直接改写 module.shutil.which/module.subprocess.run（全局共享模块对象泄漏
    # 风险，见本文件其它用例同一处判断记录），改为重新绑定本模块实例自己的 shutil/subprocess 名字。
    module.shutil = SimpleNamespace(which=lambda name: "/fake/dotnet")
    module.subprocess = SimpleNamespace(run=lambda *a, **k: SimpleNamespace(
        returncode=1, stdout="error CS1234: something went wrong\nbuild failed", stderr=""
    ))

    exit_code = module.main(["--data-root", str(data_root), "--json"])
    assert exit_code == 1

    result = json.loads(capsys.readouterr().out)
    assert result["validator"]["returncode"] == 1
    assert "raw_output" in result["validator"]
    assert "parse_error" in result["validator"]
    assert "build failed" in result["validator"]["raw_output"]


if __name__ == "__main__":
    import sys
    import pytest

    sys.exit(pytest.main([__file__, "-q"]))
