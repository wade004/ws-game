#!/usr/bin/env python3
"""耗时统计：读仓库根 ``timing/*.jsonl``（AGENTS.md 1c 的耗时记录），按 phase、按 step 汇总。

每个 (phase) 与每个 (phase, step) 输出：次数、合计秒数、中位数、P90、最大值。输出写 stdout，不写文件，
不改 ``timing/``。

口径（判断记录）：

- **phase 汇总不含 ``_total`` 行**：``_total`` 是门禁脚本总墙钟，与同一次运行里各步骤行重叠（并行线下
  步骤秒数之和还大于墙钟），一起加会重复计数；``_total`` 只作为 ``(phase, "_total")`` 一个 step 单独列出。
- **P90 用线性插值**（``k = (n-1)*0.9`` 取相邻两个有序值插值），保证 中位数 <= P90 <= 最大值；单个样本时
  三者相等。
- 过滤：``--since`` 比较行的 ``start`` 日期（含当天）；``--branch`` 精确匹配行里的 ``branch`` 字段，或匹配
  文件名里的分支简名（去 ``feature/``、``bugfix/`` 前缀）；``--phase`` 精确匹配。三者可叠加（取交集）。
- 坏行（不是 JSON、缺 ``phase``/``step``/``seconds``、seconds 不是数字）跳过并在 stderr 打一行计数，不中断。

用法::

    python toolchain/timing_report.py                      # 文本表
    python toolchain/timing_report.py --json               # JSON（供脚本/测试读）
    python toolchain/timing_report.py --since 2026-10-01 --phase 全量门禁
    python toolchain/timing_report.py --branch feature/targeted-gate_20261001
    python toolchain/timing_report.py --timing-dir <目录>  # 读别的目录（测试用）

返回码：0 成功（含没有任何记录）；2 参数错。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from pathlib import Path
from typing import Any, Iterable

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TIMING_DIR = REPO_ROOT / "timing"

TOTAL_STEP = "_total"
PHASE_ORDER = ["勘察", "设计", "编码", "定向门禁", "全量门禁", "提交", "汇报", "等待"]


def branch_slug(branch: str) -> str:
    """与 ``toolchain/_gate_timing.ps1`` 的 Get-GateTimingBranchSlug 同口径。"""
    slug = re.sub(r"^(feature|bugfix)/", "", branch)
    return re.sub(r"[\\/]", "-", slug)


def normalize_date(text: str) -> str:
    """``20261001`` / ``2026-10-01`` -> ``2026-10-01``；其它格式抛 ValueError。"""
    t = text.strip()
    m = re.fullmatch(r"(\d{4})-?(\d{2})-?(\d{2})", t)
    if not m:
        raise ValueError(f"日期格式应为 YYYYMMDD 或 YYYY-MM-DD：{text!r}")
    return f"{m.group(1)}-{m.group(2)}-{m.group(3)}"


def percentile(sorted_values: list[float], q: float) -> float:
    """线性插值分位数；``sorted_values`` 须升序且非空。"""
    n = len(sorted_values)
    if n == 1:
        return sorted_values[0]
    k = (n - 1) * q
    lo = int(k)
    hi = min(lo + 1, n - 1)
    frac = k - lo
    return sorted_values[lo] + (sorted_values[hi] - sorted_values[lo]) * frac


def stats(values: Iterable[float]) -> dict[str, Any]:
    vals = sorted(values)
    n = len(vals)
    if n == 0:
        return {"count": 0, "total": 0.0, "median": 0.0, "p90": 0.0, "max": 0.0}
    mid = n // 2
    median = vals[mid] if n % 2 == 1 else (vals[mid - 1] + vals[mid]) / 2
    return {
        "count": n,
        "total": sum(vals),
        "median": median,
        "p90": percentile(vals, 0.9),
        "max": vals[-1],
    }


def load_rows(
    timing_dir: Path,
    *,
    since: str | None = None,
    branch: str | None = None,
    phase: str | None = None,
) -> tuple[list[dict[str, Any]], int, int]:
    """返回 (通过过滤的行, 读到的文件数, 跳过的坏行数)。"""
    rows: list[dict[str, Any]] = []
    bad = 0
    files = sorted(timing_dir.glob("*.jsonl")) if timing_dir.is_dir() else []
    wanted_branch_slug = branch_slug(branch) if branch else None
    for path in files:
        with path.open(encoding="utf-8-sig") as fh:
            for raw in fh:
                line = raw.strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                    if not isinstance(row, dict):
                        raise ValueError("not an object")
                    row_phase = str(row["phase"])
                    row_step = str(row["step"])
                    seconds = float(row["seconds"])
                except (ValueError, KeyError, TypeError):
                    bad += 1
                    continue
                if seconds != seconds or seconds < 0:  # NaN / 负数
                    bad += 1
                    continue
                row["phase"], row["step"], row["seconds"] = row_phase, row_step, seconds
                if since is not None and str(row.get("start", ""))[:10] < since:
                    continue
                if phase is not None and row_phase != phase:
                    continue
                if branch is not None:
                    row_branch = str(row.get("branch", ""))
                    if row_branch != branch and branch_slug(row_branch) != wanted_branch_slug:
                        continue
                rows.append(row)
    return rows, len(files), bad


def _phase_sort_key(phase: str) -> tuple[int, str]:
    return (PHASE_ORDER.index(phase), phase) if phase in PHASE_ORDER else (len(PHASE_ORDER), phase)


def aggregate(rows: list[dict[str, Any]]) -> dict[str, Any]:
    by_phase: dict[str, list[float]] = {}
    by_step: dict[tuple[str, str], list[float]] = {}
    for row in rows:
        p, s, sec = row["phase"], row["step"], row["seconds"]
        by_step.setdefault((p, s), []).append(sec)
        if s != TOTAL_STEP:
            by_phase.setdefault(p, []).append(sec)
    phases = [{"phase": p, **stats(v)} for p, v in by_phase.items()]
    phases.sort(key=lambda d: _phase_sort_key(d["phase"]))
    steps = [{"phase": p, "step": s, **stats(v)} for (p, s), v in by_step.items()]
    steps.sort(key=lambda d: (_phase_sort_key(d["phase"]), -d["total"], d["step"]))
    return {"rows": len(rows), "phases": phases, "steps": steps}


def _width(text: str) -> int:
    return sum(2 if unicodedata.east_asian_width(ch) in ("W", "F") else 1 for ch in text)


def _pad(text: str, width: int, right: bool = False) -> str:
    gap = " " * max(0, width - _width(text))
    return gap + text if right else text + gap


def _fmt(x: float) -> str:
    return f"{x:.1f}"


def render_text(report: dict[str, Any]) -> str:
    out: list[str] = []
    f = report["filters"]
    shown = ", ".join(f"{k}={v}" for k, v in f.items() if v) or "无"
    out.append(f"耗时统计：{report['files']} 个文件，{report['rows']} 行（过滤：{shown}；phase 汇总不含 _total）")

    def table(title: str, headers: list[str], body: list[list[str]], right_from: int) -> None:
        out.append("")
        out.append(title)
        if not body:
            out.append("  （无记录）")
            return
        widths = [max(_width(h), *(_width(r[i]) for r in body)) for i, h in enumerate(headers)]
        out.append("  " + "  ".join(_pad(h, widths[i], i >= right_from) for i, h in enumerate(headers)))
        for r in body:
            out.append("  " + "  ".join(_pad(c, widths[i], i >= right_from) for i, c in enumerate(r)))

    table(
        "按 phase",
        ["phase", "次数", "合计(s)", "中位数(s)", "P90(s)", "最大(s)"],
        [[d["phase"], str(d["count"]), _fmt(d["total"]), _fmt(d["median"]), _fmt(d["p90"]), _fmt(d["max"])] for d in report["phases"]],
        1,
    )
    table(
        "按 step",
        ["phase", "step", "次数", "合计(s)", "中位数(s)", "P90(s)", "最大(s)"],
        [
            [d["phase"], d["step"], str(d["count"]), _fmt(d["total"]), _fmt(d["median"]), _fmt(d["p90"]), _fmt(d["max"])]
            for d in report["steps"]
        ],
        2,
    )
    return "\n".join(out)


def build_report(
    timing_dir: Path,
    *,
    since: str | None = None,
    branch: str | None = None,
    phase: str | None = None,
) -> tuple[dict[str, Any], int]:
    rows, files, bad = load_rows(timing_dir, since=since, branch=branch, phase=phase)
    report = aggregate(rows)
    report["files"] = files
    report["filters"] = {"since": since, "branch": branch, "phase": phase}
    return report, bad


def main(argv: list[str] | None = None) -> int:
    ensure_utf8_stdio()
    parser = argparse.ArgumentParser(description="timing/*.jsonl 耗时统计（stdout，不写文件）")
    parser.add_argument("--since", help="只统计 start 日期不早于此日（YYYYMMDD 或 YYYY-MM-DD）")
    parser.add_argument("--branch", help="只统计该分支（行里的 branch 字段，或去前缀后的简名）")
    parser.add_argument("--phase", help="只统计该 phase")
    parser.add_argument("--json", action="store_true", help="输出 JSON")
    parser.add_argument("--timing-dir", default=str(DEFAULT_TIMING_DIR), help="记录目录（默认仓库根 timing/）")
    args = parser.parse_args(argv)

    since = None
    if args.since:
        try:
            since = normalize_date(args.since)
        except ValueError as exc:
            print(f"参数错误：{exc}", file=sys.stderr)
            return 2

    report, bad = build_report(Path(args.timing_dir), since=since, branch=args.branch, phase=args.phase)
    if bad:
        print(f"警告：跳过 {bad} 个坏行（非 JSON 或缺 phase/step/seconds）", file=sys.stderr)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(render_text(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
