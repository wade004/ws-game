"""toolchain/tests 里"起 git 子进程"的唯一入口：干净环境 + 临时仓库辅助 + 真实仓库配置指纹
（缺陷修复 bugfix/test-git-env-leak_20261001，2026-10-01 事故）。

事故：预提交钩子里跑完整 pytest 时，git 给钩子进程注入了 ``GIT_DIR``（链接工作树时指向
``<主检出>/.git/worktrees/<名>``）、``GIT_INDEX_FILE``、``GIT_PREFIX``，以及 ``git -c`` 带下来的
``GIT_CONFIG_PARAMETERS``（另有 ``GIT_AUTHOR_*``/``GIT_COMMITTER_*``）。测试在临时目录里
``git init`` / ``git config`` / ``git add`` 时没有剥掉这些变量，命令实际作用在**真实仓库**上：
``git init`` 在 ``GIT_DIR`` 已设而 ``GIT_WORK_TREE`` 未设时把仓库当裸库，往共享的 ``.git/config`` 写
``core.bare=true``；随后的 ``git config user.*``/``commit.gpgsign`` 也写进同一个共享文件；``git add`` 写进
真实暂存区、``git tag`` 写进真实标签。之后所有工作树的 git 命令都报
``fatal: this operation must be run in a work tree``。

三道防线（缺一不可，互相兜底）：

1. ``conftest.py`` 会话开始时 :func:`strip_git_env_from_process` 把 ``os.environ`` 里全部 ``GIT_*`` 变量
   移除，所有子进程（含 PowerShell 脚本里再起的 git）默认继承干净环境。确有用例要这些变量时，必须在该用例
   里自己显式设置（``monkeypatch.setenv`` / 子进程 ``env=``）。
2. 本模块的 :func:`git_env` / :func:`run_git` / :func:`init_temp_repo`：测试里给临时仓库起 git 子进程一律
   走它们——显式传干净环境，再设 ``GIT_CONFIG_NOSYSTEM=1`` 与指向一个空文件的 ``GIT_CONFIG_GLOBAL``，
   临时仓库需要的用户名/邮箱写进**它自己的**仓库配置，绝不写全局或真实仓库；``init_temp_repo`` 建完先核对
   仓库确实落在目标目录下才继续写配置。
3. ``conftest.py`` 的不变量守卫：会话开始记下真实仓库共享配置（``git rev-parse --git-common-dir`` 下的
   ``config``）的 SHA-256 与全文，会话结束再比一次，不一致让整个会话失败并打印差异（只报告，不还原）。

为什么剥掉全部 ``GIT_*`` 而不只是一份定位变量清单：清单要随 git 版本补全（``GIT_DIR``、``GIT_WORK_TREE``、
``GIT_INDEX_FILE``、``GIT_OBJECT_DIRECTORY``、``GIT_ALTERNATE_OBJECT_DIRECTORIES``、``GIT_COMMON_DIR``、
``GIT_PREFIX``、``GIT_CEILING_DIRECTORIES``、``GIT_NAMESPACE``、``GIT_CONFIG*``、``GIT_AUTHOR_*``、
``GIT_COMMITTER_*``……），漏一个就是下一次事故；测试不依赖任何 ``GIT_*`` 变量，整个前缀剥掉最稳。
"""

from __future__ import annotations

import atexit
import difflib
import hashlib
import os
import subprocess
import tempfile
from pathlib import Path

GIT_ENV_PREFIX = "GIT_"

#: 事故里实际出现、且会改变 git 行为的变量（文档用途；实现按前缀整体剥离，不依赖这份清单是否完整）。
KNOWN_HAZARD_VARS = (
    "GIT_DIR",
    "GIT_WORK_TREE",
    "GIT_INDEX_FILE",
    "GIT_OBJECT_DIRECTORY",
    "GIT_ALTERNATE_OBJECT_DIRECTORIES",
    "GIT_COMMON_DIR",
    "GIT_PREFIX",
    "GIT_CEILING_DIRECTORIES",
    "GIT_NAMESPACE",
    "GIT_CONFIG_PARAMETERS",
    "GIT_CONFIG_COUNT",
)


def is_git_env_var(name: str) -> bool:
    return name.upper().startswith(GIT_ENV_PREFIX)


def clean_git_env(base: dict[str, str] | None = None) -> dict[str, str]:
    """返回 ``base``（默认当前进程环境）去掉全部 ``GIT_*`` 变量后的副本。"""
    source = os.environ if base is None else base
    return {k: v for k, v in source.items() if not is_git_env_var(k)}


def strip_git_env_from_process() -> list[str]:
    """把当前进程 ``os.environ`` 里全部 ``GIT_*`` 变量移除，返回被移除的变量名（排序）。"""
    removed = sorted(k for k in list(os.environ) if is_git_env_var(k))
    for name in removed:
        os.environ.pop(name, None)
    return removed


_EMPTY_GLOBAL_CONFIG: Path | None = None


def _empty_global_config() -> Path:
    """进程内共用的一个空文件，充当 ``GIT_CONFIG_GLOBAL``（退出时删除）。"""
    global _EMPTY_GLOBAL_CONFIG
    if _EMPTY_GLOBAL_CONFIG is None or not _EMPTY_GLOBAL_CONFIG.exists():
        fd, name = tempfile.mkstemp(prefix="ws_game_empty_gitconfig_", suffix=".cfg")
        os.close(fd)
        path = Path(name)
        atexit.register(lambda p=path: p.unlink(missing_ok=True))
        _EMPTY_GLOBAL_CONFIG = path
    return _EMPTY_GLOBAL_CONFIG


def git_env(extra: dict[str, str] | None = None) -> dict[str, str]:
    """给测试里起的 git 子进程用的环境：无 ``GIT_*`` 继承、无系统/全局 git 配置。

    ``extra`` 里的 ``GIT_*`` 变量是调用方显式要求的，原样保留（例如专门模拟钩子环境的用例）。
    """
    env = clean_git_env()
    env["GIT_CONFIG_NOSYSTEM"] = "1"
    env["GIT_CONFIG_GLOBAL"] = str(_empty_global_config())
    if extra:
        env.update(extra)
    return env


def run_git(
    cwd: Path | str,
    *args: str,
    check: bool = True,
    env_extra: dict[str, str] | None = None,
    timeout: int = 120,
) -> subprocess.CompletedProcess[str]:
    """在 ``cwd`` 里以干净环境运行 ``git <args>``，文本模式（UTF-8）。``check`` 为真时失败即抛 AssertionError。"""
    proc = subprocess.run(
        ["git", *args],
        cwd=str(cwd),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        env=git_env(env_extra),
        timeout=timeout,
    )
    if check and proc.returncode != 0:
        raise AssertionError(f"git {' '.join(args)} 失败（cwd={cwd}，退出码 {proc.returncode}）：{proc.stderr}")
    return proc


def init_temp_repo(
    path: Path,
    *,
    branch: str | None = None,
    identity: bool = True,
) -> Path:
    """在 ``path``（须是测试自己的临时目录）下建一个独立 git 仓库。

    - 干净环境；``identity`` 为真时把用户名/邮箱/``commit.gpgsign=false`` 写进**该仓库自己的**配置。
    - 建完先核对 ``git rev-parse --absolute-git-dir`` 确实落在 ``path`` 之下，否则抛错且不写任何配置
      （即使有人绕开了环境清理，也不会把配置写进别的仓库）。
    """
    path.mkdir(parents=True, exist_ok=True)
    init_args = ["init", "-q"]
    if branch:
        init_args += ["-b", branch]
    run_git(path, *init_args)
    git_dir = Path(run_git(path, "rev-parse", "--absolute-git-dir").stdout.strip()).resolve()
    root = path.resolve()
    if root != git_dir and root not in git_dir.parents:
        raise AssertionError(f"git init 没有落在目标目录 {root} 下（实际 git 目录 {git_dir}），中止，未写任何配置")
    if identity:
        run_git(path, "config", "user.email", "t@example.invalid")
        run_git(path, "config", "user.name", "t")
        run_git(path, "config", "commit.gpgsign", "false")
    return path


# ---------------------------------------------------------------------------
# 真实仓库共享配置指纹（不变量守卫）
# ---------------------------------------------------------------------------


def common_config_path(repo_root: Path) -> Path | None:
    """``repo_root`` 所在仓库的共享配置文件路径（``git rev-parse --git-common-dir`` 下的 ``config``）；
    不是 git 仓库（例如发布 zip 解出的目录）或没有 git 时返回 None。用干净环境查，不受外部 ``GIT_DIR`` 影响。"""
    try:
        proc = subprocess.run(
            ["git", "rev-parse", "--git-common-dir"],
            cwd=str(repo_root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=git_env(),
            timeout=60,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if proc.returncode != 0:
        return None
    common = Path(proc.stdout.strip())
    if not common.is_absolute():
        common = Path(repo_root) / common
    return (common / "config").resolve()


class ConfigFingerprint:
    """某个 git 配置文件在某一刻的字节快照（SHA-256 + 全文，缺文件记为 None）。"""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.data: bytes | None = path.read_bytes() if path.exists() else None

    @property
    def sha256(self) -> str:
        return "<缺失>" if self.data is None else hashlib.sha256(self.data).hexdigest()

    def diff_against_current(self) -> str | None:
        """当前文件与快照一致返回 None，否则返回含哈希与统一 diff 的说明文本。"""
        now = ConfigFingerprint(self.path)
        if now.data == self.data:
            return None
        before = (self.data or b"").decode("utf-8", errors="replace").splitlines()
        after = (now.data or b"").decode("utf-8", errors="replace").splitlines()
        body = "\n".join(difflib.unified_diff(before, after, "会话开始", "会话结束", lineterm=""))
        return f"{self.path}\n  SHA-256 开始 {self.sha256}\n  SHA-256 结束 {now.sha256}\n{body}"
