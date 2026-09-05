#!/usr/bin/env python3
"""事件常量生成器（T1-8）。

用途：读取事件词汇登记表 ``found.event_catalog``（默认
``data/_framework/found/found.event_catalog.json``——判定为框架级数据表，见
``data/README.md``"框架级数据表与游戏数据目录并列加载"一节：本表的行由
``EventKeys.g.cs`` 常量硬引用），为其中每一行生成一个
C# 强类型常量（``Core.Foundation.Common.Id``），写入
``core/foundation/event_bus/generated/EventKeys.g.cs``（默认路径）。

生成文件是**生成物，不可手改**：修改事件登记表后重新运行本脚本；提交门槛用
``--check`` 比较生成内容与已提交文件是否一致（不一致返回非 0）。

数据表信封与字段规范见 ``core/foundation/event_bus/schema/found.event_catalog.md``；
``key`` 字段格式为 ``^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$``
（见 ``core/foundation/common/contracts/Id.cs``）。本脚本对 ``key`` 做与
``Id`` 构造函数相同的格式校验，双保险——即便格式非法的行漏过本脚本，
``EventKeys`` 类型初始化时 ``Id`` 构造函数仍会抛出异常。

常量命名规则：``key`` 按 ``.`` 切分为多段，每段再按 ``_`` 切分为单词，
全部单词依次首字母大写后拼接（不保留分隔符），例如：

- ``skill.cast_start``               -> ``SkillCastStart``
- ``combat.damage_dealt``            -> ``CombatDamageDealt``
- ``display_info.reloaded``          -> ``DisplayInfoReloaded``
- ``presentation.playback_finished`` -> ``PresentationPlaybackFinished``

若两个不同的 ``key`` 拼出同一个常量名，视为数据错误，返回码 1。

返回码约定：
    0 —— 生成/校验成功。
    1 —— 数据错误（信封非法、key 格式非法、重复 key、常量名冲突）或
          ``--check`` 模式下生成内容与现有文件不一致。
    2 —— 命令行参数错误（如 ``--catalog`` 指向不存在的文件）。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

# 与 core/foundation/common/contracts/Id.cs 的 FormatRegex 保持一致。
KEY_RE = re.compile(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")

EXPECTED_TABLE = "found.event_catalog"

DEFAULT_CATALOG = "data/_framework/found/found.event_catalog.json"
DEFAULT_OUTPUT = "core/foundation/event_bus/generated/EventKeys.g.cs"
DEFAULT_NAMESPACE = "Core.Foundation.EventBus"


class DataError(Exception):
    """数据/一致性错误：信封非法、key 非法、重复 key、常量名冲突等。"""


def find_repo_root() -> Path:
    """仓库根 = 本脚本所在目录（toolchain/）的上一级，不依赖当前工作目录。"""
    return Path(__file__).resolve().parent.parent


def _resolve(repo_root: Path, path_arg: str) -> Path:
    p = Path(path_arg)
    return p if p.is_absolute() else (repo_root / p)


def load_catalog(catalog_path: Path) -> list[dict[str, Any]]:
    """读取并校验信封，返回 rows 列表（未做逐行 key 校验，见 validate_rows）。"""
    text = catalog_path.read_text(encoding="utf-8")
    try:
        data = json.loads(text)
    except json.JSONDecodeError as exc:
        raise DataError(f"JSON 解析失败：{exc}") from exc

    if not isinstance(data, dict):
        raise DataError("顶层结构必须是 JSON 对象（含 table/schema_version/rows）")

    table = data.get("table")
    if table != EXPECTED_TABLE:
        raise DataError(
            f"顶层字段 table 应为 '{EXPECTED_TABLE}'，实际为 {table!r}"
        )

    rows = data.get("rows")
    if not isinstance(rows, list):
        raise DataError("顶层字段 rows 必须是数组")

    return rows


def pascal_case_from_key(key: str) -> str:
    """把 key（'领域.名称' 多段，段内可含下划线）转成 PascalCase 常量名。"""
    words: list[str] = []
    for segment in key.split("."):
        for word in segment.split("_"):
            if not word:
                continue
            words.append(word[0].upper() + word[1:])
    return "".join(words)


def escape_xml(text: str) -> str:
    """XML 文本内容转义：& 必须最先处理，避免二次转义已生成的实体。"""
    return text.replace("&", "&amp;").replace("<", "&lt;")


class EventRow:
    __slots__ = ("key", "fields", "description", "const_name")

    def __init__(self, key: str, fields: list[str], description: str | None):
        self.key = key
        self.fields = fields
        self.description = description
        self.const_name = pascal_case_from_key(key)


def validate_rows(rows: list[dict[str, Any]]) -> list[EventRow]:
    """校验每行并返回 EventRow 列表（未排序，未做常量名冲突检查）。"""
    seen_keys: set[str] = set()
    result: list[EventRow] = []

    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            raise DataError(f"rows[{index}] 必须是对象")

        key = row.get("key")
        if not isinstance(key, str) or not KEY_RE.match(key):
            raise DataError(
                f"rows[{index}] 字段 key 取值 {key!r} 不符合 id 格式 "
                f"(^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$)"
            )

        if key in seen_keys:
            raise DataError(f"重复的 key：'{key}'")
        seen_keys.add(key)

        fields = row.get("fields")
        if fields is None:
            fields = []
        if not isinstance(fields, list) or not all(isinstance(f, str) for f in fields):
            raise DataError(f"rows[{index}]（key='{key}'）字段 fields 必须是字符串数组")

        description = row.get("description")
        if description is not None and not isinstance(description, str):
            raise DataError(f"rows[{index}]（key='{key}'）字段 description 必须是字符串或 null")

        result.append(EventRow(key, fields, description))

    return result


def check_name_collisions(rows: list[EventRow]) -> None:
    seen: dict[str, str] = {}
    for row in rows:
        prior = seen.get(row.const_name)
        if prior is not None:
            raise DataError(
                f"常量名冲突：key '{prior}' 与 '{row.key}' 都生成常量名 '{row.const_name}'"
            )
        seen[row.const_name] = row.key


def build_summary(row: EventRow) -> str:
    fields_part = ", ".join(row.fields) if row.fields else "无字段"
    summary = f"{row.key} — 字段：{fields_part}。"
    if row.description:
        desc = row.description
        summary += desc if desc.endswith("。") else f"{desc}。"
    return escape_xml(summary)


def render(rows: list[EventRow], namespace: str, catalog_rel: str) -> str:
    """生成确定性的 C# 源码文本（LF 换行，末尾一个换行符，无 BOM）。"""
    sorted_rows = sorted(rows, key=lambda r: r.key)

    lines: list[str] = []
    lines.append("// <auto-generated>")
    lines.append(f"// 由 toolchain/gen_event_constants.py 从 {catalog_rel} 生成。")
    lines.append("// 生成物不可手改：修改事件登记表后重新运行脚本；提交门槛用 --check 核对。")
    lines.append("// </auto-generated>")
    lines.append("using Core.Foundation.Common;")
    lines.append("")
    lines.append(f"namespace {namespace}")
    lines.append("{")
    lines.append("    /// <summary>事件词汇登记表中全部事件 key 的强类型常量。</summary>")
    lines.append("    public static class EventKeys")
    lines.append("    {")

    for row in sorted_rows:
        lines.append(f"        /// <summary>{build_summary(row)}</summary>")
        lines.append(
            f'        public static readonly Id {row.const_name} = new Id("{row.key}");'
        )
        lines.append("")

    lines.append("        /// <summary>全部已登记事件 key（按序数排序）。</summary>")
    lines.append("        public static readonly Id[] All =")
    lines.append("        {")
    for row in sorted_rows:
        lines.append(f"            {row.const_name},")
    lines.append("        };")
    lines.append("    }")
    lines.append("}")
    lines.append("")

    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="从事件词汇登记表生成 C# 事件 key 常量（EventKeys.g.cs）"
    )
    parser.add_argument(
        "--catalog",
        default=DEFAULT_CATALOG,
        help=f"事件词汇登记表 JSON 路径，相对仓库根解析（默认 {DEFAULT_CATALOG}）",
    )
    parser.add_argument(
        "--output",
        default=DEFAULT_OUTPUT,
        help=f"生成文件输出路径，相对仓库根解析（默认 {DEFAULT_OUTPUT}）",
    )
    parser.add_argument(
        "--namespace",
        default=DEFAULT_NAMESPACE,
        help=f"生成类型所在命名空间（默认 {DEFAULT_NAMESPACE}）",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="不写文件，只比较生成内容与 --output 现有文件是否一致；不一致返回码 1",
    )

    try:
        args = parser.parse_args(argv)
    except SystemExit as exc:
        return exc.code if isinstance(exc.code, int) else 2

    repo_root = find_repo_root()
    catalog_path = _resolve(repo_root, args.catalog)
    output_path = _resolve(repo_root, args.output)

    if not catalog_path.exists():
        print(f"参数错误：--catalog 指向的文件不存在: {catalog_path}", file=sys.stderr)
        return 2
    if not catalog_path.is_file():
        print(f"参数错误：--catalog 不是文件: {catalog_path}", file=sys.stderr)
        return 2

    try:
        rows_raw = load_catalog(catalog_path)
        rows = validate_rows(rows_raw)
        check_name_collisions(rows)
    except DataError as exc:
        print(f"数据错误：{exc}", file=sys.stderr)
        return 1

    try:
        catalog_rel = catalog_path.relative_to(repo_root).as_posix()
    except ValueError:
        catalog_rel = catalog_path.as_posix()

    content = render(rows, args.namespace, catalog_rel)

    if args.check:
        if not output_path.exists():
            print(f"check 失败：输出文件不存在: {output_path}")
            return 1
        existing = output_path.read_text(encoding="utf-8")
        if existing != content:
            print(f"check 失败：生成内容与现有文件不一致: {output_path}")
            return 1
        print(f"check 通过：{len(rows)} 个常量与 {output_path} 一致")
        return 0

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(content, encoding="utf-8", newline="\n")
    print(f"generated {len(rows)} constants")
    return 0


if __name__ == "__main__":
    sys.exit(main())
