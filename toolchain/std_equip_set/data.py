"""占位装备集的数据表（``data/_equip``）：全部由 :mod:`config` 的物品清单推出，与资产生成器同源。

表：item.slot_definition / item.quality_definition / item.template / display.map / display.equip_visual /
display.weapon_style / feel.weapon（只放框架 data/_feel 里没有的三把）/ feel.calibration / sfx.def /
ui_layout_definition / l10n.locale / l10n.text。字段顺序按 schema 声明顺序（format_data.py --schema-order）。
"""

from __future__ import annotations

import json
from pathlib import Path

from . import config as C

L10N_LOCALE = "l10n.locale.zh_cn"
MIRROR_PAIRS = [
    {"direction_slot": "dir.front_side_l", "mirror_of": "dir.front_side_r", "flip_x": True},
    {"direction_slot": "dir.side_l", "mirror_of": "dir.side_r", "flip_x": True},
    {"direction_slot": "dir.back_side_l", "mirror_of": "dir.back_side_r", "flip_x": True},
]

#: 框架 data/_feel/feel/feel.weapon.json 已有这两行；其余三把由本数据根补充（同表跨根追加）。
FRAMEWORK_FEEL_WEAPONS = ("sword_1h", "greatsword")


def _write(field: str, value, op: str = "set") -> dict:
    return {"field": field, "op": op, "value": value}


def _weapon_writes(impact_class, atk_hs, tgt_hs, kb, stagger, kill_hs, cancel, combo, stop, move_ratio, camera,
                   trail, afterimage, vfx_scale, tiers, material) -> list[dict]:
    swing, whiff, impact, sweet = tiers
    return [
        _write("impact_class", impact_class), _write("attacker_hitstop_ms", atk_hs),
        _write("target_hitstop_ms", tgt_hs), _write("knockback_distance", kb), _write("stagger_power", stagger),
        _write("kill_hitstop_scale", kill_hs), _write("cancel_window_scale", cancel),
        _write("combo_window_scale", combo), _write("stop_distance", stop),
        _write("action_move_speed_ratio", move_ratio), _write("action_turn_lock", True),
        _write("camera_impulse_gain", camera), _write("trail_enabled", trail),
        _write("afterimage_enabled", afterimage), _write("impact_vfx_scale", vfx_scale),
        _write("sfx_swing_tier", swing), _write("sfx_whiff_tier", whiff), _write("sfx_impact_tier", impact),
        _write("sfx_sweetener_tier", sweet), _write("sfx_material", material),
    ]


def feel_weapon_rows() -> list[dict]:
    spec = {
        "dagger": ("匕首（轻近战，快速）占位试调起点，不是已验证值。", "1h",
                   _weapon_writes("light", 25, 35, 0.1, 0.5, 1.4, 1.1, 1.2, 0.4, 0.45, 0.005, True, False, 0.8,
                                  (1, 1, 1, 1), "metal_light"),
                   {"startup_ms": 70, "active_ms": 60, "recovery_ms": 120, "lunge_body_heights": 0.15,
                    "dodge_cancel_open_progress": 0.55, "inflicted_hit_stun_ms": 110}),
        "bow": ("弓（投射，双手）占位试调起点，不是已验证值。", "2h",
                _weapon_writes("light", 20, 30, 0.12, 1, 1.5, 1.0, 1.0, 0.0, 0.6, 0.006, False, False, 0.9,
                               (1, 1, 2, 1), "wood"),
                {"startup_ms": 160, "active_ms": 40, "recovery_ms": 180, "lunge_body_heights": 0,
                 "dodge_cancel_open_progress": 0.7, "inflicted_hit_stun_ms": 130}),
        "staff": ("法杖（法器，双手）占位试调起点，不是已验证值。", "2h",
                  _weapon_writes("medium", 40, 50, 0.2, 2, 1.6, 0.9, 0.9, 0.5, 0.2, 0.01, False, False, 1.2,
                                 (2, 1, 3, 2), "wood"),
                  {"startup_ms": 140, "active_ms": 90, "recovery_ms": 240, "lunge_body_heights": 0.1,
                   "dodge_cancel_open_progress": 0.7, "inflicted_hit_stun_ms": 180}),
    }
    rows = []
    for wid, (desc, family, writes, timeline) in spec.items():
        rows.append({"id": f"feel.weapon.{wid}", "description": desc, "maturity": "experimental",
                     "profile_version": 1, "family": family, "writes": writes, "timeline_reference": timeline})
    return rows


def tables() -> dict[str, dict]:
    """``{相对 data/_equip 的路径: 表文档}``。"""
    items = C.ITEMS
    t: dict[str, dict] = {}

    t["item/item.slot_definition.json"] = _doc("item.slot_definition", [
        {"id": f"item.slot.{n}", "name_key": f"l10n.item.slot.{n}.name", "sort_weight": w, "is_weapon": wp,
         "is_equipment": True, "budget_coefficient": 1.0, "price_coefficient": 1.0, "has_armor": armor}
        for n, _zh, wp, w, armor in C.SLOTS])
    t["item/item.quality_definition.json"] = _doc("item.quality_definition", [
        {"id": f"item.quality.{n}", "name_key": f"l10n.item.quality.{n}.name", "sort_weight": w,
         "budget_multiplier": bm, "affix_count": ac, "grant_budget_share": 0.0, "price_multiplier": bm}
        for n, _zh, w, bm, ac in C.QUALITIES])

    # 物品模板校验要求默认预算曲线存在（item_budget_exceeded 规则）；占位装备不带 stats，曲线只为让规则可判定。
    t["item/item.budget_curve.json"] = {"table": "item.budget_curve", "schema_version": 2, "rows": [{
        "id": "item.budget.default",
        "entries": [{"x": 1, "y": 20}, {"x": 10, "y": 200}, {"x": 20, "y": 400}, {"x": 30, "y": 600},
                    {"x": 40, "y": 800}, {"x": 60, "y": 1200}],
        "exponent": 1.5}]}

    tmpl = []
    for it in items:
        row = {"id": it.item_id, "slot": f"item.slot.{it.slot}", "quality": f"item.quality.{it.quality}",
               "item_level": it.item_level}
        if it.is_weapon:
            row["weapon_profile"] = {"damage_min": 4 + it.item_level, "damage_max": 6 + it.item_level * 2,
                                     "speed": 1.5}
        row["display_ref"] = it.display_map_id
        row["stack_size"] = 1
        row["name_key"] = f"l10n.item.{it.name}.name"
        if it.is_weapon:
            row["feel_weapon_ref"] = f"feel.weapon.{it.weapon_id}"
        row["equip_sfx_ref"] = C.EQUIP_SFX_ID
        tmpl.append(row)
    t["item/item.template.json"] = _doc("item.template", tmpl)

    dmap = []
    for it in items:
        row = {"id": it.display_map_id, "category": "item", "logical_id": it.item_id, "kind": "sprite",
               "icon_id": it.icon_id}
        if it.is_weapon:
            row["weapon_style_ref"] = f"display.weapon_style.{it.weapon_id}"
        row["sprite_set_id"] = f"sprite.item.{it.name}"
        row["direction_count"] = C.DIRECTION_COUNT
        row["mirror_pairs"] = MIRROR_PAIRS
        dmap.append(row)
    t["display/display.map.json"] = _doc("display.map", dmap)

    ev = []
    for it in items:
        row = {"id": f"display.equip_visual.{it.name}", "item_id": it.item_id, "mode": "slot_mesh",
               "slot_id": f"slot.{it.layer}", "mesh_ref": it.mesh_ref}
        if it.preview_direction:
            row["preview_direction"] = f"dir.{it.preview_direction}"
        ev.append(row)
    t["display/display.equip_visual.json"] = _doc("display.equip_visual", ev)

    t["display/display.weapon_style.json"] = _doc("display.weapon_style", [
        {"id": f"display.weapon_style.{it.weapon_id}", "auto_attack_anim": f"sprite_anim.std_dummy_attack_{it.family}"}
        for it in items if it.is_weapon])

    t["feel/feel.weapon.json"] = _doc("feel.weapon", feel_weapon_rows())
    t["feel/feel.calibration.json"] = _doc("feel.calibration", [{
        "id": "feel.calibration.std_equip", "base_preset": "feel.preset.arpg_responsive", "reference_height": 2.0,
        "base_speed": 4.0, "animation_fps": 20.0, "reference_camera_height": 10.0, "reference_zoom": 1.0,
        "pixels_per_unit": 32.0, "marker_tolerance_ms": 50.0}])

    sfx = []
    for material, swing, impact in C.MATERIALS:
        sfx.append({"id": f"sfx.swing.{material}", "layer": "swing", "priority": 3, "resource_ref": swing})
        sfx.append({"id": f"sfx.impact.{material}", "layer": "impact", "priority": 5, "resource_ref": impact})
    sfx.append({"id": C.EQUIP_SFX_ID, "layer": "ui", "priority": 1, "resource_ref": C.EQUIP_SFX_RESOURCE})
    t["sfx/sfx.def.json"] = _doc("sfx.def", sfx)

    t["ui/ui_layout_definition.json"] = _doc("ui_layout_definition", [
        {"id": "ui_layout_definition.std_character_stats", "panel": "character_stats", "fields": {"anchor": "top_left"},
         "skin_ref": C.SKIN_REF},
        {"id": "ui_layout_definition.std_inventory", "panel": "inventory", "fields": {"anchor": "top_right"},
         "skin_ref": C.SKIN_REF},
        {"id": "ui_layout_definition.std_action_bar", "panel": "action_bar", "slots": 8,
         "fields": {"anchor": "bottom_center"}, "skin_ref": C.SKIN_REF},
    ])

    t["l10n/l10n.locale.json"] = _doc("l10n.locale", [{"id": L10N_LOCALE, "fallback": None, "is_default": True}])
    texts = [("l10n.power.health.name", "生命值")]   # 框架 arch.power_type 行的显示名文本键
    texts += [(f"l10n.item.slot.{n}.name", zh) for n, zh, *_ in C.SLOTS]
    texts += [(f"l10n.item.quality.{n}.name", zh) for n, zh, *_ in C.QUALITIES]
    texts += [(f"l10n.item.{it.name}.name", it.zh_name) for it in items]
    t["l10n/l10n.text.json"] = _doc("l10n.text", [{"key": k, "locale": L10N_LOCALE, "text": v} for k, v in texts])
    return t


def _doc(table: str, rows: list[dict]) -> dict:
    return {"table": table, "schema_version": 1, "rows": rows}


def render(doc: dict) -> str:
    return json.dumps(doc, indent=2, ensure_ascii=False) + "\n"


def write_all(data_out: Path) -> list[Path]:
    written = []
    for rel, doc in tables().items():
        path = data_out / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(render(doc), encoding="utf-8", newline="\n")
        written.append(path)
    return written
