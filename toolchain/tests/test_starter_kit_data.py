"""内容起步包数据根 ``data/_starter_kit`` 的不变量（P4，ADR-0162）。

起步包是"出厂的可选通用内容"：游戏无关（不出现样板游戏的剧情/角色/资源名）、默认不被加载、自己是一份零错误零警告的数据、
框架自带界面用到的文案键它都提供（中英文）。期望值由数据与源码里的真实引用算出，不写死裸数。

运行：``python -m pytest toolchain/tests/test_starter_kit_data.py -q``
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent
KIT = REPO_ROOT / "data" / "_starter_kit"

ZH = "l10n.locale.zh_cn"
EN = "l10n.locale.en"

# 样板游戏与实验室数据的专名：起步包是游戏无关的，不得出现（项目硬性规则：任何文件不出现具体游戏代号）。
FORBIDDEN_NAMES = ("arpg", "ashen", "bone_lord", "bone king", "bone dungeon", "topdown", "samples", "ws-game-samples")


def _tables() -> dict[str, dict]:
    out = {}
    for path in sorted(KIT.rglob("*.json")):
        doc = json.loads(path.read_text(encoding="utf-8"))
        out[doc["table"]] = doc
    return out


def test_kit_validates_with_zero_errors_and_warnings_on_the_framework_root() -> None:
    proc = subprocess.run(
        [sys.executable, str(TOOLCHAIN_DIR / "validate_data.py"), "--strict", "--framework-root", "data/_framework", "--data-root", "data/_starter_kit"],
        cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600)
    assert proc.returncode == 0, proc.stdout[-3000:] + proc.stderr[-1000:]
    match = re.search(r"errors\s+(\d+),\s*warnings\s+(\d+)", proc.stdout)
    assert match, proc.stdout[-1000:]
    assert (match.group(1), match.group(2)) == ("0", "0")


def test_kit_is_game_agnostic() -> None:
    offenders = []
    for path in sorted(KIT.rglob("*")):
        if not path.is_file():
            continue
        text = path.read_text(encoding="utf-8").lower()
        for name in FORBIDDEN_NAMES:
            if name in text:
                offenders.append(f"{path.relative_to(REPO_ROOT)}: {name}")
    assert not offenders, offenders


def test_every_text_key_has_both_locales_and_no_duplicates() -> None:
    rows = _tables()["l10n.text"]["rows"]
    pairs = [(r["key"], r["locale"]) for r in rows]
    assert len(pairs) == len(set(pairs)), "同一个键同一语言出现了两次"
    by_key: dict[str, set[str]] = {}
    for key, locale in pairs:
        by_key.setdefault(key, set()).add(locale)
    incomplete = sorted(k for k, locales in by_key.items() if locales != {ZH, EN})
    assert not incomplete, f"缺中文或英文：{incomplete[:10]}"
    assert all(r["text"].strip() for r in rows)


def test_kit_provides_every_ui_text_key_the_framework_ui_references() -> None:
    """复现红：框架面板/提示框用 ``T(key, 中文默认词)`` 取词，起步包不给键就只能靠硬编码默认词（P3 备忘 1 的回头路）。"""
    kit_keys = {r["key"] for r in _tables()["l10n.text"]["rows"]}
    sources = [REPO_ROOT / "presentation" / "ui" / "core" / "ViewModels" / "ItemTooltipViewModel.cs"]
    sources += sorted((REPO_ROOT / "adapters" / "unity" / "Packages" / "com.gamefoundation.adapter.unity" / "Runtime" / "Ui" / "Panels").glob("*.cs"))
    referenced: set[str] = set()
    for src in sources:
        referenced |= set(re.findall(r'"(l10n\.ui\.[a-z_.]+)"', src.read_text(encoding="utf-8")))
    assert referenced, "没扫到任何界面文案键，源码路径可能变了"
    missing = sorted(referenced - kit_keys)
    assert not missing, f"起步包缺框架界面用到的文案键：{missing}"


def test_kit_covers_the_requested_content_families() -> None:
    tables = _tables()
    skills = {r["id"] for r in tables["skill.def"]["rows"]}
    for needed in ("kit_melee_1", "kit_melee_2", "kit_melee_3", "kit_heavy", "kit_charge", "kit_dodge", "kit_ranged_shot", "kit_ultimate_aoe"):
        assert f"skill.{needed}" in skills
    assert any(r["timeline"].get("combo") for r in tables["skill.def"]["rows"] if "timeline" in r)          # 连招
    assert any(r["timeline"].get("charge") for r in tables["skill.def"]["rows"] if "timeline" in r)         # 蓄力
    assert {r["id"] for r in tables["creature.tier_definition"]["rows"]} >= {"creature.tier.normal", "creature.tier.elite", "creature.tier.boss"}
    assert {r["slot"] for r in tables["item.template"]["rows"]} >= {"item.slot.main_hand", "item.slot.chest", "item.slot.trinket"}
    assert tables["item.affix"]["rows"] and tables["loot.table"]["rows"]
    objective_types = {o["type"] for q in tables["quest.def"]["rows"] for o in q["objectives"]}
    assert {"kill", "collect", "talk", "explore"} <= objective_types                                         # 计数/收集/对话交付/探索
    assert any(len(q["objectives"]) > 1 for q in tables["quest.def"]["rows"])                                # 多阶段（多目标）
    assert any("prerequisite" in q for q in tables["quest.def"]["rows"])                                     # 任务链
    assert any("condition" in b for t in tables["dialog.story_tree"]["rows"] for n in t["nodes"] for b in n["branches"])   # 按任务状态切换
    trigger_types = {r["trigger_type"] for r in tables["area.trigger_def"]["rows"]}
    assert {"map_transition", "encounter_start"} <= trigger_types                                            # 传送、遭遇区域
    assert any("npc_flag.save_point" in r.get("npc_flags", []) for r in tables["creature.template"]["rows"])  # 存档点
    assert all(m["spawn_points"] for m in tables["world.map"]["rows"])                                       # 出生点
    for curve in ("prog.level_curve", "prog.xp_base_curve", "item.budget_curve", "econ.value_curve", "econ.gold_base_curve"):
        assert tables[curve]["rows"], curve                                                                  # 经验/成长/价格曲线


def test_kit_is_not_loaded_by_the_template_unless_the_game_declares_it() -> None:
    """起步包默认不装载：模板的数据根配置默认空，示例声明只出现在注释里。"""
    options = (REPO_ROOT / "games" / "_template" / "Runtime" / "GameOptions.cs").read_text(encoding="utf-8")
    assert re.search(r"ExtraFrameworkDatasetRoots\s*=\s*Array\.Empty<string>\(\)", options)
    code_lines = [ln for ln in options.splitlines() if not ln.strip().startswith(("//", "///", "*"))]
    declared = [ln for ln in code_lines if "StarterKitDatasetRoot" in ln and "const" not in ln]
    assert not declared, f"模板默认配置里不应声明起步包：{declared}"
    bootstrap = (REPO_ROOT / "games" / "_template" / "Runtime" / "GameBootstrap.cs").read_text(encoding="utf-8")
    assert "_starter_kit" not in bootstrap
