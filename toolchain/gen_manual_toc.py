#!/usr/bin/env python3
"""API 参考手册的概念文档目录生成器。

用途：手册站点（``docs/manual/``，构建入口 ``docs/manual/build.ps1``）把仓库里全部已入库的
模块 ``README.md``（概念文档）与 ``architecture/`` 下全部 markdown（架构文档、ADR）并入同一
站点。文档在站点里保持它们在仓库里的相对路径（``docs/manual/docfx.json`` 的内容映射把仓库根
映射到站点的 ``concepts/``），因此 README 之间、README 到架构文档之间的相对链接原样可用。
本脚本只负责生成两份导航目录（不拷贝、不改写任何文档）：

- ``docs/manual/concepts/toc.yml``：按目录层级排列的模块 README 树（``architecture/`` 除外），
  每个节点的标题取该 README 的一级标题；没有 README 的中间目录只作分组名。
- ``docs/manual/concepts/architecture/toc.yml``：架构文档分区——根目录 00～14 号文档、
  选型、落地计划、手感设计、数值设计、ADR 各成一组，标题同样取文首一级标题。

两份 toc 是生成物（已在 ``.gitignore``），不提交；``docs/manual/build.ps1`` 每次构建前重新生成。

文档清单取 ``git ls-files -c -o --exclude-standard``（已入库文件加"未入库但没被 ``.gitignore`` 忽略"的文件；
被忽略的目录如 ``node_modules``、``dist``、同步生成的 ``StreamingAssets`` 副本不会混进来）；没有 git 时退化为遍历文件系统并排除同样的生成目录。

返回码：0 成功；1 找不到仓库根或清单为空；2 命令行参数错误。
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path
from typing import Dict, List, Optional

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402

ARCH_DIR = "architecture"
# 遍历文件系统（无 git）时排除的目录名；与 docs/manual/docfx.json 的 exclude 保持一致。
FALLBACK_EXCLUDE_DIRS = {
    "node_modules", "dist", "bin", "obj", "Library", "Temp", ".git", ".cache", "_site",
    "scratchpad", ".venv", "__pycache__",
}

_H1_RE = re.compile(r"^#\s+(.+?)\s*#*\s*$")
_FENCE_RE = re.compile(r"^\s*(```|~~~)")


def find_repo_root(start: Path) -> Optional[Path]:
    for p in [start, *start.parents]:
        if (p / "Core.sln").is_file():
            return p
    return None


def list_docs(repo_root: Path) -> List[str]:
    """返回仓库根相对路径（正斜杠）：全部 README.md 与 architecture/ 下全部 .md。"""
    files: List[str] = []
    try:
        out = subprocess.run(
            ["git", "-c", "core.quotepath=off", "ls-files", "-z", "-c", "-o", "--exclude-standard", "--", "*.md"],
            cwd=str(repo_root), check=True, stdout=subprocess.PIPE,
        ).stdout.decode("utf-8")
        files = [f for f in out.split("\0") if f]
    except (OSError, subprocess.CalledProcessError):
        for p in repo_root.rglob("*.md"):
            rel = p.relative_to(repo_root)
            if any(part in FALLBACK_EXCLUDE_DIRS for part in rel.parts):
                continue
            files.append(rel.as_posix())
    picked = []
    for f in files:
        name = f.rsplit("/", 1)[-1]
        if name == "README.md" or f.startswith(ARCH_DIR + "/"):
            if f.startswith("docs/manual/"):
                continue
            picked.append(f)
    return sorted(set(picked))


def first_heading(path: Path, fallback: str) -> str:
    """文首一级标题；跳过围栏代码块；没有则用 fallback。"""
    in_fence = False
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return fallback
    for line in text.splitlines():
        if _FENCE_RE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        m = _H1_RE.match(line)
        if m:
            return m.group(1).strip()
    return fallback


def yaml_str(s: str) -> str:
    """输出为 YAML 双引号字符串。"""
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


class Node:
    def __init__(self, name: str) -> None:
        self.name = name
        self.readme: Optional[str] = None  # 仓库根相对路径
        self.title: Optional[str] = None
        self.children: Dict[str, "Node"] = {}


def build_readme_tree(repo_root: Path, readmes: List[str]) -> Node:
    root = Node("")
    for rel in readmes:
        parts = rel.split("/")
        node = root
        for d in parts[:-1]:
            node = node.children.setdefault(d, Node(d))
        node.readme = rel
        node.title = first_heading(repo_root / rel, "/".join(parts[:-1]) or "README")
    return root


def emit_tree(node: Node, toc_dir: Path, repo_root: Path, indent: int, out: List[str]) -> None:
    pad = "  " * indent
    for key in sorted(node.children):
        child = node.children[key]
        name = child.title if child.readme else key
        out.append(f"{pad}- name: {yaml_str(name)}")
        if child.readme:
            out.append(f"{pad}  href: {yaml_str(rel_href(repo_root / child.readme, toc_dir))}")
        if child.children:
            out.append(f"{pad}  items:")
            emit_tree(child, toc_dir, repo_root, indent + 2, out)


def rel_href(target: Path, toc_dir: Path) -> str:
    import os

    return Path(os.path.relpath(str(target), str(toc_dir))).as_posix()


def write_concepts_toc(repo_root: Path, readmes: List[str], toc_path: Path) -> int:
    root = build_readme_tree(repo_root, readmes)
    lines: List[str] = ["# 生成物（toolchain/gen_manual_toc.py），不要手改、不提交。"]
    toc_dir = toc_path.parent
    if root.readme:
        lines.append(f"- name: {yaml_str(root.title or '仓库总览')}")
        lines.append(f"  href: {yaml_str(rel_href(repo_root / root.readme, toc_dir))}")
    emit_tree(root, toc_dir, repo_root, 0, lines)
    toc_path.parent.mkdir(parents=True, exist_ok=True)
    toc_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return len(readmes)


def write_architecture_toc(repo_root: Path, docs: List[str], toc_path: Path) -> int:
    toc_dir = toc_path.parent
    groups: Dict[str, List[str]] = {}
    for rel in docs:
        parts = rel.split("/")
        group = parts[1] if len(parts) > 2 else ""
        groups.setdefault(group, []).append(rel)

    def item(rel: str, indent: int) -> str:
        p = repo_root / rel
        title = first_heading(p, rel.rsplit("/", 1)[-1])
        pad = "  " * indent
        return f"{pad}- name: {yaml_str(title)}\n{pad}  href: {yaml_str(rel_href(p, toc_dir))}"

    def sort_key(rel: str):
        name = rel.rsplit("/", 1)[-1]
        # 目录自己的 README 排在最前，其余按文件名。
        return (0 if name == "README.md" else 1, name)

    lines: List[str] = ["# 生成物（toolchain/gen_manual_toc.py），不要手改、不提交。"]
    for rel in sorted(groups.get("", []), key=sort_key):
        lines.append(item(rel, 0))
    for group in sorted(g for g in groups if g):
        lines.append(f"- name: {yaml_str(group)}")
        lines.append("  items:")
        for rel in sorted(groups[group], key=sort_key):
            lines.append(item(rel, 2))
    toc_path.parent.mkdir(parents=True, exist_ok=True)
    toc_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return len(docs)


def main(argv: Optional[List[str]] = None) -> int:
    ensure_utf8_stdio()
    parser = argparse.ArgumentParser(description="生成手册站点的概念文档与架构文档目录（toc.yml）。")
    parser.add_argument("--repo-root", default=None, help="仓库根（默认由脚本位置向上找 Core.sln）。")
    parser.add_argument("--out-dir", default=None,
                        help="站点源目录（默认 <仓库根>/docs/manual）；toc 写到其 concepts/ 下。")
    args = parser.parse_args(argv)

    repo_root = Path(args.repo_root).resolve() if args.repo_root else find_repo_root(Path(__file__).resolve().parent)
    if repo_root is None or not (repo_root / "Core.sln").is_file():
        print("找不到仓库根（需要 Core.sln）。", file=sys.stderr)
        return 1
    manual_dir = Path(args.out_dir).resolve() if args.out_dir else repo_root / "docs" / "manual"

    docs = list_docs(repo_root)
    readmes = [d for d in docs if d.endswith("README.md") and not d.startswith(ARCH_DIR + "/")]
    arch_docs = [d for d in docs if d.startswith(ARCH_DIR + "/")]
    if not readmes or not arch_docs:
        print("文档清单为空（README 或 architecture 下没有 markdown）。", file=sys.stderr)
        return 1

    n_readme = write_concepts_toc(repo_root, readmes, manual_dir / "concepts" / "toc.yml")
    n_arch = write_architecture_toc(repo_root, arch_docs, manual_dir / "concepts" / ARCH_DIR / "toc.yml")
    n_arch_readme = sum(1 for d in arch_docs if d.endswith("/README.md"))
    # 期望页数：站点 concepts/ 下应恰好生成这么多 html（README + 架构文档）。docfx 的文件通配会
    # 把本地被忽略的同名文件（如 StreamingAssets 同步副本）一并吃进来，build.ps1 构建后据此核对。
    (manual_dir / "concepts" / "expected_pages.txt").write_text(
        f"{len(docs)}\n", encoding="utf-8", newline="\n")
    print(f"gen_manual_toc: 模块 README {n_readme} 份（不含 architecture/），"
          f"架构文档 {n_arch} 份（其中 README {n_arch_readme} 份）→ {manual_dir / 'concepts'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
