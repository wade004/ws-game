"""``toolchain/validate_data.py`` 的 ``--no-missing-translation-warning`` 参数透传回归测试
（消费方反馈第 42 条：``DataRegistryOptions.WarnOnMissingTranslation``、
``toolchain/validator --no-missing-translation-warning``）。

背景：本参数只做"收敛用户输入 → 透传给 ``toolchain/validator`` 同名命令行开关"这一件事，不在
``validate_data.py`` 自身重复实现任何校验逻辑（同文件头判断记录"唯一实现"）。本文件复用
``test_validate_data_precompiled_validator.py`` 的"拷贝成独立模块 + monkeypatch
``shutil.which``/``subprocess.run``"手法，只断言实际传给 ``subprocess.run`` 的命令行参数列表里
是否出现 ``--no-missing-translation-warning``，不依赖真实安装 dotnet、也不解读 validator 自身
的校验结果。

运行：``python -m pytest toolchain/tests/test_validate_data_missing_translation_warning_flag.py -q``
或作为 ``toolchain`` 套件的一部分：``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]


def _load_validate_data_copy(module_dir: Path):
    """见 ``test_validate_data_precompiled_validator.py`` 同名函数判断记录：不能直接
    ``import validate_data``，那样 ``__file__`` 指向仓库内真实路径，测不出"拷贝出去的独立部署
    目录"这个场景。"""
    module_path = module_dir / "validate_data.py"
    spec = importlib.util.spec_from_file_location(
        "validate_data_copy_under_test_missing_translation_warning", module_path
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class MissingTranslationWarningFlagPassthroughTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.tmp_root = Path(self._tmp.name)

    def _make_toolchain_copy(self) -> Path:
        toolchain_copy = self.tmp_root / "toolchain_copy"
        toolchain_copy.mkdir(parents=True)
        shutil.copy(TOOLCHAIN_DIR / "validate_data.py", toolchain_copy / "validate_data.py")
        shutil.copy(TOOLCHAIN_DIR / "_console.py", toolchain_copy / "_console.py")
        # 判断记录同 test_validate_data_precompiled_validator.py：不创建
        # validator/bin/Validator.dll，走 `dotnet run --project` 现场编译分支——本测试只关心
        # 参数是否被透传，不关心走哪条调度分支，选默认（缺失预编译产物）的那条即可。
        (toolchain_copy / "validator").mkdir()
        return toolchain_copy

    def _make_valid_data_root(self) -> Path:
        data_root = self.tmp_root / "data_root"
        data_root.mkdir()
        (data_root / "test.thing.json").write_text(
            json.dumps({"table": "test.thing", "schema_version": 1, "rows": []}),
            encoding="utf-8",
        )
        return data_root

    def _run_with_fakes(self, toolchain_copy: Path, data_root: Path, extra_args: list[str]) -> list:
        module = _load_validate_data_copy(toolchain_copy)
        captured_cmd: dict = {}

        def fake_which(name):
            self.assertEqual(name, "dotnet")
            return "/fake/dotnet"

        def fake_run(cmd, cwd=None):
            captured_cmd["cmd"] = cmd
            return SimpleNamespace(returncode=0)

        # 判断记录同 test_validate_data_precompiled_validator.py：只重新绑定本次动态加载出的
        # 这个模块实例自己的 shutil/subprocess 名字，不污染进程级共享模块对象。
        module.shutil = SimpleNamespace(which=fake_which)
        module.subprocess = SimpleNamespace(run=fake_run)

        module.main(["--data-root", str(data_root), *extra_args])
        return captured_cmd.get("cmd")

    def test_flag_present_appends_validator_switch(self) -> None:
        toolchain_copy = self._make_toolchain_copy()
        data_root = self._make_valid_data_root()

        cmd = self._run_with_fakes(toolchain_copy, data_root, ["--no-missing-translation-warning"])

        self.assertIsNotNone(cmd)
        self.assertIn("--no-missing-translation-warning", cmd)

    def test_flag_absent_does_not_append_validator_switch(self) -> None:
        toolchain_copy = self._make_toolchain_copy()
        data_root = self._make_valid_data_root()

        cmd = self._run_with_fakes(toolchain_copy, data_root, [])

        self.assertIsNotNone(cmd)
        self.assertNotIn("--no-missing-translation-warning", cmd)


if __name__ == "__main__":
    unittest.main()
