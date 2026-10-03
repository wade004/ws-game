"""自检：缺键、缺事件、帧数不一致、骨骼路径与预制体不匹配、关节角越限、与 sprite 版不同源，以及已入库引擎资产与规格的一致性。

退出码语义同 sprite 版：有任一"错误"即失败；"警告"只打印。检查分两类：
- 独立判据（不从生成器推导，避免"自己验自己"）：04 §3 键清单、04 §5 事件规则、05 §9 三相起点、帧数规则、骨骼路径、关节角限；
- 对账：规格文件逐字节等于用当前代码重新生成的结果；数据行、事件与 sprite 版同源；正向运动学把写出的四元数摆回去，
  关节位置与 sprite 版同一姿势的关节位置一致；引擎侧预制体/控制器/.anim 与规格一致（解析 YAML 文本，不需要 Unity）。
"""

from __future__ import annotations

import json
import math
import re
from pathlib import Path

from std_dummy_poses import config as SC
from std_dummy_poses.skeleton import build_parts
from std_dummy_poses.verify import (AIR_KEYS, DETAIL_KEYS, MASS_SCALED_POSE_IDS, RECOMMENDED_KEYS, REQUIRED_KEYS,  # noqa: F401
                                    REQUIRED_MASS_TIERS, WOUNDED_KEYS, Report, parse_key, print_report)

from . import config as C
from . import rig
from .build import build_spec, dumps_spec, effective_pose, key_times_ms, mass_clip_defs


def _load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def _ev(clip: dict, name: str) -> list[float]:
    return [e["time_pct"] for e in clip["events"] if e["name"] == name]


def _track_series(clip: dict) -> tuple[dict[str, list[rig.Quat]], list[tuple[float, float, float]]]:
    rots: dict[str, list[rig.Quat]] = {}
    pos: list[tuple[float, float, float]] = []
    inv = {C.bone_path(n): n for n, _p, _r in C.BONES}
    for tr in clip.get("tracks", []):
        if "rot" in tr:
            v = tr["rot"]
            rots[inv.get(tr["path"], tr["path"])] = [(v[i], v[i + 1], v[i + 2], v[i + 3]) for i in range(0, len(v), 4)]
        if "pos" in tr:
            v = tr["pos"]
            pos = [(v[i], v[i + 1], v[i + 2]) for i in range(0, len(v), 3)]
    return rots, pos


def verify(assets_out: Path, data_out: Path, unity_dir: Path | None = None, sprite_spec_path: Path | None = None) -> Report:
    r = Report()
    spec_path = assets_out / C.SPEC_FILE
    if not spec_path.is_file():
        r.err(f"缺规格文件 {spec_path}")
        return r
    raw = spec_path.read_text(encoding="utf-8")
    spec = json.loads(raw)
    params = spec["params"]
    fps = params["fps"]
    clips = {e["key"]: e for e in spec["clips"]}
    skel = spec["skeleton"]

    # --- 与当前代码生成结果逐字节对账 ---
    if raw != dumps_spec(build_spec(fps)):
        r.err("规格文件与当前代码重新生成的结果不一致（有人手改了规格，或改了配置没重新生成）：运行 gen_std_dummy_model_clips.py 重新生成")
    r.tick("规格对账")

    # --- 04 §3 键清单（独立判据）---
    for k in REQUIRED_KEYS:
        if k not in clips:
            r.err(f"必备键缺失：{k}")
    for k in RECOMMENDED_KEYS:
        if k not in clips:
            r.warn(f"推荐键缺失：{k}")
    for k in AIR_KEYS + DETAIL_KEYS + WOUNDED_KEYS:
        if k not in clips:
            r.err(f"M4-D 键缺失（本集声明出齐）：{k}")
    for k in clips:
        if parse_key(k) is None:
            r.err(f"键不符合 04 §2.1 语法：{k}")
    r.tick("键语法与清单", len(clips))

    # --- 与 sprite 版同源：键集合、事件、时长、三相 ---
    defs = {c.key: c for c in SC.build_clip_defs()}
    if set(clips) != set(defs):
        r.err(f"键集合与 sprite 版配置不一致：差 {sorted(set(clips) ^ set(defs))}")
    sprite_spec_file = sprite_spec_path or (assets_out / "std_dummy_poses.json")
    if sprite_spec_file.is_file():
        sprite = _load(sprite_spec_file)["clips"]
        for k, e in clips.items():
            s = sprite.get(k)
            if s is None:
                r.err(f"{k} 在 sprite 版规格里不存在")
                continue
            if e["events"] != s["events"]:
                r.err(f"{k} 命名事件与 sprite 版不同源")
            if e["total_ms"] != s["total_ms"] or e["frame_count"] != s["frame_count"] or e["loop"] != s["loop"]:
                r.err(f"{k} 时长/帧数/循环标志与 sprite 版不一致")
            if e["phases"] != s["phases"]:
                r.err(f"{k} 三相与 sprite 版不一致")
        r.tick("与 sprite 版同源核对", len(clips))
    else:
        r.warn(f"未找到 sprite 版规格 {sprite_spec_file}，跳过同源核对")

    # --- 骨骼：层级、路径、与配置一致 ---
    bone_by_path = {b["path"]: b for b in skel["bones"]}
    if [b["name"] for b in skel["bones"]] != [n for n, _p, _r in C.BONES]:
        r.err("骨骼清单与配置不一致")
    for path, b in bone_by_path.items():
        if b["parent"] is not None and C.bone_path(b["parent"]) != path.rsplit("/", 1)[0]:
            r.err(f"骨骼路径与父级不一致：{path}")
    for v in skel["visuals"]:
        if v["bone"] not in bone_by_path:
            r.err(f"可视块体挂在不存在的骨骼上：{v['bone']}")
    expected_rot = {C.bone_path(b) for b in C.ROT_BONES}
    if set(skel["rotation_bones"]) != expected_rot or skel["position_bones"] != [C.bone_path("hips")]:
        r.err("规格的旋转/位置骨骼清单与配置不一致")
    if params.get("bone_rot_limits_deg") != C.BONE_ROT_LIMITS_DEG:
        r.err("规格里的关节角限与配置不一致（不允许规格自己放宽角限）")
    r.tick("骨骼层级")

    # --- 逐键（主集 + 体量组）---
    for key, e in clips.items():
        _verify_entry(r, key, e, defs.get(key), fps, clips, expected_rot, bone_by_path)
    _verify_mass_groups(r, spec, clips, fps, expected_rot, bone_by_path)

    # --- 数据行 ---
    _verify_data_row(r, spec, clips, data_out)

    # --- 引擎侧资产 ---
    if unity_dir is not None:
        engine = dict(clips)
        for g in spec.get("mass_groups", []):
            engine.update({f"{g['mass']}/{e['key']}": e for e in g["clips"]})
        _verify_unity_assets(r, spec, engine, unity_dir)
    return r


def _verify_entry(r: Report, key: str, e: dict, c: SC.ClipDef | None, fps: int, lookup: dict, expected_rot: set, bone_by_path: dict) -> None:
    tag = f"{c.mass}/{key}" if c is not None and c.mass else key
    if c is None:
        r.err(f"规格里有 config 没有的键 {tag}")
        return
    total = e["total_ms"]
    plan = SC.phase_frame_plan(c, fps)
    if e["frames_per_phase"] != [n for _a, n, _b in plan] or e["frame_count"] != sum(n for _a, n, _b in plan):
        r.err(f"{tag} 帧数与参数推算不一致")
    if e["resource_ref"] != C.clip_resource_ref(key, c.alias_of, c.mass):
        r.err(f"{tag} 资源引用与命名约定不一致：{e['resource_ref']}")
    for ev in e["events"]:
        if not (0.0 <= ev["time_pct"] <= 1.0):
            r.err(f"{tag} 事件 {ev['name']} time_pct 越界 {ev['time_pct']}")
    info = parse_key(key)
    if info and info["state"] == "attack" and key != "dodge":
        a0, a1, hits = _ev(e, "active_start"), _ev(e, "active_end"), _ev(e, "hit")
        if len(a0) != 1 or len(a1) != 1 or not hits:
            r.err(f"{tag} 攻击类缺 active_start/active_end/hit")
        elif not (a0[0] < a1[0]) or any(not (a0[0] <= h <= a1[0]) for h in hits):
            r.err(f"{tag} hit 不在判定相内：active=[{a0[0]},{a1[0]}] hit={hits}")
        w, a, _rr = (p["ms"] for p in e["phases"])
        exp = {"active_start": round(w / total, 4), "active_end": round((w + a) / total, 4)}
        if (a0 and abs(a0[0] - exp["active_start"]) > 1e-4) or (a1 and abs(a1[0] - exp["active_end"]) > 1e-4):
            r.err(f"{tag} 判定相标记与三相毫秒数不一致")
        fam = e["family"]
        if fam in SC.PHASES_SEG1 and key == SC.attack_key(fam, 1) and tuple(p["ms"] for p in e["phases"]) != SC.PHASES_SEG1[fam]:
            r.err(f"{tag} 三相与 05 §9 起点配置不一致")
    if info and info["state"] == "move" and info["gait"] in ("walk", "run", "sprint"):
        if len(_ev(e, "footstep")) < 2:
            r.err(f"{tag} 缺 footstep（至少 2 次/循环）")
        if "step_displacement_bh" not in e:
            r.err(f"{tag} 缺每步位移")
    if key == "dodge":
        s0, s1 = _ev(e, "invuln_start"), _ev(e, "invuln_end")
        if len(s0) != 1 or len(s1) != 1 or not (0.0 <= s0[0] < s1[0] <= 1.0):
            r.err("dodge 无敌窗口标记缺失或越界")
        else:
            m0, m1 = _ev(e, "motion_start"), _ev(e, "motion_end")
            if not m0 or not m1 or not (m0[0] <= s0[0] and s1[0] <= m1[0]):
                r.err("dodge 无敌窗口应落在 motion 窗口内")
    if e.get("alias_of"):
        if e["alias_of"] not in lookup or e["resource_ref"] != lookup[e["alias_of"]]["resource_ref"]:
            r.err(f"{tag} 别名资源引用与目标不一致")
        if "tracks" in e:
            r.err(f"{tag} 是别名，不应带轨迹")
        return
    _verify_tracks(r, key, e, c, fps, expected_rot, bone_by_path)


def _verify_mass_groups(r: Report, spec: dict, clips: dict, fps: int, expected_rot: set, bone_by_path: dict) -> None:
    """体量组（04 §7 extends）：只覆盖 MASS_KEYS，时长/帧数/事件与主集一致（只改站姿），站姿方向符合轻/重定义。"""
    groups = spec.get("mass_groups", [])
    for t in REQUIRED_MASS_TIERS:
        if t not in [g.get("mass") for g in groups]:
            r.err(f"体量档缺 {t}（至少轻/中/重三档）")
    if [g.get("mass") for g in groups] != list(SC.MASS_TIERS):
        r.err(f"体量组清单应为 {list(SC.MASS_TIERS)}，规格里是 {[g.get('mass') for g in groups]}")
    n = 0
    for g in groups:
        mass = g.get("mass")
        if g.get("id") != C.mass_anim_set_id(mass) or g.get("extends") != spec["anim_set_id"]:
            r.err(f"体量组 {mass} 的 id/extends 不符合约定")
        if not str(g.get("id", "")).startswith("display.anim_set.std_"):
            r.err(f"体量组 {mass} 的 id 前缀不是 display.anim_set.std_（ADR-0119）")
        entries = {e["key"]: e for e in g["clips"]}
        if set(entries) != set(clips):
            r.err(f"体量组 {mass} 应覆盖主集全部键（含别名键）：差 {sorted(set(entries) ^ set(clips))}")
        prof = SC.MASS_TIERS[mass]
        defs_m = {c.key: c for c in mass_clip_defs(mass)}
        for key, e in entries.items():
            main = clips.get(key)
            if main is None:
                r.err(f"体量组 {mass} 的键 {key} 不在主集里（extends 覆盖没有意义）")
                continue
            for field in ("total_ms", "frame_count", "loop", "phases", "events", "frames_per_phase"):
                if e[field] != main[field]:
                    r.err(f"体量组 {mass}/{key} 的 {field} 与主集不一致（体量组只改站姿，不改时长/帧数/事件）")
            if e["resource_ref"] == main["resource_ref"]:
                r.err(f"体量组 {mass}/{key} 没有自己的剪辑资源")
            _verify_entry(r, key, e, defs_m.get(key), fps, entries, expected_rot, bone_by_path)
            if "tracks" in e and "tracks" in main and key in C.LEGACY_MASS_KEYS:
                ta = next((t for t in e["tracks"] if t["path"].endswith("/spine")), None)
                tb = next((t for t in main["tracks"] if t["path"].endswith("/spine")), None)
                if ta is None or tb is None:
                    continue
                a, b = ta["rot"][:4], tb["rot"][:4]
                # 躯干前倾 = 绕 +X 正向旋转（四元数 x 分量增大）：重 > 中 > 轻
                if (mass == "heavy" and not a[0] > b[0]) or (mass == "light" and not a[0] < b[0]):
                    r.err(f"体量组 {mass}/{key} 起始帧躯干前倾方向不符合{'重' if mass == 'heavy' else '轻'}体量定义")
            if "tracks" in e and key not in C.LEGACY_MASS_KEYS:
                _verify_mass_offsets(r, mass, key, defs_m[key], prof, SC.clip_by_key(key), fps)
            n += 1
    r.tick("体量组核对", n)


def _verify_mass_offsets(r: Report, mass: str, key: str, gc: SC.ClipDef, prof: dict, mc: SC.ClipDef, fps: int) -> None:
    """体量组的每个键都真的带体量偏移：非受击/抬升类键与主集只差静态站姿偏移；受击/抬升类键只要求与主集不同。"""
    differs = False
    for t in key_times_ms(gc, fps):
        pg, pm = effective_pose(gc, t), effective_pose(mc, t)
        if any(abs(pg[k] - pm[k]) > 1e-9 for k in pg):
            differs = True
        if gc.pose_id in MASS_SCALED_POSE_IDS:
            continue
        for k in pg:
            want = {"t_pitch": prof["t_pitch"], "h_pitch": prof["h_pitch"], "m_sa": prof["sa"], "o_sa": prof["sa"],
                    "m_ha": prof["ha"], "o_ha": prof["ha"]}.get(k, 0.0)
            if abs((pg[k] - pm[k]) - want) > 1e-9:
                r.err(f"体量组 {mass}/{key} t={t}ms 姿势 {k} 与主集差 {pg[k] - pm[k]:.4f}，应只差体量偏移 {want}")
                return
    if not differs:
        r.err(f"体量组 {mass}/{key} 与主集姿势完全相同（体量偏移没有生效）")


def _verify_tracks(r: Report, key: str, e: dict, c: SC.ClipDef, fps: int, expected_rot: set, bone_by_path: dict) -> None:
    tag = f"{c.mass}/{key}" if c.mass else key
    times = e.get("times_ms", [])
    total = e["total_ms"]
    # 关键帧时刻：帧数 + 1 个，起 0 止总时长，严格递增，且与 sprite 版帧划分（含相边界）一致
    if len(times) != e["frame_count"] + 1:
        r.err(f"{tag} 关键帧数 {len(times)} != 帧数 {e['frame_count']} + 1")
    if not times or abs(times[0]) > C.TIME_TOLERANCE_MS or abs(times[-1] - total) > C.TIME_TOLERANCE_MS:
        r.err(f"{tag} 关键帧时刻没有覆盖 [0, {total}] ms")
    if any(b <= a for a, b in zip(times, times[1:])):
        r.err(f"{tag} 关键帧时刻不是严格递增")
    expect_times = key_times_ms(c, fps)
    if len(expect_times) == len(times) and any(abs(a - b) > C.TIME_TOLERANCE_MS for a, b in zip(times, expect_times)):
        r.err(f"{tag} 关键帧时刻与 sprite 版帧划分不一致")
    acc = 0.0
    for p in e["phases"]:
        acc += p["ms"]
        if not any(abs(t - acc) <= C.TIME_TOLERANCE_MS for t in times):
            r.err(f"{tag} 相边界 {acc} ms 没有对应的关键帧")
    n = len(times)

    # 轨迹路径：必须是预制体骨骼，且恰好是配置声明的旋转骨骼 + 髋位置
    paths_rot = {t["path"] for t in e["tracks"] if "rot" in t}
    paths_pos = {t["path"] for t in e["tracks"] if "pos" in t}
    for t in e["tracks"]:
        if t["path"] not in bone_by_path:
            r.err(f"{tag} 轨迹路径 {t['path']} 不在骨架里（与预制体骨骼不匹配）")
    if paths_rot != expected_rot:
        r.err(f"{tag} 旋转轨迹路径与骨架不一致：缺 {sorted(expected_rot - paths_rot)} 多 {sorted(paths_rot - expected_rot)}")
    if paths_pos != {C.bone_path("hips")}:
        r.err(f"{tag} 位置轨迹应只有 hips")
    for t in e["tracks"]:
        if "rot" in t and len(t["rot"]) != 4 * n:
            r.err(f"{tag} 轨迹 {t['path']} 四元数个数与关键帧数不一致")
        if "pos" in t and len(t["pos"]) != 3 * n:
            r.err(f"{tag} 轨迹 {t['path']} 位置个数与关键帧数不一致")
    rots, pos = _track_series(e)
    if any(len(v) != n for v in rots.values()) or len(pos) != n:
        return

    # 四元数：单位长、半球连续；关节角限
    for b, seq in rots.items():
        limit = C.BONE_ROT_LIMITS_DEG.get(b)
        if b == "hips":
            limit = C.hips_rot_limit_deg(key)  # 髋大角度只给整身俯仰类键（翻滚一整圈、躺平放平到 88 度），其余键髋只绕竖轴转几度
        for i, q in enumerate(seq):
            norm = math.sqrt(sum(x * x for x in q))
            if abs(norm - 1.0) > 1e-4:
                r.err(f"{tag} {b}[{i}] 四元数不是单位长（{norm:.6f}）")
            if i and rig.quat_dot(seq[i - 1], q) < 0.0:
                r.err(f"{tag} {b}[{i}] 与前一帧不在同一半球（插值会绕远路）")
            if limit is not None and rig.quat_angle_deg(q) > limit + 1e-3:
                r.err(f"{tag} {b}[{i}] 关节角 {rig.quat_angle_deg(q):.1f} 度超过上限 {limit}")
    r.tick("轨迹与关节角限", len(rots))

    # 循环首尾连续
    if e["loop"]:
        # 四元数 q 与 -q 是同一旋转（翻滚一整圈后髋四元数回到 -q，半球连续要求保留这个符号）：首尾按"至多差一个符号"比较
        d = max(min(max(abs(a - b) for a, b in zip(seq[0], seq[-1])), max(abs(a + b) for a, b in zip(seq[0], seq[-1])))
                for seq in rots.values())
        dp = max(abs(a - b) for a, b in zip(pos[0], pos[-1]))
        if d > 1e-5 or dp > 1e-4:
            r.err(f"{tag} 循环首尾姿势不连续（四元数最大差 {d:.6f}，位置最大差 {dp:.6f}）")
        r.tick("循环连续")

    # 源姿势关节角的解剖范围（肘/膝不反向过伸等）+ 正向运动学对账
    worst = 0.0
    for i, t in enumerate(times):
        pose = effective_pose(c, t)
        for name, (lo, hi) in C.SOURCE_ANGLE_LIMITS.items():
            if not (lo - 1e-6 <= pose[name] <= hi + 1e-6):
                r.err(f"{tag} t={t}ms 源关节角 {name}={pose[name]:.1f} 超出范围 [{lo}, {hi}]")
        if key not in C.BP_KEYS and abs(pose["bp"]) > 1e-9:
            r.err(f"{tag} t={t}ms 整身俯仰 bp={pose['bp']:.1f} 只允许翻滚类、躺平类与重受击/击退键使用（{', '.join(C.BP_KEYS)}）")
        if key in C.BP_RECOIL_KEYS and not (-C.BP_RECOIL_LIMIT_DEG - 1e-6 <= pose["bp"] <= 1e-9):
            r.err(f"{tag} t={t}ms 重受击/击退的整身俯仰 bp={pose['bp']:.1f} 应在 [-{C.BP_RECOIL_LIMIT_DEG}, 0]（只后仰、不前翻）")
        parts, joints = build_parts(pose, None)
        rot_i = {b: seq[i] for b, seq in rots.items()}
        hips = (pose["px"], SC.REST_HIP_Y + pose["py"], pose["pz"])
        fk = rig.forward_kinematics(rot_i, hips)
        want = {"foot_r": joints["ankle_m"], "foot_l": joints["ankle_o"], "hand_r": joints["hand_m"], "hand_l": joints["hand_o"],
                "upper_arm_r": joints["shoulder_m"], "upper_arm_l": joints["shoulder_o"], "head": joints["head_center"],
                "socket.main_hand": joints["hand_m"]}
        for b, w in want.items():
            dist = math.dist(fk[b], w)
            worst = max(worst, dist)
            if dist > C.FK_TOLERANCE_BH:
                r.err(f"{tag} t={t}ms 骨骼 {b} 正向运动学位置与 sprite 版关节位置相差 {dist:.5f} 身高倍数")
        wr = rig.forward_kinematics_rot(rot_i)
        flat = max(abs(a - b) for ra, rb in zip(wr["foot_r"], wr["hips"]) for a, b in zip(ra, rb))
        if flat > 1e-4:
            r.err(f"{tag} t={t}ms 脚掌没有保持与髋系平行（偏差 {flat:.5f}）")
        # 髋高度：着地求解后最低点在地面（lift 之外）——对账规格里写出的位置
        want_y = rig.hips_position_bh(pose, (parts, joints))[1] * C.BODY_HEIGHT_UNITS
        if abs(pos[i][1] - want_y) > 2e-5:
            r.err(f"{tag} t={t}ms 髋高度 {pos[i][1]:.5f} 与着地求解 {want_y:.5f} 不一致")
    r.tick("正向运动学对账", n)

    # 走/跑：接触姿势（关键帧 0）两脚前后间距 = 每步位移（身高倍数，容差 5%）
    info = parse_key(key)
    if info and info["state"] == "move" and info["gait"] in ("walk", "run", "sprint") and not c.combat and c.family is None:
        pose0 = effective_pose(c, times[0])
        fk = rig.forward_kinematics({b: seq[0] for b, seq in rots.items()}, (pose0["px"], SC.REST_HIP_Y + pose0["py"], pose0["pz"]))
        sep = abs(fk["foot_r"][2] - fk["foot_l"][2])
        step = SC.step_displacement_bh(info["gait"], c.stride_factor)
        if abs(sep - step) > 0.05 * step:
            r.err(f"{tag} 接触姿势脚间距 {sep:.3f} 与每步位移 {step:.3f} 偏差超 5%")
        if abs(e["step_displacement_bh"] * 2 - e["cycle_displacement_bh"]) > 1e-4:
            r.err(f"{tag} 每循环位移与每步位移不一致")
        r.tick("步幅核对")


def _verify_data_row(r: Report, spec: dict, clips: dict, data_out: Path) -> None:
    data_path = data_out / "display" / "display.anim_set.json"
    if not data_path.is_file():
        r.err(f"缺数据文件 {data_path}")
        return
    doc = _load(data_path)
    rows = [x for x in doc.get("rows", []) if x.get("id") == spec["anim_set_id"]]
    if len(rows) != 1:
        r.err(f"数据文件缺行 {spec['anim_set_id']}")
        return
    row = rows[0]
    if not spec["anim_set_id"].startswith("display.anim_set.std_"):
        r.err("anim_set id 前缀不是 display.anim_set.std_（ADR-0119）")
    if set(row["clips"]) != set(clips):
        r.err(f"数据行键集合与规格不一致：差 {sorted(set(row['clips']) ^ set(clips))}")
    for k, v in row["clips"].items():
        if k in clips and (v["resource_ref"] != clips[k]["resource_ref"] or v["events"] != clips[k]["events"]
                           or v.get("blend_ms") != clips[k].get("blend_ms")):
            r.err(f"数据行与规格不一致：{k}")
        if not isinstance(v.get("blend_ms"), int) or not (0 <= v["blend_ms"] <= 2000):
            r.err(f"数据行 {k} 缺 blend_ms 或越界（0..2000 ms）：{v.get('blend_ms')!r}")
        if not v["resource_ref"].startswith(C.REF_CATEGORY + "."):
            r.err(f"数据行 {k} 的资源引用类别应为 {C.REF_CATEGORY}.（model 型动画剪辑）：{v['resource_ref']}")
    r.tick("数据行核对", len(row["clips"]))
    if row.get("extends"):
        r.err("主集数据行不应有 extends")
    if row.get("blends") != spec.get("blends"):
        r.err("数据行 blends 与规格不一致")
    for b in row.get("blends", []):
        if b["from"] not in clips or b["to"] not in clips or not (0 <= b["blend_ms"] <= 2000):
            r.err(f"数据行 blends 条目非法（键不存在或时长越界）：{b}")
    pairs = [(b["from"], b["to"]) for b in row.get("blends", [])]
    if len(set(pairs)) != len(pairs):
        r.err("数据行 blends 有重复的（from, to）对")
    r.tick("过渡混合核对", len(pairs))
    med = [x for x in doc.get("rows", []) if x.get("id") == f"{spec['anim_set_id']}_{spec['params']['mass_tiers']['main']}"]
    if len(med) != 1 or med[0].get("extends") != spec["anim_set_id"] or med[0].get("clips"):
        r.err("中体量数据行应为 extends 主集的空覆盖行")
    for g in spec.get("mass_groups", []):
        grows = [x for x in doc.get("rows", []) if x.get("id") == g["id"]]
        if len(grows) != 1:
            r.err(f"数据文件缺体量组行 {g['id']}")
            continue
        grow = grows[0]
        if grow.get("extends") != spec["anim_set_id"]:
            r.err(f"体量组行 {g['id']} 的 extends 应为 {spec['anim_set_id']}")
        want = {e["key"]: e for e in g["clips"]}
        if set(grow["clips"]) != set(want):
            r.err(f"体量组行 {g['id']} 键集合与规格不一致：差 {sorted(set(grow['clips']) ^ set(want))}")
        for k, v in grow["clips"].items():
            if k in want and (v["resource_ref"] != want[k]["resource_ref"] or v["events"] != want[k]["events"]):
                r.err(f"体量组行 {g['id']} 与规格不一致：{k}")
            if "blend_ms" in v:
                r.err(f"体量组行 {g['id']} 的 {k} 不应重复声明 blend_ms（按键继承主集）")
        if grow.get("blends"):
            r.err(f"体量组行 {g['id']} 不应声明 blends（继承主集）")
        r.tick("体量组数据行核对", len(grow["clips"]))


# --------------------------------------------------------------------------
# 引擎侧资产（解析 Unity YAML 文本）
# --------------------------------------------------------------------------

def _guid_of(meta: Path) -> str | None:
    if not meta.is_file():
        return None
    m = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", meta.read_text(encoding="utf-8"), re.M)
    return m.group(1) if m else None


def prefab_paths(text: str) -> tuple[str | None, set[str], str | None]:
    """解析预制体 YAML：返回 (根名, 全部后代的相对路径集合, Animator 控制器 guid)。"""
    docs = re.split(r"^--- !u!(\d+) &(-?\d+)[^\n]*\n", text, flags=re.M)
    go_name: dict[str, str] = {}
    tr_go: dict[str, str] = {}
    tr_father: dict[str, str] = {}
    controller_guid = None
    for i in range(1, len(docs), 3):
        typ, anchor, body = docs[i], docs[i + 1], docs[i + 2]
        if typ == "1":
            m = re.search(r"^\s*m_Name:\s*(.*)$", body, re.M)
            go_name[anchor] = m.group(1).strip() if m else ""
        elif typ == "4":
            go = re.search(r"m_GameObject:\s*\{fileID:\s*(-?\d+)\}", body)
            fa = re.search(r"m_Father:\s*\{fileID:\s*(-?\d+)\}", body)
            if go and fa:
                tr_go[anchor] = go.group(1)
                tr_father[anchor] = fa.group(1)
        elif typ == "95":
            m = re.search(r"m_Controller:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})", body)
            if m:
                controller_guid = m.group(1)
    root = None
    paths: set[str] = set()
    for tid, fid in tr_father.items():
        if fid == "0":
            root = go_name.get(tr_go[tid])
    for tid in tr_go:
        parts = []
        cur = tid
        while cur != "0" and cur in tr_go:
            parts.append(go_name.get(tr_go[cur], "?"))
            cur = tr_father[cur]
        parts.reverse()
        if len(parts) > 1:
            paths.add("/".join(parts[1:]))
    return root, paths, controller_guid


def anim_info(text: str) -> dict:
    """解析 .anim YAML：名字、终止时间、循环、曲线路径、事件。"""
    info: dict = {"name": None, "stop": None, "loop": None, "paths": set(), "events": []}
    m = re.search(r"^  m_Name:\s*(.*)$", text, re.M)
    info["name"] = m.group(1).strip() if m else None
    m = re.search(r"^\s+m_StopTime:\s*([-0-9.eE+]+)\s*$", text, re.M)
    info["stop"] = float(m.group(1)) if m else None
    m = re.search(r"^\s+m_LoopTime:\s*(\d)\s*$", text, re.M)
    info["loop"] = bool(int(m.group(1))) if m else None
    section = None
    for line in text.splitlines():
        m = re.match(r"^  (m_\w+):", line)
        if m:
            section = m.group(1)
        if section in ("m_RotationCurves", "m_PositionCurves", "m_EulerCurves", "m_ScaleCurves"):
            pm = re.match(r"^    path:\s*(.*)$", line)
            if pm:
                info["paths"].add(pm.group(1).strip())
    ev_text = text.split("\n  m_Events:", 1)
    if len(ev_text) == 2:
        for blk in re.split(r"^  - ", ev_text[1], flags=re.M)[1:]:
            t = re.search(r"time:\s*([-0-9.eE+]+)", blk)
            fn = re.search(r"functionName:\s*(\S+)", blk)
            data = re.search(r"^\s*data:\s*(.*)$", blk, re.M)
            if t and fn:
                info["events"].append((float(t.group(1)), fn.group(1), (data.group(1).strip() if data else "")))
    return info


def _verify_unity_assets(r: Report, spec: dict, clips: dict, unity_dir: Path) -> None:
    res = unity_dir / C.UNITY_RESOURCES
    prefab = res / C.PREFAB_REL
    controller = res / C.CONTROLLER_REL
    if not prefab.is_file():
        r.err(f"缺预制体 {prefab}（运行引擎侧编辑器生成脚本生成）")
    else:
        root, paths, ctrl_guid = prefab_paths(prefab.read_text(encoding="utf-8"))
        skel = spec["skeleton"]
        want = {b["path"] for b in skel["bones"]} | {f"{v['bone']}/{v['name']}" for v in skel["visuals"]}
        if root != skel["root"]:
            r.err(f"预制体根名 {root!r} 与规格 {skel['root']!r} 不一致")
        if paths != want:
            r.err(f"预制体骨骼/块体路径与规格不匹配：缺 {sorted(want - paths)} 多 {sorted(paths - want)}")
        cg = _guid_of(Path(str(controller) + ".meta"))
        if ctrl_guid is None or (cg is not None and ctrl_guid != cg):
            r.err("预制体 Animator 引用的控制器 guid 与控制器 .meta 不一致")
        r.tick("预制体路径核对")
    if not controller.is_file():
        r.err(f"缺动画控制器 {controller}")
        return
    ctext = controller.read_text(encoding="utf-8")
    states: dict[str, str] = {}
    parts = re.split(r"^--- !u!1102 &(-?\d+)[^\n]*\n", ctext, flags=re.M)
    for i in range(1, len(parts), 2):
        blk = parts[i + 1]
        nm = re.search(r"^\s+m_Name:\s*(.*)$", blk, re.M)
        mo = re.search(r"m_Motion:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})", blk)
        if nm:
            name = nm.group(1).strip()
            states[name] = mo.group(1) if mo else ""
            if int(parts[i]) != C.controller_state_file_id(name):
                r.err(f"控制器状态 {name} 的 fileID 不是确定性公式的值（控制器不是由编辑器生成脚本的确定性写法产出的）")
    bones = {b["path"] for b in spec["skeleton"]["bones"]}
    want_states = {e["state"] for e in clips.values() if "alias_of" not in e}
    if set(states) != want_states:
        r.err(f"控制器状态集合与规格不一致：缺 {sorted(want_states - set(states))} 多 {sorted(set(states) - want_states)}")
    for key, e in clips.items():
        if "alias_of" in e:
            continue
        state = e["state"]
        clip_path = res / C.CLIP_DIR_REL / f"{state}.anim"
        if state not in states:
            r.err(f"{key} 控制器里没有状态 {state}")
        if not clip_path.is_file():
            r.err(f"{key} 缺剪辑资产 {clip_path.name}")
            continue
        ai = anim_info(clip_path.read_text(encoding="utf-8"))
        gid = _guid_of(Path(str(clip_path) + ".meta"))
        if state in states and gid is not None and states[state] != gid:
            r.err(f"{key} 控制器状态 {state} 的 Motion 不是剪辑 {clip_path.name}")
        if ai["name"] != state:
            r.err(f"{key} 剪辑资产内部名 {ai['name']!r} != {state!r}")
        if ai["stop"] is None or abs(ai["stop"] * 1000.0 - e["total_ms"]) > 0.05:
            r.err(f"{key} 剪辑资产时长 {ai['stop']} s 与规格 {e['total_ms']} ms 不一致")
        if ai["loop"] != e["loop"]:
            r.err(f"{key} 剪辑资产循环标志 {ai['loop']} 与规格 {e['loop']} 不一致")
        spec_paths = {t["path"] for t in e["tracks"]}
        if ai["paths"] != spec_paths:
            r.err(f"{key} 剪辑资产曲线路径与规格不一致：缺 {sorted(spec_paths - ai['paths'])} 多 {sorted(ai['paths'] - spec_paths)}")
        if not ai["paths"] <= bones:
            r.err(f"{key} 剪辑资产曲线路径不在骨架里：{sorted(ai['paths'] - bones)}")
        want_ev = sorted((ev["name"], ev["time_pct"] * e["total_ms"] / 1000.0) for ev in e["anim_events"])
        got_ev = sorted((d, t) for t, fn, d in ai["events"] if fn == "OnAnimEvent")
        if len(ai["events"]) != len(got_ev) or [n for n, _t in want_ev] != [n for n, _t in got_ev] or \
                any(abs(a[1] - b[1]) > 1e-4 for a, b in zip(want_ev, got_ev)):
            r.err(f"{key} 剪辑资产内嵌事件与规格不一致：规格 {want_ev} 资产 {got_ev}")
    # 清单外残留
    clip_dir = res / C.CLIP_DIR_REL
    if clip_dir.is_dir():
        expected = {f"{e['state']}.anim" for e in clips.values() if "alias_of" not in e}
        for p in clip_dir.glob(SC.STEM_PREFIX + "*.anim"):
            if p.name not in expected:
                r.warn(f"清单外残留剪辑资产：{p.name}")
    r.tick("引擎资产核对", sum(1 for e in clips.values() if "alias_of" not in e))
