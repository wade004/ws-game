#!/usr/bin/env python3
"""版本标签推导与分支名规范检查（ADR-0127，`AGENTS.md` §1b）。

版本标签是**自动推导**的展示用标识，不写进 `VERSION`：

* 在 ``feature/<名>`` 或 ``bugfix/<名>`` 分支上：``<VERSION 内容>_<名>``，例
  ``1.92.0_gate-timing-autolog_20261001``。
* 在 ``main`` 上：``<VERSION 内容>_release``（发布后 ``VERSION`` 升级，标签随之变成新版本号加 ``_release``）。
* 其它分支（``release/X.Y.x`` 等）：``<VERSION 内容>_<分支名，/ 换成 ->``，例 ``1.92.0_release-1.90.x``。
* 游离 HEAD：``<VERSION 内容>_detached-<短 sha>``。

不变量：任何分支名下，标签都以 ``VERSION`` 内容开头且其后紧跟 ``_``。

分支名规范（只对 ``feature/``、``bugfix/`` 前缀的分支判定，`AGENTS.md` §1b）：
``feature|bugfix/<小写字母数字与连字符>_<八位年月日>``。不合规范时标签仍照常推导，
``--check-branch-name`` 退出码 1。

判断记录（为什么标签不进 ``VERSION``、不进包版本）：npm 与引擎包管理器只接受语义化版本，下划线后缀会让发布失败；
改成 ``-xxx`` 预发布后缀又会让版本排序低于正式版，同样错误。所以包版本、标签、发布包名、变更日志标题、
公开面兼容探针基线继续用纯 ``X.Y.Z``，用户规定的格式只作为构建时写入程序集信息版本的标签与门禁打印。

用法::

    python toolchain/version_label.py                    # 打印标签
    python toolchain/version_label.py --check-branch-name  # 分支名规范检查，不合规退出码 1
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

TOOLCHAIN_DIR = Path(__file__).resolve().parent
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from _console import ensure_utf8_stdio  # noqa: E402

REPO_ROOT = TOOLCHAIN_DIR.parent

MAIN_BRANCH = "main"
JUDGED_PREFIXES = ("feature/", "bugfix/")
BRANCH_NAME_PATTERN = re.compile(r"^(?:feature|bugfix)/[a-z0-9]+(?:-[a-z0-9]+)*_(\d{8})$")


def compute_label(version: str, branch: str | None, short_sha: str = "") -> str:
    """由 VERSION 内容与分支名推导版本标签。``branch`` 为 ``None`` 表示游离 HEAD。"""
    version = version.strip()
    if branch is None:
        return f"{version}_detached-{short_sha or 'unknown'}"
    if branch == MAIN_BRANCH:
        return f"{version}_release"
    for prefix in JUDGED_PREFIXES:
        if branch.startswith(prefix):
            suffix = branch[len(prefix):]
            return f"{version}_{suffix.replace('/', '-')}"
    return f"{version}_{branch.replace('/', '-')}"


def check_branch_name(branch: str | None) -> tuple[bool, str]:
    """分支名规范判定。只判 feature/、bugfix/ 前缀；其它分支与游离 HEAD 不判定、视为通过。"""
    if branch is None or not branch.startswith(JUDGED_PREFIXES):
        return True, f"分支名规范：{_show(branch)} 不是 feature/ 或 bugfix/ 分支，不判定"
    match = BRANCH_NAME_PATTERN.match(branch)
    if not match:
        return False, (
            f"分支名不合规范：{_show(branch)}。要求 feature/<需求名称>_<年月日> 或 bugfix/<缺陷名称>_<年月日>，"
            "名称只用小写英文字母、数字、连字符（不含中文、大写、空格），年月日为八位数字，"
            "例 feature/targeted-gate_20261001（AGENTS.md §1b）"
        )
    try:
        datetime.strptime(match.group(1), "%Y%m%d")
    except ValueError:
        return False, f"分支名不合规范：{_show(branch)} 末尾的八位年月日 {match.group(1)} 不是有效日期（AGENTS.md §1b）"
    return True, f"分支名规范：{branch} 符合 AGENTS.md §1b"


def _show(branch: str | None) -> str:
    if branch is None:
        return "（游离 HEAD）"
    if branch.isascii():
        return branch
    # 非 ASCII 分支名：同时给出转义形式，避免控制台代码页不一致时看不出具体是哪些字符
    return f"{branch}（转义：{branch.encode('unicode_escape').decode('ascii')}）"


def read_version(repo_root: Path) -> str:
    return (repo_root / "VERSION").read_text(encoding="utf-8-sig").strip()


def _git(repo_root: Path, *args: str) -> tuple[int, str]:
    proc = subprocess.run(
        ["git", "-C", str(repo_root), *args],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
    )
    return proc.returncode, proc.stdout.strip()


def current_branch(repo_root: Path) -> tuple[str | None, str]:
    """返回 (分支名或 None=游离 HEAD, 短 sha)。"""
    code, branch = _git(repo_root, "symbolic-ref", "--short", "-q", "HEAD")
    _, sha = _git(repo_root, "rev-parse", "--short", "HEAD")
    if code != 0 or not branch:
        return None, sha
    return branch, sha


def main(argv: list[str] | None = None) -> int:
    ensure_utf8_stdio()
    parser = argparse.ArgumentParser(description="版本标签推导与分支名规范检查（ADR-0127）")
    parser.add_argument("--repo-root", default=str(REPO_ROOT), help="仓库根（默认本脚本所在仓库）")
    parser.add_argument("--check-branch-name", action="store_true", help="检查分支名规范，不合规退出码 1")
    parser.add_argument("--branch", default=None, help="显式指定分支名（默认读当前分支；测试与演示用）")
    parser.add_argument("--version", default=None, help="显式指定 VERSION 内容（默认读仓库根 VERSION）")
    args = parser.parse_args(argv)

    repo_root = Path(args.repo_root).resolve()
    branch, sha = (args.branch, "") if args.branch is not None else current_branch(repo_root)

    if args.check_branch_name:
        ok, message = check_branch_name(branch)
        print(("PASS " if ok else "FAIL ") + message)
        return 0 if ok else 1

    version = args.version if args.version is not None else read_version(repo_root)
    print(compute_label(version, branch, sha))
    return 0


if __name__ == "__main__":
    sys.exit(main())
