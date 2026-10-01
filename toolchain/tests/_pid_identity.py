"""按"进程身份"（映像名 + 启动时间）而不是裸 PID 去查询/结束进程的测试工具。

背景（2026-10-01，bugfix/upm-evidence-stale-pid）：Windows 会复用已退出进程的 PID。测试的
``finally`` 里对"可能已经退出的 PID"无条件执行 ``Stop-Process -Force``，PID 恰好被复用时会杀掉
一个无关进程（门禁运行中被怀疑误杀过 Unity 包管理器子进程）。这里的做法：起进程之后马上记下身份
（映像名 + 启动时间的 UTC ticks），之后任何查询/结束都先在**同一次 PowerShell 调用里**核对身份，
进程不存在 -> GONE、身份不符 -> MISMATCH（都不动它），只有身份一致才返回 SAME / 执行结束并返回
KILLED。启动时间取自内核创建时间，PID 复用后的新进程启动时间必然晚于旧进程退出时间，不可能与
旧身份一致（比较容差 10 ms，只为吸收不同 .NET 运行时对 FILETIME 的舍入差异）。

查询与结束在同一个 PowerShell 进程里连续执行，核对与 ``Stop-Process`` 之间只剩微秒级窗口，
不再有"先问存活、过很久再按 PID 杀"的长窗口。
"""

from __future__ import annotations

import subprocess
from dataclasses import dataclass

from _ps_subprocess_env import clean_powershell_env

# 10 ms，单位 100ns tick。
START_TIME_TOLERANCE_TICKS = 100_000


@dataclass(frozen=True)
class ProcessIdentity:
    pid: int
    name: str
    start_ticks: int


def _run_ps(powershell: str, command: str, timeout: int = 60) -> str:
    result = subprocess.run(
        [powershell, "-NoProfile", "-Command", command],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        env=clean_powershell_env(powershell),
    )
    return result.stdout.strip()


def snapshot_process_identity(pid: int | str, powershell: str) -> ProcessIdentity | None:
    """读取进程当前的身份；进程不存在或读不到启动时间时返回 None。"""
    pid_int = int(pid)
    out = _run_ps(
        powershell,
        f"$p = Get-Process -Id {pid_int} -ErrorAction SilentlyContinue; "
        "if ($null -ne $p) { try { Write-Output ('{0}|{1}' -f $p.ProcessName, $p.StartTime.ToUniversalTime().Ticks) } catch { } }",
    )
    if "|" not in out:
        return None
    name, _, ticks = out.rpartition("|")
    try:
        return ProcessIdentity(pid=pid_int, name=name, start_ticks=int(ticks))
    except ValueError:
        return None


def probe_process(identity: ProcessIdentity, powershell: str, *, kill: bool = False) -> str:
    """核对 ``identity.pid`` 当前是不是当初那个进程。

    返回值：
      - ``"GONE"``：该 PID 现在没有进程（已退出），什么都不做；
      - ``"MISMATCH"``：该 PID 现在是另一个进程（PID 被复用，或读不到启动时间无法确认），**不动它**；
      - ``"SAME"``：仍是当初那个进程（``kill=False``）；
      - ``"KILLED"``：仍是当初那个进程，且已结束（``kill=True``）。
    """
    stop = "Stop-Process -Id $targetPid -Force -Confirm:$false -ErrorAction SilentlyContinue; Write-Output 'KILLED'" if kill else "Write-Output 'SAME'"
    command = (
        f"$targetPid = {identity.pid}; $expectName = '{identity.name.replace(chr(39), chr(39) * 2)}'; $expectTicks = {identity.start_ticks}; "
        "$p = Get-Process -Id $targetPid -ErrorAction SilentlyContinue; "
        "if ($null -eq $p) { Write-Output 'GONE' } else { "
        "$ticks = $null; try { $ticks = $p.StartTime.ToUniversalTime().Ticks } catch { } ; "
        f"if ($null -eq $ticks -or $p.ProcessName -ne $expectName -or [Math]::Abs($ticks - $expectTicks) -gt {START_TIME_TOLERANCE_TICKS}) "
        f"{{ Write-Output 'MISMATCH' }} else {{ {stop} }} }}"
    )
    out = _run_ps(powershell, command)
    last = out.splitlines()[-1].strip() if out else ""
    if last not in {"GONE", "MISMATCH", "SAME", "KILLED"}:
        # 宁可不动：拿不到明确结论就当身份无法确认。
        return "MISMATCH"
    return last


def kill_if_same_process(identity: ProcessIdentity | None, powershell: str) -> str:
    """只在 PID 仍指向当初那个进程时才结束它；``identity`` 为 None（当初就没记下身份）时什么都不做。"""
    if identity is None:
        return "GONE"
    try:
        return probe_process(identity, powershell, kill=True)
    except Exception:
        return "MISMATCH"
