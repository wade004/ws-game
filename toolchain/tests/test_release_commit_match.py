"""release.yml "Check for existing release assets" 步骤的提交号比对与 zip 条目查找回归（2026-10-02）。

缺陷（两个不同根因，都让 Release 工作流在这一步 exit 1，四个 .tgz 因而一直没补上 GitHub Release）：

1. v1.93.0（运行 36964875468）："Existing ws-game-1.93.0.zip was built from commit b7ac4dc3, but tag
   v1.93.0 points at b7ac4dc"。MANIFEST/lock 的 ``git_commit`` 是发布机完整仓库 ``git rev-parse --short``
   的 8 位，CI 浅克隆的 ``--short`` 只给 7 位，字符串全等比较误判。修法：比对完整 sha 的前缀
   （``Test-WsGameCommitMatch``）。
2. v1.92.0（运行 36811895996）："has no MANIFEST.txt at expected path"。1.92.0 发布包的条目分隔符是正斜杠
   （``ws-game-1.92.0/MANIFEST.txt``），1.93.0 是反斜杠；workflow 用反斜杠字面量精确匹配。修法：条目查找
   两侧归一分隔符（``Get-WsGameZipEntry``）。

用例：
- ``test_commit_match_*``：逐例直接调 ``Test-WsGameCommitMatch``，含"记录 8 位、当前 7 位缩写同一提交"
  这一原缺陷输入（复现），以及过短/不一致/dirty 后缀/空值必须判不一致（不变量：放宽 7/8 位的同时不能
  放过真正不同的提交）。
- ``test_zip_entry_lookup_*``：正斜杠 zip 与反斜杠 zip 都能按反斜杠路径查到条目，缺失返回空。
- ``test_release_yml_*``：release.yml 不再有 ``rev-parse --short`` 与对当前提交的字符串全等比较，zip 与 lock
  两处都经过 ``Test-WsGameCommitMatch``（静态守卫，防止有人把这一步改回去）。

Windows-only（PowerShell 子进程）；无宿主时 skip（沿用 ``_ps_harness`` 约定）。
"""

from __future__ import annotations

import re
import zipfile
from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, TOOLCHAIN_DIR, ps_quote, run_ps_json

LOCK_WRITEBACK = TOOLCHAIN_DIR / "_lock_writeback.ps1"
RELEASE_YML = REPO_ROOT / ".github" / "workflows" / "release.yml"

# 一个 40 位完整 sha，前 8 位 b7ac4dc3（取自 v1.93.0 的真实现场），前 7 位 b7ac4dc。
FULL_SHA = "b7ac4dc3" + "0123456789abcdef0123456789abcdef"
assert len(FULL_SHA) == 40


def _commit_match_results(tmp_path: Path, cases: list[tuple[str, str]]) -> list[bool]:
    items = ",".join(f"@({ps_quote(rec)},{ps_quote(full)})" for rec, full in cases)
    body = (
        f". {ps_quote(LOCK_WRITEBACK)}\n"
        f"$cases = @({items})\n"
        "$out = @()\n"
        "foreach ($c in $cases) { $out += ,([bool](Test-WsGameCommitMatch -Recorded $c[0] -FullSha $c[1])) }\n"
        "ConvertTo-Json -InputObject @($out) | Set-Content -LiteralPath $ResultPath -Encoding UTF8\n"
    )
    return run_ps_json(tmp_path, body, name="commit_match")


def test_commit_match_accepts_7_and_8_digit_abbreviations_of_same_commit(tmp_path: Path) -> None:
    # 原缺陷输入：lock/MANIFEST 记 8 位，CI 浅克隆缩写只有 7 位——两者指向同一提交，必须判一致。
    # 另含 7 位记录、40 位记录、大写记录。
    cases = [
        (FULL_SHA[:8], FULL_SHA),
        (FULL_SHA[:7], FULL_SHA),
        (FULL_SHA, FULL_SHA),
        (FULL_SHA[:8].upper(), FULL_SHA),
    ]
    assert _commit_match_results(tmp_path, cases) == [True, True, True, True]


def test_commit_match_rejects_different_or_malformed_records(tmp_path: Path) -> None:
    # 不变量：放宽位数不能放过真正不同的提交，也不能接受过短 / 带后缀 / 空的记录值。
    other = "a1b2c3d4" + FULL_SHA[8:]
    cases = [
        (other[:8], FULL_SHA),                 # 不同提交（8 位）
        (other[:7], FULL_SHA),                 # 不同提交（7 位）
        (FULL_SHA[:6], FULL_SHA),              # 同一提交但低于 7 位下限
        (FULL_SHA[:8] + "-dirty", FULL_SHA),   # dirty 后缀含非十六进制字符
        ("", FULL_SHA),                        # 空记录
        (FULL_SHA + "0", FULL_SHA),            # 记录比完整 sha 还长
        (FULL_SHA[:8], FULL_SHA[:8]),          # 当前提交不是 40 位完整 sha（调用方没取完整值）
    ]
    assert _commit_match_results(tmp_path, cases) == [False] * len(cases)


def _make_zip(path: Path, separator: str) -> None:
    top = "ws-game-9.9.9"
    with zipfile.ZipFile(path, "w") as zf:
        for rel, text in (("MANIFEST.txt", "git_commit: b7ac4dc3\n"), ("packages/x-9.9.9.tgz", "tgz")):
            info = zipfile.ZipInfo("placeholder")
            # ZipInfo 构造时会把 os.sep 换成 '/'；构造后直接改属性才能写出反斜杠条目（Compress-Archive
            # 在部分 PowerShell 版本下的真实产物形状）。
            info.filename = separator.join([top, *rel.split("/")])
            zf.writestr(info, text)


@pytest.mark.parametrize("separator", ["/", "\\"], ids=["forward_slash_zip", "backslash_zip"])
def test_zip_entry_lookup_ignores_path_separator(tmp_path: Path, separator: str) -> None:
    zip_path = tmp_path / "pkg.zip"
    _make_zip(zip_path, separator)
    body = (
        "Add-Type -AssemblyName System.IO.Compression.FileSystem\n"
        f". {ps_quote(LOCK_WRITEBACK)}\n"
        f"$zr = [System.IO.Compression.ZipFile]::OpenRead({ps_quote(zip_path)})\n"
        "try {\n"
        "  $m = Get-WsGameZipEntry -ZipReader $zr -EntryPath 'ws-game-9.9.9\\MANIFEST.txt'\n"
        "  $t = Get-WsGameZipEntry -ZipReader $zr -EntryPath 'ws-game-9.9.9\\packages\\x-9.9.9.tgz'\n"
        "  $n = Get-WsGameZipEntry -ZipReader $zr -EntryPath 'ws-game-9.9.9\\nope.txt'\n"
        "  $res = [ordered]@{ manifest = [bool]$m; tgz = [bool]$t; missing = [bool]$n }\n"
        "} finally { $zr.Dispose() }\n"
        "$res | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8\n"
    )
    result = run_ps_json(tmp_path, body, name="zip_entry")
    assert result == {"manifest": True, "tgz": True, "missing": False}


def _release_yml_run_lines() -> list[str]:
    lines = RELEASE_YML.read_text(encoding="utf-8").splitlines()
    return [ln for ln in lines if not ln.lstrip().startswith("#")]


def test_release_yml_compares_commit_by_full_sha_prefix() -> None:
    code = "\n".join(_release_yml_run_lines())
    assert "rev-parse --short" not in code, "release.yml 不得再用缩写 sha 做比对（缩写位数随仓库对象数变化）"
    assert "rev-parse HEAD" in code
    # 对当前提交不得再有字符串全等/不等比较。
    assert not re.search(r"-n?e\s+\$currentCommit", code), "提交号比对必须走 Test-WsGameCommitMatch"
    # zip（MANIFEST.txt）与 lock 两处都经过前缀比对。
    assert len(re.findall(r"Test-WsGameCommitMatch\s+-Recorded", code)) == 2


def test_release_yml_zip_entry_lookup_uses_separator_agnostic_helper() -> None:
    code = "\n".join(_release_yml_run_lines())
    assert not re.search(r"\$_\.FullName\s+-eq", code), "条目查找不得再用带固定分隔符的 FullName 精确匹配"
    assert "Get-WsGameZipEntry" in code
