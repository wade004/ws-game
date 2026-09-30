"""toolchain/tests 下需要真实运行 ``toolchain/simrunner``、``toolchain/validator`` 两个 C# 命令行
工具的用例共用的定位/构建/调用辅助（测试覆盖梳理 I-3、I-4，2026-10-01）。

判断记录：

1. 可执行文件来源。先读环境变量 ``WS_GAME_CLI_ARTIFACTS``（一个 ``dotnet build ... --artifacts-path``
   产物根，内含 ``bin/SimRunner/release/SimRunner.dll`` 与 ``bin/Validator/release/Validator.dll``；
   门禁 ``_gate_line_heavy.ps1`` 的 ``$ArtifactsPath`` 就是这种布局），有则直接用，省掉构建；没有则在
   本 pytest 进程内第一次需要时按 AGENTS.md 第 4 节（"dotnet 命令一律带 --artifacts-path"）把两个工程
   一次性构建到一个系统临时目录，进程退出时清理。这样 ``python -m pytest`` 单跑也是自包含的，不依赖
   "别的步骤先构建过"。
2. 本机没有 ``dotnet`` 时 skip 并说明原因（与仓库其余依赖外部可执行文件的测试一致）；有 dotnet
   却构建失败则是真失败（``pytest.fail``），不 skip——构建坏了正是这些用例要暴露的问题。
3. 子进程一律 ``cwd=仓库根``：两个工具都把相对路径按当前工作目录解析，门禁也是在仓库根运行
   （``Push-Location $RepoRoot``）；标准输出/错误按 UTF-8 解码（两个工具都显式把控制台设为 UTF-8）。
"""

from __future__ import annotations

import atexit
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]

_ENV_ARTIFACTS = "WS_GAME_CLI_ARTIFACTS"

_PROJECTS = {
    "SimRunner": Path("toolchain") / "simrunner" / "SimRunner.csproj",
    "Validator": Path("toolchain") / "validator" / "Validator.csproj",
}

_built_artifacts_root: Path | None = None


def _dll_under(artifacts_root: Path, name: str) -> Path:
    return artifacts_root / "bin" / name / "release" / f"{name}.dll"


def _dotnet_or_skip() -> str:
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        pytest.skip("本机未找到 dotnet 可执行文件，跳过（与仓库其余依赖外部可执行文件的测试一致）")
    return dotnet


def get_cli_dll(name: str) -> Path:
    """返回已构建的 ``SimRunner.dll`` / ``Validator.dll`` 路径（必要时先构建）。"""
    global _built_artifacts_root
    assert name in _PROJECTS, name

    env_root = os.environ.get(_ENV_ARTIFACTS)
    if env_root:
        candidate = _dll_under(Path(env_root), name)
        if candidate.is_file():
            return candidate

    dotnet = _dotnet_or_skip()
    if _built_artifacts_root is None:
        root = Path(tempfile.mkdtemp(prefix="ws_game_cli_build_"))
        atexit.register(shutil.rmtree, str(root), True)
        _built_artifacts_root = root
    dll = _dll_under(_built_artifacts_root, name)
    if not dll.is_file():
        proc = subprocess.run(
            [dotnet, "build", str(REPO_ROOT / _PROJECTS[name]), "-c", "Release",
             "--artifacts-path", str(_built_artifacts_root), "-nologo", "-v", "q"],
            cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=900,
        )
        if proc.returncode != 0 or not dll.is_file():
            pytest.fail(f"构建 {name} 失败（exit={proc.returncode}）：\n{proc.stdout[-3000:]}\n{proc.stderr[-2000:]}")
    return dll


def run_cli(dll: Path, args: list[str], timeout: int = 300) -> subprocess.CompletedProcess[str]:
    """``dotnet <dll> <args...>``，cwd=仓库根，UTF-8 解码。"""
    return subprocess.run(
        [_dotnet_or_skip(), str(dll), *args],
        cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout,
    )
