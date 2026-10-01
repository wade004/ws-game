"""``toolchain/unity_test_triage.py`` 内部纯逻辑的单元测试（测试覆盖第四批 I-10，2026-10-01）。

既有 ``test_unity_test_triage.py`` 只从命令行/端到端角度验证；这里直接 ``import`` 模块，对被拆开的
纯函数逐个断言边界：

- ``_test_name_matches``：全名相等 / 任一方是另一方的后缀才算匹配，子串不算。
- ``find_test_boundary``：Started/Finished 取"第一次出现"（两个测试程序集的回调会让每行重复），
  Started 缺失 -> 全 None，Finished 缺失 -> 只有 start。
- ``find_log_during_test_hits``：窗口闭区间、全日志兜底、别的用例的行不算。
- ``locate_window``：四级回退（test_first_chance / name_fallback / message_fallback / not_found）逐级触发。
- ``collect_exception_lines``：分类优先级、行数上限、窗口外不收。
- ``find_unity_log_messages``：直接前缀行、调用栈块里 LogError/LogException/LogWarning 的识别与前瞻上限。
- ``summarize_warning_error``：去重计数、按次数降序再按首行排序、上限。
- ``parse_nunit_xml``：汇总计数（含非法数字置 0）、失败 message/stack-trace、缺省 fullname 回退到 name、非法 XML 抛 TriageError。
- ``read_text_lines`` 不存在时抛 TriageError；``format_human_report`` 对四种定位方式的措辞分支。

运行：``python -m pytest toolchain/tests/test_unity_test_triage_logic.py -q``。
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

import unity_test_triage as triage  # noqa: E402


def _started(name: str) -> str:
    return f"[TestFirstChance] Started: {name}"


def _finished(name: str, result: str = "Failed", message: str = "boom") -> str:
    return f"[TestFirstChance] Finished: {name} result={result} message={message}"


def _log_during(name: str, level: str, text: str) -> str:
    return f"[TestFirstChance] LogDuringTest: {name}: {level}: {text}"


def _case(fullname: str, message: str | None = None, method: str | None = None) -> dict:
    return {
        "fullname": fullname, "name": fullname.rsplit(".", 1)[-1], "classname": "C",
        "methodname": method if method is not None else fullname.rsplit(".", 1)[-1],
        "result": "Failed", "duration": "0.1", "message": message, "stacktrace": None,
    }


# ----------------------------------------------------------------------------
# _test_name_matches / find_test_boundary
# ----------------------------------------------------------------------------

@pytest.mark.parametrize(
    "candidate,fullname,expected",
    [
        ("A.B.Test1", "A.B.Test1", True),
        ("Test1", "A.B.Test1", True),          # fullname 以 candidate 结尾
        ("A.B.Test1", "Test1", True),          # candidate 以 fullname 结尾
        ("A.B.Test1", "A.B.Test2", False),
        ("A.B.Test1", "A.B.Test10", False),
        ("Test", "A.B.MyTest", True),          # 已知口径：后缀匹配不要求点边界（与实现一致，别误当子串匹配）
        ("B.Test", "A.B.Test1", False),        # 仅子串，不是后缀
    ],
)
def test_name_match_rules(candidate: str, fullname: str, expected: bool) -> None:
    assert triage._test_name_matches(candidate, fullname) is expected


def test_boundary_takes_first_started_and_first_finished_after_it() -> None:
    lines = [
        "noise",
        _started("N.T1"), _started("N.T1"),  # 两个回调实例各打一遍
        "mid",
        _finished("N.T1", "Failed", "first msg"), _finished("N.T1", "Failed", "second msg"),
    ]
    b = triage.find_test_boundary(lines, "N.T1")
    assert b == {"start": 1, "end": 4, "result": "Failed", "message": "first msg"}


def test_boundary_ignores_other_tests_and_finished_before_started() -> None:
    lines = [_finished("N.T1"), _started("N.T2"), _finished("N.T2"), _started("N.T1"), "x", _finished("N.T1", "Passed", "ok")]
    b = triage.find_test_boundary(lines, "N.T1")
    assert b["start"] == 3 and b["end"] == 5 and b["result"] == "Passed"


def test_boundary_without_started_is_all_none() -> None:
    assert triage.find_test_boundary(["a", "b"], "N.T1") == {"start": None, "end": None, "result": None, "message": None}


def test_boundary_without_finished_only_has_start() -> None:
    b = triage.find_test_boundary([_started("N.T1"), "tail"], "N.T1")
    assert b["start"] == 0 and b["end"] is None and b["result"] is None


# ----------------------------------------------------------------------------
# find_log_during_test_hits
# ----------------------------------------------------------------------------

def test_log_during_hits_respect_window_and_test_name() -> None:
    lines = [
        _log_during("N.T1", "Error", "before window"),     # 0
        _started("N.T1"),                                  # 1
        _log_during("N.T1", "Exception", "inside"),        # 2
        _log_during("N.T2", "Error", "someone else"),      # 3
        _finished("N.T1"),                                 # 4
        _log_during("N.T1", "Error", "after window"),      # 5
    ]
    hits = triage.find_log_during_test_hits(lines, "N.T1", 1, 4)
    assert [h["line"] for h in hits] == [3]
    assert hits[0]["level"] == "Exception"
    assert hits[0]["text"].endswith("inside")


def test_log_during_hits_window_is_closed_interval() -> None:
    lines = [_log_during("N.T1", "Error", "edge-start"), "x", _log_during("N.T1", "Error", "edge-end")]
    hits = triage.find_log_during_test_hits(lines, "N.T1", 0, 2)
    assert [h["line"] for h in hits] == [1, 3]


def test_log_during_hits_without_window_scans_whole_log() -> None:
    lines = [_log_during("N.T1", "Error", "a"), "x", _log_during("N.T1", "Error", "b")]
    assert len(triage.find_log_during_test_hits(lines, "N.T1", None, None)) == 2


def test_log_during_hits_ignores_warning_level() -> None:
    lines = ["[TestFirstChance] LogDuringTest: N.T1: Warning: nope"]
    assert triage.find_log_during_test_hits(lines, "N.T1", None, None) == []


# ----------------------------------------------------------------------------
# locate_window 四级回退
# ----------------------------------------------------------------------------

def test_locate_window_level1_uses_boundary_markers() -> None:
    lines = ["a", _started("N.T1"), "x", _finished("N.T1", "Failed", "m"), "z"]
    target = _case("N.T1")
    w = triage.locate_window(lines, target, [target], 20)
    assert w["method"] == "test_first_chance"
    assert (w["start_line"], w["end_line"]) == (2, 4)
    assert w["finished_result"] == "Failed" and w["finished_message"] == "m"


def test_locate_window_level1_without_finished_extends_to_end_of_log() -> None:
    lines = ["a", _started("N.T1"), "x", "y"]
    target = _case("N.T1")
    w = triage.locate_window(lines, target, [target], 20)
    assert w["method"] == "test_first_chance"
    assert (w["start_line"], w["end_line"]) == (2, 4)
    assert "未找到对应 Finished" in w["note"]


def test_locate_window_level2_name_fallback_ends_at_next_cases_first_mention() -> None:
    lines = ["head", "running N.T1 now", "t1 detail", "running N.T2 now", "t2 detail"]
    t1, t2 = _case("N.T1"), _case("N.T2")
    w = triage.locate_window(lines, t1, [t1, t2], 20)
    assert w["method"] == "name_fallback"
    assert (w["start_line"], w["end_line"]) == (2, 3)  # 下一个用例首次出现在第 4 行（1-based），窗口止于其前一行
    last = triage.locate_window(lines, t2, [t1, t2], 20)
    assert (last["start_line"], last["end_line"]) == (4, len(lines))


def test_locate_window_level2_matches_method_name_when_fullname_absent() -> None:
    lines = ["x", "Method_Only_Name executing", "y"]
    t = _case("Some.Long.Namespace.Fixture.Method_Only_Name", method="Method_Only_Name")
    w = triage.locate_window(lines, t, [t], 20)
    assert w["method"] == "name_fallback" and w["start_line"] == 2


def test_locate_window_level3_message_fallback_lists_related_lines() -> None:
    lines = ["nothing", "Expected: 5 But was: 7 in somewhere", "other", "Expected: 5 But was: 7 again"]
    t = _case("N.T1", message="Expected: 5 But was: 7")
    w = triage.locate_window(lines, t, [t], 20)
    assert w["method"] == "message_fallback"
    assert w["related_lines"] == [2, 4]
    assert w["start_line"] is None and w["end_line"] is None
    assert "不保证" in w["note"]


def test_locate_window_level4_not_found() -> None:
    t = _case("N.T1", message="msg that is nowhere")
    w = triage.locate_window(["a", "b"], t, [t], 20)
    assert w["method"] == "not_found"
    assert w["related_lines"] == [] and w["start_line"] is None


def test_locate_window_level4_when_message_is_empty() -> None:
    t = _case("N.T1", message=None)
    assert triage.locate_window(["a"], t, [t], 20)["method"] == "not_found"


# ----------------------------------------------------------------------------
# collect_exception_lines
# ----------------------------------------------------------------------------

def test_exception_kind_priority_and_order() -> None:
    lines = [
        "plain",
        "NUnit.Framework.AssertionException: Expected 1",           # assertion_exception 优先于 Exception/Expected
        "System.InvalidOperationException happened",               # \bException\b 不匹配 InvalidOperationException
        "UnexpectedLogMessageException: Unhandled log message",    # unexpected_log_exception
        "System.Exception: bad",                                   # exception
        "  Assert.AreEqual(1, 2)",                                 # assert_call
        "Expected: 3",                                             # expected
        "Unexpected thing",                                        # unexpected
    ]
    hits = triage.collect_exception_lines(lines, 1, len(lines), 50)
    assert [(h["line"], h["kind"]) for h in hits] == [
        (2, "assertion_exception"), (4, "unexpected_log_exception"), (5, "exception"),
        (6, "assert_call"), (7, "expected"), (8, "unexpected"),
    ]


def test_exception_collection_respects_limit_and_window() -> None:
    lines = ["Exception A", "Exception B", "Exception C", "Exception D"]
    assert [h["line"] for h in triage.collect_exception_lines(lines, 1, 4, 2)] == [1, 2]
    assert [h["line"] for h in triage.collect_exception_lines(lines, 2, 3, 50)] == [2, 3]
    assert triage.collect_exception_lines(lines, None, 3, 50) == []
    assert triage.collect_exception_lines(lines, 1, None, 50) == []


def test_exception_collection_tolerates_out_of_range_window() -> None:
    lines = ["Exception A"]
    assert [h["line"] for h in triage.collect_exception_lines(lines, 0, 10, 50)] == [1]


# ----------------------------------------------------------------------------
# find_unity_log_messages / summarize_warning_error
# ----------------------------------------------------------------------------

def test_log_messages_direct_level_prefix_lines() -> None:
    lines = ["WARNING: shader x", "ERROR: compile failed", "warning: lower case is not a prefix"]
    hits = triage.find_unity_log_messages(lines, 1, len(lines))
    assert [(h["level"], h["line"]) for h in hits] == [("Warning", 1), ("Error", 2)]


def test_log_messages_stack_block_levels() -> None:
    lines = [
        "my warning message",
        "UnityEngine.Debug:ExtractStackTraceNoAlloc (byte*,int,string)",
        "UnityEngine.Debug:LogWarning (object)",
        "my error message",
        "UnityEngine.Debug:ExtractStackTraceNoAlloc (byte*,int,string)",
        "UnityEngine.Debug:LogError (object)",
        "my exception message",
        "UnityEngine.Debug:ExtractStackTraceNoAlloc (byte*,int,string)",
        "UnityEngine.Debug:LogException (System.Exception)",
    ]
    hits = triage.find_unity_log_messages(lines, 1, len(lines))
    assert [(h["level"], h["text"]) for h in hits] == [
        ("Warning", "my warning message"), ("Error", "my error message"), ("Error", "my exception message"),
    ]


def test_log_messages_stack_block_without_level_within_lookahead_is_ignored() -> None:
    filler = ["frame"] * 12
    lines = ["orphan message", "UnityEngine.Debug:ExtractStackTraceNoAlloc (x)", *filler, "UnityEngine.Debug:LogError (object)"]
    assert triage.find_unity_log_messages(lines, 1, len(lines)) == []


def test_log_messages_window_none_or_block_at_window_start_yields_nothing() -> None:
    lines = ["UnityEngine.Debug:ExtractStackTraceNoAlloc (x)", "UnityEngine.Debug:LogError (object)"]
    assert triage.find_unity_log_messages(lines, None, 2) == []
    assert triage.find_unity_log_messages(lines, 1, 2) == [], "头一行没有前置消息行，不能凭空造出一条"


def test_summarize_dedups_counts_and_orders() -> None:
    hits = [
        {"level": "Error", "text": "B", "line": 9},
        {"level": "Warning", "text": "A", "line": 3},
        {"level": "Error", "text": "B", "line": 12},
        {"level": "Warning", "text": "C", "line": 1},
    ]
    out = triage.summarize_warning_error(hits)
    assert [(o["text"], o["count"], o["first_line"]) for o in out] == [("B", 2, 9), ("C", 1, 1), ("A", 1, 3)]
    assert len(triage.summarize_warning_error(hits, limit=1)) == 1
    # 同文不同级别分开计
    mixed = triage.summarize_warning_error([
        {"level": "Error", "text": "X", "line": 1}, {"level": "Warning", "text": "X", "line": 2},
    ])
    assert len(mixed) == 2


# ----------------------------------------------------------------------------
# parse_nunit_xml / read_text_lines / format_human_report
# ----------------------------------------------------------------------------

def test_parse_nunit_xml_summary_failures_and_defaults(tmp_path: Path) -> None:
    xml = tmp_path / "r.xml"
    xml.write_text(
        '<test-run result="Failed" total="3" passed="1" failed="1" skipped="notanumber" inconclusive="1">'
        '<test-suite><test-case name="OnlyName" classname="C" methodname="M" result="Passed" duration="0.5"/>'
        '<test-case fullname="N.C.Bad" name="Bad" classname="N.C" methodname="Bad" result="Failed" duration="1">'
        '<failure><message>  Expected 1  </message><stack-trace> at X </stack-trace></failure></test-case>'
        '<test-case name="NoFullname" result="Inconclusive"/>'
        '</test-suite></test-run>',
        encoding="utf-8",
    )
    summary, cases = triage.parse_nunit_xml(xml)
    assert summary == {"result": "Failed", "total": 3, "passed": 1, "failed": 1, "skipped": 0, "inconclusive": 1}
    assert [c["fullname"] for c in cases] == ["OnlyName", "N.C.Bad", "NoFullname"], "缺 fullname 回退到 name"
    bad = cases[1]
    assert bad["message"] == "Expected 1" and bad["stacktrace"] == "at X"
    assert cases[0]["message"] is None


def test_parse_nunit_xml_rejects_malformed_and_missing(tmp_path: Path) -> None:
    bad = tmp_path / "bad.xml"
    bad.write_text("<test-run", encoding="utf-8")
    with pytest.raises(triage.TriageError):
        triage.parse_nunit_xml(bad)
    with pytest.raises(triage.TriageError):
        triage.parse_nunit_xml(tmp_path / "missing.xml")


def test_read_text_lines_missing_file_raises_and_bad_bytes_are_replaced(tmp_path: Path) -> None:
    with pytest.raises(triage.TriageError):
        triage.read_text_lines(tmp_path / "nope.log")
    log = tmp_path / "x.log"
    log.write_bytes(b"line1\n\xff\xfebad\nline3")
    assert triage.read_text_lines(log)[0] == "line1" and len(triage.read_text_lines(log)) == 3


def _report_with(window: dict, **extra) -> dict:
    t = {
        "fullname": "N.T1", "classname": "N", "methodname": "T1", "duration": "1",
        "nunit_message": "msg", "nunit_stacktrace": None, "window": window,
        "log_during_test": [], "first_exceptions": [], "warning_error_summary": [],
    }
    t.update(extra)
    return {
        "xml_path": "x.xml", "log_path": "x.log", "advisory": triage.ADVISORY_TEXT,
        "summary": {"result": "Failed", "total": 1, "passed": 0, "failed": 1, "skipped": 0, "inconclusive": 0},
        "failed_tests": [t],
    }


def test_human_report_branches_per_window_method() -> None:
    exact = triage.format_human_report(_report_with({
        "method": "test_first_chance", "start_line": 3, "end_line": 9, "note": "n", "related_lines": [],
        "finished_result": "Failed", "finished_message": "m", "is_full_log_fallback": False,
    }), 10)
    assert "窗口行号：3 ~ 9" in exact and "Finished 记录：result=Failed" in exact and "窗口内没有找到疑似异常" in exact

    fallback = triage.format_human_report(_report_with({
        "method": "message_fallback", "start_line": None, "end_line": None, "note": "n",
        "related_lines": [5, 6, 7], "is_full_log_fallback": True,
    }), 2)
    assert "共 3 处，最多列 2 个" in fallback and "[5, 6]" in fallback
    assert "整份日志范围（未能定位到该用例的执行窗口" in fallback

    none_failed = {**_report_with({}), "failed_tests": []}
    assert "没有失败用例，无需分诊。" in triage.format_human_report(none_failed, 10)


def test_build_report_counts_only_failed_and_falls_back_to_full_log(tmp_path: Path) -> None:
    xml = tmp_path / "r.xml"
    xml.write_text(
        '<test-run result="Failed" total="2" passed="1" failed="1" skipped="0" inconclusive="0"><test-suite>'
        '<test-case fullname="N.Ok" name="Ok" methodname="Ok" result="Passed"/>'
        '<test-case fullname="N.Bad" name="Bad" methodname="Bad" result="Failed">'
        '<failure><message>zzz-unique-message-zzz</message></failure></test-case></test-suite></test-run>',
        encoding="utf-8",
    )
    log = tmp_path / "r.log"
    log.write_text("Exception: unrelated\nERROR: shader\n", encoding="utf-8")
    report = triage.build_report(xml, log, 40)
    assert [t["fullname"] for t in report["failed_tests"]] == ["N.Bad"]
    t = report["failed_tests"][0]
    assert t["window"]["method"] == "not_found" and t["window"]["is_full_log_fallback"] is True
    assert [e["line"] for e in t["first_exceptions"]] == [1]
    assert [(w["level"], w["count"]) for w in t["warning_error_summary"]] == [("Error", 1)]
