"""``_pid_identity.py``（按进程身份而不是裸 PID 结束进程）的回归测试
（2026-10-01，bugfix/upm-evidence-stale-pid）。

背景：``test_registry_stop_pidfile_rewrite_timestamp.py`` 的 ``finally`` 里原先对"可能已经退出的 PID"
无条件 ``Stop-Process -Force``。Windows 会复用已退出进程的 PID，复用到的无关进程（例如门禁里 Unity 的
包管理器子进程）会被误杀。修法：起进程后马上记下身份（映像名 + 启动时间），清理时先在同一次
PowerShell 调用里核对身份，只结束身份匹配的进程。

真实的 PID 复用无法在测试里可控地制造（要在系统里耗尽/绕回 PID 空间），所以用等价的构造覆盖判定本身：

- 复现（原缺陷的核心）：给一个**已退出**进程的身份 -> 必须返回 GONE，不动任何进程；给一个**仍存活但不是
  当初那个进程**（同一个 PID，启动时间或映像名不符，等价于 PID 被复用）的身份 -> 必须返回 MISMATCH，
  且那个无关进程在调用之后仍然存活。
- 不变量：任何情况下只结束身份匹配的进程——身份匹配才返回 KILLED 且进程真的退出；``kill=False`` 的查询
  不结束进程；身份为 None（当初就没记下）时什么都不做。

宿主选择走 ``_ps_harness.find_powershell``，会进入 PowerShell 5.1/7 宿主矩阵。
"""

from __future__ import annotations

import subprocess
import sys
import time
from collections.abc import Iterator
from dataclasses import replace

import pytest

from _pid_identity import (
    START_TIME_TOLERANCE_TICKS,
    ProcessIdentity,
    kill_if_same_process,
    probe_process,
    snapshot_process_identity,
)
from _ps_harness import find_powershell

pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="依赖 Windows 进程 API")


@pytest.fixture(scope="module")
def powershell() -> str:
    return find_powershell()


def _spawn_sleeper(seconds: int = 120) -> subprocess.Popen:
    return subprocess.Popen(
        [sys.executable, "-c", f"import time; time.sleep({seconds})"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


@pytest.fixture()
def bystander(powershell: str) -> Iterator[tuple[subprocess.Popen, ProcessIdentity]]:
    """一个仍在运行的"无关进程"及其真实身份。测试结束时无条件结束它（这是测试自己起的进程，按句柄杀，
    不涉及按 PID 误杀）。"""
    proc = _spawn_sleeper()
    try:
        identity = None
        deadline = time.time() + 15
        while identity is None and time.time() < deadline:
            identity = snapshot_process_identity(proc.pid, powershell)
            if identity is None:
                time.sleep(0.2)
        assert identity is not None, "读不到刚起的进程的身份"
        yield proc, identity
    finally:
        proc.kill()
        proc.wait(timeout=15)


def test_exited_process_is_reported_gone_and_nothing_is_killed(powershell: str, bystander) -> None:
    proc_b, identity_b = bystander
    # 起一个只活几秒的进程，趁它还活着记下身份，再等它自然退出：此后 identity.pid 指向一个已退出的进程。
    sleeper = _spawn_sleeper(4)
    try:
        identity = snapshot_process_identity(sleeper.pid, powershell)
        assert identity is not None
    finally:
        sleeper.wait(timeout=60)
    assert probe_process(identity, powershell) == "GONE"
    assert kill_if_same_process(identity, powershell) == "GONE"
    # 无关的活进程不受任何影响。
    assert proc_b.poll() is None


@pytest.mark.parametrize("mutation", ["start_time", "name"])
def test_pid_now_owned_by_a_different_process_is_not_killed(powershell: str, bystander, mutation: str) -> None:
    """等价于 PID 复用：该 PID 现在是个活进程，但映像名或启动时间与当初记下的身份不符。"""
    proc_b, real = bystander
    if mutation == "start_time":
        # 当初记下的是"更早启动的另一个进程"：启动时间早 10 分钟。
        recorded = replace(real, start_ticks=real.start_ticks - 10 * 60 * 10_000_000)
    else:
        recorded = replace(real, name="definitely-not-this-image")
    assert probe_process(recorded, powershell) == "MISMATCH"
    assert kill_if_same_process(recorded, powershell) == "MISMATCH"
    time.sleep(0.5)
    assert proc_b.poll() is None, "身份不符的进程不得被结束"


def test_start_time_tolerance_is_tight(powershell: str, bystander) -> None:
    proc_b, real = bystander
    just_outside = replace(real, start_ticks=real.start_ticks + START_TIME_TOLERANCE_TICKS + 10_000_000)
    assert probe_process(just_outside, powershell) == "MISMATCH"
    assert proc_b.poll() is None


def test_matching_identity_is_the_only_case_that_kills(powershell: str, bystander) -> None:
    proc_b, real = bystander
    # 只查询不结束。
    assert probe_process(real, powershell) == "SAME"
    assert proc_b.poll() is None
    # 身份匹配才结束，并且真的结束了。
    assert kill_if_same_process(real, powershell) == "KILLED"
    proc_b.wait(timeout=15)
    assert proc_b.poll() is not None


def test_missing_identity_kills_nothing(powershell: str, bystander) -> None:
    proc_b, _ = bystander
    assert kill_if_same_process(None, powershell) == "GONE"
    assert proc_b.poll() is None
