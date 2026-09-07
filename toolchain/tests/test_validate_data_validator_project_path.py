"""``toolchain/validate_data.py`` 第二道校验的 ``validator_project`` 路径解析回归测试。

背景（P02 根治，审计 architecture/落地计划/audit-7e63d66-20260907/project-review.md P02）：
此前用 ``find_repo_root() / "toolchain" / "validator"`` 拼路径，``find_repo_root()`` 是
"本文件所在目录的上一级"。这在源码仓库内正确（``repo_root`` 就是仓库根），但在
UPM ``com.gamefoundation.toolchain`` 包内不成立——``validate_data.py`` 随包落在
``Tools~/validate_data.py``，"本文件所在目录的上一级"是包根（不含 ``toolchain/`` 这一层
名字），拼出的 ``<包根>/toolchain/validator`` 并不存在（真实目录是 ``<包根>/Tools~/validator``），
``dotnet run --project`` 直接报找不到项目文件。

修法改为 ``Path(__file__).resolve().parent / "validator"``——``validator/`` 目录在源码仓库
（``toolchain/validate_data.py`` 与 ``toolchain/validator/``）与 UPM 包（``Tools~/validate_data.py``
与 ``Tools~/validator/``）两种布局下都与本文件直接同级，不再依赖"repo_root/toolchain"这一假设
仓库布局的拼接方式。

本测试把 ``validate_data.py``/``_console.py`` 复制进一个刻意不叫 ``toolchain`` 的临时目录
（模拟 ``Tools~`` 这种"目录名与仓库布局不同名"的场景），monkeypatch 掉 ``shutil.which``/
``subprocess.run``（不依赖真实安装 dotnet），断言实际传给 ``dotnet run --project`` 的路径
是"本文件所在目录下的 validator 子目录"，而不是"仓库根/toolchain/validator"这种在该场景下
根本不存在的路径。
"""

from __future__ import annotations

import importlib.util
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]


def _load_validate_data_copy(module_dir: Path):
    """把 validate_data.py 加载成一个独立模块实例，``__file__`` 指向 ``module_dir`` 下的拷贝。

    不能直接 ``import validate_data``——那样 ``__file__`` 会指向仓库内真实路径，测不出"部署到
    别的目录名下"这个场景。用 ``importlib`` 从拷贝出来的文件路径显式加载一个独立模块对象。
    """
    module_path = module_dir / "validate_data.py"
    spec = importlib.util.spec_from_file_location("validate_data_copy_under_test", module_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ValidatorProjectPathTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.tmp_root = Path(self._tmp.name)

    def _make_fake_package_layout(self) -> Path:
        """模拟 UPM 包内布局：<包根>/Tools~/validate_data.py + <包根>/Tools~/validator/。

        目录名故意不叫 "toolchain"，且 <包根> 下没有任何 "toolchain" 子目录——若实现退回到
        旧的 "repo_root/toolchain/validator" 拼法，这里会拼出一个不存在、且与实际 validator/
        目录完全不同的路径，测试能感知到区别（即使不依赖真实 dotnet 执行）。
        """
        package_root = self.tmp_root / "com.gamefoundation.toolchain-fake"
        tools_tilde = package_root / "Tools~"
        tools_tilde.mkdir(parents=True)
        shutil.copy(TOOLCHAIN_DIR / "validate_data.py", tools_tilde / "validate_data.py")
        shutil.copy(TOOLCHAIN_DIR / "_console.py", tools_tilde / "_console.py")
        (tools_tilde / "validator").mkdir()
        return tools_tilde

    def _make_valid_data_root(self) -> Path:
        """一个能通过第一道骨架检查（0 errors）的最小数据根。"""
        data_root = self.tmp_root / "data_root"
        data_root.mkdir()
        (data_root / "test.thing.json").write_text(
            json.dumps({"table": "test.thing", "schema_version": 1, "rows": []}),
            encoding="utf-8",
        )
        return data_root

    def test_validator_project_is_sibling_of_script_not_repo_root_toolchain(self) -> None:
        tools_tilde = self._make_fake_package_layout()
        data_root = self._make_valid_data_root()
        module = _load_validate_data_copy(tools_tilde)

        captured_cmd = {}

        def fake_which(name):
            self.assertEqual(name, "dotnet")
            return "/fake/dotnet"

        def fake_run(cmd, cwd=None):
            captured_cmd["cmd"] = cmd
            captured_cmd["cwd"] = cwd
            return SimpleNamespace(returncode=0)

        module.shutil.which = fake_which
        module.subprocess.run = fake_run

        exit_code = module.main(["--data-root", str(data_root)])

        self.assertEqual(exit_code, 0)
        self.assertIn("cmd", captured_cmd)
        cmd = captured_cmd["cmd"]
        self.assertIn("--project", cmd)
        project_arg = cmd[cmd.index("--project") + 1]
        project_path = Path(project_arg)

        expected_validator_dir = tools_tilde / "validator"
        self.assertEqual(
            project_path.resolve(),
            expected_validator_dir.resolve(),
            "validator_project 应该是 validate_data.py 拷贝所在目录（此处是 Tools~）下的 "
            "validator 子目录，不是按 repo_root/toolchain/validator 拼出的、在本场景下根本"
            "不存在的路径",
        )

        wrong_legacy_path = tools_tilde.parent / "toolchain" / "validator"
        self.assertNotEqual(
            project_path.resolve() if project_path.exists() or True else None,
            wrong_legacy_path,
        )
        self.assertFalse(
            wrong_legacy_path.exists(),
            "本测试场景故意不创建 <包根>/toolchain/validator，用来确认修复后的实现不会拼出这个"
            "路径（若拼出了这个路径但目录不存在，上面对 project_path 的相等性断言已经能感知到差异；"
            "这里额外确认该路径确实不存在，排除“凑巧目标相同”的可能）",
        )


if __name__ == "__main__":
    unittest.main()
