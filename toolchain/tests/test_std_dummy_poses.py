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
#: 既有 34 个键（M4-W5 起不含 cast：其时间轴为对齐实验室技能命中点已改，见 test_cast_*）的规格条目（不含新增的 hit_frame 事件）的摘要，取自追加前的入库版本。
LEGACY_ENTRIES_SHA256 = "83a39dadef7c8a87233fa79243b3ddbc9fca704082c35f54ce4e78006058ef1f"
#: 手感落地 M4-D 之前已入库的 103 个键的规格条目整体摘要（取自 1.95.0 入库版本）：M4-D 只追加新键，既有 103 个键的规格条目不变。
LEGACY103_ENTRIES_SHA256 = "a8ca087faad778da537d56334f8ef466925f97622b7f7c8a0e1725826e549726"
LEGACY103_COUNT = 103
#: 手感落地 M4-D 追加的键（独立于 config 的字面清单）。
AIR_KEYS = ("jump.rise", "jump.fall", "jump.land", "hit.air", "attack.air", "attack.air.unarmed", "attack.air.1h",
            "attack.air.2h", "attack.air.polearm", "attack.air.bow", "attack.air.staff", "attack.air.dual",
            "attack.air.shield")
DETAIL_KEYS = ("hit.block", "hit.block.shield", "stunned.sway", "hit.launch.tumble", "hit.launch.land")
WOUNDED_KEYS = ("idle.wounded", "idle.combat.wounded", "move.walk.wounded", "move.run.wounded", "move.walk.combat.wounded",
                "move.run.combat.wounded", "move.sprint.wounded", "move.sprint.combat.wounded", "move.start.wounded",
                "move.stop.wounded", "move.pivot.wounded")


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
                  "death", "jump", "cast", "cast.quick", "cast.heavy", "dodge", "hit.launch", "stunned", "block",
                  "jump.rise", "jump.fall", "jump.land", "hit.air", "hit.block", "hit.block.shield", "stunned.sway",
                  "hit.launch.tumble", "hit.launch.land")
    for key, e in clips.items():
        has = "hand_main" in e["layers"]
        assert has == (e["family"] in WEAPON_FAMILIES or key in state_keys), key
        assert has == (e["weapon_layer_family"] is not None), key


def test_all_keys_follow_pose_key_syntax():
    for c in C.build_clip_defs():
        info = parse_key(c.key)
        assert info is not None, c.key
        assert info["state"] in {"idle", "move", "attack", "cast", "hit", "death", "jump", "dodge", "stunned", "block"}


def test_m4d_key_syntax_parses_air_block_launch_and_wounded_transitions():
    """回落链要靠键语法认得 air/sway/tumble/land 与启停过渡的 wounded：解析出的维度必须是预期的。"""
    assert parse_key("attack.air.1h")["family"] == "1h" and parse_key("attack.air.1h")["air"] is True
    assert parse_key("attack.air")["family"] is None and parse_key("attack.air")["air"] is True
    assert parse_key("hit.air")["variant"] == "air" and parse_key("jump.rise")["variant"] == "rise"
    assert parse_key("hit.block.shield")["family"] == "shield" and parse_key("hit.block")["variant"] == "block"
    assert parse_key("hit.launch.tumble")["variant"] == "launch.tumble"
    assert parse_key("stunned.sway")["variant"] == "sway"
    assert parse_key("move.start.wounded")["wounded"] is True and parse_key("move.pivot")["variant"] == "pivot"
    assert parse_key("move.sprint.combat.wounded")["variant"] == "wounded"
    for bad in ("jump.rise.x", "attack.air.nope", "stunned.sway.x", "move.start.nope"):
        assert parse_key(bad) is None, bad


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
              "death", "jump", "cast", "cast.quick", "cast.heavy", "dodge", "hit.launch", "stunned", "block",
              "jump.rise", "jump.fall", "jump.land", "hit.air", "hit.block", "hit.block.shield", "stunned.sway",
              "hit.launch.tumble", "hit.launch.land")


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
        if k == "cast":
            continue
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
    assert n == 25  # 地面 17（徒手3 + 1h 3 + 2h 2 + 长柄2 + 弓1 + 法杖2 + 双持3 + 盾1）+ 空中 8（每族一段）


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


# --- 手感落地 M4-D：既有键只追加 / 体量档覆盖全部键 / 带伤覆盖 / 空中键 / 格挡眩晕击飞细节 / 武器形体 ---

def test_legacy_103_entries_unchanged_and_new_keys_only_appended(gen4):
    """不变量：1.95.0 入库的 103 个键仍在最前、顺序与规格条目逐字段不变；M4-D 键只追加在其后。"""
    clips = gen4[2]["clips"]
    keys = list(clips)
    old = [[k, clips[k]] for k in keys[:LEGACY103_COUNT] if k != "cast"]
    assert hashlib.sha256(json.dumps(old, sort_keys=True, ensure_ascii=False).encode()).hexdigest() == LEGACY103_ENTRIES_SHA256
    assert set(keys[LEGACY103_COUNT:]) == (set(AIR_KEYS) | set(DETAIL_KEYS) | {"cast.quick", "cast.heavy"}
                                           | {k for k in WOUNDED_KEYS if k not in keys[:LEGACY103_COUNT]})
    assert len(keys) == 130


def test_cast_clip_keeps_length_and_frames_but_release_moves_to_lab_skill_hit_time(gen4):
    """M4-W5：cast 总时长与帧数不变（600 ms / 12 帧），仅 windup 与 recovery 重新分配，使 release 落在 150 ms。"""
    c = gen4[2]["clips"]["cast"]
    assert c["total_ms"] == 600 and c["frame_count"] == 12
    assert [(p["name"], p["ms"]) for p in c["phases"]] == [("windup", 150), ("release", 100), ("recovery", 350)]
    rel = [e for e in c["events"] if e["name"] == "release"]
    assert len(rel) == 1 and abs(rel[0]["time_pct"] - 150 / 600) < 1e-3


def test_cast_release_variants_keep_length_and_frames_with_release_at_the_declared_time(gen4):
    """M4-W6：cast.quick（释放 100 ms）/ cast.heavy（释放 300 ms）总时长与帧数同 cast（600 ms / 12 帧）；
    release 事件恰在前摇结束处；每个变体都带武器层（随武器族换层）。"""
    clips = gen4[2]["clips"]
    base = clips["cast"]
    for key, want_ms in (("cast.quick", 100), ("cast.heavy", 300)):
        c = clips[key]
        assert c["total_ms"] == base["total_ms"] == 600 and c["frame_count"] == base["frame_count"] == 12, key
        assert c["variant"] == key.split(".")[1] and c["tier"] == "optional"
        assert [(p["name"], p["ms"]) for p in c["phases"]][0] == ("windup", want_ms)
        rel = [e for e in c["events"] if e["name"] == "release"]
        assert len(rel) == 1 and abs(rel[0]["time_pct"] - want_ms / 600) < 1e-3, key
        assert c["layers"] == base["layers"] and c["weapon_layer_family"] == base["weapon_layer_family"], key


def test_check_flags_cast_variant_release_off_the_declared_time(gen4, tmp_path):
    """复现：把 cast.quick 的 release 事件挪离 100 ms，自检必须报错（防止数据与施放点声明脱节）。"""
    a, d = _copy(gen4, tmp_path)
    def bend(s):
        for e in s["clips"]["cast.quick"]["events"]:
            if e["name"] in ("release", "hit_frame"):
                e["time_pct"] = 0.5
    _rewrite_spec(a, bend)
    assert any("cast.quick" in e and "release" in e for e in _errors(a, d))


def test_wounded_variant_covers_every_idle_and_move_key_including_sprint_and_combat(gen4):
    """复现：此前 wounded 只有待机与走/跑；不变量：徒手基础族每个待机/移动键（含冲刺、战斗走跑、启停过渡）都有 .wounded。"""
    clips = gen4[2]["clips"]
    for k in WOUNDED_KEYS:
        assert k in clips, k
        assert clips[k]["variant"] == "wounded"
    base_keys = [k for k, e in clips.items() if e["family"] is None and parse_key(k)["state"] in ("idle", "move")
                 and parse_key(k)["variant"] in (None, "start", "stop", "pivot") and not k.endswith(".wounded")]
    assert len(base_keys) == len(WOUNDED_KEYS)
    for k in base_keys:
        assert f"{k}.wounded" in clips, k
    # 带伤冲刺：步幅倍率 < 1，每步位移 = 标称 x 倍率；战斗冲刺别名到和平冲刺
    sp = clips["move.sprint.wounded"]
    assert sp["stride_factor"] < 1.0
    assert clips["move.sprint.combat.wounded"]["alias_of"] == "move.sprint.wounded"
    # 过渡：带伤的起步/急停/急转的端点接带伤的待机/奔跑
    from std_dummy_poses.poses import pose_at
    from std_dummy_poses.verify import pose_diff
    ck = C.clip_by_key
    for k, a, b in (("move.start.wounded", "idle.wounded", "move.run.wounded"),
                    ("move.stop.wounded", "move.run.wounded", "idle.wounded")):
        c = ck(k)
        assert pose_diff(pose_at(c, 0.0), pose_at(ck(a), 0.0)) < 1e-9
        assert pose_diff(pose_at(c, float(c.total_ms)), pose_at(ck(b), 0.0)) < 1e-9


def test_air_keys_resolve_to_declared_fallback_and_follow_ground_attack_phases(gen4):
    clips = gen4[2]["clips"]
    for k in AIR_KEYS + DETAIL_KEYS:
        assert k in clips, k
    assert clips["attack.air"]["alias_of"] == "attack.air.unarmed"
    assert clips["jump.rise"]["loop"] and clips["jump.fall"]["loop"] and not clips["jump.land"]["loop"]
    for fam in ("unarmed", "1h", "2h", "polearm", "bow", "staff", "dual", "shield"):
        air, ground = clips[f"attack.air.{fam}"], clips[f"attack.{fam}"]
        assert air["phases"] == ground["phases"], fam
        names = {x["name"] for x in air["events"]}
        assert {"active_start", "hit", "active_end", "hit_frame"} <= names
        assert not any(n.startswith(("combo_", "cancel_open")) for n in names), fam   # 空中没有连招段与地面闪避取消
    # 空中攻击不带抬升：高度归逻辑竖直轴
    from std_dummy_poses.poses import pose_at
    for fam in ("1h", "bow"):
        c = C.clip_by_key(f"attack.air.{fam}")
        assert max(abs(pose_at(c, c.total_ms * i / 10)["lift"]) for i in range(11)) == 0.0


def test_air_clip_endpoints_chain_into_jump_fall_loop():
    """不变量：jump.land 起点 = jump.fall 起点、hit.air 首尾 = jump.fall 起点（接下落循环）。"""
    from std_dummy_poses.poses import pose_at
    from std_dummy_poses.verify import pose_diff
    fall0 = pose_at(C.clip_by_key("jump.fall"), 0.0)
    assert pose_diff(pose_at(C.clip_by_key("jump.land"), 0.0), fall0) < 1e-9
    ha = C.clip_by_key("hit.air")
    assert pose_diff(pose_at(ha, 0.0), fall0) < 1e-9 and pose_diff(pose_at(ha, float(ha.total_ms)), fall0) < 1e-9


def test_block_hit_shake_returns_to_block_hold_and_stun_sway_is_wide():
    from std_dummy_poses.poses import pose_at
    from std_dummy_poses.verify import pose_diff
    for hk, bk in (("hit.block", "block"), ("hit.block.shield", "block.shield")):
        h, b = C.clip_by_key(hk), C.clip_by_key(bk)
        assert pose_diff(pose_at(h, 0.0), pose_at(b, 0.0)) < 1e-9
        assert pose_diff(pose_at(h, float(h.total_ms)), pose_at(b, 0.0)) < 1e-9
        mid = pose_at(h, 80.0)           # 冲击相终点：与持握有明显差异（抖动真的发生）
        assert pose_diff(mid, pose_at(b, 0.0)) > 5.0
    sw = C.clip_by_key("stunned.sway")
    rolls = [pose_at(sw, sw.total_ms * i / 32)["t_roll"] for i in range(32)]
    assert max(rolls) - min(rolls) >= 15.0
    plain = C.clip_by_key("stunned")
    assert max(rolls) - min(rolls) > 1.3 * (max(pose_at(plain, plain.total_ms * i / 32)["t_roll"] for i in range(32))
                                          - min(pose_at(plain, plain.total_ms * i / 32)["t_roll"] for i in range(32)))


def test_launch_tumble_flips_a_full_turn_without_penetrating_ground_and_lands_into_getup():
    from std_dummy_poses.poses import pose_at
    from std_dummy_poses.skeleton import build_parts, solve_ground
    from std_dummy_poses.verify import pose_diff
    tb, ld, gu = C.clip_by_key("hit.launch.tumble"), C.clip_by_key("hit.launch.land"), C.clip_by_key("hit.getup")
    bps = [pose_at(tb, tb.total_ms * i / 40)["bp"] for i in range(40)]
    assert bps[0] == 0.0 and all(b2 < b1 for b1, b2 in zip(bps, bps[1:])) and bps[-1] < -350.0   # 单调翻一整圈
    for c in (tb, ld):
        for i in range(41):
            p = pose_at(c, c.total_ms * i / 40)
            parts, _j = build_parts(p, None)
            low = min(q[1] for pp in parts if pp.layer == "body" for q in pp.corners) + solve_ground(p, parts)
            assert low >= -1e-6, (c.key, i, low)          # 不穿地
    assert pose_diff(pose_at(ld, 0.0), pose_at(tb, float(tb.total_ms))) < 1e-9
    assert pose_diff(pose_at(ld, float(ld.total_ms)), pose_at(gu, 0.0)) < 1e-9


def test_mass_tiers_cover_all_keys_and_inherit_main_set(gen4):
    """复现：此前体量组只覆盖待机与移动站姿。不变量：每档覆盖主集全部键（含别名键）、数据行 extends 主集、中体量为空覆盖行、
    非受击/抬升类键与主集只差静态站姿偏移。"""
    assets, data, spec = gen4
    main = spec["clips"]
    assert list(spec["mass_tiers"]["tiers"]) == list(C.MASS_TIERS) == ["light", "heavy"]
    assert spec["mass_tiers"]["main"] == "medium"
    doc = json.loads((data / "display" / "display.anim_set.json").read_text(encoding="utf-8"))
    rows = {r["id"]: r for r in doc["rows"]}
    mid = rows["display.anim_set.std_dummy_biped_medium"]
    assert mid["extends"] == "display.anim_set.std_dummy_biped" and mid["clips"] == {}
    from std_dummy_poses.poses import pose_at
    for g in spec["mass_groups"]:
        assert set(g["clips"]) == set(main)
        row = rows[g["id"]]
        assert row["extends"] == "display.anim_set.std_dummy_biped" and set(row["clips"]) == set(main)
        for k, e in g["clips"].items():
            assert e["resource_ref"].startswith(f"sprite_anim.std_dummy_{g['mass']}_")
            assert e["events"] == main[k]["events"] and e["total_ms"] == main[k]["total_ms"]
        prof = C.MASS_TIERS[g["mass"]]
        for c in C.mass_clip_defs(g["mass"]):
            if c.alias_of or c.pose_id in ("hit", "hit.light", "hit.heavy", "hit.knockback", "hit.knockdown", "death",
                                           "hit.launch", "jump", "hit_air", "hit_block"):
                continue
            pg, pm = pose_at(c, 0.0), pose_at(C.clip_by_key(c.key), 0.0)
            for kk in pg:
                want = {"t_pitch": prof["t_pitch"], "h_pitch": prof["h_pitch"], "m_sa": prof["sa"], "o_sa": prof["sa"],
                        "m_ha": prof["ha"], "o_ha": prof["ha"]}.get(kk, 0.0)
                assert pg[kk] - pm[kk] == pytest.approx(want, abs=1e-9), (g["mass"], c.key, kk)
    # 受击类键的体量差异体现在反应幅度（相对各自站姿的后仰量）：轻 > 中 > 重
    hv = C.clip_by_key("hit.heavy")
    t = hv.phases[0][1]

    def react(c):
        return abs(pose_at(c, t)["t_pitch"] - pose_at(c, 0.0)["t_pitch"])
    assert react(dataclasses_replace(hv, "light")) > react(hv) > react(dataclasses_replace(hv, "heavy"))


def dataclasses_replace(clip, mass):
    import dataclasses
    return dataclasses.replace(clip, mass=mass)


def test_mass_tiers_are_data_declared_adding_a_tier_adds_a_group_and_row(monkeypatch):
    """可数据声明的多档：在 MASS_TIERS 里加一档，规格与数据行就多出对应的组与行，不改任何代码。"""
    monkeypatch.setitem(C.MASS_TIERS, "xheavy", {"t_pitch": 9.0, "h_pitch": -6.0, "sa": 11.0, "ha": 7.0, "react": 0.7, "air": 0.8})
    spec = build_spec(8, 20, True)
    assert [g["mass"] for g in spec["mass_groups"]] == ["light", "heavy", "xheavy"]
    from std_dummy_poses.build import build_mass_data_rows
    ids = [r["id"] for r in build_mass_data_rows(spec)]
    assert ids == ["display.anim_set.std_dummy_biped_medium", "display.anim_set.std_dummy_biped_light",
                   "display.anim_set.std_dummy_biped_heavy", "display.anim_set.std_dummy_biped_xheavy"]
    assert all(len(g["clips"]) == len(spec["clips"]) for g in spec["mass_groups"])
    assert next(g for g in spec["mass_groups"] if g["mass"] == "xheavy")["clips"]["idle"]["resource_ref"] == \
        "sprite_anim.std_dummy_xheavy_idle"


def test_check_flags_mass_group_missing_key_and_missing_tier(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    _rewrite_spec(a, lambda s: s["mass_groups"][1]["clips"].pop("attack.1h"))
    assert any("覆盖全部键" in e and "heavy" in e for e in _errors(a, d))
    b, d2 = _copy(gen4, tmp_path / "b")
    _rewrite_spec(b, lambda s: (s["mass_tiers"]["tiers"].pop("heavy"), s["mass_groups"].pop(1)))
    assert any("体量档缺 heavy" in e for e in _errors(b, d2))


def test_check_flags_missing_m4d_key_and_uncovered_wounded_key(gen4, tmp_path):
    a, d = _copy(gen4, tmp_path)
    _rewrite_spec(a, lambda s: s["clips"].pop("jump.rise"))
    assert any("M4-D 键缺失" in e and "jump.rise" in e for e in _errors(a, d))
    b, d2 = _copy(gen4, tmp_path / "b")
    _rewrite_spec(b, lambda s: s["clips"].pop("move.sprint.wounded"))
    assert any("带伤变体未覆盖 move.sprint" in e for e in _errors(b, d2))


def test_five_new_weapon_shapes_are_low_poly_not_boxes_and_stay_within_canvas():
    """复现：M3-D 的新族武器是方块占位（1-4 个盒子）。不变量：零件数不低于下限、总长可辨认、零件名与登记一致；
    画布不裁切由逐帧自检保证（generated tree 自检 0 错误）。"""
    from std_dummy_poses import weapons
    from std_dummy_poses.poses import pose_at
    from std_dummy_poses.skeleton import build_parts
    floor = {"polearm": 5, "bow": 9, "staff": 6, "dual": 5, "shield": 5}
    for fam, n in floor.items():
        parts, _j = build_parts(pose_at(C.clip_by_key(f"idle.combat.{fam}"), 0.0), fam)
        wp = [p for p in parts if p.layer == "weapon"]
        assert len(wp) >= n and [p.name for p in wp] == list(weapons.part_names(fam)), fam
        assert len({p.name for p in wp}) == len(wp)
    # 武器形体仍是占位：装备美术覆盖（ADR-0100）——形体由 hand_main 层承载，身体层不含武器
    parts, _j = build_parts(pose_at(C.clip_by_key("idle.combat.polearm"), 0.0), "polearm")
    assert {p.layer for p in parts} == {"body", "weapon"}


def test_weapon_shape_change_leaves_body_layers_and_non_weapon_families_untouched():
    """不变量：武器形体改动只影响五个新族的武器层（含整身合成），身体层与 1h/2h 的像素不变——渲染同一姿势，徒手与 1h 完全一致。"""
    from std_dummy_poses.poses import pose_at
    from std_dummy_poses.skeleton import render
    c = C.clip_by_key("idle.combat.bow")
    p = pose_at(c, 0.0)
    body = render(p, 0.0, "bow", "body")
    assert body.tobytes() == render(p, 0.0, None, "body").tobytes()


LYING_KEYS = ("hit.launch", "hit.knockdown", "hit.getup", "death")
BP_KEYS = LYING_KEYS + ("hit.launch.tumble", "hit.launch.land", "hit.heavy", "hit.knockback")


def test_m4w5_bp_only_on_lying_and_tumble_keys_and_lying_poses_lie_flat_without_penetrating_ground():
    """复现：M4-D 的躺姿用肩/髋大角度硬掰出来（肩 > 190、髋 < -50，超出人体范围）。不变量：躺姿键改用骨盆整身俯仰 bp 放平，
    bp 只出现在躺姿 + 翻滚 + 重受击/击退（M4-W6）键；躺姿键的肩/髋/躯干源角度落在收紧后的人体范围内；任何一帧不穿地。"""
    from std_dummy_poses.poses import pose_at
    from std_dummy_poses.skeleton import build_parts, solve_ground
    for mass in (None, "light", "heavy"):
        defs = C.mass_clip_defs(mass) if mass else C.build_clip_defs()
        for c in defs:
            if c.alias_of:
                continue
            bps = [abs(pose_at(c, c.total_ms * i / 40)["bp"]) for i in range(41)]
            if c.key not in BP_KEYS:
                assert max(bps) == 0.0, (mass, c.key)
            elif c.key in LYING_KEYS:
                assert max(bps) >= 85.0, (mass, c.key)
            for i in range(41):
                p = pose_at(c, c.total_ms * i / 40)
                if c.key in LYING_KEYS:
                    for name, lo, hi in (("m_sf", -65, 185), ("o_sf", -65, 185), ("m_hf", -46, 100), ("o_hf", -46, 100),
                                         ("t_pitch", -50, 60)):
                        assert lo - 1e-6 <= p[name] <= hi + 1e-6, (mass, c.key, i, name, p[name])
                parts, _j = build_parts(p, None)
                low = min(q[1] for pp in parts if pp.layer == "body" for q in pp.corners) + solve_ground(p, parts)
                assert low >= -1e-6, (mass, c.key, i, low)


def test_m4w5_lying_poses_are_supine_and_prone_by_bp_sign_and_getup_starts_from_the_knockdown_end():
    """击倒落在仰卧（bp < 0，后倒），死亡落在俯卧（bp > 0，前扑）；起身从击倒终点出发、回到站姿。"""
    from std_dummy_poses.poses import pose_at
    kd, gu, dt = (C.clip_by_key(k) for k in ("hit.knockdown", "hit.getup", "death"))
    assert pose_at(kd, float(kd.total_ms))["bp"] <= -85.0 and pose_at(dt, float(dt.total_ms))["bp"] >= 85.0
    assert abs(pose_at(gu, float(gu.total_ms))["bp"]) < 1e-9
    assert pose_at(gu, 0.0)["bp"] == pose_at(kd, float(kd.total_ms))["bp"]


def test_m4w6_every_lab_skill_hit_marker_equals_the_release_of_the_cast_clip_it_plays(gen4):
    """复现（M4-H 实测、M4-W5 判定）：起手 100 ms 的技能动画晚 50 ms、起手 300 ms 的精英重击动画早 150 ms（单一 cast 剪辑只有一个释放点）。
    不变量：每个有命中标记且用施放动画（cast_time > 0）的实验室技能（动作式技能库 + space_ext 的 jab），其命中标记等于它实际播放的施放剪辑的 release 时刻：
    没声明覆盖的用 cast（150 ms），声明了 weapon_style.cast_anim_override 的用覆盖的变体；精灵与模型两份武器风格行的覆盖完全一致，且回退链
    cast.<变体> -> cast 的目标都存在；每个变体都被至少一个技能用到（没有死条目）。"""
    clips = gen4[2]["clips"]

    def release_ms(key):
        t = 0
        for ph in clips[key]["phases"]:
            if ph["name"] == "release":
                return t
            t += ph["ms"]
        raise AssertionError(key)

    rows = json.loads((REPO_ROOT / "data" / "_lab_action" / "display" / "display.weapon_style.json").read_text(encoding="utf-8"))["rows"]
    by_id = {r["id"]: r for r in rows}
    sprite, model = by_id["display.weapon_style.lab_sprite"]["cast_anim_override"], by_id["display.weapon_style.lab_model"]["cast_anim_override"]
    assert set(sprite) == set(model)
    key_of = lambda ref, prefix: ref[len(prefix):].replace("std_dummy_", "").replace("_", ".", 1)  # noqa: E731
    for skill in sprite:
        assert key_of(sprite[skill], "sprite_anim.") == key_of(model[skill], "anim."), skill
        assert key_of(sprite[skill], "sprite_anim.") in clips, skill
        assert key_of(sprite[skill], "sprite_anim.") in ("cast.quick", "cast.heavy"), skill

    used, checked = set(), []
    for rel in ("data/_lab_action/skill/skill.def.json", "lab/fixtures/data/space_ext/skill/skill.def.json"):
        for r in json.loads((REPO_ROOT / rel).read_text(encoding="utf-8"))["rows"]:
            hits = [m["at_ms"] for m in r.get("timeline", {}).get("markers", []) if m["name"] == "hit"]
            if not hits or r.get("cast_time", 0) <= 0:
                continue
            key = key_of(sprite[r["id"]], "sprite_anim.") if r["id"] in sprite else "cast"
            used.add(key)
            checked.append(r["id"])
            for h in hits:
                assert h == release_ms(key), (r["id"], h, key, release_ms(key))
    assert {"skill.lab_a_slash", "skill.lab_a_combo3", "skill.lab_a_elite_swing", "skill.lab_spx_jab"} <= set(checked)
    assert used == {"cast", "cast.quick", "cast.heavy"}


def test_m4w5_mass_groups_carry_whole_body_direction_variants_and_flag_agrees(gen4):
    """复现：此前体量组只有主方向（front/back/side_r），分段/8 向切到别的朝向时重/轻体量回落到中体量图。不变量：规格里的标记与
    参数一致，体量组的整身剪辑目录集合与主集一致（每键每方向一份）。"""
    assets, data, spec = gen4
    assert spec["mass_tiers"]["composite_direction_variants"] is True
    assert spec["params"]["composite_direction_variants"] is True
    slots = ("front", "back", "side_r")
    for g in spec["mass_groups"]:
        for k, e in g["clips"].items():
            if "alias_of" in e:
                continue
            main_ref = spec["clips"][k]["resource_ref"]
            for slot in slots:
                md = clip_dir(assets, e["resource_ref"], slot)
                assert md.is_dir(), (g["mass"], k, slot)
                assert len(list(md.glob("*.png"))) == len(list(clip_dir(assets, main_ref, slot).glob("*.png"))), (g["mass"], k, slot)
