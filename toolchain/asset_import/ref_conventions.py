"""``sprite_set_id``/``icon_id`` 资源引用 id -> 资产相对路径约定（消费方反馈第 32 条，
ADR-0025：资源引用标识到资产相对路径的约定纳入公开契约）。

本模块是该约定的 Python 侧实现，与 C# 侧
``core/foundation/engine_adapter/contracts/AssetRefConventions.cs`` 表达同一条规则；两种语言
无法共享同一份源码，靠 ``toolchain/tests/test_ref_conventions.py`` 与该文件对应的 C# 单元测试
（``core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs``）用同一组样例互相对照——
任一侧改动规则而另一侧未同步会被各自语言的测试独立捕获，不依赖两侧互相调用。

``sprite_cmd.py``/``icon_cmd.py`` 此前各自内联计算这条规则（其中 ``sprite_cmd.py`` 的
``sprite_out_dir`` 与本模块 :func:`sprite_set_directory` 逐字节一致，见该脚本判断记录"输出路径：
目录名与运行时资源 id 解析规则对齐"），本次改为调用本模块，不再各自维护一份拷贝。
"""

from __future__ import annotations

__all__ = [
    "strip_category_prefix",
    "sprite_set_directory",
    "icon_file",
    "try_parse_sprite_set_id",
    "try_parse_icon_id",
]


def strip_category_prefix(resource_ref_id: str) -> str:
    """去掉资源引用 id 的"类别前缀"（第一个点分段，如 ``sprite.creature.wolf_grey`` 的
    ``sprite``），剩余部分把点号换成下划线（架构文档 14 第 1.2 节命名模板）。id 不含点号时原样
    返回，不抛异常（与 C# 侧 ``AssetRefConventions.StripCategoryPrefix`` 同一容错行为）。
    """
    if "." not in resource_ref_id:
        return resource_ref_id
    _, _, remainder = resource_ref_id.partition(".")
    return remainder.replace(".", "_")


def sprite_set_directory(sprite_set_id: str) -> str:
    """把 ``display.map.sprite_set_id``（形如 ``sprite.<category>.<name>``）解析为该精灵集在
    资产根目录下的相对目录路径（正斜杠分隔，不以 ``/`` 结尾）：``"sprites/<category>_<name>"``。
    """
    return "sprites/" + strip_category_prefix(sprite_set_id)


def icon_file(icon_id: str) -> str:
    """把 ``display.map.icon_id``（形如 ``icon.<category>.<name>``）解析为该图标在资产根目录下的
    相对文件路径（正斜杠分隔，含 ``.png`` 扩展名）：``"icons/<category>/<name>.png"``——与
    :func:`sprite_set_directory` 不同，``category`` 段保留为独立子目录、不与 ``name`` 拼接扁平化。

    Raises:
        ValueError: ``icon_id`` 去掉类别前缀后不含点号，无法拆出 ``category``/``name`` 两段。
    """
    if "." not in icon_id:
        raise ValueError(f"icon_id '{icon_id}' 不含点号，无法拆出类别前缀")
    _, _, remainder = icon_id.partition(".")
    if "." not in remainder:
        raise ValueError(
            f"icon_id '{icon_id}' 去掉类别前缀后剩余 '{remainder}' 不含点号，"
            "无法拆出 category/name 两段（期望形如 icon.<category>.<name>）"
        )
    category, _, name = remainder.partition(".")
    return f"icons/{category}/{name.replace('.', '_')}.png"


def try_parse_sprite_set_id(relative_directory: str) -> str | None:
    """:func:`sprite_set_directory` 的反向解析：给定形如 ``"sprites/<category>_<name>"`` 的相对
    目录路径（允许前导/末尾多余的 ``/``），尝试还原出 ``sprite_set_id``；解析失败返回 ``None``
    （不抛异常，与 C# 侧 ``TryParse*`` 惯例对齐）。判断记录（有损逆运算的边界）：假定 ``category``
    是显示类别枚举值（不含下划线，见 ``core/foundation/display_info/core/DisplaySchemas.cs``
    ``Categories``），按"第一个下划线之前的部分是 category"切分，与 C# 侧
    ``AssetRefConventions.TryParseSpriteSetId`` 同一假设。
    """
    trimmed = relative_directory.strip("/")
    prefix = "sprites/"
    if not trimmed.startswith(prefix):
        return None
    flattened = trimmed[len(prefix):]
    if "_" not in flattened:
        return None
    category, _, name = flattened.partition("_")
    if not category or not name:
        return None
    return f"sprite.{category}.{name}"


def try_parse_icon_id(relative_file_path: str) -> str | None:
    """:func:`icon_file` 的反向解析：给定形如 ``"icons/<category>/<name>.png"`` 的相对文件路径
    （允许前导 ``/``，扩展名允许缺省），尝试还原出 ``icon_id``；解析失败返回 ``None``。
    """
    trimmed = relative_file_path.lstrip("/")
    prefix = "icons/"
    if not trimmed.startswith(prefix):
        return None
    remainder = trimmed[len(prefix):]
    segments = remainder.split("/")
    if len(segments) != 2 or not segments[0] or not segments[1]:
        return None
    category, file_name = segments
    name = file_name.rsplit(".", 1)[0] if "." in file_name else file_name
    if not name:
        return None
    return f"icon.{category}.{name}"
