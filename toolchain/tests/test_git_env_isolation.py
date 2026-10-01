"""git 环境隔离与真实仓库配置守卫的测试（缺陷修复 bugfix/test-git-env-leak_20261001，2026-10-01 事故）。

事故回顾与三道防线见 ``_git_env.py`` 模块文档。本文件只验证防线本身，**绝不碰真实仓库**：所有会被
"打穿"的目标都是 ``tmp_path`` 里一次性建出来的沙箱仓库（带一个链接工作树，模拟钩子里
``GIT_DIR=<主检出>/.git/worktrees/<名>`` + ``GIT_INDEX_FILE`` 的真实形状）。

测试 = 复现 + 不变量各至少一条：

- **复现（修复前红、修复后绿）**：
  1. ``test_repro_affected_tests_do_not_write_into_hook_target_repo``：真的在模拟钩子环境（环境变量指向沙箱）
     里把事故中会泄漏的那批用例（``test_version_label``、``test_check_unity_meta``、
     ``test_dist_immutability_guard`` 的四条临时仓库用例、``test_gate_step_runner`` 的 git grep 用例、``test_change_impact`` 的两条
     临时仓库用例）当子 pytest 跑，断言沙箱的配置 / HEAD / 暂存区 / refs 一个字节都没变。参数化两次：
     完整（conftest 剥环境 + 辅助函数）与 ``--noconftest``（只靠辅助函数，证明防线 2 不依赖防线 1）。
  2. ``test_repro_session_strip_protects_naive_git_calls``：一个**故意不带任何防护**的朴素用例
     （``subprocess.run(["git","init"])`` 不传 env）在 conftest 剥环境后落在自己的目录里而不是沙箱。
  3. ``test_repro_config_guard_fails_session_and_reports_diff``：测试会话里真实仓库配置被改，整个会话失败、
     打印差异、不自动还原；没改则不吭声。
- **不变量**：``clean_git_env`` 对任意变量集合都不留 ``GIT_*``（大小写不敏感）且不动其它变量；
  ``git_env`` 隔离系统/全局配置；``init_temp_repo`` 落点不对时拒绝写配置；环境里带着钩子变量时辅助函数仍然
  只作用于目标临时仓库。
"""

from __future__ import annotations

import os
import random
import shutil
import string
import subprocess
import sys
from pathlib import Path

import pytest

import _git_env
from _git_env import ConfigFingerprint, clean_git_env, git_env, init_temp_repo, is_git_env_var, run_git

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parents[1]

# 事故中会泄漏的用例（见 docs 里本次复盘与 toolchain/README 判断记录）：文件整体或 -k 选择。
AFFECTED_TESTS = [
    "toolchain/tests/test_version_label.py",
    "toolchain/tests/test_check_unity_meta.py",
    # 只放真会在临时仓库里起 git 的四条；同文件里 test_build_*_entry_blocked_* 两条直接跑真实仓库的 build.ps1
    # （不起任何临时 git 仓库、不可能泄漏），嵌套再跑一遍只会与并行门禁线争用 StreamingAssets 占位文件
    # （2026-10-01 合并前全量 GetContentWriterIOError），不属于本复现的范围。
    "toolchain/tests/test_dist_immutability_guard.py::test_not_released_version_passes",
    "toolchain/tests/test_dist_immutability_guard.py::test_released_version_blocked_with_guidance",
    "toolchain/tests/test_dist_immutability_guard.py::test_allow_overwrite_bypasses_with_warning",
    "toolchain/tests/test_dist_immutability_guard.py::test_dryrun_suffixed_version_not_confused_with_released_tag",
    "toolchain/tests/test_gate_step_runner.py::test_git_grep_banned_codename_no_hit_on_clean_repo",
    "toolchain/tests/test_gate_step_runner.py::test_git_grep_banned_codename_detects_tracked_file",
    "toolchain/tests/test_gate_step_runner.py::test_git_grep_banned_codename_ignores_untracked_file",
    "toolchain/tests/test_change_impact.py::test_check_dryrun_prints_playmode_category_filter",
    "toolchain/tests/test_change_impact.py::test_module_map_check_flags_uncovered_path_category_and_ghost_rule",
]


class Sandbox:
    """一次性沙箱仓库 + 链接工作树，外加"钩子里 git 注入的环境变量"（全部指向沙箱）。"""

    def __init__(self, root: Path) -> None:
        self.main = root / "sandbox_main"
        init_temp_repo(self.main, branch="main")
        run_git(self.main, "commit", "-q", "--allow-empty", "-m", "init")
        self.worktree = root / "sandbox_wt"
        run_git(self.main, "worktree", "add", "-q", str(self.worktree), "-b", "b1")
        self.git_dir = self.main / ".git" / "worktrees" / "sandbox_wt"
        assert self.git_dir.is_dir(), self.git_dir
        self.hook_env = {
            "GIT_DIR": str(self.git_dir),
            "GIT_INDEX_FILE": str(self.git_dir / "index"),
            "GIT_PREFIX": "",
            "GIT_CONFIG_PARAMETERS": "'core.hooksPath'='nowhere'",
            "GIT_AUTHOR_NAME": "hook",
            "GIT_AUTHOR_EMAIL": "hook@example.invalid",
            "GIT_COMMITTER_NAME": "hook",
            "GIT_COMMITTER_EMAIL": "hook@example.invalid",
            "GIT_EDITOR": ":",
        }

    def snapshot(self) -> dict[str, bytes | None]:
        common = self.main / ".git"
        files = {
            "config": common / "config",
            "HEAD": common / "HEAD",
            "worktree HEAD": self.git_dir / "HEAD",
            "worktree index": self.git_dir / "index",
            "packed-refs": common / "packed-refs",
        }
        snap: dict[str, bytes | None] = {k: (p.read_bytes() if p.exists() else None) for k, p in files.items()}
        for ref in sorted((common / "refs").rglob("*")):
            if ref.is_file():
                snap[f"refs/{ref.relative_to(common / 'refs').as_posix()}"] = ref.read_bytes()
        return snap

    def changed_since(self, before: dict[str, bytes | None]) -> list[str]:
        now = self.snapshot()
        return sorted(k for k in set(before) | set(now) if before.get(k) != now.get(k))

    def describe_config(self) -> str:
        return (self.main / ".git" / "config").read_text(encoding="utf-8", errors="replace")


@pytest.fixture()
def sandbox(tmp_path: Path) -> Sandbox:
    return Sandbox(tmp_path)


def _nested_env(extra: dict[str, str]) -> dict[str, str]:
    env = clean_git_env()
    env["PYTHONUTF8"] = "1"
    env.update(extra)
    return env


def _run_pytest(args: list[str], cwd: Path, env: dict[str, str], timeout: int = 900) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, "-m", "pytest", "-p", "no:cacheprovider", "-q", *args],
        cwd=str(cwd),
        env=env,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )


# ---------------------------------------------------------------------------
# 复现 1：事故里会泄漏的那批真实用例，在钩子环境下不得写进目标仓库
# ---------------------------------------------------------------------------


@pytest.mark.parametrize("extra_args", [[], ["--noconftest"]], ids=["conftest+helpers", "helpers-only"])
def test_repro_affected_tests_do_not_write_into_hook_target_repo(sandbox: Sandbox, extra_args: list[str]) -> None:
    before = sandbox.snapshot()
    proc = _run_pytest([*AFFECTED_TESTS, *extra_args], REPO_ROOT, _nested_env(sandbox.hook_env))
    changed = sandbox.changed_since(before)
    assert not changed, (
        f"钩子环境下子 pytest 改动了沙箱仓库的 {changed}（真实事故里这就是真实仓库）。\n沙箱 config 现为：\n"
        f"{sandbox.describe_config()}\n子 pytest 输出尾部：\n{proc.stdout[-1500:]}"
    )
    if not extra_args:
        # 完整防线：环境也被剥干净，被测脚本自己起的只读 git（如 check_unity_meta 的 git ls-files）也读对仓库，用例应全绿。
        # helpers-only 变体不断言退出码：没有 conftest 时被测脚本自己继承的 GIT_DIR 会让它们读到沙箱，属预期，
        # 这一支只证明"测试自己起的 git 写操作"不再打穿。
        assert proc.returncode == 0, f"子 pytest 失败（退出码 {proc.returncode}）：\n{proc.stdout[-3000:]}\n{proc.stderr[-1500:]}"


# ---------------------------------------------------------------------------
# 复现 2 / 3：用一个迷你"项目"（自带 conftest 副本）验证会话级剥环境与配置守卫
# ---------------------------------------------------------------------------


def _make_mini_project(root: Path, test_source: str) -> Path:
    proj = root / "mini_project"
    tests = proj / "toolchain" / "tests"
    tests.mkdir(parents=True)
    init_temp_repo(proj)  # 迷你项目自己的仓库：conftest 副本的 REPO_ROOT 就是它
    shutil.copy(TESTS_DIR / "conftest.py", tests / "conftest.py")
    shutil.copy(TESTS_DIR / "_git_env.py", tests / "_git_env.py")
    (tests / "test_probe.py").write_text(test_source, encoding="utf-8", newline="\n")
    return proj


_NAIVE_SOURCE = '''
import os
import subprocess
from pathlib import Path


def test_naive_git_calls_land_in_own_directory(tmp_path):
    # 故意不带任何防护：不传 env，也不用辅助函数（修复前这就是事故里的写法）。
    assert not any(k.upper().startswith("GIT_") for k in os.environ), sorted(os.environ)
    subprocess.run(["git", "init", "-q"], cwd=tmp_path, check=True)
    subprocess.run(["git", "config", "user.email", "naive@example.invalid"], cwd=tmp_path, check=True)
    cfg = (Path(tmp_path) / ".git" / "config").read_text(encoding="utf-8")
    assert "naive@example.invalid" in cfg
'''


def test_repro_session_strip_protects_naive_git_calls(tmp_path: Path, sandbox: Sandbox) -> None:
    proj = _make_mini_project(tmp_path, _NAIVE_SOURCE)
    before = sandbox.snapshot()
    proc = _run_pytest(["toolchain/tests/test_probe.py"], proj, _nested_env(sandbox.hook_env), timeout=300)
    changed = sandbox.changed_since(before)
    assert not changed, f"朴素 git 调用打穿了沙箱：{changed}\n{sandbox.describe_config()}\n{proc.stdout[-1500:]}"
    assert proc.returncode == 0, proc.stdout[-3000:] + proc.stderr[-1500:]


_MUTATE_SOURCE = '''
from pathlib import Path

from _git_env import run_git


def test_mutates_real_repo_config():
    repo_root = Path(__file__).resolve().parents[2]
    run_git(repo_root, "config", "--local", "ws.leak", "1")
'''

_QUIET_SOURCE = '''
def test_does_nothing():
    assert True
'''


def test_repro_config_guard_fails_session_and_reports_diff(tmp_path: Path) -> None:
    proj = _make_mini_project(tmp_path / "mutating", _MUTATE_SOURCE)
    config = proj / ".git" / "config"
    before = ConfigFingerprint(config)
    proc = _run_pytest(["toolchain/tests/test_probe.py"], proj, _nested_env({}), timeout=300)
    out = proc.stdout + proc.stderr
    assert "1 passed" in out, "用例本身应通过（失败来自会话级守卫，不是用例断言）：\n" + out
    assert proc.returncode != 0, "真实仓库配置被改，整个会话必须失败：\n" + out
    assert "git 配置守卫" in out, out
    assert "SHA-256 开始" in out and "SHA-256 结束" in out, out
    assert "+\tleak = 1" in out, "守卫要打印出差异行：\n" + out
    # 只报告、不自动还原
    assert before.diff_against_current() is not None
    assert "leak = 1" in config.read_text(encoding="utf-8")


def test_config_guard_is_silent_when_config_unchanged(tmp_path: Path) -> None:
    proj = _make_mini_project(tmp_path / "quiet", _QUIET_SOURCE)
    proc = _run_pytest(["toolchain/tests/test_probe.py"], proj, _nested_env({}), timeout=300)
    out = proc.stdout + proc.stderr
    assert proc.returncode == 0, out
    assert "git 配置守卫" not in out


# ---------------------------------------------------------------------------
# 不变量
# ---------------------------------------------------------------------------


@pytest.mark.parametrize("seed", range(20))
def test_invariant_clean_git_env_removes_exactly_git_vars(seed: int) -> None:
    rng = random.Random(seed)
    names: dict[str, str] = {}
    for _ in range(rng.randint(5, 25)):
        suffix = "".join(rng.choice(string.ascii_uppercase + "_") for _ in range(rng.randint(1, 12)))
        prefix = rng.choice(["GIT_", "git_", "Git_", "", "X", "MY_GIT_", "GITX", "GI_"])
        names[prefix + suffix] = str(rng.random())
    cleaned = clean_git_env(names)
    assert not [k for k in cleaned if k.upper().startswith("GIT_")]
    expected = {k: v for k, v in names.items() if not k.upper().startswith("GIT_")}
    assert cleaned == expected  # 其它变量一个不少、一个不改
    assert is_git_env_var("git_dir") and not is_git_env_var("MYGIT_DIR")


def test_invariant_known_hazard_vars_are_all_stripped() -> None:
    dirty = {name: "x" for name in _git_env.KNOWN_HAZARD_VARS}
    dirty["PATH"] = "p"
    assert clean_git_env(dirty) == {"PATH": "p"}


def test_invariant_git_env_isolates_system_and_global_config(tmp_path: Path) -> None:
    env = git_env()
    assert env["GIT_CONFIG_NOSYSTEM"] == "1"
    global_cfg = Path(env["GIT_CONFIG_GLOBAL"])
    assert global_cfg.is_file() and global_cfg.read_bytes() == b""
    repo = init_temp_repo(tmp_path / "repo", identity=False)
    # 用户全局/系统配置里的任何内容在这里都读不到：--show-origin 里不会出现 file:...gitconfig
    listing = run_git(repo, "config", "--list", "--show-origin").stdout
    assert ".gitconfig" not in listing and "etc/gitconfig" not in listing.replace("\\", "/"), listing
    # 即使调用方的 env_extra 想给一个 GIT_* 变量也只对这次调用生效，不污染进程环境
    run_git(repo, "status", "--short", env_extra={"GIT_TRACE": "0"})
    assert "GIT_TRACE" not in os.environ


def test_invariant_helpers_ignore_ambient_hook_env(tmp_path: Path, sandbox: Sandbox, monkeypatch: pytest.MonkeyPatch) -> None:
    """进程环境里真的带着钩子变量时（本进程内直接模拟），辅助函数建仓库/写配置/提交/打标签只作用于目标临时仓库。"""
    for key, value in sandbox.hook_env.items():
        monkeypatch.setenv(key, value)
    before = sandbox.snapshot()
    repo = init_temp_repo(tmp_path / "target", branch="main")
    (repo / "f.txt").write_text("x\n", encoding="utf-8")
    run_git(repo, "add", "f.txt")
    run_git(repo, "commit", "-q", "-m", "init")
    run_git(repo, "tag", "v1")
    assert sandbox.changed_since(before) == []
    assert run_git(repo, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip() == "main"
    assert run_git(repo, "config", "user.email").stdout.strip() == "t@example.invalid"
    assert run_git(repo, "tag", "-l").stdout.split() == ["v1"]


def test_invariant_init_temp_repo_refuses_when_repo_lands_elsewhere(
    tmp_path: Path, sandbox: Sandbox, monkeypatch: pytest.MonkeyPatch
) -> None:
    """万一有人绕开环境清理（这里用把 git_env 换成脏环境来模拟），落点校验要拒绝，且不往那个仓库写用户配置。"""
    run_git(sandbox.main, "config", "user.email", "orig@example.invalid")
    dirty = {**clean_git_env(), **sandbox.hook_env}
    monkeypatch.setattr(_git_env, "git_env", lambda extra=None: dict(dirty))
    with pytest.raises(AssertionError, match="没有落在目标目录"):
        init_temp_repo(tmp_path / "victim")
    monkeypatch.undo()
    assert run_git(sandbox.main, "config", "user.email").stdout.strip() == "orig@example.invalid"


def test_invariant_config_fingerprint_detects_any_change(tmp_path: Path) -> None:
    cfg = tmp_path / "config"
    cfg.write_bytes(b"[core]\n\tbare = false\n")
    fp = ConfigFingerprint(cfg)
    assert fp.diff_against_current() is None
    cfg.write_bytes(b"[core]\n\tbare = true\n")
    report = fp.diff_against_current()
    assert report is not None and "-\tbare = false" in report and "+\tbare = true" in report
    cfg.write_bytes(b"[core]\n\tbare = false\n")
    assert fp.diff_against_current() is None  # 改回去就一致
    cfg.unlink()
    assert fp.diff_against_current() is not None  # 删除也算改动
