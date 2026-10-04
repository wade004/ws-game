# -*- coding: utf-8 -*-
"""演示场景：动画集键 -> 角色姿势片段 的对应表，以及每个片段的时长/循环/出手点（取自框架标准假人动画集同名键，保证事件时间百分比与逻辑命中标记对得上）。"""
from __future__ import annotations

import json
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
STD_SET = "display.anim_set.std_dummy_biped"
FAMILIES = ("unarmed", "1h", "2h", "polearm", "bow", "staff", "dual", "shield")

LOOP_RANGE = {"idle": (14, 30), "idle_combat": (12, 28), "run": (6, 20), "sprint": (5, 16)}

# 额外的施法剪辑键（实验室武器风格行按技能 id 指到它们）：键 -> (姿势, 时长 ms, 出手点百分比)
EXTRA_CAST = {
    "hero": {
        "cast.combo2": ("combo2", 600, 0.1667),
        "cast.combo3": ("combo3", 600, 0.25),
        "cast.slam": ("slam", 600, 0.25),
        "cast.charge": ("charge", 600, 0.25),
    },
    "grunt": {},
    "brute": {},
}

POSES = {
    "hero": {"idle", "idle_combat", "run", "sprint", "combo1", "combo2", "combo3", "slam", "charge", "dodge", "hit_light", "hit_heavy", "knockback", "knockdown", "getup", "death"},
    "grunt": {"idle", "run", "hit_light", "hit_heavy", "knockback", "knockdown", "getup", "death"},
    "brute": {"idle", "run", "swing", "hit_light", "hit_heavy", "knockdown", "getup", "death"},
}


def pose_for_key(char: str, key: str) -> str | None:
    """框架动画集键 -> 该角色的姿势；没有对应姿势返回 None（该角色的动画集里不含这个键）。"""
    parts = key.split(".")
    if any(p in FAMILIES or p == "wounded" or p == "02" or p == "03" for p in parts):
        return None
    head = parts[0]
    avail = POSES[char]
    pose: str | None
    if head == "idle":
        pose = "idle_combat" if "combat" in parts else "idle"
    elif head in ("stunned", "block", "jump"):
        return None
    elif head == "move":
        if len(parts) > 1 and parts[1] in ("start", "stop", "pivot"):
            return None
        pose = "sprint" if "sprint" in parts else "run"
    elif head == "attack":
        pose = "combo1"
    elif head == "cast":
        pose = {"quick": "combo1", "heavy": "slam"}.get(parts[1] if len(parts) > 1 else "", "slam")
    elif head == "hit":
        sub = parts[1] if len(parts) > 1 else ""
        if sub in ("block", "air", "launch"):
            return None
        pose = {"heavy": "hit_heavy", "knockback": "knockback", "knockdown": "knockdown", "getup": "getup"}.get(sub, "hit_light")
    elif head == "death":
        pose = "death"
    elif head == "dodge":
        pose = "dodge"
    else:
        return None
    # 角色没有这个姿势时的替补
    if char != "hero":
        pose = {
            "combo1": "swing", "slam": "swing", "sprint": "run", "idle_combat": "idle", "dodge": None,
        }.get(pose, pose)
        if pose == "swing" and "swing" not in avail:
            pose = None
        if pose == "knockback" and "knockback" not in avail:
            pose = "hit_heavy"
    if pose is not None and pose not in avail:
        return None
    return pose


def std_rows() -> dict:
    d = json.loads((REPO / "data" / "_framework" / "display" / "display.anim_set.json").read_text(encoding="utf-8"))
    row = [r for r in d["rows"] if r["id"] == STD_SET][0]
    return row["clips"]


def std_ms(ref: str) -> tuple[int, bool]:
    """标准假人剪辑的总时长（ms）与是否循环（读占位美术的 frames.json）。"""
    name = ref.split(".", 1)[1]
    base = REPO / "assets" / "_placeholder" / "sprite_anim"
    for slot in ("front", "front__body"):
        p = base / f"{name}__{slot}" / "frames.json"
        if p.exists():
            f = json.loads(p.read_text(encoding="utf-8"))
            return round(sum(x["duration"] for x in f["frames"]) * 1000), bool(f.get("loop"))
    raise FileNotFoundError(ref)


def _strike_pct(events: list[dict]) -> float | None:
    for name in ("release", "hit_frame", "hit"):
        for e in events:
            if e["name"] == name:
                return float(e["time_pct"])
    return None


def keys_for(char: str) -> dict[str, dict]:
    """该角色动画集的全部键：键 -> {pose, ms, loop, events, strike}。"""
    std = std_rows()
    out: dict[str, dict] = {}
    for key, c in std.items():
        pose = pose_for_key(char, key)
        if pose is None:
            continue
        ms, loop = std_ms(c["resource_ref"])
        strike = _strike_pct(c["events"])
        out[key] = {"pose": pose, "ms": ms, "loop": loop, "events": c["events"], "strike": strike}
    for key, (pose, ms, pct) in EXTRA_CAST.get(char, {}).items():
        out[key] = {"pose": pose, "ms": ms, "loop": False, "events": [{"name": "release", "time_pct": pct}], "strike": pct}
    return out


def clip_name(pose: str, ms: int, strike: float | None) -> str:
    return f"{pose}_{ms}" + (f"_{round(strike * 1000):03d}" if strike is not None else "")


def clip_table(char: str) -> dict[str, dict]:
    """去重后的片段表：片段名 -> {pose, ms, loop, kind, strike}。"""
    table: dict[str, dict] = {}
    for key, c in keys_for(char).items():
        pose = c["pose"]
        if pose in ("idle", "idle_combat", "run", "sprint") and c["loop"]:
            kind = "loop"
            strike = None
        elif c["strike"] is not None and pose not in ("dodge",):
            kind = "attack"
            strike = c["strike"]
        else:
            kind = "react"
            strike = None
        name = clip_name(pose, c["ms"], strike)
        table.setdefault(name, {"pose": pose, "ms": c["ms"], "loop": c["loop"], "kind": kind, "strike": strike})
    return table


def static_clip(char: str) -> str:
    """精灵集静态图取自的片段名（标准待机键对应的片段）。"""
    c = keys_for(char)["idle"]
    return clip_name(c["pose"], c["ms"], None)
