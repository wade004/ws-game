"""``toolchain/_version_writeback.ps1`` 写回版本号到已跟踪源码文件（VERSION、两个 package.json、
packages-lock.json）不应产生 CRLF 的回归测试（build.ps1 -Release 写回版本号步骤 CRLF 缺陷根治，
2026-09-10）。

背景：见 toolchain/_version_writeback.ps1 头注释与 Set-SourcePackageJsonVersion 函数注释——
PowerShell 5.1 的 ConvertTo-Json 输出自带 `\r\n`，此前 build.ps1 内联的写回函数直接把这段文本传给
WriteAllText，写出的两个 package.json 带 CRLF，与 .gitattributes 的 `eol=lf` 声明冲突，触发
check.ps1 门禁"工作树文本文件无 CR"步骤 FAIL（-Release 1.15.0 实测复现）。

本测试把仓库当前 VERSION、两个 package.json、packages-lock.json 复制到临时目录，dot-source
_version_writeback.ps1 后直接调用其中的函数对副本写回一个测试版本号，断言：
  1. 写出的文件字节不含任何 `\r`（无论原始是何种混用行尾，都不能出现——最强断言）。
  2. UTF-8 编码且不带 BOM（与原文件一致）。
  3. 内容改动只限于版本号字段（用 JSON 解析比较，或对 packages-lock.json 做长度差量粗略比较），
     其余字段/格式不受影响。
  4. 两个 package.json 写回后仍以单个 `\n` 收尾（与原文件既有"文件尾随一个换行"的约定一致）。
  5. 静态断言 toolchain/_version_writeback.ps1 不使用 Set-Content/Out-File/Add-Content
     （PowerShell 5.1 下这几个 cmdlet 要么固定带 BOM，要么不便控制换行符，是本次缺陷的同类风险
     写法）。

跨平台说明：前四类测试依赖 `powershell`（Windows PowerShell 5.1）或 `pwsh` 可执行，本机没有时
skip，不 fail（与仓库其余依赖 PowerShell 解释器的测试约定一致）；第五类是纯文本静态检查，不依赖
PowerShell 解释器。

运行：``python -m pytest toolchain/tests/test_build_version_writeback_lf.py -q`` 或
``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
WRITEBACK_SCRIPT = REPO_ROOT / "toolchain" / "_version_writeback.ps1"

ADAPTER_PACKAGE_JSON = REPO_ROOT / "adapters" / "unity" / "Packages" / "com.gamefoundation.adapter.unity" / "package.json"
TEMPLATE_PACKAGE_JSON = REPO_ROOT / "games" / "_template" / "package.json"
PACKAGES_LOCK_JSON = REPO_ROOT / "adapters" / "unity" / "Packages" / "packages-lock.json"
VERSION_FILE = REPO_ROOT / "VERSION"

NEW_VERSION = "9.9.9"


def _find_powershell() -> str | None:
    for exe in ("powershell", "pwsh"):
        found = shutil.which(exe)
        if found:
            return exe
    return None


def _run_ps(exe: str, script: str) -> None:
    result = subprocess.run(
        [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        capture_output=True,
        text=True,
    )
    assert result.returncode == 0, (
        f"PowerShell 脚本执行失败 (exit={result.returncode})\nstdout={result.stdout}\nstderr={result.stderr}"
    )


@pytest.fixture()
def ps_exe() -> str:
    exe = _find_powershell()
    if exe is None:
        pytest.skip("本机未找到 powershell/pwsh 可执行文件，跳过（与仓库其余 .ps1 相关测试一致约定）")
    return exe


def _assert_no_cr(path: Path) -> None:
    data = path.read_bytes()
    assert b"\r" not in data, f"{path} 写回后仍包含 \\r 字节（应为纯 LF）"


def _assert_utf8_no_bom(path: Path) -> None:
    data = path.read_bytes()
    assert not data.startswith(b"\xef\xbb\xbf"), f"{path} 写回后带 UTF-8 BOM（应无 BOM）"
    data.decode("utf-8")


def test_set_version_file_content_no_cr(tmp_path: Path, ps_exe: str) -> None:
    dst = tmp_path / "VERSION"
    shutil.copy2(VERSION_FILE, dst)
    _run_ps(ps_exe, f'. "{WRITEBACK_SCRIPT}"; Set-VersionFileContent -Path "{dst}" -Version "{NEW_VERSION}"')
    _assert_no_cr(dst)
    _assert_utf8_no_bom(dst)
    assert dst.read_text(encoding="utf-8") == NEW_VERSION


@pytest.mark.parametrize("src", [ADAPTER_PACKAGE_JSON, TEMPLATE_PACKAGE_JSON], ids=["adapter_package_json", "template_package_json"])
def test_set_source_package_json_version_no_cr(tmp_path: Path, ps_exe: str, src: Path) -> None:
    dst = tmp_path / src.name
    shutil.copy2(src, dst)
    original_obj = json.loads(src.read_text(encoding="utf-8"))

    _run_ps(ps_exe, f'. "{WRITEBACK_SCRIPT}"; Set-SourcePackageJsonVersion -JsonPath "{dst}" -Version "{NEW_VERSION}"')

    _assert_no_cr(dst)
    _assert_utf8_no_bom(dst)

    new_text = dst.read_text(encoding="utf-8")
    assert new_text.endswith("\n") and not new_text.endswith("\n\n"), "写回后应以单个换行收尾（与原文件约定一致）"

    new_obj = json.loads(new_text)
    assert new_obj["version"] == NEW_VERSION
    original_obj["version"] = NEW_VERSION
    if "dependencies" in original_obj and "com.gamefoundation.adapter.unity" in original_obj.get("dependencies", {}):
        original_obj["dependencies"]["com.gamefoundation.adapter.unity"] = NEW_VERSION
    assert new_obj == original_obj, "写回后内容应只有版本号字段变化，其余内容不变"


def test_set_packages_lock_game_template_dependency_no_cr(tmp_path: Path, ps_exe: str) -> None:
    dst = tmp_path / "packages-lock.json"
    shutil.copy2(PACKAGES_LOCK_JSON, dst)
    original_raw = PACKAGES_LOCK_JSON.read_bytes()

    _run_ps(ps_exe, f'. "{WRITEBACK_SCRIPT}"; Set-PackagesLockGameTemplateDependency -JsonPath "{dst}" -Version "{NEW_VERSION}"')

    _assert_no_cr(dst)
    _assert_utf8_no_bom(dst)

    new_raw = dst.read_bytes()
    assert abs(len(new_raw) - len(original_raw)) <= 8, "写回后文件长度变化应仅来自版本号数字位数差异（正则定点替换，不应改动其余内容）"


def test_writeback_script_avoids_set_content_family() -> None:
    text = WRITEBACK_SCRIPT.read_text(encoding="utf-8")
    for forbidden in ("Set-Content", "Out-File", "Add-Content"):
        assert forbidden not in text, (
            f"toolchain/_version_writeback.ps1 不应使用 {forbidden}"
            "（应统一用 [System.IO.File]::WriteAllText，见文件头判断记录）"
        )


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
