"""``python toolchain/import_assets.py <子命令> ...`` 的 argparse 入口。

返回码约定（与 toolchain/validate_data.py 一致的分工）：
    0 —— 成功（``check`` 子命令：无问题）。
    1 —— 数据/资产问题（``AssetImportError``、缺文件等）或 ``check`` 发现问题。
    2 —— 命令行参数错误。
"""

from __future__ import annotations

import argparse
import sys

from . import check_cmd, icon_cmd, sfx_cmd, sprite_cmd, vfx_cmd
from .common import AssetImportError, setup_utf8_streams


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="import_assets.py",
        description=(
            "资产导入工具：把出图产物（精灵集/图标/特效序列帧/音效）规范化落到 assets/<dataset>/，"
            "并把对应的外形表/特效表/音效表数据行合并写入 data/<dataset>/。"
        ),
    )
    sub = parser.add_subparsers(dest="command", required=True)

    p_sprite = sub.add_parser("sprite", help="精灵集源目录 -> 规范化精灵资源 + display.map 行")
    sprite_cmd.add_arguments(p_sprite)
    p_sprite.set_defaults(func=sprite_cmd.run)

    p_icon = sub.add_parser("icon", help="一批图标源图 -> 归一化尺寸后落到 assets/<dataset>/icons/")
    icon_cmd.add_arguments(p_icon)
    p_icon.set_defaults(func=icon_cmd.run)

    p_vfx = sub.add_parser("vfx", help="序列帧目录 -> 图集 + vfx.def 行")
    vfx_cmd.add_arguments(p_vfx)
    p_vfx.set_defaults(func=vfx_cmd.run)

    p_sfx = sub.add_parser("sfx", help="音频文件 -> 复制到 assets/<dataset>/sfx/ + sfx.def 行")
    sfx_cmd.add_arguments(p_sfx)
    p_sfx.set_defaults(func=sfx_cmd.run)

    p_check = sub.add_parser("check", help="assets/<dataset>/ 与 data/<dataset>/ 交叉校验")
    check_cmd.add_arguments(p_check)
    p_check.set_defaults(func=check_cmd.run)

    return parser


def main(argv: list[str] | None = None) -> int:
    setup_utf8_streams()
    parser = build_parser()
    try:
        args = parser.parse_args(argv)
    except SystemExit as exc:
        return exc.code if isinstance(exc.code, int) else 2

    try:
        return args.func(args)
    except AssetImportError as exc:
        print(f"错误: {exc}", file=sys.stderr)
        return 1
    except FileNotFoundError as exc:
        print(f"错误: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
