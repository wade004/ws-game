"""脚本在 ANSI 代码页下的可解析性门禁（第九轮审计后续，2026-09-08 CI 失败根治）。

背景：本机 `build.ps1 -Release 1.7.0 -PublishRegistry` 顺利跑完并本地发布，但推送前发现远端
CI 在同一提交上失败——`toolchain/_hash.ps1` 没有 UTF-8 BOM 且含中文注释，托管运行器的 Windows
PowerShell 5.1 按系统 ANSI 代码页（cp1252）读取脚本文件，中文 UTF-8 字节序列里的 0x93/0x94
被当成弯引号字符，导致 `_hash.ps1:48` 报 `TerminatorExpectedAtEndOfString`（字符串终止符缺失）。
本机代码页是 GBK，同样不是 UTF-8，但巧合下没有踩到这个具体的解析错误，门禁没有暴露这个缺陷。

仓库里其余 `.ps1`/`.psm1` 文件都带 UTF-8 BOM（`_hash.ps1` 是唯一的例外，已在同一轮修复中补
上），BOM 能让 PowerShell（包括 Windows PowerShell 5.1）正确识别文件是 UTF-8 而不必依赖系统
代码页猜测。本文件把这条约定变成门禁：

1. `test_non_ascii_script_has_bom`：仓库里任何跟踪的 `.ps1`/`.psm1` 文件，只要内容含非 ASCII
   字节，就必须以 UTF-8 BOM（`EF BB BF`）开头。
2. `test_script_parses_under_ci_codepage`：用 PowerShell 语言分析器（`Parser.ParseInput`，只做
   语法解析，不执行）复核每个脚本在“CI 场景”下能否被正确解析——没有 BOM 的文件按 cp1252
   解码文件字节来模拟托管 CI 运行器按 ANSI 代码页误读的情形；带 BOM 的文件改用 PowerShell 自身
   `Get-Content -Raw` 的默认读取（BOM 会被正确识别为 UTF-8，不需要额外模拟），断言两种情形下解析
   错误数都是 0。

`architecture/落地计划/audit-*/` 目录下的历史审计证据脚本（复现脚本、探针脚本等）不在本次门禁
范围内——它们是特定审计轮次的既有证据文件，改动会破坏审计留痕，且本身不参与 `build.ps1`/
`check.ps1` 的正常执行路径。

判断记录：先在 `_hash.ps1` 加回 BOM 之前跑过本文件确认
`test_script_parses_under_ci_codepage[toolchain/_hash.ps1]` 失败（解析错误数 > 0，与 CI 报的
`TerminatorExpectedAtEndOfString` 同一类问题），修复（补 BOM）后同一用例转为通过，且
`test_non_ascii_script_has_bom` 全量跑过没有新增失败——确认门禁测试本身有效，不是无论脚本对错
都通过的假阳性门禁。

运行：

```
python -m pytest toolchain/tests/test_powershell_scripts_ansi_safe.py -q
```

Windows-only（依赖 Windows PowerShell/`pwsh` 解析脚本），非 Windows 环境下全部用例自动跳过。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
HELPER_SCRIPT = Path(__file__).resolve().parent / "_ansi_parse_check.ps1"

# audit-*/ 下的历史审计证据脚本不参与本门禁（见模块 docstring）。
_EXCLUDE_SEGMENT = "architecture/落地计划/audit-"

pytestmark = pytest.mark.skipif(
    sys.platform != "win32", reason="依赖 Windows PowerShell/pwsh 解析 .ps1 脚本"
)


def _find_powershell_executables() -> list[str]:
    """本机可用的 PowerShell 宿主（去重，Windows PowerShell 5.1 优先、pwsh 其次）。"""
    found: list[str] = []
    seen_resolved: set[str] = set()
    for candidate in ("powershell.exe", "powershell", "pwsh.exe", "pwsh"):
        path = shutil.which(candidate)
        if not path:
            continue
        try:
            resolved = str(Path(path).resolve()).lower()
        except OSError:
            resolved = path.lower()
        if resolved in seen_resolved:
            continue
        seen_resolved.add(resolved)
        found.append(path)
    return found


AVAILABLE_POWERSHELLS: list[str] = (
    _find_powershell_executables() if sys.platform == "win32" else []
)


def _list_tracked_ps_files() -> list[str]:
    """`git ls-files` 列出的仓库内 `.ps1`/`.psm1` 相对路径，剔除 audit-*/ 证据脚本。
    `-c core.quotepath=false` 关闭 git 对非 ASCII 路径的八进制转义输出（否则 Chinese 路径段
    会被转义成 `"\\346..."` 形式的字面量，既不便匹配排除前缀，Python 侧也拿不到真实路径）。
    """
    result = subprocess.run(
        ["git", "-c", "core.quotepath=false", "ls-files", "*.ps1", "*.psm1"],
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=True,
    )
    files = [line for line in result.stdout.splitlines() if line.strip()]
    return [f for f in files if _EXCLUDE_SEGMENT not in f]


TRACKED_PS_FILES: list[str] = _list_tracked_ps_files() if sys.platform == "win32" else []


def _has_bom(data: bytes) -> bool:
    return data[:3] == b"\xef\xbb\xbf"


def _has_non_ascii(data: bytes) -> bool:
    return any(b > 0x7F for b in data)


@pytest.mark.parametrize("relpath", TRACKED_PS_FILES or ["__none__"])
def test_non_ascii_script_has_bom(relpath: str) -> None:
    if relpath == "__none__":
        pytest.skip("仓库内没有找到需要检查的 .ps1/.psm1 文件")
    data = (REPO_ROOT / relpath).read_bytes()
    if not _has_non_ascii(data):
        pytest.skip(f"{relpath} 内容全为 ASCII，不受本条约束")
    assert _has_bom(data), (
        f"{relpath} 含非 ASCII 字节但没有 UTF-8 BOM——托管 CI 运行器的 Windows PowerShell 5.1"
        "会按系统 ANSI 代码页误读该文件，可能产生解析错误（见 _hash.ps1 的实际故障案例）。"
    )


def _run_parse_check(relpath: str, mode: str, powershell: str) -> tuple[int, str]:
    target = REPO_ROOT / relpath
    result = subprocess.run(
        [
            powershell,
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(HELPER_SCRIPT),
            "-Path",
            str(target),
            "-Mode",
            mode,
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
    )
    lines = [line for line in result.stdout.splitlines() if line.strip()]
    if not lines:
        pytest.fail(
            f"_ansi_parse_check.ps1 对 {relpath}（mode={mode}）没有任何输出，"
            f"returncode={result.returncode}\nstdout={result.stdout}\nstderr={result.stderr}"
        )
    try:
        error_count = int(lines[0])
    except ValueError:
        pytest.fail(
            f"_ansi_parse_check.ps1 对 {relpath}（mode={mode}）输出格式异常：{lines[0]!r}\n"
            f"stdout={result.stdout}\nstderr={result.stderr}"
        )
    detail = "\n".join(lines[1:])
    return error_count, detail


@pytest.mark.parametrize("relpath", TRACKED_PS_FILES or ["__none__"])
def test_script_parses_under_ci_codepage(relpath: str) -> None:
    if relpath == "__none__":
        pytest.skip("仓库内没有找到需要检查的 .ps1/.psm1 文件")
    if not AVAILABLE_POWERSHELLS:
        pytest.skip("找不到 powershell.exe 或 pwsh")

    data = (REPO_ROOT / relpath).read_bytes()
    mode = "native" if _has_bom(data) else "cp1252"

    for powershell in AVAILABLE_POWERSHELLS:
        error_count, detail = _run_parse_check(relpath, mode, powershell)
        assert error_count == 0, (
            f"{relpath} 在 mode={mode}（宿主 {powershell}）下解析失败，"
            f"错误数={error_count}：\n{detail}"
        )
