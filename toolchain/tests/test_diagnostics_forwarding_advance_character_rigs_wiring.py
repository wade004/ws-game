"""诊断转发到引擎控制台（feat/diagnostics-console-forward 后续收口）——三处生产入口
``AdvanceCharacterRigs`` 必须一致接入 ``UnityViewFactory.PumpDiagnostics()`` 的一致性回归。

背景：``feat/diagnostics-console-forward``（提交 995d3dc）把 ``SpriteCharacterRig.Diagnostics``
新增的警告转发到引擎控制台，接线点是 ``UnityViewFactory.PumpDiagnostics()``，供每一处生产装配的
``AdvanceCharacterRigs`` 每帧维护步骤调用一次（同一次遍历顺带完成，见该方法判断记录）。该单只接了
``adapters/unity/`` 里的 ``GameFoundationBootstrap``/``FrameworkResidentHost`` 两处同名方法，漏了
``games/_template/Runtime/GameBootstrap.cs`` 的同款方法——按模板起的真实游戏（本类型正是模板的
生产入口，见该文件类型顶部判断记录"游戏层组合根"）反而从未获得这份转发，是三处近似重复代码里
"改了两处、漏了第三处"这一类缺陷的具体案例。

这段接线本身是 Unity 胶水（``UnityViewFactory``/``ViewFactory`` 依赖 ``UnityEngine``），无法在不
起 Unity 的前提下用 dotnet 测其运行期行为（``PresentationDiagnosticsConsoleForwarding.cs`` 抽离出的
纯逻辑——去重网关/LRU/轮询——已在 ``adapters/unity/DiagnosticsForwarding/tests/
PresentationDiagnosticsConsoleForwardingTests.cs`` 用 13 例 xUnit 覆盖，不需要重复）。真正没有任何
自动化覆盖的是"这三处近似重复的方法是否真的都调用了这一行"这件事本身——用 Python 直接解析三份
源码文件的 ``AdvanceCharacterRigs`` 方法体（花括号配对提取，不依赖编译），断言均含
``ViewFactory(!?).PumpDiagnostics()`` 调用，把"三处必须保持同步"做成可重复运行的 pytest 用例，
跟随 ``check.ps1`` 既有的 "toolchain 自身 pytest 套件"步骤一并跑，不需要起 Unity。

反向确认（本文件新增时已执行，证据见任务汇报）：临时删掉
``games/_template/Runtime/GameBootstrap.cs`` 里的 ``ViewFactory.PumpDiagnostics();`` 一行，本文件的
``test_game_bootstrap_template_advance_character_rigs_calls_pump_diagnostics`` 必然失败（报出缺失
调用的具体文件路径），确认后已还原该行，不留痕。

Unity 侧胶水（``UnityViewFactory.PumpDiagnostics`` 是否真的经 ``Debug.LogWarning`` 写入控制台）
仍待主会话跑引擎门禁确认——本文件只保证"三处调用点存在且一致"这一件事，不替代真实 Unity 批处理
验证。

运行：``python -m pytest toolchain/tests/test_diagnostics_forwarding_advance_character_rigs_wiring.py -q``
或作为 ``toolchain`` 套件的一部分：``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent

_PUMP_CALL_RE = re.compile(r"ViewFactory!?\.PumpDiagnostics\(\)\s*;")

# presentation/assembly/README.md 判断记录 10 跟进：VfxPlayer/SfxPlayer/FeedbackBinder（含
# HitFrameSyncPolicy）/ViewBinder 四条新增诊断源经 PresentationAssemblyDiagnosticsForwarder 轮询
# 转发，接线点同样是三处 AdvanceCharacterRigs（紧跟 ViewFactory.PumpDiagnostics() 之后）——同一类
# "三处近似重复代码，改了两处漏第三处"的缺陷模式，一并做成回归断言，不能只靠新增当轮人工核对。
_PRESENTATION_DIAGNOSTICS_PUMP_CALL_RE = re.compile(r"_presentationDiagnosticsForwarder\?\.Pump\(\)\s*;")

# 三处生产入口的 AdvanceCharacterRigs 同名方法，均按"推进仍存活 rig -> 顺带转发诊断"同一套惯例
# 接线（见 UnityViewFactory.PumpDiagnostics 判断记录"本工厂是唯一持有 SpriteCharacterRig 引用的
# adapters/unity 代码"）。三项路径均为仓库相对路径，跨平台统一用正斜杠。
_SOURCE_FILES = (
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs",
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs",
    "games/_template/Runtime/GameBootstrap.cs",
)

_METHOD_SIGNATURE_RE = re.compile(r"private\s+void\s+AdvanceCharacterRigs\s*\(\s*\)")


def _extract_method_body(text: str, rel_path: str) -> str:
    """按花括号配对提取 ``AdvanceCharacterRigs()`` 方法体全文（含花括号本身），不依赖编译——
    只做纯文本扫描，找到方法签名后从第一个 ``{`` 开始配对计数直到归零。"""
    match = _METHOD_SIGNATURE_RE.search(text)
    assert match is not None, (
        f"{rel_path}: 未找到 `private void AdvanceCharacterRigs()` 方法签名——"
        "方法可能被重命名/删除/改了访问修饰符，需要先确认这三处近似重复方法是否仍然存在，"
        "而不是直接假设本检查过期。"
    )

    body_start = text.index("{", match.end())
    depth = 0
    i = body_start
    while i < len(text):
        ch = text[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[body_start : i + 1]
        i += 1

    raise AssertionError(f"{rel_path}: `AdvanceCharacterRigs()` 方法体花括号未配对（文件可能被截断）")


def _read_source(rel_path: str) -> str:
    abs_path = REPO_ROOT / rel_path
    assert abs_path.exists(), f"{rel_path}: 文件不存在——路径本身可能已经过期，需要先核实"
    return abs_path.read_text(encoding="utf-8")


@pytest.mark.parametrize("rel_path", _SOURCE_FILES)
def test_advance_character_rigs_calls_pump_diagnostics(rel_path: str) -> None:
    text = _read_source(rel_path)
    body = _extract_method_body(text, rel_path)
    assert _PUMP_CALL_RE.search(body), (
        f"{rel_path}: `AdvanceCharacterRigs()` 方法体未调用 `ViewFactory.PumpDiagnostics()`——"
        "三处近似重复的生产入口必须保持一致接线（见 UnityViewFactory.PumpDiagnostics 判断记录），"
        "遗漏这一行会让该入口下真实游戏的诊断转发静默失效，控制台不会输出任何 "
        "SpriteCharacterRig.Diagnostics 警告。"
    )


@pytest.mark.parametrize("rel_path", _SOURCE_FILES)
def test_advance_character_rigs_calls_presentation_diagnostics_forwarder_pump(rel_path: str) -> None:
    text = _read_source(rel_path)
    body = _extract_method_body(text, rel_path)
    assert _PRESENTATION_DIAGNOSTICS_PUMP_CALL_RE.search(body), (
        f"{rel_path}: `AdvanceCharacterRigs()` 方法体未调用 `_presentationDiagnosticsForwarder?.Pump()`"
        "——presentation/assembly/README.md 判断记录 10 新增的 Vfx/Sfx/Feedback/ViewBinder 四条诊断"
        "链路转发同样要求三处生产入口保持一致接线，遗漏这一行会让该入口下这四条链路的诊断转发静默"
        "失效。"
    )


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-v"]))
