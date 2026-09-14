#!/usr/bin/env python3
"""Unity 测试结果分诊脚本（排查复盘 2026-09-15 落地，见
``architecture/落地计划/排查复盘-2026-09-15-PlayMode-PRES180.md``）。

背景：一次 PlayMode 全量门禁稳定失败（``VerticalSliceTests.PRES180_...``）耗时约 3.5 小时、
约 156 万 token 才查明真因——玩家资源池跨用例未回满，导致 PRES180 的攻击断言真失败，但
Unity Test Framework 的 ``UnityLogCheckDelegatingCommand`` 在断言异常之后仍执行
``CheckLogs``，两条 ``LogAssert.Expect`` 落空再抛一次异常，NUnit ``TestResult`` 只记录
**最后一次**异常，真正的断言失败被覆盖，表面症状变成"字体警告没出现"。复盘定位到耗时环节
之一：没有工具把失败用例对应的日志片段与首个异常抽出来，排查 agent 只能整段读
``playmode.log``（本仓库这份日志实测有一万多行）。本脚本就是补上这一环。

**判断记录（日志里没有官方的逐用例起止标记，2026-09-15 实测确认）**：本仓库
``check.ps1`` 跑出的 ``playmode.log``/``editmode.log``（Unity 6000.3.23f1 批处理
``-runTests``）里**没有**任何形如 ``[Test] Started: xxx``/``Finished`` 的逐用例标记，
甚至连用例名/类名本身都不会被打印（用真实门禁产物核对过：``grep -c VerticalSliceTests
playmode.log`` 为 0）。因此本脚本的用例窗口定位分四级回退，见 :func:`locate_window`：

1. ``test_first_chance``——若测试程序集里已接入 ``toolchain/unity_test_triage.py`` 的配套
   C# 回调（``[TestFirstChance]`` 日志行，见 ``adapters/unity`` 下 ``FirstChanceExceptionLogger``
   一类实现），直接用该行定位，最精确。
2. ``name_fallback``——退化为在日志里搜索用例 ``fullname``/``methodname`` 的**首次出现位置**，
   按各用例找到的行号排序切窗口；同批全部搜不到时整体回退到下一级。
3. ``message_fallback``——再退化为用 NUnit 失败 message 里的一段原文去日志里找可能相关的行，
   **不保证**属于该用例窗口，报告里明确标注"不保证"。
4. ``not_found``——以上都找不到，如实说明，只保留 NUnit 记录的 message/stack-trace 与全局
   Warning/Error 摘要。

**判断记录（Warning/Error 摘要的识别方式）**：Unity 批处理日志同样不会给消息行加
``[Warning]``/``[Error]`` 前缀（``LogAssert.Expect`` message 里的 ``[Warning]`` 只是
``LogType`` 的描述文字，不是日志原文格式）；日志里唯一稳定的信号是 Unity 自己吐出的调用栈
——``Debug.LogWarning``/``Debug.LogError``/``Debug.LogException`` 被调用时，紧跟在消息行后面
一定会有一段以 ``UnityEngine.Debug:ExtractStackTraceNoAlloc`` 开头、几行之内出现
``UnityEngine.Debug:LogWarning``/``LogError``/``LogException`` 的固定调用栈。本脚本据此反推
"消息行"（调用栈块首行的上一行），见 :func:`find_unity_log_messages`；另外少数子系统
（如着色器编译）直接在行首打 ``WARNING:``/``ERROR:``，一并识别。

**判断记录（NUnit "最后异常覆盖"提醒）**：``UnityLogCheckDelegatingCommand`` 的行为决定了
NUnit 结果 XML 里的 ``<failure><message>`` 只是**最后一次**抛出的异常，不代表窗口内最早出现
的问题——这正是本次排查绕远路的根因之一。本脚本在窗口内按行号顺序列出全部疑似
断言/异常行（:func:`collect_exception_lines`），并在报告里固定打印一句提醒，把"第一现场"
交回给排查者自己判断，不替 NUnit 的结论背书。

用法：
    python toolchain/unity_test_triage.py --xml <playmode.xml|editmode.xml> --log <playmode.log|editmode.log>
    python toolchain/unity_test_triage.py --xml ... --log ... --json
    python toolchain/unity_test_triage.py --xml ... --log ... --max-lines 60

返回码约定（与仓库其余 ``toolchain/`` 脚本一致，见 ``toolchain/README.md``"返回码约定"）：
    0 —— 结果 XML 里没有失败用例（``failed == 0``）。
    1 —— 至少一个失败用例（这是诊断信息的退出码，不代表本脚本自身运行出错）。
    2 —— 命令行参数错误 / 找不到或解析不了输入文件。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

# 与 toolchain/validate_data.py 等既有脚本的惯例一致：把 toolchain/ 目录本身放进 sys.path，
# 使本文件既可以 `python toolchain/unity_test_triage.py` 方式独立运行，也可以被测试模块
# `import` 复用。
sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402

DEFAULT_MAX_LINES = 40

# 首个异常/断言识别：按优先级分类，命中即归类到第一个匹配的分组，仅用于报告里标注"kind"，
# 不影响是否命中（同一行可能同时含 Exception 与 Assert，只取排在前面的类别名）。
_EXCEPTION_PATTERN = re.compile(
    r"(?P<assertion_exception>AssertionException)"
    r"|(?P<unexpected_log_exception>UnexpectedLogMessageException)"
    r"|(?P<exception>\bException\b)"
    r"|(?P<assert_call>\bAssert\.[A-Za-z]+)"
    r"|(?P<expected>\bExpected\b)"
    r"|(?P<unexpected>\bUnexpected\b)"
)

# TestFirstChance 回调（见本文件头判断记录 1）打印的行格式：
#   [TestFirstChance] <用例 FullName>: <异常类型全名>: <消息首行>
_TEST_FIRST_CHANCE_PATTERN = re.compile(r"^\[TestFirstChance\]\s*(?P<test>.+?):\s*(?P<rest>.+)$")

# Unity Debug.LogWarning/LogError/LogException 调用栈块的起始行；块内几行之内会出现具体的
# LogWarning/LogError/LogException 调用点，见本文件头判断记录 2。
_STACK_TRACE_HEAD = "UnityEngine.Debug:ExtractStackTraceNoAlloc"
_STACK_TRACE_LEVEL_PATTERNS = (
    ("Error", re.compile(r"UnityEngine\.Debug:LogError|UnityEngine\.Debug:LogException")),
    ("Warning", re.compile(r"UnityEngine\.Debug:LogWarning")),
)
_STACK_TRACE_LOOKAHEAD = 10

# 少数子系统（着色器编译等）不走 Debug.Log*，直接在行首打级别前缀。
_DIRECT_LEVEL_PATTERN = re.compile(r"^(?P<level>WARNING|ERROR):\s*(?P<text>.*)$")

ADVISORY_TEXT = (
    "提醒：NUnit 结果 XML 里的 message/stack-trace 只是该用例抛出的最后一次异常"
    "（见 UnityLogCheckDelegatingCommand 判断记录）；下面“窗口内异常/断言”列表按日志行号"
    "顺序排列，更早出现的一条才是第一现场，不要只看 NUnit 记录的那一条。"
)


class TriageError(Exception):
    """输入文件不存在/解析失败等命令行参数错误（对应返回码 2）。"""


def read_text_lines(path: Path) -> list[str]:
    try:
        raw = path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        raise TriageError(f"读取文件失败：{path}（{exc}）") from exc
    return raw.splitlines()


def parse_nunit_xml(xml_path: Path) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    """解析 NUnit3 结果 XML，返回 (汇总, 全部 test-case 列表——按文档顺序)。

    test-case 顺序即 XML 文档顺序；NUnit3 命令行报告器通常按用例结束时机写出 test-suite/
    test-case 节点，与实际执行顺序大体一致，但不保证——用作 name_fallback 的排序依据时
    在报告里已注明"近似"。
    """
    try:
        tree = ET.parse(str(xml_path))
    except (ET.ParseError, OSError) as exc:
        raise TriageError(f"解析 NUnit 结果 XML 失败：{xml_path}（{exc}）") from exc
    root = tree.getroot()

    def _int_attr(name: str) -> int:
        try:
            return int(root.attrib.get(name, "0"))
        except ValueError:
            return 0

    summary = {
        "result": root.attrib.get("result", ""),
        "total": _int_attr("total"),
        "passed": _int_attr("passed"),
        "failed": _int_attr("failed"),
        "skipped": _int_attr("skipped"),
        "inconclusive": _int_attr("inconclusive"),
    }

    cases: list[dict[str, Any]] = []
    for node in root.iter("test-case"):
        message = None
        stacktrace = None
        failure_node = node.find("failure")
        if failure_node is not None:
            message_node = failure_node.find("message")
            stacktrace_node = failure_node.find("stack-trace")
            if message_node is not None and message_node.text:
                message = message_node.text.strip()
            if stacktrace_node is not None and stacktrace_node.text:
                stacktrace = stacktrace_node.text.strip()
        cases.append(
            {
                "fullname": node.attrib.get("fullname", node.attrib.get("name", "")),
                "name": node.attrib.get("name", ""),
                "classname": node.attrib.get("classname", ""),
                "methodname": node.attrib.get("methodname", node.attrib.get("name", "")),
                "result": node.attrib.get("result", ""),
                "duration": node.attrib.get("duration", ""),
                "message": message,
                "stacktrace": stacktrace,
            }
        )
    return summary, cases


def find_test_first_chance_hits(lines: list[str], fullname: str) -> list[dict[str, Any]]:
    hits: list[dict[str, Any]] = []
    for idx, line in enumerate(lines):
        m = _TEST_FIRST_CHANCE_PATTERN.match(line.strip())
        if not m:
            continue
        test = m.group("test").strip()
        if test == fullname or fullname.endswith(test) or test.endswith(fullname):
            hits.append({"line": idx + 1, "text": line.strip()})
    return hits


def _first_occurrence_line(lines: list[str], needle: str) -> int | None:
    if not needle:
        return None
    for idx, line in enumerate(lines):
        if needle in line:
            return idx
    return None


def locate_window(
    lines: list[str],
    target: dict[str, Any],
    all_cases: list[dict[str, Any]],
    context: int,
) -> dict[str, Any]:
    """定位失败用例在日志里的执行窗口，四级回退，见文件头判断记录。返回：
    ``{"method": ..., "start_line": int|None, "end_line": int|None, "note": str,
      "related_lines": [int, ...]}``（``related_lines`` 仅 message_fallback 使用）。
    """
    fullname = target["fullname"]

    # 1) test_first_chance：最精确，直接用命中行 ± context 围出窗口。
    first_chance_hits = find_test_first_chance_hits(lines, fullname)
    if first_chance_hits:
        first_hit_line = first_chance_hits[0]["line"] - 1  # 转回 0-based
        start = max(0, first_hit_line - context)
        end = min(len(lines), first_hit_line + context + 1)
        return {
            "method": "test_first_chance",
            "start_line": start + 1,
            "end_line": end,
            "note": "定位依据：[TestFirstChance] 回调打印的第一现场标记（见 toolchain/unity_test_triage.py 头部判断记录 1）。",
            "related_lines": [],
        }

    # 2) name_fallback：按全部用例名称首次出现的行号排序切窗口。
    positions: dict[str, int] = {}
    for case in all_cases:
        needle = case["fullname"] or case["methodname"]
        idx = _first_occurrence_line(lines, needle)
        if idx is None and case["methodname"]:
            idx = _first_occurrence_line(lines, case["methodname"])
        if idx is not None:
            positions[case["fullname"]] = idx

    if fullname in positions:
        ordered = sorted(positions.items(), key=lambda kv: kv[1])
        start = positions[fullname]
        end = len(lines)
        for other_name, other_idx in ordered:
            if other_idx > start:
                end = other_idx
                break
        return {
            "method": "name_fallback",
            "start_line": start + 1,
            "end_line": end,
            "note": (
                "定位依据：日志里未找到任何逐用例起止标记（本仓库 Unity 批处理日志的已知限制，"
                "见文件头判断记录），退化为按用例全名/方法名首次出现的行号切片，"
                "窗口边界为近似值，不代表 Unity Test Framework 真实的用例起止时刻。"
            ),
            "related_lines": [],
        }

    # 3) message_fallback：用 NUnit message 里一段原文去日志里找可能相关的行，不保证归属。
    message = target.get("message") or ""
    excerpt = re.sub(r"[\[\]\\]", "", message).strip()
    excerpt = excerpt[:40].strip()
    related: list[int] = []
    if excerpt:
        for idx, line in enumerate(lines):
            if excerpt and excerpt in line:
                related.append(idx + 1)
    if related:
        return {
            "method": "message_fallback",
            "start_line": None,
            "end_line": None,
            "note": (
                "定位依据：日志里既没有逐用例标记也没有用例名，退化为用 NUnit 失败 message 的"
                "一段原文（\"" + excerpt + "\"）去全量日志里查找可能相关的行——"
                "以下行号不保证真的属于本用例的执行窗口，仅供人工核对。"
            ),
            "related_lines": related,
        }

    # 4) not_found：以上都没找到。
    return {
        "method": "not_found",
        "start_line": None,
        "end_line": None,
        "note": "日志里既没有逐用例标记、也没有用例名/方法名、也没有 message 原文可匹配，无法定位窗口；只能提供 NUnit 记录的 message/stack-trace 与全局 Warning/Error 摘要。",
        "related_lines": [],
    }


def collect_exception_lines(
    lines: list[str], start: int | None, end: int | None, limit: int
) -> list[dict[str, Any]]:
    if start is None or end is None:
        return []
    results: list[dict[str, Any]] = []
    for idx in range(start - 1, end):
        if idx < 0 or idx >= len(lines):
            continue
        line = lines[idx]
        m = _EXCEPTION_PATTERN.search(line)
        if not m:
            continue
        kind = next(name for name, value in m.groupdict().items() if value)
        results.append({"line": idx + 1, "kind": kind, "text": line.strip()})
        if len(results) >= limit:
            break
    return results


def find_unity_log_messages(
    lines: list[str], start: int | None, end: int | None
) -> list[dict[str, Any]]:
    """在窗口内找 Debug.LogWarning/LogError/LogException 的原始消息行，见文件头判断记录 2。"""
    if start is None or end is None:
        return []
    lo = max(0, start - 1)
    hi = min(len(lines), end)
    results: list[dict[str, Any]] = []
    i = lo
    while i < hi:
        line = lines[i]
        direct = _DIRECT_LEVEL_PATTERN.match(line.strip())
        if direct:
            results.append(
                {"line": i + 1, "level": direct.group("level").capitalize(), "text": line.strip()}
            )
            i += 1
            continue
        if _STACK_TRACE_HEAD in line:
            level = None
            limit = min(hi, i + _STACK_TRACE_LOOKAHEAD)
            j = i
            while j < limit:
                for level_name, pattern in _STACK_TRACE_LEVEL_PATTERNS:
                    if pattern.search(lines[j]):
                        level = level_name
                        break
                if level:
                    break
                j += 1
            if level and i - 1 >= lo:
                msg_line = lines[i - 1].strip()
                if msg_line:
                    results.append({"line": i, "level": level, "text": msg_line})
            i = j + 1
            continue
        i += 1
    return results


def summarize_warning_error(hits: list[dict[str, Any]], limit: int = 20) -> list[dict[str, Any]]:
    counts: dict[tuple[str, str], dict[str, Any]] = {}
    for hit in hits:
        key = (hit["level"], hit["text"])
        if key not in counts:
            counts[key] = {"level": hit["level"], "text": hit["text"], "count": 0, "first_line": hit["line"]}
        counts[key]["count"] += 1
    ordered = sorted(counts.values(), key=lambda v: (-v["count"], v["first_line"]))
    return ordered[:limit]


def build_report(xml_path: Path, log_path: Path, max_lines: int) -> dict[str, Any]:
    summary, cases = parse_nunit_xml(xml_path)
    lines = read_text_lines(log_path)

    failed_cases = [c for c in cases if c["result"] == "Failed"]
    context = max(5, max_lines // 2)

    failed_reports = []
    for case in failed_cases:
        window = locate_window(lines, case, cases, context)
        first_chance_hits = find_test_first_chance_hits(lines, case["fullname"])

        # 窗口定位方式为 message_fallback/not_found 时没有 start/end——按判断记录里承诺的
        # "退化为全局摘要"，改用整份日志范围扫异常/Warning/Error，报告里同步标注这是全局范围，
        # 不是精确窗口（见 format_human_report 对 window_is_full_log 的处理）。
        eff_start = window.get("start_line")
        eff_end = window.get("end_line")
        window_is_full_log = eff_start is None or eff_end is None
        if window_is_full_log:
            eff_start, eff_end = 1, len(lines)
        window["is_full_log_fallback"] = window_is_full_log

        exceptions = collect_exception_lines(lines, eff_start, eff_end, max_lines)
        log_messages = find_unity_log_messages(lines, eff_start, eff_end)
        failed_reports.append(
            {
                "fullname": case["fullname"],
                "classname": case["classname"],
                "methodname": case["methodname"],
                "duration": case["duration"],
                "nunit_message": case["message"],
                "nunit_stacktrace": case["stacktrace"],
                "window": window,
                "first_chance_hits": first_chance_hits,
                "first_exceptions": exceptions,
                "warning_error_summary": summarize_warning_error(log_messages),
            }
        )

    return {
        "xml_path": str(xml_path),
        "log_path": str(log_path),
        "summary": summary,
        "failed_tests": failed_reports,
        "advisory": ADVISORY_TEXT,
    }


def format_human_report(report: dict[str, Any], max_lines: int) -> str:
    out: list[str] = []
    s = report["summary"]
    out.append("=== Unity 测试结果分诊 ===")
    out.append(f"xml: {report['xml_path']}")
    out.append(f"log: {report['log_path']}")
    out.append(
        f"汇总：total={s['total']} passed={s['passed']} failed={s['failed']} "
        f"skipped={s['skipped']} inconclusive={s['inconclusive']} result={s['result']}"
    )

    if not report["failed_tests"]:
        out.append("没有失败用例，无需分诊。")
        return "\n".join(out)

    out.append("")
    out.append(report["advisory"])

    for i, t in enumerate(report["failed_tests"], start=1):
        out.append("")
        out.append(f"--- 失败用例 {i}/{len(report['failed_tests'])}：{t['fullname']} ---")
        out.append(f"NUnit message：{t['nunit_message']}")
        if t["nunit_stacktrace"]:
            out.append(f"NUnit stack-trace：{t['nunit_stacktrace']}")

        w = t["window"]
        out.append(f"窗口定位方式：{w['method']}")
        out.append(f"  {w['note']}")
        if w["method"] in ("test_first_chance", "name_fallback") and w["start_line"]:
            out.append(f"  窗口行号：{w['start_line']} ~ {w['end_line']}")
        if w["method"] == "message_fallback" and w["related_lines"]:
            shown = w["related_lines"][:max_lines]
            out.append(f"  可能相关的行号（不保证归属，共 {len(w['related_lines'])} 处，最多列 {len(shown)} 个）：{shown}")

        if t["first_chance_hits"]:
            out.append(f"TestFirstChance 命中（{len(t['first_chance_hits'])} 条）：")
            for hit in t["first_chance_hits"][:max_lines]:
                out.append(f"  行 {hit['line']}：{hit['text']}")

        scope_label = "整份日志范围（未能定位到该用例的执行窗口，以下可能混入其它用例的内容）" if w.get("is_full_log_fallback") else "窗口内"

        if t["first_exceptions"]:
            out.append(f"{scope_label}异常/断言（按行号顺序，第一条才是第一现场，最多 {max_lines} 条）：")
            for exc in t["first_exceptions"]:
                out.append(f"  行 {exc['line']} [{exc['kind']}]：{exc['text']}")
        else:
            out.append(f"{scope_label}没有找到疑似异常/断言行（异常文案可能不在识别范围内）。")

        if t["warning_error_summary"]:
            out.append(f"{scope_label} Warning/Error 摘要（去重计数）：")
            for w_hit in t["warning_error_summary"]:
                out.append(f"  [{w_hit['level']}] x{w_hit['count']}（首次行 {w_hit['first_line']}）：{w_hit['text']}")

    return "\n".join(out)


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Unity 测试结果分诊：从 NUnit3 结果 XML 与 Unity 日志里抽出失败用例的窗口片段与首个异常。"
    )
    parser.add_argument("--xml", required=True, help="NUnit3 结果 XML 路径（playmode.xml/editmode.xml）")
    parser.add_argument("--log", required=True, help="对应的 Unity 日志路径（playmode.log/editmode.log）")
    parser.add_argument("--json", action="store_true", help="输出 JSON 而不是人类可读文本")
    parser.add_argument(
        "--max-lines",
        type=int,
        default=DEFAULT_MAX_LINES,
        help=f"每个失败用例最多列出的窗口相关行数（异常行/相关行），默认 {DEFAULT_MAX_LINES}",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    # 命令行入口最先做 UTF-8 stdio，惯例同 toolchain/validate_data.py（见 toolchain/_console.py
    # 判断记录）。
    ensure_utf8_stdio()

    parser = build_arg_parser()
    args = parser.parse_args(argv)

    xml_path = Path(args.xml)
    log_path = Path(args.log)
    if not xml_path.is_file():
        print(f"错误：结果 XML 不存在：{xml_path}", file=sys.stderr)
        return 2
    if not log_path.is_file():
        print(f"错误：日志文件不存在：{log_path}", file=sys.stderr)
        return 2

    try:
        report = build_report(xml_path, log_path, max(1, args.max_lines))
    except TriageError as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 2

    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(format_human_report(report, max(1, args.max_lines)))

    return 1 if report["summary"]["failed"] > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
