#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 3D 演示场景（ADR-0158）的模型包规格与显示数据行。

单一来源：本文件里的 CHARACTERS。它同时产出两份入库文件，二者由 toolchain/tests/test_showcase3d_assets.py 逐字节核对：

* ``assets/_showcase/models/showcase3d_models.json`` —— 模型包规格（给引擎编辑器侧的模型包生成器读：FBX、贴图、材质、
  状态剪辑 id -> 包内剪辑名、循环/倒放、目标身高、朝向偏移）；
* ``data/_showcase_3d/display/display.anim_set.json`` 与 ``display.weapon_style.json`` —— 与 2D 演示场景同构的显示数据行
  （动画集的键与事件取自框架标准假人 model 动画集同名键；武器风格行按技能 id 指向施法剪辑）。

    python build_data_3d.py            # 写出
    python build_data_3d.py --check    # 只核对，不一致退出 1

美术来源：Quaternius "RPG Character Pack"（CC0，https://quaternius.com/packs/rpgcharacters.html，2020-11），见 assets/_showcase/models/SOURCE.md。
包里没有击退/击倒/起身/闪避专用剪辑：击退取重受击，击倒取死亡倒地，起身取死亡倒放（编辑器侧由同一份死亡剪辑确定性派生），闪避取翻滚；
这些映射在 LIMITATIONS 里逐条列出，并写进规格文件的 limitations 字段。
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent.parent
SPEC_OUT = REPO / "assets" / "_showcase" / "models" / "showcase3d_models.json"
ROWS_DIR = REPO / "data" / "_showcase_3d" / "display"
STD_ANIM_SET = REPO / "data" / "_framework" / "display" / "display.anim_set.json"
STD_ROW_ID = "display.anim_set.std_dummy_biped_model"
PACK = "quaternius_rpg"

# 包内剪辑名前缀（FBX 的动画栈名都是 "CharacterArmature|<名>"）。
ARM = "CharacterArmature|"

# 逐角色定义：clips = 剪辑 id 后缀 -> (包内剪辑名, 是否循环, 是否倒放, 默认混合毫秒)；keys = 动画集键 -> 剪辑 id 后缀。
CHARACTERS = {
    "hero": {
        "pack_name": "Warrior",
        "textures": ["Warrior_Texture", "Warrior_Sword_Texture"],
        "height": 1.7,
        "clips": {
            "idle": ("Idle_Weapon", True, False, 120),
            "idle_combat": ("Idle_Attacking", True, False, 120),
            "walk": ("Walk", True, False, 100),
            "run": ("Run_Weapon", True, False, 100),
            "combo1": ("Sword_AttackFast", False, False, 40),
            "combo2": ("Sword_Attack", False, False, 40),
            "combo3": ("Sword_Attack", False, False, 40),
            "slam": ("Sword_Attack", False, False, 40),
            "charge": ("Sword_Attack", False, False, 40),
            "lunge": ("Sword_AttackFast", False, False, 40),
            "jab": ("Punch", False, False, 40),
            "hit_light": ("RecieveHit", False, False, 30),
            "hit_heavy": ("RecieveHit_2", False, False, 30),
            "knockback": ("RecieveHit_2", False, False, 30),
            "knockdown": ("Death", False, False, 30),
            "getup": ("Death", False, True, 120),
            "death": ("Death", False, False, 80),
            "dodge": ("Roll", False, False, 60),
        },
        "keys": {
            "idle": "idle", "idle.combat": "idle_combat",
            "move": "run", "move.walk": "walk", "move.walk.combat": "walk",
            "move.run": "run", "move.run.combat": "run", "move.sprint": "run", "move.sprint.combat": "run",
            "attack": "combo1", "attack.air": "combo1", "cast": "slam",
            "cast.quick": "combo1", "cast.combo2": "combo2", "cast.combo3": "combo3", "cast.slam": "slam", "cast.charge": "charge",
            "cast.heavy": "slam",
            "hit": "hit_light", "hit.light": "hit_light", "hit.heavy": "hit_heavy", "hit.knockback": "knockback",
            "hit.knockdown": "knockdown", "hit.getup": "getup", "death": "death", "dodge": "dodge",
        },
        # 技能 id -> 施法剪辑 id 后缀（武器风格行的 cast_anim_override；所有单位共用同一行，各单位只会用到自己的技能）。
        "skills": {
            "skill.lab_a_combo1": "combo1", "skill.lab_a_combo2": "combo2", "skill.lab_a_combo3": "combo3",
            "skill.lab_a_slam": "slam", "skill.lab_a_charge": "charge", "skill.lab_a_poise_chip": "combo1",
            "skill.lab_a_lunge": "lunge", "skill.lab_spx_jab": "jab", "skill.lab_a_dodge": "dodge",
        },
    },
    "grunt": {
        "pack_name": "Rogue",
        "textures": ["Rogue_Texture", "Rogue_Dagger_Texture"],
        "height": 1.4,
        "clips": {
            "idle": ("Idle", True, False, 120),
            "idle_combat": ("Attacking_Idle", True, False, 120),
            "walk": ("Walk", True, False, 100),
            "run": ("Run", True, False, 100),
            "stab": ("Dagger_Attack", False, False, 40),
            "stab2": ("Dagger_Attack2", False, False, 40),
            "hit_light": ("RecieveHit", False, False, 30),
            "hit_heavy": ("RecieveHit_2", False, False, 30),
            "knockback": ("RecieveHit_2", False, False, 30),
            "knockdown": ("Death", False, False, 30),
            "getup": ("Death", False, True, 120),
            "death": ("Death", False, False, 80),
            "dodge": ("Roll", False, False, 60),
        },
        "keys": {
            "idle": "idle", "idle.combat": "idle_combat",
            "move": "run", "move.walk": "walk", "move.walk.combat": "walk",
            "move.run": "run", "move.run.combat": "run", "move.sprint": "run", "move.sprint.combat": "run",
            "attack": "stab", "cast": "stab2",
            "hit": "hit_light", "hit.light": "hit_light", "hit.heavy": "hit_heavy", "hit.knockback": "knockback",
            "hit.knockdown": "knockdown", "hit.getup": "getup", "death": "death", "dodge": "dodge",
        },
        "skills": {},
    },
    "brute": {
        "pack_name": "Cleric",
        "textures": ["Cleric_Texture", "Cleric_Staff_Texture"],
        "height": 2.05,
        "clips": {
            "idle": ("Idle_Weapon", True, False, 120),
            "idle_combat": ("Attack_Idle", True, False, 120),
            "walk": ("Walk", True, False, 100),
            "run": ("Run_Weapon", True, False, 100),
            "swing": ("Staff_Attack", False, False, 40),
            "hit_light": ("RecieveHit", False, False, 30),
            "hit_heavy": ("RecieveHit_Attacking", False, False, 30),
            "knockback": ("RecieveHit_Attacking", False, False, 30),
            "knockdown": ("Death", False, False, 30),
            "getup": ("Death", False, True, 120),
            "death": ("Death", False, False, 80),
            "dodge": ("Roll", False, False, 60),
        },
        "keys": {
            "idle": "idle", "idle.combat": "idle_combat",
            "move": "run", "move.walk": "walk", "move.walk.combat": "walk",
            "move.run": "run", "move.run.combat": "run", "move.sprint": "run", "move.sprint.combat": "run",
            "attack": "swing", "attack.air": "swing", "cast": "swing", "cast.quick": "swing", "cast.heavy": "swing",
            "hit": "hit_light", "hit.light": "hit_light", "hit.heavy": "hit_heavy", "hit.knockback": "knockback",
            "hit.knockdown": "knockdown", "hit.getup": "getup", "death": "death", "dodge": "dodge",
        },
        "skills": {"skill.lab_a_elite_swing": "swing"},
    },
    "dummy": {
        "pack_name": "Monk",
        "textures": ["Monk_Texture"],
        "height": 1.6,
        "clips": {
            "idle": ("Idle", True, False, 120),
            "hit_light": ("RecieveHit", False, False, 30),
            "hit_heavy": ("RecieveHit_Attacking", False, False, 30),
            "death": ("Death", False, False, 80),
        },
        # 木桩不走不打：除受击与死亡外一律待机（与 2D 演示的木桩一致）。
        "keys": {
            "idle": "idle", "move": "idle", "attack": "idle", "cast": "idle",
            "hit": "hit_light", "hit.light": "hit_light", "hit.heavy": "hit_heavy", "hit.knockback": "hit_heavy",
            "hit.knockdown": "hit_heavy", "hit.getup": "idle", "death": "death",
        },
        "skills": {},
    },
}

# 编辑器侧朝向偏移：包里的模型默认面向 +Z，框架 3D 渲染器把朝向 0 放在 +X（绕 Y 轴旋转 -facing），所以预制体里加一层 +90 度的朝向偏移。
# upright：预制体里放 ModelGroundUpright 枢轴，让模型站在"地面 = 世界 XY 平面、相机在 -Z 一侧"的地面上（见该组件的判断记录）；
# 身高 target_height 是站直后的世界单位高度（固定俯角相机下屏幕上只占 sin(俯角) 倍，所以数值比场景里"看上去的"大一些）。
YAW_OFFSET_DEGREES = 90.0

LIMITATIONS = [
    "包里没有击退专用剪辑：hit.knockback 取重受击（hero/grunt：RecieveHit_2；brute/dummy：RecieveHit_Attacking）。",
    "包里没有击倒与起身剪辑：hit.knockdown 取死亡倒地剪辑（Death，不循环，停在倒地姿势），hit.getup 取同一份死亡剪辑的确定性倒放（编辑器侧派生，不改包内文件）。",
    "包里没有闪避剪辑：dodge 取翻滚（Roll）。木桩（Monk）不声明闪避。",
    "包里只有两种挥剑（Sword_AttackFast / Sword_Attack）：连招第三段与重击、蓄力取 Sword_Attack，三个技能各有独立剪辑 id 但画面相同。",
    "冲刺（move.sprint）与奔跑共用同一份跑步剪辑；包里没有受伤姿态与持械变体，idle.combat 取各角色自带的战斗待机。",
    "模型没有专门的击杀倒地后淡出：死亡剪辑停在最后一帧，尸体由逻辑清场移除。",
]


def _std_events() -> dict:
    data = json.loads(STD_ANIM_SET.read_text(encoding="utf-8"))
    row = next(r for r in data["rows"] if r["id"] == STD_ROW_ID)
    return {k: v["events"] for k, v in row["clips"].items()}


def _events_for(key: str, std: dict) -> list:
    """动画集键的事件：取框架标准假人 model 动画集同名键；没有同名键时按键族回落；出手/施放点旁补一个 hit_frame（3D 渲染器只认它）。"""
    if key in std:
        events = [dict(e) for e in std[key]]
    elif key.startswith("cast"):
        events = [dict(e) for e in std["cast"]]
    elif key.startswith("attack"):
        events = [dict(e) for e in std["attack"]]
    elif key == "move":
        events = [dict(e) for e in std["move.run"]]
    else:
        events = []
    names = {e["name"] for e in events}
    anchor = next((e for e in events if e["name"] in ("hit", "release")), None)
    if anchor is not None and "hit_frame" not in names:
        events.append({"name": "hit_frame", "time_pct": anchor["time_pct"]})
    return events


def model_id(look: str) -> str:
    return f"show3d_{look}"


def clip_id(look: str, suffix: str) -> str:
    return f"show3d_{look}_{suffix}"


def build_spec() -> dict:
    models = []
    for look, c in CHARACTERS.items():
        clips = []
        for suffix, (source, loop, reverse, _blend) in c["clips"].items():
            clips.append({"id": clip_id(look, suffix), "source": ARM + source, "loop": loop, "reverse": reverse})
        models.append({
            "id": model_id(look),
            "fbx": f"{PACK}/{c['pack_name']}.fbx",
            "target_height": c["height"],
            "yaw_offset_degrees": YAW_OFFSET_DEGREES,
            "upright": True,
            "materials": [{"name": t, "texture": f"{PACK}/{t}.png"} for t in c["textures"]],
            "default_clip": clip_id(look, "idle"),
            "clips": clips,
        })
    return {
        "schema": "gf.model_pack.v1",
        "pack": "Quaternius RPG Character Pack (CC0)",
        "shader": "ShowcaseModelLit.shader",
        "limitations": LIMITATIONS,
        "models": models,
    }


def build_rows() -> tuple[dict, dict]:
    std = _std_events()
    anim_rows = []
    for look, c in CHARACTERS.items():
        clips = {}
        for key, suffix in c["keys"].items():
            blend = c["clips"][suffix][3]
            clips[key] = {"resource_ref": f"anim.{clip_id(look, suffix)}", "events": _events_for(key, std), "blend_ms": blend}
        anim_rows.append({"id": f"display.anim_set.{model_id(look)}", "clips": clips})
    cast = {}
    for look, c in CHARACTERS.items():
        for skill, suffix in c["skills"].items():
            cast[skill] = f"anim.{clip_id(look, suffix)}"
    weapon_rows = [{
        "id": "display.weapon_style.show_model",
        "auto_attack_anim": f"anim.{clip_id('hero', 'combo1')}",
        "cast_anim_override": cast,
    }]
    return (
        {"table": "display.anim_set", "schema_version": 1, "rows": anim_rows},
        {"table": "display.weapon_style", "schema_version": 1, "rows": weapon_rows},
    )


def _dump(obj: dict) -> str:
    return json.dumps(obj, ensure_ascii=False, indent=2) + "\n"


def outputs() -> dict:
    anim, weapon = build_rows()
    return {
        SPEC_OUT: _dump(build_spec()),
        ROWS_DIR / "display.anim_set.json": _dump(anim),
        ROWS_DIR / "display.weapon_style.json": _dump(weapon),
    }


def main(argv: list) -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except (AttributeError, OSError):
        pass
    check = "--check" in argv
    bad = 0
    for path, text in outputs().items():
        if check:
            current = path.read_text(encoding="utf-8") if path.exists() else None
            if current != text:
                print(f"不一致：{path.relative_to(REPO)}")
                bad += 1
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
            print(f"写出：{path.relative_to(REPO)}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
