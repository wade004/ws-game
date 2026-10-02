"""标准姿势清单的导入校验侧（手感设计/04 第 3、7、8 节）：

- 复现 + 不变量：必备键缺失为错误、推荐键缺失为警告并写明回落键、可选键静默；选入制；extends 合并与成环；
- 对照：本侧清单与 C# ``PoseChecklist.cs``（权威出处）逐项一致——改一侧忘改另一侧会被这里拦下；
- 框架级假人姿势集 ``display.anim_set.std_dummy_biped`` 对导入校验零问题。
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "toolchain"))

from asset_import import check_cmd, pose_checklist  # noqa: E402

CSHARP = REPO / "core" / "foundation" / "display_info" / "contracts" / "PoseChecklist.cs"

REQUIRED = ["idle", "move.walk", "move.run", "attack", "hit", "death"]


def _row(row_id: str, keys, **extra) -> dict:
    row = {"id": row_id, "clips": {k: {"resource_ref": "sprite_anim.x", "events": []} for k in keys}}
    row.update(extra)
    return row


def _run(rows: list[dict]) -> list:
    by_id = {r["id"]: r for r in rows}
    problems: list = []
    for r in rows:
        check_cmd._check_anim_set_pose(r, by_id, problems)
    return problems


def test_framework_set_missing_required_is_error_per_key():
    problems = _run([_row("display.anim_set.std_probe", [k for k in REQUIRED if k not in ("death", "move.run")])])
    errors = [p for p in problems if p.check == check_cmd.CHECK_ANIM_SET_POSE_REQUIRED_MISSING]
    assert len(errors) == 2
    assert all(p.severity == check_cmd.SEVERITY_ERROR for p in errors)
    assert any("move.run" in p.message for p in errors) and any("death" in p.message for p in errors)


def test_missing_recommended_is_warning_naming_fallback_key():
    problems = _run([_row("display.anim_set.std_probe2", REQUIRED)])
    warnings = [p for p in problems if p.check == check_cmd.CHECK_ANIM_SET_POSE_RECOMMENDED_MISSING]
    assert all(p.severity == check_cmd.SEVERITY_WARNING for p in warnings)
    recommended_count = sum(1 for e in pose_checklist.ENTRIES if e.tier == pose_checklist.TIER_RECOMMENDED)
    assert len(warnings) == recommended_count
    heavy = [p for p in warnings if "hit.heavy" in p.message]
    assert len(heavy) == 1 and "回落到 hit" in heavy[0].message
    run_combat = [p for p in warnings if "move.run.combat" in p.message]
    assert len(run_combat) == 1 and "回落到 move.run" in run_combat[0].message
    assert any("cast" in p.message and "无可回落" in p.message for p in warnings)


def test_complete_set_with_legacy_keys_is_clean_and_optional_is_silent():
    keys = REQUIRED + ["combat_idle", "move.run.combat", "attack.1h.02", "attack.2h.03", "hit.heavy",
                       "hit.knockback", "hit.knockdown", "hit.getup", "cast", "dodge", "jump"]
    assert _run([_row("display.anim_set.std_complete", keys)]) == []
    report = pose_checklist.evaluate(keys)
    assert not report[pose_checklist.TIER_REQUIRED]
    optional = {f.entry.key: f for f in report[pose_checklist.TIER_OPTIONAL]}
    assert "move.sprint" in optional and "混合" in optional["move.start"].describe()


def test_checklist_is_opt_in():
    legacy_keys = ["idle", "move", "attack", "cast", "hit", "death"]
    assert _run([_row("display.anim_set.legacy_sample", legacy_keys)]) == []
    opted = _run([_row("display.anim_set.legacy_opted", legacy_keys, pose_standard=True)])
    assert len([p for p in opted if p.check == check_cmd.CHECK_ANIM_SET_POSE_REQUIRED_MISSING]) == 2


def test_extends_merge_satisfies_required_and_cycles_are_errors():
    parent = _row("display.anim_set.std_parent", REQUIRED)
    child = _row("display.anim_set.std_child", ["hit.heavy"], extends="display.anim_set.std_parent")
    assert not [p for p in _run([parent, child]) if p.check == check_cmd.CHECK_ANIM_SET_POSE_REQUIRED_MISSING]

    a = _row("display.anim_set.cyc_a", [], extends="display.anim_set.cyc_b")
    b = _row("display.anim_set.cyc_b", [], extends="display.anim_set.cyc_a")
    cycle = _run([a, b])
    assert {p.record_key for p in cycle if p.check == check_cmd.CHECK_ANIM_SET_EXTENDS_INVALID} == {a["id"], b["id"]}

    selfie = _row("display.anim_set.selfie", [], extends="display.anim_set.selfie")
    assert [p.check for p in _run([selfie])] == [check_cmd.CHECK_ANIM_SET_EXTENDS_INVALID]

    orphan = _row("display.anim_set.orphan", [], extends="display.anim_set.nobody")
    assert [p.check for p in _run([orphan])] == [check_cmd.CHECK_ANIM_SET_EXTENDS_INVALID]


def test_sprint_missing_falls_back_to_run_in_report():
    report = pose_checklist.evaluate(REQUIRED)
    sprint = [f for f in report[pose_checklist.TIER_OPTIONAL] if f.entry.key == "move.sprint"][0]
    assert sprint.fallback_key == "move.run"


def test_std_dummy_biped_has_zero_pose_issues():
    path = REPO / "data" / "_framework" / "display" / "display.anim_set.json"
    rows = json.loads(path.read_text(encoding="utf-8"))["rows"]
    # sprite 型与 model 型两版并列（手感设计/04 第 10 节），两版都有 medium（空 extends 行）、light、heavy 三个体量行；全部必须零姿势问题
    assert sorted(r["id"] for r in rows) == sorted(
        base + suffix for base in ("display.anim_set.std_dummy_biped", "display.anim_set.std_dummy_biped_model")
        for suffix in ("", "_medium", "_light", "_heavy"))
    assert _run(rows) == []
    for row in rows:
        if "extends" not in row:        # extends 行要靠同表的主集行合并，单独校验没有意义
            assert _run([row]) == []


def test_python_checklist_matches_csharp_source_of_truth():
    source = CSHARP.read_text(encoding="utf-8")
    tier_map = {"Required": pose_checklist.TIER_REQUIRED, "Recommended": pose_checklist.TIER_RECOMMENDED,
                "Optional": pose_checklist.TIER_OPTIONAL}
    cs = re.findall(r'new PoseChecklistEntry\("([a-z0-9_.]+)", PoseTier\.(Required|Recommended|Optional)', source)
    assert [(k, tier_map[t]) for k, t in cs] == [(e.key, e.tier) for e in pose_checklist.ENTRIES]

    # 带 fallbackFrom 的项两侧一致
    cs_from = dict(re.findall(r'new PoseChecklistEntry\("([a-z0-9_.]+)",[^\n]*?fallbackFrom: "([a-z0-9_.]+)"', source))
    py_from = {e.key: e.fallback_from for e in pose_checklist.ENTRIES if e.fallback_from}
    assert cs_from == py_from

    # 框架级姿势集前缀与旧键前缀
    prefix = re.search(r'FrameworkSetIdPrefix = "([^"]+)"', source).group(1)
    assert prefix == pose_checklist.FRAMEWORK_SET_ID_PREFIX
    keys_cs = (REPO / "core" / "foundation" / "display_info" / "contracts" / "AnimSetDef.cs").read_text(encoding="utf-8")
    legacy = re.search(r'CombatClipKeyPrefix = "([^"]+)"', keys_cs).group(1)
    assert legacy == pose_checklist.LEGACY_COMBAT_PREFIX
