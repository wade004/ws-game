"""``toolchain/_unity_path_length_guard.ps1`` 的回归测试（2026-09-20，Windows MAX_PATH 快速
失败守卫）。

背景（见该脚本头注释与 check.ps1 调用点判断记录）：深层 scratchpad 工作树里跑 Unity PlayMode
测试——具体是 games/_template/Tests/Runtime/GameTemplateResidentTests.cs 的
ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly 用例——会把
adapters/unity/Assets/StreamingAssets/GameFoundation/data/game 整棵目录树复制到同级一个带
32 位十六进制 GUID 的新目录，工作树根路径较深时复制出来的文件绝对路径可能超过 Windows 260
字符 MAX_PATH；且 Mono/.NET 旧式路径 API 在这种情况下抛的是 DirectoryNotFoundException 而不是
PathTooLongException，容易被误判为产品缺陷。`Test-UnityWorkingTreePathLength` 在真正调用任何
Unity 批处理之前先算一次预估最长路径，超阈值直接终止并给出自解释的错误信息。

本文件用一棵内容极简的合成"仓库"目录树（只含
adapters/unity/Assets/StreamingAssets/GameFoundation/data/game/ 下两三个小文件）验证该函数：
  1. 根路径够短时放行（不抛异常）。
  2. 根路径足够深时终止，且错误信息包含关键定位信息：当前根路径长度、预估最长路径长度、
     260 上限、"怎么办"指引。
  3. 扫描逻辑正确挑出 data/game 下最长的相对路径（含子目录），且正确排除 .meta 文件。
  4. data/game 数据根缺失时不误报（返回而不抛异常，这是数据校验步骤该管的事）。

判断记录（为什么测试夹具的物理路径始终控制在 260 字符以内，不靠开启 Windows 长路径支持来让
夹具本身能安全落到 260+ 字符）：本次任务范围明确排除"修复"路径过深本身（改注册表、开长路径
支持、加长路径前缀都不在本单范围内，见任务书），测试同样不应该依赖宿主机的长路径支持状态——
用例 2 只让函数内部"模拟"出来的覆盖目录路径超过阈值，真正在磁盘上创建的目录/文件路径全程
远低于 260（详见 `_MAX_SAFE_PHYSICAL_REPO_ROOT_LEN` 的推导），因此在任何未开启长路径支持的
Windows 机器上都应确定性通过，不会因为"这台机器能不能建超长路径"而变得不稳定。

跨平台说明：依赖 `powershell`（Windows PowerShell 5.1）或 `pwsh` 可执行，本机没有时 skip，不
fail（与仓库其余依赖 PowerShell 解释器的测试约定一致）。

运行：``python -m pytest toolchain/tests/test_unity_path_length_guard.py -q``。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD_SCRIPT = REPO_ROOT / "toolchain" / "_unity_path_length_guard.ps1"

DATASET_RELATIVE_DIR = Path("adapters/unity/Assets/StreamingAssets/GameFoundation/data/game")

# 以下几个常量镜像 toolchain/_unity_path_length_guard.ps1 里的同名字面量——两边必须保持一致，
# 该文件改了对应字面量时这里也要跟着改（不是真正共享同一份定义：PowerShell 侧是生产代码，这里
# 只是测试侧独立复算一遍同样的算式，用来精确推导"多深的根路径既能触发阈值、又能保证物理路径不
# 超过 260"这个安全窗口，避免测试本身变成又一个写死数字、脱离实际算式的地方）。
_OVERRIDE_DIR_NAME_PLACEHOLDER = "gf_test_dataset_root_override_" + ("0" * 32)
_WINDOWS_MAX_PATH = 260
_SAFETY_MARGIN = 10
_THRESHOLD = _WINDOWS_MAX_PATH - _SAFETY_MARGIN

# 用例 2（深层根路径）里真正会在磁盘上创建的最长相对路径，以及函数内部会模拟出来（从不落盘）
# 的对应覆盖目录相对路径。
_PHYSICAL_LONGEST_RELATIVE = str(DATASET_RELATIVE_DIR / "combat" / "deep_file_in_subdir.json")
_SIMULATED_LONGEST_RELATIVE = "\\".join(
    [
        "adapters", "unity", "Assets", "StreamingAssets", "GameFoundation", "data",
        _OVERRIDE_DIR_NAME_PLACEHOLDER, "combat", "deep_file_in_subdir.json",
    ]
)

# 安全窗口：根路径长度必须大于此值，预估的模拟路径长度才会超过阈值。
_MIN_DEEP_REPO_ROOT_LEN = _THRESHOLD - 1 - len(_SIMULATED_LONGEST_RELATIVE) + 1
# 根路径长度必须不超过此值，物理真实创建的路径长度才能留有余量地保持在 260 字符以内
# （259 - 1 - 物理相对路径长度，再减一点余量给分隔符/取整误差）。
_MAX_SAFE_PHYSICAL_REPO_ROOT_LEN = _WINDOWS_MAX_PATH - 1 - 1 - len(_PHYSICAL_LONGEST_RELATIVE) - _SAFETY_MARGIN
_TARGET_DEEP_REPO_ROOT_LEN = (_MIN_DEEP_REPO_ROOT_LEN + _MAX_SAFE_PHYSICAL_REPO_ROOT_LEN) // 2

assert _MIN_DEEP_REPO_ROOT_LEN < _MAX_SAFE_PHYSICAL_REPO_ROOT_LEN, (
    "安全窗口计算有误：不存在一个根路径长度，能同时满足'模拟路径超阈值'与'物理路径远低于 260'"
)


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


def _build_dataset_fixture(repo_root: Path) -> None:
    """在 repo_root 下搭一棵极简的 data/game 树：根目录一个短文件（a.json），combat/ 子目录一个
    明显更长的文件名 + 对应 .meta——用于验证扫描时会挑中子目录里更长的那条相对路径，且会跳过
    .meta 文件（否则 .meta 常常比对应正文文件名更长，会污染"最长相对路径"的判定）。"""
    game_dir = repo_root / DATASET_RELATIVE_DIR
    (game_dir / "combat").mkdir(parents=True, exist_ok=True)
    (game_dir / "a.json").write_text("{}", encoding="utf-8")
    (game_dir / "combat" / "deep_file_in_subdir.json").write_text("{}", encoding="utf-8")
    (game_dir / "combat" / "deep_file_in_subdir.json.meta").write_text("fileFormatVersion: 2", encoding="utf-8")


def _run_guard(ps_exe: str, repo_root: Path) -> subprocess.CompletedProcess[str]:
    # 判断记录（Windows PowerShell 5.1 下非终端/被管道重定向的 stdout/stderr 默认走系统 ANSI
    # 代码页——本机实测是 GBK/936，不是源文件的 UTF-8——直接用 `text=True`/默认 locale 解码
    # 偶发在多字节序列跨读缓冲区边界处抛 UnicodeDecodeError，且换一台非中文系统代码页就会完全
    # 解不出来）：脚本内先把 `[Console]::OutputEncoding` 显式设成 UTF8，并把
    # `Test-UnityWorkingTreePathLength` 包一层 try/catch，异常信息改走 `Write-Output`（stdout）
    # 而不是让它以未捕获异常的形式落到原生 stderr——这样无论宿主机系统区域设置是什么代码页，
    # Python 侧固定用 `encoding="utf-8"` 解码都能拿到正确文本，不依赖也不硬编码某个特定代码页。
    script = (
        '$ErrorActionPreference = "Stop"; '
        '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; '
        f'. "{GUARD_SCRIPT}"; '
        'try { '
        f'Test-UnityWorkingTreePathLength -RepoRoot "{repo_root}"; '
        'Write-Output "GUARD_PASSED" '
        '} catch { '
        'Write-Output "GUARD_THROWN:"; '
        'Write-Output $_.Exception.Message; '
        'exit 1 '
        '}'
    )
    return subprocess.run(
        [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        env=clean_powershell_env(ps_exe),
    )


def test_short_repo_root_passes(tmp_path: Path, ps_exe: str) -> None:
    repo_root = tmp_path / "short"
    _build_dataset_fixture(repo_root)
    assert len(str(repo_root)) < _MIN_DEEP_REPO_ROOT_LEN, "本用例前提是根路径本身足够短，不应该已经触发守卫"

    result = _run_guard(ps_exe, repo_root)

    assert result.returncode == 0, f"短路径根不应触发守卫\nstdout={result.stdout}\nstderr={result.stderr}"
    assert "GUARD_PASSED" in result.stdout


def test_deep_repo_root_blocked_with_actionable_message(tmp_path: Path, ps_exe: str) -> None:
    # 用一段占位目录名把根路径人为撑到安全窗口内（见模块头判断记录与上方几个 _MIN/_MAX 常量的
    # 推导）：物理创建的真实路径（根路径 + data/game 下最深那条相对路径）留有余量地保持在 260
    # 字符以内，不依赖宿主机的长路径支持；只有函数内部"模拟"出来的覆盖目录路径会超过阈值。
    floor_len = len(str(tmp_path / "d"))
    if floor_len > _MAX_SAFE_PHYSICAL_REPO_ROOT_LEN:
        pytest.skip(
            f"宿主机 pytest tmp_path 本身已经比较深（{tmp_path}，{floor_len} 字符），无法在保持"
            "物理路径远低于 260 字符的前提下再撑出目标测试根长度，跳过（不代表守卫函数本身有问题）"
        )
    target_len = max(_TARGET_DEEP_REPO_ROOT_LEN, floor_len)
    target_len = min(target_len, _MAX_SAFE_PHYSICAL_REPO_ROOT_LEN)
    padding_len = max(target_len - len(str(tmp_path)) - 1, 1)
    repo_root = tmp_path / ("d" * padding_len)
    _build_dataset_fixture(repo_root)

    physical_longest = repo_root / _PHYSICAL_LONGEST_RELATIVE
    assert len(str(physical_longest)) < _WINDOWS_MAX_PATH - _SAFETY_MARGIN, (
        "测试夹具本身必须留有余量地保持在 260 字符以内——本用例只应该让函数内部的模拟计算超阈值，"
        "不应该依赖宿主机真的能在磁盘上创建接近上限的路径"
    )
    assert len(str(repo_root)) > _MIN_DEEP_REPO_ROOT_LEN, "本用例前提是根路径长度足以让模拟出来的覆盖目录路径超过阈值"

    result = _run_guard(ps_exe, repo_root)

    assert result.returncode != 0, f"深层根路径应触发守卫终止\nstdout={result.stdout}\nstderr={result.stderr}"
    combined = result.stdout + result.stderr
    assert "GUARD_PASSED" not in combined
    assert "260" in combined, "错误信息应报出 Windows MAX_PATH 上限"
    assert str(len(str(repo_root))) in combined, "错误信息应报出当前根路径的实际长度"
    assert "combat" in combined and "deep_file_in_subdir.json" in combined, (
        "错误信息应包含扫描出的最长相对路径（证明确实挑中了子目录里更长的那条，而不是根目录下的 a.json）"
    )
    assert "a.json" not in combined, "不应该把根目录下更短的 a.json 误判为最长相对路径"
    assert "怎么办" in combined, "错误信息应给出可执行的指引"
    assert "主检出" in combined, "指引应提到换到主检出或路径足够短的工作树"


def test_missing_data_game_root_does_not_throw(tmp_path: Path, ps_exe: str) -> None:
    repo_root = tmp_path / "no_dataset"
    repo_root.mkdir(parents=True, exist_ok=True)

    result = _run_guard(ps_exe, repo_root)

    assert result.returncode == 0, (
        "data/game 数据根缺失时不应该误报——那是数据校验步骤该管的事\n"
        f"stdout={result.stdout}\nstderr={result.stderr}"
    )
    assert "GUARD_PASSED" in result.stdout


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
