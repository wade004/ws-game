"""``equip`` 子命令：装备资产包 + 界面皮肤包校验，输出装备完整性报告（手感设计/08 第 5 节、ADR-0123）。

    python toolchain/import_assets.py equip [--data-root <根> ...] [--assets-dir assets/_placeholder]
                                            [--anim-set <id>] [--direction-count 8] [--skin-ref skin.<名> ...]
                                            [--report-dir bin/_check_artifacts/equip_report] [--no-report] [--json]

默认数据根 = ``data/_framework`` + ``data/_feel`` + ``data/_equip``（框架占位装备集，``toolchain/std_equip_set``
生成），资产目录 ``assets/_placeholder``。同一套校验也作为 ``import_assets.py check --only equip`` 的检查域
（见 :mod:`check_cmd`），两处共用 :func:`run_equip`。

返回码：0 = 零错误（可以有警告；``--strict-warnings`` 时警告也算失败）；1 = 有错误级问题或资产/数据目录不存在。报告写 ``--report-dir`` 下的
``equip_completeness.json`` / ``.txt``（该目录在 ``.gitignore`` 里，只留本地）；``--json`` 时 stdout 只输出报告
JSON 文档，其余日志走 stderr。

**判断记录**

1. 退出码只看错误：08 第 5 节把"推荐键缺失 / 族无姿势键 / 材质缺行 / 皮肤缺项"定为警告，警告只进报告，
   不让命令失败（与其它 ``check`` 域"有问题即 1"不同，因为它们没有警告级问题）。
2. validated 数据集的判定接口是 :meth:`equip_pack.EquipReport.is_validated` /
   :func:`equip_pack.filter_validated`；命令行侧用 ``--blocked-out <文件>`` 写出被阻断物品 id 清单（每行一个），
   供下游导出/打包脚本剔除。
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from . import skin_pack
from .common import AssetImportError, find_repo_root
from .equip_pack import (
    DEFAULT_DIRECTION_COUNT,
    EquipReport,
    EquipValidator,
    load_tables,
    render_text,
    write_report,
)

DEFAULT_DATA_ROOTS = ("data/_framework", "data/_feel", "data/_equip")
DEFAULT_ASSETS_DIR = "assets/_placeholder"
DEFAULT_REPORT_DIR = "bin/_check_artifacts/equip_report"


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--data-root", action="append", default=None, metavar="DIR",
                        help=f"数据根，可重复（同名表跨根追加行）；默认 {', '.join(DEFAULT_DATA_ROOTS)}")
    parser.add_argument("--assets-dir", default=None, help=f"资产目录（含 icons/ sprites/ sprite_anim/ ui/skin/），默认 {DEFAULT_ASSETS_DIR}")
    parser.add_argument("--anim-set", default=None, help="姿势集 display.anim_set 行 id；缺省取键最多的一行")
    parser.add_argument("--direction-count", type=int, default=DEFAULT_DIRECTION_COUNT, choices=(4, 8, 16),
                        help=f"该游戏声明的方向档数，默认 {DEFAULT_DIRECTION_COUNT}（14 第 2.1 节）")
    parser.add_argument("--skin-ref", action="append", default=None, metavar="skin.<名>",
                        help="额外校验的皮肤包（可重复）；缺省只校验数据里 ui_layout_definition.skin_ref 引用到的与占位皮肤")
    parser.add_argument("--report-dir", default=None, help=f"报告输出目录，默认 {DEFAULT_REPORT_DIR}")
    parser.add_argument("--no-report", action="store_true", help="不写报告文件")
    parser.add_argument("--blocked-out", default=None, help="把被阻断（有错误级项）的物品 id 写到该文件，每行一个")
    parser.add_argument("--strict-warnings", action="store_true",
                        help="警告也让命令失败（门禁对框架占位装备集用：它必须零警告）")
    parser.add_argument("--json", action="store_true", help="stdout 只输出报告 JSON 文档")


def run_equip(data_roots: list[Path], assets_dir: Path, *, anim_set: str | None = None,
              direction_count: int = DEFAULT_DIRECTION_COUNT, skin_refs: list[str] | None = None) -> EquipReport:
    """装备资产包 + 皮肤包的合并校验入口（命令行与 check 域共用）。"""
    tables = load_tables(data_roots)
    report = EquipValidator(tables, assets_dir, anim_set=anim_set, direction_count=direction_count).run()
    skin_issues, skin_reports = skin_pack.check_skins(tables, assets_dir, skin_refs=skin_refs)
    report.extra_issues.extend(skin_issues)
    report.skins = [s.as_dict() for s in skin_reports]
    return report


def _resolve(repo_root: Path, value: str) -> Path:
    p = Path(value)
    return p if p.is_absolute() else repo_root / p


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    use_json = bool(getattr(args, "json", False))
    log = sys.stderr if use_json else sys.stdout
    roots = [_resolve(repo_root, v) for v in (args.data_root or DEFAULT_DATA_ROOTS)]
    for r in roots:
        if not r.is_dir():
            raise AssetImportError(f"数据根不存在: {r}")
    assets_dir = _resolve(repo_root, args.assets_dir or DEFAULT_ASSETS_DIR)
    if not assets_dir.is_dir():
        raise AssetImportError(f"资产目录不存在: {assets_dir}")

    report = run_equip(roots, assets_dir, anim_set=args.anim_set, direction_count=args.direction_count,
                       skin_refs=args.skin_ref)
    for issue in report.issues:
        print(f"{issue.severity}: {issue.render_text()}", file=log)
    print(f"[equip] 装备 {len(report.items)} 件，通过 {len(report.validated_item_ids())} 件，"
          f"错误 {report.error_count}，警告 {report.warning_count}（姿势集 {report.anim_set_id}，"
          f"{report.direction_count} 方向档）", file=log)

    if not args.no_report:
        out = _resolve(repo_root, args.report_dir or DEFAULT_REPORT_DIR)
        jp, tp = write_report(report, out)
        print(f"[equip] 报告: {tp} / {jp}", file=log)
    if args.blocked_out:
        Path(args.blocked_out).write_text("".join(i + "\n" for i in report.blocked_item_ids()),
                                          encoding="utf-8", newline="\n")
    if use_json:
        print(json.dumps(report.as_dict(), ensure_ascii=False))
    return 1 if report.error_count or (args.strict_warnings and report.warning_count) else 0


__all__ = ["add_arguments", "run", "run_equip", "render_text"]
