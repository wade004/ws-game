"""``toolchain/validate_data.py`` 第二道校验优先使用预编译 ``validator/bin/Validator.dll`` 的
调度逻辑回归测试（消费方反馈 E1 根治，2026-09-10，见
``architecture/落地计划/消费方反馈-2026-09-10-编辑器.md`` E1）。

背景：``validate_data.py`` 此前一律用 ``dotnet run --project toolchain/validator`` 现场编译
第二道校验。若 ``toolchain/`` 目录（源码仓库内、或消费方解压出的 dist zip/UPM 包）落在消费方仓库
工作树内，MSBuild 沿项目目录向上查找 ``Directory.Build.props`` 会继承到消费方自己的设置（例如
``TreatWarningsAsErrors=true``），把本工具 XML 文档注释里原本无害的告警（如未指定重载的 cref
歧义 CS0419）提升为编译错误，消费方连校验都跑不起来——复现见
``toolchain/tests/test_get_framework_path_boundary.py`` 同批之外的手工复现记录（打包
``dist/ws-game-<ver>.zip`` 解压到一个上层带 ``Directory.Build.props``（``TreatWarningsAsErrors=
true``）的目录里，跑 ``validate_data.py`` 直接失败）。

根治：``build.ps1 -Dist``/``-Release`` 现在会把 Validator 项目连同其依赖的六个核心 DLL 预编译进
``toolchain/validator/bin/``（见该脚本"5.057 消费方反馈 E1 根治"判断记录），``validate_data.py``
存在该目录下的 ``Validator.dll`` 时优先直接 ``dotnet <Validator.dll 路径>`` 执行——完全不触发
MSBuild/``Directory.Build.props`` 解析；找不到时退回原有的 ``dotnet run --project`` 现场编译路径
（源码仓库内自测场景，或消费方精简掉了 ``bin/`` 目录时的防御性兜底，那条路径由 dist 打包时随附的
空 ``Directory.Build.props`` 挡住消费方设置继承，见 build.ps1 判断记录，不在本文件重复验证）。

本文件复用同目录 ``test_validate_data_validator_project_path.py`` 的"拷贝成独立模块 + monkeypatch
``shutil.which``/``subprocess.run``"手法，不依赖真实安装 dotnet，只断言实际传给
``subprocess.run`` 的命令行在两种场景（``bin/Validator.dll`` 存在 / 不存在）下分别是什么。

运行：``python -m pytest toolchain/tests/test_validate_data_precompiled_validator.py -q``
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
    """把 validate_data.py 加载成一个独立模块实例，``__file__`` 指向 ``module_dir`` 下的拷贝。

    做法与 ``test_validate_data_validator_project_path.py`` 一致：不能直接 ``import
    validate_data``，那样 ``__file__`` 会指向仓库内真实路径，测不出"validator/bin/Validator.dll
    是否存在"这个由调用方拷贝布局决定的场景。
    """
    module_path = module_dir / "validate_data.py"
    spec = importlib.util.spec_from_file_location("validate_data_copy_under_test_precompiled", module_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PrecompiledValidatorDispatchTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.tmp_root = Path(self._tmp.name)

    def _make_toolchain_copy(self) -> Path:
        """模拟一份独立部署的 toolchain/ 目录：validate_data.py + _console.py + validator/
        子目录（不含真实 Validator 项目源码，本测试不依赖真实编译/运行）。
        """
        toolchain_copy = self.tmp_root / "toolchain_copy"
        toolchain_copy.mkdir(parents=True)
        shutil.copy(TOOLCHAIN_DIR / "validate_data.py", toolchain_copy / "validate_data.py")
        shutil.copy(TOOLCHAIN_DIR / "_console.py", toolchain_copy / "_console.py")
        (toolchain_copy / "validator").mkdir()
        return toolchain_copy

    def _make_valid_data_root(self) -> Path:
        """一个能通过第一道骨架检查（0 errors）的最小数据根。"""
        data_root = self.tmp_root / "data_root"
        data_root.mkdir()
        (data_root / "test.thing.json").write_text(
            json.dumps({"table": "test.thing", "schema_version": 1, "rows": []}),
            encoding="utf-8",
        )
        return data_root

    def _run_with_fakes(self, toolchain_copy: Path, data_root: Path) -> tuple[int, list]:
        module = _load_validate_data_copy(toolchain_copy)
        captured_cmd: dict = {}

        def fake_which(name):
            self.assertEqual(name, "dotnet")
            return "/fake/dotnet"

        def fake_run(cmd, cwd=None):
            captured_cmd["cmd"] = cmd
            captured_cmd["cwd"] = cwd
            return SimpleNamespace(returncode=0)

        # 判断记录：不能写 module.shutil.which = .../module.subprocess.run = ...——shutil/
        # subprocess 是解释器内所有模块共享的同一个全局模块对象，那样写会把真实的
        # shutil.which/subprocess.run 在整个进程范围内永久替换掉，泄漏给按字母序更晚收集执行、
        # 真正需要跑真实 PowerShell 子进程的用例（test_get_framework_*/test_powershell_* 等），
        # 曾经实测复现过整个 toolchain/tests 套件级联失败。改为只重新绑定本次动态加载出的这个
        # 模块实例自己的 shutil/subprocess 名字，指向只带所需属性的轻量命名空间对象。
        module.shutil = SimpleNamespace(which=fake_which)
        module.subprocess = SimpleNamespace(run=fake_run)

        exit_code = module.main(["--data-root", str(data_root)])
        return exit_code, captured_cmd.get("cmd")

    def test_prefers_precompiled_validator_dll_when_present(self) -> None:
        """存在 validator/bin/Validator.dll 时，直接 `dotnet <Validator.dll>`，不经过
        `dotnet run --project`/MSBuild——这是根治点本身：这条路径完全不解析任何
        Directory.Build.props，消费方构建设置（如 TreatWarningsAsErrors）无从生效。
        """
        toolchain_copy = self._make_toolchain_copy()
        validator_bin_dir = toolchain_copy / "validator" / "bin"
        validator_bin_dir.mkdir()
        fake_dll_path = validator_bin_dir / "Validator.dll"
        fake_dll_path.write_bytes(b"fake-dll-content-not-a-real-assembly")
        data_root = self._make_valid_data_root()

        exit_code, cmd = self._run_with_fakes(toolchain_copy, data_root)

        self.assertEqual(exit_code, 0)
        self.assertIsNotNone(cmd)
        self.assertEqual(cmd[0], "/fake/dotnet")
        self.assertEqual(
            Path(cmd[1]).resolve(),
            fake_dll_path.resolve(),
            "第二个参数应直接是 Validator.dll 的路径，而不是 'run'/'--project' 这类 dotnet-run 专属参数",
        )
        self.assertNotIn("run", cmd)
        self.assertNotIn("--project", cmd)
        self.assertNotIn("--", cmd)
        self.assertIn("--data-root", cmd)

    def test_falls_back_to_dotnet_run_when_precompiled_dll_absent(self) -> None:
        """不存在 validator/bin/Validator.dll 时（例如源码仓库自测、或消费方精简掉了 bin/ 目录），
        退回原有的 `dotnet run --project` 现场编译路径——防御性兜底，行为与改动前完全一致。
        """
        toolchain_copy = self._make_toolchain_copy()
        # 故意不创建 validator/bin/，模拟预编译产物缺失的场景。
        data_root = self._make_valid_data_root()

        exit_code, cmd = self._run_with_fakes(toolchain_copy, data_root)

        self.assertEqual(exit_code, 0)
        self.assertIsNotNone(cmd)
        self.assertEqual(cmd[0], "/fake/dotnet")
        self.assertIn("run", cmd)
        self.assertIn("--project", cmd)
        project_arg = cmd[cmd.index("--project") + 1]
        self.assertEqual(Path(project_arg).resolve(), (toolchain_copy / "validator").resolve())
        self.assertIn("--", cmd)
        self.assertIn("--data-root", cmd)


if __name__ == "__main__":
    unittest.main()
