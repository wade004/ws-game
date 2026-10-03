#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
run_prerender_skin.py
=====================

把"标准骨骼 + 蒙皮"预渲染成 sprite 型序列帧（architecture/手感设计/04 第 6.2 节、ADR-0140）：
游戏只需要做一套蒙皮（骨骼名符合标准骨骼的模型，可带装备层），得到全部姿势键 × 全部方向档的序列帧
与 `display.anim_set.<名>` 数据行。离线工具链能力，运行期契约不变；不使用任何 AI 生图。

用法：
    python toolchain/run_prerender_skin.py <渲染配置.json> --assets-root assets --data-root data --dataset <数据集>
        [--unity-project adapters/unity] [--unity-exe <Unity.exe>] [--clean] [--plan-only]
        [--work-dir <中间文件目录>] [--keep-work] [--no-external-validate]

渲染配置字段、蒙皮输入约定、判断记录见 toolchain/prerender_skin/README.md。
退出码：0 通过；1 自检/数据校验/引擎失败；2 配置不合法；3 蒙皮被拒绝（缺骨骼等）。
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402
from prerender_skin.cli import main  # noqa: E402

if __name__ == "__main__":
    ensure_utf8_stdio()
    sys.exit(main())
