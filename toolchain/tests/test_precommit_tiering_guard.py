"""``toolchain/_precommit_tiering_guard.ps1`` 的回归测试（2026-09-22，提交前钩子分级任务）。

背景（见 ``toolchain/_precommit_tiering_guard.ps1`` 文件头判断记录、
``.githooks/pre-commit`` 文件头判断记录）：一轮发布里主会话有 4～5 次提交，其中 CHANGELOG
定版/答复稿/回归记录只改 ``.md``，``build.ps1 -Release`` 在 32 步全量门禁通过后做的"发布
X.Y.Z"提交只改版本文件——这两类提交此前都会再跑一遍 29 步 ``check.ps1 -SkipUnity -Quick``
（约 100 秒），是重复验证。``.githooks/pre-commit`` 现在按暂存改动清单把提交分成三档：
``DocsOnly``（跑 ``check.ps1 -DocsOnly``，只跑文档相关几步）、``ReleaseSkip``（不重复跑
``check.ps1``）、``Full``（照旧跑 ``check.ps1 -SkipUnity -Quick``）。

本文件覆盖抽出的纯函数 ``Get-PreCommitCheckTier``（输入：暂存路径清单、
``build.ps1 -Release`` 是否已设置 ``WS_GAME_RELEASE_COMMIT`` 环境变量；输出：
``Tier``/``Reason`` 两个字段），惯例同
``toolchain/tests/test_unity_smoke_wait_scope_guard.py``：dot-source 独立的纯函数脚本后
直接调用，不需要真的跑一次 git 提交或 check.ps1。

覆盖：纯 md -> DocsOnly；md + 一个 .cs -> Full；版本文件清单 + 变量 -> ReleaseSkip；版本文件
清单无变量 -> Full；变量在但清单多了一个 .cs -> Full；空清单（有/无变量都不应该被真空真判定
成任何"可跳过"档）-> Full；重命名后落地的新路径（``git diff --cached --name-only`` 对重命名
只报告新路径这一行，纯函数不需要、也不感知"这条路径是不是由重命名产生"）-> 按扩展名正常分类；
大小写不敏感的 ``.MD`` 扩展名；版本文件清单的真子集（不要求五个文件齐全）同样判 ReleaseSkip；
清单里混入一个不在 ``build.ps1`` 实际 ``git add`` 清单里的"像版本文件"的路径时不能误判为
ReleaseSkip。

跨平台说明：依赖 ``powershell``（Windows PowerShell 5.1）或 ``pwsh`` 可执行，本机没有时 skip，
不 fail（与仓库其余 ``.ps1`` 相关测试一致约定）。

运行：``python -m pytest toolchain/tests/test_precommit_tiering_guard.py -q``。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD_SCRIPT = REPO_ROOT / "toolchain" / "_precommit_tiering_guard.ps1"

# 与 build.ps1 "-Release 第 6 步" 实际 `git add` 的清单、
# toolchain/_precommit_tiering_guard.ps1 里的 $script:ReleaseWritebackFiles 三处保持一致
# （单一事实来源在生产代码里，这里的字面量只是测试侧独立核对，改了任一处忘了同步改另一处，
# 相关用例会直接失败）。
RELEASE_FILES = [
    "VERSION",
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json",
    "adapters/unity/Packages/packages-lock.json",
    "games/_template/package.json",
    "CHANGELOG.md",
]


def _find_powershell() -> str | None:
    for exe in ("powershell", "pwsh"):
        found = shutil.which(exe)
        if found:
            return exe
    return None


@pytest.fixture()
def ps_exe() -> str:
    exe = _find_powershell()
    if exe is None:
        pytest.skip("本机未找到 powershell/pwsh 可执行文件，跳过（与仓库其余 .ps1 相关测试一致约定）")
    return exe


def _ps_single_quote(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def _ps_string_array_literal(paths: list[str]) -> str:
    if not paths:
        return "@()"
    return "@(" + ", ".join(_ps_single_quote(p) for p in paths) + ")"


def _run_tier(ps_exe: str, paths: list[str], release_env_set: bool) -> tuple[str, str]:
    """dot-source 守卫脚本并直接调用 ``Get-PreCommitCheckTier``，返回 ``(Tier, Reason)``。"""
    bool_literal = "$true" if release_env_set else "$false"
    script = (
        '$ErrorActionPreference = "Stop"; '
        '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; '
        f'. "{GUARD_SCRIPT}"; '
        "$result = Get-PreCommitCheckTier "
        f"-StagedPaths {_ps_string_array_literal(paths)} "
        f"-ReleaseCommitEnvSet {bool_literal}; "
        'Write-Output ("TIER=" + $result.Tier); '
        'Write-Output ("REASON=" + $result.Reason)'
    )
    result = subprocess.run(
        [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        env=clean_powershell_env(ps_exe),
        check=True,
    )
    tier: str | None = None
    reason: str | None = None
    for line in result.stdout.splitlines():
        if line.startswith("TIER="):
            tier = line[len("TIER="):].strip()
        elif line.startswith("REASON="):
            reason = line[len("REASON="):].strip()
    if tier is None or reason is None:
        raise AssertionError(
            f"未在输出中找到 TIER=/REASON= 标记：stdout={result.stdout!r} stderr={result.stderr!r}"
        )
    return tier, reason


# ---------------------------------------------------------------------------
# 1. 文档档
# ---------------------------------------------------------------------------


def test_pure_md_is_docs_only(ps_exe: str) -> None:
    tier, _ = _run_tier(ps_exe, ["CHANGELOG.md", "architecture/00_foo.md"], False)
    assert tier == "DocsOnly"


def test_single_md_is_docs_only(ps_exe: str) -> None:
    tier, _ = _run_tier(ps_exe, ["README.md"], False)
    assert tier == "DocsOnly"


def test_uppercase_md_extension_is_docs_only(ps_exe: str) -> None:
    tier, _ = _run_tier(ps_exe, ["README.MD"], False)
    assert tier == "DocsOnly"


def test_renamed_md_path_is_docs_only(ps_exe: str) -> None:
    # git diff --cached --name-only 对重命名只报告改名后的新路径（一行一个仓库相对路径）；
    # 纯函数不需要、也不感知"这条路径是不是由重命名产生"——只要落地路径以 .md 结尾就按文档处理。
    tier, _ = _run_tier(ps_exe, ["architecture/落地计划/renamed-plan.md"], False)
    assert tier == "DocsOnly"


# ---------------------------------------------------------------------------
# 2. md 混入代码文件 -> 全量档
# ---------------------------------------------------------------------------


def test_md_plus_cs_is_full(ps_exe: str) -> None:
    tier, _ = _run_tier(ps_exe, ["CHANGELOG.md", "core/foo/Bar.cs"], False)
    assert tier == "Full"


def test_renamed_non_md_path_is_full(ps_exe: str) -> None:
    tier, _ = _run_tier(ps_exe, ["CHANGELOG.md", "toolchain/renamed_script.py"], False)
    assert tier == "Full"


# ---------------------------------------------------------------------------
# 3. 发布跳过档：版本文件清单 + 环境变量
# ---------------------------------------------------------------------------


def test_release_files_with_env_is_release_skip(ps_exe: str) -> None:
    tier, reason = _run_tier(ps_exe, RELEASE_FILES, True)
    assert tier == "ReleaseSkip"
    assert "build.ps1" in reason


def test_release_files_subset_with_env_is_release_skip(ps_exe: str) -> None:
    # 只含版本写回清单里的一部分（例如只改了 VERSION 一个文件）也算"只含"，不要求五个文件齐全。
    tier, _ = _run_tier(ps_exe, ["VERSION"], True)
    assert tier == "ReleaseSkip"


def test_release_files_without_env_is_full(ps_exe: str) -> None:
    tier, _ = _run_tier(ps_exe, RELEASE_FILES, False)
    assert tier == "Full"


def test_release_files_plus_extra_cs_with_env_is_full(ps_exe: str) -> None:
    tier, _ = _run_tier(ps_exe, RELEASE_FILES + ["core/foo/Bar.cs"], True)
    assert tier == "Full"


def test_lookalike_version_file_with_env_is_full(ps_exe: str) -> None:
    # 变量在，但清单里出现一个不在 build.ps1 实际 git add 清单里的"像版本文件"的路径
    # （adapters/unity/Packages/manifest.json 不是 packages-lock.json），仍不能判为 ReleaseSkip。
    tier, _ = _run_tier(ps_exe, ["VERSION", "adapters/unity/Packages/manifest.json"], True)
    assert tier == "Full"


# ---------------------------------------------------------------------------
# 4. 空清单：不做真空真判定
# ---------------------------------------------------------------------------


def test_empty_staged_list_is_full(ps_exe: str) -> None:
    tier, reason = _run_tier(ps_exe, [], False)
    assert tier == "Full"
    assert "空" in reason


def test_empty_staged_list_with_release_env_is_still_full(ps_exe: str) -> None:
    tier, _ = _run_tier(ps_exe, [], True)
    assert tier == "Full"


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-v"]))
