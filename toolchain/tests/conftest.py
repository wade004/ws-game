"""toolchain/tests 的 pytest 公共配置：PowerShell 宿主矩阵开关（测试覆盖第四批，复盘 I-5 缩减版）。

背景：toolchain 下一批 pytest 用例要启动 PowerShell 子进程去跑 `.ps1` 脚本，各测试文件各自用
`shutil.which("powershell"/"powershell.exe"/"pwsh"/…)` 选宿主，惯例是 Windows PowerShell 5.1 优先、
pwsh（PowerShell 7）其次——于是在装了两个宿主的机器上，这批用例永远只在 5.1 下跑，PowerShell 7
下的行为差异（编码、参数绑定、`ConvertTo-Json` 等）没有任何门禁看到。11 章 §8 要求的环境矩阵因此缺
一半。

做法：环境变量 `WS_GAME_PS_HOST` 控制本次 pytest 进程里所有 `shutil.which` 对 PowerShell 宿主名的
解析结果，不改任何一个测试文件的宿主选择代码：

- 未设置（默认，日常 pytest 与门禁主 pytest 步骤）：完全不介入，行为与以前一致。
- `5.1`：`powershell`/`powershell.exe`/`pwsh`/`pwsh.exe` 一律解析为 Windows PowerShell 5.1。
- `7`：四个名字一律解析为 PowerShell 7（`pwsh`）。

会话开始时 `pytest_sessionstart` 会真的启动所选宿主查 `$PSVersionTable.PSVersion.Major`，与请求的
宿主不符（比如要求 7 却解析到 5.1，或本机根本没有 pwsh）就直接 `pytest.exit` 报错，不让矩阵步骤
悄悄退化成"又在 5.1 下跑了一遍"或因找不到宿主而整批 skip 后假通过。

使用者：`toolchain/_gate_line_heavy.ps1` 的"PowerShell 脚本类 pytest 双宿主矩阵"步骤（只在全量门禁里
跑，`-Quick`/`-SkipUnity` 下 SKIP）。

第二块职责：git 环境隔离与真实仓库配置不变量守卫（缺陷修复 bugfix/test-git-env-leak_20261001，
2026-10-01 事故，原理与三道防线见 `_git_env.py` 模块文档）：

- 本模块被导入时（早于收集阶段）就把 `os.environ` 里全部 `GIT_*` 变量移除：在 git 钩子里跑 pytest 时
  `GIT_DIR`/`GIT_INDEX_FILE`/`GIT_PREFIX`/`GIT_CONFIG_PARAMETERS` 会被注入，测试在临时目录里起的 git 命令
  不剥掉就会作用到真实仓库。确有用例需要这些变量时，必须在该用例里显式设置。
- 会话开始记下真实仓库共享配置（`git rev-parse --git-common-dir` 下的 `config`）的 SHA-256 与全文，会话
  结束再比一次；不一致让整个会话失败并打印差异，只报告、不自动还原。比较前先去掉 `[branch ...]`/`[remote ...]`
  小节（别的会话正当地 `git branch --set-upstream-to`、`git push -u`、`git remote add` 只会改这些小节，
  不算污染；`core`/`user`/`commit` 等其余小节的任何改动照常报错，见 `_git_env.normalize_config_bytes`）。
"""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
from pathlib import Path

import pytest

from _git_env import ConfigFingerprint, common_config_path, strip_git_env_from_process

# 必须早于任何收集期/模块级的 git 调用：先剥环境，再记配置指纹（指纹用干净环境查仓库位置）。
_STRIPPED_GIT_VARS = strip_git_env_from_process()

# �Ž���ռ�����루toolchain/_gate_lock.ps1�������׼������ check.ps1 / build.ps1 �ӽ��̵�������������ʵ��
# D:\wt\_exclusive_gate.lock Ӱ�죨��ĻỰ����ʱ�����޹������ᱻ�ܾ���������Ҳ���ü̳�����Ž��ĳ��������ݡ�
# �����������test_gate_lock.py���Լ����ӽ����� WS_GATE_LOCK_PATH��
os.environ["WS_GATE_LOCK_PATH"] = str(Path(tempfile.gettempdir()) / f"ws_game_pytest_{os.getpid()}.lock")
os.environ.pop("WS_GATE_LOCK_HOLDER", None)
_REPO_ROOT = Path(__file__).resolve().parents[2]
_config_guard: ConfigFingerprint | None = None

_ENV_NAME = "WS_GAME_PS_HOST"
_POWERSHELL_NAMES = {"powershell", "pwsh"}

_real_which = shutil.which


def _normalize(command: object) -> str | None:
    if not isinstance(command, str):
        return None
    lowered = command.lower()
    if lowered.endswith(".exe"):
        lowered = lowered[: -len(".exe")]
    return lowered if lowered in _POWERSHELL_NAMES else None


def _requested_host() -> str | None:
    value = os.environ.get(_ENV_NAME, "").strip()
    if value == "":
        return None
    if value not in ("5.1", "7"):
        raise pytest.UsageError(f"{_ENV_NAME} 只接受 5.1 或 7，实际为 {value!r}")
    return value


def _which_with_host_override(cmd, *args, **kwargs):
    host = _requested_host()
    if host is not None and _normalize(cmd) is not None:
        target = "pwsh" if host == "7" else "powershell"
        return _real_which(target, *args, **kwargs)
    return _real_which(cmd, *args, **kwargs)


shutil.which = _which_with_host_override


def pytest_sessionstart(session: pytest.Session) -> None:
    global _config_guard
    strip_git_env_from_process()
    config_path = common_config_path(_REPO_ROOT)
    _config_guard = ConfigFingerprint(config_path) if config_path is not None else None
    host = _requested_host()
    if host is None:
        return
    exe = _real_which("pwsh" if host == "7" else "powershell")
    if exe is None:
        pytest.exit(f"{_ENV_NAME}={host}：本机找不到对应的 PowerShell 宿主，矩阵步骤无法进行", returncode=3)
    completed = subprocess.run(
        [exe, "-NoProfile", "-NonInteractive", "-Command", "$PSVersionTable.PSVersion.Major"],
        capture_output=True,
        text=True,
        timeout=60,
    )
    reported = completed.stdout.strip()
    expected_ok = reported == "5" if host == "5.1" else (reported.isdigit() and int(reported) >= 7)
    if completed.returncode != 0 or not expected_ok:
        pytest.exit(
            f"{_ENV_NAME}={host}：解析到的宿主 {exe} 实际报告主版本 {reported!r}"
            f"（退出码 {completed.returncode}），与请求不符",
            returncode=3,
        )
    print(f"\n[{_ENV_NAME}={host}] PowerShell 宿主 {exe}，主版本 {reported}")


def pytest_sessionfinish(session: pytest.Session, exitstatus: int) -> None:
    """不变量守卫：真实仓库共享 git 配置在整个测试会话里不得变化；变了就让会话失败（只报告，不还原）。"""
    if _config_guard is None:
        return
    report = _config_guard.diff_against_current()
    if report is None:
        return
    rule = "=" * 72
    banner = (
        "\n" + rule
        + "\n[git 配置守卫] 测试会话期间真实仓库的共享 git 配置被改动了——有用例把 git 命令作用到了真实仓库。"
        + "\n本守卫只报告、不自动还原；先查清是哪条用例（测试里起 git 子进程必须走 _git_env 辅助函数），再手工处理。\n"
        + report
        + "\n" + rule + "\n"
    )
    reporter = session.config.pluginmanager.get_plugin("terminalreporter")
    if reporter is not None:
        reporter.write(banner, red=True)
    else:
        print(banner)
    session.exitstatus = pytest.ExitCode.TESTS_FAILED
