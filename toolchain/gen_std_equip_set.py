#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_std_equip_set.py
====================

生成框架级占位装备集（手感设计/06 第 2 节、08 装备资产包契约的参照实现）：单手剑、双手巨剑、匕首、弓、法杖、
胸甲各一个完整资产包（图标 + 纸娃娃静态层图 + 逐层剪辑 + 外观/武器表现/手感/音效材质数据行），加框架占位界面
皮肤包 ``skin.default``。不使用任何 AI 生图。

用法：
    python toolchain/gen_std_equip_set.py [--assets-out assets/_placeholder] [--data-out data/_equip]
                                          [--only <物品名,...>] [--clean] [--sheet <缩略拼图.png>]
    python toolchain/gen_std_equip_set.py --check

--check 只读：对生成物跑装备完整性校验（import_assets.py equip，须零错误零警告）。--sheet 输出每件装备
全方向档的待机帧拼图（只留本地，不入库）。详见 toolchain/std_equip_set/README.md。
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402
from std_equip_set import config as C  # noqa: E402
from std_equip_set.build import generate  # noqa: E402


def _sheet(assets_out: Path, path: Path) -> None:
    from PIL import Image

    dirs = ("front", "front_side_r", "side_r", "back_side_r", "back")
    w = 144
    sheet = Image.new("RGBA", (w * len(dirs), w * len(C.ITEMS)), (60, 64, 72, 255))
    for r, it in enumerate(C.ITEMS):
        for c, d in enumerate(dirs):
            p = assets_out / "sprites" / it.mesh_stem / d / f"{it.layer}.png"
            if p.is_file():
                im = Image.open(p).convert("RGBA")
                sheet.alpha_composite(im, (c * w, r * w))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def main(argv=None) -> int:
    ensure_utf8_stdio()
    ap = argparse.ArgumentParser(description="生成/自检框架级占位装备集")
    ap.add_argument("--assets-out", default=C.ASSETS_DIRNAME)
    ap.add_argument("--data-out", default=C.DATA_DIRNAME)
    ap.add_argument("--only", default=None, help="只生成这些物品（逗号分隔物品名，如 std_dagger）；数据表与皮肤仍整套写")
    ap.add_argument("--clean", action="store_true", help="生成前删除本生成器的旧产物（只限 item_std_* 等）")
    ap.add_argument("--check", action="store_true", help="只读自检：装备完整性校验")
    ap.add_argument("--sheet", default=None, help="输出每件装备全方向档拼图路径")
    args = ap.parse_args(argv)

    repo = Path(__file__).resolve().parent.parent
    assets_out = Path(args.assets_out) if Path(args.assets_out).is_absolute() else repo / args.assets_out
    data_out = Path(args.data_out) if Path(args.data_out).is_absolute() else repo / args.data_out

    if not args.check:
        items = C.ITEMS
        if args.only:
            names = {n.strip() for n in args.only.split(",") if n.strip()}
            items = tuple(i for i in C.ITEMS if i.name in names)
            if len(items) != len(names):
                print(f"错误: --only 含未知物品名，可选 {[i.name for i in C.ITEMS]}", file=sys.stderr)
                return 2
        generate(assets_out, data_out, items=items, clean=args.clean)
    if args.sheet:
        _sheet(assets_out, Path(args.sheet))
    cmd = [sys.executable, str(repo / "toolchain" / "import_assets.py"), "equip", "--assets-dir", str(assets_out),
           "--data-root", str(repo / "data" / "_framework"), "--data-root", str(repo / "data" / "_feel"),
           "--data-root", str(data_out), "--no-report"]
    rc = subprocess.call(cmd)
    return rc


if __name__ == "__main__":
    sys.exit(main())
