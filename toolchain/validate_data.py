#!/usr/bin/env python3
"""数据表校验器（阶段 0 骨架 + 阶段 2 真实校验的入口封装）。

两道校验，串行执行：

1. **骨架级通用检查**（本文件自己实现，纯 Python 标准库）：遍历 ``--data-root``
   下的全部数据表 JSON 文件，检查与具体游戏内容无关的最基础形状——JSON 是否
   合法、顶层信封三键（``table`` / ``schema_version`` / ``rows``）是否齐全、
   ``table`` 是否与文件名一致、每条记录的主键（``id`` 或 ``l10n.text`` 表的
   ``key``/``locale``）是否符合 id 格式与 domain 前缀约定。
2. **真实校验**（子进程调用 ``toolchain/validator``，一个复用
   ``core/foundation/data_registry.DataRegistry`` 与
   ``core/rules/assembly.RulesSchemaCatalog`` 的 .NET 控制台工具）：字段级
   必填/类型/枚举、引用完整性、文本键存在、Expr 可解析，以及 skill/combat/
   target/ai 四个 L2 模块登记的全部模块专属校验规则（效果数上限、叠加类别
   冲突、命中表概率区间、抗性曲线单调性、目标链来源已注册与无环、AI 优先级
   唯一性等）。

**判断记录（T2-12 落地）**：字段级/引用完整性/Expr/模块专属校验规则的唯一实现
是 ``core`` 内对应的 C# 类型（``DataRegistry``、各模块 ``IValidationRule``）；
本文件不得重新实现其中任何一条判断逻辑——第 1 道检查覆盖的内容与第 2 道完全
不重叠（信封/表名/id 格式 vs. 字段语义），这是有意为之的分工，不是"暂未实现"。

返回码约定：
    0 —— 两道检查全部通过，无错误。
    1 —— 至少一道检查报出错误（``--strict`` 下 Warning 也算，见下）。
    2 —— 命令行参数错误（含 ``--data-root``/``--dataset`` 指向不存在的目录、
         找不到 ``dotnet`` 可执行文件）。

常用参数：
    ``--strict``      第二道校验里 Warning 也阻断（透传给 ``toolchain/validator``
                       的 ``--strict``，见该工具 ``Program.cs``）。
    ``--skip-dotnet``  只跑第一道骨架检查，跳过第二道（用于没有安装 .NET SDK
                       的环境，或只想快速跑一遍最基础的形状检查）。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
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
    # Windows 控制台默认代码页通常不是 UTF-8，输出里的中文文本（本文件与
    # toolchain/validator 打印的说明/错误消息）会因此乱码；显式把标准输出/标准错误
    # reconfigure 成 UTF-8，惯例同 toolchain/validator/Program.cs 对 Console.OutputEncoding
    # 的处理。某些非交互式重定向目标可能不支持 reconfigure，失败时静默保留原编码，不影响
    # 校验逻辑本身。
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, OSError):
            pass

    parser = argparse.ArgumentParser(
        description="数据表校验器（第一道骨架级通用检查 + 第二道调用 toolchain/validator 的真实校验）"
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
    parser.add_argument(
        "--strict",
        action="store_true",
        help="第二道校验（toolchain/validator）里 Warning 也阻断，透传为该工具的 --strict",
    )
    parser.add_argument(
        "--skip-dotnet",
        action="store_true",
        help="跳过第二道校验（不调用 toolchain/validator），只跑第一道骨架检查",
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

    # ---------------------------------------------------------------
    # 第一道：骨架级通用检查（本文件自己实现）。
    # ---------------------------------------------------------------

    total_files = 0
    total_errors = 0

    for path in iter_json_files(target_root):
        total_files += 1
        rel_path = path.relative_to(repo_root) if _is_relative_to(path, repo_root) else path
        file_errors = validate_file(path, args.verbose)
        for message in file_errors:
            print(f"{rel_path}: {message}")
            total_errors += 1

    print(f"[第一道·骨架检查] checked {total_files} files, {total_errors} errors")
    stage1_failed = total_errors > 0

    if args.skip_dotnet:
        return 1 if stage1_failed else 0

    # ---------------------------------------------------------------
    # 第二道：真实校验（子进程调用 toolchain/validator，复用 core 内
    # DataRegistry + RulesSchemaCatalog 的全部字段级/引用完整性/Expr/模块专属
    # 校验规则；本文件不重新实现其中任何一条判断逻辑，见文件头判断记录）。
    # ---------------------------------------------------------------

    dotnet_path = shutil.which("dotnet")
    if dotnet_path is None:
        print(
            "参数错误：未找到 dotnet 可执行文件，无法运行第二道校验"
            "（core 内真实校验逻辑，见 toolchain/validator）。"
            "请安装 .NET SDK 后重试，或用 --skip-dotnet 只跑第一道骨架检查。",
            file=sys.stderr,
        )
        return 2

    validator_project = repo_root / "toolchain" / "validator"
    cmd = [dotnet_path, "run", "--project", str(validator_project)]
    # 判断记录：本工具（`dotnet run`）与 `dotnet build`/`dotnet test` 一样支持 `--artifacts-path`
    # 统一构建产物落盘目录（见任务书硬性规则 5）；`validate_data.py` 本身不接受命令行参数指定该
    # 路径（避免与 `--data-root`/`--strict` 等既有参数表面混杂），改用环境变量
    # `WS_GAME_ARTIFACTS_PATH` 透传——未设置该环境变量时行为与改动前完全一致（不传
    # `--artifacts-path`，使用 dotnet 默认输出目录）。
    artifacts_path = os.environ.get("WS_GAME_ARTIFACTS_PATH")
    if artifacts_path:
        cmd += ["--artifacts-path", artifacts_path]
    cmd += ["--", "--data-root", str(target_root)]
    if args.strict:
        cmd.append("--strict")

    print(
        "[第二道·真实校验] 正在运行 toolchain/validator（首次运行会自动编译，可能需要几秒）: "
        + " ".join(cmd)
    )
    result = subprocess.run(cmd, cwd=str(repo_root))

    if result.returncode == 2:
        # toolchain/validator 自身的参数错误（如 --data-root 指向的目录在子进程视角下不存在），
        # 按同一约定原样透传为脚本级参数错误，不归为"数据校验失败"。
        return 2

    stage2_failed = result.returncode != 0
    return 1 if (stage1_failed or stage2_failed) else 0


def _is_relative_to(path: Path, other: Path) -> bool:
    try:
        path.relative_to(other)
        return True
    except ValueError:
        return False


if __name__ == "__main__":
    sys.exit(main())
