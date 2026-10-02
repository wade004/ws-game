"""装备资产包 + 界面皮肤包校验（``toolchain/asset_import/equip_pack.py`` / ``skin_pack.py`` / ``equip_cmd.py``）
与框架占位装备集生成器（``toolchain/std_equip_set``）的测试。

两类夹具：
- 仓库真实占位装备集（``data/_equip`` + ``assets/_placeholder``，只读）：零错误零警告冒烟；
- 临时目录里按需拷贝/合成的小夹具：删表行、删层剪辑、删皮肤项等破坏性场景，一律写 tmp_path，不动仓库。

运行：``python -m pytest toolchain/tests/test_equip_pack.py -q``
"""

from __future__ import annotations

import copy
import json
import shutil
import sys
from pathlib import Path

import pytest
from PIL import Image

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from asset_import import check_cmd, cli, equip_cmd, equip_pack as E, skin_pack  # noqa: E402
from std_equip_set import config as SC  # noqa: E402
from std_equip_set import data as SD  # noqa: E402
from std_equip_set.build import clip_stem, clips_for_item, generate_item  # noqa: E402

REPO = TOOLCHAIN_DIR.parent
ASSETS = REPO / "assets" / "_placeholder"
DATA_ROOTS = [REPO / "data" / "_framework", REPO / "data" / "_feel", REPO / "data" / "_equip"]
SWORD = "item.std_sword_1h"


@pytest.fixture(scope="module")
def real_tables():
    return E.load_tables(DATA_ROOTS)


def _only_item(tables, item_id):
    t = copy.deepcopy(tables)
    t["item.template"] = [r for r in t["item.template"] if r["id"] == item_id]
    return t


def _sword_assets(tmp_path: Path) -> Path:
    """只拷贝单手剑的图标/静态层图/逐层剪辑 + 占位皮肤到 tmp（约 1 MB），供破坏性场景用。"""
    dst = tmp_path / "assets"
    for rel in ("icons/item/std_sword_1h.png",):
        (dst / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ASSETS / rel, dst / rel)
    shutil.copytree(ASSETS / "sprites" / "item_std_sword_1h", dst / "sprites" / "item_std_sword_1h", dirs_exist_ok=True)
    (dst / "sprite_anim").mkdir(parents=True, exist_ok=True)
    for d in (ASSETS / "sprite_anim").glob("item_std_sword_1h__*"):
        shutil.copytree(d, dst / "sprite_anim" / d.name, dirs_exist_ok=True)
    shutil.copytree(ASSETS / "ui" / "skin", dst / "ui" / "skin", dirs_exist_ok=True)
    return dst


def _run(tables, assets, **kw):
    return E.EquipValidator(tables, assets, **kw).run()


def _checks(report, severity=None):
    return sorted(i.check for i in report.issues if severity is None or i.severity == severity)


# ---------------------------------------------------------------------------
# 仓库占位装备集
# ---------------------------------------------------------------------------

def test_placeholder_set_has_zero_errors_and_warnings(real_tables):
    report = equip_cmd.run_equip(DATA_ROOTS, ASSETS)
    assert [i.render_text() for i in report.issues] == []
    assert len(report.items) == 6
    assert sorted(report.validated_item_ids()) == sorted(i.item_id for i in SC.ITEMS)
    assert report.blocked_item_ids() == []
    # 逐层剪辑全部带方向命中：键数 × 5 个 canonical 方向档
    for it in report.items:
        assert it.clip_slots == it.clip_hits > 0 and it.clip_missing == 0


def test_placeholder_set_covers_each_equip_slot_and_weapon_family(real_tables):
    kinds = {i.kind for i in SC.ITEMS}
    assert kinds == {"sword_1h", "greatsword", "dagger", "bow", "staff", "chestplate"}
    report = equip_cmd.run_equip(DATA_ROOTS, ASSETS)
    fams = {i.item_id: i.family for i in report.items}
    assert fams[SWORD] == "1h" and fams["item.std_greatsword"] == "2h" and fams["item.std_chestplate"] is None


def test_generator_clip_sets_match_validator_needed_keys(real_tables):
    """生成器（从假人 ClipDef 推）与校验器（从 anim_set 数据行推）对"哪些剪辑需要逐层剪辑"的结论一致。"""
    anim = next(r for r in real_tables["display.anim_set"] if r["id"] == SC.ANIM_SET_ID)["clips"]
    for it in SC.ITEMS:
        want = {c.resource_ref for c in clips_for_item(it)}
        got = set(E.needed_pose_keys(anim, it.family, it.is_weapon))
        assert want == got, it.name


def test_committed_data_tables_match_generator():
    for rel, doc in SD.tables().items():
        assert (REPO / "data" / "_equip" / rel).read_text(encoding="utf-8") == SD.render(doc), rel


def test_generated_dagger_matches_committed_assets(tmp_path):
    dagger = SC.item_by_name("std_dagger")
    generate_item(dagger, tmp_path, 20, log=lambda *_: None)
    for rel in ("icons/item/std_dagger.png", "sprites/item_std_dagger/front/hand_main.png"):
        assert (tmp_path / rel).read_bytes() == (ASSETS / rel).read_bytes(), rel
    one = "sprite_anim/item_std_dagger__std_dummy_attack_1h__side_r__hand_main"
    for f in ("atlas.png", "frames.json"):
        assert (tmp_path / one / f).read_bytes() == (ASSETS / one / f).read_bytes(), f


def test_equip_layer_assets_stay_inside_canvas():
    """逐层剪辑的装备像素不贴画布边（贴边说明被裁切），抽查最长的法杖与最宽的胸甲。"""
    for name, clip in (("std_staff", "std_dummy_attack_2h"), ("std_chestplate", "std_dummy_death")):
        item = SC.item_by_name(name)
        d = ASSETS / "sprite_anim" / f"{item.mesh_stem}__{clip}__side_r__{item.layer}"
        atlas = Image.open(d / "atlas.png").convert("RGBA")
        frames = json.loads((d / "frames.json").read_text(encoding="utf-8"))["frames"]
        for f in frames:
            box = atlas.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])).getchannel("A").getbbox()
            assert box is not None
            assert box[0] > 0 and box[1] > 0 and box[2] < f["w"] and box[3] < f["h"], (name, f["index"])


# ---------------------------------------------------------------------------
# 冒烟场景：删 feel.weapon 行 / 删推荐键 / 删必备键 / 皮肤缺一项
# ---------------------------------------------------------------------------

def test_smoke_delete_feel_weapon_row_gives_one_error(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["feel.weapon"] = [r for r in t["feel.weapon"] if r["id"] != "feel.weapon.sword_1h"]
    report = _run(t, _sword_assets(tmp_path))
    errs = [i for i in report.issues if i.severity == "error"]
    assert [e.check for e in errs] == [E.CHECK_WEAPON_PAIR_MISSING]
    assert "feel.weapon" in errs[0].message and "display.weapon_style" not in errs[0].message.split("缺")[1][:20]
    assert report.blocked_item_ids() == [SWORD] and not report.is_validated(SWORD)


def test_smoke_delete_weapon_style_row_also_one_error(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["display.weapon_style"] = [r for r in t["display.weapon_style"] if r["id"] != "display.weapon_style.sword_1h"]
    report = _run(t, _sword_assets(tmp_path))
    assert _checks(report, "error") == [E.CHECK_WEAPON_PAIR_MISSING]


def test_smoke_delete_both_weapon_rows_is_still_one_error(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["display.weapon_style"] = [r for r in t["display.weapon_style"] if r["id"] != "display.weapon_style.sword_1h"]
    t["feel.weapon"] = [r for r in t["feel.weapon"] if r["id"] != "feel.weapon.sword_1h"]
    report = _run(t, _sword_assets(tmp_path))
    assert _checks(report, "error") == [E.CHECK_WEAPON_PAIR_MISSING]
    assert "都缺" in report.issues[0].message


def test_smoke_delete_recommended_pose_key_gives_one_warning_with_fallback(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    for d in (assets / "sprite_anim").glob("item_std_sword_1h__std_dummy_hit_heavy__*__hand_main"):
        shutil.rmtree(d)
    report = _run(_only_item(real_tables, SWORD), assets)
    assert _checks(report, "error") == []
    warns = [i for i in report.issues if i.severity == "warning"]
    assert [w.check for w in warns] == [E.CHECK_LAYER_CLIP_MISSING_RECOMMENDED]
    assert "sprite_anim.std_dummy_hit_heavy" in warns[0].message
    assert warns[0].fallback == "sprites/item_std_sword_1h/<方向>/hand_main.png（静态层图）"
    assert warns[0].as_dict()["fallback"] == warns[0].fallback
    assert report.is_validated(SWORD)          # 警告不阻断


def test_smoke_delete_required_pose_key_is_error_and_blocks(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    for d in (assets / "sprite_anim").glob("item_std_sword_1h__std_dummy_attack_1h__*__hand_main"):
        shutil.rmtree(d)
    report = _run(_only_item(real_tables, SWORD), assets)
    assert _checks(report, "error") == [E.CHECK_LAYER_CLIP_MISSING]
    assert report.blocked_item_ids() == [SWORD]
    filtered = E.filter_validated(_only_item(real_tables, SWORD), report)
    assert filtered["item.template"] == []
    assert all(r["item_id"] != SWORD for r in filtered["display.equip_visual"])
    assert any(r["item_id"] != SWORD for r in filtered["display.equip_visual"])      # 只剔除被阻断的那件


def test_one_missing_direction_of_required_key_is_error(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    shutil.rmtree(assets / "sprite_anim" / "item_std_sword_1h__std_dummy_idle_1h__back__hand_main")
    report = _run(_only_item(real_tables, SWORD), assets)
    errs = [i for i in report.issues if i.severity == "error"]
    assert [e.check for e in errs] == [E.CHECK_LAYER_CLIP_MISSING] and "back" in errs[0].message
    assert report.items[0].clip_missing == 1


def test_direction_less_clip_is_level_two_fallback_not_issue(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    src = assets / "sprite_anim" / "item_std_sword_1h__std_dummy_move_run_1h__front__hand_main"
    shutil.move(src, assets / "sprite_anim" / "item_std_sword_1h__std_dummy_move_run_1h__hand_main")
    report = _run(_only_item(real_tables, SWORD), assets)
    # front 档的带方向剪辑被改名成无方向剪辑：front 退一级命中，其余 4 个方向档仍带方向命中，都不报问题
    assert report.issues == [] and report.items[0].clip_fallback_nodir == 1
    assert report.items[0].clip_hits == report.items[0].clip_slots - 1


def test_missing_static_layer_is_error(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    (assets / "sprites" / "item_std_sword_1h" / "side_r" / "hand_main.png").unlink()
    report = _run(_only_item(real_tables, SWORD), assets)
    assert _checks(report, "error") == [E.CHECK_LAYER_STATIC_MISSING]


def test_smoke_skin_pack_missing_one_item_falls_back_to_placeholder_and_is_recorded(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    alt = assets / "ui" / "skin" / "alt"
    shutil.copytree(assets / "ui" / "skin" / "default", alt)
    (alt / "slot_frame" / "std_chest.png").unlink()
    tables = copy.deepcopy(real_tables)
    tables["ui_layout_definition"] = [dict(r, skin_ref="skin.alt") for r in tables["ui_layout_definition"]]
    issues, reports = skin_pack.check_skins(tables, assets)
    assert [(i.check, i.severity) for i in issues] == [(skin_pack.CHECK_SKIN_ITEM_MISSING, "warning")]
    assert issues[0].fallback == "skin.default:slot_frame/std_chest.png"
    alt_report = next(r for r in reports if r.skin_ref == "skin.alt")
    assert alt_report.fallbacks == [{"item": "slot_frame/std_chest.png", "fallback": "skin.default:slot_frame/std_chest.png"}]


def test_skin_item_missing_in_placeholder_too_falls_back_to_default_image(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    alt = assets / "ui" / "skin" / "alt"
    shutil.copytree(assets / "ui" / "skin" / "default", alt)
    (alt / "slot_frame" / "std_chest.png").unlink()
    (assets / "ui" / "skin" / "default" / "slot_frame" / "std_chest.png").unlink()
    issues, _ = skin_pack.check_skins(real_tables, assets, skin_refs=["skin.alt"])
    by = {(i.check, i.record_key): i for i in issues}
    assert by[(skin_pack.CHECK_SKIN_ITEM_MISSING, "skin.alt")].fallback == "skin.default:slot_frame/_default.png"
    assert by[(skin_pack.CHECK_SKIN_PLACEHOLDER_MISSING, "skin.default")].severity == "error"


def test_whole_skin_pack_missing_warns_per_item_and_once_for_pack(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    issues, reports = skin_pack.check_skins(real_tables, assets, skin_refs=["skin.nope"])
    kinds = [i.check for i in issues]
    assert kinds.count(skin_pack.CHECK_SKIN_PACK_MISSING) == 1
    assert kinds.count(skin_pack.CHECK_SKIN_ITEM_MISSING) == reports[-1].checked == 15
    assert all(i.severity == "warning" for i in issues)


def test_invalid_theme_and_missing_panel_layout_are_warnings(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    alt = assets / "ui" / "skin" / "alt"
    shutil.copytree(assets / "ui" / "skin" / "default", alt)
    (alt / "theme.json").write_text("{\"colors\": {}}", encoding="utf-8")
    tables = copy.deepcopy(real_tables)
    tables["ui_layout_definition"] = [r for r in tables["ui_layout_definition"] if r["panel"] != "inventory"]
    issues, _ = skin_pack.check_skins(tables, assets, skin_refs=["skin.alt"])
    assert sorted(i.check for i in issues) == [skin_pack.CHECK_SKIN_PANEL_LAYOUT_MISSING, skin_pack.CHECK_SKIN_THEME_INVALID]
    assert all(i.severity == "warning" for i in issues)


def test_invalid_skin_ref_format_is_error(real_tables, tmp_path):
    issues, _ = skin_pack.check_skins(real_tables, _sword_assets(tmp_path), skin_refs=["default"])
    assert [(i.check, i.severity) for i in issues] == [(skin_pack.CHECK_SKIN_REF_INVALID, "error")]


# ---------------------------------------------------------------------------
# 其它规则
# ---------------------------------------------------------------------------

def test_icon_rules(real_tables, tmp_path):
    assets = _sword_assets(tmp_path)
    icon = assets / "icons" / "item" / "std_sword_1h.png"
    t = _only_item(real_tables, SWORD)
    icon.unlink()
    assert _checks(_run(t, assets), "error") == [E.CHECK_ICON_FILE_MISSING]
    Image.new("RGBA", (64, 48), (255, 0, 0, 255)).save(icon)
    assert _checks(_run(t, assets), "error") == [E.CHECK_ICON_SIZE_INVALID]
    Image.new("RGBA", (64, 64), (255, 0, 0, 255)).save(icon)          # 满铺 = 贴边（疑似含品质框）
    r = _run(t, assets)
    assert _checks(r, "error") == [E.CHECK_ICON_SIZE_INVALID] and "贴边" in r.issues[0].message
    Image.new("RGBA", (64, 64), (0, 0, 0, 0)).save(icon)
    assert "全透明" in _run(t, assets).issues[0].message
    t["display.map"] = [dict(r, icon_id=None) for r in t["display.map"]]
    assert _checks(_run(t, assets), "error") == [E.CHECK_ICON_UNRESOLVED]


def test_missing_equip_visual_row_is_error(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["display.equip_visual"] = []
    assert _checks(_run(t, _sword_assets(tmp_path)), "error") == [E.CHECK_VISUAL_MISSING]


def test_weapon_style_and_feel_weapon_must_share_id(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["display.map"] = [dict(r, weapon_style_ref="display.weapon_style.dagger") if r["id"].endswith("sword_1h") else r
                        for r in t["display.map"]]
    assert _checks(_run(t, _sword_assets(tmp_path)), "error") == [E.CHECK_WEAPON_ID_MISMATCH]


def test_family_without_pose_keys_warns_and_skips_per_key_requirement(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["feel.weapon"] = [dict(r, family="whip") if r["id"] == "feel.weapon.sword_1h" else r for r in t["feel.weapon"]]
    assets = _sword_assets(tmp_path)
    shutil.rmtree(assets / "sprite_anim" / "item_std_sword_1h__std_dummy_idle_1h__front__hand_main")  # 不逐键要求
    report = _run(t, assets)
    assert _checks(report) == [E.CHECK_FAMILY_WITHOUT_POSE_KEYS]
    assert report.issues[0].fallback.startswith("基础键") and report.issues[0].severity == "warning"


def test_sfx_material_without_row_warns_with_generic_fallback(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["sfx.def"] = [r for r in t["sfx.def"] if r["id"] != "sfx.impact.metal_light"]
    report = _run(t, _sword_assets(tmp_path))
    assert _checks(report) == [E.CHECK_SFX_MATERIAL_MISSING]
    assert report.issues[0].fallback == "generic" and "impact" in report.issues[0].message
    t["sfx.def"] = [dict(r, id="sfx.impact.alias", material="metal_light") if r.get("layer") == "impact"
                    and r["id"].endswith("metal_heavy") else r for r in t["sfx.def"]]
    assert _checks(_run(t, _sword_assets(tmp_path))) == []   # material 字段也认


def test_optional_refs_warn_when_dangling(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["item.template"] = [dict(r, equip_sfx_ref="sfx.nope") for r in t["item.template"]]
    t["feel.weapon"] = [dict(r, writes=r["writes"] + [{"field": "trail_ref", "op": "set", "value": "vfx.nope"}])
                        if r["id"] == "feel.weapon.sword_1h" else r for r in t["feel.weapon"]]
    report = _run(t, _sword_assets(tmp_path))
    assert _checks(report) == [E.CHECK_SFX_REF_MISSING, E.CHECK_TRAIL_REF_MISSING]


def test_override_clip_without_layer_clip_warns(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["display.weapon_style"] = [dict(r, cast_anim_override={"skill.x": "sprite_anim.custom_cast"})
                                 if r["id"].endswith("sword_1h") else r for r in t["display.weapon_style"]]
    report = _run(t, _sword_assets(tmp_path))
    assert _checks(report) == [E.CHECK_OVERRIDE_CLIP_LAYER_MISSING]
    assert report.issues[0].fallback == "整身剪辑 sprite_anim.custom_cast"


def test_preview_direction_must_be_declared_direction(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["display.equip_visual"] = [dict(r, preview_direction="dir.side_l") for r in t["display.equip_visual"]]
    assert _checks(_run(t, _sword_assets(tmp_path))) == []                       # 镜像档也是声明的档
    t["display.equip_visual"] = [dict(r, preview_direction="dir.nope") for r in t["display.equip_visual"]]
    assert _checks(_run(t, _sword_assets(tmp_path))) == [E.CHECK_PREVIEW_DIRECTION_INVALID]


def test_direction_count_four_needs_only_four_direction_canonicals(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["display.equip_visual"] = [{k: v for k, v in r.items() if k != "preview_direction"} for r in t["display.equip_visual"]]
    r4 = _run(t, _sword_assets(tmp_path), direction_count=4)     # front/side_r/back 都有，4 方向更宽松
    assert _checks(r4, "error") == []
    # 层剪辑槽 = 键数 × 方向档数：4 方向只要 3 个 canonical 档（front/side_r/back），默认 8 方向要 5 个；键数由姿势集推，不写死
    r8 = _run(t, _sword_assets(tmp_path))
    assert r4.items[0].clip_slots * 5 == r8.items[0].clip_slots * 3


def test_model_slot_and_socket_naming():
    tables = {
        "item.slot_definition": [{"id": "item.slot.m", "is_equipment": True}],
        "display.map": [{"id": "display.map.hero", "kind": "model", "slots": ["slot.head"], "sockets": ["socket.main_hand"]},
                        {"id": "display.map.helm", "icon_id": "icon.item.helm"},
                        {"id": "display.map.sword", "icon_id": "icon.item.sword"}],
        "item.template": [{"id": "item.helm", "slot": "item.slot.m", "display_ref": "display.map.helm"},
                          {"id": "item.sword", "slot": "item.slot.m", "display_ref": "display.map.sword"},
                          {"id": "item.cape", "slot": "item.slot.m", "display_ref": "display.map.helm"}],
        "display.equip_visual": [
            {"id": "ev.helm", "item_id": "item.helm", "mode": "slot_mesh", "slot_id": "slot.head", "mesh_ref": "model.biped"},
            {"id": "ev.sword", "item_id": "item.sword", "mode": "socket_attach", "socket_id": "socket.off_hand",
             "model_ref": "model.biped"},
            {"id": "ev.cape", "item_id": "item.cape", "mode": "slot_mesh", "slot_id": "slot.back", "mesh_ref": "model.biped"},
        ],
    }
    report = E.EquipValidator(tables, Path("."), direction_count=8).run()
    by_item = {it.item_id: sorted(i.check for i in it.errors if i.check.startswith("equip_model")) for it in report.items}
    assert by_item == {"item.helm": [], "item.sword": [E.CHECK_MODEL_SOCKET_UNKNOWN], "item.cape": [E.CHECK_MODEL_SLOT_UNKNOWN]}
    # 命名必须带类别前缀
    tables["display.equip_visual"][0]["slot_id"] = "head"
    assert E.EquipValidator(tables, Path(".")).run().items[0].errors[-1].check == E.CHECK_MODEL_SLOT_UNKNOWN


def test_pose_key_parsing_tiering_and_needed_sets():
    pk = E.parse_pose_key
    assert pk("move.run.combat.2h") == E.PoseKey("move", "run", "combat", "2h", None)
    assert pk("attack.1h.02") == E.PoseKey("attack", None, None, "1h", "02")
    assert pk("hit.heavy") == E.PoseKey("hit", None, None, None, "heavy")
    assert pk("idle.combat") == E.PoseKey("idle", None, "combat", None, None)
    tier = E.pose_key_tier
    assert [tier(k) for k in ("idle", "idle.1h", "move.walk", "move.run.2h", "attack.1h", "hit", "death")] == ["required"] * 7
    assert [tier(k) for k in ("idle.combat", "move.run.combat.1h", "attack.1h.02", "hit.heavy", "jump", "cast", "dodge")] \
        == ["recommended"] * 7
    clips = {k: {"resource_ref": "sprite_anim." + k.replace(".", "_")}
             for k in ("idle", "idle.1h", "idle.2h", "hit", "hit.heavy", "attack.1h", "attack.1h.02", "attack.2h", "death")}
    weapon = E.needed_pose_keys(clips, "1h", True)
    assert sorted(weapon) == sorted("sprite_anim." + k for k in ("idle_1h", "hit", "hit_heavy", "attack_1h", "attack_1h_02", "death"))
    assert weapon["sprite_anim.attack_1h_02"] == "recommended" and weapon["sprite_anim.hit"] == "required"
    assert len(E.needed_pose_keys(clips, None, False)) == len(clips)                    # 身体跟随层全覆盖
    alias = dict(clips, attack={"resource_ref": clips["attack.1h"]["resource_ref"]})
    assert len(E.needed_pose_keys(alias, None, False)) == len(clips)                    # 别名按资源去重


def test_missing_anim_set_warns_instead_of_crashing(real_tables, tmp_path):
    t = _only_item(real_tables, SWORD)
    t["display.anim_set"] = []
    report = _run(t, _sword_assets(tmp_path))
    assert _checks(report) == [E.CHECK_ANIM_SET_MISSING]


# ---------------------------------------------------------------------------
# 命令行与 check 域
# ---------------------------------------------------------------------------

def test_cli_equip_succeeds_and_writes_local_report(tmp_path, capsys):
    rc = cli.main(["equip", "--report-dir", str(tmp_path / "rep"), "--blocked-out", str(tmp_path / "blocked.txt")])
    assert rc == 0
    doc = json.loads((tmp_path / "rep" / "equip_completeness.json").read_text(encoding="utf-8"))
    assert doc["ok"] is True and doc["counts"] == {"error": 0, "warning": 0, "items": 6, "validated": 6}
    assert (tmp_path / "rep" / "equip_completeness.txt").read_text(encoding="utf-8").startswith("装备完整性报告")
    assert (tmp_path / "blocked.txt").read_text(encoding="utf-8") == ""
    assert "错误 0，警告 0" in capsys.readouterr().out


def test_cli_equip_json_stdout_and_exit_code_on_error(tmp_path, capsys):
    bad = tmp_path / "data"
    for src in DATA_ROOTS:
        shutil.copytree(src, bad / src.name)
    path = bad / "_equip" / "display" / "display.equip_visual.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    doc["rows"] = [r for r in doc["rows"] if r["item_id"] != SWORD]
    path.write_text(json.dumps(doc), encoding="utf-8")
    argv = ["equip", "--json", "--no-report", "--blocked-out", str(tmp_path / "b.txt")]
    for n in ("_framework", "_feel", "_equip"):
        argv += ["--data-root", str(bad / n)]
    rc = cli.main(argv)
    out = capsys.readouterr().out
    assert rc == 1
    parsed = json.loads(out.strip().splitlines()[-1])
    assert parsed["ok"] is False and parsed["counts"]["error"] == 1
    assert parsed["issues"][0]["check"] == E.CHECK_VISUAL_MISSING and parsed["issues"][0]["severity"] == "error"
    assert (tmp_path / "b.txt").read_text(encoding="utf-8") == SWORD + "\n"


def test_cli_equip_strict_warnings_flag(tmp_path):
    bad = tmp_path / "data"
    for src in DATA_ROOTS:
        shutil.copytree(src, bad / src.name)
    path = bad / "_equip" / "sfx" / "sfx.def.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    doc["rows"] = [r for r in doc["rows"] if r["id"] != "sfx.swing.wood"]
    path.write_text(json.dumps(doc), encoding="utf-8")
    argv = ["equip", "--no-report"]
    for n in ("_framework", "_feel", "_equip"):
        argv += ["--data-root", str(bad / n)]
    assert cli.main(argv) == 0                      # 警告不让命令失败
    assert cli.main(argv + ["--strict-warnings"]) == 1


def test_check_domain_equip_is_opt_in_and_passes(capsys):
    assert "equip" not in check_cmd.DEFAULT_DOMAINS and "equip" not in check_cmd.ALL_DOMAINS
    assert check_cmd._parse_only("equip") == {"equip"}
    rc = cli.main(["check", "--dataset", "_equip", "--only", "equip", "--json"])
    doc = json.loads(capsys.readouterr().out.strip().splitlines()[-1])
    assert rc == 0 and doc["ok"] is True and doc["domains"] == ["equip"]
    assert doc["domain_counts"] == {"item.template": 6} and doc["counts"] == {"error": 0, "warning": 0}


def test_check_unknown_domain_still_rejected(capsys):
    assert cli.main(["check", "--dataset", "_equip", "--only", "nope"]) == 1
