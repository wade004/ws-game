"""扫描仓库内 Markdown 文档的相对文件链接，核对目标文件确实存在。

背景（PJ130 交付侧审计，2026-09-08，见
``architecture/落地计划/audit-5c444f1-20260908/AUDIT_REPORT.md`` "文档漂移与需更新项"第 6/7
条）：`architecture/落地计划/落地方案与分阶段计划.md` 与四个模块 `schema/README.md`/
`save_slot_meta.md` 曾各自出现相对路径层级数错误（如 `adr/0017-...` 缺一层 `../`、
`../../../architecture` 少算一层目录），链接在 GitHub 网页或本地编辑器点击后 404。这类问题此前
没有任何自动化门禁覆盖，只能靠人工审计逐条发现——本模块把"相对文件链接必须能解析到一个真实存在
的文件/目录"做成可重复运行的 pytest 用例，跟随 `check.ps1` 的 "toolchain 自身 pytest 套件"步骤
一并跑（该步骤本来就执行 ``python -m pytest toolchain/tests``，不需要改 check.ps1）。

范围与忽略规则：
  - 只扫描 ``git ls-files`` 报告的、当前由 git 跟踪的 ``*.md`` 文件（与
    ``AUDIT_REPORT.md`` "由 git ls-files 重新统计"口径一致，天然排除 node_modules/bin/obj/
    .venv/dist 等 .gitignore 覆盖的目录，不需要重复维护一份排除名单）。
  - 排除路径中任意目录段精确匹配 ``audit-*`` 的文件（各轮审计报告快照目录，如
    ``architecture/落地计划/audit-5c444f1-20260908/``——这些文件本身引用的是审计归档路径
    或外部快照路径，不代表仓库当前应有的文件布局，不属于本检查覆盖范围，见任务口径"审计目录
    audit-* 排除"）。
  - 只校验"相对文件链接"，忽略：纯锚点（``#foo``）、外部链接（``http(s)://``、``mailto:``、
    ``ftp://`` 等带 scheme 的链接）、站点绝对路径（以 ``/`` 开头）、Windows 绝对路径（形如
    ``C:\\...`` 或 ``C:/...``）。链接自带的 ``#锚点`` 部分与可选的 ``"title"``/``'title'``
    后缀会被剥离后再做文件存在性判断，不校验锚点本身指向的标题是否真实存在（"忽略锚点"——只验证
    文件路径存在，不验证锚点有效性，与 AUDIT_REPORT.md 第 216 条判断记录口径一致）。

运行：

```
python -m pytest toolchain/tests/test_markdown_relative_links.py -q
```
或作为 ``toolchain`` 套件的一部分：``python -m pytest toolchain/tests -q``（`check.ps1` 已在跑）。
"""

from __future__ import annotations

import re
import subprocess
import sys
import urllib.parse
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent

# 目录段精确匹配 "audit-<任意内容>" 时排除（不是子串匹配——"文档代码一致性审计_2026-09-05.md"
# 这类文件名含"审计"但不是目录段 "audit-*"，不受本规则排除，仍在扫描范围内）。
_AUDIT_DIR_SEGMENT = re.compile(r"^audit-.*$")

# markdown 行内链接：[text](target)、[text](target "title")、[text](target 'title')。
# target 允许包含空格前需要有引号包裹的 title，本正则先贪心捕获括号内全部内容，再在 Python 侧
# 拆出 target 与可选 title（比在正则里精确处理引号嵌套更稳妥、可读）。
_MD_LINK_RE = re.compile(r"\[([^\]]*)\]\(([^)]+)\)")

# 参考式链接定义：[label]: target "title"（本仓库文档基本不用这种写法，仍一并覆盖，避免漏检）。
_MD_REF_DEF_RE = re.compile(r"^\s{0,3}\[[^\]]+\]:\s*(\S+)(?:\s+.*)?$", re.MULTILINE)

_EXTERNAL_SCHEME_RE = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.\-]*:(?://|[^/\\])")
_WINDOWS_ABS_RE = re.compile(r"^[a-zA-Z]:[\\/]")


def _list_tracked_markdown_files() -> list[Path]:
    """用 ``git ls-files`` 枚举当前由 git 跟踪的 ``*.md`` 文件（绝对路径列表）。

    判断记录：不用 ``Path.rglob``——rglob 会扫到 .gitignore 覆盖的目录（node_modules、
    bin/obj、.venv、dist 等），需要额外维护一份排除名单且容易漏项；``git ls-files`` 直接给出
    "当前仓库实际认可的文件"这份权威清单，与 AUDIT_REPORT.md 自己统计 182 篇 Markdown 用的
    方法一致。
    """
    # 判断记录：不加 -c core.quotepath=false 时，git 对非 ASCII 文件名默认按 C 转义整体加双引号
    # 输出（例如中文路径变成 `"architecture/00_\346\236\266..."` 这种八进制转义字符串），会把
    # 中文路径解析成一个带反斜杠转义的错误字符串，用 Path() 直接包出的对象打不开文件（Windows
    # 下报 "Invalid argument"，本函数早期版本实测复现）。显式关闭这个 quotepath 行为，让 git
    # 直接输出原始 UTF-8 字节的路径文本，不加引号也不转义。
    result = subprocess.run(
        ["git", "-c", "core.quotepath=false", "ls-files", "*.md"],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=True,
    )
    files = []
    for line in result.stdout.splitlines():
        line = line.strip()
        if not line:
            continue
        parts = Path(line).parts
        if any(_AUDIT_DIR_SEGMENT.match(seg) for seg in parts[:-1]):
            continue
        files.append((REPO_ROOT / line).resolve())
    return sorted(files)


def _strip_title(target: str) -> str:
    """从 ``target`` 里剥离可选的 ``"title"``/``'title'`` 后缀，只留路径部分。"""
    target = target.strip()
    # 形如: path "title" 或 path 'title'
    m = re.match(r"""^(\S+)\s+(?:"[^"]*"|'[^']*')\s*$""", target)
    if m:
        return m.group(1)
    return target


def _is_external_or_absolute(target: str) -> bool:
    if target == "":
        return True
    if target.startswith("#"):
        return True
    if target.startswith("//"):
        # 协议相对 URL（如 //example.com/x），按外部链接处理
        return True
    if target.startswith("/"):
        # 站点绝对路径，忽略（任务口径"忽略...绝对路径"）
        return True
    if _WINDOWS_ABS_RE.match(target):
        return True
    if _EXTERNAL_SCHEME_RE.match(target):
        return True
    return False


def _extract_link_targets(markdown_text: str) -> list[str]:
    targets: list[str] = []
    for _label, raw_target in _MD_LINK_RE.findall(markdown_text):
        targets.append(_strip_title(raw_target))
    for raw_target in _MD_REF_DEF_RE.findall(markdown_text):
        targets.append(raw_target)
    return targets


def _resolve_relative_target(md_file: Path, target: str) -> tuple[str, Path] | None:
    """把 ``target``（已剥离 title）拆成"路径部分"与锚点，解析为绝对路径。

    返回 ``None`` 表示这个 target 不需要做文件存在性校验（外部链接/绝对路径/纯锚点）；
    否则返回 ``(path_part_original, resolved_absolute_path)``。
    """
    if _is_external_or_absolute(target):
        return None

    path_part = target.split("#", 1)[0]
    # 去掉可能出现的查询串（本仓库文档几乎不用，防御性处理）
    path_part = path_part.split("?", 1)[0]
    if path_part == "":
        # 只有锚点或查询串，指向同一文件，天然存在
        return None

    # markdown 链接里的路径可能带 URL 百分号编码（例如空格 -> %20）
    decoded = urllib.parse.unquote(path_part)
    resolved = (md_file.parent / decoded).resolve()
    return path_part, resolved


def _is_gitignored(path: Path) -> bool:
    """判断 ``path``（不要求实际存在）是否落在 ``.gitignore`` 规则覆盖的路径下。

    判断记录：`architecture/落地计划/文档代码深度审核_2026-09-07_main.md` 等历史审计运行记录
    会用相对链接指回当次门禁产生的原始日志（如 `../../bin/audit-20260907/check.log`）——
    `bin/` 整体 `.gitignore`（构建产物目录），这些日志文件只在跑那次审计的本机短暂存在，从不
    随仓库提交，属于"引用一次性证据文件、按设计就不会出现在干净检出里"，与本检查要抓的"相对路径
    层级算错导致链接目标本就该存在却 404"是两类不同的问题（前者不是文档缺陷）。用
    ``git check-ignore --no-index`` 判定，不用手写一份 gitignore 规则子集去重复维护——它对
    不存在的路径同样有效（按路径文本匹配规则，不要求文件先存在）。
    """
    result = subprocess.run(
        ["git", "check-ignore", "--no-index", "-q", str(path)],
        cwd=REPO_ROOT,
        capture_output=True,
    )
    return result.returncode == 0


def _collect_broken_links() -> list[str]:
    problems: list[str] = []
    md_files = _list_tracked_markdown_files()
    assert len(md_files) > 50, (
        f"只发现 {len(md_files)} 篇非审计 Markdown，明显少于预期（审计基线约 150 篇左右）——"
        "先检查 git ls-files/exclude 规则是否误伤，而不是继续跑存在性校验。"
    )

    for md_file in md_files:
        try:
            text = md_file.read_text(encoding="utf-8")
        except UnicodeDecodeError as exc:  # pragma: no cover - 仓库内文档均为 UTF-8
            problems.append(f"{md_file}: 读取失败（非 UTF-8？）：{exc}")
            continue

        for target in _extract_link_targets(text):
            resolved = _resolve_relative_target(md_file, target)
            if resolved is None:
                continue
            path_part, resolved_path = resolved
            if resolved_path.exists():
                continue
            if _is_gitignored(resolved_path):
                # 指向 .gitignore 覆盖路径（构建产物/一次性日志等）：按设计不随仓库提交，不算
                # 文档缺陷，跳过。
                continue
            rel_md = md_file.relative_to(REPO_ROOT)
            problems.append(
                f"{rel_md}: 链接目标 '{path_part}' 解析为 '{resolved_path}' 不存在"
            )
    return problems


def test_no_broken_relative_links_in_tracked_markdown() -> None:
    problems = _collect_broken_links()
    if problems:
        detail = "\n  ".join(problems)
        pytest.fail(
            f"发现 {len(problems)} 处失效相对链接（排除 audit-* 目录、外部链接、锚点、"
            f"绝对路径）：\n  {detail}"
        )


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-v"]))
