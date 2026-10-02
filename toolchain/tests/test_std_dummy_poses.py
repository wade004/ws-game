"""``toolchain/gen_std_dummy_poses.py``（框架级假人姿势集，sprite 型）的测试。

覆盖：04 §3 键清单 × 全方向档、命名事件、帧数规则（含 fps 参数化）、与 05 §9 试调起点一致、确定性、
自检能抓出缺陷（缺文件/缺事件/帧数不一致/必备键缺失），以及仓库已入库产物自检通过。
临时输出一律写 tmp_path，不触碰仓库内的 assets/data。

运行：``python -m pytest toolchain/tests/test_std_dummy_poses.py -q``
"""

from __future__ import annotations

import hashlib
import json
import shutil
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from std_dummy_poses import config as C  # noqa: E402
from std_dummy_poses.build import SPEC_FILE, build_spec, clip_dir, generate  # noqa: E402
from std_dummy_poses.verify import REQUIRED_KEYS, parse_key, verify  # noqa: E402

REPO_ROOT = TOOLCHAIN_DIR.parent

#: 04 §2 武器族清单里持械的全部族（独立于 config 的字面清单）。
WEAPON_FAMILIES = ("1h", "2h", "polearm", "bow", "staff", "dual", "shield")
NEW_FAMILIES = ("polearm", "bow", "staff", "dual", "shield")
#: 手感落地 M3-D 之前已入库的 34 个键（顺序即规格里的顺序）：本次只追加，既有键的规格条目不变。
LEGACY_KEYS = [
    "idle", "idle.combat", "move.walk", "move.run", "move.run.combat", "idle.1h", "idle.combat.1h", "move.walk.1h",
    "move.run.1h", "move.run.combat.1h", "idle.2h", "idle.combat.2h", "move.walk.2h", "move.run.2h",
    "move.run.combat.2h", "attack.unarmed", "attack.unarmed.02", "attack.unarmed.03", "attack.1h", "attack.1h.02",
    "attack.1h.03", "attack.2h", "attack.2h.02", "attack", "hit", "hit.light", "hit.heavy", "hit.knockback",
    "hit.knockdown", "hit.getup", "death", "jump", "cast", "dodge",
]
#: 既有 34 个键的规格条目（不含新增的 hit_frame 事件）的摘要，取自追加前的入库版本。
LEGACY_ENTRIES_SHA256 = "e044cc9b56dbb855a9112f47b1d764bedfb4a4f1179ffd2588a6b2b69a9561e2"


@pytest.fixture(scope="module")
def gen4(tmp_path_factory):
    """4 方向（3 个 canonical 档）输出，速度快；覆盖全部键与逐层剪辑。"""
    root = tmp_path_factory.mktemp("dummy4")
    assets, data = root / "assets", root / "data"
    spec = generate(assets, data, direction_count=4, log=lambda *_: None)
    return assets, data, spec


def _errors(assets, data):
    return verify(assets, data).errors


def test_committed_tree_passes_self_check():
    rep = verify(REPO_ROOT / "assets" / "_placeholder", REPO_ROOT / "data" / "_framework")
    assert rep.errors == []
    assert rep.warnings == []


def test_generated_4_direction_set_passes_self_check(gen4):
    assets, data, _spec = gen4
    rep = verify(assets, data)
    assert rep.errors == []
    assert rep.warnings == []


def test_required_and_recommended_keys_times_all_directions(gen4):
    assets, _data, spec = gen4
    clips = spec["clips"]
    for k in REQUIRED_KEYS:
        assert k in clips
    slots = spec["directions"]["canonical"]
    assert slots == ["front", "side_r", "back"]
    assert len(slots) + len(spec["directions"]["mirror_pairs"]) == 4
    for key, e in clips.items():
        if e.get("alias_of"):
            continue
        for slot in slots:
            for layer in e["layers"]:
                assert (clip_dir(assets, e["resource_ref"], slot, layer) / "frames.json").is_file(), (key, slot, layer)
            assert (clip_dir(assets, e["resource_ref"], slot) / "atlas.png").is_file()
    # 武器层出现在持械族剪辑与无族的状态剪辑（hit.*/death/jump/cast/dodge/stunned/block），徒手 idle/move/attack 没有
    state_keys = ("hit", "hit.light", "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup",
                  "death", "jump", "cast", "dodge", "hit.launch", "stunned", "block")
    for key, e in clips.items():
        has = "hand_main" in e["layers"]
        assert has == (e["family"] in WEAPON_FAMILIES or key in state_keys), key
        assert has == (e["weapon_layer_family"] is not None), key


def test_all_keys_follow_pose_key_syntax():
    for c in C.build_clip_defs():
        info = parse_key(c.key)
        assert info is not None, c.key
        assert info["state"] in {"idle", "move", "attack", "cast", "hit", "death", "jump", "dodge", "stunned", "block"}


def test_phases_match_feel_05_section9_starting_points(gen4):
    clips = gen4[2]["clips"]
    assert [p["ms"] for p in clips["attack.1h"]["phases"]] == [110, 80, 190]
    assert [p["ms"] for p in clips["attack.2h"]["phases"]] == [170, 100, 290]
    assert clips["attack"]["alias_of"] == "attack.unarmed"
    cancel = {k: [e["time_pct"] for e in clips[k]["events"] if e["name"] == "cancel_open:dodge"]
              for k in ("attack.1h", "attack.2h")}
    assert cancel == {"attack.1h": [0.65], "attack.2h": [0.75]}


def test_frame_counts_follow_rule_and_fps_parameter():
    assert C.frames_per_phase(110, 20) == 2 and C.frames_per_phase(80, 20) == 2 and C.frames_per_phase(190, 20) == 4
    assert C.frames_per_phase(170, 20) == 3 and C.frames_per_phase(100, 20) == 2 and C.frames_per_phase(290, 20) == 6
    assert C.frames_per_phase(1, 20) == 1  # 下限 1 帧
    for fps in (10, 20, 30, 60):
        spec = build_spec(8, fps, True)
        for key, e in spec["clips"].items():
            c = {x.key: x for x in C.build_clip_defs()}[key]
            assert e["frame_count"] == sum(C.frames_per_phase(ms, fps) for _n, ms in c.phases), (fps, key)
        assert spec["clips"]["move.walk"]["frame_count"] == round(1.0 * fps)


def test_walk_run_cycle_displacement_and_footstep_spacing(gen4):
    clips = gen4[2]["clips"]
    walk, run = clips["move.walk"], clips["move.run"]
    assert walk["cycle_displacement_bh"] == pytest.approx(C.REFERENCE_BASE_SPEED_BH_PER_S * C.WALK_SPEED_RATIO * 1.0)
    assert run["cycle_displacement_bh"] == pytest.approx(C.REFERENCE_BASE_SPEED_BH_PER_S * C.RUN_SPEED_RATIO * 0.6)
    for e in (walk, run):
        fs = [x["time_pct"] for x in e["events"] if x["name"] == "footstep"]
        assert fs == [0.0, 0.5]
        # 每步位移 = 每循环位移 / 步数（04 §9 第 3 条）
        assert e["step_displacement_bh"] * len(fs) == pytest.approx(e["cycle_displacement_bh"])


def test_direction_slots_16_and_8_match_import_tool():
    from asset_import.directions import all_slot_names, canonical_slot_names
    for n in (4, 8, 16):
        spec = build_spec(n, 20, True)
        assert spec["directions"]["canonical"] == canonical_slot_names(n)
        mirrors = [m["direction_slot"] for m in spec["directions"]["mirror_pairs"]]
        assert sorted(spec["directions"]["canonical"] + mirrors) == sorted(all_slot_names(n))


def test_generation_is_deterministic(tmp_path):
    def snapshot(root):
        out = {}
        for p in sorted(root.rglob("*")):
            if p.is_file():
                out[p.relative_to(root).as_posix()] = hashlib.sha256(p.read_bytes()).hexdigest()
        return out

    for name in ("a", "b"):
        generate(tmp_path / name / "assets", tmp_path / name / "data", direction_count=4,
                 composite_dirs=False, log=lambda *_: None)
    assert snapshot(tmp_path / "a") == snapshot(tmp_path / "b")


# --- 自检要能抓出缺陷（复现 + 不变量）---

def _copy(gen4, tmp_path):
    assets, data, _ = gen4
    a, d = tmp_path / "assets", tmp_path / "data"
    shutil.copytree(assets, a)
    shutil.copytree(data, d)
    return a, d


def _rewrite_spec(assets, mutate):
    p = assets / SPEC_FILE
    spec = json.loads(p.read_text(encoding="utf-8"))
    mutate(spec)
    p.write_text(json.dumps(spec, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def test_check_flags_missing_layer_file(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    spec = gen4[2]
    shutil.rmtree(clip_dir(a, spec["clips"]["attack.1h"]["resource_ref"], "side_r", "hand_main"))
    assert any("attack.1h" in e and "hand_main/side_r" in e for e in _errors(a, d))


def test_check_flags_missing_footstep_and_data_mismatch(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    _rewrite_spec(a, lambda s: s["clips"]["move.walk"].update(
        events=[e for e in s["clips"]["move.walk"]["events"] if e["name"] != "footstep"]))
    errs = _errors(a, d)
    assert any("move.walk" in e and "footstep" in e for e in errs)
    assert any("数据行与规格不一致" in e and "move.walk" in e for e in errs)


def test_check_flags_attack_without_hit_and_hit_outside_active(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    _rewrite_spec(a, lambda s: s["clips"]["attack.2h"].update(
        events=[e for e in s["clips"]["attack.2h"]["events"] if e["name"] != "hit"]))
    assert any("attack.2h" in e and "hit" in e for e in _errors(a, d))
    b, d2 = _copy(gen4, tmp_path / "b")

    def move_hit(s):
        for e in s["clips"]["attack.1h"]["events"]:
            if e["name"] == "hit":
                e["time_pct"] = 0.99
    _rewrite_spec(b, move_hit)
    assert any("attack.1h" in e and "判定相" in e for e in _errors(b, d2))


def test_check_flags_dodge_without_invuln_and_missing_required_key(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    _rewrite_spec(a, lambda s: s["clips"]["dodge"].update(
        events=[e for e in s["clips"]["dodge"]["events"] if e["name"] != "invuln_end"]))
    assert any("dodge" in e and "无敌" in e for e in _errors(a, d))
    b, d2 = _copy(gen4, tmp_path / "b")
    _rewrite_spec(b, lambda s: s["clips"].pop("death"))
    assert any("必备键缺失：death" in e for e in _errors(b, d2))


def test_check_flags_frame_count_mismatch(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    spec = gen4[2]
    fj = clip_dir(a, spec["clips"]["move.run"]["resource_ref"]) / "frames.json"
    doc = json.loads(fj.read_text(encoding="utf-8"))
    doc["frames"].pop()
    fj.write_text(json.dumps(doc), encoding="utf-8", newline="\n")
    assert any("move.run" in e and "帧数" in e for e in _errors(a, d))


# --- 武器层逐层剪辑（遗留：hit/death/jump/cast/dodge 的武器层原是静态图）---

STATE_KEYS = ("hit", "hit.light", "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup",
              "death", "jump", "cast", "dodge", "hit.launch", "stunned", "block")


def _frame_bytes(clip_dir_path):
    from PIL import Image
    doc = json.loads((clip_dir_path / "frames.json").read_text(encoding="utf-8"))
    with Image.open(clip_dir_path / "atlas.png") as im:
        im.load()
        return doc, [im.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])).tobytes() for f in doc["frames"]]


def test_state_clips_have_weapon_layer_that_follows_body_frames(gen4):
    """不变量：持械角色播 hit/death/jump/cast/dodge 时武器层与身体层同帧数、同逐帧时长，且武器层逐帧变化。"""
    assets, _data, spec = gen4
    for key in STATE_KEYS:
        e = spec["clips"][key]
        assert "hand_main" in e["layers"], key
        for slot in spec["directions"]["canonical"]:
            body_doc, body_frames = _frame_bytes(clip_dir(assets, e["resource_ref"], slot, "body"))
            wp_doc, wp_frames = _frame_bytes(clip_dir(assets, e["resource_ref"], slot, "hand_main"))
            assert len(wp_frames) == len(body_frames) == e["frame_count"], (key, slot)
            assert [f["duration"] for f in wp_doc["frames"]] == [f["duration"] for f in body_doc["frames"]], (key, slot)
            # 身体帧有变化 => 武器帧也有变化（不是同一张静态图重复）
            assert len(set(body_frames)) > 1 and len(set(wp_frames)) > 1, (key, slot)


def test_state_clip_weapon_layers_are_not_composited_into_body_or_whole_body_clip(gen4):
    """不变量：整身合成与身体层仍是徒手姿势（武器只在 hand_main 层），否则无纸娃娃层的外形会凭空多一把剑。"""
    from PIL import Image
    assets, _data, spec = gen4
    ref = spec["clips"]["hit"]["resource_ref"]
    with Image.open(clip_dir(assets, ref, "side_r") / "atlas.png") as whole,             Image.open(clip_dir(assets, ref, "side_r", "body") / "atlas.png") as body:
        assert whole.tobytes() == body.tobytes()


def test_check_flags_static_weapon_layer_and_missing_weapon_layer(gen4, tmp_path):
    from PIL import Image
    a, d = _copy(gen4, tmp_path)
    spec = gen4[2]
    # 复现：把 hit 的武器层逐帧改成第 0 帧的重复（回到修复前"静态图"的状态）
    wd = clip_dir(a, spec["clips"]["hit"]["resource_ref"], "side_r", "hand_main")
    doc = json.loads((wd / "frames.json").read_text(encoding="utf-8"))
    with Image.open(wd / "atlas.png") as im:
        im.load()
        f0 = doc["frames"][0]
        first = im.crop((f0["x"], f0["y"], f0["x"] + f0["w"], f0["y"] + f0["h"]))
        out = im.copy()
        for f in doc["frames"]:
            out.paste(first, (f["x"], f["y"]))
    out.save(wd / "atlas.png")
    assert any("hit" in e and "武器层" in e and "没有随身体帧走" in e for e in _errors(a, d))
    # 缺武器层逐层剪辑：清单判据报错
    b, d2 = _copy(gen4, tmp_path / "b")
    _rewrite_spec(b, lambda s: s["clips"]["cast"].update(layers=["body"], weapon_layer_family=None))
    assert any("cast" in e and "缺武器层" in e for e in _errors(b, d2))


# --- 手感落地 M3-D：可选键、五个新武器族、变体、hit_frame、既有键只追加 ---

def test_legacy_keys_are_append_only_and_entries_unchanged(gen4):
    """不变量：既有 34 个键仍在最前且顺序不变，条目（除新增的 hit_frame 事件外）逐字段不变。"""
    clips = gen4[2]["clips"]
    keys = list(clips)
    assert keys[:len(LEGACY_KEYS)] == LEGACY_KEYS
    assert len(keys) > len(LEGACY_KEYS)
    fields = ("resource_ref", "tier", "family", "loop", "phases", "frames_per_phase", "frame_count", "total_ms", "events",
              "layers", "weapon_layer_family", "alias_of", "step_displacement_bh", "cycle_displacement_bh")
    rows = []
    for k in LEGACY_KEYS:
        e = {f: clips[k][f] for f in fields if f in clips[k]}
        e["events"] = [x for x in e["events"] if x["name"] != "hit_frame"]
        rows.append([k, e])
    blob = json.dumps(rows, sort_keys=True, ensure_ascii=False)
    assert hashlib.sha256(blob.encode()).hexdigest() == LEGACY_ENTRIES_SHA256


def test_optional_keys_present_with_declared_semantics(gen4):
    clips = gen4[2]["clips"]
    for k in ("move.sprint", "move.walk.combat", "move.start", "move.stop", "move.pivot", "hit.launch", "stunned", "block",
              "idle.wounded", "idle.combat.wounded", "move.walk.wounded", "move.run.wounded"):
        assert k in clips, k
    # 启停过渡：非循环、各一次脚触地；带伤变体是循环剪辑，周期不短于基础键（蹒跚更慢）
    for k in ("move.start", "move.stop", "move.pivot"):
        assert clips[k]["loop"] is False and clips[k]["transition"] == k.split(".")[1]
        assert [x["name"] for x in clips[k]["events"]] == ["footstep"]
    assert clips["stunned"]["loop"] is True and clips["block"]["loop"] is True
    assert clips["hit.launch"]["loop"] is False
    assert [x["name"] for x in clips["hit.launch"]["events"]] == ["impact"]
    for k, base in (("idle.wounded", "idle"), ("idle.combat.wounded", "idle.combat"),
                    ("move.walk.wounded", "move.walk"), ("move.run.wounded", "move.run")):
        assert clips[k]["variant"] == "wounded"
        assert clips[k]["loop"] is True and clips[k]["total_ms"] >= clips[base]["total_ms"]


def test_sprint_is_faster_than_run_and_combat_sprint_aliases_peace_sprint(gen4):
    clips = gen4[2]["clips"]
    spr, run = clips["move.sprint"], clips["move.run"]
    # 每步位移 = 参考基础移速 × 冲刺倍率 × 周期 / 2（与走/跑同一公式）；冲刺周期更短、每步位移更大
    want = C.REFERENCE_BASE_SPEED_BH_PER_S * C.SPRINT_SPEED_RATIO * spr["total_ms"] / 1000.0 / 2.0
    assert spr["step_displacement_bh"] == pytest.approx(want, abs=1e-3)
    assert spr["total_ms"] < run["total_ms"] and spr["step_displacement_bh"] > run["step_displacement_bh"]
    assert [x["time_pct"] for x in spr["events"] if x["name"] == "footstep"] == [0.0, 0.5]
    # 回落链：冲刺请求先走完带 sprint 的候选才会改用 run，所以每个战斗姿态/武器族的 sprint 键都必须存在（别名复用和平姿态剪辑）
    assert clips["move.sprint.combat"]["alias_of"] == "move.sprint"
    for fam in WEAPON_FAMILIES:
        assert clips[f"move.sprint.combat.{fam}"]["alias_of"] == f"move.sprint.{fam}"
        assert clips[f"move.sprint.{fam}"]["family"] == fam and "alias_of" not in clips[f"move.sprint.{fam}"]


def test_wounded_stride_is_shorter_than_base(gen4):
    clips = gen4[2]["clips"]
    for g in ("walk", "run"):
        w, b = clips[f"move.{g}.wounded"], clips[f"move.{g}"]
        assert w["stride_factor"] < 1.0
        assert w["step_displacement_bh"] == pytest.approx(b["step_displacement_bh"] * w["stride_factor"], abs=1e-3)


def test_five_new_weapon_families_follow_1h_2h_convention(gen4):
    clips = gen4[2]["clips"]
    for fam in NEW_FAMILIES:
        for base in ("idle", "idle.combat", "move.walk", "move.run", "move.run.combat", "move.walk.combat", "move.sprint"):
            e = clips[f"{base}.{fam}"]
            assert e["family"] == fam and "hand_main" in e["layers"], (fam, base)
        atk = clips[f"attack.{fam}"]
        assert atk["family"] == fam and tuple(p["ms"] for p in atk["phases"]) == C.PHASES_SEG1[fam]
        names = [x["name"] for x in atk["events"]]
        for need in ("active_start", "hit", "active_end", "hit_frame", "cancel_open:dodge"):
            assert need in names, (fam, need)
    # 弓：放箭点 release 与 hit 同刻
    bow = clips["attack.bow"]["events"]
    assert [x["time_pct"] for x in bow if x["name"] == "release"] == [x["time_pct"] for x in bow if x["name"] == "hit"]
    # 连招：长柄/法杖/双持有多段，弓/盾单段
    assert "attack.polearm.02" in clips and "attack.staff.02" in clips and "attack.dual.03" in clips
    assert "attack.bow.02" not in clips and "attack.shield.02" not in clips


def test_hit_frame_event_same_time_as_hit_on_every_attack_clip_and_data_row(gen4):
    """不变量：每个攻击剪辑恰有一条 hit_frame，与 hit 同刻；数据行里同样带它（引擎按数据行事件名换算命中帧关键帧）。"""
    _assets, data, spec = gen4
    doc = json.loads((data / "display" / "display.anim_set.json").read_text(encoding="utf-8"))
    row = next(r for r in doc["rows"] if r["id"] == "display.anim_set.std_dummy_biped")
    n = 0
    for key, e in spec["clips"].items():
        info = parse_key(key)
        if e.get("alias_of") or info["state"] != "attack":
            continue
        hits = [x["time_pct"] for x in e["events"] if x["name"] == "hit"]
        hf = [x["time_pct"] for x in e["events"] if x["name"] == "hit_frame"]
        assert len(hits) == 1 and hf == hits, key
        assert [x for x in row["clips"][key]["events"] if x["name"] == "hit_frame"] == [{"name": "hit_frame", "time_pct": hits[0]}]
        n += 1
    assert n == 17  # 徒手3 + 1h 3 + 2h 2 + 长柄2 + 弓1 + 法杖2 + 双持3 + 盾1


def test_check_flags_missing_hit_frame_and_missing_family_key(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    _rewrite_spec(a, lambda s: s["clips"]["attack.bow"].update(
        events=[e for e in s["clips"]["attack.bow"]["events"] if e["name"] != "hit_frame"]))
    assert any("attack.bow" in e and "hit_frame" in e for e in _errors(a, d))
    b, d2 = _copy(gen4, tmp_path / "b")
    _rewrite_spec(b, lambda s: s["clips"].pop("move.sprint.polearm"))
    assert any("武器族 polearm 缺键：move.sprint.polearm" in e for e in _errors(b, d2))


def test_check_flags_missing_optional_key(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    _rewrite_spec(a, lambda s: s["clips"].pop("hit.launch"))
    assert any("可选键缺失" in e and "hit.launch" in e for e in _errors(a, d))
