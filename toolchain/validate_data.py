#!/usr/bin/env python3
"""数据表校验器（阶段 0 骨架）。

用途：遍历 ``--data-root`` 下的全部数据表 JSON 文件，跑一遍与具体游戏内容
无关的"骨架级通用检查"：JSON 是否合法、顶层信封三键（``table`` /
``schema_version`` / ``rows``）是否齐全、``table`` 是否与文件名一致、每条
记录的主键（``id`` 或 ``l10n.text`` 表的 ``key``/``locale``）是否符合
id 格式与 domain 前缀约定。

本脚本只是阶段 0（T0-7）的骨架，字段规范与校验项的权威定义见
``architecture/04_数据与内容管线.md``：

- 引用完整性（第 5 节）——本阶段未实现，需要先跑通 DataRegistry 的
  ``declareReference`` 机制才能校验外键。
- 枚举合法（第 5 节）——本阶段未实现，需要各表登记枚举字段的合法取值集合。
- 表达式可解析（第 5 节）——本阶段未实现，需要先接入 Expr 语法解析器（第 6 节）。
- 外形映射存在 / 外形类型字段组完整（第 5 节、第 7.1 节）——本阶段未实现。
- 文本键存在（第 5 节）——本阶段未实现，需要跨文件核对 ``l10n.text``。
- schema 版本已知（第 3 节）——本阶段只检查 ``schema_version`` 是正整数，
  不检查是否落在已登记的迁移链范围内（本阶段没有迁移链登记表）。
- 循环引用检测、孤儿记录检测、时间字段与时间模型一致（第 3.1 节、第 5 节）——
  本阶段未实现。

以上检查项将在后续阶段随 DataRegistry 与 Expr 解析器一起逐项加入
``CHECKS`` 列表。

返回码约定：
    0 —— 全部文件通过检查，无错误。
    1 —— 至少一项检查失败。
    2 —— 命令行参数错误（含 ``--data-root``/``--dataset`` 指向不存在的目录）。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any, Callable, Iterable

# id 格式：见 architecture/04_数据与内容管线.md 第 2.1 节。
# <domain>.<segment>(.<segment>)*，domain 与各 segment 均为小写字母/数字/下划线，
# domain 首字符必须是字母。
ID_RE = re.compile(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")

# 需要特殊主键处理的表名（复合主键 key+locale，见 04 第 7.2 节）。
L10N_TEXT_TABLE = "l10n.text"


class FileErrors(list):
    """收集单个文件的错误信息（字符串列表的轻量别名，便于类型提示）。"""


def check_envelope(path: Path, data: Any) -> FileErrors:
    """检查顶层信封三键是否存在且类型正确：table(str)/schema_version(正整数)/rows(数组)。"""
    errors = FileErrors()
    if not isinstance(data, dict):
        errors.append("顶层结构必须是 JSON 对象（含 table/schema_version/rows）")
        return errors

    if "table" not in data:
        errors.append("缺少顶层字段 table")
    elif not isinstance(data["table"], str):
        errors.append("顶层字段 table 必须是字符串")

    if "schema_version" not in data:
        errors.append("缺少顶层字段 schema_version")
    else:
        sv = data["schema_version"]
        if isinstance(sv, bool) or not isinstance(sv, int) or sv < 1:
            errors.append("顶层字段 schema_version 必须是从 1 起的正整数")

    if "rows" not in data:
        errors.append("缺少顶层字段 rows")
    elif not isinstance(data["rows"], list):
        errors.append("顶层字段 rows 必须是数组")

    return errors


def check_table_matches_filename(path: Path, data: Any) -> FileErrors:
    """检查 table 字段是否等于文件名（不含扩展名）。"""
    errors = FileErrors()
    if not isinstance(data, dict):
        return errors
    table = data.get("table")
    if not isinstance(table, str):
        return errors  # check_envelope 已经报过类型错误，这里不重复报
    expected = path.stem
    if table != expected:
        errors.append(f"table 字段 '{table}' 与文件名 '{expected}' 不一致")
    return errors


def _check_id_value(id_value: Any, domain_prefix: str, field_name: str) -> str | None:
    """校验单个 id/key 取值：必须是字符串，匹配 ID_RE，且首段等于 domain_prefix。"""
    if not isinstance(id_value, str):
        return f"字段 {field_name} 必须是字符串"
    if not ID_RE.match(id_value):
        return f"字段 {field_name} 取值 '{id_value}' 不符合 id 格式 (^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$)"
    first_segment = id_value.split(".", 1)[0]
    if first_segment != domain_prefix:
        return (
            f"字段 {field_name} 取值 '{id_value}' 的 domain 前缀 '{first_segment}' "
            f"应等于表名首段 '{domain_prefix}'"
        )
    return None


def check_rows(path: Path, data: Any) -> FileErrors:
    """检查 rows 中每条记录：必须是对象；含 id 则校验其格式；l10n.text 表另行校验 key/locale。"""
    errors = FileErrors()
    if not isinstance(data, dict):
        return errors
    rows = data.get("rows")
    if not isinstance(rows, list):
        return errors  # check_envelope 已经报过

    table = data.get("table")
    table = table if isinstance(table, str) else path.stem
    domain_prefix = table.split(".", 1)[0] if table else path.stem.split(".", 1)[0]
    is_l10n_text = table == L10N_TEXT_TABLE

    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            errors.append(f"rows[{index}] 必须是对象")
            continue

        if is_l10n_text:
            if "key" not in row:
                errors.append(f"rows[{index}] 缺少字段 key（l10n.text 表要求复合主键 key+locale）")
            else:
                key = row["key"]
                if not isinstance(key, str) or not key.startswith("l10n."):
                    errors.append(f"rows[{index}] 字段 key 取值 '{key}' 必须以 'l10n.' 开头")
                elif not ID_RE.match(key):
                    errors.append(
                        f"rows[{index}] 字段 key 取值 '{key}' 不符合 id 格式 "
                        f"(^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$)"
                    )
            if "locale" not in row:
                errors.append(f"rows[{index}] 缺少字段 locale（l10n.text 表要求复合主键 key+locale）")
        elif "id" in row:
            msg = _check_id_value(row["id"], domain_prefix, "id")
            if msg:
                errors.append(f"rows[{index}] {msg}")

    return errors


# 骨架级通用检查列表：每个检查函数签名为 (path, data) -> FileErrors。
# 后续阶段在此追加引用完整性、枚举合法、表达式可解析等领域检查。
CHECKS: list[Callable[[Path, Any], FileErrors]] = [
    check_envelope,
    check_table_matches_filename,
    check_rows,
]


def find_repo_root() -> Path:
    """仓库根 = 本脚本所在目录（toolchain/）的上一级，不依赖当前工作目录。"""
    return Path(__file__).resolve().parent.parent


def iter_json_files(root: Path) -> Iterable[Path]:
    yield from sorted(root.rglob("*.json"))


def validate_file(path: Path, verbose: bool) -> list[str]:
    """校验单个文件，返回该文件的错误信息列表（已去除路径前缀，纯说明文字）。"""
    try:
        text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError as exc:
        return [f"文件编码错误，要求 UTF-8：{exc}"]

    try:
        data = json.loads(text)
    except json.JSONDecodeError as exc:
        return [f"JSON 解析失败：{exc}"]

    if verbose:
        print(f"[verbose] 解析成功: {path}", file=sys.stderr)

    errors: list[str] = []
    for check in CHECKS:
        errors.extend(check(path, data))
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="数据表校验器（阶段 0 骨架，只做骨架级通用检查）"
    )
    parser.add_argument(
        "--data-root",
        default="data",
        help="数据根目录，相对仓库根解析（默认 data）；也可传绝对路径",
    )
    parser.add_argument(
        "--dataset",
        default=None,
        help="只校验 <data-root>/<dataset>/ 下的表；省略则校验整个 data-root",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="输出更详细的检查过程信息（写入标准错误）",
    )

    try:
        args = parser.parse_args(argv)
    except SystemExit as exc:
        # argparse 在参数解析失败时自身会以 code 2 退出，这里保持约定一致。
        return exc.code if isinstance(exc.code, int) else 2

    repo_root = find_repo_root()

    data_root_arg = Path(args.data_root)
    data_root = data_root_arg if data_root_arg.is_absolute() else (repo_root / data_root_arg)

    if args.dataset:
        target_root = data_root / args.dataset
    else:
        target_root = data_root

    if not target_root.exists():
        print(f"参数错误：目录不存在: {target_root}", file=sys.stderr)
        return 2
    if not target_root.is_dir():
        print(f"参数错误：不是目录: {target_root}", file=sys.stderr)
        return 2

    total_files = 0
    total_errors = 0

    for path in iter_json_files(target_root):
        total_files += 1
        rel_path = path.relative_to(repo_root) if _is_relative_to(path, repo_root) else path
        file_errors = validate_file(path, args.verbose)
        for message in file_errors:
            print(f"{rel_path}: {message}")
            total_errors += 1

    print(f"checked {total_files} files, {total_errors} errors")
    return 1 if total_errors > 0 else 0


def _is_relative_to(path: Path, other: Path) -> bool:
    try:
        path.relative_to(other)
        return True
    except ValueError:
        return False


if __name__ == "__main__":
    sys.exit(main())
