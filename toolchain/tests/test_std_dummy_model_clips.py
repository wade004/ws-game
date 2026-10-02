"""``toolchain/gen_std_dummy_model_clips.py``（框架级假人姿势集，model 型 / 骨骼剪辑）的测试。

覆盖：04 §3 键清单、与 sprite 版同源（键集合/事件/时长/三相）、05 §9 三相起点、帧数规则（含 fps 参数化）、确定性、
关节角限与正向运动学对账、骨骼路径与预制体一致、自检能抓出缺陷（缺键/缺事件/帧数不一致/骨骼路径不匹配/关节角越限/
数据行不一致/引擎资产与规格不一致），以及仓库已入库产物（规格 + 数据行 + 引擎资产）自检通过。
临时输出一律写 tmp_path，不触碰仓库内的 assets/data/adapters。

运行：``python -m pytest toolchain/tests/test_std_dummy_model_clips.py -q``
"""

from __future__ import annotations

import hashlib
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
#: 手感落地 M3-D 之前已入库的 34 个键的模型剪辑条目（时间、轨迹、内嵌事件等）摘要：既有键逐字节不变（只追加）。
LEGACY_CLIPS_SHA256 = "5748f15e2e32037104db8ceabeeb83caf536484983cd0af491f9c73a0127e423"
LEGACY_CLIP_COUNT = 34
#: 手感落地 M4-D 之前已入库的 103 个键（主集）与 16 条体量组条目（轻/重各 8 键）的模型剪辑摘要（取自 1.95.0 入库版本）：只追加不改。
LEGACY103_CLIPS_SHA256 = "9117f7e3bd1fbfe8f6d664ca9087543d262aec6ed376dcffdd3df6a7e233f8cd"
LEGACY103_COUNT = 103
LEGACY_MASS_ENTRIES_SHA256 = "7b4a4308cde5b6c9d1ff452b3058cf5951c5d24f600b6f4ebf9c19bd3d91f457"
_CLIP_FIELDS = ("resource_ref", "state", "alias_of", "times_ms", "tracks", "anim_events", "total_ms", "frame_count", "loop",
                "phases", "tier", "family")
#: 既有动画控制器 .meta 的 guid：重新生成必须沿用。
CONTROLLER_GUID = "da377b4a4ff6d3649a389b3bdb6b5e5b"
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
    spec = _repo_spec()
    n_real = sum(1 for c in spec["clips"] if "alias_of" not in c)
    assert rep.counts.get("引擎资产核对", 0) == n_real * (1 + len(SC.MASS_TIERS))   # 主集 + 每个体量档各一份全键剪辑
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
    for fam in ("1h", "2h", "polearm", "bow", "staff", "dual", "shield"):
        for base in ("idle", "idle.combat", "move.walk", "move.run", "move.run.combat", "move.walk.combat", "move.sprint"):
            assert f"{base}.{fam}" in keys
    for fam in ("unarmed", "1h", "2h", "polearm", "bow", "staff", "dual", "shield"):
        assert f"attack.{fam}" in keys
    for k in ("move.sprint", "move.start", "move.stop", "move.pivot", "hit.launch", "stunned", "block",
              "idle.wounded", "idle.combat.wounded", "move.walk.wounded", "move.run.wounded"):
        assert k in keys
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
                # 数据行事件自带 hit_frame（sprite 版同源）时不重复烘：每个攻击剪辑恰一条
                assert len(baked["hit_frame"]) == 1, c["key"]
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
                # 四元数 q 与 -q 是同一旋转：整身翻滚一整圈后髋的末帧是 -q（保持半球连续），按至多差一个符号比较
                assert v[:w] == v[-w:] or ("rot" in t and v[:w] == [-x + 0.0 for x in v[-w:]]), (c["key"], t["path"])


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
                assert rig.quat_angle_deg(tuple(t["rot"][i:i + 4])) <= limit + 1e-3, (c["key"], name)  # 四元数保留 6 位小数的舍入余量
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


# --------------------------------------------------------------------------
# 手感落地 M3-D：既有键只追加、可选键/五个新族的骨骼剪辑、体量组（extends）、确定性控制器
# --------------------------------------------------------------------------

def test_legacy_clips_unchanged_and_appended_only():
    """不变量：既有 34 个键的模型剪辑（时间、轨迹、内嵌事件）逐字节不变，新键只追加在其后；骨骼不变。"""
    spec = _repo_spec()
    clips = spec["clips"]
    assert len(clips) > LEGACY_CLIP_COUNT
    fields = ("resource_ref", "state", "alias_of", "times_ms", "tracks", "anim_events", "total_ms", "frame_count", "loop",
              "phases", "tier", "family")
    rows = [[c["key"], {k: c[k] for k in fields if k in c}] for c in clips[:LEGACY_CLIP_COUNT]]
    blob = json.dumps(rows, sort_keys=True, ensure_ascii=False)
    assert hashlib.sha256(blob.encode()).hexdigest() == LEGACY_CLIPS_SHA256


def test_new_keys_have_tracks_for_every_rotation_bone_and_hips_position():
    spec = _repo_spec()
    for c in spec["clips"]:
        if "alias_of" in c:
            continue
        paths_rot = {t["path"] for t in c["tracks"] if "rot" in t}
        assert paths_rot == {C.bone_path(b) for b in C.ROT_BONES}, c["key"]
        assert [t["path"] for t in c["tracks"] if "pos" in t] == [C.bone_path("hips")], c["key"]


def test_sprint_start_stop_launch_stunned_block_and_wounded_motion_is_recognizable():
    spec = _repo_spec()

    def series(key, bone):
        t = next(t for t in _clip(spec, key)["tracks"] if t["path"] == C.bone_path(bone))
        return [tuple(t["rot"][i:i + 4]) for i in range(0, len(t["rot"]), 4)]

    def swing(key, bone="thigh_r"):
        q = series(key, bone)
        return max(math.degrees(2 * math.acos(min(1.0, abs(rig.quat_dot(q[0], x))))) for x in q)

    # 冲刺的迈步幅度大于跑；带伤的小于基础
    assert swing("move.sprint") > swing("move.run") > swing("move.walk")
    assert swing("move.walk.wounded") < swing("move.walk") or swing("move.run.wounded") < swing("move.run")
    # 启动：起点接近待机、终点接近跑起点（躯干前倾增大）；急停相反
    assert rig.quat_dot(series("move.start", "spine")[0], series("idle", "spine")[0]) > 0.999
    assert rig.quat_dot(series("move.stop", "spine")[-1], series("idle", "spine")[0]) > 0.999
    # 击飞：髋位置抬离地面（空中），随后落地；眩晕与格挡是循环
    pos = next(t for t in _clip(spec, "hit.launch")["tracks"] if "pos" in t)["pos"]
    ys = pos[1::3]
    assert max(ys) > min(ys) + 0.2
    assert _clip(spec, "stunned")["loop"] and _clip(spec, "block")["loop"]


def test_mass_groups_cover_all_keys_extend_main_set_and_only_change_posture():
    spec = _repo_spec()
    groups = {g["mass"]: g for g in spec["mass_groups"]}
    assert list(groups) == list(SC.MASS_TIERS) == ["light", "heavy"]
    main = {c["key"]: c for c in spec["clips"]}
    for mass, g in groups.items():
        assert g["id"] == f"{C.ANIM_SET_ID}_{mass}" and g["extends"] == C.ANIM_SET_ID
        assert {e["key"] for e in g["clips"]} == set(main)         # 覆盖主集全部键（含别名键）
        entries = {e["key"]: e for e in g["clips"]}
        for e in g["clips"]:
            m = main[e["key"]]
            # 步幅轴不出资产：时长、帧数、事件、每步位移与中体量一致；只有站姿（轨迹）不同
            for f in ("total_ms", "frame_count", "loop", "phases", "events", "frames_per_phase"):
                assert e[f] == m[f], (mass, e["key"], f)
            assert e["resource_ref"] != m["resource_ref"]
            if "alias_of" in e:
                assert e["alias_of"] == m["alias_of"]
                assert e["resource_ref"] == entries[e["alias_of"]]["resource_ref"]
            else:
                assert e["state"] == f"std_dummy_{mass}_{e['key'].replace('.', '_')}"
                assert e["tracks"] != m["tracks"], (mass, e["key"])   # 每个键都真的有体量偏移
    # 躯干前倾：重 > 中 > 轻（绕 +X 正向旋转 = 四元数 x 分量；取静态站姿键）
    def spine_x(c):
        return next(t for t in c["tracks"] if t["path"].endswith("/spine"))["rot"][0]
    for key in ("idle", "move.walk", "move.run", "attack.1h", "cast"):
        light = next(e for e in groups["light"]["clips"] if e["key"] == key)
        heavy = next(e for e in groups["heavy"]["clips"] if e["key"] == key)
        assert spine_x(heavy) > spine_x(main[key]) > spine_x(light), key


def test_legacy_mass_group_entries_unchanged():
    """不变量：M3-D 的 8 个体量键（轻/重各 8 条）剪辑逐字节不变：全键覆盖只追加新键，不改既有体量组条目。"""
    spec = _repo_spec()
    rows = []
    for g in spec["mass_groups"]:
        by = {c["key"]: c for c in g["clips"]}
        for k in C.LEGACY_MASS_KEYS:
            rows.append([g["mass"] + "/" + k, {f: by[k][f] for f in _CLIP_FIELDS if f in by[k]}])
    assert hashlib.sha256(json.dumps(rows, sort_keys=True, ensure_ascii=False).encode()).hexdigest() == LEGACY_MASS_ENTRIES_SHA256


def test_legacy_103_main_clips_unchanged_and_appended_only():
    spec = _repo_spec()
    clips = spec["clips"]
    assert len(clips) == 128
    rows = [[c["key"], {f: c[f] for f in _CLIP_FIELDS if f in c}] for c in clips[:LEGACY103_COUNT]]
    assert hashlib.sha256(json.dumps(rows, sort_keys=True, ensure_ascii=False).encode()).hexdigest() == LEGACY103_CLIPS_SHA256


def test_mass_group_rows_in_shared_data_file_extend_main_and_medium_row_is_empty():
    doc = json.loads((REPO_ROOT / "data" / "_framework" / "display" / "display.anim_set.json").read_text(encoding="utf-8"))
    rows = {r["id"]: r for r in doc["rows"]}
    main = rows[C.ANIM_SET_ID]
    assert "extends" not in main
    mid = rows[f"{C.ANIM_SET_ID}_medium"]
    assert mid["extends"] == C.ANIM_SET_ID and mid["clips"] == {}
    for mass in ("light", "heavy"):
        row = rows[f"{C.ANIM_SET_ID}_{mass}"]
        assert row["extends"] == C.ANIM_SET_ID
        assert set(row["clips"]) == set(main["clips"])
        for k, v in row["clips"].items():
            assert v["resource_ref"].startswith(f"anim.std_dummy_{mass}_"), k
            assert "blend_ms" not in v, k            # 混合时长只在主集行声明，体量组按键继承
        assert "blends" not in row


def test_check_flags_mass_group_defects(tree):
    assets, data = tree

    def bad(s):
        g = next(x for x in s["mass_groups"] if x["mass"] == "heavy")
        e = next(x for x in g["clips"] if x["key"] == "move.walk")
        e["total_ms"] += 100                       # 体量组不得改时长
        light = next(x for x in s["mass_groups"] if x["mass"] == "light")
        light["extends"] = "display.anim_set.nope"  # 必须继承主集

    _mutate_spec(assets, bad)
    errs = _errors(*tree)
    assert _has(errs, "体量组 heavy/move.walk 的 total_ms 与主集不一致") or _has(errs, "heavy/move.walk 的 total_ms")
    assert _has(errs, "体量组 light 的 id/extends 不符合约定")


def test_check_flags_mass_group_row_missing_extends_in_data(tree):
    assets, data = tree
    path = data / "display" / "display.anim_set.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    row = next(r for r in doc["rows"] if r["id"] == f"{C.ANIM_SET_ID}_heavy")
    del row["extends"]
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    assert _has(_errors(*tree), "体量组行")


def test_controller_is_deterministic_state_ids_and_guid_is_stable(unity_copy):
    """复现 + 不变量：控制器状态 fileID 取状态名的确定性公式（Unity 自己分配的是随机数，两次生成会抖动），.meta guid 沿用既有值。"""
    ctrl = unity_copy / C.UNITY_RESOURCES / C.CONTROLLER_REL
    text = ctrl.read_text(encoding="utf-8")
    ids = dict((name.strip(), int(fid)) for fid, name in re.findall(
        r"^--- !u!1102 &(-?\d+)[^\n]*\n(?:(?!^---).*\n)*?\s+m_Name:\s*(.*)$", text, flags=re.M))
    spec = _repo_spec()
    assert len(ids) == sum(1 for c in spec["clips"] if "alias_of" not in c) * (1 + len(SC.MASS_TIERS))
    for name, fid in ids.items():
        assert fid == C.controller_state_file_id(name), name
    assert len(set(ids.values())) == len(ids)
    assert C.controller_relay_file_id("std_dummy_idle") != C.controller_state_file_id("std_dummy_idle")
    meta = Path(str(ctrl) + ".meta").read_text(encoding="utf-8")
    assert f"guid: {CONTROLLER_GUID}" in meta


def test_check_flags_non_deterministic_controller_state_id(tree, unity_copy):
    ctrl = unity_copy / C.UNITY_RESOURCES / C.CONTROLLER_REL
    text = ctrl.read_text(encoding="utf-8")
    fid = C.controller_state_file_id("std_dummy_idle")
    assert f"&{fid}\n" in text
    ctrl.write_text(text.replace(f"&{fid}\n", "&1234567890123\n").replace(f"fileID: {fid}}}", "fileID: 1234567890123}"),
                    encoding="utf-8", newline="\n")
    assert _has(_engine_errors(tree, unity_copy), "std_dummy_idle 的 fileID 不是确定性公式的值")


# --------------------------------------------------------------------------
# 手感落地 M4-D：新键、翻滚、过渡混合时长（blend_ms / blends）、体量档数据声明
# --------------------------------------------------------------------------

M4D_KEYS = ("jump.rise", "jump.fall", "jump.land", "hit.air", "attack.air", "attack.air.unarmed", "attack.air.1h",
            "attack.air.2h", "attack.air.polearm", "attack.air.bow", "attack.air.staff", "attack.air.dual",
            "attack.air.shield", "hit.block", "hit.block.shield", "stunned.sway", "hit.launch.tumble", "hit.launch.land",
            "move.sprint.wounded", "move.sprint.combat.wounded", "move.walk.combat.wounded", "move.run.combat.wounded",
            "move.start.wounded", "move.stop.wounded", "move.pivot.wounded")


def _hips_angles(clip: dict) -> list[float]:
    t = next(t for t in clip["tracks"] if t["path"] == C.bone_path("hips") and "rot" in t)
    return [rig.quat_angle_deg(tuple(t["rot"][i:i + 4])) for i in range(0, len(t["rot"]), 4)]


def test_m4d_keys_present_with_tracks_and_every_non_tumble_key_keeps_hips_within_five_degrees():
    """复现：整身翻滚需要髋大角度。不变量：只有翻滚类两个键的髋旋转可大于 5 度，其余键（含全部体量组）不变。"""
    spec = _repo_spec()
    keys = {c["key"] for c in spec["clips"]}
    assert set(M4D_KEYS) <= keys and len(keys) == 128
    allc = list(spec["clips"]) + [c for g in spec["mass_groups"] for c in g["clips"]]
    for c in allc:
        if "alias_of" in c:
            continue
        mx = max(_hips_angles(c))
        if c["key"] in C.BONE_ROT_HIPS_TUMBLE_KEYS:
            continue
        assert mx <= C.HIPS_ROT_LIMIT_DEG + 1e-3, (c["key"], mx)
    tb = _clip(spec, "hit.launch.tumble")
    ang = _hips_angles(tb)
    assert max(ang) > 170.0 and min(ang) < 1.0          # 翻到身体倒置，起止回到直立
    assert tb["loop"] is True


def test_tumble_hips_quaternion_loop_is_sign_insensitive_and_hemisphere_continuous():
    """翻滚一整圈的髋四元数回到 -q（同一旋转）：自检按至多差一个符号比较首尾，半球连续仍须逐帧成立。"""
    spec = _repo_spec()
    t = next(t for t in _clip(spec, "hit.launch.tumble")["tracks"] if t["path"] == C.bone_path("hips") and "rot" in t)
    q = [tuple(t["rot"][i:i + 4]) for i in range(0, len(t["rot"]), 4)]
    assert all(rig.quat_dot(a, b) > 0 for a, b in zip(q, q[1:]))
    first, last = q[0], q[-1]
    assert max(abs(a + b) for a, b in zip(first, last)) < 1e-5 or max(abs(a - b) for a, b in zip(first, last)) < 1e-5


def _row():
    doc = json.loads((REPO_ROOT / "data" / "_framework" / "display" / "display.anim_set.json").read_text(encoding="utf-8"))
    return next(r for r in doc["rows"] if r["id"] == C.ANIM_SET_ID)


def test_data_row_declares_blend_ms_per_key_and_blend_pairs():
    row = _row()
    spec = _repo_spec()
    assert set(row["clips"]) == {c["key"] for c in spec["clips"]}
    for k, v in row["clips"].items():
        assert isinstance(v["blend_ms"], int) and 0 <= v["blend_ms"] <= 2000, k
        assert v["blend_ms"] == SC.blend_ms_for(SC.clip_by_key(k)), k
    # 别名键与目标键同混合时长（同一资源）
    for c in spec["clips"]:
        if "alias_of" in c:
            assert row["clips"][c["key"]]["blend_ms"] == row["clips"][c["alias_of"]]["blend_ms"], c["key"]
    pairs = [(b["from"], b["to"]) for b in row["blends"]]
    assert len(pairs) == len(set(pairs)) and len(pairs) == len(SC.BLEND_PAIRS)
    assert all(a in row["clips"] and b in row["clips"] for a, b in pairs)
    # 每对覆盖值与逐键值不同的才有意义：至少有一对显式覆盖了逐键默认（hit.getup -> idle 180 vs idle 120）
    by = {(b["from"], b["to"]): b["blend_ms"] for b in row["blends"]}
    assert by[("hit.getup", "idle")] != row["clips"]["idle"]["blend_ms"]


def test_blend_is_data_only_on_model_rows_and_sprite_row_has_none():
    doc = json.loads((REPO_ROOT / "data" / "_framework" / "display" / "display.anim_set.json").read_text(encoding="utf-8"))
    spr = next(r for r in doc["rows"] if r["id"] == "display.anim_set.std_dummy_biped")
    assert "blends" not in spr and all("blend_ms" not in v for v in spr["clips"].values())


def test_check_flags_missing_blend_ms_and_bad_blend_pair(tree):
    assets, data = tree
    path = data / "display" / "display.anim_set.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    row = next(r for r in doc["rows"] if r["id"] == C.ANIM_SET_ID)
    del row["clips"]["idle"]["blend_ms"]
    row["blends"][0]["to"] = "no.such.key"
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    errs = _errors(*tree)
    assert _has(errs, "数据行 idle 缺 blend_ms")
    assert _has(errs, "数据行 blends")


def test_check_flags_mass_group_that_repeats_blend_or_misses_a_key(tree):
    assets, data = tree
    path = data / "display" / "display.anim_set.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    row = next(r for r in doc["rows"] if r["id"] == f"{C.ANIM_SET_ID}_heavy")
    row["clips"]["idle"]["blend_ms"] = 10
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    assert _has(_errors(*tree), "不应重复声明 blend_ms")
    _mutate_spec(assets, lambda s: next(g for g in s["mass_groups"] if g["mass"] == "light")["clips"].pop(0))
    assert _has(_errors(*tree), "体量组 light 应覆盖主集全部键")


def test_mass_tiers_are_data_declared_model_side_follows(monkeypatch):
    """在 SC.MASS_TIERS 里加一档，model 规格就多出对应的全键组与数据行（不改代码）。"""
    monkeypatch.setitem(SC.MASS_TIERS, "xheavy", {"t_pitch": 9.0, "h_pitch": -6.0, "sa": 11.0, "ha": 7.0, "react": 0.7, "air": 0.8})
    spec = build_spec(20)
    assert [g["mass"] for g in spec["mass_groups"]] == ["light", "heavy", "xheavy"]
    xg = next(g for g in spec["mass_groups"] if g["mass"] == "xheavy")
    assert len(xg["clips"]) == len(spec["clips"]) and xg["id"] == f"{C.ANIM_SET_ID}_xheavy"
    from std_dummy_model_clips.build import build_mass_data_rows
    assert [r["id"] for r in build_mass_data_rows(spec)][-1] == f"{C.ANIM_SET_ID}_xheavy"
