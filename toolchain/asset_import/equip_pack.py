"""装备资产包校验与装备完整性报告（手感设计/08 第 2/5 节、ADR-0123）。

本模块是纯函数 + 数据类：输入"已加载的数据表"与"资产目录"，输出 :class:`EquipReport`；命令行装配与
文本/JSON 渲染在 :mod:`equip_cmd`。界面皮肤包校验在 :mod:`skin_pack`，两者共用同一份报告。

对每件可装备物品（``item.template`` 的槽位在 ``item.slot_definition`` 里 ``is_equipment`` 为真）检查：

错误级（该物品不能进入 validated 数据集，见 :meth:`EquipReport.is_validated`）：

- ``equip_icon_*``：经 ``display_ref`` → ``display.map.icon_id`` 解析的图标资源缺失/尺寸不合规/贴边；
- ``equip_visual_missing``：装备没有 ``display.equip_visual`` 行；
- ``equip_layer_static_missing``：``paperdoll`` 型（sprite）缺任一 canonical 方向档的静态层图
  ``sprites/<mesh>/<方向>/<层名>.png``（逐层剪辑缺失时 ADR-0072 回落到它，它自己不能缺）；
- ``equip_layer_clip_missing``：必备姿势键 × canonical 方向档的层剪辑两级探测（ADR-0100：带方向 → 不带方向）
  都不命中；
- ``equip_model_slot_unknown`` / ``equip_model_socket_unknown``：``model`` 型槽位/挂点命名不在 model 型
  ``display.map`` 登记的 ``slots``/``sockets`` 里（14 第 4.3 节：命名逐字一致）；
- ``equip_weapon_pair_missing`` / ``equip_weapon_id_mismatch``：武器的 ``display.weapon_style`` 与
  ``feel.weapon`` 缺其一，或二者不同 id（同 id 分表，05 第 2 节）；
- ``equip_preview_direction_invalid``：``display.equip_visual.preview_direction`` 不是该游戏声明的方向档；
- ``equip_behind_direction_invalid``：``display.equip_visual.behind_directions``（可选，逐方向层序）不是方向槽位 id 列表，或含该游戏没声明的方向档；
- ``equip_anchor_invalid`` / ``equip_anchor_out_of_bounds``：层精灵集 ``sprites/<mesh>/anchors.json`` 里可选的 ``directions.<方向>.grip``
  不是 ``[x, y]`` 数字对，或落在该方向静态层图画布之外（预览按它把层对到身体挂接点，越界会把层摆出画面）。

警告级（报告记录回落目标，不阻断）：

- ``equip_opaque_coverage_low``：图标/静态层图的不透明像素覆盖率低于清单（``skin_manifest.json`` 的 ``icons.coverage`` /
  ``paperdoll.static_layer_coverage``）写的阈值——几乎看不见（细线条武器、画得太小），阈值写在清单里；
- ``equip_layer_density_mismatch``：层精灵集（带 ``anchors.json``）的顶层 ``pixels_per_unit`` 与身体精灵集（``ui_layout_definition`` 行
  ``preview_body_set``）的不一致或没声明——运行期合成里层与身体按各自密度换算，装备相对身体画得偏大或偏小，预览区（两者同一个缩放）看不出来（ADR-0155）；
- ``equip_layer_clip_missing_recommended``：推荐键层剪辑缺失（回落静态层图）；
- ``equip_override_clip_layer_missing``：武器表现档案的攻击/施法覆盖剪辑没有该装备的逐层剪辑（回落整身剪辑）；
- ``equip_family_without_pose_keys``：``feel.weapon.family`` 在姿势集里没有任何键（全部回落到基础键）；
- ``equip_sfx_material_missing``：``sfx_material`` 在 ``sfx.def`` 无对应层行（回落 ``generic``）；
- ``equip_sfx_ref_missing`` / ``equip_trail_ref_missing``：穿脱音效/拖尾引用在 ``sfx.def``/``vfx.def`` 无行。

**判断记录**

1. 必备/推荐分档取自 04 第 3 节清单（:func:`pose_key_tier`），不读任何生成器规格文件——游戏用真实美术
   替换假人姿势集后，本校验仍只依赖 ``display.anim_set`` 数据行（键集 + ``resource_ref``）。
2. 一件装备需要哪些键由"姿势解析会落到哪些键"推出（:func:`needed_pose_keys`）：身体跟随层（护甲等）
   覆盖姿势集全部键；武器层只覆盖该武器族的族键，加上姿势集里没有该族变体的无族键（受击/死亡/跳跃/施法/
   闪避——解析回落到它们，武器仍持在手里）。武器族在姿势集里没有任何族键时不逐键要求（回落到基础键，由
   ``equip_family_without_pose_keys`` 警告承担）。
3. 同一 ``resource_ref`` 的别名键（如 ``attack`` → ``attack.unarmed``）按资源去重，取较高档位。
4. 层剪辑路径与 ADR-0100 一致：``sprite_anim/<mesh 去前缀>__<剪辑去前缀>__<方向>__<层名>/`` 命中，不命中退一级
   去掉方向段；两级各要求 ``atlas.png`` + ``frames.json``。
5. 槽位有无外观由 ``item.slot_definition.has_appearance``（可选 Bool，缺省 ``true``）决定：缺省或 ``true`` 时装备类槽位的
   物品必须有 ``display.equip_visual`` 行（与该字段登记前行为逐位一致）；显式 ``false``（戒指/项链等不上身可见的槽位）时
   不要求外观行、不报 ``equip_visual_missing``，其余检查（图标、武器配对、音效/拖尾引用）照常。只认 JSON 布尔 ``false``，
   其它取值（字符串、``null``、缺省）一律按有外观处理——类型错误由数据加载期的 ``field_type`` 校验拦，这里不重复判。
   该槽位的物品若仍带了外观行，外观行照常校验（不因为槽位声明无外观就放过一份写了的外观）。
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Optional

from . import directions, skin_manifest
from .check_cmd import SEVERITY_ERROR, SEVERITY_WARNING, CheckIssue
from .common import DIRECTION_SLOT_ID_PREFIX, read_json
from .ref_conventions import paperdoll_equip_layer_file, strip_category_prefix

# ---------------------------------------------------------------------------
# 检查名（稳定契约，--json 消费方按 check 过滤）
# ---------------------------------------------------------------------------
CHECK_ICON_UNRESOLVED = "equip_icon_unresolved"
CHECK_ICON_FILE_MISSING = "equip_icon_file_missing"
CHECK_ICON_SIZE_INVALID = "equip_icon_size_invalid"
CHECK_VISUAL_MISSING = "equip_visual_missing"
CHECK_LAYER_STATIC_MISSING = "equip_layer_static_missing"
CHECK_LAYER_CLIP_MISSING = "equip_layer_clip_missing"
CHECK_LAYER_CLIP_MISSING_RECOMMENDED = "equip_layer_clip_missing_recommended"
CHECK_OVERRIDE_CLIP_LAYER_MISSING = "equip_override_clip_layer_missing"
CHECK_MODEL_SLOT_UNKNOWN = "equip_model_slot_unknown"
CHECK_MODEL_SOCKET_UNKNOWN = "equip_model_socket_unknown"
CHECK_VISUAL_MODE_INVALID = "equip_visual_mode_invalid"
CHECK_WEAPON_PAIR_MISSING = "equip_weapon_pair_missing"
CHECK_WEAPON_ID_MISMATCH = "equip_weapon_id_mismatch"
CHECK_PREVIEW_DIRECTION_INVALID = "equip_preview_direction_invalid"
CHECK_FAMILY_WITHOUT_POSE_KEYS = "equip_family_without_pose_keys"
CHECK_SFX_MATERIAL_MISSING = "equip_sfx_material_missing"
CHECK_SFX_REF_MISSING = "equip_sfx_ref_missing"
CHECK_TRAIL_REF_MISSING = "equip_trail_ref_missing"
CHECK_ANIM_SET_MISSING = "equip_anim_set_missing"
CHECK_ICON_ALPHA_INVALID = "equip_icon_alpha_invalid"
CHECK_LAYER_IMAGE_INVALID = "equip_layer_image_invalid"
CHECK_LAYER_FRAME_COUNT_MISMATCH = "equip_layer_frame_count_mismatch"
CHECK_LAYER_FRAME_SIZE_MISMATCH = "equip_layer_frame_size_mismatch"
CHECK_LAYER_ATLAS_MISMATCH = "equip_layer_atlas_mismatch"
CHECK_OPAQUE_COVERAGE_LOW = "equip_opaque_coverage_low"
CHECK_ANCHOR_OUT_OF_BOUNDS = "equip_anchor_out_of_bounds"
CHECK_ANCHOR_INVALID = "equip_anchor_invalid"
CHECK_BEHIND_DIRECTION_INVALID = "equip_behind_direction_invalid"
CHECK_LAYER_DENSITY_MISMATCH = "equip_layer_density_mismatch"

CHECK_NAMES: tuple[str, ...] = (
    CHECK_ICON_UNRESOLVED, CHECK_ICON_FILE_MISSING, CHECK_ICON_SIZE_INVALID, CHECK_VISUAL_MISSING,
    CHECK_LAYER_STATIC_MISSING, CHECK_LAYER_CLIP_MISSING, CHECK_LAYER_CLIP_MISSING_RECOMMENDED,
    CHECK_OVERRIDE_CLIP_LAYER_MISSING, CHECK_MODEL_SLOT_UNKNOWN, CHECK_MODEL_SOCKET_UNKNOWN,
    CHECK_VISUAL_MODE_INVALID, CHECK_WEAPON_PAIR_MISSING, CHECK_WEAPON_ID_MISMATCH,
    CHECK_PREVIEW_DIRECTION_INVALID, CHECK_FAMILY_WITHOUT_POSE_KEYS, CHECK_SFX_MATERIAL_MISSING,
    CHECK_SFX_REF_MISSING, CHECK_TRAIL_REF_MISSING, CHECK_ANIM_SET_MISSING, CHECK_ICON_ALPHA_INVALID,
    CHECK_LAYER_IMAGE_INVALID, CHECK_LAYER_FRAME_COUNT_MISMATCH, CHECK_LAYER_FRAME_SIZE_MISMATCH,
    CHECK_LAYER_ATLAS_MISMATCH, CHECK_OPAQUE_COVERAGE_LOW, CHECK_ANCHOR_OUT_OF_BOUNDS, CHECK_ANCHOR_INVALID,
    CHECK_BEHIND_DIRECTION_INVALID, CHECK_LAYER_DENSITY_MISMATCH,
)

TABLE_ITEM = "item.template"
TABLE_EQUIP_VISUAL = "display.equip_visual"

DEFAULT_DIRECTION_COUNT = 8
DEFAULT_SFX_MATERIAL = "generic"
SFX_MATERIAL_LAYERS = ("swing", "impact")

# 图标规格取自界面资源契约清单（skin_manifest.json 的 icons 段），不在这里另抄一份。
ICON_MIN_SIDE = skin_manifest.load_manifest()["icons"]["size"]["min"]
ICON_MAX_SIDE = skin_manifest.load_manifest()["icons"]["size"]["max"]
# 不透明像素覆盖率阈值同样取自清单，不在这里另抄一份。
ICON_COVERAGE = skin_manifest.load_manifest()["icons"]["coverage"]
LAYER_COVERAGE = skin_manifest.load_manifest()["paperdoll"]["static_layer_coverage"]
ANCHOR_NAME_GRIP = "grip"
TABLE_UI_LAYOUT = "ui_layout_definition"
FIELD_PREVIEW_BODY_SET = "preview_body_set"

GAITS = ("walk", "run", "sprint")
STANCES = ("peace", "combat")
# 带武器族维度的状态（04 第 2 节：武器族主要服务运动态、待机与攻击）。
FAMILY_STATES = ("idle", "move", "attack", "block")
REQUIRED_BASES = ("idle", "move.walk", "move.run", "hit", "death", "attack")

TIER_REQUIRED = "required"
TIER_RECOMMENDED = "recommended"


@dataclass
class EquipIssue(CheckIssue):
    """装备完整性问题：在 :class:`CheckIssue` 上加 ``fallback``（警告级的回落目标）。

    ``as_dict`` 只在 ``fallback`` 非空时才多一个键，错误级条目的 JSON 形状与 :class:`CheckIssue` 逐字一致。
    """

    fallback: Optional[str] = None

    def as_dict(self) -> dict:
        d = super().as_dict()
        if self.fallback is not None:
            d["fallback"] = self.fallback
        return d

    def render_text(self) -> str:
        suffix = f"（回落目标: {self.fallback}）" if self.fallback else ""
        return f"{self.record_key}: [{self.check}] {self.message}{suffix}"


# ---------------------------------------------------------------------------
# 数据表加载
# ---------------------------------------------------------------------------

def load_tables(data_roots: Iterable[Path]) -> dict[str, list[dict]]:
    """递归读取各数据根下全部 ``*.json`` 表文件（外层 ``{"table", "rows"}`` 信封），同名表跨根按根顺序追加行。

    非信封 JSON（如 ``package.json``）静默跳过——与 ``validate_data.py`` 对数据根的宽容一致。"""
    tables: dict[str, list[dict]] = {}
    for root in data_roots:
        root = Path(root)
        if not root.is_dir():
            continue
        for path in sorted(root.rglob("*.json")):
            try:
                doc = read_json(path)
            except (OSError, ValueError):
                continue
            if not isinstance(doc, dict) or "table" not in doc or not isinstance(doc.get("rows"), list):
                continue
            tables.setdefault(str(doc["table"]), []).extend(r for r in doc["rows"] if isinstance(r, dict))
    return tables


# ---------------------------------------------------------------------------
# 姿势键解析
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class PoseKey:
    state: str
    gait: Optional[str]
    stance: Optional[str]
    family: Optional[str]
    variant: Optional[str]
    air: bool = False   # 空中攻击子状态 ``attack.air[.<族>]``（手感落地 M4-D）：``air`` 不是武器族，也不是变体

    def base(self) -> tuple:
        """去掉武器族维度后的键：(状态, 步态, 姿态, 变体, 是否空中)。"""
        return (self.state, self.gait, self.stance, self.variant, self.air)


def parse_pose_key(key: str) -> PoseKey:
    """``<state>[.<gait>][.<stance>][.<family>][.<variant>]``（04 第 2.1 节）。

    只有 ``idle/move/attack`` 带武器族段；其余状态（``hit/death/cast/jump/dodge``）的尾段是变体
    （如 ``hit.heavy``）。纯数字尾段（连招段 ``02``）永远是变体。"""
    segs = key.split(".")
    state = segs[0]
    i = 1
    gait = stance = family = variant = None
    air = False
    if state == "attack" and i < len(segs) and segs[i] == "air":
        air = True
        i += 1
    if state == "move" and i < len(segs) and segs[i] in GAITS:
        gait = segs[i]
        i += 1
    if i < len(segs) and segs[i] in STANCES:
        stance = segs[i]
        i += 1
    if state == "hit" and i + 1 < len(segs) and segs[i] == "block":
        # 格挡受击 ``hit.block[.<族>]``（M4-D）：``block`` 是变体，其后的族段仍是武器族（盾族有自己的剪辑）
        variant = "block"
        i += 1
        if i < len(segs):
            family = segs[i]
            i += 1
    if state in FAMILY_STATES and i < len(segs) and not segs[i].isdigit():
        family = segs[i]
        i += 1
    if i < len(segs):
        variant = ".".join(segs[i:])
    return PoseKey(state, gait, stance, family, variant, air)


def pose_key_tier(key: str) -> str:
    """04 第 3 节：必备 = ``idle/move.walk/move.run/attack(每族一段)/hit/death``，其余推荐。"""
    pk = parse_pose_key(key)
    if pk.variant is not None or pk.air:
        return TIER_RECOMMENDED
    base = ".".join(x for x in (pk.state, pk.gait, pk.stance) if x)
    return TIER_REQUIRED if base in REQUIRED_BASES else TIER_RECOMMENDED


def _tier_max(a: str, b: str) -> str:
    return TIER_REQUIRED if TIER_REQUIRED in (a, b) else TIER_RECOMMENDED


def needed_pose_keys(clips: dict[str, dict], family: Optional[str], is_weapon_layer: bool) -> dict[str, str]:
    """返回 ``{剪辑 resource_ref: 档位}``：该装备层需要逐层剪辑的剪辑（判断记录 2/3）。"""
    parsed = {k: parse_pose_key(k) for k in clips}
    chosen: list[str] = []
    if not is_weapon_layer:
        chosen = list(clips)
    else:
        family_bases = {pk.base() for pk in parsed.values() if pk.family == family}
        for k, pk in parsed.items():
            if pk.family == family:
                chosen.append(k)
            elif pk.family is None and pk.base() not in family_bases:
                chosen.append(k)
    out: dict[str, str] = {}
    for k in chosen:
        ref = clips[k].get("resource_ref") if isinstance(clips[k], dict) else None
        if not ref:
            continue
        tier = pose_key_tier(k)
        out[ref] = _tier_max(out[ref], tier) if ref in out else tier
    return out


def pose_families(clips: dict[str, dict]) -> set[str]:
    return {pk.family for pk in (parse_pose_key(k) for k in clips) if pk.family}


# ---------------------------------------------------------------------------
# 报告
# ---------------------------------------------------------------------------

@dataclass
class ItemReport:
    item_id: str
    slot: str = ""
    is_weapon: bool = False
    mode: str = ""              # sprite / model / none
    family: Optional[str] = None
    layer: Optional[str] = None
    mesh_ref: Optional[str] = None
    icon: Optional[str] = None  # 解析出的图标相对路径
    clip_slots: int = 0         # 逐层剪辑槽位总数（键 × 方向）
    clip_hits: int = 0          # 其中带方向命中
    clip_fallback_nodir: int = 0
    clip_missing: int = 0
    issues: list[EquipIssue] = field(default_factory=list)

    @property
    def errors(self) -> list[EquipIssue]:
        return [i for i in self.issues if i.severity == SEVERITY_ERROR]

    @property
    def warnings(self) -> list[EquipIssue]:
        return [i for i in self.issues if i.severity == SEVERITY_WARNING]

    def as_dict(self) -> dict:
        return {
            "item_id": self.item_id, "slot": self.slot, "is_weapon": self.is_weapon, "mode": self.mode,
            "family": self.family, "layer": self.layer, "mesh_ref": self.mesh_ref, "icon": self.icon,
            "clip_slots": self.clip_slots, "clip_hits": self.clip_hits,
            "clip_fallback_nodir": self.clip_fallback_nodir, "clip_missing": self.clip_missing,
            "validated": not self.errors,
            "errors": len(self.errors), "warnings": len(self.warnings),
        }


@dataclass
class EquipReport:
    items: list[ItemReport] = field(default_factory=list)
    extra_issues: list[EquipIssue] = field(default_factory=list)   # 不属于单件装备的问题（皮肤包、姿势集缺失）
    direction_count: int = DEFAULT_DIRECTION_COUNT
    anim_set_id: Optional[str] = None
    skins: list[dict] = field(default_factory=list)                # skin_pack.SkinReport.as_dict() 每包一项

    @property
    def issues(self) -> list[EquipIssue]:
        out: list[EquipIssue] = []
        for it in self.items:
            out.extend(it.issues)
        out.extend(self.extra_issues)
        return out

    @property
    def error_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == SEVERITY_ERROR)

    @property
    def warning_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == SEVERITY_WARNING)

    # -- validated 数据集判定接口（08 第 5 节："缺错误级项的装备资产包不能进入 validated 数据集"）--------
    def is_validated(self, item_id: str) -> bool:
        """该物品的装备资产包是否零错误；报告里没有的物品（非装备类）视为不受本规则约束，返回 True。"""
        for it in self.items:
            if it.item_id == item_id:
                return not it.errors
        return True

    def validated_item_ids(self) -> list[str]:
        return [it.item_id for it in self.items if not it.errors]

    def blocked_item_ids(self) -> list[str]:
        return [it.item_id for it in self.items if it.errors]

    def as_dict(self) -> dict:
        return {
            "tool": "import_assets.equip",
            "direction_count": self.direction_count,
            "anim_set": self.anim_set_id,
            "ok": self.error_count == 0,
            "counts": {"error": self.error_count, "warning": self.warning_count,
                       "items": len(self.items), "validated": len(self.validated_item_ids())},
            "items": [it.as_dict() for it in self.items],
            "skins": self.skins,
            "issues": [i.as_dict() for i in self.issues],
        }


def filter_validated(tables: dict[str, list[dict]], report: EquipReport) -> dict[str, list[dict]]:
    """从数据表里剔除被阻断装备的 ``item.template`` 与 ``display.equip_visual`` 行，返回新字典（不改入参）。"""
    blocked = set(report.blocked_item_ids())
    out = {name: list(rows) for name, rows in tables.items()}
    out[TABLE_ITEM] = [r for r in out.get(TABLE_ITEM, []) if r.get("id") not in blocked]
    out[TABLE_EQUIP_VISUAL] = [r for r in out.get(TABLE_EQUIP_VISUAL, []) if r.get("item_id") not in blocked]
    return out


# ---------------------------------------------------------------------------
# 校验
# ---------------------------------------------------------------------------

def _by_id(rows: list[dict]) -> dict[str, dict]:
    return {r["id"]: r for r in rows if isinstance(r.get("id"), str)}


def _bare(value: str) -> str:
    return value[len(DIRECTION_SLOT_ID_PREFIX):] if value.startswith(DIRECTION_SLOT_ID_PREFIX) else value


def _write_value(writes: object, field_name: str) -> Optional[object]:
    if not isinstance(writes, list):
        return None
    for w in writes:
        if isinstance(w, dict) and w.get("field") == field_name and w.get("op", "set") == "set":
            return w.get("value")
    return None


def _id_suffix(row_id: str, table: str) -> str:
    prefix = table + "."
    return row_id[len(prefix):] if row_id.startswith(prefix) else row_id


def _icon_path(icon_id: str) -> Optional[str]:
    parts = icon_id.split(".", 2)
    if len(parts) != 3:
        return None
    return f"icons/{parts[1]}/{parts[2].replace('.', '_')}.png"


def check_icon_image(path: Path) -> Optional[tuple[str, str]]:
    """返回 (检查名, 不合规原因)（None = 合规）：正方形、边长 2 的幂且在清单规定范围内（``equip_icon_size_invalid``）；
    非全透明、四边留透明边距、图标不含品质框（``equip_icon_alpha_invalid``）。"""
    from PIL import Image  # 延迟导入：只有真正读图时才需要 Pillow

    with Image.open(path) as im:
        im = im.convert("RGBA")
        w, h = im.size
        if w != h:
            return CHECK_ICON_SIZE_INVALID, f"不是正方形（{w}x{h}）"
        if not (ICON_MIN_SIDE <= w <= ICON_MAX_SIDE) or (w & (w - 1)) != 0:
            return CHECK_ICON_SIZE_INVALID, f"边长 {w} 不是 {ICON_MIN_SIDE}～{ICON_MAX_SIDE} 内 2 的幂"
        alpha = im.getchannel("A")
        bbox = alpha.getbbox()
        if bbox is None:
            return CHECK_ICON_ALPHA_INVALID, "图标全透明"
        if alpha.getextrema()[0] == 255:
            return CHECK_ICON_ALPHA_INVALID, "没有任何透明像素（不透明底图标放进槽位会露出方块底；图标须是带 alpha 通道的 PNG，主体外透明）"
        if bbox[0] <= 0 or bbox[1] <= 0 or bbox[2] >= w or bbox[3] >= h:
            return CHECK_ICON_ALPHA_INVALID, (f"主体贴边（不透明区域包围盒 {bbox[0]},{bbox[1]}～{bbox[2]},{bbox[3]}，图像 {w}x{h}）；"
                                              "契约要求四周留透明边距，且图标不含品质框")
    return None


def opaque_ratio(path: Path, alpha_cutoff: int) -> Optional[float]:
    """不透明像素（alpha >= ``alpha_cutoff``）占整张图的比例；读不出（或没有 Pillow）为 None（不做覆盖率核对）。"""
    try:
        from PIL import Image

        with Image.open(path) as im:
            alpha = im.convert("RGBA").getchannel("A")
            histogram = alpha.histogram()
            total = alpha.width * alpha.height
    except Exception:  # noqa: BLE001 - 图本身读不出由其它检查（尺寸/透明度）报
        return None
    if total <= 0:
        return None
    return sum(histogram[alpha_cutoff:]) / total


def coverage_problem(path: Path, spec: dict) -> Optional[str]:
    """覆盖率低于清单阈值时返回人读原因，否则 None。"""
    ratio = opaque_ratio(path, int(spec["alpha_cutoff"]))
    if ratio is None or ratio >= float(spec["min_opaque_ratio"]):
        return None
    return (f"不透明像素覆盖率 {ratio * 100:.2f}% 低于清单阈值 {float(spec['min_opaque_ratio']) * 100:.2f}%"
            f"（alpha >= {spec['alpha_cutoff']} 的像素占比；几乎看不见，请加粗/放大主体）")


def _image_size(path: Path) -> Optional[tuple[int, int]]:
    try:
        from PIL import Image

        with Image.open(path) as im:
            return im.size
    except Exception:  # noqa: BLE001 - 读不出就不做图集边界核对（文件存在性已由两级探测承担）
        return None


def _static_layer_problem(path: Path) -> Optional[str]:
    """静态层图的像素规则（契约清单 paperdoll.static_layer_alpha）：读得出、不是全透明、不是实心满铺（层图必须有透明像素，否则会盖住身体）。"""
    try:
        from PIL import Image

        with Image.open(path) as im:
            lo, hi = im.convert("RGBA").getchannel("A").getextrema()
    except ImportError:
        return None
    except Exception as exc:  # noqa: BLE001
        return f"读不出（{exc}）"
    if hi == 0:
        return "全透明"
    if lo == 255:
        return "没有任何透明像素（实心满铺会盖住身体）"
    return None


class EquipValidator:
    """对一组数据表 + 资产目录跑装备资产包校验。"""

    def __init__(self, tables: dict[str, list[dict]], assets_dir: Path, *, anim_set: Optional[str] = None,
                 direction_count: int = DEFAULT_DIRECTION_COUNT, body_set: Optional[str] = None) -> None:
        self.tables = tables
        self.body_set = body_set
        self.assets = Path(assets_dir)
        self.direction_count = direction_count
        self.dirs = directions.canonical_slot_names(direction_count)
        self.all_dirs = set(directions.all_slot_names(direction_count))
        self.slots = _by_id(tables.get("item.slot_definition", []))
        self.display_map = _by_id(tables.get("display.map", []))
        self.equip_visual = tables.get(TABLE_EQUIP_VISUAL, [])
        self.weapon_style = _by_id(tables.get("display.weapon_style", []))
        self.feel_weapon = _by_id(tables.get("feel.weapon", []))
        self.sfx = _by_id(tables.get("sfx.def", []))
        self.vfx = _by_id(tables.get("vfx.def", []))
        self.anim_set_row = self._pick_anim_set(anim_set)
        self.clips: dict[str, dict] = dict(self.anim_set_row.get("clips", {})) if self.anim_set_row else {}
        self.families = pose_families(self.clips)
        self._frames_cache: dict[str, Optional[dict]] = {}
        self.model_slots: set[str] = set()
        self.model_sockets: set[str] = set()
        for row in self.display_map.values():
            if row.get("kind") == "model":
                self.model_slots.update(s for s in row.get("slots", []) if isinstance(s, str))
                self.model_sockets.update(s for s in row.get("sockets", []) if isinstance(s, str))

    def _pick_anim_set(self, anim_set: Optional[str]) -> Optional[dict]:
        rows = _by_id(self.tables.get("display.anim_set", []))
        if anim_set:
            return rows.get(anim_set)
        best = None
        for row in rows.values():
            if isinstance(row.get("clips"), dict) and (best is None or len(row["clips"]) > len(best["clips"])):
                best = row
        return best

    # -- 入口 ---------------------------------------------------------------------------------
    def run(self) -> EquipReport:
        report = EquipReport(direction_count=self.direction_count,
                             anim_set_id=self.anim_set_row.get("id") if self.anim_set_row else None)
        if self.anim_set_row is None:
            report.extra_issues.append(EquipIssue(
                severity=SEVERITY_WARNING, table="display.anim_set", record_key="(anim_set)",
                check=CHECK_ANIM_SET_MISSING,
                message="数据根里没有可用的 display.anim_set 行，逐层剪辑覆盖无法按姿势键核对（只核对静态层图与表项）",
                fallback="仅静态层图"))
        for item in self.tables.get(TABLE_ITEM, []):
            slot = self.slots.get(item.get("slot", ""))
            if not slot or not slot.get("is_equipment"):
                continue
            report.items.append(self._check_item(item, slot))
        return report

    # -- 单件装备 -----------------------------------------------------------------------------
    def _issue(self, ir: ItemReport, severity: str, check: str, message: str, *, field_path: Optional[str] = None,
               path: Optional[str] = None, fallback: Optional[str] = None, table: str = TABLE_ITEM) -> None:
        ir.issues.append(EquipIssue(severity=severity, table=table, record_key=ir.item_id, check=check,
                                    message=message, field_path=field_path, path=path, fallback=fallback))

    def _check_item(self, item: dict, slot: dict) -> ItemReport:
        item_id = item.get("id", "?")
        dm = self.display_map.get(item.get("display_ref", ""), {})
        fw_id = item.get("feel_weapon_ref")
        ws_id = dm.get("weapon_style_ref")
        is_weapon = bool(slot.get("is_weapon") or fw_id or ws_id)
        ir = ItemReport(item_id=item_id, slot=item.get("slot", ""), is_weapon=is_weapon)

        self._check_icon(ir, item, dm)
        visuals = [v for v in self.equip_visual if v.get("item_id") == item_id]
        if not visuals and slot.get("has_appearance") is not False:
            self._issue(ir, SEVERITY_ERROR, CHECK_VISUAL_MISSING,
                        "装备没有 display.equip_visual 行（外观映射缺失）", table=TABLE_EQUIP_VISUAL)
        ws_row = self._check_weapon(ir, item, ws_id, fw_id) if is_weapon else None
        for v in visuals:
            self._check_visual(ir, v, ws_row)
        self._check_extras(ir, item, fw_id)
        return ir

    def _check_icon(self, ir: ItemReport, item: dict, dm: dict) -> None:
        icon_id = dm.get("icon_id")
        if not icon_id or not _icon_path(icon_id):
            self._issue(ir, SEVERITY_ERROR, CHECK_ICON_UNRESOLVED,
                        f"display_ref '{item.get('display_ref')}' 没有可解析的 icon_id（期望 icon.<类别>.<名>）",
                        field_path="display_ref")
            return
        rel = _icon_path(icon_id)
        ir.icon = rel
        file = self.assets / rel
        if not file.is_file():
            self._issue(ir, SEVERITY_ERROR, CHECK_ICON_FILE_MISSING, f"图标资源缺失: {file}",
                        field_path="display_ref", path=str(file))
            return
        problem = check_icon_image(file)
        if problem:
            self._issue(ir, SEVERITY_ERROR, problem[0], f"图标 {rel} 不合规：{problem[1]}",
                        field_path="display_ref", path=str(file))
            return
        low = coverage_problem(file, ICON_COVERAGE)
        if low:
            self._issue(ir, SEVERITY_WARNING, CHECK_OPAQUE_COVERAGE_LOW, f"图标 {rel} {low}",
                        field_path="display_ref", path=str(file), fallback="照常使用（图标在格子里几乎看不见）")

    def _check_weapon(self, ir: ItemReport, item: dict, ws_id: Optional[str], fw_id: Optional[str]) -> Optional[dict]:
        """返回该武器的 display.weapon_style 行（缺失为 None），供覆盖剪辑逐层核对。"""
        ws_ok = bool(ws_id) and ws_id in self.weapon_style
        fw_ok = bool(fw_id) and fw_id in self.feel_weapon
        if not ws_ok and not fw_ok:
            self._issue(ir, SEVERITY_ERROR, CHECK_WEAPON_PAIR_MISSING,
                        "武器两张表都缺：display.weapon_style（经 display.map.weapon_style_ref）与 feel.weapon"
                        "（经 item.template.feel_weapon_ref）均无对应行", field_path="feel_weapon_ref")
        elif ws_ok != fw_ok:
            missing = "feel.weapon" if ws_ok else "display.weapon_style"
            ref = fw_id if ws_ok else ws_id
            self._issue(ir, SEVERITY_ERROR, CHECK_WEAPON_PAIR_MISSING,
                        f"武器缺 {missing} 行（引用 '{ref}' 在该表无对应行；二者须同 id 分表）",
                        field_path="feel_weapon_ref" if ws_ok else "display_ref")
        elif _id_suffix(ws_id, "display.weapon_style") != _id_suffix(fw_id, "feel.weapon"):
            self._issue(ir, SEVERITY_ERROR, CHECK_WEAPON_ID_MISMATCH,
                        f"display.weapon_style '{ws_id}' 与 feel.weapon '{fw_id}' 不同 id（同 id 分表，05 第 2 节）",
                        field_path="feel_weapon_ref")
        if fw_ok:
            row = self.feel_weapon[fw_id]
            ir.family = row.get("family")
            if self.clips and ir.family and ir.family not in self.families:
                self._issue(ir, SEVERITY_WARNING, CHECK_FAMILY_WITHOUT_POSE_KEYS,
                            f"feel.weapon.family '{ir.family}' 在姿势集 '{self.anim_set_row.get('id')}' 里没有任何对应键"
                            f"（姿势集武器族: {', '.join(sorted(self.families)) or '无'}）",
                            field_path="family", fallback="基础键（idle/move.*/attack/...）", table="feel.weapon")
            self._check_sfx_material(ir, row)
            trail = _write_value(row.get("writes"), "trail_ref")
            if isinstance(trail, str) and trail not in self.vfx:
                self._issue(ir, SEVERITY_WARNING, CHECK_TRAIL_REF_MISSING,
                            f"trail_ref '{trail}' 在 vfx.def 无对应行", field_path="trail_ref",
                            fallback="无拖尾", table="feel.weapon")
        return self.weapon_style[ws_id] if ws_ok else None

    def _check_sfx_material(self, ir: ItemReport, row: dict) -> None:
        material = str(_write_value(row.get("writes"), "sfx_material") or DEFAULT_SFX_MATERIAL)
        missing = [layer for layer in SFX_MATERIAL_LAYERS if not self._has_material_row(layer, material)]
        if missing:
            self._issue(ir, SEVERITY_WARNING, CHECK_SFX_MATERIAL_MISSING,
                        f"sfx_material '{material}' 在 sfx.def 缺 {'/'.join(missing)} 层对应行"
                        f"（约定：id 为 sfx.<层>.{material} 或行内 material 字段等于 '{material}'，layer 为 swing/impact）",
                        field_path="sfx_material", fallback=DEFAULT_SFX_MATERIAL, table="feel.weapon")

    def _has_material_row(self, layer: str, material: str) -> bool:
        for row in self.sfx.values():
            if row.get("layer") != layer:
                continue
            if row.get("material") == material or str(row.get("id", "")).endswith("." + material):
                return True
        return False

    def _check_extras(self, ir: ItemReport, item: dict, fw_id: Optional[str]) -> None:
        sfx_ref = item.get("equip_sfx_ref")
        if sfx_ref and sfx_ref not in self.sfx:
            self._issue(ir, SEVERITY_WARNING, CHECK_SFX_REF_MISSING,
                        f"equip_sfx_ref '{sfx_ref}' 在 sfx.def 无对应行", field_path="equip_sfx_ref",
                        fallback="穿脱无专属音效")

    # -- 外观映射 -----------------------------------------------------------------------------
    def _check_visual(self, ir: ItemReport, v: dict, ws_row: Optional[dict]) -> None:
        mode = v.get("mode")
        mesh_ref = v.get("mesh_ref") or v.get("model_ref") or ""
        category = mesh_ref.partition(".")[0]
        preview = v.get("preview_direction")
        if preview and _bare(preview) not in self.all_dirs:
            self._issue(ir, SEVERITY_ERROR, CHECK_PREVIEW_DIRECTION_INVALID,
                        f"preview_direction '{preview}' 不在该游戏声明的 {self.direction_count} 方向档里",
                        field_path="preview_direction", table=TABLE_EQUIP_VISUAL)
        behind = v.get("behind_directions")
        if behind is not None:
            if not isinstance(behind, list):
                shown = "不是列表"
            else:
                bad = [b for b in behind
                       if not isinstance(b, str) or not b.startswith(DIRECTION_SLOT_ID_PREFIX) or _bare(b) not in self.all_dirs]
                shown = ("含未声明的方向档 " + ", ".join(repr(b) for b in bad)) if bad else ""
            if shown:
                self._issue(ir, SEVERITY_ERROR, CHECK_BEHIND_DIRECTION_INVALID,
                            f"behind_directions {shown}（须是 dir.<档位> 列表，档位在该游戏声明的 {self.direction_count} 方向档里）",
                            field_path="behind_directions", table=TABLE_EQUIP_VISUAL)
        if category == "paperdoll":
            ir.mode = "sprite"
            self._check_paperdoll(ir, v, mesh_ref, ws_row)
        elif category == "model" or mode == "socket_attach":
            ir.mode = "model"
            self._check_model(ir, v)
        else:
            ir.mode = ir.mode or "none"

    def _check_model(self, ir: ItemReport, v: dict) -> None:
        mode = v.get("mode")
        if mode == "socket_attach":
            sid = v.get("socket_id", "")
            if not str(sid).startswith("socket.") or sid not in self.model_sockets:
                self._issue(ir, SEVERITY_ERROR, CHECK_MODEL_SOCKET_UNKNOWN,
                            f"挂点 '{sid}' 不存在：须形如 socket.<名> 且在 model 型 display.map.sockets 里登记"
                            f"（已登记: {', '.join(sorted(self.model_sockets)) or '无'}）",
                            field_path="socket_id", table=TABLE_EQUIP_VISUAL)
        elif mode == "slot_mesh":
            sid = v.get("slot_id", "")
            if not str(sid).startswith("slot.") or sid not in self.model_slots:
                self._issue(ir, SEVERITY_ERROR, CHECK_MODEL_SLOT_UNKNOWN,
                            f"槽位 '{sid}' 不在标准骨骼槽位表：须形如 slot.<名> 且在 model 型 display.map.slots 里登记"
                            f"（已登记: {', '.join(sorted(self.model_slots)) or '无'}）",
                            field_path="slot_id", table=TABLE_EQUIP_VISUAL)
        else:
            self._issue(ir, SEVERITY_ERROR, CHECK_VISUAL_MODE_INVALID,
                        f"mode '{mode}' 不是 slot_mesh / socket_attach", field_path="mode", table=TABLE_EQUIP_VISUAL)

    def _check_paperdoll(self, ir: ItemReport, v: dict, mesh_ref: str, ws_row: Optional[dict]) -> None:
        slot_id = v.get("slot_id") or ""
        layer = slot_id.rpartition(".")[2]
        ir.layer, ir.mesh_ref = layer, mesh_ref
        if not layer:
            self._issue(ir, SEVERITY_ERROR, CHECK_LAYER_STATIC_MISSING,
                        "paperdoll 型外观缺 slot_id（层名由它推导）", field_path="slot_id", table=TABLE_EQUIP_VISUAL)
            return
        for d in self.dirs:
            rel = paperdoll_equip_layer_file(mesh_ref, d, layer)
            if not (self.assets / rel).is_file():
                self._issue(ir, SEVERITY_ERROR, CHECK_LAYER_STATIC_MISSING,
                            f"方向档 '{d}' 的静态层图缺失: {rel}", field_path="mesh_ref", path=str(self.assets / rel),
                            table=TABLE_EQUIP_VISUAL)
                continue
            reason = _static_layer_problem(self.assets / rel)
            if reason:
                self._issue(ir, SEVERITY_ERROR, CHECK_LAYER_IMAGE_INVALID, f"方向档 '{d}' 的静态层图 {rel} {reason}",
                            field_path="mesh_ref", path=str(self.assets / rel), table=TABLE_EQUIP_VISUAL)
                continue
            low = coverage_problem(self.assets / rel, LAYER_COVERAGE)
            if low:
                self._issue(ir, SEVERITY_WARNING, CHECK_OPAQUE_COVERAGE_LOW, f"方向档 '{d}' 的静态层图 {rel} {low}",
                            field_path="mesh_ref", path=str(self.assets / rel), fallback="照常使用（装备层几乎看不见）",
                            table=TABLE_EQUIP_VISUAL)
        self._check_layer_anchors(ir, mesh_ref, layer)
        self._check_layer_density(ir, mesh_ref)
        if not self.clips:
            return
        family = ir.family if ir.is_weapon else None
        weapon_layer = ir.is_weapon
        if weapon_layer and (not family or family not in self.families):
            needed: dict[str, str] = {}       # 族无姿势键：不逐键要求（判断记录 2）
        else:
            needed = needed_pose_keys(self.clips, family, weapon_layer)
        mesh_stem = strip_category_prefix(mesh_ref)
        for ref, tier in sorted(needed.items()):
            clip_stem = strip_category_prefix(ref)
            missing_dirs: list[str] = []
            frame_bad: dict[str, list[str]] = {}
            for d in self.dirs:
                ir.clip_slots += 1
                lvl = self._clip_level(mesh_stem, clip_stem, d, layer)
                if lvl == 1:
                    ir.clip_hits += 1
                elif lvl == 2:
                    ir.clip_fallback_nodir += 1
                else:
                    ir.clip_missing += 1
                    missing_dirs.append(d)
                    continue
                for check, why in self._clip_frame_problems(mesh_stem, clip_stem, d, layer, lvl):
                    frame_bad.setdefault(check, []).append(f"{d}（{why}）")
            for check, dirs in sorted(frame_bad.items()):
                self._issue(ir, SEVERITY_ERROR, check, f"键剪辑 '{ref}' 的层 '{layer}' 逐层剪辑与身体剪辑不对齐: {'; '.join(dirs)}",
                            field_path="mesh_ref", table=TABLE_EQUIP_VISUAL)
            if not missing_dirs:
                continue
            static_target = f"sprites/{mesh_stem}/<方向>/{layer}.png（静态层图）"
            where = f"{len(missing_dirs)}/{len(self.dirs)} 个方向档（{', '.join(missing_dirs)}）"
            if tier == TIER_REQUIRED:
                self._issue(ir, SEVERITY_ERROR, CHECK_LAYER_CLIP_MISSING,
                            f"必备键剪辑 '{ref}' 的层 '{layer}' 逐层剪辑缺 {where}",
                            field_path="mesh_ref", table=TABLE_EQUIP_VISUAL)
            else:
                self._issue(ir, SEVERITY_WARNING, CHECK_LAYER_CLIP_MISSING_RECOMMENDED,
                            f"推荐键剪辑 '{ref}' 的层 '{layer}' 逐层剪辑缺 {where}",
                            field_path="mesh_ref", fallback=static_target, table=TABLE_EQUIP_VISUAL)
        if weapon_layer:
            self._check_override_clips(ir, ws_row, mesh_stem, layer, set(needed))

    def _check_layer_anchors(self, ir: ItemReport, mesh_ref: str, layer: str) -> None:
        """层精灵集 anchors.json 里可选的 ``directions.<方向>.grip``：必须是 ``[x, y]`` 数字对，且落在该方向静态层图画布之内。
        没有该文件/没有 grip 声明 = 按画布居中叠放（不报任何东西，与此前逐位一致）。"""
        file = self.assets / "sprites" / strip_category_prefix(mesh_ref) / "anchors.json"
        if not file.is_file():
            return
        try:
            doc = json.loads(file.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return          # 解析失败由精灵导入的 check 子命令负责（pixels_per_unit 等），这里只核对 grip
        by_direction = doc.get("directions") if isinstance(doc, dict) else None
        if not isinstance(by_direction, dict):
            return
        for d, anchors in sorted(by_direction.items()):
            if not isinstance(anchors, dict) or ANCHOR_NAME_GRIP not in anchors:
                continue
            value = anchors[ANCHOR_NAME_GRIP]
            where = f"{file.relative_to(self.assets).as_posix()} 的 directions.{d}.{ANCHOR_NAME_GRIP}"
            if not (isinstance(value, list) and len(value) == 2
                    and all(isinstance(n, (int, float)) and not isinstance(n, bool) for n in value)):
                self._issue(ir, SEVERITY_ERROR, CHECK_ANCHOR_INVALID, f"{where} 不是 [x, y] 数字对（实际 {value!r}）",
                            field_path="mesh_ref", path=str(file), table=TABLE_EQUIP_VISUAL)
                continue
            image = self.assets / paperdoll_equip_layer_file(mesh_ref, d, layer)
            size = _image_size(image) if image.is_file() else None
            if size is None:
                continue
            x, y = value
            if not (0 <= x <= size[0] and 0 <= y <= size[1]):
                self._issue(ir, SEVERITY_ERROR, CHECK_ANCHOR_OUT_OF_BOUNDS,
                            f"{where} = [{x}, {y}] 落在层图 {image.relative_to(self.assets).as_posix()}（{size[0]}x{size[1]}）画布之外",
                            field_path="mesh_ref", path=str(file), table=TABLE_EQUIP_VISUAL)

    def _body_set(self) -> Optional[str]:
        """身体精灵集目录名：构造时显式给的 ``body_set``，否则 ``ui_layout_definition`` 行 ``fields.preview_body_set`` 声明的第一个（装备面板预览用的同一份约定）；都没有返回 None（不核对密度）。"""
        if self.body_set:
            return self.body_set
        for row in self.tables.get(TABLE_UI_LAYOUT, []):
            fields = row.get("fields")
            value = fields.get(FIELD_PREVIEW_BODY_SET) if isinstance(fields, dict) else None
            if isinstance(value, str) and value:
                return value
        return None

    @staticmethod
    def _declared_density(file: Path) -> Optional[float]:
        """精灵集 anchors.json 顶层的 ``pixels_per_unit``（正数）；文件缺失/解析失败/没声明/非法返回 None。"""
        try:
            doc = json.loads(file.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return None
        value = doc.get("pixels_per_unit") if isinstance(doc, dict) else None
        if isinstance(value, (int, float)) and not isinstance(value, bool) and value > 0:
            return float(value)
        return None

    def _check_layer_density(self, ir: ItemReport, mesh_ref: str) -> None:
        """警告 ``equip_layer_density_mismatch``：层精灵集与身体精灵集的像素密度（anchors.json 顶层 ``pixels_per_unit``）不一致时，运行期合成里
        层与身体按各自密度换算世界尺寸，装备相对身体画得偏大或偏小（预览区对两者用同一个缩放，所以预览看不出来）。只在层精灵集自己带 anchors.json
        （声明过锚点/密度，参与对齐）且身体精灵集声明了密度时核对；层集没声明密度也算不一致（运行期落在引擎缺省密度）。没有 anchors.json 的层集与没声明密度的身体集不报。"""
        body = self._body_set()
        mesh_set = strip_category_prefix(mesh_ref)
        if not body or body == mesh_set:
            return
        layer_file = self.assets / "sprites" / mesh_set / "anchors.json"
        body_file = self.assets / "sprites" / body / "anchors.json"
        if not layer_file.is_file() or not body_file.is_file():
            return
        body_density = self._declared_density(body_file)
        if body_density is None:
            return
        layer_density = self._declared_density(layer_file)
        if layer_density is not None and abs(layer_density - body_density) <= 1e-6:
            return
        shown = "没有声明 pixels_per_unit（运行期落在引擎缺省密度）" if layer_density is None else f"pixels_per_unit = {layer_density:g}"
        self._issue(ir, SEVERITY_WARNING, CHECK_LAYER_DENSITY_MISMATCH,
                    f"层精灵集 sprites/{mesh_set}/anchors.json {shown}，与身体精灵集 sprites/{body}/anchors.json 的 pixels_per_unit = {body_density:g} 不一致："
                    f"运行期合成里该装备相对身体会画得偏大或偏小（预览区看不出来）",
                    field_path="mesh_ref", path=str(layer_file), fallback="照常使用（运行期比例与身体不一致）", table=TABLE_EQUIP_VISUAL)

    def _clip_level(self, mesh_stem: str, clip_stem: str, direction: str, layer: str) -> int:
        """ADR-0100 两级探测：1 = 带方向命中，2 = 不带方向命中，0 = 都不命中。"""
        for lvl, name in ((1, f"{mesh_stem}__{clip_stem}__{direction}__{layer}"), (2, f"{mesh_stem}__{clip_stem}__{layer}")):
            d = self.assets / "sprite_anim" / name
            if (d / "atlas.png").is_file() and (d / "frames.json").is_file():
                return lvl
        return 0

    def _frames_doc(self, directory: Path) -> Optional[dict]:
        key = str(directory)
        if key not in self._frames_cache:
            try:
                doc = json.loads((directory / "frames.json").read_text(encoding="utf-8"))
                self._frames_cache[key] = doc if isinstance(doc, dict) and isinstance(doc.get("frames"), list) else None
            except (OSError, ValueError):
                self._frames_cache[key] = None
        return self._frames_cache[key]

    def _clip_frame_problems(self, mesh_stem: str, clip_stem: str, direction: str, layer: str, lvl: int) -> list[tuple[str, str]]:
        """逐层剪辑与同姿势同方向身体剪辑的帧对齐核对（契约清单 paperdoll.frame_rules）：帧数、帧宽高、帧矩形落在图集内。
        身体剪辑不存在（没有可比基准）时只核对图集边界。"""
        name = f"{mesh_stem}__{clip_stem}__{direction}__{layer}" if lvl == 1 else f"{mesh_stem}__{clip_stem}__{layer}"
        layer_dir = self.assets / "sprite_anim" / name
        doc = self._frames_doc(layer_dir)
        if doc is None:
            return []
        out: list[tuple[str, str]] = []
        body_doc = None
        for body_name in (f"{clip_stem}__{direction}__body", f"{clip_stem}__body"):
            body_dir = self.assets / "sprite_anim" / body_name
            if (body_dir / "frames.json").is_file():
                body_doc = self._frames_doc(body_dir)
                break
        if body_doc is not None:
            if len(doc["frames"]) != len(body_doc["frames"]):
                out.append((CHECK_LAYER_FRAME_COUNT_MISMATCH, f"层 {len(doc['frames'])} 帧，身体 {len(body_doc['frames'])} 帧"))
            lw, lh = doc.get("frame_w"), doc.get("frame_h")
            bw, bh = body_doc.get("frame_w"), body_doc.get("frame_h")
            if (lw, lh) != (bw, bh):
                out.append((CHECK_LAYER_FRAME_SIZE_MISMATCH, f"层帧 {lw}x{lh}，身体帧 {bw}x{bh}"))
        atlas = _image_size(layer_dir / "atlas.png")
        if atlas is not None:
            for f in doc["frames"]:
                if not isinstance(f, dict) or not all(isinstance(f.get(k), int) for k in ("x", "y", "w", "h"))                         or f["x"] < 0 or f["y"] < 0 or f["x"] + f["w"] > atlas[0] or f["y"] + f["h"] > atlas[1]:
                    out.append((CHECK_LAYER_ATLAS_MISMATCH, f"第 {f.get('index') if isinstance(f, dict) else '?'} 帧矩形超出图集 {atlas[0]}x{atlas[1]}"))
                    break
        return out

    def _check_override_clips(self, ir: ItemReport, ws: Optional[dict], mesh_stem: str, layer: str,
                              covered_refs: set[str]) -> None:
        if not ws:
            return
        overrides: list[tuple[str, str]] = []
        if ws.get("auto_attack_anim"):
            overrides.append(("auto_attack_anim", ws["auto_attack_anim"]))
        for skill, ref in (ws.get("cast_anim_override") or {}).items():
            overrides.append((f"cast_anim_override[{skill}]", ref))
        for fld, ref in overrides:
            if not isinstance(ref, str) or not ref.startswith("sprite_anim.") or ref in covered_refs:
                continue
            clip_stem = strip_category_prefix(ref)
            if all(self._clip_level(mesh_stem, clip_stem, d, layer) == 0 for d in self.dirs):
                self._issue(ir, SEVERITY_WARNING, CHECK_OVERRIDE_CLIP_LAYER_MISSING,
                            f"武器表现档案 {fld} 的覆盖剪辑 '{ref}' 没有层 '{layer}' 的逐层剪辑",
                            field_path=fld, fallback=f"整身剪辑 {ref}", table="display.weapon_style")


# ---------------------------------------------------------------------------
# 报告渲染
# ---------------------------------------------------------------------------

def render_text(report: EquipReport) -> str:
    lines = [
        "装备完整性报告（手感设计/08 第 5 节）",
        f"姿势集: {report.anim_set_id or '(无)'}；方向档数: {report.direction_count}；"
        f"装备 {len(report.items)} 件，通过 {len(report.validated_item_ids())} 件；"
        f"错误 {report.error_count}，警告 {report.warning_count}",
        "",
    ]
    for it in report.items:
        status = "通过" if not it.errors else "阻断"
        lines.append(f"[{status}] {it.item_id}  槽位={it.slot} 武器={'是' if it.is_weapon else '否'} 外观={it.mode or '-'} "
                     f"族={it.family or '-'} 层={it.layer or '-'}")
        lines.append(f"    图标={it.icon or '-'}；逐层剪辑 {it.clip_slots} 格：带方向 {it.clip_hits}，"
                     f"不带方向 {it.clip_fallback_nodir}，缺 {it.clip_missing}")
        for i in it.issues:
            lines.append(f"    {i.severity}: [{i.check}] {i.message}" + (f"（回落目标: {i.fallback}）" if i.fallback else ""))
    if report.extra_issues:
        lines.append("")
        lines.append("其它问题")
        for i in report.extra_issues:
            lines.append(f"    {i.severity}: [{i.check}] {i.message}" + (f"（回落目标: {i.fallback}）" if i.fallback else ""))
    for s in report.skins:
        lines += ["", f"皮肤包 {s.get('skin_ref')}：检查 {s.get('checked')} 项，缺失回落 {s.get('fallbacks')} 项，"
                      f"错误 {s.get('errors')}"]
        for fb in s.get("fallback_items", []):
            lines.append(f"    回落: {fb['item']} -> {fb['fallback']}")
    return "\n".join(lines) + "\n"


def write_report(report: EquipReport, out_dir: Path) -> tuple[Path, Path]:
    """写 ``equip_completeness.json`` 与 ``equip_completeness.txt``（只留本地，目录在 .gitignore 里）。"""
    out_dir.mkdir(parents=True, exist_ok=True)
    jp, tp = out_dir / "equip_completeness.json", out_dir / "equip_completeness.txt"
    jp.write_text(json.dumps(report.as_dict(), ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    tp.write_text(render_text(report), encoding="utf-8", newline="\n")
    return jp, tp
