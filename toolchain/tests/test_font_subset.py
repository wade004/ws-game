"""``toolchain/font_subset.py`` 的验收（P4 内容起步包配套，ADR-0162）。

不变量（期望值由数据本身算出，不写死裸数）：

1. **覆盖**：输出子集字体的 cmap 覆盖数据根里 ``l10n.text`` 出现过的全部字符（多个数据根、多语言、多文件都要扫到），
   也覆盖常用字表（默认带）；
2. **体积**：子集字体远小于整套字体（常用字表 + 起步包文案 <= 整套的 1/4；只留数据字符 <= 1/20）；
3. **缺字报告**：数据里出现源字体没有的字符（私用区字符）时，退出码 1、缺字清单在报告里、``--strict`` 时不写文件；
4. **确定性**：同样的输入产生字节相同的输出；
5. **默认字体 + 起步包**：默认字体（OFL 许可证随发）能装下起步包的全部文案，不缺字。

覆盖与体积两条用例走真实框架字体（16 MiB，各做一次子集，约半分钟）；缺字、确定性、命令行这类与字形无关的用例用现场造的微型字体，秒级。没有 ``fontTools`` 的环境整体跳过。
运行：``python -m pytest toolchain/tests/test_font_subset.py -q``
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

# fontTools 是 toolchain/requirements.txt 声明的必需依赖：缺依赖必须明确失败，不得整体跳过（跳过会被门禁的 skipped=0 判红，
# 且更糟的是在没有该门禁的环境里悄悄不测）。2.4.0～2.8.0 的 CI 红就是因为这里原来是 importorskip 而 CI 没装它。
import fontTools  # noqa: F401

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent
sys.path.insert(0, str(TOOLCHAIN_DIR))

import font_subset  # noqa: E402

KIT_ROOT = REPO_ROOT / "data" / "_starter_kit"
FONT = font_subset.DEFAULT_FONT


def _write_text_table(root: Path, relative: str, rows: list[tuple[str, str, str]]) -> None:
    target = root / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    doc = {"table": "l10n.text", "schema_version": 1,
           "rows": [{"key": k, "locale": loc, "text": text} for k, loc, text in rows]}
    target.write_text(json.dumps(doc, ensure_ascii=False), encoding="utf-8")


def _cmap(path: Path) -> set[int]:
    return font_subset.font_cmap(path)


def test_license_ships_next_to_the_default_font() -> None:
    license_file = FONT.parent / "LICENSE-OFL.txt"
    assert FONT.is_file()
    assert license_file.is_file()
    assert "SIL OPEN FONT LICENSE" in license_file.read_text(encoding="utf-8").upper()


def test_common_chars_cover_level_one_hanzi_ascii_and_punctuation() -> None:
    common = font_subset.common_chars()
    hanzi = [c for c in common if "一" <= c <= "鿿"]
    assert len(hanzi) >= 3700                       # GB2312 一级字 3755 个
    for ch in "的一是不了人我在有他这中大来上":
        assert ch in common
    for ch in "AZaz09，。！？（）":
        assert ch in common


def test_collects_characters_from_every_root_language_and_file(tmp_path: Path) -> None:
    root_a = tmp_path / "a"
    root_b = tmp_path / "b"
    _write_text_table(root_a, "l10n/l10n.text.json", [("l10n.a", "l10n.locale.zh_cn", "龘{count}"), ("l10n.a", "l10n.locale.en", "Zq")])
    _write_text_table(root_b, "extra/l10n.text.json", [("l10n.b", "l10n.locale.zh_cn", "爨")])

    used = font_subset.collect_data_chars([root_a, root_b])

    assert set("龘爨Zq{}count") <= used
    assert all(ch >= " " for ch in used)
    with pytest.raises(FileNotFoundError):
        font_subset.collect_data_chars([tmp_path / "missing"])


@pytest.fixture(scope="module")
def kit_subsets(tmp_path_factory: pytest.TempPathFactory) -> dict:
    """真实字体上的两份子集（模块内只做一次）：常用字表 + 起步包文案、只留起步包用字。"""
    base = tmp_path_factory.mktemp("kit_subsets")
    full = font_subset.subset_font(FONT, font_subset.build_charset([KIT_ROOT], include_common=True), base / "common.otf")
    only = font_subset.subset_font(FONT, font_subset.build_charset([KIT_ROOT], include_common=False), base / "only.otf")
    return {"common": (base / "common.otf", full), "only": (base / "only.otf", only)}


def test_subset_covers_all_used_characters_and_is_much_smaller_than_the_full_font(kit_subsets: dict) -> None:
    out, report = kit_subsets["common"]
    used = font_subset.collect_data_chars([KIT_ROOT])

    cmap = _cmap(out)
    assert all(ord(ch) in cmap for ch in used), "数据里用到的字必须全部在子集字体里"
    source = _cmap(FONT)
    assert all(ord(ch) in cmap for ch in font_subset.common_chars() if ord(ch) in source)
    assert report["missing_count"] == 0, "默认字体应当装得下起步包的全部文案与常用字表"
    assert report["output_bytes"] * 4 <= report["source_bytes"]


def test_data_only_subset_is_tiny_and_still_covers_the_data(kit_subsets: dict) -> None:
    out, report = kit_subsets["only"]
    used = font_subset.collect_data_chars([KIT_ROOT])

    assert set(chr(c) for c in _cmap(out)) >= used
    assert report["output_bytes"] * 20 <= report["source_bytes"]
    assert report["output_bytes"] < kit_subsets["common"][1]["output_bytes"]


@pytest.fixture(scope="module")
def tiny_font(tmp_path_factory: pytest.TempPathFactory) -> Path:
    """现场造的微型字体：只有 A 与"好"两个字形，用来测与字形无关的行为（缺字报告、确定性、命令行），秒级。"""
    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen

    path = tmp_path_factory.mktemp("tiny") / "tiny.ttf"
    order = [".notdef", "A", "uni597D"]
    builder = FontBuilder(1000, isTTF=True)
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap({0x41: "A", 0x597D: "uni597D"})
    pen = TTGlyphPen(None)
    pen.moveTo((0, 0))
    pen.lineTo((0, 500))
    pen.lineTo((500, 500))
    pen.closePath()
    glyph = pen.glyph()
    builder.setupGlyf({name: glyph for name in order})
    builder.setupHorizontalMetrics({name: (600, 0) for name in order})
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable({"familyName": "Tiny", "styleName": "Regular"})
    builder.setupOS2()
    builder.setupPost()
    builder.save(str(path))
    return path


def test_missing_glyphs_are_reported_and_strict_mode_refuses_to_write(tmp_path: Path, tiny_font: Path) -> None:
    root = tmp_path / "game"
    _write_text_table(root, "l10n/l10n.text.json", [("l10n.x", "l10n.locale.zh_cn", "好")])  # U+E000 私用区：字体里没有
    out = tmp_path / "out.ttf"

    rc = font_subset.main(["--font", str(tiny_font), "--data-root", str(root), "--no-common", "--out", str(out),
                           "--report", str(tmp_path / "r.json")])
    assert rc == 1
    report = json.loads((tmp_path / "r.json").read_text(encoding="utf-8"))
    assert report["missing_chars"] == ""
    assert ord("好") in _cmap(out)

    out2 = tmp_path / "strict.ttf"
    assert font_subset.main(["--font", str(tiny_font), "--data-root", str(root), "--no-common", "--strict", "--out", str(out2)]) == 1
    assert not out2.exists()


def test_subset_keeps_only_requested_glyphs(tmp_path: Path, tiny_font: Path) -> None:
    root = tmp_path / "game"
    _write_text_table(root, "l10n/l10n.text.json", [("l10n.x", "l10n.locale.en", "A")])
    out = tmp_path / "a.ttf"
    assert font_subset.main(["--font", str(tiny_font), "--data-root", str(root), "--no-common", "--out", str(out)]) == 0
    assert _cmap(out) == {ord("A")}


def test_output_is_deterministic(tmp_path: Path, tiny_font: Path) -> None:
    root = tmp_path / "game"
    _write_text_table(root, "l10n/l10n.text.json", [("l10n.x", "l10n.locale.zh_cn", "好A")])
    first = tmp_path / "1.ttf"
    second = tmp_path / "2.ttf"
    for out in (first, second):
        assert font_subset.main(["--font", str(tiny_font), "--data-root", str(root), "--no-common", "--out", str(out)]) == 0
    assert first.read_bytes() == second.read_bytes()


def test_extra_chars_are_kept_even_when_not_in_the_data(tmp_path: Path, tiny_font: Path) -> None:
    root = tmp_path / "game"
    _write_text_table(root, "l10n/l10n.text.json", [("l10n.x", "l10n.locale.en", "A")])
    extra = tmp_path / "extra.txt"
    extra.write_text("好\n", encoding="utf-8")
    out = tmp_path / "e.ttf"
    assert font_subset.main(["--font", str(tiny_font), "--data-root", str(root), "--no-common", "--extra-chars-file", str(extra), "--out", str(out)]) == 0
    assert _cmap(out) == {ord("A"), ord("好")}


def test_cli_rejects_missing_inputs(tmp_path: Path, tiny_font: Path) -> None:
    assert font_subset.main(["--font", str(tmp_path / "nope.otf"), "--data-root", str(KIT_ROOT), "--out", str(tmp_path / "o.otf")]) == 2
    assert font_subset.main(["--font", str(tiny_font), "--no-common", "--out", str(tmp_path / "o.otf")]) == 2
    assert font_subset.main(["--font", str(tiny_font), "--data-root", str(tmp_path / "nope"), "--out", str(tmp_path / "o.otf")]) == 2
