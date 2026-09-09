"""`toolchain/registry/start_registry.ps1` 的 -Stop 误拒回归测试（2026-09-10 根治）。

背景（实测复现，证据落盘于本次修复的会话记录，未随代码提交）：`-Detach` 就绪后按 -Listen 端口
核实一次真正监听的 PID（脚本头判断记录二"双保险"），与 `Start-Process` 记录的 PID 不一致时以
端口查到的为准重写 `-PidFile` 与配套的 `<PidFile>.meta.json`。此前的重写分支在重写的同时重新
取一次 `[datetime]::UtcNow` 写进 `pid_file_written_at_utc`（记为 T2），而不是沿用本次 `-Detach`
调用最初写 PID 文件时记录的时刻（记为 T1）。触发条件并不罕见——只要 `-Detach` 在端口已经被占用
的情况下再调用一次（例如前一次 `-Detach` 忘了先 `-Stop` 就再跑一次），本次 `Start-Process` 起的
新进程会因端口占用而绑定失败随即退出，但 `/-/ping` 命中的仍是早先已经在监听、真正提供服务的旧
进程，从而触发重写分支：把旧进程的 PID 连同"重写发生的当下"（T2）一起写回。旧进程的真实启动时刻
S 必然早于 T2（往往早几分钟，远超 `Test-VerdaccioProcessIdentity` 条件 (c) 2 秒的时钟粒度容差），
导致任意一次后续 `-Status`/`-Stop` 都把这个货真价实、仍在正常服务的 Verdaccio 实例误判成"PID 被
系统复用给了另一个更早启动的无关进程"而拒绝停止——`-Stop` 以非零退出码结束，端口和 PID 文件都
没有被清理。

根治：重写分支不再重新取 `UtcNow`，沿用本次调用最初写 PID 文件时记录的 `$pidWrittenAtUtc`（T1）。
T1 产生于本次调用 `Start-Process` 之后，早于或约等于真正监听端口的那个进程的启动时刻，用它做条件
(c) 的基准既不会误伤本次调用期间已经在跑、真正由本脚本管理的 Verdaccio 实例，也仍然能拦住"PID
被复用给一个在 T1 之前就已启动的更早无关进程"这一条件 (c) 原本要防的场景。见脚本头判断记录四。

本文件两层覆盖：

1. `test_rewrite_branch_reuses_initial_timestamp_statically`：静态断言——定位脚本里含"已按端口
   监听结果重写 PID 文件"提示文案的代码块，从这里到写元数据的 `Write-VerdaccioIdentityMeta` 调用
   之间不允许再出现 `[datetime]::UtcNow`（不允许重新取当下时刻）；另外断言全文件里
   `$pidWrittenAtUtc = [datetime]::UtcNow` 这一赋值只出现一次（只在本次调用最初写 PID 文件时取
   一次，重写分支必须复用同一个变量，而不是另开一次赋值）。不依赖 Windows/PowerShell 宿主，任何
   平台都能跑。

2. `test_detach_twice_then_stop_succeeds`：行为级回归，实测复现"连续两次 -Detach 触发重写分支"
   这一真实触发路径，再验证修复后 `-Status`/`-Stop` 都不再误判、`-Stop` 能把仍在监听的真实进程
   干净停掉、端口释放、PID 文件与元数据文件都被清理。用临时目录里的最小配置（storage/htpasswd
   都指向 `tmp_path`，避免污染仓库内 `toolchain/registry/` 下的真实 storage/htpasswd）与随机空闲
   端口，不影响仓库内任何真实私服实例；无论断言是否通过都在 `finally` 里尽力强杀残留进程。
   Windows-only（依赖 `Start-Process`/`Get-NetTCPConnection`/`Win32_Process`），且要求本机已经
   `npm ci` 过 `toolchain/registry/node_modules`（不在测试里现跑 `npm ci`，避免引入网络依赖）；
   条件不满足时跳过并说明原因。

运行：

```
python -m pytest toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py -q
```

或作为 `toolchain` 套件的一部分：`python -m pytest toolchain/tests -q`。
"""

from __future__ import annotations

import locale
import re
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "toolchain" / "registry" / "start_registry.ps1"
REGISTRY_DIR = SCRIPT_PATH.parent
VERDACCIO_BIN = REGISTRY_DIR / "node_modules" / "verdaccio" / "bin" / "verdaccio"

REWRITE_MARKER = "已按端口监听结果重写 PID 文件"
META_WRITE_CALL = "Write-VerdaccioIdentityMeta"
UTCNOW_TOKEN = "[datetime]::UtcNow"


def _script_text() -> str:
    # utf-8-sig 自动剥离 BOM，仓库门禁（test_powershell_scripts_ansi_safe.py）已保证本文件带 BOM。
    return SCRIPT_PATH.read_text(encoding="utf-8-sig")


def test_rewrite_branch_reuses_initial_timestamp_statically() -> None:
    """重写分支不得重新取 UtcNow；`$pidWrittenAtUtc = [datetime]::UtcNow` 全文件只应出现一次
    （首次写 PID 文件那一处），重写分支必须复用同一个变量。不依赖任何 PowerShell 宿主。
    """
    text = _script_text()

    marker_idx = text.find(REWRITE_MARKER)
    assert marker_idx != -1, (
        f"脚本里找不到提示文案 {REWRITE_MARKER!r}，重写分支可能被改名/删除，"
        "本测试的定位方式需要同步更新"
    )

    meta_call_idx = text.find(META_WRITE_CALL, marker_idx)
    assert meta_call_idx != -1, (
        f"在 {REWRITE_MARKER!r} 之后找不到 {META_WRITE_CALL!r} 调用，重写分支的结构可能已改变"
    )

    # 重写分支的代码块：从提示文案所在行到写元数据调用结束（含该行）。
    block_end = text.find("\n", meta_call_idx)
    if block_end == -1:
        block_end = len(text)
    rewrite_block = text[marker_idx:block_end]

    assert UTCNOW_TOKEN not in rewrite_block, (
        "重写分支（'已按端口监听结果重写 PID 文件' 到 Write-VerdaccioIdentityMeta 调用之间）"
        f"不应再出现 {UTCNOW_TOKEN}——应当复用本次调用最初写 PID 文件时记录的 $pidWrittenAtUtc"
        "（T1），而不是重新取当下时刻（T2），否则会导致条件 (c) 误判真实进程为无关进程。"
        f"\n实际代码块：\n{rewrite_block}"
    )

    # 全文件只应有一次 "$pidWrittenAtUtc = [datetime]::UtcNow" 赋值（首次写 PID 文件时）。
    assignment_pattern = re.compile(
        re.escape("$pidWrittenAtUtc") + r"\s*=\s*" + re.escape(UTCNOW_TOKEN)
    )
    assignments = assignment_pattern.findall(text)
    assert len(assignments) == 1, (
        f"预期 '$pidWrittenAtUtc = {UTCNOW_TOKEN}' 全文件只出现一次（首次写 PID 文件时），"
        f"实际出现 {len(assignments)} 次——重写分支不应该另开一次赋值。"
    )


# ---------------------------------------------------------------------------
# 行为级回归：实测复现"连续两次 -Detach 触发重写分支"，验证修复后 -Status/-Stop 不再误判。
# ---------------------------------------------------------------------------

pytestmark_behavior = pytest.mark.skipif(
    sys.platform != "win32", reason="依赖 Windows 的 Start-Process/Get-NetTCPConnection"
)


def _find_powershell() -> str | None:
    for candidate in ("powershell.exe", "powershell", "pwsh.exe", "pwsh"):
        path = shutil.which(candidate)
        if path:
            return path
    return None


def _free_tcp_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        s.listen(1)
        return s.getsockname()[1]


def _run(args: list[str], powershell: str, timeout: int, tmp_path: Path, tag: str) -> subprocess.CompletedProcess:
    """判断记录：-Detach 启动的 verdaccio 是长驻的孙进程，Windows 下 `Start-Process` 若没有显式
    限制句柄继承，孙进程可能继承 Python 用来捕获 powershell.exe（本次调用的直接子进程）stdout/
    stderr 的匿名管道写端——即便 powershell.exe 本身已经跑完退出，Python 侧 `subprocess.run(...,
    capture_output=True)` 的阻塞读仍然等不到 EOF（管道写端还被那个长驻的孙进程攥着），整个调用
    挂起，直到那个长驻进程自己退出为止（实测复现：第一次 -Detach 之后调用直接卡死，几分钟不返回，
    此时唯一还活着的相关进程只有 verdaccio 自己）。改为把 stdout/stderr 重定向到临时文件而不是
    管道——避免这条继承链，调用完成后再读文件内容还原成字符串，行为对断言透明。
    """
    cmd = [
        powershell,
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(SCRIPT_PATH),
    ] + args
    stdout_path = tmp_path / f"_run_{tag}.stdout.log"
    stderr_path = tmp_path / f"_run_{tag}.stderr.log"
    with open(stdout_path, "wb") as stdout_f, open(stderr_path, "wb") as stderr_f:
        completed = subprocess.run(
            cmd,
            stdout=stdout_f,
            stderr=stderr_f,
            timeout=timeout,
        )
    # 判断记录：直接把 stdout/stderr 重定向到文件句柄（而不是走 capture_output 的管道 +
    # text=True/encoding="utf-8"）时，Windows PowerShell 5.1 实测按系统 ANSI 代码页
    # （本机 GBK/cp936）写字节，不是 UTF-8——固定用 "utf-8" 解码会把中文提示文案（例如重写分支
    # 的提示）解码成替换字符，导致按中文子串断言必然落空。改用 `locale.getpreferredencoding()`
    # （反映的正是同一个系统 ANSI 代码页）解码，errors="replace" 兜底任何解不出的字节。
    console_encoding = locale.getpreferredencoding(False)
    stdout_text = stdout_path.read_text(encoding=console_encoding, errors="replace")
    stderr_text = stderr_path.read_text(encoding=console_encoding, errors="replace")
    return subprocess.CompletedProcess(cmd, completed.returncode, stdout_text, stderr_text)


def _process_alive(pid: str, powershell: str) -> bool:
    result = subprocess.run(
        [
            powershell,
            "-NoProfile",
            "-Command",
            f"if (Get-Process -Id {pid} -ErrorAction SilentlyContinue) "
            "{ Write-Output 'ALIVE' } else { Write-Output 'GONE' }",
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=30,
    )
    return "ALIVE" in result.stdout


def _kill_pid_best_effort(pid: str, powershell: str) -> None:
    try:
        subprocess.run(
            [
                powershell,
                "-NoProfile",
                "-Command",
                f"Stop-Process -Id {pid} -Force -Confirm:$false -ErrorAction SilentlyContinue",
            ],
            capture_output=True,
            text=True,
            timeout=30,
        )
    except Exception:
        pass


@pytest.mark.skipif(sys.platform != "win32", reason="依赖 Windows 的 Start-Process/Get-NetTCPConnection")
def test_detach_twice_then_stop_succeeds(tmp_path: Path) -> None:
    """连续两次 -Detach（第二次因端口已被占用而触发重写分支）后，-Status/-Stop 都不应把仍在监听的
    真实进程误判为无关进程；-Stop 必须成功停止、端口释放、PID 文件与元数据文件都被清理。
    """
    powershell = _find_powershell()
    if not powershell:
        pytest.skip("找不到 powershell.exe 或 pwsh")
    if not VERDACCIO_BIN.exists():
        pytest.skip(
            f"本机 {VERDACCIO_BIN} 不存在（尚未 npm ci 安装 verdaccio 依赖），"
            "行为级回归依赖真实进程，测试里不现跑 npm ci（避免引入网络依赖），跳过"
        )

    storage_dir = tmp_path / "storage"
    storage_dir.mkdir()
    htpasswd_path = tmp_path / "htpasswd"
    htpasswd_path.write_text("", encoding="utf-8")
    config_path = tmp_path / "config.yaml"
    # 最小可用配置：storage/htpasswd 用绝对路径指向 tmp_path，避免相对路径解析到
    # start_registry.ps1 的 -WorkingDirectory（即 toolchain/registry/ 本身）污染仓库内真实文件。
    config_path.write_text(
        "storage: " + storage_dir.as_posix() + "\n"
        "auth:\n"
        "  htpasswd:\n"
        "    file: " + htpasswd_path.as_posix() + "\n"
        "    max_users: -1\n"
        "packages:\n"
        "  '**':\n"
        "    access: $all\n"
        "    publish: $authenticated\n"
        "    unpublish: $authenticated\n"
        "log: { type: stdout, format: pretty, level: warn }\n",
        encoding="utf-8",
    )

    pid_file = tmp_path / "verdaccio.pid"
    meta_file = tmp_path / "verdaccio.pid.meta.json"
    port = _free_tcp_port()
    listen = f"127.0.0.1:{port}"
    common_args = [
        "-ConfigPath", str(config_path),
        "-Listen", listen,
        "-PidFile", str(pid_file),
        "-SkipInstall",
    ]

    first_pid = ""
    try:
        # 第一次 -Detach：正常起一个真正监听端口的实例，不应触发重写分支。
        result1 = _run(["-Detach"] + common_args, powershell, timeout=60, tmp_path=tmp_path, tag="detach1")
        assert result1.returncode == 0, result1.stdout + result1.stderr
        assert REWRITE_MARKER not in result1.stdout, (
            "第一次 -Detach 不应触发重写分支：\n" + result1.stdout
        )
        assert pid_file.is_file(), "第一次 -Detach 后 PID 文件未生成"
        first_pid = pid_file.read_text(encoding="utf-8-sig").strip()
        assert _process_alive(first_pid, powershell), f"第一次 -Detach 拉起的 PID {first_pid} 未存活"

        # 第二次 -Detach：端口已被第一次的进程占用，新进程绑定失败随即退出，/-/ping 命中旧进程，
        # 触发重写分支——这正是原缺陷的真实触发路径。
        result2 = _run(["-Detach"] + common_args, powershell, timeout=60, tmp_path=tmp_path, tag="detach2")
        assert result2.returncode == 0, result2.stdout + result2.stderr
        assert REWRITE_MARKER in result2.stdout, (
            "第二次 -Detach（端口已被占用）预期触发重写分支，但没有观察到提示文案，"
            "复现路径可能已经失效，需要重新核实触发条件：\n" + result2.stdout
        )
        assert meta_file.is_file(), "重写分支后元数据文件应当存在"
        rewritten_pid = pid_file.read_text(encoding="utf-8-sig").strip()
        assert rewritten_pid == first_pid, (
            f"重写后 PID 文件应仍记录第一次真正监听端口的 PID {first_pid}，"
            f"实际为 {rewritten_pid}"
        )

        # 拉开一点时间差，让"若误判"的场景更明显（原缺陷即便间隔很短也可能因为超过 2 秒容差而误判，
        # 这里额外等待放大差距，避免因为两次调用间隔过短导致测试本身对缺陷不敏感）。
        time.sleep(2.5)

        status_result = _run(["-Status", "-Listen", listen, "-PidFile", str(pid_file)], powershell, timeout=30, tmp_path=tmp_path, tag="status")
        assert status_result.returncode == 0, status_result.stdout + status_result.stderr
        assert "身份核验未通过" not in status_result.stdout, (
            "-Status 不应把仍在监听的真实进程误判为身份核验未通过：\n" + status_result.stdout
        )
        assert "本脚本管理的 Verdaccio" in status_result.stdout, status_result.stdout

        stop_result = _run(["-Stop", "-Listen", listen, "-PidFile", str(pid_file)], powershell, timeout=30, tmp_path=tmp_path, tag="stop")
        assert stop_result.returncode == 0, (
            "-Stop 预期成功（修复重写分支的时间戳后不应再误判），实际失败：\n"
            + stop_result.stdout + stop_result.stderr
        )
        assert "身份核验未通过" not in stop_result.stdout, stop_result.stdout
        assert "已停止" in stop_result.stdout, stop_result.stdout

        assert not pid_file.exists(), "-Stop 成功后 PID 文件应被清理"
        assert not meta_file.exists(), "-Stop 成功后元数据文件应被清理"
        assert not _process_alive(first_pid, powershell), f"PID {first_pid} 在 -Stop 后仍存活"

        # 端口应已释放。
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            s.settimeout(1)
            result = s.connect_ex(("127.0.0.1", port))
            assert result != 0, f"端口 {port} 在 -Stop 后仍可连接，未真正释放"
    finally:
        if first_pid:
            _kill_pid_best_effort(first_pid, powershell)
