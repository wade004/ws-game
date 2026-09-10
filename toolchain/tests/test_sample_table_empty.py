"""
消费方反馈第三批第 23 条（2026-09-10，见
架构/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 23 条）：``sample_table_empty``
告警级门禁——找出"已注册 schema 但没有任何行"的表，提醒示例数据集覆盖不全。

判断记录（为什么按"_sample 与 _framework 的并集"判定，不是严格"只看 _sample"）：任务书原文
"找出 data/_sample（与 _framework）中……没有任何行的表"——但仓库里不少表是框架级单例表（如
``found.event_catalog``/``found.game_state``/``found.hook``/``found.input_action``，只登记在
``data/_framework``，按设计永远不会出现在 ``data/_sample``），若严格只看 _sample 是否有数据，
这类表会永久触发告警、盖过真正需要关注的信号（"告警级门禁"存在的意义是让人看一眼就知道有没有
新问题，长期噪音等于门禁失效）。因此本检查按"该表在 _sample 与 _framework 两个根的并集里是否
出现过至少一行"判定——这正是本任务发现 ``item.affix``/``item.set``/``world.flag_schema`` 三张
表（已在本次任务补齐最小合法样例数据）时使用的同一套判定口径（见
``architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md`` 第 23 条回复）。

"告警级"落地方式：本测试恒 PASS（不 assert False），命中时用 ``warnings.warn`` 打印一条肉眼
可见的提醒——不阻断 ``pytest toolchain/tests`` 的整体退出码（呼应任务书"告警级门禁，仅对
_sample"的定语：这类问题提醒作者去补数据，不应该阻塞与该表完全无关的改动提交）。
"""

from __future__ import annotations

import json
import subprocess
import warnings
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
VALIDATOR_PROJECT = REPO_ROOT / "toolchain" / "validator"
VALIDATOR_DLL = VALIDATOR_PROJECT / "bin" / "Validator.dll"


def _registered_table_names() -> list[str]:
    """经 ``--schema-audit --json`` 的 ``table_names`` 字段（消费方反馈第三批第 23 条新增，见
    ``toolchain/validator/Program.cs`` ``PrintSchemaAuditJson`` 判断记录）拿到全架构已注册的表名
    权威列表——只有 C# 侧持有这份信息，Python 侧不重复维护一份可能漂移的清单。"""
    if VALIDATOR_DLL.is_file():
        cmd = ["dotnet", str(VALIDATOR_DLL), "--schema-audit", "--json"]
    else:
        cmd = ["dotnet", "run", "--project", str(VALIDATOR_PROJECT), "--", "--schema-audit", "--json"]
    result = subprocess.run(
        cmd, cwd=str(REPO_ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace"
    )
    data = json.loads(result.stdout)
    return list(data["table_names"])


def _tables_with_at_least_one_row(root: Path) -> set[str]:
    covered: set[str] = set()
    for path in root.rglob("*.json"):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        table = data.get("table")
        rows = data.get("rows")
        if isinstance(table, str) and isinstance(rows, list) and len(rows) > 0:
            covered.add(table)
    return covered


def test_sample_table_empty_warns_on_tables_with_no_data_in_either_root():
    registered = _registered_table_names()
    covered = _tables_with_at_least_one_row(REPO_ROOT / "data" / "_sample")
    covered |= _tables_with_at_least_one_row(REPO_ROOT / "data" / "_framework")

    empty = sorted(t for t in registered if t not in covered)
    if empty:
        warnings.warn(
            "sample_table_empty（告警级，不阻断——见本文件模块级判断记录）："
            f"以下已注册 schema 的表在 data/_sample 与 data/_framework 中都没有任何行："
            f"{empty}；建议补充最小合法样例数据（每表 1～2 行）。"
        )
