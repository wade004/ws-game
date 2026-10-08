"""提交信息乱码拦截（.githooks/commit-msg + toolchain/check_commit_msg.py，2026-10-09）。

覆盖：纯函数判定（正常中文 / 纯 ASCII / U+FFFD / 非法 UTF-8 / GBK、Latin-1 误解码 / 问号乱码 / 空合并运算符不误伤）、
命令行入口，以及经真实 `git commit` 走完整个钩子链路（`-F` 读 UTF-8 文件通过，GBK 字节与 U+FFFD 被拒绝且不产生提交）。
钩子是 POSIX sh + Python，没有 PowerShell 部分，所以不涉及 PS5.1/7 宿主差异。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from _git_env import init_temp_repo, run_git

REPO_ROOT = Path(__file__).resolve().parents[2]
CHECKER = REPO_ROOT / "toolchain" / "check_commit_msg.py"
HOOK_DIR = REPO_ROOT / ".githooks"

sys.path.insert(0, str(REPO_ROOT / "toolchain"))
import check_commit_msg as ccm  # noqa: E402

GOOD_CN = "新增(门禁): 提交信息乱码拦截；`git commit -F <文件>` 读取 UTF-8\n\n正文第二段，含全角标点：，。！\n\nCo-Authored-By: X <x@example.invalid>\n"


def test_normal_chinese_passes() -> None:
    assert ccm.find_problems(GOOD_CN.encode("utf-8")) == []


def test_ascii_passes() -> None:
    assert ccm.find_problems(b"fix(gate): tighten lock check\n\nbody line\n") == []
    assert ccm.find_problems(b"") == []


def test_null_coalescing_and_code_spans_are_not_flagged() -> None:
    assert ccm.find_problems("修复: 用 `a ?? b` 与 x ?? y 以及 v ??= 3 的写法\n".encode("utf-8")) == []
    assert ccm.find_problems(b"is this fine? yes, really?\n") == []


def test_replacement_char_is_rejected() -> None:
    problems = ccm.find_problems("修复 �� 门禁\n".encode("utf-8"))
    assert problems and any("U+FFFD" in p for p in problems)


def test_invalid_utf8_bytes_are_rejected() -> None:
    gbk_bytes = "新增(门禁)".encode("gbk") + b"\n"
    problems = ccm.find_problems(gbk_bytes)
    assert problems and any("UTF-8" in p for p in problems)
    assert ccm.find_problems(b"ok \xff\xfe bad\n")


@pytest.mark.parametrize("encoding", ["gbk", "latin-1"])
def test_mojibake_from_wrong_decoding_is_rejected(encoding: str) -> None:
    original = "发布 2.9.0" if encoding == "gbk" else "修复门禁"
    garbled = original.encode("utf-8").decode(encoding)
    assert garbled != original
    problems = ccm.find_problems((garbled + "\n").encode("utf-8"))
    assert problems and any("误解码" in p for p in problems), problems


def test_cp1252_mojibake_is_rejected() -> None:
    garbled = "新增工具".encode("utf-8").decode("cp1252")
    assert ccm.find_problems((garbled + "\n").encode("utf-8"))


def test_question_mark_garbling_is_rejected() -> None:
    assert ccm.find_problems(b"??(??): ?????\n")
    assert ccm.find_problems(b"fix: ??? broken\n")


def test_cli_exit_codes(tmp_path: Path) -> None:
    good = tmp_path / "good.txt"
    good.write_bytes(GOOD_CN.encode("utf-8"))
    bad = tmp_path / "bad.txt"
    bad.write_bytes("新增".encode("gbk"))
    ok = subprocess.run([sys.executable, str(CHECKER), str(good)], capture_output=True)
    assert ok.returncode == 0
    refused = subprocess.run([sys.executable, str(CHECKER), str(bad)], capture_output=True)
    assert refused.returncode == 1
    stderr = refused.stderr.decode("utf-8", errors="replace")
    assert "git commit -F" in stderr and "UTF-8" in stderr


# ---------------------------------------------------------------------------
# 经真实 git commit 走完整条钩子链路
# ---------------------------------------------------------------------------


def _repo_with_hook(tmp_path: Path) -> Path:
    # 只装 commit-msg（拷贝钩子与它按相对位置找的检查脚本），不带 pre-commit：这里测的是乱码拦截，不是门禁。
    hooks = tmp_path / "kit" / ".githooks"
    hooks.mkdir(parents=True)
    shutil.copyfile(HOOK_DIR / "commit-msg", hooks / "commit-msg")
    (tmp_path / "kit" / "toolchain").mkdir()
    shutil.copyfile(CHECKER, tmp_path / "kit" / "toolchain" / "check_commit_msg.py")
    repo = init_temp_repo(tmp_path / "r")
    run_git(repo, "config", "core.hooksPath", str(hooks))
    (repo / "f.txt").write_text("x\n", encoding="utf-8")
    run_git(repo, "add", "f.txt")
    return repo


def _commit_count(repo: Path) -> int:
    proc = run_git(repo, "rev-list", "--count", "HEAD", check=False)
    return int(proc.stdout.strip()) if proc.returncode == 0 else 0


def _commit_file(repo: Path, payload: bytes, name: str = "msg.txt") -> subprocess.CompletedProcess[str]:
    msg = repo.parent / name
    msg.write_bytes(payload)
    return run_git(repo, "commit", "-F", str(msg), "--", "f.txt", check=False)


def test_git_commit_dash_F_utf8_file_passes(tmp_path: Path) -> None:
    repo = _repo_with_hook(tmp_path)
    proc = _commit_file(repo, GOOD_CN.encode("utf-8"))
    assert proc.returncode == 0, proc.stderr
    assert _commit_count(repo) == 1
    body = run_git(repo, "log", "-1", "--format=%B").stdout
    assert "提交信息乱码拦截" in body and "�" not in body


def test_git_commit_ascii_passes(tmp_path: Path) -> None:
    repo = _repo_with_hook(tmp_path)
    proc = _commit_file(repo, b"chore: ascii only\n")
    assert proc.returncode == 0, proc.stderr
    assert _commit_count(repo) == 1


def test_git_commit_gbk_bytes_is_refused_and_no_commit_created(tmp_path: Path) -> None:
    repo = _repo_with_hook(tmp_path)
    proc = _commit_file(repo, "新增(门禁): 修复\n".encode("gbk"))
    assert proc.returncode != 0
    assert "git commit -F" in proc.stderr
    assert _commit_count(repo) == 0


def test_git_commit_replacement_char_is_refused(tmp_path: Path) -> None:
    repo = _repo_with_hook(tmp_path)
    proc = _commit_file(repo, "新增 ��(��)\n".encode("utf-8"))
    assert proc.returncode != 0
    assert _commit_count(repo) == 0
