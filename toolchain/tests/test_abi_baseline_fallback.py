"""ABI 基线发行包路径解析的回落（``toolchain/_abi_baseline_resolve.ps1``，2026-10-02，dist 瘦身与基线回落）。

背景：``dist/`` 是 .gitignore 的本机构建缓存，只存在于主检出；并行会话在链接工作树里跑门禁时，
``abi_probe.ps1`` 只看 ``<工作树>\\dist`` 找不到基线，有人于是把整个 ``dist``（十几 GB）复制进每个工作树。

不变量（真实 git 仓库 + 真实链接工作树，不 mock）：

1. 工作树里没有 ``dist``、主工作树有基线 zip -> 用主工作树的（``FromMainWorktree``），路径正是主工作树那份；
2. 工作树自己有基线 zip -> 用自己的，不回落（显式/本地优先）；
3. 两处都没有 -> ``Found`` 为假、路径仍是本工作树的 ``dist\\ws-game-<ver>.zip``（调用方沿用既有"缺基线"提示）；
4. 本目录就是主检出（不是链接工作树）时，即使缺基线也不会"回落到自己"，行为与改动前一致；
5. Python 侧同口径的 ``_latest_dist.resolve_dist_dir`` / ``locate_dist_file``（真实 dist 产物回归用例用）。

运行：``python -m pytest toolchain/tests/test_abi_baseline_fallback.py -q``（无 PowerShell 宿主时跳过）。
"""

from __future__ import annotations

from pathlib import Path

from _git_env import init_temp_repo, run_git
from _latest_dist import locate_dist_file, main_checkout_root, resolve_dist_dir
from _ps_harness import TOOLCHAIN_DIR, ps_quote, run_ps_json

RESOLVER = TOOLCHAIN_DIR / "_abi_baseline_resolve.ps1"
VERSION = "9.9.9"
ZIP_NAME = f"ws-game-{VERSION}.zip"


def _make_main_and_worktree(tmp_path: Path) -> tuple[Path, Path]:
    main = init_temp_repo(tmp_path / "main_checkout")
    (main / "README.txt").write_text("x", encoding="utf-8")
    run_git(main, "add", "--", "README.txt")
    run_git(main, "commit", "-q", "-m", "init")
    worktree = tmp_path / "linked_wt"
    run_git(main, "worktree", "add", "-q", str(worktree), "-b", "slice/test")
    return main, worktree


def _resolve_all(tmp_path: Path, roots: dict[str, Path]) -> dict:
    lines = [f". {ps_quote(RESOLVER)}", "$out = @{}"]
    for key, root in roots.items():
        lines.append(
            f"$r = Resolve-AbiBaselineZip -RepoRoot {ps_quote(root)} -BaselineVersion {ps_quote(VERSION)}\n"
            f"$out[{ps_quote(key)}] = @{{ Path = [string]$r.Path; FromMainWorktree = [bool]$r.FromMainWorktree; Found = [bool]$r.Found }}"
        )
    lines.append("$out | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ResultPath -Encoding UTF8")
    return run_ps_json(tmp_path, "\n".join(lines) + "\n", name="resolve")


def _same(a: str, b: Path) -> bool:
    return Path(a).resolve() == Path(b).resolve()


def test_worktree_without_dist_falls_back_to_main_worktree_dist(tmp_path: Path) -> None:
    main, worktree = _make_main_and_worktree(tmp_path)
    (main / "dist").mkdir()
    main_zip = main / "dist" / ZIP_NAME
    main_zip.write_bytes(b"baseline")
    assert not (worktree / "dist").exists()

    res = _resolve_all(tmp_path, {"worktree": worktree})["worktree"]
    assert res["Found"] is True and res["FromMainWorktree"] is True
    assert _same(res["Path"], main_zip), res


def test_local_baseline_wins_and_missing_everywhere_keeps_local_path(tmp_path: Path) -> None:
    main, worktree = _make_main_and_worktree(tmp_path)
    (main / "dist").mkdir()
    (main / "dist" / ZIP_NAME).write_bytes(b"main")
    (worktree / "dist").mkdir()
    local_zip = worktree / "dist" / ZIP_NAME
    local_zip.write_bytes(b"local")

    # 本地有 -> 用本地的，不回落
    res = _resolve_all(tmp_path, {"worktree": worktree})["worktree"]
    assert res["Found"] is True and res["FromMainWorktree"] is False
    assert _same(res["Path"], local_zip), res

    # 两处都没有 -> Found 假，路径仍是本工作树的 dist；主检出本身缺基线也不"回落到自己"
    local_zip.unlink()
    (main / "dist" / ZIP_NAME).unlink()
    both = _resolve_all(tmp_path, {"worktree": worktree, "main": main})
    for key, root in (("worktree", worktree), ("main", main)):
        assert both[key]["Found"] is False and both[key]["FromMainWorktree"] is False, both[key]
        assert _same(both[key]["Path"], root / "dist" / ZIP_NAME), both[key]


def test_python_side_dist_fallback_helpers_use_main_checkout_only_for_linked_worktree(tmp_path: Path) -> None:
    main, worktree = _make_main_and_worktree(tmp_path)
    (main / "dist").mkdir()
    (main / "dist" / ZIP_NAME).write_bytes(b"baseline")
    (main / "dist" / "ws-game-1.2.3.zip").write_bytes(b"z")

    assert main_checkout_root(main) is None
    assert main_checkout_root(worktree) is not None and main_checkout_root(worktree).resolve() == main.resolve()
    assert _same(str(resolve_dist_dir(worktree)), main / "dist")
    assert _same(str(locate_dist_file(worktree, ZIP_NAME)), main / "dist" / ZIP_NAME)
    # 主检出自己：就是自己的 dist
    assert _same(str(resolve_dist_dir(main)), main / "dist")
    # 工作树自己有 zip 时优先本地
    (worktree / "dist").mkdir()
    (worktree / "dist" / "ws-game-0.0.1.zip").write_bytes(b"l")
    assert _same(str(resolve_dist_dir(worktree)), worktree / "dist")
    # 本地没有而主检出有的具体文件仍可回落定位
    assert _same(str(locate_dist_file(worktree, ZIP_NAME)), main / "dist" / ZIP_NAME)
    # 都没有 -> 返回本地路径（不存在），调用方按缺产物处理
    missing = locate_dist_file(worktree, "ws-game-0.0.0.zip")
    assert _same(str(missing), worktree / "dist" / "ws-game-0.0.0.zip") and not missing.exists()
