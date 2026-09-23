"""``toolchain/_unity_smoke_wait_scope_guard.ps1`` 的回归测试（2026-09-21，
consumer_smoke.ps1 的 ``Wait-NoResidualUnityProcess`` 等待范围收窄）。

背景（见 ``toolchain/_unity_smoke_wait_scope_guard.ps1`` 文件头判断记录、
``consumer_smoke.ps1`` 文件头 ``.NOTES``"P07 根治之一"一节）：``Wait-NoResidualUnityProcess``
此前不按工程路径过滤，等系统里任何 ``Unity.exe`` 都退出才放行——这个"不过滤"本身是故意的
（``check.ps1`` 全量门禁跑完框架自己的 ``adapters/unity`` 工程后紧接着跑本脚本，本脚本随即在另一个
工程路径下拉起 Unity，上一个进程退出到它真正清理完之间有滞后窗口，按本脚本自己的工程路径过滤会
漏掉这类残留），但代价是别的仓库里长时间正常运行的 Unity 进程也会被一起等，等多久都不会消失，
白白拖垮门禁。修复把"要等"收窄为"可能与本次演练撞车的两类工程"：本仓库根目录下的、本脚本工作
目录下的；拿不到命令行/没有 ``-projectPath`` 时按保守口径当作要等；明确落在别处的不等（但会打印
提示，不静默忽略）。

本文件覆盖抽出的纯函数 ``Get-UnitySmokeProcessWaitDecision``（输入：进程命令行字符串、仓库根、
工作目录；输出："Wait"/"NoWait"/"Unknown" 三态之一），惯例同
``toolchain/tests/test_unity_path_length_guard.py``/``test_dist_immutability_guard.py``：
dot-source 独立的守卫脚本后直接调用，不需要真的起 Unity 进程。

至少覆盖：本仓库下的工程 -> Wait；本脚本工作目录下 -> Wait；别的仓库下 -> NoWait；无
``-projectPath`` -> Unknown；空命令行 -> Unknown；路径带引号/带空格/大小写不同/分隔符混用时
判断仍然正确；以及最容易写错的一例——仓库根的同名前缀目录（仓库根
``D:\\workespace\\ws-game``，另一个工程在 ``D:\\workespace\\ws-game-wow\\unity``）必须判 NoWait，
不能被简单的字符串前缀匹配误判成本仓库的子目录。

跨平台说明：依赖 ``powershell``（Windows PowerShell 5.1）或 ``pwsh`` 可执行，本机没有时 skip，
不 fail（与仓库其余 ``.ps1`` 相关测试一致约定）。

运行：``python -m pytest toolchain/tests/test_unity_smoke_wait_scope_guard.py -q``。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD_SCRIPT = REPO_ROOT / "toolchain" / "_unity_smoke_wait_scope_guard.ps1"

# 用固定字面量而不是真实的 REPO_ROOT/环境 TEMP，让测试用例完全自包含、不受宿主机实际路径影响
# （尤其"同名前缀目录"这一例，需要精确控制仓库根与另一工程路径的关系）。
REPO_ROOT_FOR_TEST = r"D:\workespace\ws-game"
WORK_DIR_FOR_TEST = r"C:\Users\tester\AppData\Local\Temp\gf_consumer_smoke"


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


def _run_decision(
    ps_exe: str,
    command_line: str,
    repo_root: str = REPO_ROOT_FOR_TEST,
    work_dir: str = WORK_DIR_FOR_TEST,
) -> str:
    """dot-source 守卫脚本并直接调用 ``Get-UnitySmokeProcessWaitDecision``，返回其三态返回值的
    纯文本（"Wait"/"NoWait"/"Unknown"）。命令行参数一律走 PowerShell 单引号字面量并对内部的
    ``'`` 做转义（``''``），避免 Python 侧拼接字符串时与 PowerShell 侧的引号规则互相干扰——待测的
    进程命令行本身经常包含双引号，用单引号包裹整体最省心。
    """

    def _ps_single_quote(value: str) -> str:
        return "'" + value.replace("'", "''") + "'"

    script = (
        '$ErrorActionPreference = "Stop"; '
        '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; '
        f'. "{GUARD_SCRIPT}"; '
        "$decision = Get-UnitySmokeProcessWaitDecision "
        f"-CommandLine {_ps_single_quote(command_line)} "
        f"-RepoRoot {_ps_single_quote(repo_root)} "
        f"-WorkDir {_ps_single_quote(work_dir)}; "
        'Write-Output ("DECISION=" + $decision)'
    )
    result = subprocess.run(
        [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        env=clean_powershell_env(ps_exe),
        check=True,
    )
    for line in result.stdout.splitlines():
        if line.startswith("DECISION="):
            return line[len("DECISION="):].strip()
    raise AssertionError(f"未在输出中找到 DECISION= 标记：stdout={result.stdout!r} stderr={result.stderr!r}")


# ---------------------------------------------------------------------------
# 1. 基本三态判定
# ---------------------------------------------------------------------------


def test_project_under_repo_root_waits(ps_exe: str) -> None:
    cmdline = '"C:\\Unity.exe" -batchmode -nographics -quit -projectPath "D:\\workespace\\ws-game\\adapters\\unity" -logFile x.log'
    assert _run_decision(ps_exe, cmdline) == "Wait"


def test_project_under_work_dir_waits(ps_exe: str) -> None:
    cmdline = (
        f'"C:\\Unity.exe" -batchmode -projectPath "{WORK_DIR_FOR_TEST}\\ConsumerProject" -logFile x.log'
    )
    assert _run_decision(ps_exe, cmdline) == "Wait"


def test_project_under_other_repo_does_not_wait(ps_exe: str) -> None:
    cmdline = '"C:\\Unity.exe" -batchmode -projectPath "D:\\workespace\\other-project\\unity" -logFile x.log'
    assert _run_decision(ps_exe, cmdline) == "NoWait"


def test_missing_project_path_flag_is_unknown(ps_exe: str) -> None:
    cmdline = '"C:\\Unity.exe" -batchmode -nographics -quit -logFile x.log'
    assert _run_decision(ps_exe, cmdline) == "Unknown"


def test_empty_command_line_is_unknown(ps_exe: str) -> None:
    assert _run_decision(ps_exe, "") == "Unknown"


# ---------------------------------------------------------------------------
# 2. 关键回归：仓库根的同名前缀目录不能被误判为子目录
# ---------------------------------------------------------------------------


def test_sibling_repo_with_shared_prefix_not_waited(ps_exe: str) -> None:
    """仓库根 D:\\workespace\\ws-game 与另一工程 D:\\workespace\\ws-game-wow\\unity——后者的
    字符串前缀恰好是仓库根，若直接用简单的 StartsWith(ancestor) 比较（不补分隔符）会把它误判成
    仓库根的子目录，从而错误地等待一个根本不相关、可能长期运行的别的项目的 Unity 进程。这是本次
    任务书明确点出的"最容易写错的一例"。
    """
    cmdline = '"C:\\Unity.exe" -batchmode -projectPath "D:\\workespace\\ws-game-wow\\unity" -logFile x.log'
    assert _run_decision(ps_exe, cmdline) == "NoWait"


def test_prefix_directory_of_work_dir_not_waited(ps_exe: str) -> None:
    """同一类同名前缀问题也要在 WorkDir 一侧成立：工作目录是 ...\\gf_consumer_smoke，另一个工程
    落在 ...\\gf_consumer_smoke_other_tool 之类的同名前缀兄弟目录下，不应被误判为工作目录的子级。
    """
    sibling = WORK_DIR_FOR_TEST + "_other_tool\\unity"
    cmdline = f'"C:\\Unity.exe" -batchmode -projectPath "{sibling}" -logFile x.log'
    assert _run_decision(ps_exe, cmdline) == "NoWait"


# ---------------------------------------------------------------------------
# 3. 路径写法的健壮性：引号/空格/大小写/分隔符
# ---------------------------------------------------------------------------


def test_project_path_with_spaces_in_quotes(ps_exe: str) -> None:
    cmdline = '"C:\\Unity.exe" -batchmode -projectPath "D:\\workespace\\ws-game\\adapters\\unity project" -logFile x.log'
    assert _run_decision(
        ps_exe,
        cmdline,
        repo_root="D:\\workespace\\ws-game",
    ) == "Wait"


def test_project_path_unquoted_no_spaces(ps_exe: str) -> None:
    cmdline = "C:\\Unity.exe -batchmode -projectPath D:\\workespace\\ws-game\\adapters\\unity -logFile x.log"
    assert _run_decision(ps_exe, cmdline) == "Wait"


def test_project_path_case_insensitive_and_mixed_separators(ps_exe: str) -> None:
    cmdline = '"C:\\Unity.exe" -PROJECTPATH "D:/WORKESPACE/WS-GAME/Adapters/Unity" -logFile x.log'
    assert _run_decision(ps_exe, cmdline) == "Wait"


def test_project_path_trailing_separator_still_matches(ps_exe: str) -> None:
    cmdline = '"C:\\Unity.exe" -projectPath "D:\\workespace\\ws-game\\" -logFile x.log'
    assert _run_decision(ps_exe, cmdline) == "Wait"


def test_project_path_equal_to_repo_root_waits(ps_exe: str) -> None:
    cmdline = '"C:\\Unity.exe" -projectPath "D:\\workespace\\ws-game" -logFile x.log'
    assert _run_decision(ps_exe, cmdline) == "Wait"


# -------------------------------------------------------------------------
# 3. 逐参数加引号的命令行（2026-09-23 实测翻红的真实形态）
#
# 按参数列表启动进程时，Windows 会把每个参数分别加引号，开关名自己也被引号包住
# （"-projectPath"），开关名后面紧跟的是引号而不是空白。这三例钉死：归属判定不因为这种写法退化
# 成 Unknown——否则别的仓库里正常运行的 Unity 批处理会被当成"无法判断"而保守等待，等满超时判
# 失败（1.67.0 发布门禁的消费方演练即因此连挂 3 步）。
# -------------------------------------------------------------------------


def _each_arg_quoted(project_path: str) -> str:
    return f'"C:\\Unity.exe" "-batchmode" "-projectPath" "{project_path}" "-logFile" "x.log"'


def test_each_arg_quoted_other_repo_does_not_wait(ps_exe: str) -> None:
    cmdline = _each_arg_quoted("D:\\workespace\\ws-game-wow\\unity")
    assert _run_decision(ps_exe, cmdline) == "NoWait"


def test_each_arg_quoted_repo_root_still_waits(ps_exe: str) -> None:
    cmdline = _each_arg_quoted("D:\\workespace\\ws-game\\adapters\\unity")
    assert _run_decision(ps_exe, cmdline) == "Wait"


def test_each_arg_quoted_work_dir_still_waits(ps_exe: str) -> None:
    cmdline = _each_arg_quoted(f"{WORK_DIR_FOR_TEST}\\ConsumerProject")
    assert _run_decision(ps_exe, cmdline) == "Wait"


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
