"""界面皮肤包校验（手感设计/08 第 3/5 节、ADR-0123）。

皮肤包是资产目录 ``assets/<数据集>/ui/skin/<名>/``（``ui_layout_definition.skin_ref`` = ``skin.<名>``，缺省
框架占位皮肤 ``skin.default``）。包内文件布局（本文件是该布局的唯一权威，``toolchain/std_equip_set`` 生成占位
皮肤时引用同一份清单 :func:`expected_items`）：

========================  =====================================================  =========
项                        文件                                                   回落
========================  =====================================================  =========
槽位框（每个装备槽一张）  ``slot_frame/<槽位名>.png``（``item.slot.<名>``）      占位同名 → 占位 ``_default.png``
槽位框三态                ``slot_frame/_highlight.png`` / ``_disabled.png`` /    占位同名
                          ``_drag_hover.png``
品质框（每个品质一张）    ``quality_frame/<品质名>.png``（``item.quality.<名>``）占位同名 → 占位 ``_default.png``
拖拽态                    ``drag/ghost.png`` / ``target_ok.png`` /               占位同名
                          ``target_blocked.png``
提示框                    ``tooltip/background.png`` / ``divider.png`` /         占位同名
                          ``row.png``
纸娃娃预览区              ``paperdoll_preview/background.png``                   占位同名
主题                      ``theme.json``（``colors`` 对象 + ``font`` 字符串）    占位同名
面板布局                  ``ui_layout_definition`` 行：``character_stats`` /     框架占位布局
                          ``inventory`` / ``action_bar`` 三个面板
========================  =====================================================  =========

规则：皮肤包缺项 **不阻断**，记警告并写明回落目标；框架占位皮肤自身缺项没有可回落的下一级，记错误。

**判断记录**

1. 槽位名/品质名取 ``item.slot_definition`` / ``item.quality_definition`` 行 id 去掉 ``item.slot.`` /
   ``item.quality.`` 前缀（没有该前缀则取最后一个点分段），按 08 第 3 节"按槽位 id 命名"落到文件名。
2. 只核对文件存在与 ``theme.json`` 结构，不读图（与 import_assets.py check 其它域同一口径，秒级）。
3. 装备面板的"槽位/预览区"在 ``ui_layout_definition`` 里没有独立 panel 枚举值（枚举是十个固定面板），装备面板
   落在 ``character_stats``（角色面板）里，故面板布局核对这三个。
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

from .check_cmd import SEVERITY_ERROR, SEVERITY_WARNING
from .equip_pack import EquipIssue

SKIN_REF_PREFIX = "skin."
PLACEHOLDER_SKIN = "skin.default"
SKIN_ROOT = "ui/skin"

CHECK_SKIN_PACK_MISSING = "equip_skin_pack_missing"
CHECK_SKIN_ITEM_MISSING = "equip_skin_item_missing"
CHECK_SKIN_PLACEHOLDER_MISSING = "equip_skin_placeholder_missing"
CHECK_SKIN_PANEL_LAYOUT_MISSING = "equip_skin_panel_layout_missing"
CHECK_SKIN_THEME_INVALID = "equip_skin_theme_invalid"
CHECK_SKIN_REF_INVALID = "equip_skin_ref_invalid"

CHECK_NAMES: tuple[str, ...] = (
    CHECK_SKIN_PACK_MISSING, CHECK_SKIN_ITEM_MISSING, CHECK_SKIN_PLACEHOLDER_MISSING,
    CHECK_SKIN_PANEL_LAYOUT_MISSING, CHECK_SKIN_THEME_INVALID, CHECK_SKIN_REF_INVALID,
)

SLOT_STATES = ("highlight", "disabled", "drag_hover")
DRAG_ITEMS = ("ghost", "target_ok", "target_blocked")
TOOLTIP_ITEMS = ("background", "divider", "row")
REQUIRED_PANELS = ("character_stats", "inventory", "action_bar")
THEME_FILE = "theme.json"


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
    """皮肤包期望文件清单：``[(包内相对路径, 说明, 兜底相对路径或 None)]``。兜底是占位皮肤里的 ``_default``。"""
    items: list[tuple[str, str, Optional[str]]] = []
    for s in slots:
        items.append((f"slot_frame/{s}.png", f"槽位框 {s}", "slot_frame/_default.png"))
    for state in SLOT_STATES:
        items.append((f"slot_frame/_{state}.png", f"槽位框三态 {state}", None))
    for q in qualities:
        items.append((f"quality_frame/{q}.png", f"品质框 {q}", "quality_frame/_default.png"))
    for d in DRAG_ITEMS:
        items.append((f"drag/{d}.png", f"拖拽态 {d}", None))
    for t in TOOLTIP_ITEMS:
        items.append((f"tooltip/{t}.png", f"提示框 {t}", None))
    items.append(("paperdoll_preview/background.png", "纸娃娃预览区背景", None))
    items.append((THEME_FILE, "主题（配色与字体）", None))
    return items


def default_fallback_files() -> list[str]:
    return ["slot_frame/_default.png", "quality_frame/_default.png"]


@dataclass
class SkinReport:
    skin_ref: str
    checked: int = 0
    fallbacks: list[dict] = field(default_factory=list)   # [{"item", "fallback"}]
    errors: int = 0

    def as_dict(self) -> dict:
        return {"skin_ref": self.skin_ref, "checked": self.checked, "fallbacks": len(self.fallbacks),
                "fallback_items": self.fallbacks, "errors": self.errors}


def _theme_problem(path: Path) -> Optional[str]:
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        return f"无法解析为 JSON（{exc}）"
    if not isinstance(doc, dict) or not isinstance(doc.get("colors"), dict) or not doc["colors"]:
        return "缺 colors 对象（配色）"
    if not isinstance(doc.get("font"), str) or not doc["font"]:
        return "缺 font 字符串（字体）"
    return None


def collect_skin_refs(tables: dict[str, list[dict]]) -> list[str]:
    refs = []
    for row in tables.get("ui_layout_definition", []):
        ref = row.get("skin_ref")
        if isinstance(ref, str) and ref not in refs:
            refs.append(ref)
    return refs


def check_skins(tables: dict[str, list[dict]], assets_dir: Path, *, skin_refs: Optional[list[str]] = None,
                ) -> tuple[list[EquipIssue], list[SkinReport]]:
    """校验占位皮肤 + 数据里引用的（或显式给出的）每个皮肤包。返回 (问题, 每包报告)。"""
    assets_dir = Path(assets_dir)
    issues: list[EquipIssue] = []
    reports: list[SkinReport] = []
    slots, qualities = slot_names(tables), quality_names(tables)
    items = expected_items(slots, qualities)
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
    for rel, label, _fb in items:
        ph.checked += 1
        if not (placeholder_dir / rel).is_file():
            ph.errors += 1
            issues.append(EquipIssue(
                severity=SEVERITY_ERROR, table="ui.skin", record_key=PLACEHOLDER_SKIN,
                check=CHECK_SKIN_PLACEHOLDER_MISSING, message=f"框架占位皮肤缺 {label}: {rel}",
                path=str(placeholder_dir / rel)))
    for rel in default_fallback_files():
        ph.checked += 1
        if not (placeholder_dir / rel).is_file():
            ph.errors += 1
            issues.append(EquipIssue(
                severity=SEVERITY_ERROR, table="ui.skin", record_key=PLACEHOLDER_SKIN,
                check=CHECK_SKIN_PLACEHOLDER_MISSING, message=f"框架占位皮肤缺兜底图 {rel}",
                path=str(placeholder_dir / rel)))
    theme = placeholder_dir / THEME_FILE
    if theme.is_file():
        problem = _theme_problem(theme)
        if problem:
            ph.errors += 1
            issues.append(EquipIssue(severity=SEVERITY_ERROR, table="ui.skin", record_key=PLACEHOLDER_SKIN,
                                     check=CHECK_SKIN_THEME_INVALID, message=f"框架占位皮肤 {THEME_FILE} {problem}",
                                     path=str(theme)))
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
        for rel, label, default_rel in items:
            rep.checked += 1
            if (pack_dir / rel).is_file():
                continue
            target = rel if (placeholder_dir / rel).is_file() else (default_rel or rel)
            fb = f"{PLACEHOLDER_SKIN}:{target}"
            rep.fallbacks.append({"item": rel, "fallback": fb})
            issues.append(EquipIssue(
                severity=SEVERITY_WARNING, table="ui.skin", record_key=ref, check=CHECK_SKIN_ITEM_MISSING,
                message=f"皮肤包缺 {label}: {rel}", fallback=fb, path=str(pack_dir / rel)))
        theme = pack_dir / THEME_FILE
        if theme.is_file():
            problem = _theme_problem(theme)
            if problem:
                rep.fallbacks.append({"item": THEME_FILE, "fallback": f"{PLACEHOLDER_SKIN}:{THEME_FILE}"})
                issues.append(EquipIssue(
                    severity=SEVERITY_WARNING, table="ui.skin", record_key=ref, check=CHECK_SKIN_THEME_INVALID,
                    message=f"皮肤包 {THEME_FILE} {problem}", fallback=f"{PLACEHOLDER_SKIN}:{THEME_FILE}",
                    path=str(theme)))
    return issues, reports
