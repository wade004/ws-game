"""``toolchain/gen_std_dummy_model_clips.py``（框架级假人姿势集，model 型 / 骨骼剪辑）的测试。

覆盖：04 §3 键清单、与 sprite 版同源（键集合/事件/时长/三相）、05 §9 三相起点、帧数规则（含 fps 参数化）、确定性、
关节角限与正向运动学对账、骨骼路径与预制体一致、自检能抓出缺陷（缺键/缺事件/帧数不一致/骨骼路径不匹配/关节角越限/
数据行不一致/引擎资产与规格不一致），以及仓库已入库产物（规格 + 数据行 + 引擎资产）自检通过。
临时输出一律写 tmp_path，不触碰仓库内的 assets/data/adapters。

运行：``python -m pytest toolchain/tests/test_std_dummy_model_clips.py -q``
"""

from __future__ import annotations

import json
import math
import re
import shutil
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from std_dummy_model_clips import config as C  # noqa: E402
from std_dummy_model_clips import rig  # noqa: E402
from std_dummy_model_clips.build import build_spec, dumps_spec, effective_pose, generate, key_times_ms  # noqa: E402
from std_dummy_model_clips.verify import REQUIRED_KEYS, RECOMMENDED_KEYS, verify  # noqa: E402
from std_dummy_poses import config as SC  # noqa: E402

REPO_ROOT = TOOLCHAIN_DIR.parent
SPEC_REL = Path("assets") / "_placeholder" / C.SPEC_FILE
UNITY_RES = REPO_ROOT / "adapters" / "unity" / C.UNITY_RESOURCES


def _repo_spec() -> dict:
    return json.loads((REPO_ROOT / SPEC_REL).read_text(encoding="utf-8"))


def _clip(spec: dict, key: str) -> dict:
    return next(c for c in spec["clips"] if c["key"] == key)


@pytest.fixture()
def tree(tmp_path):
    """仓库已入库的规格/sprite 规格/数据文件的临时副本（缺陷注入用）。"""
    assets, data = tmp_path / "assets", tmp_path / "data"
    (data / "display").mkdir(parents=True)
    assets.mkdir()
    shutil.copy(REPO_ROOT / SPEC_REL, assets / C.SPEC_FILE)
    shutil.copy(REPO_ROOT / "assets" / "_placeholder" / "std_dummy_poses.json", assets / "std_dummy_poses.json")
    shutil.copy(REPO_ROOT / "data" / "_framework" / "display" / "display.anim_set.json", data / "display" / "display.anim_set.json")
    return assets, data


def _mutate_spec(assets: Path, fn) -> None:
    path = assets / C.SPEC_FILE
    spec = json.loads(path.read_text(encoding="utf-8"))
    fn(spec)
    path.write_text(dumps_spec(spec), encoding="utf-8", newline="\n")


def _errors(assets, data, unity=None) -> list[str]:
    return verify(assets, data, unity).errors


def _has(errors: list[str], needle: str) -> bool:
    return any(needle in e for e in errors)


# --------------------------------------------------------------------------
# 已入库产物
# --------------------------------------------------------------------------

def test_committed_tree_passes_self_check_including_engine_assets():
    rep = verify(REPO_ROOT / "assets" / "_placeholder", REPO_ROOT / "data" / "_framework", REPO_ROOT / "adapters" / "unity")
    assert rep.errors == []
    assert rep.warnings == []
    assert rep.counts.get("引擎资产核对", 0) == 33
    assert rep.counts.get("预制体路径核对", 0) == 1


def test_generation_is_deterministic_and_committed_spec_is_up_to_date(tmp_path):
    out = []
    for i in range(2):
        assets, data = tmp_path / f"a{i}", tmp_path / f"d{i}"
        generate(assets, data, log=lambda *_: None)
        out.append(((assets / C.SPEC_FILE).read_bytes(), (data / "display" / "display.anim_set.json").read_bytes()))
    assert out[0] == out[1], "同输入应逐字节同输出"
    assert out[0][0] == (REPO_ROOT / SPEC_REL).read_bytes(), "已入库规格应等于当前代码生成结果（改了配置没重新生成）"


def test_generated_set_passes_self_check_without_engine_assets(tmp_path):
    assets, data = tmp_path / "assets", tmp_path / "data"
    generate(assets, data, log=lambda *_: None)
    shutil.copy(REPO_ROOT / "assets" / "_placeholder" / "std_dummy_poses.json", assets / "std_dummy_poses.json")
    rep = verify(assets, data)
    assert rep.errors == []


def test_generation_keeps_the_other_rows_of_the_shared_data_file(tmp_path):
    """sprite 型与 model 型两行共用 display.anim_set.json：写 model 行不得动 sprite 行。"""
    data = tmp_path / "data"
    (data / "display").mkdir(parents=True)
    src = REPO_ROOT / "data" / "_framework" / "display" / "display.anim_set.json"
    shutil.copy(src, data / "display" / "display.anim_set.json")
    generate(tmp_path / "assets", data, log=lambda *_: None)
    assert (data / "display" / "display.anim_set.json").read_bytes() == src.read_bytes()
    ids = [r["id"] for r in json.loads(src.read_text(encoding="utf-8"))["rows"]]
    assert {"display.anim_set.std_dummy_biped", C.ANIM_SET_ID} <= set(ids)


# --------------------------------------------------------------------------
# 键清单 / 同源 / 三相 / 帧数
# --------------------------------------------------------------------------

def test_required_and_recommended_keys_present_and_key_set_is_sprite_key_set():
    spec = _repo_spec()
    keys = {c["key"] for c in spec["clips"]}
    for k in REQUIRED_KEYS + RECOMMENDED_KEYS:
        assert k in keys, k
    assert keys == {c.key for c in SC.build_clip_defs()}, "键集合必须与 sprite 型同源（单一来源）"
    # 别名键只在数据行里复用同一份剪辑资源，不单独出轨迹
    alias = _clip(spec, "attack")
    assert alias["alias_of"] == "attack.unarmed" and "tracks" not in alias
    assert alias["resource_ref"] == _clip(spec, "attack.unarmed")["resource_ref"]


def test_weapon_family_variants_exist_for_locomotion_and_attack():
    keys = {c["key"] for c in _repo_spec()["clips"]}
    for fam in ("1h", "2h"):
        for base in ("idle", "idle.combat", "move.walk", "move.run", "move.run.combat"):
            assert f"{base}.{fam}" in keys
    for fam in ("unarmed", "1h", "2h"):
        assert f"attack.{fam}" in keys
    for k in ("hit.light", "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup"):
        assert k in keys


def test_events_phases_and_duration_identical_to_sprite_spec():
    model = _repo_spec()
    sprite = json.loads((REPO_ROOT / "assets" / "_placeholder" / "std_dummy_poses.json").read_text(encoding="utf-8"))["clips"]
    for c in model["clips"]:
        s = sprite[c["key"]]
        assert c["events"] == s["events"], c["key"]
        assert c["phases"] == s["phases"], c["key"]
        assert (c["total_ms"], c["frame_count"], c["loop"]) == (s["total_ms"], s["frame_count"], s["loop"]), c["key"]


def test_attack_phases_match_feel_05_section9_starting_points():
    spec = _repo_spec()
    assert [p["ms"] for p in _clip(spec, "attack.1h")["phases"]] == [110, 80, 190]
    assert [p["ms"] for p in _clip(spec, "attack.2h")["phases"]] == [170, 100, 290]
    for key, (w, a, r) in (("attack.1h", (110, 80, 190)), ("attack.2h", (170, 100, 290))):
        c = _clip(spec, key)
        ev = {e["name"]: e["time_pct"] for e in c["events"]}
        total = w + a + r
        assert ev["active_start"] == pytest.approx(w / total, abs=1e-4)
        assert ev["active_end"] == pytest.approx((w + a) / total, abs=1e-4)
        assert ev["hit"] == pytest.approx((w + a / 2) / total, abs=1e-4), "命中点 = 判定相中点"


def test_hit_marker_has_engine_alias_at_same_time_in_baked_events():
    """model 型角色外壳识别的命中帧事件名是 hit_frame：烘进 .anim 的事件里每个 hit 旁边同刻带一条别名。"""
    for c in _repo_spec()["clips"]:
        if "alias_of" in c:
            continue
        baked = {}
        for e in c["anim_events"]:
            baked.setdefault(e["name"], []).append(e["time_pct"])
        for e in c["events"]:
            assert e["time_pct"] in baked[e["name"]]
            if e["name"] == "hit":
                assert baked["hit_frame"] == baked["hit"], c["key"]
        assert ("hit_frame" in baked) == ("hit" in baked)


@pytest.mark.parametrize("fps", [20, 30, 60])
def test_frame_and_keyframe_counts_follow_rule_and_fps_parameter(fps):
    spec = build_spec(fps)
    for c in spec["clips"]:
        if "alias_of" in c:
            continue
        expect = sum(max(1, int(math.floor(p["ms"] * fps / 1000.0 + 0.5))) for p in c["phases"])
        assert c["frame_count"] == expect, c["key"]
        assert len(c["times_ms"]) == expect + 1, c["key"]
        assert c["times_ms"][0] == 0.0 and c["times_ms"][-1] == c["total_ms"]
        # 每个相边界都是关键帧
        acc = 0.0
        for p in c["phases"]:
            acc += p["ms"]
            assert any(abs(t - acc) < 1e-3 for t in c["times_ms"]), (c["key"], acc)
        for t in c["tracks"]:
            n = len(t["rot"]) // 4 if "rot" in t else len(t["pos"]) // 3
            assert n == expect + 1


def test_walk_run_have_footsteps_and_displacement_and_loop_is_seamless():
    spec = _repo_spec()
    for key in ("move.walk", "move.run"):
        c = _clip(spec, key)
        assert [e["time_pct"] for e in c["events"] if e["name"] == "footstep"] == [0.0, 0.5]
        assert c["step_displacement_bh"] > 0 and c["cycle_displacement_bh"] == pytest.approx(2 * c["step_displacement_bh"], abs=1e-3)
    for c in spec["clips"]:
        if c["loop"] and "tracks" in c:
            for t in c["tracks"]:
                v = t.get("rot") or t["pos"]
                w = 4 if "rot" in t else 3
                assert v[:w] == v[-w:], (c["key"], t["path"])


# --------------------------------------------------------------------------
# 动作质量 / 关节角
# --------------------------------------------------------------------------

def test_hinge_joints_never_hyperextend_and_all_bone_angles_within_limits():
    spec = _repo_spec()
    for c in spec["clips"]:
        if "tracks" not in c:
            continue
        for t in c["tracks"]:
            if "rot" not in t:
                continue
            name = next(n for n, _p, _r in C.BONES if C.bone_path(n) == t["path"])
            limit = C.BONE_ROT_LIMITS_DEG[name]
            for i in range(0, len(t["rot"]), 4):
                assert rig.quat_angle_deg(tuple(t["rot"][i:i + 4])) <= limit + 1e-6, (c["key"], name)
    defs = [d for d in SC.build_clip_defs() if not d.alias_of]
    for d in defs:
        for t in key_times_ms(d, SC.FPS):
            p = effective_pose(d, t)
            for k in ("m_el", "o_el", "m_kn", "o_kn"):
                assert 0.0 <= p[k] <= 150.0, (d.key, t, k, p[k])


def test_motion_is_recognizable_idle_breath_alternating_gait_and_three_phase_attack():
    spec = _repo_spec()

    def rot_series(key, bone):
        c = _clip(spec, key)
        t = next(t for t in c["tracks"] if t["path"] == C.bone_path(bone))
        return [tuple(t["rot"][i:i + 4]) for i in range(0, len(t["rot"]), 4)]

    def angle_between(a, b):
        d = abs(rig.quat_dot(a, b))
        return math.degrees(2 * math.acos(min(1.0, d)))

    # idle 呼吸：躯干/头有可见的循环起伏（同一循环里最大偏离 > 0.5 度），且首尾相接
    sp = rot_series("idle", "spine")
    assert max(angle_between(sp[0], q) for q in sp) > 0.5
    # walk/run：左右大腿反相（某一关键帧一个前摆一个后摆），迈步幅度显著
    for key, min_swing in (("move.walk", 15.0), ("move.run", 30.0)):
        tr, tl = rot_series(key, "thigh_r"), rot_series(key, "thigh_l")
        swing_r = max(angle_between(tr[0], q) for q in tr)
        assert swing_r > min_swing, key
        # 交替：t=0 与半周期左右腿互换（半周期右腿姿势 ≈ 起点左腿姿势）
        half = len(tr) // 2
        assert angle_between(tr[half], tl[0]) < 6.0, f"{key} 半周期右腿应接近起点左腿（交替迈步）"
    # 攻击三相：前摇（武器后举）-> 命中（前挥）-> 收招：主手臂在三个相边界上的姿势互不相同，且前摇终点与命中终点相差显著
    c = _clip(spec, "attack.1h")
    ua = rot_series("attack.1h", "upper_arm_r")
    bounds, acc = [0], 0
    for n in c["frames_per_phase"]:
        acc += n
        bounds.append(acc)
    windup_end, active_end = ua[bounds[1]], ua[bounds[2]]
    assert angle_between(ua[0], windup_end) > 20.0, "前摇应有可见的举臂"
    assert angle_between(windup_end, active_end) > 40.0, "判定相应有可见的挥击"


# --------------------------------------------------------------------------
# 自检抓缺陷
# --------------------------------------------------------------------------

def test_check_flags_missing_required_key(tree):
    assets, data = tree
    _mutate_spec(assets, lambda s: s["clips"].remove(_clip(s, "hit")))
    errs = _errors(*tree)
    assert _has(errs, "必备键缺失：hit")
    assert _has(errs, "键集合与 sprite 版配置不一致")


def test_check_flags_missing_event_and_event_not_from_sprite_source(tree):
    assets, data = tree

    def drop_hit(s):
        c = _clip(s, "attack.1h")
        c["events"] = [e for e in c["events"] if e["name"] != "hit"]

    _mutate_spec(assets, drop_hit)
    errs = _errors(*tree)
    assert _has(errs, "attack.1h 攻击类缺 active_start/active_end/hit")
    assert _has(errs, "attack.1h 命名事件与 sprite 版不同源")


def test_check_flags_missing_footstep(tree):
    assets, data = tree

    def drop(s):
        c = _clip(s, "move.run")
        c["events"] = [e for e in c["events"] if e["name"] != "footstep"]

    _mutate_spec(assets, drop)
    assert _has(_errors(*tree), "move.run 缺 footstep")


def test_check_flags_frame_count_and_keyframe_count_mismatch(tree):
    assets, data = tree

    def bad(s):
        c = _clip(s, "attack.2h")
        c["frame_count"] += 1

    _mutate_spec(assets, bad)
    assert _has(_errors(*tree), "attack.2h 帧数与参数推算不一致")

    shutil.copy(REPO_ROOT / SPEC_REL, assets / C.SPEC_FILE)

    def drop_key(s):
        c = _clip(s, "move.walk")
        c["times_ms"].pop(3)
        for t in c["tracks"]:
            if "rot" in t:
                del t["rot"][12:16]
            else:
                del t["pos"][9:12]

    _mutate_spec(assets, drop_key)
    errs = _errors(*tree)
    assert _has(errs, "move.walk 关键帧数")
    assert _has(errs, "关键帧时刻与 sprite 版帧划分不一致") or _has(errs, "关键帧数")


def test_check_flags_phase_mismatch_with_feel_starting_points(tree):
    assets, data = tree

    def bad(s):
        c = _clip(s, "attack.1h")
        c["phases"][0]["ms"] = 115

    _mutate_spec(assets, bad)
    assert _has(_errors(*tree), "attack.1h 三相与 sprite 版不一致")


def test_check_flags_track_path_not_in_skeleton(tree):
    assets, data = tree

    def bad(s):
        c = _clip(s, "idle")
        c["tracks"][2]["path"] = "hips/spine/not_a_bone"

    _mutate_spec(assets, bad)
    errs = _errors(*tree)
    assert _has(errs, "轨迹路径 hips/spine/not_a_bone 不在骨架里")
    assert _has(errs, "旋转轨迹路径与骨架不一致")


def test_check_flags_joint_angle_over_limit_and_fk_mismatch(tree):
    assets, data = tree

    def bad(s):
        c = _clip(s, "idle")
        t = next(t for t in c["tracks"] if t["path"] == C.bone_path("forearm_r"))
        # 把第 2 帧前臂转 170 度（肘反折/过伸量级）：既越角限，又与 sprite 版关节位置对不上
        q = rig.mat_to_quat(rig.rx(-170.0))
        t["rot"][8:12] = [round(v, 6) for v in q]

    _mutate_spec(assets, bad)
    errs = _errors(*tree)
    assert _has(errs, "关节角") and _has(errs, "超过上限")
    assert _has(errs, "正向运动学位置与 sprite 版关节位置相差")


def test_check_flags_non_unit_and_hemisphere_flipped_quaternion(tree):
    assets, data = tree

    def bad(s):
        c = _clip(s, "idle")
        t = next(t for t in c["tracks"] if t["path"] == C.bone_path("spine"))
        t["rot"][8:12] = [-v for v in t["rot"][8:12]]            # 同一旋转取反：插值会绕远路
        t["rot"][12:16] = [2 * v for v in t["rot"][12:16]]       # 非单位长

    _mutate_spec(assets, bad)
    errs = _errors(*tree)
    assert _has(errs, "不在同一半球")
    assert _has(errs, "不是单位长")


def test_check_flags_data_row_mismatch(tree):
    assets, data = tree
    path = data / "display" / "display.anim_set.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    row = next(r for r in doc["rows"] if r["id"] == C.ANIM_SET_ID)
    row["clips"]["attack.1h"]["events"][0]["time_pct"] = 0.99
    del row["clips"]["idle.2h"]
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    errs = _errors(*tree)
    assert _has(errs, "数据行键集合与规格不一致")
    assert _has(errs, "数据行与规格不一致：attack.1h")


def test_check_flags_hand_edited_spec_that_is_not_reproducible(tree):
    assets, data = tree

    def bad(s):
        _clip(s, "idle")["tracks"][0]["pos"][1] += 0.001

    _mutate_spec(assets, bad)
    assert _has(_errors(*tree), "规格文件与当前代码重新生成的结果不一致")


# --------------------------------------------------------------------------
# 引擎资产与规格一致（解析 Unity YAML 文本）
# --------------------------------------------------------------------------

@pytest.fixture()
def unity_copy(tmp_path):
    """Resources/GameFoundation 里本批资产的临时副本，目录结构同 adapters/unity。"""
    dst = tmp_path / "unity" / C.UNITY_RESOURCES
    (dst / "models").mkdir(parents=True)
    (dst / C.CLIP_DIR_REL).mkdir(parents=True)
    for suffix in (".prefab", ".prefab.meta", ".controller", ".controller.meta"):
        shutil.copy(UNITY_RES / "models" / f"{C.MODEL_NAME}{suffix}", dst / "models")
    for p in (UNITY_RES / C.CLIP_DIR_REL).glob(SC.STEM_PREFIX + "*"):
        shutil.copy(p, dst / C.CLIP_DIR_REL)
    return tmp_path / "unity"


def _engine_errors(tree, unity):
    assets, data = tree
    return _errors(assets, data, unity)


def test_engine_assets_copy_passes(tree, unity_copy):
    assert _engine_errors(tree, unity_copy) == []


def test_check_flags_prefab_bone_renamed(tree, unity_copy):
    prefab = unity_copy / C.UNITY_RESOURCES / C.PREFAB_REL
    text = prefab.read_text(encoding="utf-8")
    assert "m_Name: forearm_l\n" in text
    prefab.write_text(text.replace("m_Name: forearm_l\n", "m_Name: forearm_x\n"), encoding="utf-8", newline="\n")
    assert _has(_engine_errors(tree, unity_copy), "预制体骨骼/块体路径与规格不匹配")


def test_check_flags_clip_asset_curve_path_event_and_duration_mismatch(tree, unity_copy):
    clips = unity_copy / C.UNITY_RESOURCES / C.CLIP_DIR_REL
    p = clips / "std_dummy_attack_1h.anim"
    text = p.read_text(encoding="utf-8")
    assert "  path: hips/spine/head\n" in text or "    path: hips/spine/head\n" in text
    text2 = re.sub(r"(\n\s+path: )hips/spine/head\n", r"\1hips/spine/head_x\n", text)
    text2 = re.sub(r"(m_StopTime: )[-0-9.eE+]+", r"\g<1>9.99", text2)
    text2 = text2.replace("    data: hit_frame\n", "    data: hit_frame_x\n")
    p.write_text(text2, encoding="utf-8", newline="\n")
    errs = _engine_errors(tree, unity_copy)
    assert _has(errs, "attack.1h 剪辑资产曲线路径与规格不一致")
    assert _has(errs, "attack.1h 剪辑资产时长")
    assert _has(errs, "attack.1h 剪辑资产内嵌事件与规格不一致")


def test_check_flags_missing_clip_asset_and_controller_state(tree, unity_copy):
    clips = unity_copy / C.UNITY_RESOURCES / C.CLIP_DIR_REL
    (clips / "std_dummy_death.anim").unlink()
    errs = _engine_errors(tree, unity_copy)
    assert _has(errs, "death 缺剪辑资产 std_dummy_death.anim")
