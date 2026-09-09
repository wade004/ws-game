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
  - **不再**排除 ``audit-*`` 归档目录（2026-09-09 codex 第十二轮 P3 归档项修正：此前"整段排除
    audit-* 目录"曾漏掉 ``audit-e070e3f-20260908/`` 下 9 处指向缺失证据文件的失效链接——
    这些审计归档本身就是要长期留存的证据材料，不是"外部快照路径"，理应受本检查覆盖。旧版排除
    规则的历史动机（各轮审计报告可能引用一次性构建产物路径）改由下面两条机制分别兜底：
    仍在 .gitignore 覆盖范围内的一次性证据用 ``_is_gitignored`` 跳过；证据原件确认已经
    不可找回、只能在正文标注"原件未归档"的少数链接，显式登记进
    ``toolchain/tests/.linkcheck-ignore`` 白名单）。
  - 只校验"相对文件链接"，忽略：纯锚点（``#foo``）、外部链接（``http(s)://``、``mailto:``、
    ``ftp://`` 等带 scheme 的链接）、站点绝对路径（以 ``/`` 开头）、Windows 绝对路径（形如
    ``C:\\...`` 或 ``C:/...``）。链接自带的 ``#锚点`` 部分与可选的 ``"title"``/``'title'``
    后缀会被剥离后再做文件存在性判断，不校验锚点本身指向的标题是否真实存在（"忽略锚点"——只验证
    文件路径存在，不验证锚点有效性，与 AUDIT_REPORT.md 第 216 条判断记录口径一致）。
  - 指向仍被 ``.gitignore`` 覆盖路径（构建产物、一次性运行日志）的链接照旧跳过，见
    ``_is_gitignored`` 的判断记录。
  - 白名单 ``toolchain/tests/.linkcheck-ignore``：每行一条 ``<md 文件仓库相对路径>::<链接原始
    target 文本>``（与 markdown 源文件中 ``[text](target)`` 的 target 逐字符一致，剥离 title
    前），``#`` 开头或空行忽略。只用于登记"证据原件确认找不回、md 正文已标注原因，链接留作历史
    索引"的场景（例如 codex 产出目录已被清理），不得用来掩盖真正的路径层级错误。

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
LINKCHECK_IGNORE_FILE = Path(__file__).resolve().parent / ".linkcheck-ignore"

# markdown 行内链接：[text](target)、[text](target "title")、[text](target 'title')。
# target 允许包含空格前需要有引号包裹的 title，本正则先贪心捕获括号内全部内容，再在 Python 侧
# 拆出 target 与可选 title（比在正则里精确处理引号嵌套更稳妥、可读）。
_MD_LINK_RE = re.compile(r"\[([^\]]*)\]\(([^)]+)\)")

# 参考式链接定义：[label]: target "title"（本仓库文档基本不用这种写法，仍一并覆盖，避免漏检）。
_MD_REF_DEF_RE = re.compile(r"^\s{0,3}\[[^\]]+\]:\s*(\S+)(?:\s+.*)?$", re.MULTILINE)

_EXTERNAL_SCHEME_RE = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.\-]*:(?://|[^/\\])")
_WINDOWS_ABS_RE = re.compile(r"^[a-zA-Z]:[\\/]")

# 各轮审计 core-findings.md/validation-boundaries.md 等大量使用 "path.cs:123" /
# "path.cs:123-145" 做源码行号引用（如 `[core/x/Y.cs:83-92](../../../core/x/Y.cs:83)`），
# 这不是文件系统路径的一部分，而是编辑器/IDE 可识别的 "文件:行号" 跳转记法。开头一段 `_collect_
# broken_links` 曾把这类链接误判为 90 处失效链接（2026-09-09 codex 第十二轮 P3 归档项复核时，
# 取消 audit-* 排除后触发），实测校验：把结尾的 `:数字` 或 `:数字-数字` 剥掉之后，绝大多数目标
# 路径确实存在。只有在"完整路径（含 :行号）"本身不存在、且剥掉行号后缀确实存在时才做这个宽松匹配，
# 避免掩盖真正的路径错误（例如整个目录层级都算错的链接，剥掉行号后一样不存在，仍会被判定为失效）。
_LINE_CITATION_SUFFIX_RE = re.compile(r":\d+(?:-\d+)?$")


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
        files.append((REPO_ROOT / line).resolve())
    return sorted(files)


def _load_linkcheck_ignore() -> set[tuple[str, str]]:
    """读取 ``.linkcheck-ignore`` 白名单，返回 ``(md 仓库相对路径, target 原文)`` 集合。

    文件不存在时返回空集合（不是错误——大多数场景下不需要豁免任何链接）。
    """
    if not LINKCHECK_IGNORE_FILE.exists():
        return set()
    entries: set[tuple[str, str]] = set()
    for raw_line in LINKCHECK_IGNORE_FILE.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if "::" not in line:
            continue
        md_rel, target = line.split("::", 1)
        entries.add((md_rel.strip(), target.strip()))
    return entries


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
    if not resolved.exists():
        # 尝试按 "文件:行号" 引用记法剥掉行号后缀再判一次存在性（见 _LINE_CITATION_SUFFIX_RE
        # 判断记录）；剥掉后仍不存在就保留原始（未剥离）的 resolved，走正常的"链接失效"路径。
        m = _LINE_CITATION_SUFFIX_RE.search(decoded)
        if m:
            stripped_resolved = (md_file.parent / decoded[: m.start()]).resolve()
            if stripped_resolved.exists():
                return path_part, stripped_resolved
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
    assert len(md_files) > 150, (
        f"只发现 {len(md_files)} 篇 Markdown，明显少于预期（含 audit-* 归档基线约 220 篇左右）——"
        "先检查 git ls-files/exclude 规则是否误伤，而不是继续跑存在性校验。"
    )
    ignore_entries = _load_linkcheck_ignore()

    for md_file in md_files:
        try:
            text = md_file.read_text(encoding="utf-8")
        except UnicodeDecodeError as exc:  # pragma: no cover - 仓库内文档均为 UTF-8
            problems.append(f"{md_file}: 读取失败（非 UTF-8？）：{exc}")
            continue

        rel_md = md_file.relative_to(REPO_ROOT).as_posix()

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
            if (rel_md, path_part) in ignore_entries:
                # 已在 toolchain/tests/.linkcheck-ignore 显式登记："证据原件确认找不回，正文
                # 已标注原因"，不算文档缺陷。
                continue
            problems.append(
                f"{rel_md}: 链接目标 '{path_part}' 解析为 '{resolved_path}' 不存在"
            )
    return problems


def test_no_broken_relative_links_in_tracked_markdown() -> None:
    problems = _collect_broken_links()
    if problems:
        detail = "\n  ".join(problems)
        pytest.fail(
            f"发现 {len(problems)} 处失效相对链接（排除外部链接、锚点、绝对路径、.gitignore 覆盖"
            f"路径、toolchain/tests/.linkcheck-ignore 白名单）：\n  {detail}"
        )


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-v"]))
