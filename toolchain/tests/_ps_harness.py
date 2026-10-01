"""toolchain/tests 里"跑一段 PowerShell 判定、拿 JSON 结果回来"的共用小工具（测试覆盖第四批，2026-10-01）。

门禁脚本里的纯函数（``toolchain/_gate_*.ps1``、``_release_regression_guard.ps1``）只能通过 PowerShell
子进程执行；各测试文件原先各自拼 ``-Command`` 字符串并处理编码，这里收敛成一处：

- 脚本写成带 UTF-8 BOM 的 ``.ps1`` 文件再用 ``-File`` 执行（Windows PowerShell 5.1 对无 BOM 脚本按
  ANSI 代码页读，中文字面量会乱码；也避开 ``-Command`` 多行 + 引号转义的坑）。
- 结果由脚本写成 JSON 文件（``$ResultPath``），Python 再读回——不解析控制台输出，避免编码问题。
- 宿主选择走 ``shutil.which``（``powershell.exe`` 优先、``pwsh`` 其次），因此会被
  ``conftest.py`` 的 ``WS_GAME_PS_HOST`` 矩阵开关重定向（5.1 / 7），这批用例自动进入宿主矩阵。
- ``clean_powershell_env`` 沿用 ``_ps_subprocess_env.py`` 的 PSModulePath 处理。

本机没有 PowerShell 宿主时 ``find_powershell`` 调 ``pytest.skip``（与仓库其余 .ps1 相关测试一致）。
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

import pytest

from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
TOOLCHAIN_DIR = REPO_ROOT / "toolchain"


def find_powershell() -> str:
    if sys.platform != "win32":
        pytest.skip("门禁脚本只在 Windows 下运行")
    for candidate in ("powershell.exe", "powershell", "pwsh.exe", "pwsh"):
        path = shutil.which(candidate)
        if path:
            return path
    pytest.skip("找不到 powershell.exe 或 pwsh")
    raise AssertionError("unreachable")


def ps_quote(value: object) -> str:
    """PowerShell 单引号字符串字面量。"""
    return "'" + str(value).replace("'", "''") + "'"


def git_env() -> dict[str, str]:
    """去掉 GIT_* 环境变量（例如在 git 钩子里跑 pytest 时 GIT_INDEX_FILE 会把临时仓库的 git 命令
    指到外面的仓库）。"""
    return {k: v for k, v in os.environ.items() if not k.startswith("GIT_")}


def run_ps_script(
    tmp_path: Path,
    body: str,
    *,
    env_extra: dict[str, str] | None = None,
    timeout: int = 180,
    name: str = "driver",
) -> subprocess.CompletedProcess[str]:
    """把 body 写成带 BOM 的 .ps1 并用选定宿主执行。body 里可用 ``$ResultPath``。"""
    exe = find_powershell()
    result_path = tmp_path / f"{name}_result.json"
    script = (
        '$ErrorActionPreference = "Stop"\n'
        f"$ResultPath = {ps_quote(result_path)}\n"
        + body
    )
    script_path = tmp_path / f"{name}.ps1"
    script_path.write_bytes(b"\xef\xbb\xbf" + script.encode("utf-8"))
    env = clean_powershell_env(exe)
    if env is None:
        env = dict(os.environ)
    if env_extra:
        env = {**env, **env_extra}
    return subprocess.run(
        [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script_path)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        env=env,
    )


def run_ps_json(
    tmp_path: Path,
    body: str,
    *,
    env_extra: dict[str, str] | None = None,
    timeout: int = 180,
    name: str = "driver",
) -> Any:
    """执行 body（须自己把结果 ConvertTo-Json 写到 ``$ResultPath``），返回解析后的 JSON。"""
    proc = run_ps_script(tmp_path, body, env_extra=env_extra, timeout=timeout, name=name)
    assert proc.returncode == 0, f"stdout={proc.stdout}\nstderr={proc.stderr}"
    result_path = tmp_path / f"{name}_result.json"
    assert result_path.exists(), f"脚本没有写出结果文件。stdout={proc.stdout}\nstderr={proc.stderr}"
    return json.loads(result_path.read_text(encoding="utf-8-sig"))
