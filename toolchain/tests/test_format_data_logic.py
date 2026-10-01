"""``toolchain/format_data.py`` 内部纯逻辑的单元测试（测试覆盖第四批 I-10，2026-10-01）。

既有 ``test_format_data_schema_order.py`` 以场景为主；这里直接 ``import`` 模块，对被拆开的函数逐个断言边界：

- ``_reorder_row``：已登记字段按 schema 顺序、未登记字段保持相对顺序追加在后、行里没有的登记字段不凭空添加、不丢字段。
- ``_build_validator_cmd``：优先预编译 ``bin/Validator.dll``，缺失退回 ``dotnet run --project``；
  每个数据根各带一个 ``--data-root``；找不到 dotnet 抛 RuntimeError。
  （用 tmp 里的模块副本控制 ``validator/bin`` 是否存在，不依赖真实仓库当前是否编译过。）
- ``get_field_order_map``：从伪造的 validator 输出取字段顺序与 schema_version（缺省 1）；
  非法 JSON / 缺 tables_list 抛 RuntimeError，不静默返回空表。
- ``process_file``：非法 JSON、非 dict、未登记的表、``rows`` 不是列表、行里混入非 dict、布尔型
  schema_version、版本落后（跳过）、版本相等/更高（照常重排）、已有序文件不重排。
- ``main``：缺 ``--schema-order``、数据根不存在、取字段顺序失败均返回 2；``--check`` 有需重排文件返回 1 且不写盘，
  无则 0；非 ``--check`` 就地改写；汇总行里的计数。

运行：``python -m pytest toolchain/tests/test_format_data_logic.py -q``。
"""

from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

import format_data as fd  # noqa: E402


def _module_copy(tmp_path: Path, name: str = "format_data_logic_copy"):
    """把 format_data.py 与 _console.py 复制到 tmp 下加载，使 ``__file__`` 相对路径落在可控目录里。"""
    root = tmp_path / "toolchain_copy"
    root.mkdir(parents=True, exist_ok=True)
    for fname in ("format_data.py", "_console.py"):
        shutil.copy2(TOOLCHAIN_DIR / fname, root / fname)
    spec = importlib.util.spec_from_file_location(name, root / "format_data.py")
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module, root


# ----------------------------------------------------------------------------
# _reorder_row
# ----------------------------------------------------------------------------

def test_reorder_registered_fields_follow_schema_and_unregistered_trail_in_original_order() -> None:
    row = {"z_extra": 1, "c": 3, "a": 1, "override": True, "b": 2, "y_extra": 0}
    out = fd._reorder_row(row, ["a", "b", "c"])
    assert list(out) == ["a", "b", "c", "z_extra", "override", "y_extra"]
    assert out == row, "只换顺序，值不变"


def test_reorder_does_not_invent_missing_registered_fields_or_drop_values() -> None:
    out = fd._reorder_row({"b": 2}, ["a", "b", "c"])
    assert out == {"b": 2}
    assert fd._reorder_row({}, ["a"]) == {}
    assert list(fd._reorder_row({"q": 1, "r": 2}, [])) == ["q", "r"]


# ----------------------------------------------------------------------------
# _build_validator_cmd
# ----------------------------------------------------------------------------

def test_validator_cmd_prefers_prebuilt_dll_when_present(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    module, root = _module_copy(tmp_path)
    (root / "validator" / "bin").mkdir(parents=True)
    dll = root / "validator" / "bin" / "Validator.dll"
    dll.write_bytes(b"MZ")
    monkeypatch.setattr(module.shutil, "which", lambda name: "C:/dotnet/dotnet.exe")
    cmd = module._build_validator_cmd(tmp_path, [tmp_path / "r1", tmp_path / "r2"])
    assert cmd[:2] == ["C:/dotnet/dotnet.exe", str(dll)]
    assert cmd[2:4] == ["--list-tables", "--json"]
    assert cmd[4:] == ["--data-root", str(tmp_path / "r1"), "--data-root", str(tmp_path / "r2")]


def test_validator_cmd_falls_back_to_dotnet_run_without_dll(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    module, root = _module_copy(tmp_path)
    monkeypatch.setattr(module.shutil, "which", lambda name: "dotnet")
    cmd = module._build_validator_cmd(tmp_path, [])
    assert cmd == ["dotnet", "run", "--project", str(root / "validator"), "--", "--list-tables", "--json"]


def test_validator_cmd_without_dotnet_raises(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    module, _ = _module_copy(tmp_path)
    monkeypatch.setattr(module.shutil, "which", lambda name: None)
    with pytest.raises(RuntimeError, match="dotnet"):
        module._build_validator_cmd(tmp_path, [])


# ----------------------------------------------------------------------------
# get_field_order_map
# ----------------------------------------------------------------------------

def _fake_run(stdout: str, returncode: int = 0):
    def run(cmd, **kwargs):
        return SimpleNamespace(stdout=stdout, stderr="", returncode=returncode)
    return run


@pytest.fixture
def patched(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    module, _ = _module_copy(tmp_path)
    monkeypatch.setattr(module.shutil, "which", lambda name: "dotnet")
    return module


def test_field_order_map_extracts_fields_and_defaults_schema_version(patched, tmp_path: Path, monkeypatch) -> None:
    payload = {"tables_list": [
        {"name": "t.a", "fields": ["x", "y"], "schema_version": 3},
        {"name": "t.b", "fields": ["p"]},                       # 旧版 validator：没有 schema_version
        {"name": "t.c", "fields": None, "schema_version": 0},   # fields 为 null -> 空列表；版本 0 兜底为 1
    ]}
    monkeypatch.setattr(patched.subprocess, "run", _fake_run(json.dumps(payload)))
    order, versions = patched.get_field_order_map(tmp_path, [tmp_path])
    assert order == {"t.a": ["x", "y"], "t.b": ["p"], "t.c": []}
    assert versions == {"t.a": 3, "t.b": 1, "t.c": 1}


def test_field_order_map_ignores_nonzero_exit_when_json_is_valid(patched, tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr(patched.subprocess, "run", _fake_run(json.dumps({"tables_list": []}), returncode=1))
    assert patched.get_field_order_map(tmp_path, [tmp_path]) == ({}, {})


def test_field_order_map_invalid_json_raises_with_diagnostics(patched, tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr(patched.subprocess, "run", _fake_run("not json at all", returncode=7))
    with pytest.raises(RuntimeError) as exc:
        patched.get_field_order_map(tmp_path, [tmp_path])
    assert "退出码 7" in str(exc.value) and "not json at all" in str(exc.value)


def test_field_order_map_missing_tables_list_raises(patched, tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr(patched.subprocess, "run", _fake_run(json.dumps({"other": 1})))
    with pytest.raises(RuntimeError, match="tables_list"):
        patched.get_field_order_map(tmp_path, [tmp_path])


# ----------------------------------------------------------------------------
# process_file
# ----------------------------------------------------------------------------

ORDER = {"t.items": ["id", "name", "cost"]}
VERSIONS = {"t.items": 2}


def _write_json(path: Path, obj) -> Path:
    path.write_text(json.dumps(obj, ensure_ascii=False), encoding="utf-8", newline="\n")
    return path


def test_process_file_reorders_and_normalizes_format(tmp_path: Path) -> None:
    f = _write_json(tmp_path / "a.json", {
        "table": "t.items", "schema_version": 2,
        "rows": [{"cost": 1, "id": "i1", "name": "N", "note": "未登记"}],
    })
    changed, text, skipped = fd.process_file(f, ORDER, VERSIONS, False)
    assert (changed, skipped) == (True, False)
    assert text.endswith("\n") and "\r" not in text
    parsed = json.loads(text)
    assert list(parsed["rows"][0]) == ["id", "name", "cost", "note"]
    assert parsed["table"] == "t.items" and parsed["schema_version"] == 2
    assert text.startswith('{\n  "table"'), "2 空格缩进、每字段各占一行"
    assert "未登记" in text, "ensure_ascii=False，不转义中文"


def test_process_file_already_ordered_is_untouched_even_if_formatting_differs(tmp_path: Path) -> None:
    f = _write_json(tmp_path / "a.json", {"table": "t.items", "schema_version": 2, "rows": [{"id": "i", "name": "n"}]})
    original = f.read_text(encoding="utf-8")
    changed, text, skipped = fd.process_file(f, ORDER, VERSIONS, False)
    assert (changed, text, skipped) == (False, original, False)


@pytest.mark.parametrize("content", ["{ broken", "[1,2,3]", '"just a string"', "42"])
def test_process_file_ignores_non_envelope_content(tmp_path: Path, content: str) -> None:
    f = tmp_path / "a.json"
    f.write_text(content, encoding="utf-8")
    assert fd.process_file(f, ORDER, VERSIONS, False) == (False, content, False)


@pytest.mark.parametrize("envelope", [
    {"table": "t.unknown", "schema_version": 1, "rows": [{"b": 1, "a": 2}]},   # 未登记的表
    {"table": 123, "rows": [{"b": 1, "a": 2}]},                               # table 不是字符串
    {"rows": [{"b": 1}]},                                                      # 无 table
    {"table": "t.items", "schema_version": 2, "rows": {"cost": 1, "id": 2}},   # rows 不是列表
    {"table": "t.items", "schema_version": 2},                                 # 无 rows
])
def test_process_file_leaves_unrecognized_shapes_alone(tmp_path: Path, envelope: dict) -> None:
    f = _write_json(tmp_path / "a.json", envelope)
    changed, _text, skipped = fd.process_file(f, ORDER, VERSIONS, False)
    assert (changed, skipped) == (False, False)


def test_process_file_low_version_is_skipped_and_flagged(tmp_path: Path) -> None:
    f = _write_json(tmp_path / "a.json", {"table": "t.items", "schema_version": 1, "rows": [{"cost": 1, "id": "i"}]})
    changed, _text, skipped = fd.process_file(f, ORDER, VERSIONS, False)
    assert (changed, skipped) == (False, True)


@pytest.mark.parametrize("file_version", [2, 3])
def test_process_file_equal_or_higher_version_is_still_reordered(tmp_path: Path, file_version: int) -> None:
    f = _write_json(tmp_path / "a.json", {"table": "t.items", "schema_version": file_version, "rows": [{"cost": 1, "id": "i"}]})
    changed, _text, skipped = fd.process_file(f, ORDER, VERSIONS, False)
    assert (changed, skipped) == (True, False)


@pytest.mark.parametrize("bad_version", [True, "1", 1.0, None])
def test_process_file_non_int_schema_version_is_not_treated_as_low(tmp_path: Path, bad_version) -> None:
    """布尔（True == 1 在 Python 里是 int 子类）和字符串/浮点/缺省都不得被当成"版本落后"而静默跳过。"""
    f = _write_json(tmp_path / "a.json", {"table": "t.items", "schema_version": bad_version, "rows": [{"cost": 1, "id": "i"}]})
    changed, _text, skipped = fd.process_file(f, ORDER, VERSIONS, False)
    assert (changed, skipped) == (True, False)


def test_process_file_keeps_non_dict_rows_in_place(tmp_path: Path) -> None:
    f = _write_json(tmp_path / "a.json", {"table": "t.items", "schema_version": 2, "rows": [7, {"cost": 1, "id": "i"}, "s"]})
    changed, text, _ = fd.process_file(f, ORDER, VERSIONS, False)
    assert changed is True
    rows = json.loads(text)["rows"]
    assert rows[0] == 7 and rows[2] == "s" and list(rows[1]) == ["id", "cost"]


def test_process_file_verbose_reports_skips_on_stderr(tmp_path: Path, capsys) -> None:
    f = _write_json(tmp_path / "a.json", {"table": "t.unknown", "rows": []})
    fd.process_file(f, ORDER, VERSIONS, True)
    assert "跳过（未登记字段顺序信息）" in capsys.readouterr().err
    low = _write_json(tmp_path / "b.json", {"table": "t.items", "schema_version": 1, "rows": []})
    fd.process_file(low, ORDER, VERSIONS, True)
    assert "低于表 't.items' 当前版本 2" in capsys.readouterr().err


# ----------------------------------------------------------------------------
# main
# ----------------------------------------------------------------------------

def _main_env(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, order=None, versions=None):
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(fd, "get_field_order_map", lambda repo, roots: (order or ORDER, versions or VERSIONS))


def _data_root(tmp_path: Path) -> Path:
    root = tmp_path / "data_root"
    root.mkdir()
    _write_json(root / "scrambled.json", {"table": "t.items", "schema_version": 2, "rows": [{"cost": 1, "id": "i"}]})
    _write_json(root / "ordered.json", {"table": "t.items", "schema_version": 2, "rows": [{"id": "i", "cost": 1}]})
    _write_json(root / "old.json", {"table": "t.items", "schema_version": 1, "rows": [{"cost": 1, "id": "i"}]})
    return root


def test_main_requires_schema_order_flag(capsys) -> None:
    assert fd.main([]) == 2
    assert "必须传 --schema-order" in capsys.readouterr().err


def test_main_missing_data_root_is_parameter_error(tmp_path: Path, monkeypatch, capsys) -> None:
    _main_env(tmp_path, monkeypatch)
    assert fd.main(["--schema-order", "--data-root", str(tmp_path / "nope")]) == 2
    assert "目录不存在" in capsys.readouterr().err


def test_main_validator_failure_is_parameter_error(tmp_path: Path, monkeypatch, capsys) -> None:
    root = _data_root(tmp_path)
    monkeypatch.chdir(tmp_path)

    def boom(repo, roots):
        raise RuntimeError("validator 挂了")

    monkeypatch.setattr(fd, "get_field_order_map", boom)
    assert fd.main(["--schema-order", "--data-root", str(root)]) == 2
    assert "validator 挂了" in capsys.readouterr().err


def test_main_check_mode_reports_without_writing(tmp_path: Path, monkeypatch, capsys) -> None:
    root = _data_root(tmp_path)
    _main_env(tmp_path, monkeypatch)
    before = (root / "scrambled.json").read_text(encoding="utf-8")
    assert fd.main(["--schema-order", "--check", "--data-root", str(root)]) == 1
    out = capsys.readouterr().out
    assert "需要重排" in out and "scrambled.json" in out and "ordered.json" not in out.replace("scrambled.json", "")
    assert "共检查 3 个文件，需要重排 1 个，跳过版本落后 1 个" in out
    assert (root / "scrambled.json").read_text(encoding="utf-8") == before


def test_main_check_mode_clean_tree_returns_zero(tmp_path: Path, monkeypatch, capsys) -> None:
    root = tmp_path / "clean"
    root.mkdir()
    _write_json(root / "ordered.json", {"table": "t.items", "schema_version": 2, "rows": [{"id": "i"}]})
    _main_env(tmp_path, monkeypatch)
    assert fd.main(["--schema-order", "--check", "--data-root", str(root)]) == 0
    assert "需要重排 0 个" in capsys.readouterr().out


def test_main_rewrites_in_place_and_leaves_skipped_files_alone(tmp_path: Path, monkeypatch, capsys) -> None:
    root = _data_root(tmp_path)
    _main_env(tmp_path, monkeypatch)
    old_before = (root / "old.json").read_bytes()
    assert fd.main(["--schema-order", "--data-root", str(root)]) == 0
    out = capsys.readouterr().out
    assert "已重排" in out and "跳过（版本落后，未判断字段顺序）" in out
    assert list(json.loads((root / "scrambled.json").read_text(encoding="utf-8"))["rows"][0]) == ["id", "cost"]
    assert (root / "old.json").read_bytes() == old_before
    # 第二次运行即幂等
    assert fd.main(["--schema-order", "--check", "--data-root", str(root)]) == 0
