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

import enum

__all__ = [
    "strip_category_prefix",
    "sprite_set_directory",
    "icon_file",
    "try_parse_sprite_set_id",
    "try_parse_icon_id",
    "vfx_resource_dir",
    "sfx_resource_file",
    "anim_clip_logical_path",
    "model_logical_path",
    "sprite_anim_dir",
    "paperdoll_layer_file",
    "AssetRefPathSpace",
    "KNOWN_CATEGORIES",
    "resolve_path_space",
    "map_directory",
    "map_ground_file",
    "map_overlay_file",
    "map_decal_file",
    "map_nav_hint_file",
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


def vfx_resource_dir(resource_ref_id: str) -> str:
    """把 ``vfx.def.resource_ref`` 解析为该特效资源在资产根目录下的相对目录路径（正斜杠分隔，
    不以 ``/`` 结尾）：``"vfx/<资源引用id去掉类别前缀，点号换下划线>"``——目录下固定含
    ``atlas.png``/``frames.json`` 两个文件。与 C# 侧 ``AssetRefConventions.VfxResourceDir``、
    ``vfx_cmd.py`` 落地产物、Unity 侧 ``UnityResourceLoader.ResolveEffectDir`` 三处逐一核对一致
    （消费方反馈第 65 条，见该类型判断记录）。
    """
    return "vfx/" + strip_category_prefix(resource_ref_id)


def sfx_resource_file(resource_ref_id: str) -> str:
    """把 ``sfx.def.resource_ref``（或 ``variants`` 列表内的单个 Id）解析为该音频变体在资产根目录下
    的相对文件路径（正斜杠分隔，含 ``.wav`` 扩展名）：
    ``"sfx/<资源引用id去掉类别前缀，点号换下划线>.wav"``——扁平文件，非子目录。与 C# 侧
    ``AssetRefConventions.SfxResourceFile`` 同一套规则（消费方反馈第 65 条：详见该方法判断记录
    "与 check_cmd.py 既有内联实现的差异，不影响任何当前可产出数据"）。
    """
    return "sfx/" + strip_category_prefix(resource_ref_id) + ".wav"


def anim_clip_logical_path(resource_ref_id: str) -> str:
    """把 model 型 ``display.anim_set.clips[*].resource_ref`` 解析为 Unity
    ``Resources.Load<AnimationClip>`` 可消费的相对路径（不含扩展名，不以任何"资产根目录"为基准，
    与 :func:`vfx_resource_dir`/:func:`sfx_resource_file` 不是同一路径空间，不能拼进
    ``assets/<dataset>/`` 做文件存在性检查——见消费方反馈第 65/66 条回复文档"待设计层确认"一节）：
    ``"GameFoundation/anim_clips/<资源引用id去掉类别前缀，点号换下划线>"``——与 C# 侧
    ``AssetRefConventions.AnimClipLogicalPath``/``UnityResourceLoader.ResolveAnimClipResourcesPath``
    逐字对应。仅覆盖 model 型消费该字段时的规则，sprite 型的并存规则见 C# 侧类型判断记录。命名上以
    ``logical_path`` 与 ``_dir``/``_file`` 区分两种路径空间——本函数与 :func:`model_logical_path`
    返回引擎侧已导入的逻辑资源路径，:func:`vfx_resource_dir`/:func:`sfx_resource_file` 返回资产
    根目录相对路径。
    """
    return "GameFoundation/anim_clips/" + strip_category_prefix(resource_ref_id)


def model_logical_path(resource_ref_id: str) -> str:
    """把 ``display.map.model_ref``、model 型 ``display.equip_visual.mesh_ref``/``model_ref`` 解析为
    Unity ``Resources.Load<GameObject>`` 可消费的相对路径（不含扩展名，同 :func:`anim_clip_logical_path`
    不以任何"资产根目录"为基准）：``"GameFoundation/models/<资源引用id去掉类别前缀，点号换下划线>"``
    ——与 C# 侧 ``AssetRefConventions.ModelLogicalPath``/``UnityResourceLoader.ResolveModelResourcesPath``
    逐字对应。命名上以 ``logical_path`` 与 ``_dir``/``_file`` 区分两种路径空间——本函数与
    :func:`anim_clip_logical_path` 返回引擎侧已导入的逻辑资源路径，
    :func:`vfx_resource_dir`/:func:`sfx_resource_file` 返回资产根目录相对路径。
    """
    return "GameFoundation/models/" + strip_category_prefix(resource_ref_id)


def sprite_anim_dir(resource_ref_id: str) -> str:
    """ADR-0038 决策 2：新增类别前缀 ``sprite_anim``，把 sprite 型消费实体的动画帧资源从 ``anim``
    前缀（决策 3：该前缀此后专属 model 型的 :func:`anim_clip_logical_path`）中拆出。把
    ``sprite_anim.<name>`` 解析为该动画帧资源在资产根目录下的相对目录路径（正斜杠分隔，不以 ``/``
    结尾）：``"sprite_anim/<资源引用id去掉类别前缀，点号换下划线>"``——目录结构与 :func:`vfx_resource_dir`
    同构（内含图集与帧数据两个文件）。与 C# 侧 ``AssetRefConventions.SpriteAnimDir`` 逐字对应。
    """
    return "sprite_anim/" + strip_category_prefix(resource_ref_id)


def paperdoll_layer_file(resource_ref_id: str) -> str:
    """ADR-0038 决策 4 附带条款：``display.equip_visual.mesh_ref`` 的 sprite 型取值（"纸娃娃层
    资源 id"）核实为与 :func:`sprite_set_directory` 承载的"精灵集目录标识"不等价（后者产出一个目录，
    前者运行期解析为单个扁平文件，不按方向拆分），按决策 1 总原则新增独立类别前缀 ``paperdoll``。把
    ``paperdoll.<category>.<name>`` 解析为该纸娃娃层覆盖资源在资产根目录下的相对文件路径（正斜杠
    分隔，含 ``.png`` 扩展名）：``"paperdoll/<资源引用id去掉类别前缀，点号换下划线>.png"``——单独一个
    子目录（不与 :func:`sprite_set_directory` 共享 ``sprites/`` 根），与 :func:`sfx_resource_file`
    同一惯例（资产根相对、扁平单文件、带扩展名）。与 C# 侧 ``AssetRefConventions.PaperdollLayerFile``
    逐字对应。
    """
    return "paperdoll/" + strip_category_prefix(resource_ref_id) + ".png"


class AssetRefPathSpace(enum.Enum):
    """见 :func:`resolve_path_space`：资源引用标识最终落在哪一类磁盘/引擎资源命名空间，与 C# 侧
    ``AssetRefConventions.AssetRefPathSpace`` 逐字对应（ADR-0038 决策 5）。"""

    ASSET_ROOT_RELATIVE = "asset_root_relative"
    """相对内容工具 ``--assets-root`` 的资产根目录，可与具体数据集目录拼接后做文件系统存在性检查。"""

    ENGINE_LOGICAL_PATH = "engine_logical_path"
    """引擎适配层内部已导入好的逻辑资源路径，不落在资产导入工具的资产根目录下，不能做文件系统
    存在性检查（见 ADR-0037 决策 3）。"""


KNOWN_CATEGORIES: tuple[str, ...] = (
    "sprite", "icon", "vfx", "sfx", "sprite_anim", "paperdoll", "anim", "model",
)
"""见 :func:`resolve_path_space`：全部已登记的合法类别前缀，与 C# 侧
``AssetRefConventions.KnownCategories`` 逐字对应。"""


def resolve_path_space(resource_ref_id: str) -> tuple[AssetRefPathSpace, str]:
    """ADR-0038 决策 5：公开路由总入口——输入资源引用标识，按其类别前缀（第一个点分段）唯一确定
    应使用的推导方法，返回 ``(路径空间, 相对路径)`` 二元组。遇到未登记的类别前缀，或标识不含任何
    点号（无法取出类别前缀），均抛出 :class:`ValueError`，不做静默兜底——错误信息同时给出收到的
    前缀与合法前缀集合。与 C# 侧 ``AssetRefConventions.ResolvePathSpace`` 各自独立实现、不互相
    调用，靠同一组输入/期望值对照测试互相校核（见 toolchain/tests/test_ref_conventions.py 与
    core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs 对应用例）。
    """
    if "." not in resource_ref_id:
        raise ValueError(
            f"资源引用 '{resource_ref_id}' 不含类别前缀（无点号），"
            f"合法类别前缀集合：{'/'.join(KNOWN_CATEGORIES)}"
        )

    category, _, _ = resource_ref_id.partition(".")

    if category == "sprite":
        return AssetRefPathSpace.ASSET_ROOT_RELATIVE, sprite_set_directory(resource_ref_id)
    if category == "icon":
        return AssetRefPathSpace.ASSET_ROOT_RELATIVE, icon_file(resource_ref_id)
    if category == "vfx":
        return AssetRefPathSpace.ASSET_ROOT_RELATIVE, vfx_resource_dir(resource_ref_id)
    if category == "sfx":
        return AssetRefPathSpace.ASSET_ROOT_RELATIVE, sfx_resource_file(resource_ref_id)
    if category == "sprite_anim":
        return AssetRefPathSpace.ASSET_ROOT_RELATIVE, sprite_anim_dir(resource_ref_id)
    if category == "paperdoll":
        return AssetRefPathSpace.ASSET_ROOT_RELATIVE, paperdoll_layer_file(resource_ref_id)
    if category == "anim":
        return AssetRefPathSpace.ENGINE_LOGICAL_PATH, anim_clip_logical_path(resource_ref_id)
    if category == "model":
        return AssetRefPathSpace.ENGINE_LOGICAL_PATH, model_logical_path(resource_ref_id)

    raise ValueError(
        f"资源引用 '{resource_ref_id}' 的类别前缀 '{category}' 不合法，"
        f"合法类别前缀集合：{'/'.join(KNOWN_CATEGORIES)}"
    )


def map_directory(map_id: str) -> str:
    """消费方反馈第 75 条（ADR-0053）：把 ``world.map`` 行的 ``id``（形如 ``world.<map>``）解析为
    该地图分层图在资产根目录下的相对目录路径（正斜杠分隔，不以 ``/`` 结尾，不含扩展名）：
    ``"maps/<map>"``——与 C# 侧 ``AssetRefConventions.MapDirectory`` 逐字对应，与
    :func:`sprite_set_directory`/:func:`vfx_resource_dir` 等同一路径空间（资产根相对，可直接与
    ``assets/<dataset>/`` 拼接后做文件系统存在性检查）。此前本格式唯一的出处是
    ``map_cmd.py`` 模块文档字符串里的一段说明性文字，从未以可调用函数的形式暴露；``map_cmd.py``
    此后改为调用本函数，不再自行拼接。

    判断记录（已知边界，同 C# 侧对应方法判断记录）：``map_cmd.py`` 改动前的 ``out_dir`` 直接拼接
    ``args.map`` 原始字符串，不做任何转义；本函数经 :func:`strip_category_prefix` 把 ``world.``
    之后剩余部分的点号也换成下划线。两者仅在地图名本身含点号时才会给出不同结果——仓库内目前全部
    地图名均为不含点号的单段名称，两种算法结果逐字节相同。
    """
    return "maps/" + strip_category_prefix(map_id)


def map_ground_file(map_id: str) -> str:
    """ADR-0053：地面层（框架固定项，必需）相对文件路径：``"maps/<map>/ground.png"``，与
    ``map_cmd.py`` 落地产物逐字节一致。"""
    return map_directory(map_id) + "/ground.png"


def map_overlay_file(map_id: str) -> str:
    """ADR-0053：前景遮挡层（框架固定项，必需）相对文件路径：``"maps/<map>/overlay.png"``，与
    ``map_cmd.py`` 落地产物逐字节一致。"""
    return map_directory(map_id) + "/overlay.png"


def map_decal_file(map_id: str) -> str:
    """ADR-0053：装饰层（可选）相对文件路径：``"maps/<map>/decal.png"``，与 ``map_cmd.py``
    落地产物逐字节一致。"""
    return map_directory(map_id) + "/decal.png"


def map_nav_hint_file(map_id: str) -> str:
    """ADR-0053：导航标注参考图（可选，供引擎适配层侧手工绘制导航数据时参考）相对文件路径：
    ``"maps/<map>/nav_hint.png"``，与 ``map_cmd.py`` 落地产物逐字节一致。"""
    return map_directory(map_id) + "/nav_hint.png"


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
