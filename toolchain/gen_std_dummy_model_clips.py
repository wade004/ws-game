#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_std_dummy_model_clips.py
============================

生成框架级假人姿势集（model 型 / 骨骼剪辑，architecture/手感设计/04 第 6.1 节、ADR-0119）：规格文件
`assets/_placeholder/std_dummy_model_clips.json`（标准人形骨架 + 逐剪辑关键帧 + 命名事件）与
`display.anim_set.std_dummy_biped_model` 数据行（并入 `data/_framework/display/display.anim_set.json`，不动别的行）。
键清单、三相、事件、帧数全部来自 sprite 型假人集（std_dummy_poses），同一份姿势函数逐帧同源。不使用任何 AI 生成。

引擎侧资产（预制体 model.std_dummy_biped、动画控制器、逐剪辑 .anim）由编辑器生成脚本按规格生成：
    Unity -batchmode -nographics -quit -projectPath adapters/unity \
          -executeMethod Adapter.Unity.EditorTools.GenerateStdDummyModelAssets.GenerateAndExit
（或菜单 Tools/GameFoundation/Generate Std Dummy Model Assets）。.meta 由 Unity 生成，随资产一并入库。

用法：
    python toolchain/gen_std_dummy_model_clips.py [--assets-out assets/_placeholder] [--data-out data/_framework]
                                                  [--unity-project adapters/unity] [--fps 20]
    python toolchain/gen_std_dummy_model_clips.py --check [--assets-out ...] [--data-out ...] [--unity-project ...] [--no-unity]

--check 只读自检（必备键、事件、帧数、骨骼路径、关节角限、正向运动学对账、与 sprite 版同源、数据行与规格一致、
引擎侧资产与规格一致），有错误退出码 1。--no-unity 跳过引擎资产核对（只验规格与数据行）。
详见 toolchain/std_dummy_model_clips/README.md。
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402
from std_dummy_model_clips import config as C  # noqa: E402
from std_dummy_model_clips.build import generate  # noqa: E402
from std_dummy_model_clips.verify import print_report, verify  # noqa: E402


def main(argv=None) -> int:
    ensure_utf8_stdio()
    ap = argparse.ArgumentParser(description="生成/自检框架级假人姿势集（model 型 / 骨骼剪辑）")
    ap.add_argument("--assets-out", default="assets/_placeholder", help="资产根（默认 assets/_placeholder）")
    ap.add_argument("--data-out", default="data/_framework", help="数据根（默认 data/_framework）")
    ap.add_argument("--unity-project", default="adapters/unity", help="Unity 工程目录（核对引擎侧资产；默认 adapters/unity）")
    ap.add_argument("--fps", type=int, default=C.FPS, help=f"标定帧率（默认 {C.FPS}，与 sprite 版共用）")
    ap.add_argument("--check", action="store_true", help="只读自检")
    ap.add_argument("--no-unity", action="store_true", help="自检时跳过引擎侧资产核对")
    args = ap.parse_args(argv)

    repo = Path(__file__).resolve().parent.parent
    assets_out = Path(args.assets_out)
    data_out = Path(args.data_out)
    unity = Path(args.unity_project)
    if not assets_out.is_absolute():
        assets_out = repo / assets_out
    if not data_out.is_absolute():
        data_out = repo / data_out
    if not unity.is_absolute():
        unity = repo / unity

    if not args.check:
        generate(assets_out, data_out, args.fps)
    return print_report(verify(assets_out, data_out, None if args.no_unity else unity))


if __name__ == "__main__":
    sys.exit(main())
