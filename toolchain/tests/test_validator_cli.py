"""``toolchain/validator``（数据校验命令行入口）的进程级反向路径与开关行为测试（测试覆盖梳理 I-4，
2026-10-01）。

背景：``toolchain/validate_data.py`` 的既有测试（``test_validate_data_*``）全部 mock 掉
``subprocess.run``，只验证 Python 侧调度，从未真正运行过 C# 校验器；校验器自己的参数解析、退出码、
``--schema-audit``/``--allowlist`` 分支也没有反向路径用例。本文件把 ``Validator.dll`` 当黑盒跑
（定位/构建方式见 ``_dotnet_cli.py``，cwd 仓库根），覆盖：

* 参数错误 → 退出码 2（缺必填参数、未知参数、选项缺值、``--display-map-sources`` 格式非法、数据根
  不存在、白名单文件读不到/结构非法）。
* 数据本身有问题 → 退出码 1，输出含 ``[error] <表>...`` 定位。
* ``--strict`` 把 warning 提升为阻断（退出码 1）。
* ``--list-tables``、``--display-map-sources``、``--enable-graph-isolation``、``--no-missing-
  translation-warning`` 各一条可观测行为。
* ``--schema-audit``：注意该模式**不读任何数据根**，审计的是代码里已登记的 ``TableSchema`` 结构本身，
  所谓"临时改坏"的输入是白名单文件（``--allowlist``）——去掉一条必要条目 → 退出码 1 且输出含该条目的
  ``<表>/<字段>`` 定位；多出一条找不到对应命中的条目 → 退出码 0 但输出 ``allowlist_entry_unused`` 警告
  （代码语义：``SchemaAudit.Run`` 把未使用的白名单条目记为 warning，不阻断）。

有效数据根用 ``core/sim/tests/data``（门禁同款嵌入式数据集），期望的表数/记录数一律从校验器自己的
汇总行读出后再互相核对，不写死裸数。
"""

from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

from _dotnet_cli import REPO_ROOT, get_cli_dll, run_cli

VALID_DATA_ROOT = "core/sim/tests/data"
REPO_ALLOWLIST = REPO_ROOT / "toolchain" / "schema_audit_allowlist.json"

SUMMARY_RE = re.compile(r"^tables (\d+), records (\d+), errors (\d+), warnings (\d+), overrides (\d+)$", re.M)
AUDIT_SUMMARY_RE = re.compile(r"^tables (\d+), fields (\d+), errors (\d+), warnings (\d+)$", re.M)


@pytest.fixture(scope="module")
def dll() -> Path:
    return get_cli_dll("Validator")


def _summary(stdout: str) -> tuple[int, int, int, int, int]:
    m = SUMMARY_RE.search(stdout)
    assert m, f"找不到汇总行：{stdout[-400:]!r}"
    return tuple(int(x) for x in m.groups())  # type: ignore[return-value]


def _audit_summary(stdout: str) -> tuple[int, int, int, int]:
    m = AUDIT_SUMMARY_RE.search(stdout)
    assert m, f"找不到审计汇总行：{stdout[-400:]!r}"
    return tuple(int(x) for x in m.groups())  # type: ignore[return-value]


# ---------------------------------------------------------------- 参数错误 -> 2


def test_no_arguments_exits_2_and_mentions_data_root(dll):
    proc = run_cli(dll, [])
    assert proc.returncode == 2
    assert "缺少必填参数 --data-root" in proc.stderr
    assert "--schema-audit" in proc.stderr  # 用法里同时提示元数据门禁模式


def test_unknown_argument_exits_2(dll):
    proc = run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--bogus"])
    assert proc.returncode == 2
    assert "未知参数" in proc.stderr and "--bogus" in proc.stderr


@pytest.mark.parametrize(
    "option,fragment",
    [
        ("--data-root", "--data-root 需要一个目录参数"),
        ("--allowlist", "--allowlist 需要一个文件路径参数"),
        ("--display-map-sources", "--display-map-sources 需要一个参数"),
    ],
)
def test_option_without_value_exits_2(dll, option, fragment):
    proc = run_cli(dll, [option])
    assert proc.returncode == 2
    assert fragment in proc.stderr


def test_nonexistent_data_root_exits_2(dll, tmp_path):
    proc = run_cli(dll, ["--data-root", str(tmp_path / "no_such_dir")])
    assert proc.returncode == 2
    assert "目录不存在" in proc.stderr


@pytest.mark.parametrize("bad", ["bad", "a:b:c", ":idField", "table:", ",", " "])
def test_display_map_sources_malformed_exits_2(dll, bad):
    proc = run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--display-map-sources", bad])
    assert proc.returncode == 2
    assert "--display-map-sources 格式非法" in proc.stderr


# ---------------------------------------------------------------- 数据有问题 -> 1


def test_unregistered_table_exits_1_with_located_error(dll, tmp_path):
    (tmp_path / "foo").mkdir()
    (tmp_path / "foo" / "foo.bar.json").write_text(
        json.dumps({"table": "foo.bar", "schema_version": 1, "rows": []}), encoding="utf-8"
    )

    proc = run_cli(dll, ["--data-root", str(tmp_path)])

    assert proc.returncode == 1
    assert re.search(r"^\[error\] foo\.bar: envelope: .*未通过 RegisterSchema 登记$", proc.stdout, re.M)
    tables, records, errors, warnings, overrides = _summary(proc.stdout)
    assert errors == len(re.findall(r"^\[error\] ", proc.stdout, re.M)) >= 1


def test_malformed_table_json_exits_1_with_table_location(dll, tmp_path):
    (tmp_path / "arch").mkdir()
    (tmp_path / "arch" / "arch.class.json").write_text("{bad", encoding="utf-8")

    proc = run_cli(dll, ["--data-root", str(tmp_path)])

    assert proc.returncode == 1
    assert re.search(r"^\[error\] arch\.class: envelope: JSON 解析失败", proc.stdout, re.M)


def test_json_output_of_blocking_data_is_single_parseable_object(dll, tmp_path):
    (tmp_path / "foo").mkdir()
    (tmp_path / "foo" / "foo.bar.json").write_text(
        json.dumps({"table": "foo.bar", "schema_version": 1, "rows": []}), encoding="utf-8"
    )

    proc = run_cli(dll, ["--data-root", str(tmp_path), "--json"])

    assert proc.returncode == 1
    result = json.loads(proc.stdout)
    assert result["blocking"] is True
    assert result["errors"] == len(result["issues"]) >= 1
    assert result["issues"][0]["table"] == "foo.bar" and result["issues"][0]["check"] == "envelope"


def test_valid_data_root_exits_0_and_strict_promotes_warnings_to_blocking(dll):
    plain = run_cli(dll, ["--data-root", VALID_DATA_ROOT])
    assert plain.returncode == 0, plain.stdout[-800:]
    _, _, errors, warnings, _ = _summary(plain.stdout)
    assert errors == 0
    # 前置：该数据集带已知探针警告；--strict 是否阻断取决于 warnings 是否 > 0，期望由输出自身推出。
    assert warnings > 0, "core/sim/tests/data 应带已确认的探针警告（AGENTS.md G1），--strict 断言依赖它"

    strict = run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--strict"])
    assert strict.returncode == 1
    assert _summary(strict.stdout)[3] == warnings


# ---------------------------------------------------------------- 各开关的可观测行为


def test_list_tables_text_lists_every_loaded_table_with_owner_line(dll):
    proc = run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--list-tables"])

    assert proc.returncode == 0
    tables, records, *_ = _summary(proc.stdout)
    table_lines = re.findall(r"^table: (\S+) \((\d+) 条记录\)$", proc.stdout, re.M)
    assert len(table_lines) == tables
    assert sum(int(n) for _, n in table_lines) == records
    names = [name for name, _ in table_lines]
    assert names == sorted(names)  # Ordinal 排序，输出确定
    assert "skill.def" in names
    assert len(re.findall(r"^  owner: layer=\S+ module=\S+ domain=\S+ time_scope=\S+$", proc.stdout, re.M)) == tables


def test_list_tables_json_carries_owner_and_field_metadata(dll):
    proc = run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--list-tables", "--json"])

    assert proc.returncode == 0
    result = json.loads(proc.stdout)
    listed = result["tables_list"]
    assert len(listed) == result["tables"]
    assert [t["name"] for t in listed] == sorted(t["name"] for t in listed)
    assert sum(t["record_count"] for t in listed) == result["records"]
    skill = next(t for t in listed if t["name"] == "skill.def")
    assert skill["fields"] and skill["domain"] == "skill"
    for key in ("layer", "module", "time_scope", "field_meta", "field_ranges", "schema_version", "migrations"):
        assert key in skill


def test_display_map_sources_overrides_default_list(dll):
    default = json.loads(run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--json"]).stdout)
    default_tables = [s["table"] for s in default["display_map_coverage_sources"]]
    assert len(default_tables) > 1 and "skill.def" in default_tables

    proc = run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--json", "--display-map-sources", "skill.def:id"])

    assert proc.returncode == 0
    result = json.loads(proc.stdout)
    # 传了则完整替换默认清单（不是追加）。
    assert result["display_map_coverage_sources"] == [{"table": "skill.def", "id_field": "id"}]
    assert "DisplayMapCoverageRule" in result["enabled_optional_rules"]


def test_enable_graph_isolation_turns_on_two_default_off_rules(dll):
    off = run_cli(dll, ["--data-root", VALID_DATA_ROOT])
    on = run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--enable-graph-isolation"])

    assert off.returncode == 0 and on.returncode == 0
    off_line = re.search(r"^optional rules disabled: (.*)$", off.stdout, re.M)
    on_line = re.search(r"^optional rules disabled: (.*)$", on.stdout, re.M)
    assert off_line and on_line
    isolation_rules = {"QuestPrerequisiteIsolationRule", "TalentTreeIsolationRule"}
    assert set(off_line.group(1).split(", ")) == isolation_rules
    assert on_line.group(1) == "none"

    def rule_count(stdout: str) -> int:
        return int(re.search(r"^rules \((\d+)\):$", stdout, re.M).group(1))

    assert rule_count(on.stdout) == rule_count(off.stdout) + len(isolation_rules)
    on_json = json.loads(run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--json", "--enable-graph-isolation"]).stdout)
    assert isolation_rules <= set(on_json["enabled_optional_rules"])
    assert on_json["disabled_optional_rules"] == []


def test_no_missing_translation_warning_flag_is_reported(dll):
    default = run_cli(dll, ["--data-root", VALID_DATA_ROOT])
    off = run_cli(dll, ["--data-root", VALID_DATA_ROOT, "--no-missing-translation-warning"])

    assert "missing translation warning: enabled" in default.stdout
    assert "missing translation warning: disabled" in off.stdout


# ---------------------------------------------------------------- --schema-audit / --allowlist


def _repo_allowlist_entries() -> list[dict]:
    return json.loads(REPO_ALLOWLIST.read_text(encoding="utf-8"))["entries"]


def _write_allowlist(path: Path, entries: list[dict]) -> Path:
    path.write_text(json.dumps({"entries": entries}, ensure_ascii=False), encoding="utf-8")
    return path


def test_schema_audit_with_repo_allowlist_exits_0(dll):
    proc = run_cli(dll, ["--schema-audit", "--allowlist", str(REPO_ALLOWLIST)])

    assert proc.returncode == 0, proc.stdout[-800:]
    tables, fields, errors, warnings = _audit_summary(proc.stdout)
    assert tables > 0 and fields > 0 and errors == 0 and warnings == 0


def test_schema_audit_without_allowlist_reports_one_located_error_per_allowlist_entry(dll):
    entries = _repo_allowlist_entries()

    proc = run_cli(dll, ["--schema-audit"])

    assert proc.returncode == 1
    _, _, errors, _ = _audit_summary(proc.stdout)
    hits = re.findall(r"^\[error\] (\S+?)/(\S+): composite_without_substructure: ", proc.stdout, re.M)
    # 期望由规则算出：仓库白名单恰好豁免全部 composite_without_substructure 命中（白名单头注释的契约）。
    assert errors == len(hits) == len(entries)
    assert {(t, f) for t, f in hits} == {(e["table"], e["field"]) for e in entries}


def test_schema_audit_allowlist_missing_one_entry_fails_with_that_location(dll, tmp_path):
    entries = _repo_allowlist_entries()
    dropped = entries[0]
    broken = _write_allowlist(tmp_path / "allowlist.json", entries[1:])

    proc = run_cli(dll, ["--schema-audit", "--allowlist", str(broken)])

    assert proc.returncode == 1
    assert f"[error] {dropped['table']}/{dropped['field']}: composite_without_substructure:" in proc.stdout
    assert _audit_summary(proc.stdout)[2] == 1


def test_schema_audit_json_output_lists_issue_location(dll, tmp_path):
    entries = _repo_allowlist_entries()
    dropped = entries[0]
    broken = _write_allowlist(tmp_path / "allowlist.json", entries[1:])

    proc = run_cli(dll, ["--schema-audit", "--allowlist", str(broken), "--json"])

    assert proc.returncode == 1
    result = json.loads(proc.stdout)
    assert result["blocking"] is True and result["errors"] == 1
    issue = result["issues"][0]
    assert (issue["table"], issue["field_path"], issue["check"]) == (
        dropped["table"], dropped["field"], "composite_without_substructure"
    )
    assert result["tables"] == len(result["table_names"])


def test_schema_audit_stale_allowlist_entry_is_warning_not_blocking(dll, tmp_path):
    stale = {"table": "no.such_table", "field": "no_such_field", "reason": "过期条目：表已不存在"}
    extended = _write_allowlist(tmp_path / "allowlist.json", _repo_allowlist_entries() + [stale])

    proc = run_cli(dll, ["--schema-audit", "--allowlist", str(extended)])

    assert proc.returncode == 0
    assert "[warning] no.such_table/no_such_field: allowlist_entry_unused:" in proc.stdout
    _, _, errors, warnings = _audit_summary(proc.stdout)
    assert (errors, warnings) == (0, 1)


def test_schema_audit_missing_allowlist_file_exits_2(dll, tmp_path):
    proc = run_cli(dll, ["--schema-audit", "--allowlist", str(tmp_path / "nope.json")])
    assert proc.returncode == 2
    assert "读取白名单文件失败" in proc.stderr


def test_schema_audit_allowlist_path_is_directory_exits_2(dll, tmp_path):
    proc = run_cli(dll, ["--schema-audit", "--allowlist", str(tmp_path)])
    assert proc.returncode == 2
    assert "读取白名单文件失败" in proc.stderr


@pytest.mark.parametrize(
    "content,fragment",
    [
        ("[]", "顶层结构必须是"),
        ('{"entries": {}}', "顶层结构必须是"),
        ('{"entries": [5]}', "entries[0] 不是对象"),
        ('{"entries": [{"table": "x", "field": "y"}]}', '"reason"'),
        ('{"entries": [{"table": "x", "field": "", "reason": "r"}]}', '"field"'),
    ],
)
def test_schema_audit_structurally_invalid_allowlist_exits_2(dll, tmp_path, content, fragment):
    bad = tmp_path / "allowlist.json"
    bad.write_text(content, encoding="utf-8")

    proc = run_cli(dll, ["--schema-audit", "--allowlist", str(bad)])

    assert proc.returncode == 2
    assert "白名单文件格式非法" in proc.stderr and fragment in proc.stderr


def test_schema_audit_non_json_allowlist_should_exit_2_not_crash(dll, tmp_path):
    bad = tmp_path / "allowlist.json"
    bad.write_text("{bad", encoding="utf-8")

    proc = run_cli(dll, ["--schema-audit", "--allowlist", str(bad)])

    assert "Unhandled exception" not in proc.stderr
    assert proc.returncode == 2
