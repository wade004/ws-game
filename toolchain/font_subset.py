#!/usr/bin/env python3
"""字体子集工具（P4 内容起步包配套，ADR-0162）：把默认中文字体按"游戏实际用到的字"裁成小字体，发版时替换整套字体以缩小体积。

默认字体（``assets/_placeholder/fonts/noto_sans_cjk_sc.otf``，约 15.7 MiB）覆盖整套简体中文，对一个文案只有几千字的游戏来说绝大部分是
用不到的字形。本工具扫描**所有数据根**里的本地化文本（``l10n.text`` 表的 ``text`` 字段，起步包根与游戏根都可以传），
加上"常用字表 + 基本符号"（避免玩家改名、存档名、之后补文案时出现缺字），输出只含这些字的子集字体。

字符集由三部分合并（顺序无关，去重）：

1. **数据文本字符**：每个 ``--data-root`` 下所有 ``l10n.text`` 表行的 ``text``（多语言、多文件都扫；``{占位符}`` 里的 ASCII 字母同样保留）。
2. **常用字表**（默认带，``--no-common`` 关闭）：GB2312 一级字表 3755 个汉字（由标准库 ``gb2312`` 编码表按区位直接生成，不需要附带数据文件）
   + ASCII 可打印字符 + 中文标点与全角符号。需要更小的体积时关掉它，只留数据里出现的字（代价：运行期出现数据里没有的字会缺字）。
3. **额外字符**：``--extra-chars`` 直接给字符串、``--extra-chars-file`` 给 UTF-8 文本文件。

退出码：0 成功；1 子集已生成但数据里有源字体没有的字（缺字清单打印出来，``--strict`` 时直接失败不写文件）；2 参数/输入错误。

用法::

    python toolchain/font_subset.py --data-root data/_starter_kit --data-root games/<game>/data/game --out build/ui_font.otf
    python toolchain/font_subset.py --data-root data/_starter_kit --no-common --report build/ui_font.report.json --out build/ui_font.otf

判断记录：
* 只依赖 ``fontTools``（工具链已安装，不新增依赖）。字体文件保持 OTF/CFF 原格式（``flavor`` 不变），引擎侧按原资源 id 继续引用，不改布局数据。
* 字体名表（name table）与 OFL 要求的版权/许可条目原样保留（``name_IDs=['*']``、``notdef_outline=True``）——子集化后仍是同一字体的衍生版本，
  分发时必须继续附带 ``LICENSE-OFL.txt``。字体的 Reserved Font Name 约束：Noto 无保留字体名，衍生版可沿用名称。
* 字形选择不改字距/连字特性（``layout_features=['*']``），子集后体积主要由字形轮廓决定；常用字表默认带上，体积大约是整套字体的十分之一到五分之一
  （实测数字见 ``toolchain/README.md`` 该工具一节）。
* 输出稳定：相同输入产生字节相同的文件（fontTools 子集化本身是确定性的；字符集排序后传入）。
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# 允许直接以 "python toolchain/font_subset.py" 方式运行；控制台输出统一走 toolchain/_console.py 的 UTF-8 入口（惯例同 validate_data.py）。
sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_FONT = REPO_ROOT / "assets" / "_placeholder" / "fonts" / "noto_sans_cjk_sc.otf"

L10N_TEXT_TABLE = "l10n.text"

# 中文标点与常见全角符号（U+3000 区、全角 ASCII 变体里的常用项、通用标点里的引号/省略号/破折号）。
_EXTRA_SYMBOLS = (
    "　、。〃〈〉《》「」『』【】〔〕〖〗"
    "—‘’“”…·×→←↑↓■□▲△●○★☆"
    "！（），．：；？～￥"
)


def common_chars() -> str:
    """常用字表：GB2312 一级汉字 3755 个 + ASCII 可打印 + 中文标点符号。"""
    chars = []
    # GB2312 一级字：区码 16..55（0xB0..0xD7），位码 1..94（0xA1..0xFE）；55 区只到 89 位（0xD7F9）。
    for hi in range(0xB0, 0xD8):
        for lo in range(0xA1, 0xFF):
            if hi == 0xD7 and lo > 0xF9:
                break
            try:
                chars.append(bytes([hi, lo]).decode("gb2312"))
            except UnicodeDecodeError:
                continue
    chars.extend(chr(c) for c in range(0x20, 0x7F))
    chars.extend(_EXTRA_SYMBOLS)
    return "".join(chars)


def _iter_text_values(data_root: Path):
    """遍历数据根下全部 l10n.text 表行的 text 字段（不依赖文件名，按 JSON 里的 ``table`` 判定）。"""
    for path in sorted(data_root.rglob("*.json")):
        try:
            doc = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        if not isinstance(doc, dict) or doc.get("table") != L10N_TEXT_TABLE:
            continue
        for row in doc.get("rows", []):
            if isinstance(row, dict) and isinstance(row.get("text"), str):
                yield row["text"]


def collect_data_chars(data_roots: list[Path]) -> set[str]:
    """扫描全部数据根的本地化文本，返回出现过的字符集合（去掉控制字符）。"""
    used: set[str] = set()
    for root in data_roots:
        if not root.is_dir():
            raise FileNotFoundError(f"数据根不存在：{root}")
        for text in _iter_text_values(root):
            used.update(ch for ch in text if ch >= " " and ch != "\x7f")
    return used


def font_cmap(font_path: Path) -> set[int]:
    from fontTools.ttLib import TTFont

    with TTFont(str(font_path), lazy=True) as font:
        return set(font.getBestCmap().keys())


def build_charset(data_roots: list[Path], *, include_common: bool = True, extra_chars: str = "", extra_chars_file: Path | None = None) -> set[str]:
    charset = collect_data_chars(data_roots)
    if include_common:
        charset.update(common_chars())
    charset.update(extra_chars)
    if extra_chars_file is not None:
        charset.update(extra_chars_file.read_text(encoding="utf-8").replace("\r", "").replace("\n", ""))
    return charset


def subset_font(font_path: Path, charset: set[str], out_path: Path) -> dict:
    """按字符集裁剪字体并写出；返回报告（源字体里没有的字符单列 ``missing``，不阻断）。"""
    from fontTools import subset

    available = font_cmap(font_path)
    present = sorted(ch for ch in charset if ord(ch) in available)
    missing = sorted(ch for ch in charset if ord(ch) not in available)

    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.notdef_outline = True
    options.glyph_names = False
    options.legacy_kern = True
    options.hinting = True
    options.recalc_bounds = True
    options.recalc_timestamp = False
    options.drop_tables += ["DSIG"]

    font = subset.load_font(str(font_path), options)
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=[ord(ch) for ch in present])
    subsetter.subset(font)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    subset.save_font(font, str(out_path), options)
    font.close()

    return {
        "source_font": str(font_path),
        "output_font": str(out_path),
        "source_bytes": font_path.stat().st_size,
        "output_bytes": out_path.stat().st_size,
        "charset_size": len(charset),
        "glyph_chars": len(present),
        "missing_chars": "".join(missing),
        "missing_count": len(missing),
    }


def main(argv: list[str] | None = None) -> int:
    ensure_utf8_stdio()
    parser = argparse.ArgumentParser(description="按数据里用到的字（加常用字表）裁剪字体，缩小发版体积。")
    parser.add_argument("--font", type=Path, default=DEFAULT_FONT, help="源字体（默认框架占位字体 noto_sans_cjk_sc.otf）")
    parser.add_argument("--data-root", type=Path, action="append", default=[], help="数据根，可重复；扫描其下所有 l10n.text 表")
    parser.add_argument("--out", type=Path, required=True, help="输出子集字体路径")
    parser.add_argument("--no-common", action="store_true", help="不带常用字表，只保留数据里出现的字符")
    parser.add_argument("--extra-chars", default="", help="额外保留的字符（字符串）")
    parser.add_argument("--extra-chars-file", type=Path, default=None, help="额外保留的字符（UTF-8 文本文件）")
    parser.add_argument("--report", type=Path, default=None, help="可选：把报告写成 JSON")
    parser.add_argument("--strict", action="store_true", help="数据里有源字体没有的字时失败（不写输出文件）")
    args = parser.parse_args(argv)

    if not args.data_root and args.no_common and not args.extra_chars and args.extra_chars_file is None:
        print("[error] 没有任何字符来源：至少给一个 --data-root、常用字表（默认）或额外字符", file=sys.stderr)
        return 2
    if not args.font.is_file():
        print(f"[error] 源字体不存在：{args.font}", file=sys.stderr)
        return 2
    try:
        charset = build_charset(args.data_root, include_common=not args.no_common, extra_chars=args.extra_chars,
                                extra_chars_file=args.extra_chars_file)
    except FileNotFoundError as exc:
        print(f"[error] {exc}", file=sys.stderr)
        return 2

    available = font_cmap(args.font)
    missing_now = sorted(ch for ch in charset if ord(ch) not in available)
    if args.strict and missing_now:
        print(f"[error] 源字体缺 {len(missing_now)} 个字：{''.join(missing_now)}", file=sys.stderr)
        return 1

    report = subset_font(args.font, charset, args.out)
    print(f"[font_subset] 字符集 {report['charset_size']}（其中源字体有字形 {report['glyph_chars']}），"
          f"{report['source_bytes']:,} -> {report['output_bytes']:,} 字节（{report['output_bytes'] / report['source_bytes']:.1%}）")
    if args.report is not None:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    if report["missing_count"]:
        print(f"[warning] 源字体缺 {report['missing_count']} 个字（没有写进子集）：{report['missing_chars']}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
