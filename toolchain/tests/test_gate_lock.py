"""门禁独占锁机制（toolchain/_gate_lock.ps1，check.ps1 / build.ps1 -Release 启动时调用）的用例。

不变量（对应"收尾四件"之 1）：
- 锁被别的持有者占着（进程存活）-> 拒绝启动，输出里有持有者与开始时间；
- 同一持有者（参数或环境变量 WS_GATE_LOCK_HOLDER）-> 放行；
- 锁里的 PID 不存在（或同 PID 的进程启动时刻对不上）-> 陈旧，警告后放行/接管；
- 获取锁的进程无论正常结束、`exit N`、未接住的异常都释放，且只释放 PID 是自己的锁；
- check.ps1 / build.ps1 -Release 真实入口在被别人持锁时立即以非 0 退出，不进入门禁。

所有用例用 WS_GATE_LOCK_PATH 指向 tmp_path 下的锁文件，不碰真实的 D:\\wt\\_exclusive_gate.lock。
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import time
from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, TOOLCHAIN_DIR, clean_git_env, find_powershell, ps_quote, run_ps_script
from _ps_subprocess_env import clean_powershell_env

LOCK_SCRIPT = TOOLCHAIN_DIR / "_gate_lock.ps1"


def _lock_env(lock_path: Path, holder_env: str | None = None) -> dict[str, str]:
    env = {"WS_GATE_LOCK_PATH": str(lock_path), "WS_GATE_LOCK_HOLDER": holder_env or ""}
    return env


def _write_lock(path: Path, holder: str, pid: int, started: str = "2026-10-08T09:00:00", pid_start: str | None = None) -> None:
    body: dict[str, object] = {"holder": holder, "started": started, "pid": pid}
    if pid_start is not None:
        body["pidStart"] = pid_start
    path.write_text(json.dumps(body), encoding="utf-8")


def _wait_gone(lock: Path, seconds: float = 20.0) -> bool:
    """释放由隐藏看守进程在持有者退出后完成（约一秒内），轮询等待。"""
    deadline = time.time() + seconds
    while time.time() < deadline:
        if not lock.exists():
            return True
        time.sleep(0.2)
    return not lock.exists()


def _dead_pid() -> int:
    p = subprocess.Popen([sys.executable, "-c", "pass"])
    p.wait()
    return p.pid


def _guard_driver(acquire: str, *, context: str = "测试") -> str:
    return (
        f". {ps_quote(LOCK_SCRIPT)}\n"
        f"$ok = Enter-GateLockGuard -AcquireName {ps_quote(acquire)} -Context {ps_quote(context)}\n"
        "@{ ok = [bool]$ok; holderEnv = \"$env:WS_GATE_LOCK_HOLDER\" } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8\n"
    )


def _run_guard(tmp_path: Path, lock_path: Path, acquire: str, holder_env: str | None = None):
    proc = run_ps_script(tmp_path, _guard_driver(acquire), env_extra=_lock_env(lock_path, holder_env))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    result = json.loads((tmp_path / "driver_result.json").read_text(encoding="utf-8-sig"))
    return proc, result


def test_free_lock_acquire_writes_holder_pid_started_and_releases_on_exit(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    body = (
        f". {ps_quote(LOCK_SCRIPT)}\n"
        "$ok = Enter-GateLockGuard -AcquireName 'agent-a' -Context 'x'\n"
        "$during = Get-Content -Raw -LiteralPath (Get-GateLockPath) | ConvertFrom-Json\n"
        "@{ ok = [bool]$ok; holder = $during.holder; started = $during.started; pid = [int]$during.pid; me = [int]$PID; env = \"$env:WS_GATE_LOCK_HOLDER\" } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8\n"
    )
    proc = run_ps_script(tmp_path, body, env_extra=_lock_env(lock))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    r = json.loads((tmp_path / "driver_result.json").read_text(encoding="utf-8-sig"))
    assert r["ok"] is True
    assert r["holder"] == "agent-a" and r["env"] == "agent-a"
    assert r["pid"] == r["me"] and r["started"]
    assert _wait_gone(lock), "进程结束后锁必须已释放"


def test_other_holder_with_live_pid_is_rejected_and_names_holder_and_started(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    _write_lock(lock, "someone-else", os.getpid(), started="2026-10-08T09:15:00")
    proc, r = _run_guard(tmp_path, lock, acquire="")
    assert r["ok"] is False
    assert "someone-else" in proc.stdout and "2026-10-08T09:15:00" in proc.stdout and str(os.getpid()) in proc.stdout
    assert lock.exists(), "被拒绝的一方不得动别人的锁"
    # 带 -AcquireExclusiveLock 但持有者名不同，同样拒绝
    _, r2 = _run_guard(tmp_path, lock, acquire="me")
    assert r2["ok"] is False
    assert json.loads(lock.read_text(encoding="utf-8"))["holder"] == "someone-else"


def test_same_holder_by_argument_or_environment_is_allowed(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    _write_lock(lock, "agent-a", os.getpid())
    _, by_arg = _run_guard(tmp_path, lock, acquire="agent-a")
    assert by_arg["ok"] is True and by_arg["holderEnv"] == "agent-a"
    _, by_env = _run_guard(tmp_path, lock, acquire="", holder_env="agent-a")
    assert by_env["ok"] is True
    assert lock.exists(), "同一持有者放行不等于获得所有权，别人持有的锁不能被这次调用删掉"


def test_stale_pid_is_taken_over_with_warning_when_acquiring(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    _write_lock(lock, "crashed-agent", _dead_pid())
    body = (
        f". {ps_quote(LOCK_SCRIPT)}\n"
        "$ok = Enter-GateLockGuard -AcquireName 'agent-b' -Context 'x'\n"
        "$during = Get-Content -Raw -LiteralPath (Get-GateLockPath) | ConvertFrom-Json\n"
        "@{ ok = [bool]$ok; holder = $during.holder; pid = [int]$during.pid; me = [int]$PID } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8\n"
    )
    proc = run_ps_script(tmp_path, body, env_extra=_lock_env(lock))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    r = json.loads((tmp_path / "driver_result.json").read_text(encoding="utf-8-sig"))
    assert r["ok"] is True and r["holder"] == "agent-b" and r["pid"] == r["me"]
    assert "[gate-lock:stale]" in proc.stdout and "crashed-agent" in proc.stdout
    assert _wait_gone(lock), "接管者结束后同样释放"


def test_stale_pid_without_acquire_proceeds_with_warning(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    _write_lock(lock, "crashed-agent", _dead_pid())
    proc, r = _run_guard(tmp_path, lock, acquire="")
    assert r["ok"] is True
    assert "[gate-lock:stale]" in proc.stdout


def test_same_pid_with_different_process_start_time_is_stale(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    # PID 是活的（本 pytest 进程），但锁里记的进程启动时刻对不上 = PID 被系统复用，锁是陈旧的。
    _write_lock(lock, "old-agent", os.getpid(), pid_start="2001-01-01T00:00:00.0000000Z")
    proc, r = _run_guard(tmp_path, lock, acquire="")
    assert r["ok"] is True and "[gate-lock:stale]" in proc.stdout


def test_handwritten_text_lock_counts_as_live_and_parses_holder(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    lock.write_text("持有者=agent-x 开始时间=2026-10-08T10:00:00", encoding="utf-8")
    proc, r = _run_guard(tmp_path, lock, acquire="")
    assert r["ok"] is False and "agent-x" in proc.stdout
    _, same = _run_guard(tmp_path, lock, acquire="", holder_env="agent-x")
    assert same["ok"] is True


@pytest.mark.parametrize(
    "tail",
    [
        "",  # 正常结束
        "exit 3",  # exit N
        "throw 'boom'",  # 未接住的异常
    ],
    ids=["normal-exit", "exit-code", "uncaught-exception"],
)
def test_lock_is_released_on_normal_exit_exit_code_and_uncaught_exception(tmp_path: Path, tail: str) -> None:
    lock = tmp_path / "gate.lock"
    body = (
        f". {ps_quote(LOCK_SCRIPT)}\n"
        "if (-not (Enter-GateLockGuard -AcquireName 'agent-c' -Context 'x')) { exit 9 }\n"
        f"if (-not (Test-Path -LiteralPath {ps_quote(lock)})) {{ exit 8 }}\n"
        + tail
        + "\n"
    )
    proc = run_ps_script(tmp_path, body, env_extra=_lock_env(lock))
    assert proc.returncode not in (8, 9), proc.stdout + proc.stderr
    if tail == "exit 3":
        assert proc.returncode == 3
    if tail.startswith("throw"):
        assert proc.returncode != 0
    assert _wait_gone(lock), f"退出方式 {tail or 'normal'} 后锁仍在：{proc.stdout}{proc.stderr}"


def test_release_only_removes_a_lock_owned_by_this_process(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    body = (
        f". {ps_quote(LOCK_SCRIPT)}\n"
        "$null = Enter-GateLockGuard -AcquireName 'agent-d' -Context 'x'\n"
        # 模拟本进程的锁被（陈旧接管）换成了别的进程的锁：退出时不得删它
        f"[System.IO.File]::WriteAllText({ps_quote(lock)}, '{{\"holder\":\"taker\",\"started\":\"2026-10-08T11:00:00\",\"pid\":' + ($PID + 100000) + '}}')\n"
    )
    proc = run_ps_script(tmp_path, body, env_extra=_lock_env(lock))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    time.sleep(4)  # 给看守进程足够时间在持有者退出后动手（它不该删别人的锁）
    assert lock.exists() and json.loads(lock.read_text(encoding="utf-8"))["holder"] == "taker"


def test_acquire_is_exclusive_between_two_processes(tmp_path: Path) -> None:
    """进程 A 持锁期间，真实的第二个 PowerShell 进程来抢被拒绝；A 结束后第二个进程能拿到。"""
    lock = tmp_path / "gate.lock"
    exe = find_powershell()
    env = clean_git_env(clean_powershell_env(exe) or dict(os.environ))
    env.update(_lock_env(lock))
    holder_script = tmp_path / "hold.ps1"
    ready = tmp_path / "ready.flag"
    holder_script.write_bytes(
        b"\xef\xbb\xbf"
        + (
            f". {ps_quote(LOCK_SCRIPT)}\n"
            "if (-not (Enter-GateLockGuard -AcquireName 'agent-a' -Context 'x')) { exit 9 }\n"
            f"Set-Content -LiteralPath {ps_quote(ready)} -Value 1\n"
            "Start-Sleep -Seconds 20\n"
        ).encode("utf-8")
    )
    holder = subprocess.Popen([exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(holder_script)], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    try:
        for _ in range(100):
            if ready.exists():
                break
            holder.poll()
            assert holder.returncode is None, "持锁进程提前退出"
            time.sleep(0.2)
        assert ready.exists()
        _, loser = _run_guard(tmp_path, lock, acquire="agent-b")
        assert loser["ok"] is False
    finally:
        holder.kill()
        holder.wait()
    # 被强杀：看守随后清掉锁（来不及清的话 PID 已死，下一次启动也按陈旧接管）——两条路径下一次启动都能拿到
    _wait_gone(lock, seconds=5)
    proc, taker = _run_guard(tmp_path, lock, acquire="agent-b")
    assert taker["ok"] is True


def _entry_env(exe: str, lock: Path) -> dict[str, str]:
    env = clean_git_env(clean_powershell_env(exe) or dict(os.environ))
    env["WS_GATE_LOCK_PATH"] = str(lock)
    env.pop("WS_GATE_LOCK_HOLDER", None)
    return env


def test_check_ps1_refuses_to_start_when_another_holder_has_the_lock(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    _write_lock(lock, "someone-else", os.getpid(), started="2026-10-08T09:15:00")
    exe = find_powershell()
    proc = subprocess.run(
        [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(REPO_ROOT / "check.ps1"), "-Quick"],
        capture_output=True, text=True, encoding="utf-8", errors="replace", env=_entry_env(exe, lock), timeout=120,
    )
    assert proc.returncode == 1
    assert "[gate-lock:refused]" in proc.stdout and "someone-else" in proc.stdout and "2026-10-08T09:15:00" in proc.stdout
    assert "====" not in proc.stdout, "被拒绝后不得进入任何门禁步骤"


def test_build_release_refuses_to_start_when_another_holder_has_the_lock(tmp_path: Path) -> None:
    lock = tmp_path / "gate.lock"
    _write_lock(lock, "someone-else", os.getpid(), started="2026-10-08T09:15:00")
    exe = find_powershell()
    proc = subprocess.run(
        [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(REPO_ROOT / "build.ps1"), "-Release", "9.9.9", "-DryRun"],
        capture_output=True, text=True, encoding="utf-8", errors="replace", env=_entry_env(exe, lock), timeout=120,
    )
    assert proc.returncode == 1
    assert "[gate-lock:refused]" in proc.stdout and "someone-else" in proc.stdout
