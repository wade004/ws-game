"""剪辑标记检查（手感设计/04 第 5、8 节、01 第 3.2 节，ADR-0147）：``import_assets check`` 的 ``display_anim`` 域里
"剪辑标记齐全"与"剪辑标记和 ``skill.def.timeline`` 一致"两类检查的实现。

与 C# 侧同口径（``core/rules/skill/core/TimelineClip.cs`` 的 ``TimelineClipImporter`` / ``TimelineClipConsistency``、
``core/rules/assembly/SkillClipConsistency.cs`` 的 ``DisplayClipMarkerSource``）：事件百分比 × 剪辑总时长 = 毫秒；
分相与判定类标记按同一套命名约定抄写；技能到剪辑的对应由武器表现数据推出。C# 侧是加载期规则（只能读数据表，所以要
``display.anim_set.clips[*].duration_ms``），本模块在工具链里可以多一条路：剪辑没有声明 ``duration_ms`` 时从资源
（``sprite_anim/<name>/frames.json``）量出总时长；声明了但与资源不一致是错误。

判断记录：
- **选入制**：标记齐全检查只对"按清单发布"的姿势集（``pose_standard: true`` 或框架级 ``display.anim_set.std_*``）做，
  与标准姿势清单同一口径——既有的自由姿势集不受影响（缺省行为不变）。一致性检查不受此限（只看 ``timeline`` 声明了的技能）。
- **model 型剪辑**（``anim.*`` 资源引用）没有磁盘资源可量：只有声明了 ``duration_ms`` 才参与一致性检查，没声明静默跳过。
- 一致性：``source: clip`` 任一偏差超过抄写取整误差（0.5 毫秒）为错误（说明忘了重新导入）；``source: data`` 偏差超过标定表
  ``marker_tolerance_ms``（取不到按 50）为警告。``source: clip`` 取不到对应剪辑为错误，``source: data`` 取不到静默跳过。
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Optional

from . import pose_checklist
from .common import read_json

COPY_EPSILON_MS = 0.5
FALLBACK_TOLERANCE_MS = 50.0
DURATION_TOLERANCE_MS = 1.0

COPIED_MARKERS = frozenset(
    {"invuln_start", "invuln_end", "armor_start", "armor_end", "guard_start", "guard_end", "motion_start", "motion_end", "release"}
)

CHECK_CLIP_MARKERS_MISSING = "anim_set_clip_markers_missing"
CHECK_CLIP_DURATION_MISMATCH = "anim_set_clip_duration_mismatch"
CHECK_TIMELINE_CLIP_MISSING = "timeline_clip_missing"
CHECK_TIMELINE_CLIP_MISMATCH = "timeline_clip_mismatch"
CHECK_TIMELINE_CLIP_DEVIATION = "timeline_clip_deviation"

CHECK_NAMES = (
    CHECK_CLIP_MARKERS_MISSING,
    CHECK_CLIP_DURATION_MISMATCH,
    CHECK_TIMELINE_CLIP_MISSING,
    CHECK_TIMELINE_CLIP_MISMATCH,
    CHECK_TIMELINE_CLIP_DEVIATION,
)


@dataclass(frozen=True)
class Finding:
    """一条检查结论（由 ``check_cmd`` 转成 ``CheckIssue``）。"""

    severity: str  # "error" / "warning"
    table: str
    record_key: str
    check: str
    field_path: Optional[str]
    message: str


# ---------------------------------------------------------------------------------------------------------------------
# 姿势集合并（沿 extends 链，子集覆盖同名键；带事件与时长）
# ---------------------------------------------------------------------------------------------------------------------


def merged_clips(row: dict, rows_by_id: dict[str, dict]) -> dict[str, dict]:
    """沿 ``extends`` 链合并后的剪辑条目（规范键 → 条目）。继承有问题（缺失、成环）时只含已走到的部分，由姿势清单检查报继承问题。"""
    chain: list[dict] = []
    seen: set[str] = set()
    cursor: Optional[dict] = row
    while cursor is not None and cursor.get("id", "?") not in seen:
        seen.add(cursor.get("id", "?"))
        chain.append(cursor)
        parent = cursor.get("extends")
        cursor = rows_by_id.get(parent) if parent else None
    merged: dict[str, dict] = {}
    for r in reversed(chain):  # 祖先在前，子集覆盖
        for key, clip in (r.get("clips") or {}).items():
            if isinstance(clip, dict):
                merged[pose_checklist.canonicalize(key)] = clip
    return merged


# ---------------------------------------------------------------------------------------------------------------------
# 04 第 8 节：标记齐全
# ---------------------------------------------------------------------------------------------------------------------


def _event_names(clip: dict) -> list[str]:
    return [e.get("name", "") for e in (clip.get("events") or []) if isinstance(e, dict)]


def _has_hit(names: Iterable[str]) -> bool:
    return any(n == "hit" or n.startswith("hit:") for n in names)


def check_clip_markers(row: dict, rows_by_id: dict[str, dict]) -> list[Finding]:
    """攻击类剪辑须有 ``active_start``/``active_end`` 与至少一个 ``hit``；走/跑/冲刺剪辑须有 ``footstep``；闪避类须有
    ``invuln_start``/``invuln_end``（04 第 8 节）。只对按清单发布的姿势集检查；缺项为错误。"""
    row_id = row.get("id", "?")
    if not pose_checklist.applies_to(row_id, bool(row.get("pose_standard", False))):
        return []

    findings: list[Finding] = []

    def missing(key: str, what: str) -> None:
        findings.append(
            Finding("error", "display.anim_set", row_id, CHECK_CLIP_MARKERS_MISSING, f"clips[{key}].events", f"剪辑 {key} 缺标记：{what}")
        )

    for key, clip in sorted(merged_clips(row, rows_by_id).items()):
        names = _event_names(clip)
        state = key.split(".", 1)[0]
        parts = key.split(".")
        if state == "attack":
            absent = [n for n in ("active_start", "active_end") if n not in names]
            if not _has_hit(names):
                absent.append("hit")
            if absent:
                missing(key, "、".join(absent))
        elif state == "move" and len(parts) > 1 and parts[1] in ("walk", "run", "sprint"):
            if "footstep" not in names:
                missing(key, "footstep")
        elif state == "dodge":
            absent = [n for n in ("invuln_start", "invuln_end") if n not in names]
            if absent:
                missing(key, "、".join(absent))
    return findings


# ---------------------------------------------------------------------------------------------------------------------
# 总时长：声明值与资源量出值
# ---------------------------------------------------------------------------------------------------------------------


def measure_sprite_anim_ms(assets_root: Path, dataset: str, resource_ref: str) -> Optional[float]:
    """``sprite_anim.<名>`` 资源的总时长（毫秒）：``frames.json`` 里全部帧时长之和。取不到（不是 sprite_anim 前缀、文件缺失、
    读不懂）返回 ``None``。"""
    category, _, name = resource_ref.partition(".")
    if category != "sprite_anim" or not name:
        return None
    frames_path = assets_root / dataset / "sprite_anim" / name / "frames.json"
    if not frames_path.is_file():
        return None
    try:
        data = read_json(frames_path)
        frames = data.get("frames", []) if isinstance(data, dict) else []
        return float(sum(float(f.get("duration", 0.0)) for f in frames)) * 1000.0
    except (ValueError, TypeError, OSError):
        return None


def clip_duration_ms(clip: dict, assets_root: Path, dataset: str) -> Optional[float]:
    declared = clip.get("duration_ms")
    if isinstance(declared, (int, float)) and declared > 0:
        return float(declared)
    return measure_sprite_anim_ms(assets_root, dataset, clip.get("resource_ref", ""))


def check_clip_duration(row: dict, assets_root: Path, dataset: str) -> list[Finding]:
    """声明了 ``duration_ms`` 的 sprite_anim 剪辑须与资源量出的总时长一致（容差 1 毫秒）；量不出资源的不检查。"""
    row_id = row.get("id", "?")
    findings: list[Finding] = []
    for key, clip in (row.get("clips") or {}).items():
        if not isinstance(clip, dict):
            continue
        declared = clip.get("duration_ms")
        if not isinstance(declared, (int, float)):
            continue
        measured = measure_sprite_anim_ms(assets_root, dataset, clip.get("resource_ref", ""))
        if measured is not None and abs(measured - declared) > DURATION_TOLERANCE_MS:
            findings.append(
                Finding(
                    "error", "display.anim_set", row_id, CHECK_CLIP_DURATION_MISMATCH, f"clips[{key}].duration_ms",
                    f"剪辑 {key} 声明 duration_ms={declared:g}，资源 {clip.get('resource_ref')} 量出 {measured:.1f} 毫秒（容差 {DURATION_TOLERANCE_MS:g}）",
                )
            )
    return findings


# ---------------------------------------------------------------------------------------------------------------------
# 导入：剪辑事件 -> 时间线字段（TimelineClipImporter 的镜像）
# ---------------------------------------------------------------------------------------------------------------------


@dataclass
class ClipTimeline:
    startup_ms: float
    active_ms: float
    recovery_ms: float
    markers: list[tuple[str, float, Optional[str]]] = field(default_factory=list)  # (name, at_ms, hit segment)
    cancel_windows: list[tuple[str, float, Optional[float]]] = field(default_factory=list)  # (class, open_ms, close_ms)
    combo_open_ms: Optional[float] = None
    combo_close_ms: Optional[float] = None
    notes: list[str] = field(default_factory=list)


def import_timeline(events: list[dict], total_ms: float) -> ClipTimeline:
    notes: list[str] = []
    active_start = active_end = combo_open = combo_close = None
    markers: list[tuple[str, float, Optional[str]]] = []
    cancel_open: list[tuple[str, float]] = []
    cancel_close: dict[str, float] = {}
    hit_ordinal = 0

    for e in events:
        name = e.get("name", "")
        at = float(e.get("time_pct", 0.0)) * total_ms
        if name == "active_start":
            active_start = at
        elif name == "active_end":
            active_end = at
        elif name == "combo_open":
            combo_open = at
        elif name == "combo_close":
            combo_close = at
        elif name.startswith("cancel_open:"):
            cancel_open.append((name[len("cancel_open:"):], at))
        elif name.startswith("cancel_close:"):
            cancel_close[name[len("cancel_close:"):]] = at
        elif name == "hit" or name.startswith("hit:"):
            segment = str(hit_ordinal) if name == "hit" else name[len("hit:"):]
            hit_ordinal += 1
            markers.append(("hit", at, segment))
        elif name in COPIED_MARKERS:
            markers.append((name, at, None))
        elif name.startswith("trail_") or name == "footstep" or name == "fx" or name.startswith("fx:") or name == "impact" or name == "hit_frame":
            continue  # 表现类事件（或命中帧别名）：只在剪辑元数据里，规则层不读
        else:
            notes.append(f'未识别的剪辑事件 "{name}" 已忽略')

    startup = active_start if active_start is not None else 0.0
    active = max(0.0, (active_end if active_end is not None else total_ms) - startup)
    recovery = max(0.0, total_ms - startup - active)
    windows = [(cls, at, cancel_close.get(cls)) for cls, at in cancel_open]
    return ClipTimeline(startup, active, recovery, markers, windows, combo_open, combo_close, notes)


def _timeline_markers(timeline: dict) -> list[tuple[str, float, Optional[str]]]:
    """``skill.def.timeline.markers`` 归一：``hit:<段>`` -> ``hit`` + 段，无段的 ``hit`` 按出现序编号。"""
    result: list[tuple[str, float, Optional[str]]] = []
    ordinal = 0
    for m in timeline.get("markers") or []:
        raw = m.get("name", "")
        name, segment = raw, None
        args = m.get("args") or {}
        if raw.startswith("hit:"):
            name, segment = "hit", raw[len("hit:"):]
        elif raw == "hit":
            segment = str(args["segment"]) if "segment" in args else None
        if name == "hit":
            if segment is None:
                segment = str(ordinal)
            ordinal += 1
        result.append((name, float(m.get("at_ms", 0.0)), segment))
    return result


def compare_timeline(skill_id: str, timeline: dict, clip: ClipTimeline, tolerance_ms: float) -> list[Finding]:
    is_clip = timeline.get("source") == "clip"
    limit = COPY_EPSILON_MS if is_clip else tolerance_ms
    severity = "error" if is_clip else "warning"
    check = CHECK_TIMELINE_CLIP_MISMATCH if is_clip else CHECK_TIMELINE_CLIP_DEVIATION
    findings: list[Finding] = []

    def add(message: str) -> None:
        findings.append(Finding(severity, "skill.def", skill_id, check, "timeline", message))

    def cmp(what: str, data_ms: float, clip_ms: float) -> None:
        if abs(data_ms - clip_ms) > limit:
            findings.append(
                Finding(severity, "skill.def", skill_id, check, "timeline", f"{what}：timeline={data_ms:g} 毫秒，剪辑={clip_ms:g} 毫秒（容差 {limit:g}）")
            )

    cmp("startup_ms", float(timeline.get("startup_ms", 0.0)), clip.startup_ms)
    cmp("active_ms", float(timeline.get("active_ms", 0.0)), clip.active_ms)
    cmp("recovery_ms", float(timeline.get("recovery_ms", 0.0)), clip.recovery_ms)

    data_markers = _timeline_markers(timeline)
    for name, at, segment in clip.markers:
        label = name + (f":{segment}" if segment is not None and name == "hit" else "")
        match = next(
            (dm for dm in data_markers if dm[0] == name and (name != "hit" or dm[2] == segment)),
            None,
        )
        if match is None:
            add(f'剪辑有标记 "{label}"，timeline.markers 里没有')
        else:
            cmp(f"标记 {label}", match[1], at)

    for cls, open_ms, close_ms in clip.cancel_windows:
        label = f"cancel_windows[{cls}]"
        match_w = next((w for w in (timeline.get("cancel_windows") or []) if w.get("class") == cls), None)
        if match_w is None:
            add(f'剪辑有取消窗口 "{label}"，timeline.cancel_windows 里没有')
            continue
        cmp(label + ".open_ms", float(match_w.get("open_ms", 0.0)), open_ms)
        if close_ms is not None and "close_ms" in match_w:
            cmp(label + ".close_ms", float(match_w["close_ms"]), close_ms)

    if clip.combo_open_ms is not None:
        combo = timeline.get("combo")
        if not isinstance(combo, dict):
            add("剪辑有 combo_open 事件，timeline 没有 combo 块")
        else:
            cmp("combo.open_ms", float(combo.get("open_ms", 0.0)), clip.combo_open_ms)
            if clip.combo_close_ms is not None:
                cmp("combo.close_ms", float(combo.get("close_ms", 0.0)), clip.combo_close_ms)
    return findings


# ---------------------------------------------------------------------------------------------------------------------
# 技能 -> 剪辑（DisplayClipMarkerSource 的镜像）
# ---------------------------------------------------------------------------------------------------------------------


@dataclass
class SkillClipSource:
    """技能 id -> (剪辑键, 剪辑条目)。技能经武器表现数据对应到剪辑：``cast_anim_override``（优先）与普攻链
    （``item.template.feel_weapon_ref`` -> ``feel.weapon.auto_attack_timeline_ref``，``item.template.display_ref`` ->
    ``display.map.weapon_style_ref`` -> ``display.weapon_style.auto_attack_anim``）。资源引用再经姿势集（合并继承链）找剪辑条目。"""

    by_skill: dict[str, tuple[str, dict]] = field(default_factory=dict)


def build_skill_clip_source(
    anim_sets: list[dict], weapon_styles: list[dict], display_maps: list[dict], items: list[dict], feel_weapons: list[dict]
) -> SkillClipSource:
    sets_by_id = {r.get("id", "?"): r for r in anim_sets}
    by_resource: dict[str, tuple[str, dict]] = {}
    for set_row in sorted(anim_sets, key=lambda r: r.get("id", "")):
        for key, clip in sorted(merged_clips(set_row, sets_by_id).items()):
            ref = clip.get("resource_ref")
            if ref and ref not in by_resource:
                by_resource[ref] = (key, clip)

    source = SkillClipSource()
    styles = {r.get("id"): r for r in weapon_styles}
    maps = {r.get("id"): r for r in display_maps}
    weapons = {r.get("id"): r for r in feel_weapons}

    for item in sorted(items, key=lambda r: r.get("id", "")):
        weapon = weapons.get(item.get("feel_weapon_ref"))
        display = maps.get(item.get("display_ref"))
        if not weapon or not display:
            continue
        skill, style_ref = weapon.get("auto_attack_timeline_ref"), display.get("weapon_style_ref")
        style = styles.get(style_ref)
        if skill and style and style.get("auto_attack_anim") in by_resource and skill not in source.by_skill:
            source.by_skill[skill] = by_resource[style["auto_attack_anim"]]

    for style in sorted(weapon_styles, key=lambda r: r.get("id", "")):
        for skill, clip_ref in (style.get("cast_anim_override") or {}).items():
            if clip_ref in by_resource:
                source.by_skill[skill] = by_resource[clip_ref]  # 施法覆盖优先
    return source


def check_skill_consistency(
    skills: list[dict], source: SkillClipSource, assets_root: Path, dataset: str, tolerance_ms: float
) -> list[Finding]:
    findings: list[Finding] = []
    for skill in sorted(skills, key=lambda r: r.get("id", "")):
        timeline = skill.get("timeline")
        if not isinstance(timeline, dict):
            continue
        skill_id = skill.get("id", "?")
        entry = source.by_skill.get(skill_id)
        total = clip_duration_ms(entry[1], assets_root, dataset) if entry else None
        if entry is None or total is None:
            if timeline.get("source") == "clip":
                findings.append(
                    Finding("error", "skill.def", skill_id, CHECK_TIMELINE_CLIP_MISSING, "timeline.source",
                            "timeline.source 为 clip 但取不到该技能对应的剪辑标记（武器表现里没有指到它的剪辑，或剪辑量不出总时长）")
                )
            continue
        clip = import_timeline(entry[1].get("events") or [], total)
        findings.extend(compare_timeline(skill_id, timeline, clip, tolerance_ms))
    return findings


def resolve_tolerance_ms(calibrations: list[dict]) -> float:
    """标定表的 ``marker_tolerance_ms``：优先 ``feel.calibration.framework_default``，否则 id 序最前的一行，都没有取 50。"""
    if not calibrations:
        return FALLBACK_TOLERANCE_MS
    chosen = next((r for r in calibrations if r.get("id") == "feel.calibration.framework_default"), None)
    if chosen is None:
        chosen = sorted(calibrations, key=lambda r: r.get("id", ""))[0]
    value = chosen.get("marker_tolerance_ms")
    return float(value) if isinstance(value, (int, float)) and not math.isnan(value) else FALLBACK_TOLERANCE_MS
