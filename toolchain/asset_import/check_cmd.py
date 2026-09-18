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

``--only``（逗号分隔，取值 ``sprite``/``vfx``/``sfx``/``world`` 的子集，省略则四项全跑）：只跑
选定的检查域，供门禁在某个数据集的部分域尚未接入真实资产时先只校验已接入的那部分（不放宽已选中
域的判断逻辑本身，只是缩小本次运行覆盖的表范围）。

``--json``（消费方反馈第 62 条）：stdout 只输出一个 JSON 文档（UTF-8、中文不转义），结构见
:class:`CheckIssue`/:func:`run` 判断记录；不加 ``--json`` 时文本输出与改造前逐字节一致。加或不加
``--json``，返回码语义相同（无问题为 0，有问题为 1）。

消费方反馈第 66 条核实结论（见 ``architecture/adr/0037-资源引用路径推导契约扩展到vfx-sfx-model-anim_set.md``
"决策 3"）：本工具本次不扩展到 ``display.anim_set.clips.resource_ref``/``display.equip_visual.
mesh_ref``/``model_ref``/``display.weapon_style.auto_attack_anim``/``cast_anim_override`` 五个
字段的资源存在性检查——这五个字段在运行期按消费实体的显示类型（sprite/model）存在两条并存的路径
解析规则，且 model 型解析出的资源不落在本工具 ``--assets-root`` 检查域内（另见引擎侧 Resources
逻辑路径，不是磁盘文件路径），本工具单看数据表本身无法判定某一行该按哪条规则核对，勉强实现会
产生假阳性/假阴性；``display.weapon_style.swing_vfx``/``impact_vfx_override`` 是指向 ``vfx.def``
的表引用（非直接资源文件引用），引用完整性属 ``toolchain/validator`` 的软引用校验域（该校验器
当前未对软引用做存在性检查，是已知、超出本次任务范围的缺口，非本工具需要补的检查）。
``camera_profile``/``ui_layout_definition``/``shell_menu_definition`` 三张表 schema 均不含任何
资源引用字段（已独立核对，见回复文档），本工具无需为它们新增域。
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
from .ref_conventions import sfx_resource_file, vfx_resource_dir

ALL_DOMAINS = ("sprite", "vfx", "sfx", "world")

# 地图必需分层文件（14 第 9.1 节"框架固定项"三层中的地面图/前景遮挡层；装饰层
# decal.png、导航标注 nav_hint.png 属于可选辅助分层，见 map 子命令与 14 第 9 节勘误）。
REQUIRED_MAP_LAYERS = ("ground.png", "overlay.png")

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
        metavar="sprite,vfx,sfx,world",
        help="只跑逗号分隔的检查域子集（取值见上），省略则四项全跑",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help=(
            "消费方反馈第 62 条：stdout 只输出一个 JSON 文档（不转义中文），其余日志改走 stderr；"
            "不加本参数时文本输出逐字节不变。JSON 顶层结构：{tool, dataset, domains, ok, "
            "counts:{error,warning}, issues:[{severity, table, record_key, field_path, check, "
            "message, path}]}。稳定 check 名清单见 check_cmd.CHECK_NAMES / 模块 docstring。"
        ),
    )


def _parse_only(value: str | None) -> set[str]:
    if value is None:
        return set(ALL_DOMAINS)
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
        for slot_name, entry in anchors_data.items():
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
    """
    row_id = row.get("id", "?")
    name = strip_domain(row_id) if "." in row_id else row_id
    map_dir = assets_root / dataset / "maps" / name
    if not map_dir.is_dir():
        problems.append(CheckIssue(
            severity=SEVERITY_ERROR, table="world.map", record_key=row_id,
            check=CHECK_WORLD_MAP_DIR_MISSING, field_path=None, path=str(map_dir),
            message=f"地图分层图目录不存在: {map_dir}（见 import_assets.py map 子命令）",
        ))
        return
    for layer_file in REQUIRED_MAP_LAYERS:
        layer_path = map_dir / layer_file
        if not layer_path.is_file():
            problems.append(CheckIssue(
                severity=SEVERITY_ERROR, table="world.map", record_key=row_id,
                check=CHECK_WORLD_MAP_LAYER_MISSING, field_path=None, path=str(layer_path),
                message=f"地图分层图缺失: {layer_path}",
            ))

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

    for issue in problems:
        print(issue.render_text(), file=log_stream)

    print(
        f"[check] dataset={args.dataset} only={','.join(sorted(only))}: 检查 {len(display_rows)} 条 "
        f"display.map / {len(vfx_rows)} 条 vfx.def / {len(sfx_rows)} 条 sfx.def / {len(world_rows)} "
        f"条 world.map，发现 {len(problems)} 个问题",
        file=log_stream,
    )

    if use_json:
        error_count = sum(1 for p in problems if p.severity == SEVERITY_ERROR)
        warning_count = sum(1 for p in problems if p.severity == SEVERITY_WARNING)
        document = {
            "tool": "import_assets.check",
            "dataset": args.dataset,
            "domains": sorted(only),
            "ok": not problems,
            "counts": {"error": error_count, "warning": warning_count},
            "issues": [p.as_dict() for p in problems],
        }
        # ensure_ascii=False：中文不转义（消费方反馈第 62 条原文要求）；stdout 只这一行 JSON。
        print(json.dumps(document, ensure_ascii=False))

    return 1 if problems else 0
