"""真实 dist 产物回归用例共用的"取最新已存在发布包"辅助。

判断记录：

1. 为什么不按 ``VERSION`` 找包。发布流程（``build.ps1 -Release``）先把 ``VERSION`` 写回为目标版本、
   再跑发布前门禁，此时 ``dist/ws-game-<目标版本>.zip|.lock`` 还没生成；按 ``VERSION`` 找包的用例会
   整批 skip，撞上门禁的"pytest skip 上限 0"（1.92.0 首次发布在此阻塞）。改为在 ``dist/`` 里按语义化
   版本排序取当前已存在、zip 与 lock 成对齐全的最大版本，不依赖 ``VERSION``。
2. ``dist/`` 里一个成对的包都没有（干净 checkout、从未发布过）才 skip，属环境性。
3. 只认严格 ``ws-game-<主>.<次>.<修订>.zip`` 与同名 ``.lock``：``-samples.zip``、``-dryrun`` 目录等
   不参与。
4. 链接工作树里没有 ``dist/``（``dist/`` 只存在于主检出，.gitignore）时回落到主检出的 ``dist/``
   （:func:`resolve_dist_dir` / :func:`locate_dist_file`），与 ``toolchain/abi_probe.ps1`` 的基线回落同一口径：
   工作树里不需要、也不得为了让这类用例不 skip 而复制整个 ``dist``（2026-10-02，dist 瘦身与基线回落）。
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Optional, Tuple

from _git_env import run_git

_ZIP_RE = re.compile(r"^ws-game-(\d+)\.(\d+)\.(\d+)\.zip$")


def find_latest_dist_package(dist_dir: Path) -> Optional[Tuple[str, Path, Path]]:
    """返回 ``(版本号, zip 路径, lock 路径)``；没有任何成对包时返回 None。"""
    if not dist_dir.is_dir():
        return None
    best: Optional[Tuple[Tuple[int, int, int], Path, Path]] = None
    for zip_path in dist_dir.iterdir():
        m = _ZIP_RE.match(zip_path.name)
        if not m or not zip_path.is_file():
            continue
        lock_path = zip_path.with_suffix(".lock")
        if not lock_path.is_file():
            continue
        key = (int(m.group(1)), int(m.group(2)), int(m.group(3)))
        if best is None or key > best[0]:
            best = (key, zip_path, lock_path)
    if best is None:
        return None
    return ".".join(str(n) for n in best[0]), best[1], best[2]


def main_checkout_root(repo_root: Path) -> Optional[Path]:
    """``repo_root`` 是链接工作树时返回主检出根目录，否则（主检出、非 git 目录、git 不可用）返回 None。"""
    try:
        git_dir = run_git(repo_root, "rev-parse", "--path-format=absolute", "--git-dir", check=False)
        common = run_git(repo_root, "rev-parse", "--path-format=absolute", "--git-common-dir", check=False)
    except (OSError, AssertionError):
        return None
    if git_dir.returncode != 0 or common.returncode != 0:
        return None
    git_dir_path = Path(git_dir.stdout.strip()).resolve()
    common_path = Path(common.stdout.strip()).resolve()
    if git_dir_path == common_path:
        return None
    return common_path.parent


def resolve_dist_dir(repo_root: Path) -> Path:
    """本工作树 ``dist/`` 里有任何 ``ws-game-*.zip`` 就用它；没有且主检出的 ``dist/`` 有，就用主检出的；
    都没有返回本工作树的 ``dist/``（调用方按"没有产物"处理）。"""
    local = Path(repo_root) / "dist"
    if local.is_dir() and any(local.glob("ws-game-*.zip")):
        return local
    main_root = main_checkout_root(Path(repo_root))
    if main_root is not None:
        main_dist = main_root / "dist"
        if main_dist.is_dir():
            return main_dist
    return local


def locate_dist_file(repo_root: Path, name: str) -> Path:
    """``dist/<name>`` 的路径：本工作树有就用本工作树的，否则回落到主检出的（存在才回落），都没有返回本工作树路径。"""
    local = Path(repo_root) / "dist" / name
    if local.is_file():
        return local
    main_root = main_checkout_root(Path(repo_root))
    if main_root is not None:
        candidate = main_root / "dist" / name
        if candidate.is_file():
            return candidate
    return local
