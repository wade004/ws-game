#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_std_dummy_poses.py
======================

生成框架级假人姿势集（sprite 型，architecture/手感设计/04 第 6.2 节、ADR-0119）：几何人偶序列帧
（整身合成 + 身体层/武器层逐层剪辑 + 方向变体）、`display.anim_set.std_dummy_biped` 数据行（含
04 第 5 节命名事件）、规格文件 `assets/_placeholder/std_dummy_poses.json`。不使用任何 AI 生图。

用法：
    python toolchain/gen_std_dummy_poses.py [--assets-out assets/_placeholder] [--data-out data/_framework]
                                            [--direction-count 8] [--fps 20] [--no-composite-dirs]
                                            [--clean] [--sheet <缩略拼图.png>]
    python toolchain/gen_std_dummy_poses.py --check [--assets-out ...] [--data-out ...] [--sheet <缩略拼图.png>]

--check 只读自检（必备键 × 全方向档、事件标记、帧数规则、循环连续、步幅、画布裁切、数据行与规格一致），
有错误退出码 1。--sheet 输出每键一格的缩略拼图（04 第 9 节第 6 条，只留本地，不入库）。
详见 toolchain/std_dummy_poses/README.md。
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402
from std_dummy_poses import config as C  # noqa: E402
from std_dummy_poses.build import generate  # noqa: E402
from std_dummy_poses.verify import contact_sheet, print_report, verify  # noqa: E402


def main(argv=None) -> int:
    ensure_utf8_stdio()
    ap = argparse.ArgumentParser(description="生成/自检框架级假人姿势集（sprite 型）")
    ap.add_argument("--assets-out", default="assets/_placeholder", help="资产根（默认 assets/_placeholder）")
    ap.add_argument("--data-out", default="data/_framework", help="数据根（默认 data/_framework）")
    ap.add_argument("--direction-count", type=int, default=C.DEFAULT_DIRECTION_COUNT, choices=(4, 8, 16))
    ap.add_argument("--fps", type=int, default=C.FPS, help=f"标定帧率（默认 {C.FPS}）")
    ap.add_argument("--no-composite-dirs", action="store_true", help="不生成整身合成的方向变体（只留默认朝向整身 + 逐层剪辑）")
    ap.add_argument("--clean", action="store_true", help="生成前删除 sprite_anim/std_dummy_* 与规格文件（只限这批）")
    ap.add_argument("--check", action="store_true", help="只读自检")
    ap.add_argument("--sheet", default=None, help="输出缩略拼图路径（每键一格）")
    args = ap.parse_args(argv)

    repo = Path(__file__).resolve().parent.parent
    assets_out = Path(args.assets_out)
    data_out = Path(args.data_out)
    if not assets_out.is_absolute():
        assets_out = repo / assets_out
    if not data_out.is_absolute():
        data_out = repo / data_out

    if not args.check:
        generate(assets_out, data_out, args.direction_count, args.fps,
                 composite_dirs=not args.no_composite_dirs, clean=args.clean)
    rc = print_report(verify(assets_out, data_out))
    if args.sheet:
        sheet = Path(args.sheet)
        print(f"缩略拼图：{contact_sheet(assets_out, sheet).as_posix()}")
    return rc


if __name__ == "__main__":
    sys.exit(main())
