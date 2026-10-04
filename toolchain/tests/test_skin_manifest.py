"""界面资源契约清单（``toolchain/asset_import/skin_manifest.json``）与皮肤包导入校验的具名诊断（手感设计/08、ADR-0149）。

- 清单自身：模板数/展开数由数据算出、占位皮肤逐位等于生成器输出、全元素参照皮肤零问题、人读清单覆盖每个文件；
- 每个具名诊断一个反例：在合格的参照皮肤上只破坏一处，断言恰好出现该诊断（且只出现它）。

运行：``python -m pytest toolchain/tests/test_skin_manifest.py -q``
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

from asset_import import cli, equip_pack as E, skin_manifest as M, skin_pack as S  # noqa: E402
from std_equip_set import skin as SK  # noqa: E402

REPO = TOOLCHAIN_DIR.parent
DATA_ROOTS = [REPO / "data" / "_framework", REPO / "data" / "_feel", REPO / "data" / "_equip"]


@pytest.fixture(scope="module")
def tables():
    return E.load_tables(DATA_ROOTS)


@pytest.fixture(scope="module")
def names(tables):
    return S.slot_names(tables), S.quality_names(tables)


@pytest.fixture()
def assets(tmp_path, names):
    """占位皮肤（生成器输出）+ 全元素参照皮肤 alt。"""
    SK.generate_skin(tmp_path)
    SK.generate_reference_pack(tmp_path / "ui" / "skin" / "alt", *names)
    return tmp_path


def _alt(assets: Path) -> Path:
    return assets / "ui" / "skin" / "alt"


def _check(tables, assets):
    issues, reports = S.check_skins(tables, assets, skin_refs=["skin.alt"])
    return issues, reports


def _only(issues, check):
    assert [i.check for i in issues] == [check], [i.render_text() for i in issues]
    return issues[0]


def _png(path: Path, size, fill=None, *, outline=None, center_clear=False):
    img = Image.new("RGBA", size, (0, 0, 0, 0) if fill is None else fill)
    if outline is not None:
        from PIL import ImageDraw
        ImageDraw.Draw(img).rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius=4, outline=outline, width=3)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)


# ---------------------------------------------------------------------------
# 清单自身
# ---------------------------------------------------------------------------

def test_manifest_counts_are_derived_from_data_and_templates_are_unique(names):
    slots, qualities = names
    templates = M.element_templates()
    assert len({t.id for t in templates}) == len(templates)
    assert len({t.path for t in templates}) == len(templates)
    files = M.expand(slots, qualities)
    # 展开数由数据算出：槽位模板×槽位数 + 品质模板×品质数 + 其余模板各 1。
    expected = sum(len(slots) if t.expand == "slot" else len(qualities) if t.expand == "quality" else 1 for t in templates)
    assert len(files) == expected == M.stats(slots, qualities)["files"]
    # 多一个槽位就多一个展开文件，多一个品质同理（不写死裸数）。
    assert len(M.expand(slots + ["extra"], qualities)) == len(files) + 1
    assert len(M.expand(slots, qualities + ["extra"])) == len(files) + 1
    st = M.stats(slots, qualities)
    assert st["required"] + st["optional"] + st["placeholder_only"] == st["files"]
    ids = {t.id for t in templates}
    for t in templates:
        assert t.requirement in (M.REQ_REQUIRED, M.REQ_OPTIONAL, M.REQ_PLACEHOLDER_ONLY)
        if t.needs:
            assert t.needs in ids
        if t.state_of:
            assert t.state_of in ids or any(o.state_of == t.state_of for o in templates)


def test_manifest_diagnostics_list_equals_the_check_names_of_the_two_modules():
    declared = set(M.load_manifest()["diagnostics"])
    emitted = {n for n in S.CHECK_NAMES}
    emitted |= {E.CHECK_ICON_SIZE_INVALID, E.CHECK_ICON_ALPHA_INVALID, E.CHECK_LAYER_IMAGE_INVALID,
                E.CHECK_LAYER_FRAME_COUNT_MISMATCH, E.CHECK_LAYER_FRAME_SIZE_MISMATCH, E.CHECK_LAYER_ATLAS_MISMATCH,
                E.CHECK_OPAQUE_COVERAGE_LOW, E.CHECK_ANCHOR_OUT_OF_BOUNDS, E.CHECK_ANCHOR_INVALID, E.CHECK_BEHIND_DIRECTION_INVALID}
    assert declared == emitted


def test_placeholder_skin_is_bit_for_bit_the_generator_output_and_check_is_clean(tmp_path, tables):
    SK.generate_skin(tmp_path)
    fresh = tmp_path / "ui" / "skin" / "default"
    repo = REPO / "assets" / "_placeholder" / "ui" / "skin" / "default"
    files = sorted(p.relative_to(repo).as_posix() for p in repo.rglob("*") if p.is_file())
    assert files == sorted(p.relative_to(fresh).as_posix() for p in fresh.rglob("*") if p.is_file())
    for rel in files:
        assert (repo / rel).read_bytes() == (fresh / rel).read_bytes(), rel
    issues, reports = S.check_skins(tables, REPO / "assets" / "_placeholder", skin_refs=[])
    assert issues == [] and reports[0].errors == 0


def test_reference_pack_covers_every_manifest_element_and_passes_clean(assets, tables, names):
    written = {p.relative_to(_alt(assets)).as_posix() for p in _alt(assets).rglob("*") if p.is_file()}
    assert written == {x.path for x in M.expand(*names)}
    issues, reports = _check(tables, assets)
    assert issues == []
    rep = reports[-1]
    assert rep.fallbacks == [] and rep.optional_absent == [] and rep.errors == 0
    # 参照皮肤里每个元素的颜色唯一（完整性用例靠它识别"这张图是哪个元素"）。
    colors = {SK.element_color(x.path) for x in M.expand(*names) if x.path != "theme.json"}
    assert len(colors) == len([x for x in M.expand(*names) if x.path != "theme.json"])


def test_checklist_lists_every_file_token_icon_category_and_diagnostic(names):
    text = M.render_checklist(*names)
    for x in M.expand(*names):
        assert f"`{x.path}`" in text
    for c in M.token_spec()["colors"]:
        assert f"colors.{c['key']}" in text
    for c in M.load_manifest()["icons"]["categories"]:
        assert f"`{c['name']}`" in text
    for d in M.load_manifest()["diagnostics"]:
        assert d in text


def test_checklist_cli_prints_stats_json(capsys):
    assert cli.main(["skin-checklist", "--json"]) == 0
    out = json.loads(capsys.readouterr().out)
    assert out["templates"] == len(M.element_templates()) and out["files"] >= out["templates"]


# ---------------------------------------------------------------------------
# 具名诊断：一个反例一条
# ---------------------------------------------------------------------------

def test_neg_pack_missing(assets, tables):
    issues, _ = S.check_skins(tables, assets, skin_refs=["skin.nope"])
    assert S.CHECK_SKIN_PACK_MISSING in {i.check for i in issues}


def test_neg_item_missing(assets, tables):
    (_alt(assets) / "slot_frame" / "std_chest.png").unlink()
    issue = _only(_check(tables, assets)[0], S.CHECK_SKIN_ITEM_MISSING)
    assert issue.severity == "warning" and issue.fallback == "skin.default:slot_frame/std_chest.png"


def test_neg_state_missing_required_variant(assets, tables):
    (_alt(assets) / "slot_frame" / "_highlight.png").unlink()
    issue = _only(_check(tables, assets)[0], S.CHECK_SKIN_STATE_MISSING)
    assert "hover" in issue.message and issue.fallback == "skin.default:slot_frame/_highlight.png"


def test_neg_state_missing_when_variant_given_without_its_normal(assets, tables):
    (_alt(assets) / "button" / "normal.png").unlink()
    issues, _ = _check(tables, assets)
    # hover/pressed/disabled/selected 四个依赖 normal，各报一次。
    assert [i.check for i in issues] == [S.CHECK_SKIN_STATE_MISSING] * 4
    assert all("button/normal.png" in i.message for i in issues)


def test_optional_element_absent_is_recorded_but_not_a_problem(assets, tables):
    shutil.rmtree(_alt(assets) / "button")
    (_alt(assets) / "panel" / "background.png").unlink()
    (_alt(assets) / "slot_frame" / "_selected.png").unlink()
    issues, reports = _check(tables, assets)
    assert issues == []
    assert sorted(reports[-1].optional_absent) == sorted(
        ["button/normal.png", "button/hover.png", "button/pressed.png", "button/disabled.png", "button/selected.png",
         "panel/background.png", "slot_frame/_selected.png"])


def test_neg_placeholder_missing(assets, tables):
    (assets / "ui" / "skin" / "default" / "drag" / "ghost.png").unlink()
    issues, _ = _check(tables, assets)
    assert [(i.check, i.severity) for i in issues] == [(S.CHECK_SKIN_PLACEHOLDER_MISSING, "error")]


def test_neg_panel_layout_missing(assets, tables):
    t = {k: list(v) for k, v in tables.items()}
    t["ui_layout_definition"] = [r for r in t["ui_layout_definition"] if r["panel"] != "action_bar"]
    issues, _ = S.check_skins(t, assets, skin_refs=["skin.alt"])
    _only(issues, S.CHECK_SKIN_PANEL_LAYOUT_MISSING)


def test_neg_ref_invalid(assets, tables):
    issues, _ = S.check_skins(tables, assets, skin_refs=["alt"])
    assert [(i.check, i.severity) for i in issues] == [(S.CHECK_SKIN_REF_INVALID, "error")]


def test_neg_theme_invalid(assets, tables):
    (_alt(assets) / "theme.json").write_text("{\"colors\": {\"panel\": \"#000000\"}}", encoding="utf-8")
    _only(_check(tables, assets)[0], S.CHECK_SKIN_THEME_INVALID)      # 缺 font


def test_neg_theme_token_missing(assets, tables):
    theme = json.loads((_alt(assets) / "theme.json").read_text(encoding="utf-8"))
    del theme["colors"]["blocked"]
    (_alt(assets) / "theme.json").write_text(json.dumps(theme), encoding="utf-8")
    issue = _only(_check(tables, assets)[0], S.CHECK_SKIN_THEME_TOKEN_MISSING)
    assert issue.field_path == "colors.blocked" and issue.fallback == "框架默认色"


@pytest.mark.parametrize("bad", ["red", "#12345", "#gggggg", 123, None])
def test_neg_theme_color_invalid(assets, tables, bad):
    theme = json.loads((_alt(assets) / "theme.json").read_text(encoding="utf-8"))
    theme["colors"]["text"] = bad
    (_alt(assets) / "theme.json").write_text(json.dumps(theme), encoding="utf-8")
    _only(_check(tables, assets)[0], S.CHECK_SKIN_THEME_COLOR_INVALID)


def test_theme_color_accepts_rrggbbaa(assets, tables):
    theme = json.loads((_alt(assets) / "theme.json").read_text(encoding="utf-8"))
    theme["colors"]["text"] = "#11223344"
    (_alt(assets) / "theme.json").write_text(json.dumps(theme), encoding="utf-8")
    assert _check(tables, assets)[0] == []


def test_neg_image_unreadable(assets, tables):
    (_alt(assets) / "drag" / "ghost.png").write_bytes(b"not a png")
    issue = _only(_check(tables, assets)[0], S.CHECK_SKIN_IMAGE_UNREADABLE)
    assert issue.severity == "error"


@pytest.mark.parametrize("rel,size", [
    ("slot_frame/std_chest.png", (48, 40)),         # 比例：必须正方形
    ("slot_frame/std_chest.png", (8, 8)),           # 小于下限（同时与同组其它图不同尺寸，另见 group 用例；这里只关心 size）
    ("tooltip/divider.png", (1, 1)),                # 小于下限
    ("paperdoll_preview/background.png", (2000, 2000)),   # 大于上限
])
def test_neg_size_invalid(assets, tables, rel, size):
    if rel.startswith("slot"):
        _png(_alt(assets) / rel, size, outline=(200, 100, 50, 255))
    else:
        _png(_alt(assets) / rel, size, (200, 100, 50, 255))
    issues, _ = _check(tables, assets)
    assert S.CHECK_SKIN_SIZE_INVALID in {i.check for i in issues}
    assert {i.check for i in issues} <= {S.CHECK_SKIN_SIZE_INVALID, S.CHECK_SKIN_SIZE_GROUP_MISMATCH}


def test_neg_size_group_mismatch(assets, tables):
    _png(_alt(assets) / "quality_frame" / "std_rare.png", (64, 64), outline=(1, 2, 3, 255))
    issue = _only(_check(tables, assets)[0], S.CHECK_SKIN_SIZE_GROUP_MISMATCH)
    assert "quality_frame/std_rare.png" in issue.message and "64x64" in issue.message


def test_neg_nineslice_border_too_big_for_image_default_border(assets, tables):
    _png(_alt(assets) / "panel" / "background.png", (8, 8), (10, 20, 30, 255))        # 默认边框 5，5×2 ≥ 8
    _only(_check(tables, assets)[0], S.CHECK_SKIN_NINESLICE_INVALID)


def test_neg_nineslice_border_from_theme_token(assets, tables):
    theme = json.loads((_alt(assets) / "theme.json").read_text(encoding="utf-8"))
    theme["button_border"] = 16                                                         # 按钮图 32x32，16×2 ≥ 32
    (_alt(assets) / "theme.json").write_text(json.dumps(theme), encoding="utf-8")
    issues, _ = _check(tables, assets)
    assert [i.check for i in issues] == [S.CHECK_SKIN_NINESLICE_INVALID] * 5            # 五张按钮图各一


def test_nineslice_border_that_fits_is_clean(assets, tables):
    theme = json.loads((_alt(assets) / "theme.json").read_text(encoding="utf-8"))
    theme["panel_border"] = 15                                                          # 32×32：15×2 < 32
    (_alt(assets) / "theme.json").write_text(json.dumps(theme), encoding="utf-8")
    assert _check(tables, assets)[0] == []


def test_neg_alpha_fully_transparent(assets, tables):
    _png(_alt(assets) / "slot_frame" / "std_chest.png", (48, 48))
    issue = _only(_check(tables, assets)[0], S.CHECK_SKIN_ALPHA_INVALID)
    assert "全透明" in issue.message


def test_neg_alpha_opaque_frame_has_no_transparency(assets, tables):
    _png(_alt(assets) / "slot_frame" / "std_chest.png", (48, 48), (90, 90, 90, 255))
    issue = _only(_check(tables, assets)[0], S.CHECK_SKIN_ALPHA_INVALID)
    assert "透明像素" in issue.message


def test_neg_alpha_quality_frame_center_must_be_clear(assets, tables):
    _png(_alt(assets) / "quality_frame" / "std_common.png", (48, 48), (90, 90, 90, 90))   # 半透明满铺：有透明度但中心不空
    issue = _only(_check(tables, assets)[0], S.CHECK_SKIN_ALPHA_INVALID)
    assert "中心" in issue.message


def test_every_skin_diagnostic_has_a_negative_test_here():
    source = Path(__file__).read_text(encoding="utf-8")
    for name in S.CHECK_NAMES:
        const = "CHECK_" + name[len("equip_"):].upper()
        assert const in source, f"{name} 没有反例用例"
