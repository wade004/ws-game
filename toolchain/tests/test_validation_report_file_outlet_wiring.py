"""ADR-0047 运行期校验报告落盘出口——四处已知校验点必须保持一致接线的回归。

背景：`toolchain/tests/test_diagnostics_forwarding_advance_character_rigs_wiring.py` 的判断记录已经
记录过同一类教训——本仓库有若干处"近似重复代码，多处生产入口必须保持一致接线"的模式（如三处
`AdvanceCharacterRigs()`），改了一处漏了另一处会让某个入口下的行为静默失效，且没有编译错误提示。
本次新增的 `Adapter.Unity.Diagnostics.ValidationReportFileOutlet`（ADR-0047）接入了四个已知校验点
（`games/_template/Runtime/GameBootstrap.cs`、`games/_template/Runtime/DataHotReload.cs`、
`adapters/unity/.../Runtime/Bootstrap/GameFoundationBootstrap.cs`、
`adapters/unity/.../Runtime/Shell/FrameworkResidentHost.cs`），属于同一类风险——四处代码物理上互不
调用彼此，纯靠人工记忆保持"每次拿到 `ValidationReport` 都落盘一次"这条约定容易漏改。

本文件与 `test_diagnostics_forwarding_advance_character_rigs_wiring.py` 同一手法：直接用正则在源码
文本里查找关键调用点，不依赖编译，跟随 `check.ps1` 的 "toolchain 自身 pytest 套件" 步骤一并跑。

顺带覆盖 ADR-0047 任务书要求"顺带修掉的两处同类缺陷"（`GameFoundationBootstrap.cs`/
`FrameworkResidentHost.cs` 校验失败日志此前误用 `Debug.LogError`，违反 ADR-0042 决策 4）——本文件
断言"数据集校验未通过"那一行日志用的是 `Debug.LogWarning`，不是 `Debug.LogError`，四处一致（另外
两处 `GameBootstrap.cs`/`DataHotReload.cs` 已在 ADR-0046 修正，这里一并纳入回归，防止未来被改回去）。

运行：``python -m pytest toolchain/tests/test_validation_report_file_outlet_wiring.py -q``
或作为 ``toolchain`` 套件的一部分：``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent

# 三处在自己的装配方法里直接调用 registry.LoadAll(...) 的"启动校验"入口：各自都必须①解析一次落盘
# 路径选项、②不论通过/阻断都调用 WriteIfConfigured、③来源标识固定为 Startup。
_STARTUP_SOURCE_FILES = (
    "games/_template/Runtime/GameBootstrap.cs",
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs",
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs",
)

# 唯一的热重载校验点：不重新解析选项（复用 GameBootstrap 已解析好的路径，见该文件构造/Initialize
# 调用点判断记录），来源标识固定为 HotReload。
_HOT_RELOAD_SOURCE_FILE = "games/_template/Runtime/DataHotReload.cs"

# 四处校验点共同的日志行——判断记录（ADR-0047 顺带修复项 + ADR-0046 既有回归）：一律必须是
# Debug.LogWarning，不能是 Debug.LogError（ADR-0042 决策 4 硬约束：诊断消息一律不产生
# Debug.LogError，否则会让宿主自动化测试框架把预期内的校验失败误判为测试用例失败）。
_ALL_VALIDATION_LOG_SOURCE_FILES = _STARTUP_SOURCE_FILES + (_HOT_RELOAD_SOURCE_FILE,)

_RESOLVE_PATH_CALL_RE = re.compile(r"ValidationReportFileOutlet\.ResolvePath\(")
_WRITE_IF_CONFIGURED_CALL_RE = re.compile(r"ValidationReportFileOutlet\.WriteIfConfigured\(")
_STARTUP_SOURCE_CONST_RE = re.compile(r"ValidationReportTriggerSource\.Startup")
_HOT_RELOAD_SOURCE_CONST_RE = re.compile(r"ValidationReportTriggerSource\.HotReload")

# "数据集校验未通过"（GameFoundationBootstrap.cs/FrameworkResidentHost.cs）与"重载...失败"
# （DataHotReload.cs，热重载场景消息文案不同，见该文件既有判断记录）两种既有文案，统一按"这一行
# 是不是 LogError"判断，不要求文案完全一致。
_VALIDATION_FAILURE_LOG_LINE_RE = re.compile(
    r"Debug\.Log(Warning|Error)\(\s*\$?\"\[[A-Za-z]+\]\s*(数据集校验未通过|热重载[\s\S]*?失败)"
)


def _read_source(rel_path: str) -> str:
    abs_path = REPO_ROOT / rel_path
    assert abs_path.exists(), f"{rel_path}: 文件不存在——路径本身可能已经过期，需要先核实"
    return abs_path.read_text(encoding="utf-8")


@pytest.mark.parametrize("rel_path", _STARTUP_SOURCE_FILES)
def test_startup_entry_point_resolves_validation_report_path(rel_path: str) -> None:
    text = _read_source(rel_path)
    assert _RESOLVE_PATH_CALL_RE.search(text), (
        f"{rel_path}: 未找到 `ValidationReportFileOutlet.ResolvePath(` 调用——ADR-0047 要求每个启动"
        "校验入口自行解析一次落盘路径选项（未配置时为 null，见该类型 WriteIfConfigured 判断记录），"
        "遗漏这一行会让该入口下 -gfValidationReportPath/GF_VALIDATION_REPORT_PATH 选项静默失效。"
    )


@pytest.mark.parametrize("rel_path", _STARTUP_SOURCE_FILES)
def test_startup_entry_point_writes_validation_report_with_startup_source(rel_path: str) -> None:
    text = _read_source(rel_path)
    assert _WRITE_IF_CONFIGURED_CALL_RE.search(text), (
        f"{rel_path}: 未找到 `ValidationReportFileOutlet.WriteIfConfigured(` 调用——ADR-0047 要求"
        "每次启动校验（不论通过/阻断）都落盘一次，遗漏这一行会让该入口下运行期校验报告永远不会"
        "出现在落盘文件里，独立进程消费方（如编辑器）读到的会是过期或缺失的内容。"
    )
    assert _STARTUP_SOURCE_CONST_RE.search(text), (
        f"{rel_path}: 未找到 `ValidationReportTriggerSource.Startup`——启动校验入口的落盘来源标识"
        "必须固定为 Startup，不能省略/写成任意字符串，否则消费方无法按来源过滤/展示。"
    )


def test_hot_reload_entry_point_writes_validation_report_with_hot_reload_source() -> None:
    text = _read_source(_HOT_RELOAD_SOURCE_FILE)
    assert _WRITE_IF_CONFIGURED_CALL_RE.search(text), (
        f"{_HOT_RELOAD_SOURCE_FILE}: 未找到 `ValidationReportFileOutlet.WriteIfConfigured(` 调用——"
        "ADR-0047 要求热重载校验（不论通过/阻断）也落盘一次，遗漏这一行会让开发期热重载触发的校验"
        "结果永远不会出现在落盘文件里。"
    )
    assert _HOT_RELOAD_SOURCE_CONST_RE.search(text), (
        f"{_HOT_RELOAD_SOURCE_FILE}: 未找到 `ValidationReportTriggerSource.HotReload`——热重载校验"
        "入口的落盘来源标识必须固定为 HotReload，不能与启动校验混用同一个来源标识，否则消费方无法"
        "区分这次结果是启动产生的还是热重载产生的。"
    )


@pytest.mark.parametrize("rel_path", _ALL_VALIDATION_LOG_SOURCE_FILES)
def test_validation_failure_log_line_uses_log_warning_not_log_error(rel_path: str) -> None:
    text = _read_source(rel_path)
    match = _VALIDATION_FAILURE_LOG_LINE_RE.search(text)
    assert match is not None, (
        f"{rel_path}: 未找到校验失败日志行（`Debug.LogWarning`/`Debug.LogError` + "
        "\"数据集校验未通过\"/\"热重载...失败\" 文案）——日志文案可能已被改写，需要先确认这条既有"
        "断言是否已经过期，而不是直接假设本检查过期。"
    )
    assert match.group(1) == "Warning", (
        f"{rel_path}: 校验失败日志用的是 `Debug.LogError`，违反 ADR-0042 决策 4 硬约束（诊断消息"
        "一律不产生 Debug.LogError，否则宿主自动化测试框架会把预期内的校验失败误判为测试用例"
        "失败）——ADR-0046/ADR-0047 已把四处校验点统一改为 Debug.LogWarning，这里回归说明被改回去了。"
    )


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-v"]))
