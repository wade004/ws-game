"""参考皮肤包 ``skin.reference_fantasy``（``assets/_reference_fantasy``，ADR-0152）的导入校验回归与"真实美术形态"负例。

参考皮肤包是用本地 ComfyUI 出图 + 后处理脚本（``assets/_reference_fantasy/_source``）做出的一套真实美术：槽位框（8 个槽位 + 兜底 + 五态）、
五档品质框、拖拽三态、提示框三件、纸娃娃预览区背景、面板/按钮九宫格（按钮五态）、theme.json，以及样例装备物品的图标与纸娃娃静态层图。
门禁仍用程序化占位皮肤保证逐位复现；本文件让"真实美术过一遍导入链路"成为永久回归：

- 参考包 + 占位装备集：``import_assets.py equip`` 零错误零警告，optional_absent 为空，覆盖清单全部元素（样例数据口径与"8 槽位 × 5 品质"扩展口径）；
- 真实美术才有的不变量（校验器只管清单规则，不管这些）：脏 alpha、格子组同尺寸、九宫格边框 = theme 令牌且边沿拉伸方向恒定、按钮五态轮廓对齐；
- 负例：拿参考包的真实图片只破坏一处，断言恰好触发对应具名诊断；校验器管不到的缺陷（令牌与美术不符、态间错位）由本文件的 lint 拦下并有反例。

运行：``python -m pytest toolchain/tests/test_reference_skin_pack.py -q``
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest
from PIL import Image

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from asset_import import cli, equip_cmd, equip_pack as E, skin_manifest as M, skin_pack as S  # noqa: E402

REPO = TOOLCHAIN_DIR.parent
REF = REPO / "assets" / "_reference_fantasy"
PLACEHOLDER = REPO / "assets" / "_placeholder"
SPEC = json.loads((REF / "_source" / "pack_spec.json").read_text(encoding="utf-8"))
PACK = REF / "ui" / "skin" / SPEC["pack"]
SKIN_REF = SPEC["skin_ref"]
DATA_ROOTS = [REPO / "data" / "_framework", REPO / "data" / "_feel", REPO / "data" / "_equip"]
CELL = SPEC["cell"]


# ---------------------------------------------------------------------------
# 夹具
# ---------------------------------------------------------------------------

def _link_or_copy(src: Path, dst: Path) -> None:
    """把大目录（sprite_anim 约 70 MB）以目录联接接进临时资产目录；联接失败退回复制。"""
    dst.parent.mkdir(parents=True, exist_ok=True)
    try:
        if os.name == "nt":
            subprocess.run(["cmd", "/c", "mklink", "/J", str(dst), str(src)], check=True, capture_output=True)
        else:
            os.symlink(src, dst, target_is_directory=True)
    except (OSError, subprocess.CalledProcessError):
        shutil.copytree(src, dst)


def _unlink(path: Path) -> None:
    try:
        os.rmdir(path)                      # 联接/符号链接：只摘链接，不动目标
    except OSError:
        shutil.rmtree(path, ignore_errors=True)


def build_composite(root: Path, *, with_anim: bool) -> Path:
    """参考资产叠在占位资产上的临时资产目录：占位皮肤 + 参考皮肤包 + 参考图标/静态层覆盖占位同名文件 + （可选）占位逐层剪辑。"""
    shutil.copytree(PLACEHOLDER / "ui", root / "ui")
    shutil.copytree(REF / "ui" / "skin" / SPEC["pack"], root / "ui" / "skin" / SPEC["pack"])
    shutil.copytree(PLACEHOLDER / "icons", root / "icons")
    shutil.copytree(PLACEHOLDER / "sprites", root / "sprites")
    for sub in ("icons", "sprites"):
        if (REF / sub).is_dir():
            shutil.copytree(REF / sub, root / sub, dirs_exist_ok=True)
    if with_anim:
        _link_or_copy(PLACEHOLDER / "sprite_anim", root / "sprite_anim")
    return root


@pytest.fixture(scope="module")
def tables():
    return E.load_tables(DATA_ROOTS)


@pytest.fixture(scope="module")
def names(tables):
    return S.slot_names(tables), S.quality_names(tables)


@pytest.fixture(scope="module")
def full_composite(tmp_path_factory):
    root = build_composite(tmp_path_factory.mktemp("refskin_full"), with_anim=True)
    yield root
    _unlink(root / "sprite_anim")


@pytest.fixture()
def skin_only(tmp_path):
    """只含占位皮肤与参考皮肤包的资产目录（皮肤负例用，不跑装备部分）。"""
    shutil.copytree(PLACEHOLDER / "ui", tmp_path / "ui")
    shutil.copytree(PACK, tmp_path / "ui" / "skin" / SPEC["pack"])
    return tmp_path


@pytest.fixture()
def equip_composite(tmp_path):
    """含图标/静态层覆盖与占位逐层剪辑的资产目录（图标、层图负例用）。"""
    root = build_composite(tmp_path, with_anim=True)
    yield root
    _unlink(root / "sprite_anim")


def _pack(assets: Path) -> Path:
    return assets / "ui" / "skin" / SPEC["pack"]


def _check(tables, assets):
    return S.check_skins(tables, assets, skin_refs=[SKIN_REF])


def _only(issues, check):
    assert [i.check for i in issues] == [check], [i.render_text() for i in issues]
    return issues[0]


def _rgba(path: Path) -> Image.Image:
    return Image.open(path).convert("RGBA")


def _extended_tables(tables):
    """样例数据只有 2 个槽位 2 个品质；按 pack_spec 补到 8 槽位 × 5 品质（只加槽位/品质定义行，不加物品）。"""
    t = {k: list(v) for k, v in tables.items()}
    have_slots = {r["id"] for r in t["item.slot_definition"]}
    for i, name in enumerate(SPEC["slots"]):
        rid = "item.slot." + name
        if rid not in have_slots:
            t["item.slot_definition"].append({"id": rid, "name_key": f"l10n.{rid}.name", "sort_weight": 20 + i, "is_weapon": False,
                                              "is_equipment": True, "budget_coefficient": 1.0, "price_coefficient": 1.0, "has_armor": False})
    have_q = {r["id"] for r in t["item.quality_definition"]}
    for i, name in enumerate(SPEC["qualities"]):
        rid = "item.quality." + name
        if rid not in have_q:
            t["item.quality_definition"].append({"id": rid, "name_key": f"l10n.{rid}.name", "sort_weight": 10 + i, "budget_multiplier": 1.0,
                                                 "affix_count": 0, "grant_budget_share": 0.0, "price_multiplier": 1.0})
    return t


# ---------------------------------------------------------------------------
# 九宫格与按钮态 lint（校验器按清单只核"边框 ×2 小于边长"，管不到美术与令牌是否一致；这里补上并有反例）
# ---------------------------------------------------------------------------

def nine_slice_lint(img: Image.Image, border: int, *, tol: int = 8) -> list[str]:
    """返回问题清单：四角镜像对称；上/下边带沿 x、左/右边带沿 y 恒定（拉伸不糊开花纹）；角之外的"边框"像素确实在 border 之内收敛
    （边带宽度正好 border：往里再走一个像素就进入恒定的中心）。"""
    w, h = img.size
    px = img.load()
    problems: list[str] = []

    def diff(a, b):
        return max(abs(a[i] - b[i]) for i in range(4))

    # 四角镜像
    for y in range(border):
        for x in range(border):
            tl = px[x, y]
            for name, other in (("右上", px[w - 1 - x, y]), ("左下", px[x, h - 1 - y]), ("右下", px[w - 1 - x, h - 1 - y])):
                if diff(tl, other) > tol:
                    problems.append(f"{name}角与左上角不镜像对称（像素 {x},{y}）")
                    break
            if problems:
                break
        if problems:
            break
    # 上下边带沿 x 恒定
    for y in range(border):
        row = [px[x, y] for x in range(border, w - border)]
        if row and max(diff(row[0], c) for c in row) > tol:
            problems.append(f"上边带第 {y} 行沿 x 不恒定（拉伸会糊开花纹）")
            break
    for y in range(h - border, h):
        row = [px[x, y] for x in range(border, w - border)]
        if row and max(diff(row[0], c) for c in row) > tol:
            problems.append(f"下边带第 {y} 行沿 x 不恒定")
            break
    for x in range(border):
        col = [px[x, y] for y in range(border, h - border)]
        if col and max(diff(col[0], c) for c in col) > tol:
            problems.append(f"左边带第 {x} 列沿 y 不恒定")
            break
    for x in range(w - border, w):
        col = [px[x, y] for y in range(border, h - border)]
        if col and max(diff(col[0], c) for c in col) > tol:
            problems.append(f"右边带第 {x} 列沿 y 不恒定")
            break
    # 边带宽度正好 border：紧贴边带内侧的一行/列不应还带着边框的结构（与中心色一致）
    center = px[w // 2, h // 2]
    if diff(px[w // 2, border], center) > 24:
        problems.append(f"边框宽度大于令牌 {border}：紧贴边带内侧的像素与中心色不一致")

    return problems


def _alpha_mask(img: Image.Image):
    return img.getchannel("A").point(lambda v: 255 if v > 127 else 0)


def button_state_lint(pack: Path) -> list[str]:
    """按钮五态：同尺寸、不透明轮廓逐像素一致（态间对齐，切换状态时轮廓不抖）。"""
    base = _rgba(pack / "button" / "normal.png")
    out = []
    for st in ("hover", "pressed", "disabled", "selected"):
        im = _rgba(pack / "button" / f"{st}.png")
        if im.size != base.size:
            out.append(f"{st}: 尺寸 {im.size} 与 normal {base.size} 不同")
        elif list(_alpha_mask(im).getdata()) != list(_alpha_mask(base).getdata()):
            out.append(f"{st}: 不透明轮廓与 normal 不一致")
    return out


# ---------------------------------------------------------------------------
# 导入校验：参考包零错误零警告、覆盖清单全部元素
# ---------------------------------------------------------------------------

def test_reference_pack_equip_import_is_clean_and_covers_every_manifest_element(full_composite, tables, names):
    report = equip_cmd.run_equip(DATA_ROOTS, full_composite, skin_refs=[SKIN_REF])
    assert [i.render_text() for i in report.issues] == []
    assert report.error_count == 0 and report.warning_count == 0
    ref = [s for s in report.skins if s["skin_ref"] == SKIN_REF][0]
    assert ref["errors"] == 0 and ref["fallbacks"] == 0 and ref["optional_absent"] == 0
    files = [x for x in M.expand(*names) if x.requirement != M.REQ_PLACEHOLDER_ONLY]
    assert ref["checked"] == len(files)                     # 清单展开的必备 + 可选元素逐个核对过
    # 参考包里每个清单元素（含占位必备的兜底）都在，且只有清单元素 + 扩展槽位/品质框
    present = {p.relative_to(_pack(full_composite)).as_posix() for p in _pack(full_composite).rglob("*") if p.is_file()}
    assert {x.path for x in M.expand(*names)} <= present
    # 每件样例装备的图标都是参考图标（不是占位）
    for it in report.items:
        assert it.icon and (full_composite / it.icon).read_bytes() == (REF / it.icon).read_bytes(), it.item_id


def test_reference_pack_covers_extended_slots_and_the_five_quality_tiers(tmp_path, tables):
    ext = _extended_tables(tables)
    slots, qualities = S.slot_names(ext), S.quality_names(ext)
    assert len(slots) == len(SPEC["slots"]) and len(qualities) == len(SPEC["qualities"]) == 5
    expanded = M.expand(slots, qualities)
    present = {p.relative_to(PACK).as_posix() for p in PACK.rglob("*") if p.is_file()}
    assert {x.path for x in expanded} == present            # 包里恰好是清单展开的全集（没有多余/缺失文件）
    # 占位皮肤只带样例数据的槽位/品质，扩展口径下占位缺项；校验扩展口径时用"占位 = 参考包拷贝"的资产目录，只看参考包自己
    shutil.copytree(PACK, tmp_path / "ui" / "skin" / "default")
    shutil.copytree(PACK, tmp_path / "ui" / "skin" / SPEC["pack"])
    issues, reports = S.check_skins(ext, tmp_path, skin_refs=[SKIN_REF])
    assert [i.render_text() for i in issues] == []
    assert reports[-1].fallbacks == [] and reports[-1].optional_absent == [] and reports[-1].errors == 0


def test_reference_pack_cli_exit_code_zero_with_strict_warnings(full_composite):
    rc = cli.main(["equip", "--assets-dir", str(full_composite), "--skin-ref", SKIN_REF, "--strict-warnings", "--no-report"])
    assert rc == 0


def test_placeholder_skin_and_defaults_untouched_by_the_reference_pack():
    """参考包是独立的样例皮肤包：不在占位资产目录里，缺省皮肤仍是程序化占位。"""
    assert not (PLACEHOLDER / "ui" / "skin" / SPEC["pack"]).exists()
    default_theme = json.loads((PLACEHOLDER / "ui" / "skin" / "default" / "theme.json").read_text(encoding="utf-8"))
    assert default_theme["colors"]["panel"] == "#343a48"


# ---------------------------------------------------------------------------
# 真实美术的不变量
# ---------------------------------------------------------------------------

def _all_pngs():
    return sorted([p for p in REF.rglob("*.png") if "_source" not in p.parts])


def test_every_png_is_rgba_with_clean_alpha_and_no_fringe_colors():
    for p in _all_pngs():
        im = Image.open(p)
        assert im.mode == "RGBA", p
        a = im.getchannel("A")
        hist = a.histogram()
        assert sum(hist[1:6]) == 0, f"{p.relative_to(REF)} 有 alpha 1..5 的脏点（后处理 clean_alpha 应清零）"
        # 透明像素的 RGB 必须是 0（缩放在预乘空间做，透明处不残留底色，打包/再采样不会渗紫边）
        px = im.load()
        w, h = im.size
        for y in range(0, h, max(1, h // 64)):
            for x in range(0, w, max(1, w // 64)):
                r, g, b, aa = px[x, y]
                assert aa != 0 or (r, g, b) == (0, 0, 0), f"{p.relative_to(REF)} 在 ({x},{y}) 透明处残留颜色"


def test_cell_group_is_one_size_and_icons_are_power_of_two_with_margin():
    group = [PACK / "slot_frame" / f"{n}.png" for n in (*SPEC["slots"], "_default", "_highlight", "_disabled", "_drag_hover", "_pressed", "_selected")]
    group += [PACK / "quality_frame" / f"{n}.png" for n in (*SPEC["qualities"], "_default")]
    group += [PACK / "drag" / f"{n}.png" for n in ("ghost", "target_ok", "target_blocked")]
    assert {Image.open(p).size for p in group} == {(CELL, CELL)}
    icons = sorted((REF / "icons" / "item").glob("*.png"))
    assert len(icons) >= 6
    size = SPEC["icon"]["size"]
    for p in icons:
        im = _rgba(p)
        assert im.size == (size, size) and (size & (size - 1)) == 0
        x0, y0, x1, y1 = im.getchannel("A").getbbox()
        assert min(x0, y0, size - x1, size - y1) >= (size - SPEC["icon"]["max_extent"]) // 2 - 1, p.name     # 四周留透明边距


def test_theme_tokens_match_spec_and_nine_slice_art():
    theme = json.loads((PACK / "theme.json").read_text(encoding="utf-8"))
    for key, value in SPEC["tokens"].items():
        assert theme[key] == value
    for rel, token in (("panel/background.png", "panel_border"), ("tooltip/background.png", "tooltip_border")):
        assert nine_slice_lint(_rgba(PACK / rel), theme[token]) == [], rel
    for st in ("normal", "hover", "pressed", "disabled", "selected"):
        assert nine_slice_lint(_rgba(PACK / "button" / f"{st}.png"), theme["button_border"]) == [], st
    assert button_state_lint(PACK) == []


def test_quality_frames_have_clear_center_and_distinct_tier_colors():
    means = []
    for q in SPEC["qualities"]:
        im = _rgba(PACK / "quality_frame" / f"{q}.png")
        c = im.crop((CELL // 4, CELL // 4, CELL - CELL // 4, CELL - CELL // 4)).getchannel("A")
        assert c.getextrema() == (0, 0), q
        px = [p for p in im.getdata() if p[3] > 200]
        means.append(tuple(sum(p[i] for p in px) // len(px) for i in range(3)))
    assert len(set(means)) == len(means)                   # 五档的主色互不相同（品质阶梯可区分）


# ---------------------------------------------------------------------------
# lint 自身的反例（拿真实美术破坏一处）
# ---------------------------------------------------------------------------

def test_lint_catches_token_smaller_than_the_art_border():
    # 美术按 panel_border=16 装配；theme 令牌若写成 6，拉伸时角被当成边拉歪——校验器看不到，lint 看得到
    img = _rgba(PACK / "panel" / "background.png")
    assert nine_slice_lint(img, SPEC["tokens"]["panel_border"]) == []
    assert nine_slice_lint(img, 6) != []


def test_lint_catches_corner_asymmetry():
    # 左上角与右上角不镜像：九宫格拉伸后四角不对称（校验器只核边框 x2 小于边长，看不到）
    img = _rgba(PACK / "button" / "normal.png")
    b = SPEC["tokens"]["button_border"]
    assert nine_slice_lint(img, b) == []
    broken = img.copy()
    w, h = broken.size
    corner = broken.crop((0, 0, b, b))
    broken.paste(corner.transpose(Image.Transpose.FLIP_TOP_BOTTOM), (w - b, 0))
    assert any("镜像" in p for p in nine_slice_lint(broken, b))


def test_lint_catches_button_state_misalignment(tmp_path):
    shutil.copytree(PACK, tmp_path / "p")
    hover = _rgba(tmp_path / "p" / "button" / "hover.png")
    shifted = Image.new("RGBA", hover.size, (0, 0, 0, 0))
    shifted.paste(hover, (1, 0))
    shifted.save(tmp_path / "p" / "button" / "hover.png")
    assert button_state_lint(tmp_path / "p") == ["hover: 不透明轮廓与 normal 不一致"]


# ---------------------------------------------------------------------------
# 真实美术形态的负例：只破坏一处，断言恰好触发对应具名诊断
# ---------------------------------------------------------------------------

def test_neg_real_slot_frame_not_square(skin_only, tables):
    p = _pack(skin_only) / "slot_frame" / "std_chest.png"
    _rgba(p).resize((CELL, CELL - 16), Image.LANCZOS).save(p)
    issues, _ = _check(tables, skin_only)
    assert S.CHECK_SKIN_SIZE_INVALID in {i.check for i in issues}
    assert {i.check for i in issues} <= {S.CHECK_SKIN_SIZE_INVALID, S.CHECK_SKIN_SIZE_GROUP_MISMATCH}


def test_neg_real_quality_frame_wrong_cell_size(skin_only, tables):
    p = _pack(skin_only) / "quality_frame" / "std_rare.png"
    _rgba(p).resize((CELL * 2, CELL * 2), Image.LANCZOS).save(p)
    issue = _only(_check(tables, skin_only)[0], S.CHECK_SKIN_SIZE_GROUP_MISMATCH)
    assert "quality_frame/std_rare.png" in issue.message


def test_neg_real_quality_frame_covers_the_icon(skin_only, tables):
    p = _pack(skin_only) / "quality_frame" / "std_common.png"
    im = _rgba(p)
    icon = _rgba(REF / "icons" / "item" / "std_sword_1h.png").resize((CELL // 2, CELL // 2), Image.LANCZOS)
    im.alpha_composite(icon, (CELL // 4, CELL // 4))
    im.save(p)
    issue = _only(_check(tables, skin_only)[0], S.CHECK_SKIN_ALPHA_INVALID)
    assert "中心" in issue.message


def test_neg_real_quality_frame_with_alpha_noise_in_the_center_reports_the_alpha_value(skin_only, tables):
    # 出图模型常在"透明"处留下 alpha=1..3 的底噪：肉眼不可见，但中心区要求"全透明"，必须被拦下，且报错要说清是多少 alpha、在哪
    p = _pack(skin_only) / "quality_frame" / "std_rare.png"      # 样例数据有 std_common/std_rare 两档，只有它们会被校验遍历
    im = _rgba(p)
    im.putpixel((CELL // 2 + 3, CELL // 2 - 5), (200, 180, 90, 3))
    im.save(p)
    issue = _only(_check(tables, skin_only)[0], S.CHECK_SKIN_ALPHA_INVALID)
    assert "alpha=3" in issue.message and f"({CELL // 2 + 3}, {CELL // 2 - 5})" in issue.message
    # 同一件事在报告里要能直接定位：不能只说"不是全透明"


def test_neg_real_slot_frame_flattened_to_opaque(skin_only, tables):
    p = _pack(skin_only) / "slot_frame" / "std_main_hand.png"
    im = _rgba(p)
    flat = Image.new("RGBA", im.size, (24, 26, 34, 255))
    flat.alpha_composite(im)
    flat.save(p)
    issue = _only(_check(tables, skin_only)[0], S.CHECK_SKIN_ALPHA_INVALID)
    assert "透明像素" in issue.message


def test_neg_real_panel_border_token_exceeds_half_the_image(skin_only, tables):
    theme_path = _pack(skin_only) / "theme.json"
    theme = json.loads(theme_path.read_text(encoding="utf-8"))
    theme["panel_border"] = 64                                # 128x128 的真实面板底图
    theme_path.write_text(json.dumps(theme), encoding="utf-8")
    _only(_check(tables, skin_only)[0], S.CHECK_SKIN_NINESLICE_INVALID)


def test_neg_real_missing_state_variant_and_optional_dependency(skin_only, tables):
    (_pack(skin_only) / "slot_frame" / "_drag_hover.png").unlink()
    (_pack(skin_only) / "button" / "normal.png").unlink()
    issues, _ = _check(tables, skin_only)
    assert [i.check for i in issues] == [S.CHECK_SKIN_STATE_MISSING] * 5        # 槽位 drag_hover 1 条 + 按钮四态各缺依赖
    assert any("slot_frame/_drag_hover.png" in i.message for i in issues)


def test_neg_real_truncated_png_is_unreadable(skin_only, tables):
    p = _pack(skin_only) / "drag" / "ghost.png"
    p.write_bytes(p.read_bytes()[:200])
    _only(_check(tables, skin_only)[0], S.CHECK_SKIN_IMAGE_UNREADABLE)


@pytest.mark.parametrize("case", ["non_power_of_two", "not_square", "no_alpha_channel", "touches_edge", "fully_transparent"])
def test_neg_real_icon(equip_composite, case):
    p = equip_composite / "icons" / "item" / "std_sword_1h.png"
    im = _rgba(p)
    if case == "non_power_of_two":
        im.resize((100, 100), Image.LANCZOS).save(p)
        want = E.CHECK_ICON_SIZE_INVALID
    elif case == "not_square":
        im.resize((128, 96), Image.LANCZOS).save(p)
        want = E.CHECK_ICON_SIZE_INVALID
    elif case == "no_alpha_channel":
        bg = Image.new("RGB", im.size, (40, 40, 48))
        bg.paste(im, mask=im.getchannel("A"))
        bg.save(p)                                            # RGB PNG：没有 alpha 通道
        want = E.CHECK_ICON_ALPHA_INVALID
    elif case == "touches_edge":
        box = im.getchannel("A").getbbox()
        im.crop(box).resize(im.size, Image.LANCZOS).save(p)
        want = E.CHECK_ICON_ALPHA_INVALID
    else:
        Image.new("RGBA", im.size, (0, 0, 0, 0)).save(p)
        want = E.CHECK_ICON_ALPHA_INVALID
    problem = E.check_icon_image(p)
    assert problem is not None and problem[0] == want, problem
    if case == "no_alpha_channel":
        assert "没有任何透明像素" in problem[1]                     # 说清楚是"没有 alpha"，而不是误导成"主体贴边"
    report = equip_cmd.run_equip(DATA_ROOTS, equip_composite)
    hits = [i for i in report.issues if i.record_key == "item.std_sword_1h"]
    assert [i.check for i in hits] == [want]


def test_neg_real_static_layer_opaque_square(equip_composite):
    p = equip_composite / "sprites" / "item_std_sword_1h" / "front" / "hand_main.png"
    im = _rgba(p)
    flat = Image.new("RGBA", im.size, (90, 90, 100, 255))
    flat.alpha_composite(im)
    flat.save(p)
    report = equip_cmd.run_equip(DATA_ROOTS, equip_composite)
    hits = [i for i in report.issues if i.record_key == "item.std_sword_1h"]
    assert [i.check for i in hits] == [E.CHECK_LAYER_IMAGE_INVALID]
