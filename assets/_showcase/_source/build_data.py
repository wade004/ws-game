#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成演示场景的数据行（data/_showcase/display）：每个角色一份动画集（键与事件取自框架标准假人动画集同名键）+ 一行武器风格（按技能 id 指向施法剪辑）。

    python build_data.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import showcase_poses as P  # noqa: E402

REPO = P.REPO
OUT = REPO / "data" / "_showcase" / "display"

CAST_OVERRIDE_HERO = {
    "skill.lab_a_combo1": "cast.quick",
    "skill.lab_a_combo2": "cast.combo2",
    "skill.lab_a_combo3": "cast.combo3",
    "skill.lab_a_slam": "cast.slam",
    "skill.lab_a_charge": "cast.charge",
    "skill.lab_a_poise_chip": "cast.quick",
    "skill.lab_a_lunge": "cast.quick",
    "skill.lab_spx_jab": "cast.quick",
}
CAST_OVERRIDE_BRUTE = {"skill.lab_a_elite_swing": "cast.heavy"}


def anim_row(char: str) -> dict:
    keys = P.keys_for(char)
    clips = {}
    for key, c in keys.items():
        name = P.clip_name(c["pose"], c["ms"], c["strike"] if (c["strike"] is not None and c["pose"] != "dodge") else None)
        clips[key] = {"resource_ref": f"sprite_anim.show_{char}_{name}", "events": c["events"]}
    # 框架默认动画状态键（idle/move/attack/cast/hit/death）都要有声明，缺了会被记成"退化为单帧"：
    # 无后缀 move 取跑步剪辑；这个外形压根没有的动作（小怪不出手、木桩不动不死）按"待机"声明——这些单位在实验室里也不会进入那些状态。
    if "move" not in clips and "move.run" in clips:
        clips["move"] = dict(clips["move.run"])
    for key in ("idle", "move", "attack", "cast", "hit", "death"):
        if key not in clips and "idle" in clips:
            clips[key] = dict(clips["idle"], events=[])
    return {"id": f"display.anim_set.show_{char}", "clips": clips}


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    rows = [anim_row(c) for c in ("hero", "grunt", "brute")]
    rows.append({
        "id": "display.anim_set.show_dummy",
        "clips": {k: {"resource_ref": "sprite_anim.show_dummy_idle", "events": []} for k in ("idle", "move", "attack", "cast", "hit", "death")},
    })
    doc = {"table": "display.anim_set", "schema_version": 1, "rows": rows}
    (OUT / "display.anim_set.json").write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")

    hero = anim_row("hero")["clips"]
    brute = anim_row("brute")["clips"]
    overrides = {}
    for skill, key in CAST_OVERRIDE_HERO.items():
        overrides[skill] = hero[key]["resource_ref"]
    for skill, key in CAST_OVERRIDE_BRUTE.items():
        overrides[skill] = brute[key]["resource_ref"]
    ws = {
        "table": "display.weapon_style",
        "schema_version": 1,
        "rows": [{
            "id": "display.weapon_style.show_sprite",
            "auto_attack_anim": hero["attack"]["resource_ref"],
            "cast_anim_override": overrides,
        }],
    }
    (OUT / "display.weapon_style.json").write_text(json.dumps(ws, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print("rows", len(rows), "overrides", len(overrides))
    return 0


if __name__ == "__main__":
    sys.exit(main())
