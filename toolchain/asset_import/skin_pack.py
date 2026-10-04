"""界面皮肤包校验（手感设计/08 第 3/5 节、ADR-0123、ADR-0149）。

皮肤包是资产目录 ``assets/<数据集>/ui/skin/<名>/``（``ui_layout_definition.skin_ref`` = ``skin.<名>``，缺省
框架占位皮肤 ``skin.default``）。包内有哪些文件、每个文件的必备/可选、尺寸/比例、九宫格边框、状态变体、透明度、主题令牌，
全部在机器可读的清单 :file:`skin_manifest.json`（读取与展开见 :mod:`skin_manifest`），本模块只是"按清单逐条核对并给具名诊断"，
``toolchain/std_equip_set`` 生成占位皮肤、人读清单（``import_assets.py skin-checklist``）与引擎侧完整性用例读的是同一份清单。

规则：皮肤包缺项 **不阻断**，记警告并写明回落目标；框架占位皮肤自身缺项没有可回落的下一级，记错误；**已存在文件的内容缺陷**
（读不出、尺寸/比例不合规、九宫格边框不合法、透明度不合规、同组尺寸不一致）一律记错误——文件会被运行期原样使用，不会回落。

具名诊断（稳定契约，``--json`` 消费方按 check 过滤；每条都有一个反例用例）：

========================================  =====================================================================
``equip_skin_pack_missing``               皮肤包目录不存在（警告，回落占位皮肤）
``equip_skin_item_missing``               必备文件缺失（警告，写回落目标）
``equip_skin_state_missing``              必备状态变体缺失，或给了依赖项却缺它依赖的状态（如有 hover 无 normal）（警告）
``equip_skin_placeholder_missing``        框架占位皮肤缺必备/兜底文件（错误）
``equip_skin_panel_layout_missing``       没有三个必需面板的 ui_layout_definition 行（警告）
``equip_skin_ref_invalid``                skin_ref 不是 ``skin.<名>``（错误）
``equip_skin_theme_invalid``              theme.json 读不出/缺 colors/缺 font/数值令牌不是正整数（警告；占位皮肤为错误）
``equip_skin_theme_token_missing``        theme.json 缺清单要求的颜色键（警告，回落框架默认色）
``equip_skin_theme_color_invalid``        颜色键的值不是 ``#rrggbb`` / ``#rrggbbaa``（警告，回落框架默认色）
``equip_skin_image_unreadable``           PNG 读不出来（错误）
``equip_skin_size_invalid``               尺寸/比例不合清单规则（错误）
``equip_skin_size_group_mismatch``        同尺寸组（格子类图）里出现不同边长（错误）
``equip_skin_nineslice_invalid``          九宫格边框 ×2 ≥ 图像边长，没有可拉伸的中心区（错误）
``equip_skin_alpha_invalid``              全透明，或要求有透明像素/透明中心而没有（错误）
========================================  =====================================================================

**判断记录**

1. 槽位名/品质名取 ``item.slot_definition`` / ``item.quality_definition`` 行 id 去掉 ``item.slot.`` /
   ``item.quality.`` 前缀（没有该前缀则取最后一个点分段），按 08 第 3 节"按槽位 id 命名"落到文件名。
2. 内容检查读图（Pillow，皮肤包的图只有几十张小图，秒级）；Pillow 缺失时只做存在性与主题检查。
3. 装备面板（槽位网格 + 纸娃娃预览区）在 ``ui_layout_definition`` 里有独立的 ``equipment`` panel 行（ADR-0149，
   fields：``anchor`` / ``columns`` / ``cell_size`` / ``preview_scale`` / ``preview_body_set`` / ``preview_direction``）；
   该行是可选的（没有时取运行期缺省布局），所以必需的面板布局仍只核对 ``character_stats`` / ``inventory`` / ``action_bar``。
4. 运行期（适配器的 ``UiSkinPack``）按同一份清单与同一条回落链读皮肤包：缺项落到占位皮肤同名 → 占位 ``_default.png`` →
   程序生成的纯色框，本模块的"缺项 + 回落目标"就是运行期回落记录的静态预报。可选元素缺失只记进报告的 ``optional_absent``，不当问题。
5. 九宫格边框像素取 theme.json 里清单声明的令牌（缺省取清单默认值）；运行期用同一个值给精灵声明 border。
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

from . import skin_manifest as M
from .check_cmd import SEVERITY_ERROR, SEVERITY_WARNING
from .equip_pack import EquipIssue

SKIN_REF_PREFIX = "skin."
PLACEHOLDER_SKIN = "skin.default"
SKIN_ROOT = "ui/skin"

CHECK_SKIN_PACK_MISSING = "equip_skin_pack_missing"
CHECK_SKIN_ITEM_MISSING = "equip_skin_item_missing"
CHECK_SKIN_STATE_MISSING = "equip_skin_state_missing"
CHECK_SKIN_PLACEHOLDER_MISSING = "equip_skin_placeholder_missing"
CHECK_SKIN_PANEL_LAYOUT_MISSING = "equip_skin_panel_layout_missing"
CHECK_SKIN_THEME_INVALID = "equip_skin_theme_invalid"
CHECK_SKIN_THEME_TOKEN_MISSING = "equip_skin_theme_token_missing"
CHECK_SKIN_THEME_COLOR_INVALID = "equip_skin_theme_color_invalid"
CHECK_SKIN_REF_INVALID = "equip_skin_ref_invalid"
CHECK_SKIN_IMAGE_UNREADABLE = "equip_skin_image_unreadable"
CHECK_SKIN_SIZE_INVALID = "equip_skin_size_invalid"
CHECK_SKIN_SIZE_GROUP_MISMATCH = "equip_skin_size_group_mismatch"
CHECK_SKIN_NINESLICE_INVALID = "equip_skin_nineslice_invalid"
CHECK_SKIN_ALPHA_INVALID = "equip_skin_alpha_invalid"

CHECK_NAMES: tuple[str, ...] = (
    CHECK_SKIN_PACK_MISSING, CHECK_SKIN_ITEM_MISSING, CHECK_SKIN_STATE_MISSING, CHECK_SKIN_PLACEHOLDER_MISSING,
    CHECK_SKIN_PANEL_LAYOUT_MISSING, CHECK_SKIN_THEME_INVALID, CHECK_SKIN_THEME_TOKEN_MISSING,
    CHECK_SKIN_THEME_COLOR_INVALID, CHECK_SKIN_REF_INVALID, CHECK_SKIN_IMAGE_UNREADABLE, CHECK_SKIN_SIZE_INVALID,
    CHECK_SKIN_SIZE_GROUP_MISMATCH, CHECK_SKIN_NINESLICE_INVALID, CHECK_SKIN_ALPHA_INVALID,
)

REQUIRED_PANELS = ("character_stats", "inventory", "action_bar")
THEME_FILE = "theme.json"
_COLOR_RE = re.compile(r"^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")


def skin_dir_name(skin_ref: str) -> Optional[str]:
    """``skin.<名>`` → ``<名>``（名可含点号，换成下划线）；格式不对返回 None。"""
    if not skin_ref.startswith(SKIN_REF_PREFIX) or len(skin_ref) == len(SKIN_REF_PREFIX):
        return None
    return skin_ref[len(SKIN_REF_PREFIX):].replace(".", "_")


def _name_after(row_id: str, prefix: str) -> str:
    return row_id[len(prefix):] if row_id.startswith(prefix) else row_id.rpartition(".")[2]


def slot_names(tables: dict[str, list[dict]]) -> list[str]:
    return sorted(_name_after(r["id"], "item.slot.") for r in tables.get("item.slot_definition", [])
                  if isinstance(r.get("id"), str) and r.get("is_equipment"))


def quality_names(tables: dict[str, list[dict]]) -> list[str]:
    return sorted(_name_after(r["id"], "item.quality.") for r in tables.get("item.quality_definition", [])
                  if isinstance(r.get("id"), str))


def expected_items(slots: list[str], qualities: list[str]) -> list[tuple[str, str, Optional[str]]]:
    """皮肤包期望文件清单：``[(包内相对路径, 说明, 兜底相对路径或 None)]``，取自清单里"必备"的元素。

    不含 ``placeholder_only``（见 :func:`default_fallback_files`）与可选元素（见 :func:`optional_items`）。"""
    return [(x.path, x.label, x.element.fallback.replace("{slot}", x.name or "") if x.element.fallback else None)
            for x in M.expand(slots, qualities) if x.requirement == M.REQ_REQUIRED]


def optional_items(slots: list[str], qualities: list[str]) -> list[str]:
    return [x.path for x in M.expand(slots, qualities) if x.requirement == M.REQ_OPTIONAL]


def default_fallback_files() -> list[str]:
    """占位皮肤必须有、自有皮肤包可不带的兜底图（清单里 ``placeholder_only`` 的元素）。"""
    return [x.path for x in M.expand([], []) if x.requirement == M.REQ_PLACEHOLDER_ONLY]


@dataclass
class SkinReport:
    skin_ref: str
    checked: int = 0
    fallbacks: list[dict] = field(default_factory=list)   # [{"item", "fallback"}]
    optional_absent: list[str] = field(default_factory=list)
    errors: int = 0

    def as_dict(self) -> dict:
        return {"skin_ref": self.skin_ref, "checked": self.checked, "fallbacks": len(self.fallbacks),
                "fallback_items": self.fallbacks, "optional_absent": len(self.optional_absent), "errors": self.errors}


# ---------------------------------------------------------------------------
# 主题
# ---------------------------------------------------------------------------

def _load_theme(path: Path) -> tuple[Optional[dict], Optional[str]]:
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        return None, f"无法解析为 JSON（{exc}）"
    if not isinstance(doc, dict):
        return None, "缺 colors 对象（配色）"
    return doc, None


def _theme_problem(path: Path) -> Optional[str]:
    doc, problem = _load_theme(path)
    if problem:
        return problem
    assert doc is not None
    if not isinstance(doc.get("colors"), dict) or not doc["colors"]:
        return "缺 colors 对象（配色）"
    if not isinstance(doc.get("font"), str) or not doc["font"]:
        return "缺 font 字符串（字体）"
    for spec in M.token_spec()["numbers"]:
        value = doc.get(spec["key"])
        if value is not None and (isinstance(value, bool) or not isinstance(value, int) or value < spec.get("min", 1)):
            return f"{spec['key']} 必须是正整数（{spec['use']}）"
    return None


def _theme_token_issues(doc: dict, ref: str, path: Path, severity: str) -> list[EquipIssue]:
    out: list[EquipIssue] = []
    colors = doc.get("colors") or {}
    for spec in M.token_spec()["colors"]:
        key = spec["key"]
        if key not in colors:
            if spec["requirement"] == M.REQ_REQUIRED:
                out.append(EquipIssue(
                    severity=severity, table="ui.skin", record_key=ref, check=CHECK_SKIN_THEME_TOKEN_MISSING,
                    message=f"{THEME_FILE} 缺颜色令牌 colors.{key}（{spec['use']}）", fallback="框架默认色",
                    field_path=f"colors.{key}", path=str(path)))
        elif not isinstance(colors[key], str) or not _COLOR_RE.match(colors[key]):
            out.append(EquipIssue(
                severity=severity, table="ui.skin", record_key=ref, check=CHECK_SKIN_THEME_COLOR_INVALID,
                message=f"{THEME_FILE} 的 colors.{key} = {colors[key]!r} 不是 #rrggbb / #rrggbbaa",
                fallback="框架默认色", field_path=f"colors.{key}", path=str(path)))
    return out


# ---------------------------------------------------------------------------
# 图像内容
# ---------------------------------------------------------------------------

def _read_png(path: Path):
    """返回 (宽, 高, alpha 通道 PIL 图) 或 (None, None, 原因)；Pillow 缺失返回 (None, None, None)。"""
    try:
        from PIL import Image
    except ImportError:
        return None, None, None
    try:
        with Image.open(path) as im:
            im.load()
            rgba = im.convert("RGBA")
            return rgba.size[0], rgba.size[1], rgba.getchannel("A")
    except Exception as exc:  # noqa: BLE001 - 任何读图失败都归"读不出"
        return None, None, f"读不出（{exc}）"


def _image_issues(x: M.ExpandedElement, path: Path, theme: Optional[dict], ref: str, size_of: dict) -> list[EquipIssue]:
    e = x.element
    w, h, alpha = _read_png(path)
    if w is None:
        if alpha is None:       # 没有 Pillow
            return []
        return [EquipIssue(severity=SEVERITY_ERROR, table="ui.skin", record_key=ref, check=CHECK_SKIN_IMAGE_UNREADABLE,
                           message=f"{x.label}: {x.path} {alpha}", path=str(path))]
    size_of[x.path] = (w, h)
    out: list[EquipIssue] = []

    def issue(check: str, message: str) -> None:
        out.append(EquipIssue(severity=SEVERITY_ERROR, table="ui.skin", record_key=ref, check=check,
                              message=f"{x.label}: {x.path} {message}", path=str(path)))

    size = e.size or {}
    if size.get("aspect") == "1:1" and w != h:
        issue(CHECK_SKIN_SIZE_INVALID, f"不是正方形（{w}x{h}）")
    elif size and not (size["min"] <= min(w, h) and max(w, h) <= size["max"]):
        issue(CHECK_SKIN_SIZE_INVALID, f"尺寸 {w}x{h} 不在每边 {size['min']}～{size['max']} px 内")

    if e.nine_slice:
        border = M.nine_slice_border(e, theme)
        if 2 * border >= min(w, h):
            issue(CHECK_SKIN_NINESLICE_INVALID,
                  f"九宫格边框 {border} px（theme.{e.nine_slice['border_token']}）×2 不小于图像最小边 {min(w, h)} px，没有可拉伸的中心区")

    lo, hi = alpha.getextrema()
    if hi == 0:
        issue(CHECK_SKIN_ALPHA_INVALID, "全透明")
    elif e.alpha in (M.ALPHA_HAS_TRANSPARENCY, M.ALPHA_TRANSPARENT_CENTER) and lo == 255:
        issue(CHECK_SKIN_ALPHA_INVALID, "没有任何透明像素（框不能是实心方块）")
    elif e.alpha == M.ALPHA_TRANSPARENT_CENTER:
        box = (w // 4, h // 4, w - w // 4, h - h // 4)
        centre = alpha.crop(box)
        top = centre.getextrema()[1]
        if top != 0:
            # 出图模型的"透明"处常留 alpha 1~9 的底噪（肉眼不可见）：报出最大 alpha 与位置，便于直接定位到像素
            hit = centre.point(lambda v: 255 if v == top else 0).getbbox()
            where = f"({box[0] + hit[0]}, {box[1] + hit[1]})" if hit else "?"
            issue(CHECK_SKIN_ALPHA_INVALID,
                  f"中心区域不是全透明（最大 alpha={top}，首个位置 {where}；品质框叠在图标上，不能盖住图标）")
    return out


def _group_issues(items: list[M.ExpandedElement], size_of: dict[str, tuple[int, int]], pack_dir: Path, ref: str) -> list[EquipIssue]:
    groups: dict[str, list[M.ExpandedElement]] = {}
    for x in items:
        grp = (x.element.size or {}).get("group")
        if grp and x.path in size_of:
            groups.setdefault(grp, []).append(x)
    out: list[EquipIssue] = []
    for grp, members in sorted(groups.items()):
        counts: dict[tuple[int, int], int] = {}
        for x in members:
            counts[size_of[x.path]] = counts.get(size_of[x.path], 0) + 1
        if len(counts) <= 1:
            continue
        # 基准尺寸：出现次数最多者，并列取较大边长（出图工具一般整组用同一档，少数张不一致才是缺陷）
        base = sorted(counts.items(), key=lambda kv: (-kv[1], -kv[0][0], -kv[0][1]))[0][0]
        for x in members:
            if size_of[x.path] != base:
                w, h = size_of[x.path]
                out.append(EquipIssue(
                    severity=SEVERITY_ERROR, table="ui.skin", record_key=ref, check=CHECK_SKIN_SIZE_GROUP_MISMATCH,
                    message=f"{x.label}: {x.path} 尺寸 {w}x{h} 与同组 {grp} 的基准 {base[0]}x{base[1]} 不一致",
                    path=str(pack_dir / x.path)))
    return out


# ---------------------------------------------------------------------------
# 整包校验
# ---------------------------------------------------------------------------

def _validate_present(items: list[M.ExpandedElement], pack_dir: Path, ref: str, theme: Optional[dict]) -> tuple[list[EquipIssue], int]:
    issues: list[EquipIssue] = []
    size_of: dict[str, tuple[int, int]] = {}
    present = 0
    for x in items:
        p = pack_dir / x.path
        if x.element.kind != "image" or not p.is_file():
            continue
        present += 1
        issues.extend(_image_issues(x, p, theme, ref, size_of))
    issues.extend(_group_issues(items, size_of, pack_dir, ref))
    return issues, present


def check_skins(tables: dict[str, list[dict]], assets_dir: Path, *, skin_refs: Optional[list[str]] = None,
                ) -> tuple[list[EquipIssue], list[SkinReport]]:
    """校验占位皮肤 + 数据里引用的（或显式给出的）每个皮肤包。返回 (问题, 每包报告)。"""
    assets_dir = Path(assets_dir)
    issues: list[EquipIssue] = []
    reports: list[SkinReport] = []
    items = M.expand(slot_names(tables), quality_names(tables))
    placeholder_dir = assets_dir / SKIN_ROOT / (skin_dir_name(PLACEHOLDER_SKIN) or "default")

    # 面板布局行（每个数据集只有一份，缺项记在占位包名下）
    layout_panels = {r.get("panel") for r in tables.get("ui_layout_definition", [])}
    for panel in REQUIRED_PANELS:
        if panel not in layout_panels:
            issues.append(EquipIssue(
                severity=SEVERITY_WARNING, table="ui_layout_definition", record_key=f"panel:{panel}",
                check=CHECK_SKIN_PANEL_LAYOUT_MISSING,
                message=f"没有 panel = {panel} 的 ui_layout_definition 行（08 第 3 节面板布局）",
                fallback="框架占位布局"))

    # 占位皮肤：缺项没有下一级可回落，错误级
    ph = SkinReport(PLACEHOLDER_SKIN)
    ph_theme: Optional[dict] = None
    theme_path = placeholder_dir / THEME_FILE
    if theme_path.is_file():
        ph_theme, _ = _load_theme(theme_path)
    for x in items:
        if x.requirement == M.REQ_OPTIONAL:
            continue
        ph.checked += 1
        if not (placeholder_dir / x.path).is_file():
            ph.errors += 1
            what = "兜底图" if x.requirement == M.REQ_PLACEHOLDER_ONLY else x.label
            issues.append(EquipIssue(
                severity=SEVERITY_ERROR, table="ui.skin", record_key=PLACEHOLDER_SKIN,
                check=CHECK_SKIN_PLACEHOLDER_MISSING,
                message=f"框架占位皮肤缺{'兜底图 ' + x.path if x.requirement == M.REQ_PLACEHOLDER_ONLY else ' ' + what + ': ' + x.path}",
                path=str(placeholder_dir / x.path)))
    if theme_path.is_file():
        problem = _theme_problem(theme_path)
        if problem:
            ph.errors += 1
            issues.append(EquipIssue(severity=SEVERITY_ERROR, table="ui.skin", record_key=PLACEHOLDER_SKIN,
                                     check=CHECK_SKIN_THEME_INVALID, message=f"框架占位皮肤 {THEME_FILE} {problem}",
                                     path=str(theme_path)))
        elif ph_theme is not None:
            tok = _theme_token_issues(ph_theme, PLACEHOLDER_SKIN, theme_path, SEVERITY_ERROR)
            ph.errors += len(tok)
            issues.extend(tok)
    content, _ = _validate_present(items, placeholder_dir, PLACEHOLDER_SKIN, ph_theme)
    ph.errors += len(content)
    issues.extend(content)
    reports.append(ph)

    refs = list(skin_refs) if skin_refs is not None else collect_skin_refs(tables)
    for ref in refs:
        if ref == PLACEHOLDER_SKIN:
            continue
        name = skin_dir_name(ref)
        rep = SkinReport(ref)
        reports.append(rep)
        if name is None:
            issues.append(EquipIssue(
                severity=SEVERITY_ERROR, table="ui_layout_definition", record_key=ref,
                check=CHECK_SKIN_REF_INVALID, message=f"skin_ref '{ref}' 格式不是 skin.<名>", field_path="skin_ref"))
            rep.errors += 1
            continue
        pack_dir = assets_dir / SKIN_ROOT / name
        if not pack_dir.is_dir():
            issues.append(EquipIssue(
                severity=SEVERITY_WARNING, table="ui.skin", record_key=ref, check=CHECK_SKIN_PACK_MISSING,
                message=f"皮肤包目录不存在: {pack_dir}", fallback=PLACEHOLDER_SKIN, path=str(pack_dir)))
        by_id = {x.element.id: x for x in items}
        for x in items:
            if x.requirement == M.REQ_PLACEHOLDER_ONLY:
                continue
            exists = (pack_dir / x.path).is_file()
            if x.requirement == M.REQ_OPTIONAL:
                if exists:
                    rep.checked += 1
                    needs = x.element.needs
                    if needs and not (pack_dir / by_id[needs].path).is_file():
                        issues.append(EquipIssue(
                            severity=SEVERITY_WARNING, table="ui.skin", record_key=ref, check=CHECK_SKIN_STATE_MISSING,
                            message=f"皮肤包提供了 {x.path}，却缺它依赖的 {by_id[needs].path}（{by_id[needs].label}）",
                            fallback=f"{PLACEHOLDER_SKIN}:{by_id[needs].path}" if (placeholder_dir / by_id[needs].path).is_file() else "框架默认",
                            path=str(pack_dir / by_id[needs].path)))
                else:
                    rep.optional_absent.append(x.path)
                continue
            rep.checked += 1
            if exists:
                continue
            fallback_rel = x.element.fallback.replace("{slot}", x.name or "") if x.element.fallback else None
            target = x.path if (placeholder_dir / x.path).is_file() else (fallback_rel or x.path)
            fb = f"{PLACEHOLDER_SKIN}:{target}"
            rep.fallbacks.append({"item": x.path, "fallback": fb})
            if x.element.state_of:
                issues.append(EquipIssue(
                    severity=SEVERITY_WARNING, table="ui.skin", record_key=ref, check=CHECK_SKIN_STATE_MISSING,
                    message=f"皮肤包缺 {x.element.state_of} 的必备状态变体 {x.element.state}: {x.path}（{x.label}）",
                    fallback=fb, path=str(pack_dir / x.path)))
            else:
                issues.append(EquipIssue(
                    severity=SEVERITY_WARNING, table="ui.skin", record_key=ref, check=CHECK_SKIN_ITEM_MISSING,
                    message=f"皮肤包缺 {x.label}: {x.path}", fallback=fb, path=str(pack_dir / x.path)))
        theme = pack_dir / THEME_FILE
        theme_doc: Optional[dict] = None
        if theme.is_file():
            problem = _theme_problem(theme)
            if problem:
                rep.fallbacks.append({"item": THEME_FILE, "fallback": f"{PLACEHOLDER_SKIN}:{THEME_FILE}"})
                issues.append(EquipIssue(
                    severity=SEVERITY_WARNING, table="ui.skin", record_key=ref, check=CHECK_SKIN_THEME_INVALID,
                    message=f"皮肤包 {THEME_FILE} {problem}", fallback=f"{PLACEHOLDER_SKIN}:{THEME_FILE}",
                    path=str(theme)))
            else:
                theme_doc, _ = _load_theme(theme)
                issues.extend(_theme_token_issues(theme_doc or {}, ref, theme, SEVERITY_WARNING))
        content, _ = _validate_present(items, pack_dir, ref, theme_doc)
        rep.errors += len(content)
        issues.extend(content)
    return issues, reports


def collect_skin_refs(tables: dict[str, list[dict]]) -> list[str]:
    refs = []
    for row in tables.get("ui_layout_definition", []):
        ref = row.get("skin_ref")
        if isinstance(ref, str) and ref not in refs:
            refs.append(ref)
    return refs
