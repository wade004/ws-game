"""``docs/manual/docfx.json`` 概念页收录范围与 ``toolchain/gen_manual_toc.py`` 文档清单一致性的测试。

背景：手册生成时 ``docs/manual/build.ps1`` 会比对"站点 concepts/ 下的 html 页数"与 git 清单期望页数，
不符即让 ``build.ps1 -Release`` 在打包阶段失败。1.93.0 首次发布即因新增顶层目录 ``lab/`` 的 README
被目录生成脚本收录、却不在 docfx.json 的概念页通配里而中断。本测试把这类漂移提前到日常门禁：
凡是带模块 README 的顶层目录，都必须被 docfx.json 的概念页通配覆盖。

运行：``python -m pytest toolchain/tests/test_manual_docfx_coverage.py -q``
"""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Iterable, List

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from gen_manual_toc import ARCH_DIR, list_docs  # noqa: E402

REPO_ROOT = TOOLCHAIN_DIR.parent
DOCFX_JSON = REPO_ROOT / "docs" / "manual" / "docfx.json"


def concept_globs(docfx: dict) -> List[str]:
    """docfx.json 里以仓库根为 src、落到 concepts/ 的那一组 files 通配。"""
    for entry in docfx["build"]["content"]:
        if entry.get("src") == "../.." and entry.get("dest") == "concepts":
            return list(entry["files"])
    raise AssertionError("docfx.json 缺少 src=../.. dest=concepts 的概念页条目")


def uncovered_readme_dirs(readmes: Iterable[str], globs: Iterable[str]) -> List[str]:
    """返回带 README 却不被任何 `<顶层目录>/**/README.md` 通配（根 README 用 `README.md`）覆盖的顶层目录。"""
    globs = set(globs)
    missing = set()
    for rel in readmes:
        parts = rel.split("/")
        if parts[0] in (ARCH_DIR, "docs"):
            continue
        if len(parts) == 1:
            if "README.md" not in globs:
                missing.add("README.md")
            continue
        if f"{parts[0]}/**/README.md" not in globs:
            missing.add(parts[0])
    return sorted(missing)


def test_every_top_level_readme_dir_is_in_docfx_concepts():
    docfx = json.loads(DOCFX_JSON.read_text(encoding="utf-8"))
    readmes = [d for d in list_docs(REPO_ROOT) if d.endswith("README.md")]
    assert readmes, "文档清单为空"
    missing = uncovered_readme_dirs(readmes, concept_globs(docfx))
    assert missing == [], (
        f"这些顶层目录有 README 但不在 docs/manual/docfx.json 的概念页通配里：{missing}；"
        "请在 src=../.. dest=concepts 条目的 files 里补 `<目录>/**/README.md`，否则发布打包时手册页数校验失败"
    )


def test_detects_new_top_level_dir_missing_from_globs():
    globs = ["README.md", "core/**/README.md", "architecture/**/*.md"]
    readmes = ["README.md", "core/a/README.md", "newdir/README.md", "architecture/x/README.md"]
    assert uncovered_readme_dirs(readmes, globs) == ["newdir"]
