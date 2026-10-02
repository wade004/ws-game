"""自检：必备键 × 全方向档、事件标记（04 §8）、帧数规则、循环连续、步幅、画布裁切、数据行与规格一致。

退出码语义：有任一"错误"即失败；"警告"（推荐键缺项、清单外残留文件）只打印。
"""

from __future__ import annotations

import json
import math
import re
from pathlib import Path

from PIL import Image, ImageDraw

from . import config as C
from .build import SPEC_FILE, clip_dir, event_pct
from .poses import pose_at
from .skeleton import POSE_KEYS, build_parts, solve_ground

# 04 §3 清单的独立判据（不从 config 推导，避免"自己验自己"）：
REQUIRED_KEYS = ["idle", "move.walk", "move.run", "attack", "hit", "death",
                 "attack.unarmed", "attack.1h", "attack.2h",
                 "attack.polearm", "attack.bow", "attack.staff", "attack.dual", "attack.shield"]
RECOMMENDED_KEYS = ["idle.combat", "move.run.combat", "attack.unarmed.02", "attack.1h.02", "attack.2h.02",
                    "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup", "cast", "dodge", "jump",
                    "attack.polearm.02", "attack.staff.02", "attack.dual.02"]
# 04 §3 可选键（独立判据）：04 说"缺项静默回落"，但框架假人集声明"出齐"，所以本集缺任一项都按错误报。
OPTIONAL_KEYS = ["move.sprint", "move.walk.combat", "move.start", "move.stop", "move.pivot", "hit.launch",
                 "stunned", "block", "idle.wounded", "move.walk.wounded"]
# 04 §2 武器族清单里 1h/2h 之外的全部族，每族都要有的键口径（与 1h/2h 同口径 + 战斗走与冲刺）。
FAMILY_KEY_BASES = ["idle", "idle.combat", "move.walk", "move.run", "move.run.combat", "move.walk.combat", "move.sprint"]
ALL_FAMILIES = ["1h", "2h", "polearm", "bow", "staff", "dual", "shield"]
# 手感落地 M4-D 追加的键（独立判据）：空中键、格挡受击/眩晕摇晃/击飞翻滚落地、带伤变体覆盖全部移动与战斗移动键。
AIR_KEYS = ["jump.rise", "jump.fall", "jump.land", "hit.air", "attack.air"] + \
    [f"attack.air.{f}" for f in ["unarmed"] + ALL_FAMILIES]
DETAIL_KEYS = ["hit.block", "hit.block.shield", "stunned.sway", "hit.launch.tumble", "hit.launch.land"]
WOUNDED_KEYS = ["idle.wounded", "idle.combat.wounded", "move.walk.wounded", "move.run.wounded",
                "move.walk.combat.wounded", "move.run.combat.wounded", "move.sprint.wounded",
                "move.sprint.combat.wounded", "move.start.wounded", "move.stop.wounded", "move.pivot.wounded"]
# 体量档至少轻/中/重三档（中 = 主集）：独立于 config 的下限。
REQUIRED_MASS_TIERS = ("light", "heavy")
# 五个新族的程序化低多边形武器形体（M4-D）：零件数下限与"待机战斗姿势下武器总长"下限（身高倍数，独立判据）。
WEAPON_SHAPE_MIN = {"polearm": (5, 0.9), "bow": (9, 0.5), "staff": (6, 0.8), "dual": (5, 0.5), "shield": (5, 0.55)}
# 体量偏移之外还随体量缩放（受击反应倍率/抬升倍率）的姿势 id：这些键在体量组里只要求"与主集不同"，其余键要求只差静态偏移。
MASS_SCALED_POSE_IDS = {"hit", "hit.light", "hit.heavy", "hit.knockback", "hit.knockdown", "death", "hit.launch",
                        "jump", "hit_air", "hit_block"}
_CIRCULAR = {"bp": 360.0}

# 武器层逐层剪辑清单的独立判据（不从 config 推导）：持械角色播这些无族状态键时武器层必须随身体帧走
# （ADR-0072：缺逐层剪辑的层维持静态图，这正是要消除的遗留）。
STATE_KEYS_WITH_WEAPON_LAYER = ["hit", "hit.light", "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup",
                                "death", "jump", "cast", "dodge", "hit.launch", "stunned", "block",
                                "jump.rise", "jump.fall", "jump.land", "hit.air", "hit.block", "hit.block.shield",
                                "stunned.sway", "hit.launch.tumble", "hit.launch.land"]

_STATES = {"idle", "move", "attack", "cast", "hit", "death", "jump", "dodge", "stunned", "block"}
_JUMP_SUB = {"rise", "fall", "land"}
_LAUNCH_SUB = {"tumble", "land"}
_GAITS = {"walk", "run", "sprint"}
_FAMILIES = {"unarmed", "1h", "2h", "polearm", "bow", "staff", "dual", "shield"}
_HIT_SUFFIX = {"light", "heavy", "knockback", "knockdown", "getup", "launch", "air", "block"}
_TRANSITIONS = {"start", "stop", "pivot"}
_VARIANTS = {"wounded"}


def parse_key(key: str) -> dict | None:
    """04 §2.1：<state>[.<gait>][.<stance>][.<family>][.<variant>]；变体位这里只认攻击段号（两位数字）与 hit 的后缀。"""
    parts = key.split(".")
    if parts[0] not in _STATES:
        return None
    out = {"state": parts[0], "gait": None, "stance": None, "family": None, "variant": None}
    rest = parts[1:]
    if parts[0] == "hit" and rest and rest[0] in _HIT_SUFFIX:
        out["variant"] = rest[0]
        rest = rest[1:]
        if out["variant"] == "launch" and rest and rest[0] in _LAUNCH_SUB:
            out["variant"] = "launch." + rest[0]     # 击飞翻滚/落地缓冲（M4-D）
            rest = rest[1:]
    if parts[0] == "jump" and rest and rest[0] in _JUMP_SUB:
        out["variant"] = rest[0]                     # 空中键 jump.rise/fall/land（M4-D）
        return out if len(rest) == 1 else None
    if parts[0] == "stunned" and rest and rest[0] == "sway":
        out["variant"] = "sway"                      # 眩晕摇晃循环（M4-D）
        return out if len(rest) == 1 else None
    if parts[0] == "attack" and rest and rest[0] == "air":
        out["air"] = True                            # 空中攻击 attack.air[.<族>]（M4-D）：回落链 attack.air.<族> -> attack.<族>
        rest = rest[1:]
        if rest and rest[0] in _FAMILIES:
            out["family"] = rest[0]
            rest = rest[1:]
        return out if not rest else None
    if parts[0] == "move" and rest and rest[0] in _TRANSITIONS:
        out["variant"] = rest[0]       # 启停过渡剪辑（04 §3）：move.start/stop/pivot，不带其它维度；可带 .wounded（M4-D）
        if rest[1:] == ["wounded"]:
            out["wounded"] = True
            return out
        return out if len(rest) == 1 else None
    if parts[0] == "move" and rest and rest[0] in _GAITS:
        out["gait"] = rest[0]
        rest = rest[1:]
    if rest and rest[0] == "combat":
        out["stance"] = "combat"
        rest = rest[1:]
    if rest and rest[0] in _FAMILIES:
        out["family"] = rest[0]
        rest = rest[1:]
    if rest and (re.fullmatch(r"\d\d", rest[0]) or rest[0] in _VARIANTS):
        out["variant"] = rest[0]
        rest = rest[1:]
    return out if not rest else None


class Report:
    def __init__(self):
        self.errors: list[str] = []
        self.warnings: list[str] = []
        self.counts: dict[str, int] = {}

    def err(self, msg):
        self.errors.append(msg)

    def warn(self, msg):
        self.warnings.append(msg)

    def tick(self, name, n=1):
        self.counts[name] = self.counts.get(name, 0) + n


def _load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def _ev(clip_spec: dict, name: str) -> list[float]:
    return [e["time_pct"] for e in clip_spec["events"] if e["name"] == name]


def verify(assets_out: Path, data_out: Path, deep_images: bool = True) -> Report:
    r = Report()
    spec_path = assets_out / SPEC_FILE
    if not spec_path.is_file():
        r.err(f"缺规格文件 {spec_path}")
        return r
    spec = _load(spec_path)
    clips = spec["clips"]
    params = spec["params"]
    fps = params["fps"]
    slots = spec["directions"]["canonical"]
    dc = params["direction_count"]

    # --- 方向档与既有导入工具的命名一致 ---
    try:
        from asset_import.directions import all_slot_names, canonical_slot_names, mirror_slot_name  # type: ignore
        if canonical_slot_names(dc) != slots:
            r.err(f"canonical 档位与 asset_import.directions 不一致：{slots} vs {canonical_slot_names(dc)}")
        mirrors = [m["direction_slot"] for m in spec["directions"]["mirror_pairs"]]
        if sorted(slots + mirrors) != sorted(all_slot_names(dc)):
            r.err("canonical + 镜像档位集合与 all_slot_names 不一致")
        for m in spec["directions"]["mirror_pairs"]:
            if mirror_slot_name(m["mirror_of"]) != m["direction_slot"]:
                r.err(f"镜像命名不一致：{m}")
        r.tick("方向档命名核对")
    except ImportError:
        r.warn("未能导入 asset_import.directions，跳过方向档命名核对（需要 toolchain 在 sys.path）")
    if len(slots) + len(spec["directions"]["mirror_pairs"]) != dc:
        r.err(f"方向档总数 {len(slots)}+{len(spec['directions']['mirror_pairs'])} != direction_count {dc}")

    # --- 04 §3 键清单 ---
    for k in REQUIRED_KEYS:
        if k not in clips:
            r.err(f"必备键缺失：{k}")
    for k in RECOMMENDED_KEYS:
        if k not in clips:
            r.warn(f"推荐键缺失：{k}")
    for k in OPTIONAL_KEYS:
        if k not in clips:
            r.err(f"可选键缺失（本集声明出齐）：{k}")
    for k in AIR_KEYS + DETAIL_KEYS + WOUNDED_KEYS:
        if k not in clips:
            r.err(f"M4-D 键缺失（本集声明出齐）：{k}")
    for fam in ALL_FAMILIES:
        for base in FAMILY_KEY_BASES:
            k = f"{base}.{fam}"
            if k not in clips:
                r.err(f"武器族 {fam} 缺键：{k}")
        for k in (f"attack.{fam}", f"move.sprint.combat.{fam}"):
            if k not in clips:
                r.err(f"武器族 {fam} 缺键：{k}")
    for k in clips:
        if parse_key(k) is None:
            r.err(f"键不符合 04 §2.1 语法：{k}")
    r.tick("键语法与清单", len(clips))
    # 带伤变体覆盖：徒手基础族的每个待机/移动键（含冲刺、战斗走/跑、启停过渡）都要有 .wounded 对应键
    for k in list(clips):
        i = parse_key(k)
        if i and i["state"] in ("idle", "move") and i["family"] is None and i["variant"] in (None, "start", "stop", "pivot") \
                and not i.get("wounded") and f"{k}.wounded" not in clips:
            r.err(f"带伤变体未覆盖 {k}（缺 {k}.wounded）")
    for k in STATE_KEYS_WITH_WEAPON_LAYER:
        if k in clips and C.LAYER_WEAPON not in clips[k].get("layers", []):
            r.err(f"{k} 缺武器层（{C.LAYER_WEAPON}）逐层剪辑：持械角色播放时武器层会是静态图，不随身体帧走")

    # --- 数据行与规格一致 ---
    data_path = data_out / "display" / "display.anim_set.json"
    if not data_path.is_file():
        r.err(f"缺数据文件 {data_path}")
    else:
        doc = _load(data_path)
        rows = [x for x in doc.get("rows", []) if x.get("id") == spec["anim_set_id"]]
        if len(rows) != 1:
            r.err(f"数据文件缺行 {spec['anim_set_id']}")
        else:
            row = rows[0]
            if not spec["anim_set_id"].startswith("display.anim_set.std_dummy_"):
                r.err("anim_set id 前缀不是 display.anim_set.std_dummy_")
            if set(row["clips"]) != set(clips):
                r.err(f"数据行键集合与规格不一致：差 {sorted(set(row['clips']) ^ set(clips))}")
            for k, v in row["clips"].items():
                if k in clips and (v["resource_ref"] != clips[k]["resource_ref"] or v["events"] != clips[k]["events"]):
                    r.err(f"数据行与规格不一致：{k}")
            r.tick("数据行核对", len(row["clips"]))
            _verify_mass_tiers(r, spec, doc, assets_out, fps, deep_images)

    # --- 逐键：事件、帧数规则、循环连续、步幅 ---
    defs = {c.key: c for c in C.build_clip_defs()}
    for key, e in clips.items():
        c = defs.get(key)
        if c is None:
            r.err(f"规格里有 config 没有的键 {key}")
            continue
        total = e["total_ms"]
        plan = C.phase_frame_plan(c, fps)
        if e["frames_per_phase"] != [n for _a, n, _b in plan] or e["frame_count"] != sum(n for _a, n, _b in plan):
            r.err(f"{key} 帧数与参数推算不一致")
        for ev in e["events"]:
            if not (0.0 <= ev["time_pct"] <= 1.0):
                r.err(f"{key} 事件 {ev['name']} time_pct 越界 {ev['time_pct']}")
        info = parse_key(key)
        if e.get("weapon_layer_family") != c.weapon_layer_family:
            r.err(f"{key} 规格的 weapon_layer_family={e.get('weapon_layer_family')!r} 与 config 的 {c.weapon_layer_family!r} 不一致")
        if info["state"] == "attack" and key != "dodge":
            a0, a1, hits = _ev(e, "active_start"), _ev(e, "active_end"), _ev(e, "hit")
            if len(a0) != 1 or len(a1) != 1 or not hits:
                r.err(f"{key} 攻击类缺 active_start/active_end/hit")
            elif not (a0[0] < a1[0]) or any(not (a0[0] <= h <= a1[0]) for h in hits):
                r.err(f"{key} hit 不在判定相内：active=[{a0[0]},{a1[0]}] hit={hits}")
            hf = _ev(e, "hit_frame")
            if sorted(hf) != sorted(hits):
                r.err(f"{key} 命中帧事件 hit_frame {hf} 与 hit {hits} 不同刻（角色外壳按 hit_frame 同步命中帧，ADR-0017）")
            if e["family"] == "bow" and sorted(_ev(e, "release")) != sorted(hits):
                r.err(f"{key} 弓的 release 应与 hit 同刻（放箭点）")
            w, a, rr = (p["ms"] for p in e["phases"])
            exp = {"active_start": event_pct(w, total), "active_end": event_pct(w + a, total)}
            if (a0 and abs(a0[0] - exp["active_start"]) > 1e-4) or (a1 and abs(a1[0] - exp["active_end"]) > 1e-4):
                r.err(f"{key} 判定相标记与三相毫秒数不一致")
            fam = e["family"]
            if info.get("air"):
                ground = clips.get(f"attack.{fam}")
                if ground is None or e["phases"] != ground["phases"]:
                    r.err(f"{key} 空中攻击的三相应与同族地面第一段一致")
                bad = [x["name"] for x in e["events"] if x["name"].startswith(("combo_", "cancel_open"))]
                if bad:
                    r.err(f"{key} 空中攻击不应带连招/闪避取消事件：{bad}")
            if fam in C.PHASES_SEG1 and e["phases"][0]["ms"] and key == C.attack_key(fam, 1):
                if tuple(p["ms"] for p in e["phases"]) != C.PHASES_SEG1[fam]:
                    r.err(f"{key} 三相与配置起点不一致")
        if info["state"] == "move" and info["gait"] in ("walk", "run", "sprint"):
            fs = _ev(e, "footstep")
            if len(fs) < 2:
                r.err(f"{key} 缺 footstep（至少 2 次/循环）")
            if "step_displacement_bh" not in e:
                r.err(f"{key} 缺每步位移")
        if c.transition:
            # 启停过渡：非循环、至少一次脚触地；首尾姿势与前后相邻的循环剪辑衔接（起点/终点姿势完全相同）
            if e["loop"] or not _ev(e, "footstep"):
                r.err(f"{key} 过渡剪辑应为非循环且有 footstep")
            w = ".wounded" if c.variant == "wounded" else ""
            idle0, run0 = pose_at(C.clip_by_key("idle" + w), 0.0), pose_at(C.clip_by_key("move.run" + w), 0.0)
            want_a, want_b = {"start": (idle0, run0), "stop": (run0, idle0), "pivot": (run0, run0)}[c.transition]
            for tag, got, want in (("起点", pose_at(c, 0.0), want_a), ("终点", pose_at(c, float(total)), want_b)):
                dm = pose_diff(got, want)
                if dm > 1e-6:
                    r.err(f"{key} {tag}姿势与相邻循环剪辑不衔接（最大差 {dm:.6f}）")
        if key == "dodge":
            s0, s1 = _ev(e, "invuln_start"), _ev(e, "invuln_end")
            if len(s0) != 1 or len(s1) != 1 or not (0.0 <= s0[0] < s1[0] <= 1.0):
                r.err("dodge 无敌窗口标记缺失或越界")
            else:
                m0, m1 = _ev(e, "motion_start"), _ev(e, "motion_end")
                if not m0 or not m1 or not (m0[0] <= s0[0] and s1[0] <= m1[0]):
                    r.err("dodge 无敌窗口应落在 motion 窗口内")
        if c.loop:
            p0, p1 = pose_at(c, 0.0), pose_at(c, float(total))
            dmax = pose_diff(p0, p1)
            if dmax > 1e-6:
                r.err(f"{key} 循环首尾姿势不连续（最大差 {dmax:.6f}）")
            r.tick("循环连续")
        if info["state"] == "move" and info["gait"] in ("walk", "run", "sprint") and not c.combat and c.family is None:
            # 接触姿势两脚前后间距 ≈ 标称每步位移（容差 5%；带伤变体按其步幅倍率）
            _parts, j = build_parts(pose_at(c, 0.0), None)
            sep = abs(j["ankle_m"][2] - j["ankle_o"][2])
            step = C.step_displacement_bh(info["gait"], c.stride_factor)
            if abs(sep - step) > 0.05 * step:
                r.err(f"{key} 接触姿势脚间距 {sep:.3f} 与每步位移 {step:.3f} 偏差超 5%")
            r.tick("步幅核对")

    _verify_m4d_poses(r, clips, defs)
    _verify_weapon_shapes(r)

    # --- 文件与图像 ---
    for key, e in clips.items():
        if e.get("alias_of"):
            if e["alias_of"] not in clips:
                r.err(f"{key} 别名目标 {e['alias_of']} 不在规格里")
            elif e["resource_ref"] != clips[e["alias_of"]]["resource_ref"]:
                r.err(f"{key} 别名资源引用与目标不一致")
            continue
        ref = e["resource_ref"]
        want = [("整身", clip_dir(assets_out, ref))]
        for slot in slots:
            if params["composite_direction_variants"]:
                want.append((f"整身/{slot}", clip_dir(assets_out, ref, slot)))
            for layer in e["layers"]:
                want.append((f"{layer}/{slot}", clip_dir(assets_out, ref, slot, layer)))
        for tag, d in want:
            _check_clip_dir(r, key, tag, d, e, fps, deep_images)

    # --- 清单外残留 ---
    root = assets_out / "sprite_anim"
    if root.is_dir():
        expected = set()
        for key, e in clips.items():
            if e.get("alias_of"):
                continue
            expected.add(clip_dir(assets_out, e["resource_ref"]).name)
            for slot in slots:
                if params["composite_direction_variants"]:
                    expected.add(clip_dir(assets_out, e["resource_ref"], slot).name)
                for layer in e["layers"]:
                    expected.add(clip_dir(assets_out, e["resource_ref"], slot, layer).name)
        for g in spec.get("mass_groups", []):
            for key, e in g["clips"].items():
                if not e.get("alias_of"):
                    expected.update(d.name for _t, d in _group_dirs(assets_out, e, slots, _mass_composite(spec)))
        for d in root.iterdir():
            if d.is_dir() and d.name.startswith(C.STEM_PREFIX) and d.name not in expected:
                r.warn(f"清单外残留目录：{d.name}")

    # --- 路径推导与既有契约一致 ---
    try:
        from asset_import.ref_conventions import sprite_anim_dir  # type: ignore
        for key, e in clips.items():
            if clip_dir(Path("."), e["resource_ref"]).as_posix() != "./" + sprite_anim_dir(e["resource_ref"]) and \
                    clip_dir(Path("."), e["resource_ref"]).as_posix() != sprite_anim_dir(e["resource_ref"]):
                r.err(f"{key} 目录与 ref_conventions.sprite_anim_dir 不一致")
        r.tick("路径契约核对")
    except ImportError:
        r.warn("未能导入 asset_import.ref_conventions，跳过路径契约核对")
    return r


def pose_diff(a: dict, b: dict) -> float:
    """两姿势的最大分量差；整身俯仰 bp 按 360 度取环（翻滚循环首尾差 360 度 = 同一姿势）。"""
    m = 0.0
    for k in POSE_KEYS:
        d = abs(a[k] - b[k])
        if k in _CIRCULAR:
            d = d % _CIRCULAR[k]
            d = min(d, _CIRCULAR[k] - d)
        m = max(m, d)
    return m


def _lowest_y(pose: dict, family: str | None = None) -> float:
    """姿势里身体层最低点的世界高度（含接地求解与抬升）；< 0 = 穿地。"""
    parts, _j = build_parts(pose, family)
    low = min(c[1] for p in parts if p.layer == "body" for c in p.corners)
    return low + solve_ground(pose, parts)


def _mass_composite(spec: dict) -> bool:
    """体量组是否写整身方向变体（M4-W5 起与主集同一开关 params.composite_direction_variants；规格 mass_tiers 里同值留痕，两处不一致报错）。"""
    return bool(spec["params"]["composite_direction_variants"])


def _group_dirs(assets_out: Path, e: dict, slots: list[str], composite: bool):
    """体量组一个剪辑键应有的资源目录：整身默认朝向 + 各 canonical 方向的整身方向变体（composite 时）与逐层剪辑。"""
    out = [("整身", clip_dir(assets_out, e["resource_ref"]))]
    for slot in slots:
        if composite:
            out.append((f"整身/{slot}", clip_dir(assets_out, e["resource_ref"], slot)))
        for layer in e["layers"]:
            out.append((f"{layer}/{slot}", clip_dir(assets_out, e["resource_ref"], slot, layer)))
    return out


def _verify_m4d_poses(r: Report, clips: dict, defs: dict) -> None:
    """M4-D 新键的姿势衔接与不变量：空中键首尾衔接、空中攻击不带抬升、格挡受击收回持握、眩晕摇晃幅度、翻滚整圈且不穿地。"""
    P = lambda k, t: pose_at(defs[k], t)  # noqa: E731
    T = lambda k: float(defs[k].total_ms)  # noqa: E731
    if not all(k in defs and k in clips for k in AIR_KEYS + DETAIL_KEYS):
        return
    fall0 = P("jump.fall", 0.0)
    for tag, got in (("jump.land 起点", P("jump.land", 0.0)), ("hit.air 起点", P("hit.air", 0.0)),
                     ("hit.air 终点", P("hit.air", T("hit.air")))):
        if pose_diff(got, fall0) > 1e-6:
            r.err(f"{tag}应与 jump.fall 起点姿势衔接（最大差 {pose_diff(got, fall0):.6f}）")
    for k in ("jump.rise", "jump.fall", "stunned.sway", "hit.launch.tumble"):
        if not clips[k]["loop"]:
            r.err(f"{k} 应为循环剪辑")
    if clips["jump.land"]["loop"] or [x for x in clips["jump.land"]["events"] if x["name"] == "footstep"] != \
            [{"name": "footstep", "time_pct": 0.0}]:
        r.err("jump.land 应为非循环、起点一次 footstep（落地触地）")
    if len(_ev(clips["stunned.sway"], "footstep")) != 2:
        r.err("stunned.sway 应有 2 次 footstep（踉跄落脚）")
    # 空中攻击：高度归逻辑竖直轴，剪辑自身不带 lift
    for k in [x for x in AIR_KEYS if x.startswith("attack.air")]:
        c = defs[k]
        lifts = [abs(pose_at(c, c.total_ms * i / 20.0)["lift"]) for i in range(21)]
        if max(lifts) > 0.0:
            r.err(f"{k} 不应带 lift（空中高度归逻辑竖直轴）")
    # 格挡受击：收回到格挡持握的起点姿势（接 block 循环）
    for hk, bk in (("hit.block", "block"), ("hit.block.shield", "block.shield")):
        for tag, t in (("起点", 0.0), ("终点", T(hk))):
            if pose_diff(P(hk, t), P(bk, 0.0)) > 1e-6:
                r.err(f"{hk} {tag}应等于 {bk} 起点姿势（最大差 {pose_diff(P(hk, t), P(bk, 0.0)):.6f}）")
    # 眩晕摇晃：躯干侧倾幅度要明显大于 stunned 的占位站晃
    rolls = [P("stunned.sway", T("stunned.sway") * i / 32.0)["t_roll"] for i in range(33)]
    if max(rolls) - min(rolls) < 15.0:
        r.err("stunned.sway 躯干侧倾幅度不足 15 度，不像摇晃")
    # 击飞翻滚：整身绕骨盆翻一整圈（bp 0 -> -360 单调），全程不穿地；落地起点接翻滚终点、终点接起身起点
    n = 40
    bps = [P("hit.launch.tumble", T("hit.launch.tumble") * i / n)["bp"] for i in range(n)]   # 循环剪辑：终点取样回绕到起点
    if abs(bps[0]) > 1e-9 or bps[-1] > -360.0 * (n - 1) / n + 1e-6 or any(b2 >= b1 for b1, b2 in zip(bps, bps[1:])):
        r.err("hit.launch.tumble 的整身俯仰 bp 应从 0 单调翻到 -360 度（一整圈，循环回绕）")
    for k in ("hit.launch.tumble", "hit.launch.land"):
        low = min(_lowest_y(P(k, T(k) * i / n)) for i in range(n + 1))
        if low < -1e-6:
            r.err(f"{k} 有姿势穿地（最低点 {low:.4f}）")
    if pose_diff(P("hit.launch.land", 0.0), P("hit.launch.tumble", T("hit.launch.tumble"))) > 1e-6:
        r.err("hit.launch.land 起点应接 hit.launch.tumble 终点（bp 按整圈取环）")
    if pose_diff(P("hit.launch.land", T("hit.launch.land")), P("hit.getup", 0.0)) > 1e-6:
        r.err("hit.launch.land 终点应接 hit.getup 起点（躺姿）")
    r.tick("M4-D 姿势衔接核对")


def _verify_weapon_shapes(r: Report) -> None:
    """五个新族武器形体是程序化低多边形（不是方块占位）：零件数与武器总长不低于下限，零件名与 weapons 登记一致。"""
    from . import weapons
    from .poses import pose_at as _pa
    for fam, (min_parts, min_len) in WEAPON_SHAPE_MIN.items():
        c = C.clip_by_key(f"idle.combat.{fam}")
        parts, _j = build_parts(_pa(c, 0.0), fam)
        wp = [p for p in parts if p.layer == "weapon"]
        names = [p.name for p in wp]
        if len(wp) < min_parts:
            r.err(f"武器族 {fam} 形体只有 {len(wp)} 个零件，低于 {min_parts}（仍是方块占位？）")
        if len(set(names)) != len(names):
            r.err(f"武器族 {fam} 零件名重复：{names}")
        if names != list(weapons.part_names(fam)):
            r.err(f"武器族 {fam} 零件名与 weapons.part_names 不一致：{names}")
        pts = [q for p in wp for q in p.corners]
        if not pts:
            continue
        ext = math.sqrt(sum((max(q[i] for q in pts) - min(q[i] for q in pts)) ** 2 for i in range(3)))
        if ext < min_len:
            r.err(f"武器族 {fam} 总长 {ext:.2f} 低于 {min_len}（轮廓不可辨认）")
        if any(not math.isfinite(v) for q in pts for v in q):
            r.err(f"武器族 {fam} 形体含非有限坐标")
    r.tick("武器形体核对", len(WEAPON_SHAPE_MIN))


def _verify_mass_tiers(r: Report, spec: dict, doc: dict, assets_out: Path, fps: int, deep: bool) -> None:
    """体量档（M4-D）：数据声明的多档、每档覆盖全部键、数据行 extends 主集、资源齐全、每键与主集真有差异且只差体量偏移。"""
    mt, groups = spec.get("mass_tiers"), spec.get("mass_groups")
    if not mt or not groups:
        r.err("规格缺 mass_tiers / mass_groups")
        return
    clips = spec["clips"]
    slots = spec["directions"]["canonical"]
    main_id = spec["anim_set_id"]
    rows = {x["id"]: x for x in doc.get("rows", [])}
    for t in REQUIRED_MASS_TIERS:
        if t not in mt["tiers"]:
            r.err(f"体量档缺 {t}（至少轻/中/重三档）")
    if [g["mass"] for g in groups] != list(mt["tiers"]):
        r.err("mass_groups 与 mass_tiers.tiers 的档不一致")
    if bool(mt.get("composite_direction_variants")) != _mass_composite(spec):
        r.err("mass_tiers.composite_direction_variants 应与 params.composite_direction_variants 一致（体量组与主集同一开关）")
    med = rows.get(C.mass_anim_set_id(main_id, mt["main"]))
    if med is None or med.get("extends") != main_id or med.get("clips"):
        r.err(f"中体量行（{mt['main']}）应为 extends 主集的空覆盖行")
    for g in groups:
        row = rows.get(g["id"])
        if row is None:
            r.err(f"数据文件缺体量组行 {g['id']}")
            continue
        if row.get("extends") != main_id:
            r.err(f"{g['id']} 应 extends {main_id}")
        if set(g["clips"]) != set(clips) or set(row["clips"]) != set(clips):
            r.err(f"{g['id']} 的键集合应与主集一致（覆盖全部键）：差 {sorted(set(g['clips']) ^ set(clips))[:6]}")
            continue
        for k, e in g["clips"].items():
            m = clips[k]
            want_ref = m["resource_ref"].replace("sprite_anim.std_dummy_", f"sprite_anim.std_dummy_{g['mass']}_", 1)
            if e["resource_ref"] != want_ref:
                r.err(f"{g['id']} {k} 资源引用 {e['resource_ref']} != {want_ref}")
            if e["events"] != m["events"] or e["frame_count"] != m["frame_count"] or e["total_ms"] != m["total_ms"] \
                    or e.get("alias_of") != m.get("alias_of"):
                r.err(f"{g['id']} {k} 时长/帧数/事件/别名应与主集一致（体量只改姿势）")
            rv = row["clips"][k]
            if rv["resource_ref"] != e["resource_ref"] or rv["events"] != e["events"]:
                r.err(f"{g['id']} {k} 数据行与规格不一致")
            if e.get("alias_of"):
                continue
            for tag, d in _group_dirs(assets_out, e, slots, _mass_composite(spec)):
                _check_clip_dir(r, f"{g['mass']}:{k}", tag, d, e, fps, deep)
            # 与主集有差异：同键同方向的身体层图集字节不同
            a = clip_dir(assets_out, e["resource_ref"], slots[0], C.LAYER_BODY) / "atlas.png"
            b = clip_dir(assets_out, m["resource_ref"], slots[0], C.LAYER_BODY) / "atlas.png"
            if a.is_file() and b.is_file() and a.read_bytes() == b.read_bytes():
                r.err(f"{g['id']} {k} 身体层与主集逐字节相同（体量偏移没有生效）")
        # 姿势层面：偏移只改 t_pitch/h_pitch/肩髋外展；受击/抬升类键另受反应与抬升倍率，只要求不同
        prof = mt["tiers"][g["mass"]]
        for gc in C.mass_clip_defs(g["mass"]):
            if gc.alias_of:
                continue
            mc = C.clip_by_key(gc.key)
            for t in (0.0, mc.total_ms / 2.0, float(mc.total_ms)):
                pg, pm = pose_at(gc, t), pose_at(mc, t)
                if gc.pose_id in MASS_SCALED_POSE_IDS:
                    continue
                for k in POSE_KEYS:
                    want = {"t_pitch": prof["t_pitch"], "h_pitch": prof["h_pitch"], "m_sa": prof["sa"], "o_sa": prof["sa"],
                            "m_ha": prof["ha"], "o_ha": prof["ha"]}.get(k, 0.0)
                    if abs((pg[k] - pm[k]) - want) > 1e-9:
                        r.err(f"{g['id']} {gc.key} 姿势 {k} 与主集差 {pg[k] - pm[k]:.4f}，应只差体量偏移 {want}")
                        break
    r.tick("体量组核对", len(groups))


def _distinct_frames(im: Image.Image, frames: list[dict]) -> int:
    """图集里内容互不相同的帧数（按帧矩形像素字节去重）。"""
    seen = set()
    for f in frames:
        seen.add(im.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])).tobytes())
    return len(seen)


def _check_clip_dir(r: Report, key: str, tag: str, d: Path, e: dict, fps: int, deep: bool) -> None:
    atlas, fj = d / "atlas.png", d / "frames.json"
    if not atlas.is_file() or not fj.is_file():
        r.err(f"{key} [{tag}] 缺 atlas.png/frames.json：{d.name}")
        return
    doc = _load(fj)
    frames = doc.get("frames", [])
    if len(frames) != e["frame_count"]:
        r.err(f"{key} [{tag}] 帧数 {len(frames)} != 推算 {e['frame_count']}")
    if doc.get("fps") != fps or bool(doc.get("loop")) != bool(e["loop"]):
        r.err(f"{key} [{tag}] frames.json 的 fps/loop 与规格不一致")
    fw, fh = C.CANVAS
    if doc.get("frame_w") != fw or doc.get("frame_h") != fh:
        r.err(f"{key} [{tag}] 帧尺寸不一致")
    total = sum(f.get("duration", 0.0) for f in frames) * 1000.0
    if abs(total - e["total_ms"]) > 0.5:
        r.err(f"{key} [{tag}] 时长合计 {total:.2f} ms != {e['total_ms']} ms")
    r.tick("资源目录", 1)
    if not deep:
        return
    with Image.open(atlas) as im:
        im.load()
        for f in frames:
            if f["x"] + f["w"] > im.width or f["y"] + f["h"] > im.height:
                r.err(f"{key} [{tag}] 帧矩形越出图集")
                return
        # 画布裁切：任何不透明像素触碰帧边缘即视为被裁切
        alpha = im.getchannel("A")
        for f in frames:
            box = (f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])
            fa = alpha.crop(box)
            w, h = fa.size
            px = fa.load()
            edge = [px[x, 0] for x in range(w)] + [px[x, h - 1] for x in range(w)] + \
                   [px[0, y] for y in range(h)] + [px[w - 1, y] for y in range(h)]
            if any(v > 0 for v in edge):
                r.err(f"{key} [{tag}] 第 {f['index']} 帧触碰画布边缘（被裁切）")
                break
        if "body" in tag or "整身" in tag:
            if alpha.getbbox() is None:
                r.err(f"{key} [{tag}] 图集全透明")
        if tag.startswith(C.LAYER_WEAPON + "/"):
            if alpha.getbbox() is None:
                r.err(f"{key} [{tag}] 武器层图集全透明")
            else:
                # 武器层随身体帧走：身体层有多于一种帧内容时，武器层也必须有多于一种（否则是"静态图重复 N 帧"）
                body_d = d.parent / d.name.replace("__" + C.LAYER_WEAPON, "__" + C.LAYER_BODY)
                weapon_distinct = _distinct_frames(im, frames)
                body_distinct = None
                if (body_d / "atlas.png").is_file():
                    with Image.open(body_d / "atlas.png") as bim:
                        bim.load()
                        body_distinct = _distinct_frames(bim, _load(body_d / "frames.json").get("frames", []))
                if body_distinct and body_distinct > 1 and weapon_distinct <= 1:
                    r.err(f"{key} [{tag}] 武器层 {len(frames)} 帧内容完全相同（静态图），身体层有 {body_distinct} 种不同帧：武器层没有随身体帧走")
                r.tick("武器层随帧核对")


def contact_sheet(assets_out: Path, out_path: Path, columns: int = 8) -> Path:
    """每键一格（整身合成，取代表帧：攻击取 hit 所在帧，循环取 1/4 处，其余取中间帧），带键名。只留本地。"""
    spec = _load(assets_out / SPEC_FILE)
    keys = list(spec["clips"].keys())
    fw, fh = C.CANVAS
    label_h = 14
    rows = (len(keys) + columns - 1) // columns
    sheet = Image.new("RGBA", (columns * fw, rows * (fh + label_h)), (58, 58, 58, 255))
    dr = ImageDraw.Draw(sheet)
    for i, key in enumerate(keys):
        e = spec["clips"][key]
        # 取第二个 canonical 档位（8 方向下 front_side_r，三分之四侧视，比正面更易读）；无方向变体时退回整身默认朝向
        d = clip_dir(assets_out, e["resource_ref"], spec["directions"]["canonical"][1])
        if not (d / "frames.json").is_file():
            d = clip_dir(assets_out, e["resource_ref"])
        doc = _load(d / "frames.json")
        n = len(doc["frames"])
        hit = _ev(e, "hit")
        if hit:
            acc, idx = 0.0, n - 1
            for j, f in enumerate(doc["frames"]):
                acc += f["duration"] * 1000.0
                if acc / e["total_ms"] >= hit[0]:
                    idx = j
                    break
        elif e["loop"]:
            idx = n // 4
        else:
            idx = n // 2
        f = doc["frames"][idx]
        with Image.open(d / "atlas.png") as im:
            cell = im.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])).convert("RGBA")
        x, y = (i % columns) * fw, (i // columns) * (fh + label_h)
        sheet.alpha_composite(cell, (x, y + label_h))
        dr.text((x + 2, y + 1), key, fill=(235, 235, 235, 255))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path)
    return out_path


def print_report(r: Report, log=print) -> int:
    for w in r.warnings:
        log(f"[WARN] {w}")
    for e in r.errors:
        log(f"[ERROR] {e}")
    summary = "，".join(f"{k} {v}" for k, v in sorted(r.counts.items()))
    log(f"自检完成：错误 {len(r.errors)}，警告 {len(r.warnings)}（{summary}）")
    return 0 if not r.errors else 1
