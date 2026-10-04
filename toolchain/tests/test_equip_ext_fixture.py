"""副手与挂点装备夹具（``lab/fixtures/data/equip_ext``，ADR-0153）的导入校验覆盖。

夹具是实验室脚本 ``equip_ext_cycle`` 的独立数据根：副手槽位 + 占位副手匕首（paperdoll 层 ``hand_off``，副手手感写入叠加）、
饰品槽位 + 占位提灯（``socket_attach`` 挂点外观，挂点由 model 型 ``display.map`` 行声明）。占位装备集（``data/_equip``）不动。

资产不入库：本用例在临时目录里用占位装备集生成器（``std_equip_set``）按同一套规格生成副手匕首的图标、静态层图与逐层剪辑，
提灯只需要图标（挂点外观没有 2D 图层），再用装备资产包校验器（``equip_pack``）核对。运行期一侧（实验室基线里的 ``item_facts``）与
静态报告逐项对账，与 ``test_equip_runtime_zero_diff.py`` 对占位装备集做的是同一件事。

运行：``python -m pytest toolchain/tests/test_equip_ext_fixture.py -q``
"""

from __future__ import annotations

import copy
import json
import shutil
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from asset_import import equip_pack as E  # noqa: E402
from std_equip_set import config as SC  # noqa: E402
from std_equip_set import data as SD  # noqa: E402
from std_equip_set import icons as SI  # noqa: E402
from std_equip_set.build import generate_item  # noqa: E402

REPO = TOOLCHAIN_DIR.parent
ASSETS = REPO / "assets" / "_placeholder"
EXT_ROOT = REPO / "lab" / "fixtures" / "data" / "equip_ext"
DATA_ROOTS = [REPO / "data" / "_framework", REPO / "data" / "_feel", REPO / "data" / "_equip", EXT_ROOT]
BASELINE = REPO / "lab" / "fixtures" / "baselines" / "equip_ext_cycle.baseline.json"

OFFHAND = "item.std_offhand_dagger"
LANTERN = "item.std_lantern"
SWORD = "item.std_sword_1h"

#: 夹具里副手匕首的几何/层规格：与 lab/fixtures/data/equip_ext 的数据行一一对应（名字、槽位、层、族、手感行 id 后缀）。
OFFHAND_DEF = SC.ItemDef(
    "std_offhand_dagger", "dagger", "std_off_hand", "hand_off", "1h", "offhand_dagger", "std_common", "metal_light",
    "占位副手匕首")

MODE_MAP = {"slot_mesh": "sprite", "socket_attach": "model"}


@pytest.fixture(scope="module")
def tables():
    return E.load_tables(DATA_ROOTS)


@pytest.fixture(scope="module")
def assets(tmp_path_factory) -> Path:
    """临时资产目录：副手匕首整包（图标 + 静态层图 + 逐层剪辑）、提灯图标、单手剑（取占位装备集已有资产，供运行期对账）。"""
    dst = tmp_path_factory.mktemp("equip_ext_assets")
    generate_item(OFFHAND_DEF, dst, 20, log=lambda *_: None)
    lantern_icon = dst / "icons" / "item" / "std_lantern.png"
    lantern_icon.parent.mkdir(parents=True, exist_ok=True)
    SI.draw_icon("staff").save(lantern_icon, format="PNG", optimize=True)
    shutil.copy2(ASSETS / "icons" / "item" / "std_sword_1h.png", dst / "icons" / "item" / "std_sword_1h.png")
    shutil.copytree(ASSETS / "sprites" / "item_std_sword_1h", dst / "sprites" / "item_std_sword_1h")
    (dst / "sprite_anim").mkdir(exist_ok=True)
    for d in (ASSETS / "sprite_anim").glob("item_std_sword_1h__*"):
        shutil.copytree(d, dst / "sprite_anim" / d.name)
    return dst


def _only(tables, *item_ids):
    t = copy.deepcopy(tables)
    t["item.template"] = [r for r in t["item.template"] if r["id"] in item_ids]
    return t


def _run(tables, assets, **kw):
    return E.EquipValidator(tables, assets, **kw).run()


def _checks(report, severity=None):
    return sorted(i.check for i in report.issues if severity is None or i.severity == severity)


def _items(report):
    return {it.item_id: it for it in report.items}


# ---------------------------------------------------------------------------
# 夹具通过导入校验
# ---------------------------------------------------------------------------

def test_ext_items_pass_import_validation_with_zero_errors_and_warnings(tables, assets):
    report = _run(_only(tables, OFFHAND, LANTERN), assets)
    assert [i.render_text() for i in report.issues] == []
    items = _items(report)
    assert set(items) == {OFFHAND, LANTERN}

    off, lantern = items[OFFHAND], items[LANTERN]
    assert off.slot == "item.slot." + OFFHAND_DEF.slot and off.is_weapon and off.family == "1h"
    assert off.mode == "sprite" and off.layer == "hand_off" and off.mesh_ref == OFFHAND_DEF.mesh_ref
    assert off.clip_slots == off.clip_hits > 0 and off.clip_missing == 0
    # 挂点装备：不是武器、没有武器族、外观是 model 型（没有 2D 图层）。
    assert lantern.slot == "item.slot.std_trinket" and not lantern.is_weapon and lantern.family is None
    assert lantern.mode == "model" and lantern.layer is None


def test_ext_slots_follow_weapon_slot_naming_rule_and_equip_root_is_untouched(tables):
    # 副手槽位是第二个武器槽（按槽位 id 序数排序，同生产装配的主手/副手缺省规则）。
    weapon_slots = sorted(r["id"] for r in tables["item.slot_definition"] if r.get("is_weapon"))
    assert weapon_slots == ["item.slot.std_main_hand", "item.slot.std_off_hand"]
    # 占位装备集生成器的数据表（= 入库的 data/_equip）里没有夹具的任何一行：夹具是独立数据根，data/_equip 不动。
    generated = json.dumps(SD.tables(), ensure_ascii=False)
    for needle in ("std_off_hand", "std_trinket", "std_offhand_dagger", "std_lantern", "socket_attach"):
        assert needle not in generated, needle


def test_ext_offhand_weapon_row_only_writes_offhand_stackable_fields(tables):
    row = next(r for r in tables["feel.weapon"] if r["id"] == "feel.weapon.offhand_dagger")
    assert row.get("offhand_writes"), "夹具要演示副手叠加：必须有 offhand_writes"
    # 副手只允许写 offhand_stackable 字段的 add/multiply（FeelFieldRegistry 里标 offhand_stackable 的两个字段）。
    assert {w["field"] for w in row["offhand_writes"]} <= {"impact_vfx_scale", "sfx_sweetener_tier"}
    assert {w["op"] for w in row["offhand_writes"]} <= {"add", "multiply"}


# ---------------------------------------------------------------------------
# 复现 / 不变量：挂点与副手层的校验确实在把关（夹具被破坏时必须红）
# ---------------------------------------------------------------------------

def test_socket_attach_item_needs_a_model_row_that_declares_the_socket(tables, assets):
    broken = _only(tables, LANTERN)
    broken["display.map"] = [r for r in broken["display.map"] if r.get("kind") != "model"]
    assert _checks(_run(broken, assets)) == [E.CHECK_MODEL_SOCKET_UNKNOWN]

    # 挂点名与 model 行登记的逐字一致：改一个字符就不认。
    renamed = _only(tables, LANTERN)
    renamed["display.equip_visual"] = [dict(r, socket_id="socket.off_hand2") if r["item_id"] == LANTERN else r
                                       for r in renamed["display.equip_visual"]]
    assert _checks(_run(renamed, assets)) == [E.CHECK_MODEL_SOCKET_UNKNOWN]


def test_socket_declared_by_the_model_row_is_exactly_what_the_item_attaches_to(tables):
    models = [r for r in tables["display.map"] if r.get("kind") == "model" and "socket.off_hand" in r.get("sockets", [])]
    assert models, "夹具里必须有声明了 socket.off_hand 的 model 型 display.map 行"
    visual = next(r for r in tables["display.equip_visual"] if r["item_id"] == LANTERN)
    assert visual["mode"] == "socket_attach" and visual["socket_id"] in models[0]["sockets"]


def test_offhand_paperdoll_layer_files_are_required(tables, assets, tmp_path):
    work = tmp_path / "assets"
    shutil.copytree(assets, work)
    (work / "sprites" / "item_std_offhand_dagger" / "side_r" / "hand_off.png").unlink()
    report = _run(_only(tables, OFFHAND), work)
    assert _checks(report, "error") == [E.CHECK_LAYER_STATIC_MISSING]


# ---------------------------------------------------------------------------
# 运行期（实验室基线）与静态报告对账
# ---------------------------------------------------------------------------

def _cells():
    return json.loads(BASELINE.read_text(encoding="utf-8"))["cells"]


def _facts(cell):
    result = {}
    for fact in cell["groups"]["equip"]["item_facts"].split(";"):
        parts = fact.split("|")
        assert len(parts) == 9, fact
        result.setdefault(parts[0], parts)
    return result


def test_runtime_item_facts_equal_static_report(tables, assets):
    report = _run(_only(tables, SWORD, OFFHAND, LANTERN), assets)
    assert [i.render_text() for i in report.issues] == []
    static = _items(report)
    facts = _facts(_cells()["2d_targeted"])
    assert set(facts) == set(static), "运行期穿过的物品集合与静态报告的物品集合不一致"
    for item_id, parts in facts.items():
        _, slot, is_weapon, family, mode, layer, mesh_ref, icon, _style = parts
        it = static[item_id]
        assert slot == it.slot, item_id
        assert (is_weapon == "1") == it.is_weapon, item_id
        assert (None if family == "-" else family) == it.family, item_id
        assert MODE_MAP[mode] == it.mode, item_id
        if it.mode == "sprite":
            assert layer.removeprefix("slot.") == it.layer, item_id
            assert mesh_ref == it.mesh_ref, item_id
        else:
            assert layer.startswith("socket."), item_id        # 挂点外观：运行期这一列是挂点 id
        assert E._icon_path(icon) == it.icon, item_id


def test_runtime_zero_metrics_and_offhand_group_identical_on_all_six_cells():
    cells = _cells()
    assert len(cells) == 6
    zero = ("steps_failed", "layer_fallbacks", "unrefreshed_steps", "stale_version_steps", "pose_family_mismatch",
            "icon_missing", "visual_missing", "weapon_style_missing", "weapon_style_mismatch", "ui_mismatch",
            "attack_mismatches", "reference_mismatches")
    first = cells["2d_targeted"]["groups"]
    for name, cell in cells.items():
        for metric in zero:
            assert cell["groups"]["equip"][metric] == 0, f"{name}.{metric}"
        assert cell["groups"]["equip_offhand"]["panel_count_mismatch"] == 0, name
        assert cell["groups"]["equip_offhand"] == first["equip_offhand"], name
        assert cell["groups"]["equip"]["item_facts"] == first["equip"]["item_facts"], name
