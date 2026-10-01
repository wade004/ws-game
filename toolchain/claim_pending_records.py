#!/usr/bin/env python3
"""领走主检出里的待领耗时记录，并入当前 feature/bugfix 分支的 ``timing/``（AGENTS.md §1b、§1c）。

背景：``main`` 禁止直接提交，而 ``check.ps1`` 在 ``main`` 上（合并后全量）也要记耗时。为了不弄脏主检出
（脏工作树挡住 ``build.ps1 -Release`` 与下一次 ``git merge --ff-only``），门禁在 ``main``/游离 HEAD 上把耗时
写到被 .gitignore 覆盖的待领目录 ``timing/_pending/<年月日>_main_<时分秒>.jsonl``（一次运行一个文件）。
本脚本在**一条 feature/bugfix 分支的工作树里**运行，把主检出里待领目录的全部文件按稳定命名
``timing/<年月日>_main.jsonl`` 追加合并进当前工作树，随后删除主检出里被领走的待领文件。

行为（判断记录）：

- 合并规则：同一天的多份待领文件按文件名里的时分秒（再按文件名）顺序拼接，追加到目标文件末尾；丢弃与目标文件
  已有行、或与本次已并入行**逐字节相同**的行（所以"并入成功但删除失败"后重跑不会重复）。行尾统一成 LF、UTF-8 无 BOM。
- 只动主检出的 ``timing/_pending/``：每个待领文件领走前核对它没有被 git 跟踪（跟踪的文件一律拒绝、不删），
  不跟随符号链接，不碰主检出任何其它文件。目标文件先写完再删待领文件；写失败时一个待领文件也不删。
- 幂等：领走即删除，同一批文件重复运行第二次领 0 个文件、0 行，目标文件不变。
- 拒绝：当前检出是 ``main``（或游离 HEAD）、或与 ``--from`` 是同一个工作树、或与 ``--from`` 不属于同一个
  仓库、或 ``--from`` 的分支不是 ``main``，退出码 1 并说明原因。
- 文件名不符合 ``<8 位日期>_<名>_<6 位时分秒>.jsonl`` 的待领文件原样留下，打印警告，不领走。
- 不 ``git add``、不提交：并入的文件由调用方按显式 pathspec 提交。

用法（在分支的工作树里）::

    python toolchain/claim_pending_records.py                     # 主检出取 git worktree list 里检出 main 的那个
    python toolchain/claim_pending_records.py --from D:/ws-game   # 显式指定主检出
    python toolchain/claim_pending_records.py --dry-run           # 只打印会领走什么，不写不删

返回码：0 成功（含没有待领文件）；1 被拒绝或失败；2 参数错。
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402
from version_label import MAIN_BRANCH, current_branch  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[1]
PENDING_REL = Path("timing") / "_pending"
PENDING_NAME = re.compile(r"^(?P<day>\d{8})_(?P<tag>[A-Za-z0-9.-]+)_(?P<clock>\d{6})\.jsonl$")
UTF8_BOM = b"\xef\xbb\xbf"


class ClaimError(Exception):
    """拒绝或失败，消息直接给用户看。"""


def _git(repo: Path, *args: str) -> tuple[int, str]:
    proc = subprocess.run(
        ["git", "-C", str(repo), *args],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
    )
    return proc.returncode, proc.stdout.strip()


def find_main_checkout(repo: Path) -> Path:
    """``git worktree list --porcelain`` 里检出 main 的那个工作树。"""
    code, out = _git(repo, "worktree", "list", "--porcelain")
    if code != 0:
        raise ClaimError(f"git worktree list 失败（{repo} 不是 git 工作树？）")
    path: str | None = None
    for line in out.splitlines() + [""]:
        if line.startswith("worktree "):
            path = line[len("worktree "):]
        elif line.strip() == f"branch refs/heads/{MAIN_BRANCH}" and path:
            return Path(path)
        elif not line.strip():
            path = None
    raise ClaimError(f"git worktree list 里没有检出 {MAIN_BRANCH} 的工作树，请用 --from 指定主检出")


def _same_path(a: Path, b: Path) -> bool:
    try:
        return a.resolve() == b.resolve()
    except OSError:
        return False


def _common_dir(repo: Path) -> Path | None:
    code, out = _git(repo, "rev-parse", "--path-format=absolute", "--git-common-dir")
    return Path(out).resolve() if code == 0 and out else None


def _split_lines(raw: bytes) -> list[bytes]:
    if raw.startswith(UTF8_BOM):
        raw = raw[len(UTF8_BOM):]
    out: list[bytes] = []
    for line in raw.split(b"\n"):
        line = line.rstrip(b"\r")
        if line.strip():
            out.append(line)
    return out


def _tracked_pending(main_checkout: Path) -> set[str]:
    code, out = _git(main_checkout, "ls-files", "-z", "--", PENDING_REL.as_posix())
    if code != 0:
        raise ClaimError(f"无法读取主检出的 git 跟踪状态：{main_checkout}")
    return {p for p in out.split("\0") if p}


def claim(target_repo: Path, main_checkout: Path, *, dry_run: bool = False) -> dict:
    """领取。返回 ``{"files": n, "lines": m, "duplicates": k, "targets": [...], "skipped": [...]}``。"""
    branch, _ = current_branch(target_repo)
    if branch is None:
        raise ClaimError("当前是游离 HEAD，领取结果无处提交；请在 feature/bugfix 分支的工作树里运行")
    if branch == MAIN_BRANCH:
        raise ClaimError(
            f"拒绝：当前检出是 {MAIN_BRANCH}。{MAIN_BRANCH} 禁止直接提交（AGENTS.md §1b），"
            "请在 feature/bugfix 分支的工作树里运行本脚本"
        )
    if _same_path(target_repo, main_checkout):
        raise ClaimError(f"拒绝：当前工作树与主检出是同一个目录：{main_checkout}")
    ours, theirs = _common_dir(target_repo), _common_dir(main_checkout)
    if ours is None or theirs is None or ours != theirs:
        raise ClaimError(f"拒绝：{main_checkout} 与当前工作树不属于同一个仓库")
    main_branch, _ = current_branch(main_checkout)
    if main_branch != MAIN_BRANCH:
        raise ClaimError(f"拒绝：--from 指向的 {main_checkout} 检出的是 {main_branch}，不是 {MAIN_BRANCH}")

    result: dict = {"files": 0, "lines": 0, "duplicates": 0, "targets": [], "skipped": []}
    pending_dir = main_checkout / PENDING_REL
    if not pending_dir.is_dir():
        return result

    tracked = _tracked_pending(main_checkout)
    candidates: list[tuple[str, str, str, Path]] = []
    for path in sorted(pending_dir.iterdir(), key=lambda p: p.name):
        if path.is_symlink() or not path.is_file():
            continue
        rel = (PENDING_REL / path.name).as_posix()
        match = PENDING_NAME.match(path.name)
        if rel in tracked:
            result["skipped"].append((path.name, "已被 git 跟踪，不领、不删"))
        elif not match:
            result["skipped"].append((path.name, "文件名不是 <日期>_<名>_<时分秒>.jsonl，原样留下"))
        else:
            candidates.append((match["day"], match["clock"], path.name, path))
    candidates.sort(key=lambda c: (c[0], c[1], c[2]))

    by_day: dict[str, list[Path]] = {}
    for day, _clock, _name, path in candidates:
        by_day.setdefault(day, []).append(path)

    # 先把每个目标文件的新内容全部算好、写完，最后才删待领文件。
    to_delete: list[Path] = []
    for day, paths in by_day.items():
        target = target_repo / "timing" / f"{day}_{MAIN_BRANCH}.jsonl"
        existing = _split_lines(target.read_bytes()) if target.exists() else []
        seen = set(existing)
        added: list[bytes] = []
        for path in paths:
            for line in _split_lines(path.read_bytes()):
                if line in seen:
                    result["duplicates"] += 1
                    continue
                seen.add(line)
                added.append(line)
            to_delete.append(path)
        result["lines"] += len(added)
        result["targets"].append(target.relative_to(target_repo).as_posix())
        if added and not dry_run:
            target.parent.mkdir(parents=True, exist_ok=True)
            prefix = b""
            if target.exists() and target.stat().st_size > 0:
                with target.open("rb") as fh:
                    fh.seek(-1, 2)
                    if fh.read(1) != b"\n":
                        prefix = b"\n"
            with target.open("ab") as fh:
                fh.write(prefix + b"\n".join(added) + b"\n")
    result["files"] = len(to_delete)
    if not dry_run:
        failed: list[str] = []
        for path in to_delete:
            try:
                path.unlink()
            except OSError as exc:
                failed.append(f"{path.name}（{exc}）")
        if failed:
            raise ClaimError("目标文件已写入，但这些待领文件没删掉（重跑不会重复并入）：" + "；".join(failed))
    return result


def main(argv: list[str] | None = None) -> int:
    ensure_utf8_stdio()
    parser = argparse.ArgumentParser(description="领走主检出 timing/_pending/ 下的待领耗时记录，并入当前分支")
    parser.add_argument("--from", dest="source", default=None, help="主检出路径（默认 git worktree list 里检出 main 的工作树）")
    parser.add_argument("--repo-root", default=str(REPO_ROOT), help="目标工作树（默认本脚本所在仓库；测试用）")
    parser.add_argument("--dry-run", action="store_true", help="只打印会领走什么，不写不删")
    args = parser.parse_args(argv)

    target = Path(args.repo_root).resolve()
    try:
        main_checkout = Path(args.source).resolve() if args.source else find_main_checkout(target)
        result = claim(target, main_checkout, dry_run=args.dry_run)
    except ClaimError as exc:
        print(f"领取失败：{exc}", file=sys.stderr)
        return 1
    for name, why in result["skipped"]:
        print(f"警告：待领文件 {name} 未处理：{why}", file=sys.stderr)
    verb = "将领走" if args.dry_run else "领走"
    print(
        f"{verb} {result['files']} 个文件、{result['lines']} 行"
        f"（丢弃逐字节重复 {result['duplicates']} 行）；主检出：{main_checkout}"
    )
    for rel in result["targets"]:
        print(f"  目标：{rel}")
    if result["targets"] and not args.dry_run:
        print("提示：按显式 pathspec 提交上面的目标文件，并补写 REGRESSION_LOG.md 的合并后行（AGENTS.md §1b）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
