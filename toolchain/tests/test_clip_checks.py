"""剪辑标记检查（手感设计/04 第 5、8 节，01 第 3.2 节，ADR-0147）：

- 复现 + 不变量：攻击/走跑/闪避剪辑缺标记为错误（只对按清单发布的姿势集）；事件百分比 x 总时长 = 毫秒，由规则算出期望值；
  ``source: clip`` 偏差为错误、``source: data`` 超容差为警告；技能经武器表现（施法覆盖优先于普攻）对应到剪辑；
- 总时长：声明的 ``duration_ms`` 与资源（frames.json 帧时长之和）不一致为错误；声明缺省时从资源量出；
- 框架级假人姿势集对标记齐全检查零问题。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "toolchain"))

from asset_import import check_cmd, clip_checks  # noqa: E402

TOTAL = 800.0


def _clip(ref: str, events: list[tuple[str, float]], duration=None) -> dict:
    clip: dict = {"resource_ref": ref, "events": [{"name": n, "time_pct": p} for n, p in events]}
    if duration is not None:
        clip["duration_ms"] = duration
    return clip


ATTACK_EVENTS = [("active_start", 0.25), ("hit", 0.3125), ("active_end", 0.5)]
CAST_EVENTS = [("active_start", 0.5), ("release", 0.625), ("active_end", 0.75)]


def _set(row_id: str, clips: dict, **extra) -> dict:
    row = {"id": row_id, "clips": clips}
    row.update(extra)
    return row


# ---------------------------------------------------------------------------------------------------------------------
# 标记齐全
# ---------------------------------------------------------------------------------------------------------------------


def test_attack_walk_dodge_markers_are_required_for_standard_sets_only():
    clips = {
        "attack": _clip("anim.a", [("active_start", 0.25)]),            # 缺 active_end 与 hit
        "move.run": _clip("anim.r", []),                                # 缺 footstep
        "move.walk": _clip("anim.w", [("footstep", 0.0)]),              # 齐全
        "dodge": _clip("anim.d", [("invuln_start", 0.1)]),              # 缺 invuln_end
        "idle": _clip("anim.i", []),                                    # 不要求
    }
    standard = _set("display.anim_set.std_probe", clips)
    findings = clip_checks.check_clip_markers(standard, {standard["id"]: standard})
    by_key = {f.field_path: f for f in findings}
    assert set(by_key) == {"clips[attack].events", "clips[move.run].events", "clips[dodge].events"}
    assert all(f.severity == "error" and f.check == clip_checks.CHECK_CLIP_MARKERS_MISSING for f in findings)
    assert "active_end" in by_key["clips[attack].events"].message and "hit" in by_key["clips[attack].events"].message
    assert "invuln_end" in by_key["clips[dodge].events"].message and "invuln_start" not in by_key["clips[dodge].events"].message

    free = _set("display.anim_set.sample_free", clips)          # 选入制：自由姿势集不受影响
    assert clip_checks.check_clip_markers(free, {free["id"]: free}) == []
    opted = _set("display.anim_set.sample_opted", clips, pose_standard=True)
    assert len(clip_checks.check_clip_markers(opted, {opted["id"]: opted})) == 3


def test_markers_are_checked_on_the_merged_clips_through_extends():
    parent = _set("display.anim_set.std_parent", {"attack": _clip("anim.a", ATTACK_EVENTS)})
    child = _set("display.anim_set.std_child", {"attack": _clip("anim.b", [("hit", 0.4)])}, extends=parent["id"])
    rows = {parent["id"]: parent, child["id"]: child}
    findings = clip_checks.check_clip_markers(child, rows)
    assert [f.field_path for f in findings] == ["clips[attack].events"]   # 子集覆盖了父集的键，以子集的事件为准
    assert clip_checks.check_clip_markers(parent, rows) == []


def test_framework_standard_sets_have_all_required_markers():
    rows = json.loads((REPO / "data" / "_framework" / "display" / "display.anim_set.json").read_text(encoding="utf-8"))["rows"]
    by_id = {r["id"]: r for r in rows}
    for row in rows:
        assert clip_checks.check_clip_markers(row, by_id) == [], row["id"]


def test_check_anim_set_row_reports_marker_findings_as_check_issues(tmp_path):
    row = _set("display.anim_set.std_probe", {"attack": _clip("anim.a", [])})
    problems: list = []
    check_cmd._check_anim_set_row(row, tmp_path, "_t", problems)
    assert any(p.check == clip_checks.CHECK_CLIP_MARKERS_MISSING and p.severity == check_cmd.SEVERITY_ERROR for p in problems)


# ---------------------------------------------------------------------------------------------------------------------
# 导入与一致性
# ---------------------------------------------------------------------------------------------------------------------


def test_import_timeline_converts_percentages_with_the_clip_duration():
    imported = clip_checks.import_timeline([{"name": n, "time_pct": p} for n, p in CAST_EVENTS], TOTAL)
    assert imported.startup_ms == pytest.approx(0.5 * TOTAL)
    assert imported.active_ms == pytest.approx((0.75 - 0.5) * TOTAL)
    assert imported.recovery_ms == pytest.approx(TOTAL - 0.75 * TOTAL)
    assert ("release", pytest.approx(0.625 * TOTAL), None) in imported.markers


def _skill(source: str, startup=400, active=200, recovery=200, release_at=500) -> dict:
    return {
        "id": "skill.t_spell",
        "timeline": {"source": source, "startup_ms": startup, "active_ms": active, "recovery_ms": recovery,
                     "markers": [{"name": "release", "at_ms": release_at}]},
    }


def _source(duration=TOTAL, override_basic=True):
    anim_sets = [_set("display.anim_set.t1", {"attack": _clip("anim.t_slash", ATTACK_EVENTS, duration),
                                              "cast": _clip("anim.t_cast", CAST_EVENTS, duration)})]
    overrides = {"skill.t_spell": "anim.t_cast"}
    if override_basic:
        overrides["skill.t_basic"] = "anim.t_cast"
    styles = [{"id": "display.weapon_style.t_sword", "auto_attack_anim": "anim.t_slash", "cast_anim_override": overrides}]
    maps = [{"id": "display.map.t_sword", "weapon_style_ref": "display.weapon_style.t_sword"}]
    items = [{"id": "item.t_sword", "feel_weapon_ref": "feel.weapon.t_sword", "display_ref": "display.map.t_sword"}]
    weapons = [{"id": "feel.weapon.t_sword", "auto_attack_timeline_ref": "skill.t_basic"}]
    return clip_checks.build_skill_clip_source(anim_sets, styles, maps, items, weapons)


def test_skill_source_maps_through_weapon_styles_with_cast_override_taking_precedence():
    source = _source()
    assert source.by_skill["skill.t_spell"][0] == "cast"
    assert source.by_skill["skill.t_basic"][0] == "cast"          # 施法覆盖压过普攻
    assert _source(override_basic=False).by_skill["skill.t_basic"][0] == "attack"


def test_clip_source_exact_copy_passes_and_drift_is_an_error(tmp_path):
    assert clip_checks.check_skill_consistency([_skill("clip")], _source(), tmp_path, "_t", 50.0) == []
    drift = clip_checks.check_skill_consistency([_skill("clip", release_at=580)], _source(), tmp_path, "_t", 50.0)
    assert [(f.severity, f.check) for f in drift] == [("error", clip_checks.CHECK_TIMELINE_CLIP_MISMATCH)]


def test_data_source_deviation_beyond_tolerance_is_a_warning_and_within_is_silent(tmp_path):
    within = clip_checks.check_skill_consistency([_skill("data", release_at=500 + 49)], _source(), tmp_path, "_t", 50.0)
    assert within == []
    beyond = clip_checks.check_skill_consistency([_skill("data", release_at=500 + 51)], _source(), tmp_path, "_t", 50.0)
    assert [(f.severity, f.check) for f in beyond] == [("warning", clip_checks.CHECK_TIMELINE_CLIP_DEVIATION)]


def test_missing_clip_is_an_error_for_clip_source_and_silent_for_data_source(tmp_path):
    no_duration = _source(duration=None)                          # model 型剪辑没声明总时长：量不出
    missing = clip_checks.check_skill_consistency([_skill("clip")], no_duration, tmp_path, "_t", 50.0)
    assert [(f.severity, f.check) for f in missing] == [("error", clip_checks.CHECK_TIMELINE_CLIP_MISSING)]
    assert clip_checks.check_skill_consistency([_skill("data")], no_duration, tmp_path, "_t", 50.0) == []


def test_tolerance_prefers_the_framework_default_calibration_row():
    rows = [{"id": "feel.calibration.a", "marker_tolerance_ms": 10}, {"id": "feel.calibration.framework_default", "marker_tolerance_ms": 70}]
    assert clip_checks.resolve_tolerance_ms(rows) == 70
    assert clip_checks.resolve_tolerance_ms(rows[:1]) == 10
    assert clip_checks.resolve_tolerance_ms([]) == clip_checks.FALLBACK_TOLERANCE_MS


# ---------------------------------------------------------------------------------------------------------------------
# 总时长：声明值与资源
# ---------------------------------------------------------------------------------------------------------------------


def _write_frames(assets: Path, dataset: str, name: str, durations: list[float]) -> None:
    out = assets / dataset / "sprite_anim" / name
    out.mkdir(parents=True)
    (out / "frames.json").write_text(json.dumps({"frames": [{"index": i, "duration": d} for i, d in enumerate(durations)]}), encoding="utf-8")


def test_duration_is_measured_from_frames_and_a_wrong_declaration_is_an_error(tmp_path):
    _write_frames(tmp_path, "_t", "probe", [0.05] * 16)            # 800 毫秒
    assert clip_checks.measure_sprite_anim_ms(tmp_path, "_t", "sprite_anim.probe") == pytest.approx(TOTAL)
    assert clip_checks.measure_sprite_anim_ms(tmp_path, "_t", "anim.engine_side") is None

    ok = _set("display.anim_set.t", {"attack": _clip("sprite_anim.probe", ATTACK_EVENTS, TOTAL)})
    assert clip_checks.check_clip_duration(ok, tmp_path, "_t") == []
    bad = _set("display.anim_set.t", {"attack": _clip("sprite_anim.probe", ATTACK_EVENTS, TOTAL + 20)})
    findings = clip_checks.check_clip_duration(bad, tmp_path, "_t")
    assert [f.check for f in findings] == [clip_checks.CHECK_CLIP_DURATION_MISMATCH]
    undeclared = _set("display.anim_set.t", {"attack": _clip("sprite_anim.probe", ATTACK_EVENTS)})
    assert clip_checks.check_clip_duration(undeclared, tmp_path, "_t") == []


def test_consistency_falls_back_to_the_measured_duration_when_undeclared(tmp_path):
    _write_frames(tmp_path, "_t", "cast_probe", [0.05] * 16)
    anim_sets = [_set("display.anim_set.t1", {"cast": _clip("sprite_anim.cast_probe", CAST_EVENTS)})]
    styles = [{"id": "display.weapon_style.t", "cast_anim_override": {"skill.t_spell": "sprite_anim.cast_probe"}}]
    source = clip_checks.build_skill_clip_source(anim_sets, styles, [], [], [])
    assert clip_checks.check_skill_consistency([_skill("clip")], source, tmp_path, "_t", 50.0) == []
    drift = clip_checks.check_skill_consistency([_skill("clip", release_at=560)], source, tmp_path, "_t", 50.0)
    assert [f.check for f in drift] == [clip_checks.CHECK_TIMELINE_CLIP_MISMATCH]


def test_check_command_runs_the_skill_consistency_over_a_data_tree(tmp_path, capsys):
    data = tmp_path / "data" / "_t"
    (data / "skill").mkdir(parents=True)
    (data / "display").mkdir(parents=True)
    (data / "skill" / "skill.def.json").write_text(json.dumps({"table": "skill.def", "rows": [_skill("clip", release_at=580)]}), encoding="utf-8")
    (data / "display" / "display.anim_set.json").write_text(json.dumps({"table": "display.anim_set", "rows": [
        _set("display.anim_set.t1", {"cast": _clip("anim.t_cast", CAST_EVENTS, TOTAL)})]}), encoding="utf-8")
    (data / "display" / "display.weapon_style.json").write_text(json.dumps({"table": "display.weapon_style", "rows": [
        {"id": "display.weapon_style.t", "cast_anim_override": {"skill.t_spell": "anim.t_cast"}}]}), encoding="utf-8")

    problems: list = []
    check_cmd._check_skill_clip_consistency(tmp_path / "data", "_t", tmp_path / "assets", problems)
    assert [(p.table, p.check, p.severity) for p in problems] == [("skill.def", clip_checks.CHECK_TIMELINE_CLIP_MISMATCH, "error")]

    problems.clear()
    (data / "skill" / "skill.def.json").write_text(json.dumps({"table": "skill.def", "rows": [_skill("clip")]}), encoding="utf-8")
    check_cmd._check_skill_clip_consistency(tmp_path / "data", "_t", tmp_path / "assets", problems)
    assert problems == []
