"""toolchain/tests 下所有会启动 PowerShell 子进程的用例共用的环境变量辅助（2026-09-20，
test_real_baseline_end_to_end_via_abi_probe 环境依赖修复）。

根因（已用最小复现锁定，见下方判断记录小节）：本仓库的开发/门禁环境普遍是从 PowerShell 7
（`pwsh`，本机为 MSIX/WindowsApps 打包安装）里拉起 Windows PowerShell 5.1（`powershell.exe`）
子进程去跑各类 `.ps1` 门禁测试。Python `subprocess.run` 对子进程做的是原始 CreateProcess，会把
父进程（pwsh 7）自己的 `PSModulePath`（其中含 PowerShell 7 专属的模块目录，包含一条 MSIX/
WindowsApps 打包路径）原样继承给子进程；Windows PowerShell 5.1 在这个外来模块目录下做命令自动
发现（给 `Get-FileHash` 这类内置 cmdlet 触发的模块自动加载）时会内部命中一个非终止性异常，
被调用脚本一旦像本仓库门禁脚本的常规写法那样设置了 `$ErrorActionPreference = "Stop"`（见
AGENTS.md §3"保持确定性"对可预测失败行为的要求），这个非终止性异常会被提升为终止性异常，导致
整个命令自动发现流程中止，连内置的 `Get-FileHash` 都解析不到
（`CommandNotFoundException: The term 'Get-FileHash' is not recognized ...`）。

判断记录（根治方向选择——为什么是"测试侧显式给子进程一个干净的 PSModulePath"，不是另外两个候选）：
  1. 已用最小复现确认：pwsh 自己的 `&` 调用运算符调起 `powershell.exe` 时会把子进程的
     `PSModulePath` 换成 Windows PowerShell 5.1 的原生默认值（不含 PowerShell 7 的模块目录）；
     `check.ps1` 的 ABI 探针步骤正是用 `& powershell -NoProfile ... -Command ...` 这种形状起
     子进程（见 check.ps1 该步骤判断记录），因此不受这个问题影响。本文件把测试侧的调用方式换成
     "显式给一个确定、干净的 PSModulePath"，效果上与 `check.ps1` 已经在用的调用方式对齐，让两者
     不再因为"父进程是哪个 shell"而分道扬镳，而不是去反向工程/依赖 pwsh `&` 运算符具体做了什么
     内部过滤（未公开、不构成契约，不应该被动依赖）。
  2. 没有选择"统一测试侧也改用 pwsh 起子进程"：本仓库的 `toolchain/*.ps1` 脚本兼容目标本来就
     包含 Windows PowerShell 5.1（下游游戏仓库消费方的典型环境，见 `toolchain/get_framework.ps1`
     判断记录），相关测试（如 ANSI 代码页解析、`-File` 场景下的 `[bool]` 参数绑定行为，见
     check.ps1 判断记录）必须能在真实的 5.1 宿主下验证，不能用 pwsh 替代验证对象。
  3. 没有选择"在 abi_probe.ps1/get_framework.ps1 等生产脚本内部加防御"：这些脚本的实际调用方
     （`check.ps1`/`build.ps1`）已经用 `& powershell ...` 规避了这个问题，不需要再改；
     `get_framework.ps1` 早先为兼容"下游消费方自己的运行环境不可控"这个更宽的问题已经内联了不
     依赖 `Get-FileHash` 的兜底哈希函数（`toolchain/_hash.ps1` 的 `Get-Sha256FileHash`，见该脚本
     判断记录），但那是为下游消费方兜底、职责边界不同——本仓库内部的 pytest 用例完全知道自己要
     调用哪个宿主、需要什么环境，不存在"调用方环境不可控"的理由，把测试环境问题也顺带塞进生产
     脚本只会徒增生产代码分支、模糊两类问题的边界。

用法：`subprocess.run([resolved_exe, ...], env=clean_powershell_env(resolved_exe), ...)`。
"""

from __future__ import annotations

import os
from pathlib import Path


def _is_windows_powershell_5_1(resolved_exe: str) -> bool:
    """按可执行文件名判断 resolved_exe 是 Windows PowerShell 5.1（`powershell`/`powershell.exe`）
    还是 PowerShell 7（`pwsh`/`pwsh.exe`）——不依赖调用方传入的是绝对路径还是裸命令名、大小写。
    """
    return Path(resolved_exe).stem.lower() == "powershell"


def clean_powershell_env(resolved_exe: str) -> dict[str, str] | None:
    """返回给 `subprocess.run(..., env=...)` 用的环境变量字典。

    - `resolved_exe` 判定为 Windows PowerShell 5.1 时：返回一份该宿主自身原生默认的
      `PSModulePath`（只含 5.1 自己的三个标准模块目录，不含调用方进程可能带的 PowerShell 7
      模块目录），其余环境变量原样继承调用方当前进程的 `os.environ`——保证子进程行为不再依赖
      "这次是从哪个 shell 里启动 pytest 的"。
    - 判定为其它宿主（如 pwsh）时：返回 `None`，调用方应原样传给 `subprocess.run` 的 `env`
      参数（即不覆盖，保持默认的继承行为）——目前没有复现证据表明 pwsh 目标存在同类问题，不做
      未经验证的改动。
    """
    if not _is_windows_powershell_5_1(resolved_exe):
        return None
    env = dict(os.environ)
    system_root = os.environ.get("SystemRoot") or os.environ.get("SYSTEMROOT") or r"C:\Windows"
    program_files = os.environ.get("ProgramFiles") or r"C:\Program Files"
    module_dirs = []
    user_profile = os.environ.get("USERPROFILE")
    if user_profile:
        module_dirs.append(str(Path(user_profile) / "Documents" / "WindowsPowerShell" / "Modules"))
    module_dirs.append(str(Path(program_files) / "WindowsPowerShell" / "Modules"))
    module_dirs.append(str(Path(system_root) / "System32" / "WindowsPowerShell" / "v1.0" / "Modules"))
    env["PSModulePath"] = ";".join(module_dirs)
    return env
