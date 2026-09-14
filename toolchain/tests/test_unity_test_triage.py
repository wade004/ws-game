"""``toolchain/unity_test_triage.py`` 回归测试（排查复盘 2026-09-15 落地，见
``architecture/落地计划/排查复盘-2026-09-15-PlayMode-PRES180.md``）。

核心场景（本文件最重要的一条用例 ``test_last_exception_overridden_by_first_assertion``）：
用内嵌的最小 NUnit3 结果 XML + Unity 日志夹具，还原本次复盘的真实机制——窗口内先出现
``AssertionException``（真正的第一现场：被测行为本身没发生），几行之后才出现
``UnexpectedLogMessageException``/``Expected log did not appear``（NUnit 最终记录进结果 XML
的那一条，只是"最后一次"异常）。断言分诊脚本把 ``AssertionException`` 排在
``first_exceptions`` 列表首位，而不是被 NUnit message 里那条表象异常带偏。

运行：``python -m pytest toolchain/tests/test_unity_test_triage.py -q`` 或
``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
MODULE_PATH = TOOLCHAIN_DIR / "unity_test_triage.py"


def _load_module():
    spec = importlib.util.spec_from_file_location("unity_test_triage_under_test", MODULE_PATH)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


triage = _load_module()


def _write_xml(path: Path, *, failed: int, total: int, cases_xml: str) -> None:
    passed = total - failed
    result = "Failed(Child)" if failed else "Passed"
    path.write_text(
        f'<?xml version="1.0" encoding="utf-8"?>\n'
        f'<test-run id="1" testcasecount="{total}" result="{result}" total="{total}" '
        f'passed="{passed}" failed="{failed}" inconclusive="0" skipped="0">\n'
        f"{cases_xml}"
        f"</test-run>\n",
        encoding="utf-8",
    )


def test_last_exception_overridden_by_first_assertion(tmp_path: Path) -> None:
    """核心场景：窗口内更早的 AssertionException 才是第一现场，NUnit message 只是最后一条。"""
    log_path = tmp_path / "playmode.log"
    log_path.write_text(
        "\n".join(
            [
                "line 1: 无关的启动日志",
                "line 2: OtherFixture.OtherTest running",
                "line 3: MyFixture.MyFailingTest running",  # 用例名首次出现，用于 name_fallback 定位
                "line 4: AssertionException: died is false, expected true",  # 真正的第一现场
                "line 5: 一些中间诊断输出",
                "line 6: Expected log did not appear: [Warning] xxx was not found in the [LiberationSans SDF] font asset",
                "line 7: UnexpectedLogMessageException: xxx",
                "line 8: MyFixture.NextTest running",  # 下一个用例开始，界定窗口结束
                "line 9: 属于 NextTest 的内容，不应该被算进上一个用例的窗口",
            ]
        ),
        encoding="utf-8",
    )

    xml_path = tmp_path / "playmode.xml"
    _write_xml(
        xml_path,
        failed=1,
        total=2,
        cases_xml=(
            '  <test-case id="1" name="MyFailingTest" '
            'fullname="MyFixture.MyFailingTest" methodname="MyFailingTest" '
            'classname="MyFixture" result="Failed" duration="1.0">\n'
            "    <failure><message><![CDATA[Expected log did not appear: "
            "[Warning] xxx was not found in the [LiberationSans SDF] font asset]]></message>"
            "<stack-trace><![CDATA[UnexpectedLogMessageException: xxx]]></stack-trace></failure>\n"
            "  </test-case>\n"
            '  <test-case id="2" name="NextTest" fullname="MyFixture.NextTest" '
            'methodname="NextTest" classname="MyFixture" result="Passed" duration="1.0" />\n'
        ),
    )

    report = triage.build_report(xml_path, log_path, max_lines=40)

    assert report["summary"] == {
        "result": "Failed(Child)",
        "total": 2,
        "passed": 1,
        "failed": 1,
        "skipped": 0,
        "inconclusive": 0,
    }
    assert len(report["failed_tests"]) == 1
    failed = report["failed_tests"][0]
    assert failed["fullname"] == "MyFixture.MyFailingTest"

    # NUnit 记录的确实是"最后一次"（字体警告那条）。
    assert "font asset" in failed["nunit_message"]

    # 用例名在日志里出现过 -> 走 name_fallback，精确切到本用例窗口（不含 NextTest 那行）。
    window = failed["window"]
    assert window["method"] == "name_fallback"
    assert window["is_full_log_fallback"] is False
    assert window["start_line"] == 3
    assert window["end_line"] == 7  # 含 line 7，不含 line 8（下一个用例 NextTest 的起始行）

    # 核心断言：first_exceptions 按行号顺序排列，AssertionException 排第一，
    # UnexpectedLogMessageException/Expected 排在它后面——不能反过来。
    kinds_in_order = [(e["line"], e["kind"]) for e in failed["first_exceptions"]]
    assert kinds_in_order[0] == (4, "assertion_exception")
    later_lines = [line for line, kind in kinds_in_order if kind != "assertion_exception"]
    assert later_lines, "应该也捕获到窗口内更晚出现的 Expected/UnexpectedLogMessageException 行"
    assert min(later_lines) > 4

    # NextTest 那一行（line 9）不应该混进窗口。
    assert all(e["line"] < 8 for e in failed["first_exceptions"])


def test_test_first_chance_marker_gives_precise_window(tmp_path: Path) -> None:
    """C 部分新增的 TestFirstChanceExceptionLogger 回调命中时，定位方式应优先于 name_fallback。

    夹具按真实 C# 回调的实际输出格式构造（见 adapters/unity 下 TestFirstChanceExceptionLogger
    判断记录 1：最初设计按 AppDomain.FirstChanceException 实现，2026-09-15 实测在本仓库使用的
    Unity 6000.3.23f1 Mono 运行时完全不触发，已改为 Started/Finished 用例边界 +
    Application.logMessageReceivedThreaded 捕获窗口内 Error/Exception 级日志的验证可用方案；
    真实端到端跑通记录见同一 C# 文件与复盘文档）——断言异常本身不会出现在窗口内（NUnit 断言
    失败不经过 Unity 日志系统），但窗口边界精确，且窗口内的 Error 级日志会被
    ``[TestFirstChance] LogDuringTest`` 行捕获并归属正确用例。
    """
    log_path = tmp_path / "playmode.log"
    log_path.write_text(
        "\n".join(
            [
                "line 1: 无关",
                "[TestFirstChance] Started: MyFixture.MyFailingTest",
                "[TestFirstChance] LogDuringTest: MyFixture.MyFailingTest: Error: something went wrong",
                "line 4: 其它输出",
                "[TestFirstChance] Finished: MyFixture.MyFailingTest result=Failed message=died is false",
                "[TestFirstChance] Started: MyFixture.NextTest",
                "line 7: 属于 NextTest 的内容，不应该被算进上一个用例的窗口",
            ]
        ),
        encoding="utf-8",
    )
    xml_path = tmp_path / "playmode.xml"
    _write_xml(
        xml_path,
        failed=1,
        total=1,
        cases_xml=(
            '  <test-case id="1" name="MyFailingTest" fullname="MyFixture.MyFailingTest" '
            'methodname="MyFailingTest" classname="MyFixture" result="Failed" duration="1.0">\n'
            "    <failure><message><![CDATA[died is false]]></message></failure>\n"
            "  </test-case>\n"
        ),
    )

    report = triage.build_report(xml_path, log_path, max_lines=40)
    failed = report["failed_tests"][0]
    window = failed["window"]
    assert window["method"] == "test_first_chance"
    assert window["is_full_log_fallback"] is False
    assert window["start_line"] == 2
    assert window["end_line"] == 5
    assert window["finished_result"] == "Failed"
    assert window["finished_message"] == "died is false"

    assert len(failed["log_during_test"]) == 1
    assert failed["log_during_test"][0]["line"] == 3
    assert failed["log_during_test"][0]["level"] == "Error"
    assert "something went wrong" in failed["log_during_test"][0]["text"]


def test_no_failures_reports_empty_and_exit_zero(tmp_path: Path, capsys) -> None:
    log_path = tmp_path / "editmode.log"
    log_path.write_text("nothing interesting\n", encoding="utf-8")
    xml_path = tmp_path / "editmode.xml"
    _write_xml(xml_path, failed=0, total=1, cases_xml=(
        '  <test-case id="1" name="OkTest" fullname="MyFixture.OkTest" '
        'methodname="OkTest" classname="MyFixture" result="Passed" duration="0.1" />\n'
    ))

    exit_code = triage.main(["--xml", str(xml_path), "--log", str(log_path)])
    assert exit_code == 0
    out = capsys.readouterr().out
    assert "没有失败用例" in out


def test_json_output_is_valid_json(tmp_path: Path, capsys) -> None:
    log_path = tmp_path / "playmode.log"
    log_path.write_text("MyFixture.MyFailingTest running\nAssertionException: died\n", encoding="utf-8")
    xml_path = tmp_path / "playmode.xml"
    _write_xml(xml_path, failed=1, total=1, cases_xml=(
        '  <test-case id="1" name="MyFailingTest" fullname="MyFixture.MyFailingTest" '
        'methodname="MyFailingTest" classname="MyFixture" result="Failed" duration="0.1">\n'
        "    <failure><message><![CDATA[final message]]></message></failure>\n"
        "  </test-case>\n"
    ))

    exit_code = triage.main(["--xml", str(xml_path), "--log", str(log_path), "--json"])
    assert exit_code == 1
    out = capsys.readouterr().out
    data = json.loads(out)
    assert data["summary"]["failed"] == 1
    assert data["failed_tests"][0]["fullname"] == "MyFixture.MyFailingTest"


def test_missing_input_files_exit_code_two(tmp_path: Path) -> None:
    exit_code = triage.main(
        ["--xml", str(tmp_path / "missing.xml"), "--log", str(tmp_path / "missing.log")]
    )
    assert exit_code == 2


def test_cli_runs_as_subprocess(tmp_path: Path) -> None:
    """确认脚本可以按 `python toolchain/unity_test_triage.py ...` 独立运行（非 import 方式）。"""
    log_path = tmp_path / "editmode.log"
    log_path.write_text("nothing\n", encoding="utf-8")
    xml_path = tmp_path / "editmode.xml"
    _write_xml(xml_path, failed=0, total=1, cases_xml=(
        '  <test-case id="1" name="OkTest" fullname="MyFixture.OkTest" '
        'methodname="OkTest" classname="MyFixture" result="Passed" duration="0.1" />\n'
    ))
    proc = subprocess.run(
        [sys.executable, str(MODULE_PATH), "--xml", str(xml_path), "--log", str(log_path)],
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    assert proc.returncode == 0
    assert "没有失败用例" in proc.stdout
