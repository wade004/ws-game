"""界面资源契约清单的读取、展开与人读清单（手感设计/08 第 3/5 节、ADR-0149）。

唯一机器可读来源是 ``skin_manifest.json``（本目录）：皮肤包元素（路径模板、必备/可选、尺寸/比例规则、九宫格边框、状态变体、品质档、
透明度规则）、主题令牌（颜色键 / 字体 / 边框像素）、图标类别、纸娃娃层/方向/姿势键/帧数规则。消费方都读它，不各抄一份：

- 导入校验 :mod:`skin_pack` / :mod:`equip_pack`（每条规则对应一个具名诊断）；
- 占位皮肤生成器 ``toolchain/std_equip_set/skin.py``（按元素清单出图）；
- 本模块的人读清单（``import_assets.py skin-checklist``，给美术/出图工具的逐文件清单）；
- 引擎侧完整性用例（Unity 适配器的皮肤替换用例逐元素核对）。

**判断记录**

1. 元素是模板：``{slot}`` / ``{quality}`` 由数据展开（槽位/品质清单取自 ``item.slot_definition`` / ``item.quality_definition``），
   所以"元素数"有两个口径——清单里的模板条数（:func:`element_templates`）与按某份数据展开后的文件数（:func:`expand`）。
2. ``requirement``：``required`` 缺了记告警并写回落目标；``optional`` 缺了只记录回落（不当问题）；``placeholder_only`` 只有框架占位
   皮肤必须有（它是别人回落的终点），自有皮肤包可以不带，带了就按规则校验。
3. ``state_of`` 把一组状态变体挂在一起：必备状态缺失用专门的 ``equip_skin_state_missing``；``needs`` 表示"提供了我就必须同时提供
   ``needs`` 指向的元素"（按钮：给了 hover/pressed 就必须有 normal）。
"""

from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

MANIFEST_FILE = Path(__file__).resolve().parent / "skin_manifest.json"

REQ_REQUIRED = "required"
REQ_OPTIONAL = "optional"
REQ_PLACEHOLDER_ONLY = "placeholder_only"

ALPHA_VISIBLE = "visible"
ALPHA_HAS_TRANSPARENCY = "has_transparency"
ALPHA_TRANSPARENT_CENTER = "transparent_center"


@dataclass(frozen=True)
class SkinElement:
    """清单里的一个元素模板（未展开）。"""

    id: str
    path: str
    requirement: str
    label: str
    expand: Optional[str]
    state: Optional[str]
    state_of: Optional[str]
    needs: Optional[str]
    fallback: Optional[str]
    fallback_state: Optional[str]
    kind: str
    size: Optional[dict]
    alpha: Optional[str]
    nine_slice: Optional[dict]
    consumer: str


@dataclass(frozen=True)
class ExpandedElement:
    """按数据展开后的一个文件：包内相对路径 + 所属元素模板。"""

    path: str
    label: str
    element: SkinElement
    name: Optional[str] = None      # 展开用到的槽位名/品质名

    @property
    def requirement(self) -> str:
        return self.element.requirement


_cache: Optional[dict] = None


def load_manifest(path: Optional[Path] = None) -> dict:
    """读清单（默认缓存；传 path 时不缓存，供用例读改过的副本）。"""
    global _cache
    if path is not None:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    if _cache is None:
        _cache = json.loads(MANIFEST_FILE.read_text(encoding="utf-8"))
    return _cache


def _to_element(raw: dict) -> SkinElement:
    return SkinElement(
        id=raw["id"], path=raw["path"], requirement=raw["requirement"], label=raw.get("label", raw["id"]),
        expand=raw.get("expand"), state=raw.get("state"), state_of=raw.get("state_of"), needs=raw.get("needs"),
        fallback=raw.get("fallback"), fallback_state=raw.get("fallback_state"), kind=raw.get("kind", "image"),
        size=raw.get("size"), alpha=raw.get("alpha"), nine_slice=raw.get("nine_slice"), consumer=raw.get("consumer", ""))


def element_templates(manifest: Optional[dict] = None) -> list[SkinElement]:
    m = manifest or load_manifest()
    return [_to_element(r) for r in m["skin"]["elements"]]


def expand(slots: list[str], qualities: list[str], manifest: Optional[dict] = None) -> list[ExpandedElement]:
    """按槽位/品质清单展开成文件清单（顺序：清单次序，展开项按名升序）。"""
    out: list[ExpandedElement] = []
    for e in element_templates(manifest):
        if e.expand == "slot":
            names = slots
            key = "{slot}"
        elif e.expand == "quality":
            names = qualities
            key = "{quality}"
        else:
            out.append(ExpandedElement(e.path, e.label, e))
            continue
        for n in names:
            out.append(ExpandedElement(e.path.replace(key, n), e.label.replace(key, n), e, n))
    return out


def required_paths(slots: list[str], qualities: list[str], *, placeholder: bool, manifest: Optional[dict] = None) -> list[str]:
    """必须有的文件（自有皮肤包不含 placeholder_only；占位皮肤含）。"""
    return [x.path for x in expand(slots, qualities, manifest)
            if x.requirement == REQ_REQUIRED or (placeholder and x.requirement == REQ_PLACEHOLDER_ONLY)]


def token_spec(manifest: Optional[dict] = None) -> dict:
    return (manifest or load_manifest())["skin"]["tokens"]


def nine_slice_border(element: SkinElement, theme: Optional[dict], manifest: Optional[dict] = None) -> int:
    """九宫格元素的边框像素：theme.json 里 ``border_token`` 的值（正整数），缺省取清单给的默认值。"""
    spec = element.nine_slice or {}
    value = (theme or {}).get(spec.get("border_token", ""))
    if isinstance(value, int) and not isinstance(value, bool) and value > 0:
        return value
    return int(spec.get("default", 0))


def stats(slots: list[str], qualities: list[str], manifest: Optional[dict] = None) -> dict:
    m = manifest or load_manifest()
    templates = element_templates(m)
    files = expand(slots, qualities, m)
    return {
        "templates": len(templates),
        "files": len(files),
        "required": sum(1 for f in files if f.requirement == REQ_REQUIRED),
        "optional": sum(1 for f in files if f.requirement == REQ_OPTIONAL),
        "placeholder_only": sum(1 for f in files if f.requirement == REQ_PLACEHOLDER_ONLY),
        "color_tokens": len(m["skin"]["tokens"]["colors"]),
        "icon_categories": len(m["icons"]["categories"]),
    }


# ---------------------------------------------------------------------------
# 人读清单
# ---------------------------------------------------------------------------

_REQ_TEXT = {REQ_REQUIRED: "必备", REQ_OPTIONAL: "可选", REQ_PLACEHOLDER_ONLY: "占位皮肤必备"}
_ALPHA_TEXT = {ALPHA_VISIBLE: "非全透明", ALPHA_HAS_TRANSPARENCY: "须有透明像素（不是实心方块）",
               ALPHA_TRANSPARENT_CENTER: "须有透明像素且中心区域全透明"}


def _size_text(size: Optional[dict]) -> str:
    if not size:
        return "-"
    parts = []
    if size.get("aspect") == "1:1":
        parts.append("正方形")
    parts.append(f"边长 {size['min']}～{size['max']} px")
    if size.get("group"):
        parts.append(f"同组 {size['group']} 须同尺寸")
    return "，".join(parts)


def render_checklist(slots: list[str], qualities: list[str], manifest: Optional[dict] = None) -> str:
    """出图工具/美术用的逐文件清单（Markdown）：路径、必备/可选、尺寸与比例、九宫格、状态、透明度、令牌、图标、纸娃娃规则。"""
    m = manifest or load_manifest()
    skin = m["skin"]
    st = stats(slots, qualities, m)
    lines = [
        f"# 界面皮肤包资源清单（清单版本 {m['manifest_version']}）", "",
        f"皮肤包目录：`assets/<数据集>/{skin['root']}/<名>/`（`ui_layout_definition.skin_ref = skin.<名>`）。",
        f"本数据展开：槽位 {len(slots)} 个（{', '.join(slots) or '无'}），品质 {len(qualities)} 个（{', '.join(qualities) or '无'}）；"
        f"模板 {st['templates']} 条，展开 {st['files']} 个文件（必备 {st['required']}、可选 {st['optional']}、占位皮肤必备 {st['placeholder_only']}）。", "",
        "## 皮肤包文件", "",
        "| 文件 | 要求 | 尺寸/比例 | 九宫格 | 状态 | 透明度 | 用途 |",
        "|---|---|---|---|---|---|---|",
    ]
    for x in expand(slots, qualities, m):
        e = x.element
        nine = "-"
        if e.nine_slice:
            nine = f"边框取 theme.{e.nine_slice['border_token']}（缺省 {e.nine_slice['default']} px）"
        state = e.state or "-"
        if e.state_of:
            state += f"（{e.state_of} 组）"
        if e.needs:
            state += f"，须同时有 {e.needs}"
        alpha = _ALPHA_TEXT.get(e.alpha or "", "-")
        lines.append(f"| `{x.path}` | {_REQ_TEXT[x.requirement]} | {_size_text(e.size)} | {nine} | {state} | {alpha} | {e.consumer} |")
    lines += ["", "## 主题令牌（theme.json）", "", "| 键 | 要求 | 说明 |", "|---|---|---|"]
    for c in skin["tokens"]["colors"]:
        lines.append(f"| `colors.{c['key']}` | {_REQ_TEXT[c['requirement']]} | {c['use']}（{skin['tokens']['color_format']}） |")
    f = skin["tokens"]["font"]
    lines.append(f"| `{f['key']}` | {_REQ_TEXT[f['requirement']]} | {f['use']} |")
    for n in skin["tokens"]["numbers"]:
        lines.append(f"| `{n['key']}` | {_REQ_TEXT[n['requirement']]} | {n['use']}（正整数，缺省 {n['default']}） |")
    ic = m["icons"]
    lines += ["", "## 图标", "", f"路径 `{ic['path']}`；{ic['path_note']}。",
              f"尺寸：正方形，边长 {ic['size']['min']}～{ic['size']['max']} 内 2 的幂；{ic['margin']}。",
              f"覆盖率：不透明像素（alpha >= {ic['coverage']['alpha_cutoff']}）占比不低于 {ic['coverage']['min_opaque_ratio']}（否则警告 equip_opaque_coverage_low）。", ""]
    for c in ic["categories"]:
        lines.append(f"- `{c['name']}`（{_REQ_TEXT[c['requirement']]}）：{c['use']}")
    pd = m["paperdoll"]
    lines += ["", "## 纸娃娃图层", "",
              f"- 静态层图：`{pd['layer_static']}`（每个方向档一张，须有透明像素）",
              f"- 逐层剪辑：`{pd['layer_clip']}`，不命中退一级 `{pd['layer_clip_nodir']}`",
              f"- 身体剪辑（帧数基准）：`{pd['body_clip']}`",
              f"- 静态层覆盖率：不透明像素占画布比例不低于 {pd['static_layer_coverage']['min_opaque_ratio']}（否则警告 equip_opaque_coverage_low）",
              f"- 逐方向层序：{pd['layer_order']}", f"- 层锚点：{pd['layer_anchor']}",
              f"- 方向档：{pd['directions']}", f"- 姿势键：{pd['pose_keys']}"]
    for r in pd["frame_rules"]:
        lines.append(f"- 帧规则：{r}")
    lines += ["", "## 导入校验的具名诊断", ""]
    for d in m["diagnostics"]:
        lines.append(f"- `{d}`")
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------
# 命令行：import_assets.py skin-checklist
# ---------------------------------------------------------------------------

def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--data-root", action="append", default=None, metavar="DIR",
                        help="数据根，可重复；默认 data/_framework + data/_feel + data/_equip")
    parser.add_argument("--out", default=None, help="清单输出文件（Markdown）；缺省打印到 stdout")
    parser.add_argument("--json", action="store_true", help="输出统计 JSON（元素数）而不是 Markdown")


def run(args: argparse.Namespace) -> int:
    import sys

    from . import skin_pack
    from .common import find_repo_root
    from .equip_pack import load_tables

    repo_root = find_repo_root()
    roots = [Path(p) if Path(p).is_absolute() else repo_root / p
             for p in (args.data_root or ("data/_framework", "data/_feel", "data/_equip"))]
    tables = load_tables(roots)
    slots, qualities = skin_pack.slot_names(tables), skin_pack.quality_names(tables)
    if args.json:
        text = json.dumps(stats(slots, qualities), ensure_ascii=False) + "\n"
    else:
        text = render_checklist(slots, qualities)
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8", newline="\n")
    else:
        sys.stdout.write(text)
    return 0
