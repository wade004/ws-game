"""``editor/docs/编辑器产品文档.md`` 与其手工同步的 ``.html`` 版本之间的一致性门禁。

背景（T-N5-5，阶段 N5 文档收尾）：编辑器产品文档没有自动转换脚本（见 DOC-118-03 勘误），HTML 版
一直靠人工同步 md 版的改动；T-N5-4 落地时如实记录过"HTML 版只同步到 md v2.6，第 4.1 节契约面
清单表落后 md 版九次更新"这一缺口，T-N5-5 已把这一缺口手工回填到与 md v2.16 完全对应。本模块把
"两版文档不得再次走漂"做成可重复运行的 pytest 用例，跟随 `check.ps1` 的 toolchain pytest 套件
一并跑，不需要改 check.ps1：

  - 第 4.1 节"契约面清单"表：逐行比对两版表格第一列（契约面名称）的集合，行数与每行标识必须
    一致（不比对其余两列的完整正文——契约面名称已足以唯一定位一行，逐字比对全部正文对人工同步
    过于苛刻、容易把措辞润色误判为漂移）。
  - "变更记录"表：逐行比对两版表格第二列（版本号，如 ``v2.16``）的集合，行数与每个版本号必须
    一致。
  - 文档顶部"版本：vX.Y"字符串：两版必须完全一致——这是最容易漏改的一处（只改了表格正文却忘了
    改头部版本号，或反过来），单独断言。

只用标准库 ``html.parser`` 解析 HTML（无第三方依赖，如 BeautifulSoup/lxml），与仓库"最小依赖"
惯例一致。

运行：

```
python -m pytest toolchain/tests/test_editor_doc_consistency.py -q
```
或作为 ``toolchain`` 套件的一部分：``python -m pytest toolchain/tests -q``（``check.ps1`` 已在跑）。
"""

from __future__ import annotations

import re
import sys
from html.parser import HTMLParser
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent
DOCS_DIR = REPO_ROOT / "editor" / "docs"
MD_FILE = DOCS_DIR / "编辑器产品文档.md"
HTML_FILE = DOCS_DIR / "编辑器产品文档.html"


# ---------------------------------------------------------------------------
# Markdown 一侧：表格提取 + 行内标记剥离
# ---------------------------------------------------------------------------

_MD_TABLE_SEP_RE = re.compile(r"^\|[\s\-:|]+\|$")
_MD_LINK_RE = re.compile(r"\[([^\]]*)\]\([^)]*\)")
_MD_BOLD_RE = re.compile(r"\*\*([^*]+)\*\*")
_MD_CODE_RE = re.compile(r"`([^`]+)`")


def _strip_md_inline(text: str) -> str:
    """剥离行内 markdown 标记（链接/粗体/行内代码），只留可读文本，供与 HTML 提取结果比对。"""
    text = _MD_LINK_RE.sub(r"\1", text)
    text = _MD_BOLD_RE.sub(r"\1", text)
    text = _MD_CODE_RE.sub(r"\1", text)
    return text.strip()


def _find_md_table_rows(lines: list[str], heading_re: re.Pattern[str]) -> list[list[str]]:
    """定位 ``heading_re`` 匹配的标题行之后第一张 markdown 表格，返回数据行（跳过表头与分隔行）。

    每行返回值是该行各单元格剥离 markdown 标记后的文本列表。
    """
    start = None
    for i, line in enumerate(lines):
        if heading_re.match(line):
            start = i
            break
    if start is None:
        raise AssertionError(f"md 文档中未找到标题：{heading_re.pattern}")

    i = start
    while i < len(lines) and not lines[i].startswith("|"):
        i += 1
    if i >= len(lines):
        raise AssertionError(f"标题 {heading_re.pattern!r} 之后未找到表格")

    rows: list[list[str]] = []
    seen_header = False
    while i < len(lines) and lines[i].startswith("|"):
        line = lines[i].rstrip("\n")
        if _MD_TABLE_SEP_RE.match(line):
            seen_header = True
            i += 1
            continue
        cells = [c.strip() for c in line.split("|")[1:-1]]
        if seen_header:
            rows.append([_strip_md_inline(c) for c in cells])
        i += 1
    return rows


def _md_doc_version() -> str:
    text = MD_FILE.read_text(encoding="utf-8")
    m = re.search(r"^-\s*版本：(\S+)\s*$", text, re.MULTILINE)
    assert m, "md 文档头部未找到 '- 版本：vX.Y' 一行"
    return m.group(1)


def _md_table41_identifiers() -> list[str]:
    lines = MD_FILE.read_text(encoding="utf-8").splitlines()
    rows = _find_md_table_rows(lines, re.compile(r"^### 4\.1 契约面清单"))
    return [row[0] for row in rows]


def _md_changelog_identifiers() -> list[str]:
    lines = MD_FILE.read_text(encoding="utf-8").splitlines()
    rows = _find_md_table_rows(lines, re.compile(r"^## 变更记录"))
    return [row[1] for row in rows]  # 第二列：版本号


# ---------------------------------------------------------------------------
# HTML 一侧：用 html.parser 提取标题之后第一张表格
# ---------------------------------------------------------------------------


class _TableAfterMarkerExtractor(HTMLParser):
    """扫描整份 HTML，找到 id 等于 ``marker_id`` 的标签之后第一张 ``<table>``，提取其 ``<tbody>``
    行的各单元格纯文本（``<td>``/``<th>``）。"""

    def __init__(self, marker_id: str):
        super().__init__(convert_charrefs=True)
        self.marker_id = marker_id
        self._seen_marker = False
        self._in_table = False
        self._table_depth = 0
        self._in_row = False
        self._in_cell = False
        self._cell_buf: list[str] = []
        self._row_buf: list[str] = []
        self.rows: list[list[str]] = []
        self._done = False

    def handle_starttag(self, tag, attrs):
        if self._done:
            return
        attrs_dict = dict(attrs)
        if not self._seen_marker:
            if attrs_dict.get("id") == self.marker_id:
                self._seen_marker = True
            return
        if not self._in_table:
            if tag == "table":
                self._in_table = True
                self._table_depth = 1
            return
        if tag == "table":
            self._table_depth += 1
        elif tag == "tr":
            self._in_row = True
            self._row_buf = []
        elif tag in ("td", "th"):
            self._in_cell = True
            self._cell_buf = []

    def handle_endtag(self, tag):
        if self._done or not self._seen_marker or not self._in_table:
            return
        if tag in ("td", "th") and self._in_cell:
            self._in_cell = False
            self._row_buf.append("".join(self._cell_buf).strip())
        elif tag == "tr" and self._in_row:
            self._in_row = False
            self.rows.append(self._row_buf)
        elif tag == "table":
            self._table_depth -= 1
            if self._table_depth == 0:
                self._in_table = False
                self._done = True

    def handle_data(self, data):
        if self._in_cell:
            self._cell_buf.append(data)


def _html_table_rows(marker_id: str) -> list[list[str]]:
    html_text = HTML_FILE.read_text(encoding="utf-8")
    parser = _TableAfterMarkerExtractor(marker_id)
    parser.feed(html_text)
    assert parser._seen_marker, f"HTML 文档中未找到 id='{marker_id}' 的标签"
    assert parser.rows, f"id='{marker_id}' 之后未提取到任何表格行"
    # 第一行是表头（<th>），其余是数据行（<td>）；剔除表头行。
    return parser.rows[1:]


def _html_doc_version() -> str:
    html_text = HTML_FILE.read_text(encoding="utf-8")
    m = re.search(r"版本：(\S+?)</li>", html_text)
    assert m, "HTML 文档头部未找到 '版本：vX.Y</li>' 一行"
    return m.group(1)


def _html_table41_identifiers() -> list[str]:
    return [row[0] for row in _html_table_rows("s4-1")]


def _html_changelog_identifiers() -> list[str]:
    return [row[1] for row in _html_table_rows("changelog")]


# ---------------------------------------------------------------------------
# 测试用例
# ---------------------------------------------------------------------------


def test_doc_version_matches_between_md_and_html() -> None:
    md_version = _md_doc_version()
    html_version = _html_doc_version()
    assert md_version == html_version, (
        f"文档头部版本号不一致：md={md_version!r} html={html_version!r}——"
        "两版文档同步时漏改了其中一处版本号。"
    )


def test_section_4_1_contract_table_matches_between_md_and_html() -> None:
    md_ids = _md_table41_identifiers()
    html_ids = _html_table41_identifiers()
    md_set, html_set = set(md_ids), set(html_ids)
    assert md_set == html_set, (
        "第 4.1 节'契约面清单'表两版行集合不一致——"
        f"md 独有：{sorted(md_set - html_set)}；html 独有：{sorted(html_set - md_set)}。"
        "HTML 版落后 md 版时需要按 md 当前内容回填对应行。"
    )
    assert len(md_ids) == len(html_ids) == len(md_set), (
        f"第 4.1 节表行数或重复行异常：md {len(md_ids)} 行、html {len(html_ids)} 行、"
        f"去重后 {len(md_set)} 行——每个契约面应恰好出现一次。"
    )


def test_changelog_table_matches_between_md_and_html() -> None:
    md_versions = _md_changelog_identifiers()
    html_versions = _html_changelog_identifiers()
    md_set, html_set = set(md_versions), set(html_versions)
    assert md_set == html_set, (
        "'变更记录'表两版版本号集合不一致——"
        f"md 独有：{sorted(md_set - html_set)}；html 独有：{sorted(html_set - md_set)}。"
        "新发布一个 md 版本号时需要同步在 HTML 变更记录表补一行。"
    )
    assert len(md_versions) == len(html_versions) == len(md_set), (
        f"变更记录表行数或重复版本号异常：md {len(md_versions)} 行、html {len(html_versions)} 行、"
        f"去重后 {len(md_set)} 行——每个版本号应恰好出现一次。"
    )


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-v"]))
