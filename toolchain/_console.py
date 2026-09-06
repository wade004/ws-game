"""控制台 UTF-8 输出统一入口。

背景（判断记录）：本仓库全部命令行工具脚本的提示/错误信息都是中文。Windows 控制台默认
代码页通常不是 UTF-8（尤其是非交互式场景——CI 运行器的管道重定向、被其他进程捕获输出等，
这类场景下 Python 拿不到真实控制台代码页，会退化为系统 ANSI 代码页，例如英文版
Windows/GitHub Actions ``windows-latest`` 运行器上是 ``cp1252``），``print()`` 遇到中文字符
会直接抛 ``UnicodeEncodeError`` 而不是打印乱码——比乱码更糟：整个脚本崩溃，真正的诊断信息
（比如后面本该打印的"check 通过/check 失败"）永远不会出现。

修法：命令行入口最先调用 :func:`ensure_utf8_stdio`，显式把 ``sys.stdout``/``sys.stderr``
reconfigure 成 UTF-8，不依赖运行环境的默认代码页。``reconfigure`` 在极少数不支持的重定向目标
上会抛 ``AttributeError``/``OSError``（例如目标不是 ``io.TextIOWrapper``），静默忽略即可——
这种场景下保留原编码不会更差，但不应该让"打开 UTF-8"这件事本身变成新的崩溃点。

放在 ``toolchain/`` 顶层（而不是 ``toolchain/asset_import/`` 包内部）：``toolchain/`` 下的
入口脚本（``validate_data.py``/``gen_event_constants.py``/``gen_placeholder_assets.py``）都是
直接以 ``python toolchain/xxx.py`` 方式运行的独立脚本，不属于 ``asset_import`` 包，若把本函数
放进 ``asset_import`` 包会反过来要求这些顶层脚本依赖子包、绕远路甚至有循环导入风险
（``asset_import`` 包内部模块本身也需要这份能力）；放在 ``toolchain/`` 顶层，两侧都只需把
``toolchain/`` 目录本身放进 ``sys.path`` 即可直接 ``import _console``（各入口脚本已有的
"允许直接以 python toolchain/xxx.py 方式运行"的 ``sys.path.insert`` 惯例，见
``toolchain/import_assets.py``）。
"""

from __future__ import annotations

import sys


def ensure_utf8_stdio() -> None:
    """把标准输出/标准错误 reconfigure 成 UTF-8；reconfigure 不可用时静默保留原编码。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, OSError):
            pass
