"""``toolchain/validate_data.py`` 跳过"非数据表 JSON 文件"的回归测试（数据根非数据表 JSON 误判
修复任务，2026-09-16）。

背景：第一道骨架检查此前对 ``--data-root`` 下递归找到的每个 ``*.json`` 文件一律当成数据表候选
解析——游戏侧仓库常见"数据目录旁边/上层还有 ``package.json`` 之类配置文件"的布局（典型如 UPM 包
根目录：``games/_template/package.json`` 与 ``games/_template/data/`` 同级），一旦 ``--data-root``
指向的目录把这些文件也递归进去（如误传 ``--data-root games/_template`` 而非
``games/_template/data/game``），会把 ``package.json`` 误判成表名 ``"package"`` 的数据表，报出
一堆"缺少顶层字段 table/schema_version/rows"的假错误（真实复现见
``python toolchain/validate_data.py --data-root data/_framework --data-root games/_template
--strict``）。

根治：``is_data_table_candidate``/``_resolve_table_domain`` 按 04 第 2.2 节"域"约定 + 三张单段名
命名例外（``camera_profile``/``ui_layout_definition``/``shell_menu_definition``，见
``core/foundation/data_registry/contracts/TableSchema.cs`` 判断记录）判定一个 JSON 文件是否"像数据
表"，不像的直接跳过（打一行 ``[skip] ...`` 到标准错误，不计入 ``files_checked``、不算错误/警告）。
C# 侧（第二道真实校验、以及游戏运行期 ``DataRegistry.LoadAll``）用的是同一条规则的独立实现，见
``core/foundation/data_registry/core/FileSystemDataSource.cs`` 与
``core/foundation/data_registry/tests/FileSystemDataSourceSkipsNonTableJsonTests.cs``。

本文件全部用例走 ``--skip-dotnet``（只跑第一道骨架检查，不依赖真实安装 dotnet），惯例同
``toolchain/tests/test_validate_data_relative_root_cwd.py``。

运行：``python -m pytest toolchain/tests/test_validate_data_skips_nontable_json.py -q`` 或
``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]


def _load_validate_data_module():
    """直接从仓库内真实路径加载（不拷贝出独立目录）——本文件只关心"骨架检查判定候选文件"这一件事，
    不涉及 ``find_repo_root()``/相对路径解析基准，不需要 ``test_validate_data_relative_root_cwd.py``
    那种"拷贝成独立部署目录"的手法。"""
    module_path = TOOLCHAIN_DIR / "validate_data.py"
    spec = importlib.util.spec_from_file_location("validate_data_under_test_skips_nontable_json", module_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _write_envelope(path: Path, table: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"table": table, "schema_version": 1, "rows": []}), encoding="utf-8")


def test_package_json_directly_under_root_is_skipped_not_reported_as_error(tmp_path: Path, capsys) -> None:
    """复现场景的最小化版本：数据根下混有一张合法数据表 + 一个 package.json，
    --skip-dotnet --strict 应当零错误，且 package.json 被打印为 [skip]，不计入 files_checked。"""
    module = _load_validate_data_module()
    data_root = tmp_path / "data_root"
    _write_envelope(data_root / "arch" / "arch.class.json", "arch.class")
    (data_root / "package.json").write_text(
        json.dumps({"name": "com.example.game", "version": "1.0.0"}), encoding="utf-8"
    )

    exit_code = module.main(["--data-root", str(data_root), "--strict", "--skip-dotnet"])

    assert exit_code == 0
    captured = capsys.readouterr()
    assert "package.json" in captured.err
    assert "[skip]" in captured.err
    assert "checked 1 files" in captured.err or "checked 1 files" in captured.out
    # package.json 本身不应该出现"缺少顶层字段"的错误行。
    assert "缺少顶层字段" not in captured.out


@pytest.mark.parametrize(
    ("domain_dir", "table_name"),
    [
        ("camera", "camera_profile"),
        ("ui", "ui_layout_definition"),
        ("shell", "shell_menu_definition"),
    ],
)
def test_single_segment_exception_tables_are_still_validated_not_skipped(
    tmp_path: Path, capsys, domain_dir: str, table_name: str
) -> None:
    """04 第 2.2 节登记的三张单段名命名例外表（文件名不含 "."）放在对应域目录下时，仍应正常参与
    骨架检查，不能被新规则误伤跳过——否则这三张真实数据表会悄悄失去骨架级校验覆盖。"""
    module = _load_validate_data_module()
    data_root = tmp_path / "data_root"
    _write_envelope(data_root / domain_dir / f"{table_name}.json", table_name)

    exit_code = module.main(["--data-root", str(data_root), "--strict", "--skip-dotnet"])

    assert exit_code == 0
    captured = capsys.readouterr()
    assert "[skip]" not in captured.err
    assert "checked 1 files" in captured.err or "checked 1 files" in captured.out


def test_dotted_file_misplaced_outside_matching_domain_directory_is_skipped(tmp_path: Path, capsys) -> None:
    """"文件名凑巧带合法域前缀、但没放在对应域目录下"的配置文件（如误放的 arch.config.json）不应被
    当成 arch 域的数据表去做骨架检查（会因缺 rows/schema_version 报假错误）。"""
    module = _load_validate_data_module()
    data_root = tmp_path / "data_root"
    (data_root / "tools").mkdir(parents=True)
    (data_root / "tools" / "arch.config.json").write_text(
        json.dumps({"some": "unrelated config, not a data table"}), encoding="utf-8"
    )

    exit_code = module.main(["--data-root", str(data_root), "--strict", "--skip-dotnet"])

    assert exit_code == 0
    captured = capsys.readouterr()
    assert "[skip]" in captured.err
    assert "arch.config.json" in captured.err


def test_flat_layout_fixture_still_validated_directly_under_root(tmp_path: Path, capsys) -> None:
    """既有测试夹具惯例（数据根下直接放 "test.thing.json"，不建 test/ 子目录）不受本次修复影响。"""
    module = _load_validate_data_module()
    data_root = tmp_path / "data_root"
    _write_envelope(data_root / "test.thing.json", "test.thing")

    exit_code = module.main(["--data-root", str(data_root), "--strict", "--skip-dotnet"])

    assert exit_code == 0
    captured = capsys.readouterr()
    assert "[skip]" not in captured.err


def test_is_data_table_candidate_unit_cases(tmp_path: Path) -> None:
    """直接对判定函数做单元覆盖，不经过 main() 的参数解析/打印路径。"""
    module = _load_validate_data_module()
    root = tmp_path

    assert module.is_data_table_candidate(root, root / "package.json") is False
    assert module.is_data_table_candidate(root, root / "arch" / "arch.class.json") is True
    assert module.is_data_table_candidate(root, root / "test.thing.json") is True
    assert module.is_data_table_candidate(root, root / "camera" / "camera_profile.json") is True
    assert module.is_data_table_candidate(root, root / "misc" / "camera_profile.json") is False
    assert module.is_data_table_candidate(root, root / "tools" / "arch.config.json") is False


if __name__ == "__main__":
    import sys

    sys.exit(pytest.main([__file__, "-q"]))
