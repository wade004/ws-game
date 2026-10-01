"""换装运行期检查与导入校验静态报告的零差异（手感设计/06 第 3.6 节 equip_cycle、08 第 1 节）。

运行期一侧：实验室 ``equip_cycle`` 脚本入库基线里每次穿上的事实（``item_facts``：物品|槽位|是否武器|武器族|外观模式|
外观槽位|资源集|图标|武器表现档案）与换装链各段的"应恒为 0"度量；静态一侧：``equip_pack.EquipValidator`` 对
``data/_equip`` 占位装备集出的 ``ItemReport``（槽位、是否武器、武器族、层、资源集、图标路径）。两侧对同一批物品必须一致，
任何一侧改了而另一侧没跟上，本用例红。不起子进程、不读 git，只读入库的两份文件。

运行：``python -m pytest toolchain/tests/test_equip_runtime_zero_diff.py -q``
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from asset_import import equip_cmd, equip_pack as E  # noqa: E402

REPO = TOOLCHAIN_DIR.parent
BASELINE = REPO / "lab" / "fixtures" / "baselines" / "equip_cycle.baseline.json"
DATA_ROOTS = [REPO / "data" / "_framework", REPO / "data" / "_feel", REPO / "data" / "_equip"]
ASSETS = REPO / "assets" / "_placeholder"

ZERO_METRICS = (
    "steps_failed", "layer_fallbacks", "unrefreshed_steps", "stale_version_steps", "pose_family_mismatch",
    "icon_missing", "visual_missing", "weapon_style_missing", "weapon_style_mismatch", "ui_mismatch",
    "attack_mismatches", "reference_mismatches",
)

# 运行期外观模式（EquipVisualDef）与静态报告模式（sprite/model）的对应。
MODE_MAP = {"slot_mesh": "sprite"}


def _baseline_cells() -> dict:
    return json.loads(BASELINE.read_text(encoding="utf-8"))["cells"]


def _facts(cell: dict) -> dict[str, list[str]]:
    result: dict[str, list[str]] = {}
    for fact in cell["groups"]["equip"]["item_facts"].split(";"):
        parts = fact.split("|")
        assert len(parts) == 9, fact
        result.setdefault(parts[0], parts)
    return result


def test_runtime_zero_metrics_are_zero_on_every_cell():
    cells = _baseline_cells()
    assert len(cells) == 6
    for name, cell in cells.items():
        equip = cell["groups"]["equip"]
        for metric in ZERO_METRICS:
            assert equip[metric] == 0, f"{name}.{metric}"


def test_runtime_item_facts_equal_static_report():
    report = equip_cmd.run_equip(DATA_ROOTS, ASSETS)
    assert [i.render_text() for i in report.issues] == []
    static = {it.item_id: it for it in report.items}

    cells = _baseline_cells()
    facts = _facts(cells["2d_targeted"])
    assert set(facts) == set(static), "运行期穿过的物品集合与静态报告的物品集合不一致"
    for item_id, parts in facts.items():
        _, slot, is_weapon, family, mode, layer, mesh_ref, icon, _style = parts
        it = static[item_id]
        assert slot == it.slot, item_id
        assert (is_weapon == "1") == it.is_weapon, item_id
        assert (None if family == "-" else family) == it.family, item_id
        assert MODE_MAP.get(mode, mode) == it.mode, item_id
        assert layer.removeprefix("slot.") == it.layer, item_id
        assert mesh_ref == it.mesh_ref, item_id
        assert E._icon_path(icon) == it.icon, item_id


def test_runtime_item_facts_identical_on_all_six_cells():
    cells = _baseline_cells()
    first = cells["2d_targeted"]["groups"]["equip"]["item_facts"]
    for name, cell in cells.items():
        assert cell["groups"]["equip"]["item_facts"] == first, name


def test_runtime_families_follow_the_worn_weapon():
    """每步之后的武器族序列由"当前主手武器的静态族"算出，不写死：穿胸甲不改族，卸下主手回到空族。"""
    report = equip_cmd.run_equip(DATA_ROOTS, ASSETS)
    family_of = {it.item_id: it.family for it in report.items}
    equip = _baseline_cells()["2d_targeted"]["groups"]["equip"]
    families = equip["families"].split(";")
    main_refs = equip["main_refs"].split(";")
    assert len(families) == len(main_refs) == equip["steps"]
    # 武器手感引用 -> 物品：feel.weapon.<x> 对应 item.std_<x>（占位装备集命名约定），族取静态报告。
    for ref, family in zip(main_refs, families):
        if ref == "-":
            assert family == "-"
            continue
        item_id = "item.std_" + ref.removeprefix("feel.weapon.")
        assert family == family_of[item_id], (ref, family)
