"""``toolchain/run_prerender_skin.py``（标准骨骼蒙皮预渲染，手感设计/04 第 6.2 节、ADR-0140）驱动器的测试。

引擎侧真实渲染在 PlayMode 用例 SkinPrerenderPlayModeTests 与命令行端到端实跑里覆盖；这里用桩后端
（``_prerender_stub.py``，几何人偶渲染同形裸帧）覆盖驱动器全链路：配置校验、渲染计划与 sprite 版假人时间规则逐位一致、
组装、自检（含自检能抓出缺陷）、数据行校验、拒绝路径与退出码。临时输出一律写 tmp_path。

运行：``python -m pytest toolchain/tests/test_prerender_skin.py -q``
"""

from __future__ import annotations

import json
import shutil
import sys
from pathlib import Path

import pytest
from PIL import Image

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))
if str(Path(__file__).resolve().parent) not in sys.path:
    sys.path.insert(0, str(Path(__file__).resolve().parent))

from _prerender_stub import make_stub_backend  # noqa: E402
from prerender_skin import cli  # noqa: E402
from prerender_skin.config import ConfigError, SkinRefused, load_config, parse_config  # noqa: E402
from prerender_skin.plan import build_plan, build_spec as plan_spec, anim_set_rows  # noqa: E402
from prerender_skin.unity_runner import EngineError  # noqa: E402
from prerender_skin.verify import verify  # noqa: E402
from std_dummy_poses import build as SB  # noqa: E402
from std_dummy_poses import config as SC  # noqa: E402

REPO_ROOT = TOOLCHAIN_DIR.parent
SAMPLE_CONFIG = TOOLCHAIN_DIR / "prerender_skin" / "sample" / "dummy_skin_subset.json"

#: 任务清单要求的子集：待机、移动、cast 与释放点变体、空中攻击（别名键）、击飞。
SUBSET_KEYS = ["idle", "move.walk", "cast", "cast.quick", "attack.air", "hit.launch"]


def _cfg(**over) -> dict:
    d = {"anim_set_id": "display.anim_set.hero", "skin": "Assets/Skins/hero.prefab", "keys": SUBSET_KEYS,
         "slots": ["front", "side_r"], "layers": {"hand_main": ["hips/spine/upper_arm_r/forearm_r/hand_r"]}}
    d.update(over)
    return d


def _run(tmp_path: Path, cfg_dict: dict, backend=None, clean: bool = False, chunk_frames: int = cli.DEFAULT_CHUNK_FRAMES):
    cfg = parse_config(cfg_dict)
    assets, data = tmp_path / "assets" / "ds", tmp_path / "data" / "ds"
    work = tmp_path / "work"
    backend = backend or make_stub_backend()
    report, plan, result = cli.run_pipeline(cfg, assets, data, backend, work, clean=clean, chunk_frames=chunk_frames,
                                            log=lambda *_: None)
    return cfg, plan, report, assets, data


# --------------------------------------------------------------------------------------------- 配置


def test_sample_config_loads_and_plans():
    cfg = load_config(SAMPLE_CONFIG)
    plan = build_plan(cfg)
    assert cfg.name == "skin_sample"
    assert {cp.key for cp in plan.clips if cp.mass is None} == {"idle", "move.walk", "cast", "cast.quick", "attack.air.unarmed",
                                                                  "hit.launch"}
    assert plan.variants() == ["all", "body", "hand_main"]


def test_unknown_direction_tier_refused_before_engine():
    with pytest.raises(ConfigError, match="direction_count"):
        parse_config(_cfg(direction_count=6))
    # 镜像档位不单独渲染；未知档位名；缺默认朝向 front
    with pytest.raises(ConfigError, match="镜像"):
        parse_config(_cfg(slots=["front", "side_l"]))
    with pytest.raises(ConfigError, match="未知的方向档"):
        parse_config(_cfg(slots=["front", "nowhere"]))
    with pytest.raises(ConfigError, match="默认方向档"):
        parse_config(_cfg(slots=["side_r"]))


@pytest.mark.parametrize("bad, pattern", [
    ({"anim_set_id": "display.anim_set.std_hero"}, "std_"),
    ({"anim_set_id": "hero"}, "anim_set_id"),
    ({"keys": ["idle", "no.such.key"]}, "未知的姿势键"),
    ({"mass_groups": ["gigantic"]}, "未知的体量档"),
    ({"layers": {"body": ["a"]}}, "身体层"),
    ({"layers": {"x": ["a"], "y": ["a"]}}, "同时属于"),
    ({"lighting": "baked"}, "lighting"),
    ({"unknown_field": 1}, "未知的配置字段"),
    ({"skin": ""}, "skin"),
    ({"canvas": [144]}, "canvas"),
    ({"pivot_px": [999, 5]}, "pivot_px"),
    ({"fps": 0}, "fps"),
])
def test_bad_config_fields_refused(bad, pattern):
    with pytest.raises(ConfigError, match=pattern):
        parse_config(_cfg(**bad))


# --------------------------------------------------------------------------------------------- 计划 = sprite 版时间规则


def test_plan_matches_procedural_spec_frame_counts_and_events():
    """帧数、帧时长、事件时刻与 sprite 版假人规格（独立构造）逐键一致；别名键指向目标资源。"""
    cfg = parse_config(_cfg(mass_groups=["light", "heavy"]))
    plan = build_plan(cfg)
    spec = SB.build_spec(cfg.direction_count, cfg.fps, True)
    for mass, rows in plan.rows.items():
        want = spec["clips"] if mass is None else next(g for g in spec["mass_groups"] if g["mass"] == mass)["clips"]
        for key, (cp, _c) in rows.items():
            w = want[key]
            assert cp.frame_count == w["frame_count"], (mass, key)
            assert list(cp.frames_per_phase) == w["frames_per_phase"], (mass, key)
            assert cp.total_ms == w["total_ms"], (mass, key)
            assert [dict(e) for e in cp.events] == w["events"], (mass, key)
            assert cp.loop == w["loop"]
    # 数据行：键 -> 资源引用；别名 attack.air 与目标 attack.air.unarmed 共用同一份资源
    row = anim_set_rows(plan)[0]["clips"]
    assert row["attack.air"]["resource_ref"] == "sprite_anim.hero_attack_air_unarmed"
    assert set(row) == set(SUBSET_KEYS)


def test_frame_boundary_alignment_of_release_and_hit_matches_sprite_rule():
    """release / hit_frame 事件落在帧边界上，且换算的帧下标与 sprite 版同一规则（FrameIndexAt = floor(t*fps+1e-9)）一致。"""
    cfg = parse_config(_cfg())
    plan = build_plan(cfg)
    fps = cfg.fps
    by_key = {cp.key: cp for cp in plan.clips}
    for key, expect_frame in (("cast", 3), ("cast.quick", 2)):      # 前摇 150/100 ms，20 fps -> 3/2 帧
        cp = by_key[key]
        rel = next(e for e in cp.events if e["name"] == "release")
        t = rel["time_pct"] * cp.total_ms / 1000.0
        assert int(t * fps + 1e-9) == expect_frame
    cp = by_key["attack.air.unarmed"]
    hit = next(e for e in cp.events if e["name"] == "hit")
    hit_frame = next(e for e in cp.events if e["name"] == "hit_frame")
    assert hit["time_pct"] == hit_frame["time_pct"]


@pytest.mark.parametrize("count, slots, yaws", [
    (4, ["front", "side_r", "back"], [0.0, 90.0, 180.0]),
    (8, ["front", "front_side_r", "side_r", "back_side_r", "back"], [0.0, 45.0, 90.0, 135.0, 180.0]),
    (16, ["front", "front_side_r_a", "front_side_r", "front_side_r_b", "side_r", "back_side_r_b", "back_side_r", "back_side_r_a", "back"],
     [0.0, 22.5, 45.0, 67.5, 90.0, 112.5, 135.0, 157.5, 180.0]),
])
def test_direction_tiers_expand_to_canonical_right_side_slots_and_render_each(tmp_path, count, slots, yaws):
    """方向档数 4/8/16 各自展开为 canonical 右侧档位（左侧靠运行期翻转），每个档位一份整身/身体层/装备层产物。"""
    d = _cfg(direction_count=count, keys=["idle"])
    d.pop("slots")
    cfg = parse_config(d)
    assert cfg.effective_slots() == slots
    job = cli.build_job(build_plan(cfg), "o", "r.json")
    assert [s["yaw_deg"] for s in job["slots"]] == yaws
    _cfg_, plan, report, assets, _data = _run(tmp_path, d)
    assert report.ok, report.errors
    for slot in slots:
        for suffix in ("", "__body", "__hand_main"):
            assert (assets / "sprite_anim" / f"hero_idle__{slot}{suffix}" / "frames.json").is_file()
    assert not any(p.name.endswith("_l") or "_l__" in p.name for p in (assets / "sprite_anim").iterdir())   # 不渲染镜像档位


def test_plan_job_is_self_contained_and_stable():
    cfg = parse_config(_cfg(mass_groups=["light"]))
    job1 = cli.build_job(build_plan(cfg), "o", "r.json")
    job2 = cli.build_job(build_plan(cfg), "o", "r.json")
    assert job1 == job2
    assert [s["name"] for s in job1["slots"]] == ["front", "side_r"]
    assert [s["yaw_deg"] for s in job1["slots"]] == [0.0, 90.0]
    assert len(job1["required_bones"]) == 17 and "socket.main_hand" in job1["required_bones"]
    assert all(c["stem"].startswith("std_dummy_") for c in job1["clips"])


# --------------------------------------------------------------------------------------------- 全链路（桩后端）


def test_pipeline_end_to_end_with_stub(tmp_path):
    cfg, plan, report, assets, data = _run(tmp_path, _cfg(mass_groups=["light"]))
    assert report.ok, report.errors
    # 产物：整身默认朝向 + 方向变体 + 逐层
    for cp in plan.clips:
        assert (assets / "sprite_anim" / cp.stem / "atlas.png").is_file()
        for slot in ("front", "side_r"):
            for suffix in ("", "__body", "__hand_main"):
                d = assets / "sprite_anim" / f"{cp.stem}__{slot}{suffix}"
                doc = json.loads((d / "frames.json").read_text(encoding="utf-8"))
                assert len(doc["frames"]) == cp.frame_count
                assert doc["loop"] == cp.loop
    rows = {r["id"]: r for r in json.loads((data / "display" / "display.anim_set.json").read_text(encoding="utf-8"))["rows"]}
    assert set(rows) == {"display.anim_set.hero", "display.anim_set.hero_medium", "display.anim_set.hero_light"}
    assert rows["display.anim_set.hero_light"]["extends"] == "display.anim_set.hero"
    assert "pose_standard" not in rows["display.anim_set.hero"]      # 键子集不触发标准姿势清单
    assert (assets / "hero.prerender.json").is_file()


def test_rerun_with_clean_is_byte_identical(tmp_path):
    def snapshot(root: Path) -> dict:
        return {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file()}

    _run(tmp_path / "a", _cfg())
    _run(tmp_path / "b", _cfg())
    assert snapshot(tmp_path / "a" / "assets") == snapshot(tmp_path / "b" / "assets")
    assert snapshot(tmp_path / "a" / "data") == snapshot(tmp_path / "b" / "data")
    # 同一输出目录 --clean 重跑也一致
    before = snapshot(tmp_path / "a" / "assets")
    _run(tmp_path / "a", _cfg(), clean=True)
    assert snapshot(tmp_path / "a" / "assets") == before


def test_chunked_rendering_is_byte_identical_and_bounds_each_engine_call(tmp_path):
    """按帧数分块渲染（限制中间裸帧的磁盘占用）：每次引擎调用的帧数受上限约束，产物与不分块逐字节相同。"""
    def snapshot(root: Path) -> dict:
        return {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file()}

    calls: list = []
    _run(tmp_path / "chunked", _cfg(mass_groups=["light"]), backend=make_stub_backend(calls=calls), chunk_frames=30)
    whole_calls: list = []
    _run(tmp_path / "whole", _cfg(mass_groups=["light"]), backend=make_stub_backend(calls=whole_calls), chunk_frames=0)
    assert len(whole_calls) == 1 and len(calls) > 3
    for job in calls:
        frames = sum(len(c["sample_ms"]) for c in job["clips"])
        assert frames <= 30 or len(job["clips"]) == 1          # 超大的单份剪辑自成一块
    rendered = [c["stem"] for job in calls for c in job["clips"]]
    assert rendered == [c["stem"] for c in whole_calls[0]["clips"]]            # 分块不丢不重不乱序
    assert snapshot(tmp_path / "chunked" / "assets") == snapshot(tmp_path / "whole" / "assets")
    assert snapshot(tmp_path / "chunked" / "data") == snapshot(tmp_path / "whole" / "data")


def test_data_rows_merge_without_touching_other_rows(tmp_path):
    cfg = parse_config(_cfg())
    data = tmp_path / "data" / "ds"
    path = data / "display" / "display.anim_set.json"
    path.parent.mkdir(parents=True)
    other = {"table": "display.anim_set", "schema_version": 1,
             "rows": [{"id": "display.anim_set.other", "clips": {"idle": {"resource_ref": "sprite_anim.x", "events": []}}}]}
    path.write_text(json.dumps(other), encoding="utf-8")
    _run(tmp_path, _cfg())
    ids = [r["id"] for r in json.loads(path.read_text(encoding="utf-8"))["rows"]]
    assert "display.anim_set.other" in ids and cfg.anim_set_id in ids


def test_full_key_list_sets_pose_standard(tmp_path):
    cfg = parse_config(_cfg(keys="all"))
    assert cfg.pose_standard is True
    rows = anim_set_rows(build_plan(cfg))
    assert rows[0]["pose_standard"] is True
    text = SB._render_anim_set_file(rows)
    assert '"pose_standard": true,' in text


# --------------------------------------------------------------------------------------------- 自检能抓出缺陷


def _fresh(tmp_path):
    cfg, plan, report, assets, data = _run(tmp_path, _cfg())
    assert report.ok, report.errors
    return cfg, plan, assets, data


def test_verify_catches_missing_resource_dir(tmp_path):
    cfg, plan, assets, data = _fresh(tmp_path)
    shutil.rmtree(assets / "sprite_anim" / "hero_cast__side_r__body")
    rep = verify(plan, assets, data)
    assert not rep.ok and any("hero_cast__side_r__body" in e for e in rep.errors)


def test_verify_catches_frame_count_mismatch(tmp_path):
    cfg, plan, assets, data = _fresh(tmp_path)
    fj = assets / "sprite_anim" / "hero_cast__front" / "frames.json"
    doc = json.loads(fj.read_text(encoding="utf-8"))
    doc["frames"].pop()
    fj.write_text(json.dumps(doc), encoding="utf-8")
    rep = verify(plan, assets, data)
    assert any("帧数" in e for e in rep.errors)


def _overwrite_frame(dir_path: Path, index: int, image: Image.Image) -> None:
    doc = json.loads((dir_path / "frames.json").read_text(encoding="utf-8"))
    f = doc["frames"][index]
    atlas = Image.open(dir_path / "atlas.png").convert("RGBA")
    atlas.paste(image, (f["x"], f["y"]))
    atlas.save(dir_path / "atlas.png")


def test_verify_catches_empty_frame(tmp_path):
    cfg, plan, assets, data = _fresh(tmp_path)
    d = assets / "sprite_anim" / "hero_move_walk__front"
    _overwrite_frame(d, 3, Image.new("RGBA", cfg.canvas, (0, 0, 0, 0)))
    rep = verify(plan, assets, data)
    assert any("空帧" in e and "hero_move_walk__front" in e for e in rep.errors)


def test_verify_catches_pivot_drift(tmp_path):
    cfg, plan, assets, data = _fresh(tmp_path)
    d = assets / "sprite_anim" / "hero_idle__front"
    doc = json.loads((d / "frames.json").read_text(encoding="utf-8"))
    f = doc["frames"][0]
    atlas = Image.open(d / "atlas.png").convert("RGBA")
    frame = atlas.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))
    shifted = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    shifted.paste(frame, (0, -12))       # 整体上移 12 像素：脚离开地面点
    atlas.paste(shifted, (f["x"], f["y"]))
    atlas.save(d / "atlas.png")
    rep = verify(plan, assets, data)
    assert any("枢轴漂移" in e for e in rep.errors)


def test_verify_catches_canvas_clipping(tmp_path):
    cfg, plan, assets, data = _fresh(tmp_path)
    d = assets / "sprite_anim" / "hero_cast__front"
    solid = Image.new("RGBA", cfg.canvas, (255, 0, 0, 255))
    _overwrite_frame(d, 0, solid)
    rep = verify(plan, assets, data)
    assert any("画布裁切" in e for e in rep.errors)


def test_verify_catches_misaligned_layer(tmp_path):
    cfg, plan, assets, data = _fresh(tmp_path)
    d = assets / "sprite_anim" / "hero_cast__side_r__hand_main"
    doc = json.loads((d / "frames.json").read_text(encoding="utf-8"))
    f = doc["frames"][2]
    atlas = Image.open(d / "atlas.png").convert("RGBA")
    frame = atlas.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))
    moved = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    moved.paste(frame, (2, 0))           # 装备层整体右移 2 像素
    atlas.paste(moved, (f["x"], f["y"]))
    atlas.save(d / "atlas.png")
    rep = verify(plan, assets, data)
    assert any("层未对齐" in e for e in rep.errors)


def test_verify_catches_data_row_event_drift(tmp_path):
    cfg, plan, assets, data = _fresh(tmp_path)
    path = data / "display" / "display.anim_set.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    row = next(r for r in doc["rows"] if r["id"] == cfg.anim_set_id)
    row["clips"]["cast"]["events"][0]["time_pct"] = 0.5
    path.write_text(json.dumps(doc), encoding="utf-8")
    rep = verify(plan, assets, data)
    assert any("events" in e and "cast" in e for e in rep.errors)


# --------------------------------------------------------------------------------------------- 拒绝与退出码


def test_missing_bones_refused_with_list(tmp_path):
    backend = make_stub_backend(refuse_bones=["hand_l", "socket.off_hand"])
    with pytest.raises(SkinRefused) as exc:
        _run(tmp_path, _cfg(), backend=backend)
    assert exc.value.details["missing_bones"] == ["hand_l", "socket.off_hand"]
    assert not (tmp_path / "assets").exists()          # 拒绝时不写任何产物


def test_cli_exit_codes(tmp_path, monkeypatch, capsys):
    good = tmp_path / "ok.json"
    good.write_text(json.dumps(_cfg()), encoding="utf-8")
    common = ["--assets-root", str(tmp_path / "a"), "--data-root", str(tmp_path / "d"), "--dataset", "ds",
              "--no-external-validate"]
    # 2：配置不合法（未知方向档），且不会构造引擎后端
    bad = tmp_path / "bad.json"
    bad.write_text(json.dumps(_cfg(direction_count=5)), encoding="utf-8")
    monkeypatch.setattr(cli, "unity_backend", lambda *a, **k: pytest.fail("配置不合法时不应启动引擎"))
    assert cli.main([str(bad), *common]) == 2
    assert cli.main([str(tmp_path / "missing.json"), *common]) == 2
    # 3：蒙皮被拒绝
    monkeypatch.setattr(cli, "unity_backend", lambda *a, **k: make_stub_backend(refuse_bones=["head"]))
    assert cli.main([str(good), *common]) == 3
    assert "head" in capsys.readouterr().err
    # 1：引擎失败
    def broken(job, work):
        raise EngineError("引擎崩了")
    monkeypatch.setattr(cli, "unity_backend", lambda *a, **k: broken)
    assert cli.main([str(good), *common, "--work-dir", str(tmp_path / "w")]) == 1
    # 0：通过
    monkeypatch.setattr(cli, "unity_backend", lambda *a, **k: make_stub_backend())
    assert cli.main([str(good), *common]) == 0
    assert (tmp_path / "a" / "ds" / "sprite_anim" / "hero_idle" / "atlas.png").is_file()


def test_plan_only_does_not_need_roots(tmp_path, capsys):
    assert cli.main([str(SAMPLE_CONFIG), "--plan-only"]) == 0
    assert "skin_sample_idle" in capsys.readouterr().out


# --------------------------------------------------------------------------------------------- 外部校验（validate_data --strict + import_assets check）


def test_generated_data_passes_validate_data_strict_and_import_check(tmp_path):
    cfg = parse_config(_cfg(mass_groups=["light"]))
    assets_root, data_root = tmp_path / "assets", tmp_path / "data"
    cfg_, plan, report, assets, data = _run(tmp_path, _cfg(mass_groups=["light"]))
    assert report.ok, report.errors
    logs: list = []
    assert cli.run_external_validation(assets_root, data_root, "ds", log=logs.append), logs
