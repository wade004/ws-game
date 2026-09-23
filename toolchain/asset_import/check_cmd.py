"""``check`` 子命令：assets/<dataset>/ 与 data/<dataset>/display|vfx|sfx|world 交叉校验。

检查项（见任务口径，是对 04 第 5 节"外形映射存在"等检查项在资产文件层面的补充，
不重复实现 toolchain/validator 里的表字段级校验）：

- ``display.map`` 引用的 ``sprite_set_id``/``icon_id`` 对应的资产文件/目录必须存在。
- 每个精灵集内，同一层（跨方向档位）尺寸必须一致。
- 锚点必须落在对应方向档位画布范围内。
- ``direction_count`` 与实际落地的方向档位数一致。
- **``mirror_pairs`` 完整性**：每个已落地、但不属于该 ``direction_count`` 下 canonical
  档位集合（``directions.canonical_slot_names``）的方向档位，必须在 ``mirror_pairs`` 中有一条
  ``direction_slot`` 与之对应的声明，且其 ``mirror_of`` 指向的档位本身已落地（有实际帧文件）。
- **声明锚点缺失**：``display.map.anchor_points`` 声明的每个 ``anchor_id``，必须能在精灵集自己的
  ``anchors.json``（默认方向档位，即 ``directions.default_direction_slot`` 对应档位）标注中找到，
  对应 14 第 11 节"缺锚点：声明需要的锚点在标注中缺失"这条校验项。
- ``vfx.def``/``sfx.def`` 引用的 ``resource_ref``/``variants`` 对应资产文件必须存在。
- **地图引用存在性**：``world.map`` 每一行引用的地图分层图（见 ``map`` 子命令、14 第 9 节）在
  ``assets/<dataset>/maps/<name>/`` 下必须存在必需分层文件（``ground.png``/``overlay.png``）。

``--only``（逗号分隔，取值 ``sprite``/``vfx``/``sfx``/``world``/``display_anim`` 的子集，省略则
默认五项 ``DEFAULT_DOMAINS`` 全跑——见下方"判断记录"）：只跑选定的检查域，供门禁在某个数据集的
部分域尚未接入真实资产时先只校验已接入的那部分（不放宽已选中域的判断逻辑本身，只是缩小本次运行
覆盖的表范围）。

``--json``（消费方反馈第 62 条）：stdout 只输出一个 JSON 文档（UTF-8、中文不转义），结构见
:class:`CheckIssue`/:func:`run` 判断记录；不加 ``--json`` 时文本输出与改造前逐字节一致。加或不加
``--json``，返回码语义相同（无问题为 0，有问题为 1）。

判断记录（``--json`` 顶层新增 ``domain_counts`` 字段，消费方反馈第 74 条）：文本汇总行
（``run()`` 末尾 ``[check] dataset=... : 检查 N 条 <表>...``）里的各域记录条数此前只写进这行给人读
的自然语言句子，``--json`` 文档不携带同一份信息，消费方（内容编辑器项目）因此无法拆除自建的文本
正则解析、改用结构化 ``--json`` 输出（详见其反馈原文）。修法是纯加法：在既有六个顶层键
（``tool``/``dataset``/``domains``/``ok``/``counts``/``issues``）之外新增 ``domain_counts``
（``dict[str, int]``，键为 :class:`CheckIssue` 已用的 ``table`` 名风格，如 ``"display.map"``），
不改任何既有字段的名字/类型/含义。数值口径：与文本汇总行完全同源、同一批 ``_load_rows()`` 返回值
的 ``len()``——即"该表 JSON 文件里 ``rows`` 数组的行数"，与该行是否命中检查条件（如
``display.map`` 只对 ``kind == "sprite"`` 的行跑校验）、是否报出问题都无关；不存在的表文件按
``_load_rows`` 既有行为算 0 行。域未被 ``--only`` 选中时，该域对应的表键整体不出现在
``domain_counts`` 字典里（区分"未跑该域"与"跑了但 0 条"，与消费方 ``AssetCheckSummary`` 三个
可空字段"不臆造零值"的既有设计一致）；``display_anim`` 一个域对应 ``display.anim_set``/
``display.weapon_style``/``display.equip_visual`` 三张表，三个键同进同出。输出顺序按表名字符串
排序（与既有 ``domains`` 字段 ``sorted(only)`` 同一手法），不依赖 dict 插入顺序或字典枚举顺序，
保持跨进程/跨 Python 版本确定性（AGENTS.md 第 3 节）。

消费方反馈第 66 条核实结论（见 ``architecture/adr/0037-资源引用路径推导契约扩展到vfx-sfx-model-anim_set.md``
"决策 3"）：本工具此前（1.43.0）未扩展到 ``display.anim_set.clips.resource_ref``/``display.
equip_visual.mesh_ref``/``model_ref``/``display.weapon_style.auto_attack_anim``/
``cast_anim_override`` 五个字段的资源存在性检查——这五个字段在运行期按消费实体的显示类型
（sprite/model）存在两条并存的路径解析规则，且 model 型解析出的资源不落在本工具 ``--assets-root``
检查域内（另见引擎侧 Resources 逻辑路径，不是磁盘文件路径），本工具单看数据表本身无法判定某一行
该按哪条规则核对，勉强实现会产生假阳性/假阴性；``display.weapon_style.swing_vfx``/
``impact_vfx_override`` 是指向 ``vfx.def`` 的表引用（非直接资源文件引用），引用完整性属
``toolchain/validator`` 的软引用校验域（该校验器当前未对软引用做存在性检查，是已知、超出本次
任务范围的缺口，非本工具需要补的检查）。``camera_profile``/``ui_layout_definition``/
``shell_menu_definition`` 三张表 schema 均不含任何资源引用字段（已独立核对，见回复文档），本工具
无需为它们新增域。

ADR-0038 决策 6 后半消除了上述阻塞的根因——类别前缀此后唯一决定路径空间，不再需要先弄清楚"这一行
被哪种类型的实体消费"：新增 ``display_anim`` 检查域，覆盖 ``display.anim_set.clips.resource_ref``/
``display.weapon_style.auto_attack_anim``/``cast_anim_override``/``display.equip_visual.
mesh_ref`` 四个字段——经 :func:`ref_conventions.resolve_path_space` 判断路径空间，只对"资产根
相对"（``sprite_anim``/``paperdoll`` 前缀）的值做存在性检查，"引擎侧逻辑路径"（``anim``/``model``
前缀）的值按 ADR-0037 决策 3 同一理由跳过（不在本工具 ``--assets-root`` 检查域内）。

判断记录（``display_anim`` 纳入默认检查集合）：``display_anim`` 域此前暂不进默认集合的唯一理由是
``data/_sample/display/display.equip_visual.json`` 现有一行 ``mesh_ref:
"sprite.item.sample_hero_hat_test"`` 仍用 ADR-0038 之前的旧 ``sprite`` 前缀（该前缀本身路由到
"资产根相对"路径空间——``sprites/item_sample_hero_hat_test`` 目录——但目录结构与 ``paperdoll``
前缀期望的单文件不同，该目录若不存在会被本域误判为"缺失"）。数据迁移任务已把该行改为
``paperdoll.item.sample_hero_hat_test``，并对全部四个受影响字段的样例数据逐行核实迁移到正确的
类别前缀、补齐对应占位资产（``sprite_anim``/``paperdoll`` 两类目录结构），该理由已消除——
``display_anim`` 现登记进 ``DEFAULT_DOMAINS``，省略 ``--only`` 时随其余四项一并跑。

判断记录（ADR-0071 决策 1：``display.equip_visual.mesh_ref`` 的 ``paperdoll`` 类别改走专属校验）：
sprite 型 ``mesh_ref`` 语义变更为"装备层资源集引用"（与身体层 ``display.map.sprite_set_id`` 同一
位置，经 ``SpriteViewBase.ResolveEquipLayerResourceId`` 与身体层同一套方向档位公式换算），运行期
不再把它当 :func:`ref_conventions.paperdoll_layer_file` 描述的单个扁平文件消费。``mesh_ref`` 取值
本身仍是 ``paperdoll`` 前缀（未改类别前缀集合），但 ``_check_equip_visual_row`` 现对该类别单独分派到
:func:`_check_equip_visual_paperdoll_layers`——按 :data:`ref_conventions.EQUIP_LAYER_CHECK_DIRECTIONS`
三个方向档位各自核对 ``sprites/<mesh_ref 去掉类别前缀>/<方向>/<slot_id 推导出的层名>.png``（新增检查名
``display_anim_equip_layer_file_missing``），不再复用 ``_check_display_anim_ref`` 的
``display_anim_paperdoll_file_missing`` 单文件分支——该分支本身不删除（仍是稳定的检查名契约，理论上
留给其它未来场景），只是不再被 ``display.equip_visual`` 这一个字段命中。
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from . import directions
from .common import (
    DIRECTION_SLOT_ID_PREFIX,
    AssetImportError,
    find_repo_root,
    read_json,
    resolve_root,
    strip_domain,
)
from .ref_conventions import (
    EQUIP_LAYER_CHECK_DIRECTIONS,
    AssetRefPathSpace,
    map_directory,
    map_ground_file,
    map_overlay_file,
    paperdoll_equip_layer_file,
    resolve_path_space,
    sfx_resource_file,
    vfx_resource_dir,
)

DEFAULT_DOMAINS = ("sprite", "vfx", "sfx", "world", "display_anim")

# ADR-0038 决策 6 后半：display_anim 域已实现、已测试，数据迁移任务完成后随即纳入 DEFAULT_DOMAINS
# （省略 --only 时的默认覆盖集合）——见模块 docstring"判断记录（display_anim 纳入默认检查集合）"。
ALL_DOMAINS = DEFAULT_DOMAINS

# 地图必需分层文件对应的 ref_conventions 路径函数（14 第 9.1 节"框架固定项"三层中的地面图/
# 前景遮挡层；装饰层 decal.png、导航标注 nav_hint.png 属于可选辅助分层，见 map 子命令与 14 第 9
# 节勘误）。消费方反馈第 75 条（ADR-0053）：改为调用 map_cmd.py 同一组共享函数，不再自行拼接
# 文件名字面量，见 _check_world_row 判断记录。
REQUIRED_MAP_LAYER_FUNCS = (map_ground_file, map_overlay_file)

SEVERITY_ERROR = "error"
SEVERITY_WARNING = "warning"

# 消费方反馈第 62 条：稳定的 snake_case 检查名常量，供 --json 消费方按 check 字段过滤/分类，
# 也在本文件顶部/README 帮助文本中列出清单，供人工查阅。命名风格参照 toolchain/validator 既有
# 检查名（如 world_map_point_outside_image）；本工具当前诊断与 validator 既有规则均无语义重合，
# 未见可复用同名的既有检查。
CHECK_SPRITE_SET_ID_MISSING = "sprite_set_id_missing"
CHECK_SPRITE_SET_ID_FORMAT_INVALID = "sprite_set_id_format_invalid"
CHECK_SPRITE_SET_DIR_MISSING = "sprite_set_dir_missing"
CHECK_SPRITE_ATLAS_JSON_MISSING = "sprite_atlas_json_missing"
CHECK_SPRITE_ANCHORS_JSON_MISSING = "sprite_anchors_json_missing"
CHECK_SPRITE_ATLAS_PNG_MISSING = "sprite_atlas_png_missing"
CHECK_SPRITE_DIRECTION_COUNT_MISMATCH = "sprite_direction_count_mismatch"
CHECK_SPRITE_LAYER_SIZE_INCONSISTENT = "sprite_layer_size_inconsistent"
CHECK_SPRITE_FRAME_FILE_MISSING = "sprite_frame_file_missing"
CHECK_SPRITE_MIRROR_SOURCE_NOT_LANDED = "sprite_mirror_pair_source_not_landed"
CHECK_SPRITE_MIRROR_PAIR_MISSING = "sprite_mirror_pair_missing"
CHECK_SPRITE_ANCHOR_OUT_OF_CANVAS = "sprite_anchor_out_of_canvas"
CHECK_SPRITE_DECLARED_ANCHOR_MISSING = "sprite_declared_anchor_missing"
# ADR-0081（消费方反馈第二十三批）：anchors.json 顶层可选的 pixels_per_unit 声明——出现时必须是
# 正数，不出现不报错、不要求声明（决策 C，不新增"必须声明"类检查）。
CHECK_SPRITE_PIXELS_PER_UNIT_INVALID = "sprite_pixels_per_unit_invalid"
CHECK_ICON_ID_FORMAT_INVALID = "icon_id_format_invalid"
CHECK_ICON_FILE_MISSING = "icon_file_missing"

CHECK_VFX_RESOURCE_REF_MISSING = "vfx_resource_ref_missing"
CHECK_VFX_ATLAS_MISSING = "vfx_atlas_missing"
CHECK_VFX_FRAMES_JSON_MISSING = "vfx_frames_json_missing"

CHECK_SFX_RESOURCE_REF_MISSING = "sfx_resource_ref_missing"
CHECK_SFX_RESOURCE_FILE_MISSING = "sfx_resource_file_missing"
CHECK_SFX_VARIANTS_MISSING_RESOURCE_REF = "sfx_variants_missing_resource_ref"
CHECK_SFX_VARIANT_FILE_MISSING = "sfx_variant_file_missing"

CHECK_WORLD_MAP_DIR_MISSING = "world_map_dir_missing"
CHECK_WORLD_MAP_LAYER_MISSING = "world_map_layer_missing"
CHECK_WORLD_MAP_SCENE_REF_MISMATCH = "world_map_scene_ref_mismatch"
CHECK_WORLD_MAP_NAV_REF_MISMATCH = "world_map_nav_ref_mismatch"

# ADR-0038 决策 6 后半：display_anim 域——覆盖 display.anim_set.clips.resource_ref/
# display.weapon_style.auto_attack_anim/cast_anim_override/display.equip_visual.mesh_ref 四个
# 字段中路由到"资产根相对"路径空间（sprite_anim/paperdoll 前缀）的取值；"引擎侧逻辑路径"
# （anim/model 前缀）的取值不检查存在性，见模块 docstring。
CHECK_DISPLAY_ANIM_REF_CATEGORY_INVALID = "display_anim_ref_category_invalid"
CHECK_DISPLAY_ANIM_SPRITE_ANIM_ATLAS_MISSING = "display_anim_sprite_anim_atlas_missing"
CHECK_DISPLAY_ANIM_SPRITE_ANIM_FRAMES_JSON_MISSING = "display_anim_sprite_anim_frames_json_missing"
CHECK_DISPLAY_ANIM_PAPERDOLL_FILE_MISSING = "display_anim_paperdoll_file_missing"
# ADR-0071 决策 1：display.equip_visual.mesh_ref 的 paperdoll 类别取值不再按上面这个扁平单文件规则
# 校验（该规则仍保留给其它调用方，见 ref_conventions.paperdoll_layer_file 判断记录），改按
# EQUIP_LAYER_CHECK_DIRECTIONS 三个方向档位各自的层文件核对，见 _check_equip_visual_paperdoll_layers。
CHECK_DISPLAY_ANIM_EQUIP_LAYER_FILE_MISSING = "display_anim_equip_layer_file_missing"
# 兜底：某取值路由到"资产根相对"路径空间，但类别前缀不是本域已知的 sprite_anim/paperdoll 两种
# （典型例子：数据迁移前的旧 mesh_ref，前缀仍是 sprite——见模块 docstring"判断记录"）；本域尚未
# 针对这类遗留前缀的具体磁盘布局实现专门检查，只做"路径是否存在"的最小核对，如实报告不掩盖。
CHECK_DISPLAY_ANIM_ASSET_MISSING = "display_anim_asset_missing"

CHECK_NAMES: tuple[str, ...] = (
    CHECK_SPRITE_SET_ID_MISSING,
    CHECK_SPRITE_SET_ID_FORMAT_INVALID,
    CHECK_SPRITE_SET_DIR_MISSING,
    CHECK_SPRITE_ATLAS_JSON_MISSING,
    CHECK_SPRITE_ANCHORS_JSON_MISSING,
    CHECK_SPRITE_ATLAS_PNG_MISSING,
    CHECK_SPRITE_DIRECTION_COUNT_MISMATCH,
    CHECK_SPRITE_LAYER_SIZE_INCONSISTENT,
    CHECK_SPRITE_FRAME_FILE_MISSING,
    CHECK_SPRITE_MIRROR_SOURCE_NOT_LANDED,
    CHECK_SPRITE_MIRROR_PAIR_MISSING,
    CHECK_SPRITE_ANCHOR_OUT_OF_CANVAS,
    CHECK_SPRITE_DECLARED_ANCHOR_MISSING,
    CHECK_SPRITE_PIXELS_PER_UNIT_INVALID,
    CHECK_ICON_ID_FORMAT_INVALID,
    CHECK_ICON_FILE_MISSING,
    CHECK_VFX_RESOURCE_REF_MISSING,
    CHECK_VFX_ATLAS_MISSING,
    CHECK_VFX_FRAMES_JSON_MISSING,
    CHECK_SFX_RESOURCE_REF_MISSING,
    CHECK_SFX_RESOURCE_FILE_MISSING,
    CHECK_SFX_VARIANTS_MISSING_RESOURCE_REF,
    CHECK_SFX_VARIANT_FILE_MISSING,
    CHECK_WORLD_MAP_DIR_MISSING,
    CHECK_WORLD_MAP_LAYER_MISSING,
    CHECK_WORLD_MAP_SCENE_REF_MISMATCH,
    CHECK_WORLD_MAP_NAV_REF_MISMATCH,
    CHECK_DISPLAY_ANIM_REF_CATEGORY_INVALID,
    CHECK_DISPLAY_ANIM_SPRITE_ANIM_ATLAS_MISSING,
    CHECK_DISPLAY_ANIM_SPRITE_ANIM_FRAMES_JSON_MISSING,
    CHECK_DISPLAY_ANIM_PAPERDOLL_FILE_MISSING,
    CHECK_DISPLAY_ANIM_EQUIP_LAYER_FILE_MISSING,
    CHECK_DISPLAY_ANIM_ASSET_MISSING,
)


@dataclass
class CheckIssue:
    """结构化诊断对象（消费方反馈第 62 条）：全部 ``_check_*`` 函数先产出该对象，文本模式与
    ``--json`` 模式共享同一份诊断来源，各自只负责渲染（:meth:`render_text`/:meth:`as_dict`），
    不重复实现判断逻辑本身。

    字段与 ``toolchain/validator`` 的 ``--json`` ``issues[]``（见 ``Program.cs``
    ``AppendIssueJson``）对齐：``severity``/``table``/``check``/``message`` 同名同语义；
    ``record_key`` 对应 validator 的同名字段（本工具语境下就是"行 id"）。``field_path`` 是本工具
    新增字段，支持数组下标定位（如 ``"variants[0]"``）——语义同 validator
    ``ValidationIssue.Field`` 已支持的下标路径能力（消费方反馈第 57 条落地），只是键名不同：
    validator 沿用历史键名 ``"field"``，本工具是全新契约，直接采用更明确的 ``"field_path"``，
    两者不要求同名，只要求同语义；无法定位到具体字段时为 ``None``。``path`` 是本工具专属的新增
    字段（validator 不做资源文件存在性检查，没有对应字段）：给出"期望存在但实际缺失"的资源相对
    路径，格式/一致性类诊断没有具体缺失路径，留 ``None``。
    """

    severity: str
    table: str
    record_key: str
    check: str
    message: str
    field_path: Optional[str] = None
    path: Optional[str] = None

    def as_dict(self) -> dict:
        return {
            "severity": self.severity,
            "table": self.table,
            "record_key": self.record_key,
            "field_path": self.field_path,
            "check": self.check,
            "message": self.message,
            "path": self.path,
        }

    def render_text(self) -> str:
        """文本模式渲染：与本工具改造前的既有输出逐字节一致（``f"{row_id}: {message}"``）。"""
        return f"{self.record_key}: {self.message}"


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--dataset", default="_sample", help="要校验的数据集名，默认 _sample")
    parser.add_argument("--assets-root", default=None, help="资产根目录，默认仓库 assets/")
    parser.add_argument("--data-root", default=None, help="数据根目录，默认仓库 data/")
    parser.add_argument(
        "--only",
        default=None,
        metavar="sprite,vfx,sfx,world,display_anim",
        help=(
            "只跑逗号分隔的检查域子集（取值见上）；省略则跑 DEFAULT_DOMAINS 全部五项（sprite/vfx/"
            "sfx/world/display_anim，ADR-0038 决策 6 后半：数据迁移任务完成后 display_anim 已纳入"
            "默认集合，见模块 docstring 判断记录）"
        ),
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help=(
            "消费方反馈第 62 条：stdout 只输出一个 JSON 文档（不转义中文），其余日志改走 stderr；"
            "不加本参数时文本输出逐字节不变。JSON 顶层结构：{tool, dataset, domains, ok, "
            "counts:{error,warning}, domain_counts:{<table>: <行数>, ...}（消费方反馈第 74 条，"
            "未跑的域对应表键不出现）, issues:[{severity, table, record_key, field_path, check, "
            "message, path}]}。稳定 check 名清单见 check_cmd.CHECK_NAMES / 模块 docstring。"
        ),
    )


def _parse_only(value: str | None) -> set[str]:
    if value is None:
        # ADR-0038 决策 6 后半：省略 --only 时的默认覆盖集合是 DEFAULT_DOMAINS——数据迁移任务完成
        # 后 display_anim 已纳入默认集合，与 ALL_DOMAINS 等同，见模块 docstring 判断记录。
        return set(DEFAULT_DOMAINS)
    domains = {v.strip() for v in value.split(",") if v.strip()}
    unknown = domains - set(ALL_DOMAINS)
    if unknown:
        raise AssetImportError(
            f"--only 取值 {sorted(unknown)} 不在合法域集合 {ALL_DOMAINS} 内"
        )
    return domains


def _load_rows(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    data = read_json(path)
    return data.get("rows", []) if isinstance(data, dict) else []


def _bare_slot(value: str) -> str:
    """去掉方向档位 Id 前缀（"dir."），未带前缀的裸名字原样返回，见 common.DIRECTION_SLOT_ID_PREFIX。"""
    if value.startswith(DIRECTION_SLOT_ID_PREFIX):
        return value[len(DIRECTION_SLOT_ID_PREFIX):]
    return value


def _check_mirror_pairs(row: dict, slot_names: set[str], problems: list[CheckIssue]) -> None:
    """校验 mirror_pairs 完整性：非原创档位必须有声明，声明的镜像来源必须已落地。"""
    row_id = row.get("id", "?")
    direction_count = row.get("direction_count")
    if direction_count not in (4, 8, 16):
        return

    try:
        canonical = set(directions.canonical_slot_names(direction_count))
    except AssetImportError:
        return

    mirror_pairs = row.get("mirror_pairs", [])
    declared: dict[str, str] = {}
    for mp in mirror_pairs:
        target = _bare_slot(mp.get("direction_slot", ""))
        source = _bare_slot(mp.get("mirror_of", ""))
        declared[target] = source
        if source and source not in slot_names:
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR,
                table="display.map",
                record_key=row_id,
                check=CHECK_SPRITE_MIRROR_SOURCE_NOT_LANDED,
                field_path="mirror_pairs",
                message=(
                    f"mirror_pairs 声明 '{target}' 的镜像来源 '{mp.get('mirror_of')}' "
                    f"未落地（不在已生成的方向档位 {sorted(slot_names)} 中）"
                ),
            ))

    for slot in sorted(slot_names - canonical):
        if slot not in declared:
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR,
                table="display.map",
                record_key=row_id,
                check=CHECK_SPRITE_MIRROR_PAIR_MISSING,
                field_path="mirror_pairs",
                message=(
                    f"方向档位 '{slot}' 不是原创档位（不在 direction_count={direction_count} "
                    f"的 canonical 集合 {sorted(canonical)} 内），但 mirror_pairs 未声明其镜像来源"
                ),
            ))


def _check_declared_anchors(
    row: dict, anchors_data: dict, problems: list[CheckIssue]
) -> None:
    """校验 display.map.anchor_points 声明的每个锚点在 anchors.json 默认档位标注中都存在。"""
    row_id = row.get("id", "?")
    anchor_points = row.get("anchor_points")
    direction_count = row.get("direction_count")
    if not anchor_points or direction_count not in (4, 8, 16):
        return

    default_slot = directions.default_direction_slot(direction_count)
    slot_entry = anchors_data.get(default_slot, {})
    actual_names = set(slot_entry.get("anchors", {}).keys())

    for anchor_id in anchor_points:
        if anchor_id not in actual_names:
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR,
                table="display.map",
                record_key=row_id,
                check=CHECK_SPRITE_DECLARED_ANCHOR_MISSING,
                field_path=f"anchor_points.{anchor_id}",
                message=(
                    f"display.map.anchor_points 声明的锚点 '{anchor_id}' 在方向档位 "
                    f"'{default_slot}' 的 anchors.json 标注中缺失"
                ),
            ))


def _check_sprite_row(row: dict, assets_root: Path, dataset: str, problems: list[CheckIssue]) -> None:
    sprite_set_id = row.get("sprite_set_id")
    row_id = row.get("id", "?")
    if not sprite_set_id:
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
            check=CHECK_SPRITE_SET_ID_MISSING, field_path="sprite_set_id",
            message="kind=sprite 但缺少 sprite_set_id",
        ))
        return
    parts = sprite_set_id.split(".", 2)
    if len(parts) != 3:
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
            check=CHECK_SPRITE_SET_ID_FORMAT_INVALID, field_path="sprite_set_id",
            message=f"sprite_set_id '{sprite_set_id}' 格式不是 sprite.<category>.<name>",
        ))
        return
    # 目录名与 sprite 子命令的落地规则、运行时 UnityResourceLoader/SpriteViewBase 的资源 id
    # 解析规则对齐：sprite_set_id 去掉首段类别前缀 "sprite." 后把剩余点号换成下划线，
    # 即 "<category>_<name>"（parts[1] + "_" + parts[2]），不是只用 parts[2]。
    name = parts[1] + "_" + parts[2]
    set_dir = assets_root / dataset / "sprites" / name
    if not set_dir.is_dir():
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
            check=CHECK_SPRITE_SET_DIR_MISSING, field_path="sprite_set_id", path=str(set_dir),
            message=f"sprite_set_id '{sprite_set_id}' 对应目录不存在: {set_dir}",
        ))
        return

    atlas_json_path = set_dir / "atlas.json"
    anchors_json_path = set_dir / "anchors.json"
    atlas_png_path = set_dir / "atlas.png"
    if not atlas_json_path.is_file():
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
            check=CHECK_SPRITE_ATLAS_JSON_MISSING, field_path="sprite_set_id", path=str(atlas_json_path),
            message=f"缺少 {atlas_json_path}",
        ))
    if not anchors_json_path.is_file():
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
            check=CHECK_SPRITE_ANCHORS_JSON_MISSING, field_path="sprite_set_id", path=str(anchors_json_path),
            message=f"缺少 {anchors_json_path}",
        ))
    if not atlas_png_path.is_file():
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
            check=CHECK_SPRITE_ATLAS_PNG_MISSING, field_path="sprite_set_id", path=str(atlas_png_path),
            message=f"缺少 {atlas_png_path}",
        ))

    frame_rects: dict[str, dict] = {}
    slot_names: set[str] = set()
    if atlas_json_path.is_file():
        atlas_data = read_json(atlas_json_path)
        frame_rects = atlas_data.get("frames", {})

        slot_names = {key.split("/", 1)[0] for key in frame_rects}
        expected_count = row.get("direction_count")
        if expected_count is not None and len(slot_names) != expected_count:
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
                check=CHECK_SPRITE_DIRECTION_COUNT_MISMATCH, field_path="direction_count",
                message=(
                    f"direction_count={expected_count} 但实际落地 {len(slot_names)} 个方向档位: "
                    + ", ".join(sorted(slot_names))
                ),
            ))

        by_layer: dict[str, dict[str, tuple[int, int]]] = {}
        for key, rect in frame_rects.items():
            slot, _, layer = key.partition("/")
            layer_key = layer if layer else "*"
            by_layer.setdefault(layer_key, {})[slot] = (rect["w"], rect["h"])
        for layer_key, sizes_by_slot in by_layer.items():
            distinct_sizes = set(sizes_by_slot.values())
            if len(distinct_sizes) > 1:
                problems.append(CheckIssue(
                    severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
                    check=CHECK_SPRITE_LAYER_SIZE_INCONSISTENT, field_path=None,
                    message=f"层 '{layer_key}' 在不同方向档位尺寸不一致: {sizes_by_slot}",
                ))

        for slot in slot_names:
            expected_files = [k for k in frame_rects if k == slot or k.startswith(f"{slot}/")]
            for key in expected_files:
                layer = key.partition("/")[2]
                out_path = (
                    set_dir / f"{slot}.png" if not layer else set_dir / slot / f"{layer}.png"
                )
                if not out_path.is_file():
                    problems.append(CheckIssue(
                        severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
                        check=CHECK_SPRITE_FRAME_FILE_MISSING, field_path="sprite_set_id",
                        path=str(out_path), message=f"帧文件缺失: {out_path}",
                    ))

        _check_mirror_pairs(row, slot_names, problems)

    anchors_data: dict = {}
    if anchors_json_path.is_file():
        anchors_data = read_json(anchors_json_path)
        # ADR-0081（消费方反馈第二十三批）：顶层可选的 pixels_per_unit 是与方向档位条目平级的
        # 标量兄弟键（sprite_cmd.py --pixels-per-unit 显式传入时才写出，见该脚本判断记录），不是
        # 方向档位条目本身——必须先摘出来单独校验，否则下面按"每个顶层键都是方向档位条目"遍历时
        # 会对它调用 .get("canvas_size", ...)，一个 float 没有 .get 方法，直接抛异常。
        if "pixels_per_unit" in anchors_data:
            declared_pixels_per_unit = anchors_data["pixels_per_unit"]
            is_valid_positive_number = (
                isinstance(declared_pixels_per_unit, (int, float))
                and not isinstance(declared_pixels_per_unit, bool)
                and declared_pixels_per_unit > 0
            )
            if not is_valid_positive_number:
                problems.append(CheckIssue(
                    severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
                    check=CHECK_SPRITE_PIXELS_PER_UNIT_INVALID, field_path=None,
                    path=str(anchors_json_path),
                    message=(
                        f"{anchors_json_path} 顶层 pixels_per_unit 必须是正数，"
                        f"实际为 {declared_pixels_per_unit!r}"
                    ),
                ))
        direction_slot_entries = {
            k: v for k, v in anchors_data.items() if k != "pixels_per_unit"
        }
        for slot_name, entry in direction_slot_entries.items():
            canvas = entry.get("canvas_size", [0, 0])
            cw, ch = canvas[0], canvas[1]
            for anchor_name, coord in entry.get("anchors", {}).items():
                x, y = coord[0], coord[1]
                if not (0 <= x <= cw and 0 <= y <= ch):
                    problems.append(CheckIssue(
                        severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
                        check=CHECK_SPRITE_ANCHOR_OUT_OF_CANVAS, field_path=None,
                        message=(
                            f"方向档位 '{slot_name}' 锚点 '{anchor_name}' "
                            f"({x}, {y}) 超出画布范围 ({cw}x{ch})"
                        ),
                    ))
        _check_declared_anchors(row, anchors_data, problems)

    icon_id = row.get("icon_id")
    if icon_id:
        icon_parts = icon_id.split(".", 2)
        if len(icon_parts) == 3:
            icon_path = assets_root / dataset / "icons" / icon_parts[1] / f"{icon_parts[2]}.png"
            if not icon_path.is_file():
                problems.append(CheckIssue(
                    severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
                    check=CHECK_ICON_FILE_MISSING, field_path="icon_id", path=str(icon_path),
                    message=f"icon_id '{icon_id}' 对应文件不存在: {icon_path}",
                ))
        else:
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table="display.map", record_key=row_id,
                check=CHECK_ICON_ID_FORMAT_INVALID, field_path="icon_id",
                message=f"icon_id '{icon_id}' 格式不是 icon.<category>.<name>",
            ))


def _check_vfx_row(row: dict, assets_root: Path, dataset: str, problems: list[CheckIssue]) -> None:
    row_id = row.get("id", "?")
    resource_ref = row.get("resource_ref")
    if not resource_ref:
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="vfx.def", record_key=row_id,
            check=CHECK_VFX_RESOURCE_REF_MISSING, field_path="resource_ref",
            message="缺少 resource_ref",
        ))
        return
    # 消费方反馈第 65 条：改用 ref_conventions.vfx_resource_dir 统一推导（与 vfx_cmd.py 落地路径、
    # AssetRefConventions.VfxResourceDir、UnityResourceLoader.ResolveEffectDir 同一规则），直接对
    # resource_ref 字段本身求值，不再从 id 反推——vfx_cmd.py 产出的 resource_ref 恒为
    # "vfx.<flatten_id_segment 结果>"，两种算法结果逐字节相同（见该函数判断记录）。
    out_dir = assets_root / dataset / vfx_resource_dir(resource_ref)
    atlas_path = out_dir / "atlas.png"
    frames_path = out_dir / "frames.json"
    if not atlas_path.is_file():
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="vfx.def", record_key=row_id,
            check=CHECK_VFX_ATLAS_MISSING, field_path="resource_ref", path=str(atlas_path),
            message=f"resource_ref 对应图集缺失: {atlas_path}",
        ))
    # 运行时 ResourceKind.Effect 读 frames.json（非 atlas.json），见
    # UnityResourceLoader.TryDecodeEffect 与 assets/_placeholder/vfx/burn/frames.json 样例。
    if not frames_path.is_file():
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="vfx.def", record_key=row_id,
            check=CHECK_VFX_FRAMES_JSON_MISSING, field_path="resource_ref", path=str(frames_path),
            message=f"resource_ref 对应帧数据缺失: {frames_path}",
        ))


def _check_sfx_row(row: dict, assets_root: Path, dataset: str, problems: list[CheckIssue]) -> None:
    """校验 resource_ref（必填）与 variants（可选）各自对应的扁平音频文件是否存在，
    并校验 variants 若存在则必须包含 resource_ref 本身（见 sfx 子命令落地口径与
    presentation/vfx_sfx/schema/VfxSfxSchemas.cs 的 sfx.def schema：resource_ref 必填 Id、
    variants 可选 Id 列表）。运行时 ResourceKind.Audio 按
    "audio/<资源引用 id 去掉 'sfx.'>.wav" 解析（扁平文件，非子目录），这里同规则校验。
    """
    row_id = row.get("id", "?")
    # 消费方反馈第 65 条：改用 ref_conventions.sfx_resource_file 统一推导（与 sfx_cmd.py 落地路径、
    # AssetRefConventions.SfxResourceFile、UnityResourceLoader.ResolvePath(Audio) 同一规则），不再
    # 自行拼接——见该函数判断记录"与旧内联实现的差异，不影响任何当前可产出数据"。
    dataset_assets_root = assets_root / dataset

    resource_ref = row.get("resource_ref")
    if not resource_ref:
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="sfx.def", record_key=row_id,
            check=CHECK_SFX_RESOURCE_REF_MISSING, field_path="resource_ref",
            message="kind=sfx 但缺少 resource_ref",
        ))
    else:
        ref_path = dataset_assets_root / sfx_resource_file(resource_ref)
        if not ref_path.is_file():
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table="sfx.def", record_key=row_id,
                check=CHECK_SFX_RESOURCE_FILE_MISSING, field_path="resource_ref", path=str(ref_path),
                message=f"resource_ref '{resource_ref}' 对应音频文件缺失: {ref_path}",
            ))

    variants = row.get("variants", [])
    if variants and resource_ref and resource_ref not in variants:
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="sfx.def", record_key=row_id,
            check=CHECK_SFX_VARIANTS_MISSING_RESOURCE_REF, field_path="variants",
            message=f"variants {variants} 未包含 resource_ref '{resource_ref}'",
        ))
    for idx, ref in enumerate(variants):
        ref_path = dataset_assets_root / sfx_resource_file(ref)
        if not ref_path.is_file():
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table="sfx.def", record_key=row_id,
                check=CHECK_SFX_VARIANT_FILE_MISSING, field_path=f"variants[{idx}]", path=str(ref_path),
                message=f"variants 引用 '{ref}' 对应音频文件缺失: {ref_path}",
            ))


def _check_world_row(row: dict, assets_root: Path, dataset: str, problems: list[CheckIssue]) -> None:
    """校验 world.map 一行引用的地图分层图（map 子命令产出）是否存在，见 14 第 9 节。

    消费方反馈第 64 条：此前本函数在本文件内重复定义两次（逐字节相同，第二份覆盖第一份，
    不影响运行结果），本次删除后一份重复定义，只保留本份。

    判断记录（消费方反馈第 75 条，ADR-0053：收口第二处独立的地图路径拼接实现）：本函数改动前
    自行拼接 ``"maps" / name``（``name`` 由 ``common.strip_domain`` 得到），是仓库内独立于
    ``map_cmd.py`` 的第二处地图路径拼接实现，与 ADR-0053"路径约定权威出处唯一"的目标相悖。
    核对确认：``strip_domain``（只按第一个点号切一次，不折叠剩余点号）与
    ``ref_conventions.map_directory`` 内部使用的 ``strip_category_prefix``（额外把剩余部分的
    点号也换成下划线）仅在地图名本身含多个点号时才给出不同结果——与 ADR-0053"负面"一节已记录
    的已知边界同一类；仓库内全部 world.map 行 id 均为 ``world.<单段名称>`` 形状（架构文档 05
    第 4.1 节 ``teleport_target_ref`` 解析规则也以此为前提），两种算法对当前及约定形状下的合法
    数据逐字节相同（id 缺失/不含点号时"原样使用 row_id"的兜底分支在两侧也一致）。本次改为调用
    ``map_directory``/``map_ground_file``/``map_overlay_file``，消除这第二处独立实现；地图名
    含多个点号属 ADR-0053 已接受的已知边界，不在本次处理范围内。
    """
    row_id = row.get("id", "?")
    map_dir = assets_root / dataset / map_directory(row_id)
    if not map_dir.is_dir():
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="world.map", record_key=row_id,
            check=CHECK_WORLD_MAP_DIR_MISSING, field_path=None, path=str(map_dir),
            message=f"地图分层图目录不存在: {map_dir}（见 import_assets.py map 子命令）",
        ))
        return
    for layer_file_func in REQUIRED_MAP_LAYER_FUNCS:
        layer_path = assets_root / dataset / layer_file_func(row_id)
        if not layer_path.is_file():
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table="world.map", record_key=row_id,
                check=CHECK_WORLD_MAP_LAYER_MISSING, field_path=None, path=str(layer_path),
                message=f"地图分层图缺失: {layer_path}",
            ))

    name = strip_domain(row_id) if "." in row_id else row_id
    scene_ref = row.get("scene_ref")
    if scene_ref is not None and scene_ref != f"scene.{name}":
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="world.map", record_key=row_id,
            check=CHECK_WORLD_MAP_SCENE_REF_MISMATCH, field_path="scene_ref",
            message=f"scene_ref '{scene_ref}' 与地图目录名推导出的引用 id 'scene.{name}' 不一致",
        ))
    nav_ref = row.get("nav_ref")
    if nav_ref is not None and nav_ref != f"nav.{name}":
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="world.map", record_key=row_id,
            check=CHECK_WORLD_MAP_NAV_REF_MISMATCH, field_path="nav_ref",
            message=f"nav_ref '{nav_ref}' 与地图目录名推导出的引用 id 'nav.{name}' 不一致",
        ))


def _check_display_anim_ref(
    table: str, row_id: str, field_path: str, resource_ref: str,
    assets_root: Path, dataset: str, problems: list[CheckIssue],
) -> None:
    """ADR-0038 决策 6 后半：核对单个资源引用值——先经 :func:`resolve_path_space` 判断路径空间，
    类别前缀不合法（含不含点号）时报 :data:`CHECK_DISPLAY_ANIM_REF_CATEGORY_INVALID`；"引擎侧逻辑
    路径"（``anim``/``model`` 前缀）跳过存在性检查（ADR-0037 决策 3 同一理由）；"资产根相对"
    （``sprite_anim``/``paperdoll`` 及遗留前缀）按各自磁盘布局做存在性检查，见模块 docstring。
    """
    try:
        space, relative_path = resolve_path_space(resource_ref)
    except ValueError as exc:
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table=table, record_key=row_id,
            check=CHECK_DISPLAY_ANIM_REF_CATEGORY_INVALID, field_path=field_path,
            message=str(exc),
        ))
        return

    if space is AssetRefPathSpace.ENGINE_LOGICAL_PATH:
        # model 型消费实体的引擎侧逻辑路径：不在本工具 --assets-root 检查域内（ADR-0037 决策 3）。
        return

    dataset_root = assets_root / dataset
    category, _, _ = resource_ref.partition(".")

    if category == "sprite_anim":
        out_dir = dataset_root / relative_path
        atlas_path = out_dir / "atlas.png"
        frames_path = out_dir / "frames.json"
        if not atlas_path.is_file():
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table=table, record_key=row_id,
                check=CHECK_DISPLAY_ANIM_SPRITE_ANIM_ATLAS_MISSING, field_path=field_path, path=str(atlas_path),
                message=f"资源引用 '{resource_ref}' 对应图集缺失: {atlas_path}",
            ))
        if not frames_path.is_file():
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table=table, record_key=row_id,
                check=CHECK_DISPLAY_ANIM_SPRITE_ANIM_FRAMES_JSON_MISSING, field_path=field_path, path=str(frames_path),
                message=f"资源引用 '{resource_ref}' 对应帧数据缺失: {frames_path}",
            ))
    elif category == "paperdoll":
        file_path = dataset_root / relative_path
        if not file_path.is_file():
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table=table, record_key=row_id,
                check=CHECK_DISPLAY_ANIM_PAPERDOLL_FILE_MISSING, field_path=field_path, path=str(file_path),
                message=f"资源引用 '{resource_ref}' 对应纸娃娃层文件缺失: {file_path}",
            ))
    else:
        # 遗留前缀（如未迁移的旧 mesh_ref: "sprite.*"）：最小核对，见 CHECK_DISPLAY_ANIM_ASSET_MISSING
        # 判断记录。含扩展名按文件核对，否则按目录核对。
        target = dataset_root / relative_path
        exists = target.is_file() if target.suffix else target.is_dir()
        if not exists:
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table=table, record_key=row_id,
                check=CHECK_DISPLAY_ANIM_ASSET_MISSING, field_path=field_path, path=str(target),
                message=f"资源引用 '{resource_ref}'（遗留类别前缀 '{category}'）对应路径不存在: {target}",
            ))


def _check_anim_set_row(row: dict, assets_root: Path, dataset: str, problems: list[CheckIssue]) -> None:
    row_id = row.get("id", "?")
    clips = row.get("clips", {})
    for clip_name, clip in clips.items():
        resource_ref = clip.get("resource_ref") if isinstance(clip, dict) else None
        if resource_ref:
            _check_display_anim_ref(
                "display.anim_set", row_id, f"clips[{clip_name}].resource_ref", resource_ref,
                assets_root, dataset, problems,
            )


def _check_weapon_style_row(row: dict, assets_root: Path, dataset: str, problems: list[CheckIssue]) -> None:
    row_id = row.get("id", "?")
    auto_attack_anim = row.get("auto_attack_anim")
    if auto_attack_anim:
        _check_display_anim_ref(
            "display.weapon_style", row_id, "auto_attack_anim", auto_attack_anim,
            assets_root, dataset, problems,
        )

    cast_anim_override = row.get("cast_anim_override", {})
    for skill_id, anim_clip_id in cast_anim_override.items():
        if anim_clip_id:
            _check_display_anim_ref(
                "display.weapon_style", row_id, f"cast_anim_override[{skill_id}]", anim_clip_id,
                assets_root, dataset, problems,
            )


def _check_equip_visual_paperdoll_layers(
    row_id: str, slot_id: str | None, mesh_ref: str, assets_root: Path, dataset: str, problems: list[CheckIssue],
) -> None:
    """ADR-0071 决策 1：sprite 型 mesh_ref 语义变更为"装备层资源集引用"，与身体层 sprite_set_id
    同一套方向档位换算解析（见 presentation/render/core/SpriteViewBase.ResolveEquipLayerResourceId
    判断记录），运行期不再把 mesh_ref 当唯一扁平文件消费——改校验运行期实际会解析到的
    :data:`EQUIP_LAYER_CHECK_DIRECTIONS` 三个方向档位层文件，层名取自本行 ``slot_id`` 最后一个点分段
    （与 ``SpriteViewBase.LayerNameFromSlotId`` 同一规则）。"""
    if not slot_id:
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="display.equip_visual", record_key=row_id,
            check=CHECK_DISPLAY_ANIM_REF_CATEGORY_INVALID, field_path="slot_id",
            message=f"mesh_ref '{mesh_ref}' 使用 paperdoll 类别（sprite 型纸娃娃层）时 slot_id 必须存在，"
                    "用于推导纸娃娃层名（ADR-0071 决策 1）",
        ))
        return

    layer_name = slot_id.rpartition(".")[-1]
    dataset_root = assets_root / dataset
    for direction in EQUIP_LAYER_CHECK_DIRECTIONS:
        file_path = dataset_root / paperdoll_equip_layer_file(mesh_ref, direction, layer_name)
        if not file_path.is_file():
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table="display.equip_visual", record_key=row_id,
                check=CHECK_DISPLAY_ANIM_EQUIP_LAYER_FILE_MISSING, field_path="mesh_ref", path=str(file_path),
                message=f"资源引用 '{mesh_ref}' 方向档位 '{direction}' 对应的纸娃娃层文件缺失: {file_path}",
            ))


def _check_equip_visual_row(row: dict, assets_root: Path, dataset: str, problems: list[CheckIssue]) -> None:
    row_id = row.get("id", "?")
    mesh_ref = row.get("mesh_ref")
    if not mesh_ref:
        return

    category, _, _ = mesh_ref.partition(".")
    if category == "paperdoll":
        # ADR-0071 决策 1：paperdoll 类别专属 sprite 型纸娃娃层，不再经共享的
        # _check_display_anim_ref 单文件规则（该规则仍服务其它调用方，见 paperdoll_layer_file
        # 判断记录），改走本文件专属的三方向层文件核对。
        _check_equip_visual_paperdoll_layers(row_id, row.get("slot_id"), mesh_ref, assets_root, dataset, problems)
        return

    _check_display_anim_ref(
        "display.equip_visual", row_id, "mesh_ref", mesh_ref,
        assets_root, dataset, problems,
    )


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    assets_root = resolve_root(args.assets_root, repo_root, "assets")
    data_root = resolve_root(args.data_root, repo_root, "data")
    only = _parse_only(args.only)
    use_json = bool(getattr(args, "json", False))
    # --json 时全部人类可读输出（含本来就有的逐问题文本、汇总行）改走 stderr，stdout 只留 JSON
    # 文档本身（消费方反馈第 62 条）；不加 --json 时行为与改造前完全一致（走 stdout）。
    log_stream = sys.stderr if use_json else sys.stdout

    problems: list[CheckIssue] = []

    display_rows: list[dict] = []
    vfx_rows: list[dict] = []
    sfx_rows: list[dict] = []
    world_rows: list[dict] = []
    anim_set_rows: list[dict] = []
    weapon_style_rows: list[dict] = []
    equip_visual_rows: list[dict] = []

    if "sprite" in only:
        display_rows = _load_rows(data_root / args.dataset / "display" / "display.map.json")
        for row in display_rows:
            if row.get("kind") == "sprite":
                _check_sprite_row(row, assets_root, args.dataset, problems)

    if "vfx" in only:
        vfx_rows = _load_rows(data_root / args.dataset / "vfx" / "vfx.def.json")
        for row in vfx_rows:
            _check_vfx_row(row, assets_root, args.dataset, problems)

    if "sfx" in only:
        sfx_rows = _load_rows(data_root / args.dataset / "sfx" / "sfx.def.json")
        for row in sfx_rows:
            _check_sfx_row(row, assets_root, args.dataset, problems)

    if "world" in only:
        world_rows = _load_rows(data_root / args.dataset / "world" / "world.map.json")
        for row in world_rows:
            _check_world_row(row, assets_root, args.dataset, problems)

    if "display_anim" in only:
        anim_set_rows = _load_rows(data_root / args.dataset / "display" / "display.anim_set.json")
        for row in anim_set_rows:
            _check_anim_set_row(row, assets_root, args.dataset, problems)

        weapon_style_rows = _load_rows(data_root / args.dataset / "display" / "display.weapon_style.json")
        for row in weapon_style_rows:
            _check_weapon_style_row(row, assets_root, args.dataset, problems)

        equip_visual_rows = _load_rows(data_root / args.dataset / "display" / "display.equip_visual.json")
        for row in equip_visual_rows:
            _check_equip_visual_row(row, assets_root, args.dataset, problems)

    for issue in problems:
        print(issue.render_text(), file=log_stream)

    print(
        f"[check] dataset={args.dataset} only={','.join(sorted(only))}: 检查 {len(display_rows)} 条 "
        f"display.map / {len(vfx_rows)} 条 vfx.def / {len(sfx_rows)} 条 sfx.def / {len(world_rows)} "
        f"条 world.map / {len(anim_set_rows)} 条 display.anim_set / {len(weapon_style_rows)} 条 "
        f"display.weapon_style / {len(equip_visual_rows)} 条 display.equip_visual，发现 "
        f"{len(problems)} 个问题",
        file=log_stream,
    )

    if use_json:
        error_count = sum(1 for p in problems if p.severity == SEVERITY_ERROR)
        warning_count = sum(1 for p in problems if p.severity == SEVERITY_WARNING)
        # 消费方反馈第 74 条：各域实际加载的记录条数，纯加法新增字段，口径/键名风格/排序规则见
        # 模块 docstring"判断记录（--json 顶层新增 domain_counts 字段）"。
        domain_counts: dict[str, int] = {}
        if "sprite" in only:
            domain_counts["display.map"] = len(display_rows)
        if "vfx" in only:
            domain_counts["vfx.def"] = len(vfx_rows)
        if "sfx" in only:
            domain_counts["sfx.def"] = len(sfx_rows)
        if "world" in only:
            domain_counts["world.map"] = len(world_rows)
        if "display_anim" in only:
            domain_counts["display.anim_set"] = len(anim_set_rows)
            domain_counts["display.weapon_style"] = len(weapon_style_rows)
            domain_counts["display.equip_visual"] = len(equip_visual_rows)

        document = {
            "tool": "import_assets.check",
            "dataset": args.dataset,
            "domains": sorted(only),
            "ok": not problems,
            "counts": {"error": error_count, "warning": warning_count},
            "domain_counts": {k: domain_counts[k] for k in sorted(domain_counts)},
            "issues": [p.as_dict() for p in problems],
        }
        # ensure_ascii=False：中文不转义（消费方反馈第 62 条原文要求）；stdout 只这一行 JSON。
        print(json.dumps(document, ensure_ascii=False))

    return 1 if problems else 0
