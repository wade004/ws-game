#!/usr/bin/env python3
"""数据表字段顺序格式化工具（消费方反馈 E11 根治，2026-09-10，见
architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E11）。

背景：示例数据里记录的字段书写顺序与对应 ``TableSchema.Fields`` 的登记顺序经常不一致（内容作者
手写 JSON 时按自己顺手的顺序摆字段，schema 登记顺序则是设计时按语义分组排列的），编辑器等工具若
按 schema 声明顺序渲染表单/生成 diff，与仓库里实际存的 JSON 字段顺序对不上，人工审阅 diff 时容易
把"纯粹换了个字段摆放顺序"误读成"改了内容"。

本工具唯一模式 ``--schema-order``：把每个数据根下每张表的每条记录的字段，按该表 ``TableSchema.Fields``
的登记顺序重排（未在 schema 中登记的字段——如临时的 ``override``/``final`` 行级元字段、或尚未
补齐子结构登记的复合字段——保持原有相对顺序，整体追加在已登记字段之后，不丢弃、不报错）。

字段登记顺序的来源：调用 ``toolchain/validator --list-tables --json``（复用既有命令组合，不新增
一个独立子命令——``tables_list`` 条目本来就带 ``fields`` 数组，见该工具 ``Program.cs`` 判断记录），
不在本文件重新维护一份关于 schema 结构的判断逻辑（与 ``toolchain/validate_data.py`` 文件头判断
记录"唯一实现"同一原则）。

用法::

    python toolchain/format_data.py --schema-order                    # 处理 data/_framework + data/_sample，就地重写
    python toolchain/format_data.py --schema-order --check            # 只检查，不写文件；有需要重排的文件时退出码 1
    python toolchain/format_data.py --schema-order --data-root data/_framework  # 只处理指定根（可重复传入）

返回码约定：
    0 —— 处理完成（``--check`` 模式下也代表"未发现需要重排的文件"）。
    1 —— ``--check`` 模式下发现有文件字段顺序与 schema 声明不一致。
    2 —— 命令行参数错误、或调用 ``toolchain/validator`` 拿字段顺序失败。

判断记录：字段重排会用 ``json.dumps(..., indent=2)`` 整体重新序列化每个改动过的文件——仓库里现有
数据文件的格式化风格并不统一（部分文件每条记录压缩成单行，部分文件每个字段各占一行），本工具不
尝试逐文件保留原有的不一致排版，统一整理为"2 空格缩进、每个字段各占一行"这一种风格（与
``data/README.md``"编码与格式"约定的 UTF-8 无 BOM、2 空格缩进、LF 行尾一致），只有确实存在字段
顺序差异或原格式不是这种统一风格的文件才会被改写（``--check`` 据此判定"是否需要重排"）。
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402


def find_repo_root() -> Path:
    return Path(__file__).resolve().parent.parent


def _build_validator_cmd(repo_root: Path, data_roots: list[Path]) -> list[str]:
    """惯例同 toolchain/validate_data.py：优先用预编译的 toolchain/validator/bin/Validator.dll
    （消费方反馈 E1 根治，不触发 MSBuild/Directory.Build.props 解析），找不到才退回
    `dotnet run --project` 现场编译，见该脚本判断记录。本文件只是拿字段顺序信息，不需要重复
    实现同一套调度逻辑之外的任何新东西。
    """
    dotnet_path = shutil.which("dotnet")
    if dotnet_path is None:
        raise RuntimeError(
            "找不到 dotnet 可执行文件，无法运行 toolchain/validator 取字段顺序。"
            "请安装 .NET SDK 后重试。"
        )
    validator_project = Path(__file__).resolve().parent / "validator"
    validator_dll_path = validator_project / "bin" / "Validator.dll"
    if validator_dll_path.is_file():
        cmd = [dotnet_path, str(validator_dll_path)]
    else:
        cmd = [dotnet_path, "run", "--project", str(validator_project), "--"]
    cmd.append("--list-tables")
    cmd.append("--json")
    for root in data_roots:
        cmd += ["--data-root", str(root)]
    return cmd


def get_field_order_map(repo_root: Path, data_roots: list[Path]) -> dict[str, list[str]]:
    """跑一遍 toolchain/validator --list-tables --json，返回 {表名: [按 schema 登记顺序的字段名]}。

    判断记录：`--list-tables --json` 输出的 `fields` 数组来自 `TableSchema.GetSchema`，与
    数据是否通过校验（`report.blocking`）无关（schema 在装配阶段就已经登记完毕，不依赖加载结果，
    见 toolchain/validator/Program.cs `PrintJson` 判断记录）——即便某个数据根单独校验会因为跨表
    引用缺失而报错，`fields` 信息仍然可靠，因此这里不关心 validator 的退出码/report 是否 blocking，
    只要能拿到合法 JSON 输出就提取 `fields`；拿不到（子进程失败、非法 JSON）则直接抛错，不静默
    返回空表（那样会让调用方误以为"这张表没有登记字段"从而不做任何重排，掩盖真实问题）。
    """
    cmd = _build_validator_cmd(repo_root, data_roots)
    result = subprocess.run(
        cmd, cwd=str(repo_root), capture_output=True, text=True, encoding="utf-8", errors="replace"
    )
    try:
        data = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(
            "toolchain/validator --list-tables --json 输出不是合法 JSON，无法取字段顺序"
            f"（退出码 {result.returncode}）：{exc}\n标准输出：{result.stdout}\n标准错误：{result.stderr}"
        ) from exc

    tables_list = data.get("tables_list")
    if tables_list is None:
        raise RuntimeError(
            "toolchain/validator --list-tables --json 输出缺少 tables_list 字段，"
            "无法取字段顺序（validator 是否是较旧版本，未包含消费方反馈 E11 新增的 fields 字段？）"
        )
    return {entry["name"]: list(entry.get("fields") or []) for entry in tables_list}


def _reorder_row(row: dict, field_order: list[str]) -> dict:
    ordered: dict[str, Any] = {}
    for name in field_order:
        if name in row:
            ordered[name] = row[name]
    for key, value in row.items():
        if key not in ordered:
            ordered[key] = value
    return ordered


def _format_envelope(data: dict) -> str:
    return json.dumps(data, ensure_ascii=False, indent=2) + "\n"


def process_file(path: Path, field_order_map: dict[str, list[str]], verbose: bool) -> tuple[bool, str]:
    """返回 (是否需要重排, 重排后的完整文本)。字段顺序信息里没有这张表（未登记 schema、或表名
    与文件名不一致导致的骨架级问题——那是 validate_data.py 骨架检查的职责，本工具不重复判断）时
    原样返回，不改动、不报错。
    """
    original_text = path.read_text(encoding="utf-8")
    try:
        data = json.loads(original_text)
    except json.JSONDecodeError:
        # 非法 JSON 不是本工具的职责（validate_data.py 第一道骨架检查会报），原样跳过。
        return False, original_text

    if not isinstance(data, dict):
        return False, original_text

    table = data.get("table")
    field_order = field_order_map.get(table) if isinstance(table, str) else None
    if not field_order:
        if verbose:
            print(f"[verbose] 跳过（未登记字段顺序信息）: {path}", file=sys.stderr)
        return False, original_text

    rows = data.get("rows")
    if not isinstance(rows, list):
        return False, original_text

    # 判断记录：是否需要重排，只看"字段的相对顺序是否真的发生了变化"（逐行比较重排前后的
    # dict key 序列），不是看"整份文件重新序列化后字节是否不同"——本仓库现有数据文件的空白/
    # 换行风格本来就不统一（部分文件每条记录压缩成单行，部分文件展开成多行，见文件头判断记录），
    # 若按字节差异判定，任何风格与 json.dumps(indent=2) 输出不完全一致的文件都会被判定为"需要
    # 重排"，把纯粹的空白规范化混进"字段顺序不对"这一条判断里，夸大改动范围、也让 --check 的
    # 报错信息名不副实（明明字段顺序本来就对，却被报告为"需要重排"）。只有真的发生了字段顺序
    # 变化的文件才会被改写；这类文件顺带被统一成 2 空格缩进的展开格式，不再对这些确实要改动的
    # 文件额外维护"尽量保留原有空白风格"的复杂逻辑。
    any_row_reordered = False
    new_rows = []
    for row in rows:
        if not isinstance(row, dict):
            new_rows.append(row)
            continue
        reordered = _reorder_row(row, field_order)
        if list(reordered.keys()) != list(row.keys()):
            any_row_reordered = True
        new_rows.append(reordered)

    if not any_row_reordered:
        return False, original_text

    new_data = dict(data)
    new_data["rows"] = new_rows
    new_text = _format_envelope(new_data)

    return True, new_text


def main(argv: list[str] | None = None) -> int:
    ensure_utf8_stdio()

    parser = argparse.ArgumentParser(
        description="数据表字段顺序格式化工具（--schema-order：按 TableSchema.Fields 登记顺序重排记录字段）"
    )
    parser.add_argument(
        "--schema-order",
        action="store_true",
        help="唯一支持的模式：按表 schema 登记顺序重排每条记录的字段（必填，为未来可能新增的"
             "其它格式化模式预留参数面）",
    )
    parser.add_argument(
        "--data-root",
        dest="data_roots",
        action="append",
        default=None,
        help="数据根目录，相对当前工作目录解析（也可传绝对路径）；可重复传入，"
             "一次都不传时默认处理 data/_framework 与 data/_sample 两根",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="只检查、不写文件；发现需要重排的文件时以退出码 1 结束（供 check.ps1 等门禁调用）",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="输出更详细的处理过程信息（写入标准错误）",
    )

    args = parser.parse_args(argv)

    if not args.schema_order:
        print("参数错误：目前必须传 --schema-order（唯一支持的格式化模式）", file=sys.stderr)
        return 2

    repo_root = find_repo_root()
    cwd = Path.cwd()
    root_args = args.data_roots or ["data/_framework", "data/_sample"]

    def resolve_root(root_arg: str) -> Path:
        p = Path(root_arg)
        return p if p.is_absolute() else (cwd / p)

    data_roots = [resolve_root(r) for r in root_args]
    for root in data_roots:
        if not root.is_dir():
            print(f"参数错误：目录不存在: {root}", file=sys.stderr)
            return 2

    try:
        field_order_map = get_field_order_map(repo_root, data_roots)
    except RuntimeError as exc:
        print(f"参数错误：{exc}", file=sys.stderr)
        return 2

    changed_files: list[Path] = []
    total_files = 0
    for root in data_roots:
        for path in sorted(root.rglob("*.json")):
            total_files += 1
            needs_rewrite, new_text = process_file(path, field_order_map, args.verbose)
            if needs_rewrite:
                changed_files.append(path)
                if not args.check:
                    path.write_text(new_text, encoding="utf-8", newline="\n")

    verb = "需要重排" if args.check else "已重排"
    for f in changed_files:
        try:
            rel = f.relative_to(repo_root)
        except ValueError:
            rel = f
        print(f"{verb}: {rel}")

    print(
        f"[format_data.py --schema-order{' --check' if args.check else ''}] "
        f"共检查 {total_files} 个文件，{verb} {len(changed_files)} 个"
    )

    if args.check and changed_files:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
