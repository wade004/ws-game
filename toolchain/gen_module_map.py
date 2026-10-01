#!/usr/bin/env python3
"""模块表生成与自检（ADR-0126，定向门禁）。

``toolchain/module_map.json`` 的 ``modules`` 段由本脚本从目录结构生成；``layers`` / ``tier_rules`` /
``steps`` / ``engine`` 四段是人工维护的，本脚本原样保留、不改。

两个模式：

- 默认（写入）：重新扫描目录，刷新每个子模块的派生字段（``path`` / ``layer`` / ``test_namespaces`` /
  ``engine_category``），**按模块名保留人工字段**（``owner`` / ``interaction_exceptions``）；新出现的子模块
  以 ``owner="TBD"`` 追加；表里有而目录里已不存在的子模块被移除（并在输出里列出）。
- ``--check``（只读，门禁自检用）：目录里出现未登记的子模块、表里登记了不存在的目录、模块名重复、
  owner 为空、登记的测试工程/交互例外文件不存在，任一项成立即退出码 1。派生字段 ``test_namespaces``
  随测试增删而变，不参与 ``--check`` 判定（只比对目录集合与人工字段的合法性）。

子模块定义：``core/<层>/<模块>/`` 与 ``presentation/<模块>/`` 下，除 ``bin`` / ``obj`` / ``tests`` /
``common`` / ``assembly`` 与隐藏目录之外的目录。``common`` / ``assembly`` 是共享面（判 T3），不登记成子模块。
``layers.<层>.single_module=true`` 的层（目前只有 sim）整层是一个子模块，名字即层名。

目录扫描优先走 ``git ls-files --cached --others --exclude-standard``（只看受版本管理或未被忽略的文件，
构建产物目录天然排除；新建但尚未 ``git add`` 的目录也能被发现），不在 git 仓库里时退回目录遍历。

返回码：0 通过/写入完成；1 ``--check`` 发现问题；2 参数或表文件本身有误。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402

TOOLCHAIN_DIR = Path(__file__).resolve().parent
DEFAULT_MAP = TOOLCHAIN_DIR / "module_map.json"
EXCLUDED_DIR_NAMES = {"bin", "obj", "tests", "common", "assembly"}
NAMESPACE_RE = re.compile(r"^\s*namespace\s+([A-Za-z0-9_.]+)", re.MULTILINE)


class MapError(Exception):
    """模块表本身有误（缺段、JSON 非法）。"""


def load_map(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise MapError(f"找不到模块表：{path}") from exc
    except json.JSONDecodeError as exc:
        raise MapError(f"模块表 JSON 非法：{path}：{exc}") from exc


def save_map(path: Path, data: dict[str, Any]) -> None:
    text = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
    # 新文件统一 LF（AGENTS.md §2）
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)


def _tracked_or_untracked_files(repo_root: Path, roots: list[str]) -> list[str] | None:
    """git 视角的文件清单（仓库相对、正斜杠）；不在 git 仓库或 git 不可用时返回 None。"""
    try:
        result = subprocess.run(
            ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", *roots],
            cwd=str(repo_root),
            capture_output=True,
            check=False,
        )
    except (OSError, ValueError):
        return None
    if result.returncode != 0:
        return None
    raw = result.stdout.decode("utf-8", errors="replace")
    return [p for p in raw.split("\0") if p]


def _walk_files(repo_root: Path, roots: list[str]) -> list[str]:
    out: list[str] = []
    for root in roots:
        base = repo_root / root
        if not base.is_dir():
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj") and not d.startswith(".")]
            for name in filenames:
                full = Path(dirpath) / name
                out.append(full.relative_to(repo_root).as_posix())
    return out


def list_repo_files(repo_root: Path, roots: list[str]) -> list[str]:
    files = _tracked_or_untracked_files(repo_root, roots)
    if files is None:
        files = _walk_files(repo_root, roots)
    return files


def discover_modules(repo_root: Path, layers: dict[str, Any]) -> list[dict[str, Any]]:
    """扫描目录结构，返回子模块的派生字段（name/layer/path/test_namespaces/engine_category）。"""
    roots = sorted({layer["path"].strip("/") for layer in layers.values()})
    files = list_repo_files(repo_root, roots)
    found: list[dict[str, Any]] = []
    for layer_name, layer in layers.items():
        layer_path = layer["path"]  # 形如 core/foundation/
        if layer.get("single_module"):
            if any(f.startswith(layer_path) for f in files):
                found.append(_module_record(repo_root, layer_name, layer_name, layer_path))
            continue
        dirs: set[str] = set()
        for f in files:
            if not f.startswith(layer_path):
                continue
            rest = f[len(layer_path):]
            if "/" not in rest:
                continue  # 层根目录下的文件，不是子模块
            top = rest.split("/", 1)[0]
            if top in EXCLUDED_DIR_NAMES or top.startswith("."):
                continue
            dirs.add(top)
        for d in sorted(dirs):
            found.append(_module_record(repo_root, d, layer_name, f"{layer_path}{d}/"))
    return found


def _module_record(repo_root: Path, name: str, layer: str, path: str) -> dict[str, Any]:
    return {
        "name": name,
        "layer": layer,
        "path": path,
        "test_namespaces": _test_namespaces(repo_root, path),
        "engine_category": f"module:{name}",
    }


def _test_namespaces(repo_root: Path, module_path: str) -> list[str]:
    tests_dir = repo_root / module_path / "tests"
    spaces: set[str] = set()
    if tests_dir.is_dir():
        for dirpath, dirnames, filenames in os.walk(tests_dir):
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
            for fn in filenames:
                if not fn.endswith(".cs"):
                    continue
                try:
                    text = (Path(dirpath) / fn).read_text(encoding="utf-8-sig", errors="replace")
                except OSError:
                    continue
                spaces.update(NAMESPACE_RE.findall(text))
    return sorted(spaces)


def merge_modules(existing: list[dict[str, Any]], discovered: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """派生字段取新、人工字段按模块名保留；新模块 owner=TBD。"""
    by_name = {m["name"]: m for m in existing}
    merged: list[dict[str, Any]] = []
    for rec in discovered:
        old = by_name.get(rec["name"], {})
        merged.append(
            {
                "name": rec["name"],
                "layer": rec["layer"],
                "path": rec["path"],
                "test_namespaces": rec["test_namespaces"],
                "engine_category": rec["engine_category"],
                "owner": old.get("owner", "TBD"),
                "interaction_exceptions": old.get("interaction_exceptions", []),
            }
        )
    return merged


def _has_wildcard(pattern: str) -> bool:
    return any(ch in pattern for ch in "*?[")


def check_map(repo_root: Path, data: dict[str, Any]) -> list[str]:
    """只读自检，返回问题清单（空 = 通过）。"""
    problems: list[str] = []
    for key in ("layers", "tier_rules", "steps", "modules"):
        if key not in data:
            problems.append(f"模块表缺少 '{key}' 段")
    if problems:
        return problems
    layers = data["layers"]

    for layer_name, layer in layers.items():
        tp = layer.get("test_project", "")
        if not tp or not (repo_root / tp).is_file():
            problems.append(f"层 {layer_name} 的 test_project 不存在：{tp!r}")
        for ds in layer.get("downstream", []):
            if ds not in layers:
                problems.append(f"层 {layer_name} 的 downstream 引用了未登记的层 {ds!r}")
        if not str(layer.get("owner", "")).strip():
            problems.append(f"层 {layer_name} 的 owner 为空（初版填 TBD，不留空）")

    registered = data["modules"]
    names = [m.get("name") for m in registered]
    dup = sorted({n for n in names if names.count(n) > 1})
    if dup:
        problems.append(f"模块名重复：{dup}")

    discovered = discover_modules(repo_root, layers)
    reg_paths = {m.get("path"): m for m in registered}
    dis_paths = {m["path"]: m for m in discovered}
    for p in sorted(set(dis_paths) - set(reg_paths)):
        problems.append(f"目录里有未登记的子模块：{p}（运行 python toolchain/gen_module_map.py 生成并补人工字段）")
    for p in sorted(set(reg_paths) - set(dis_paths)):
        problems.append(f"模块表登记了不存在的目录：{p}（运行 python toolchain/gen_module_map.py 清理）")
    # 登记项自身的合法性
    discovered_names = {m["name"]: m for m in discovered}
    for m in registered:
        name = m.get("name", "?")
        if m.get("layer") not in layers:
            problems.append(f"模块 {name} 的 layer {m.get('layer')!r} 未在 layers 段登记")
        if not str(m.get("owner", "")).strip():
            problems.append(f"模块 {name} 的 owner 为空（初版填 TBD，不留空）")
        if m.get("engine_category") != f"module:{name}":
            problems.append(f"模块 {name} 的 engine_category 应为 'module:{name}'，实际 {m.get('engine_category')!r}")
        d = discovered_names.get(name)
        if d is not None and d["path"] != m.get("path"):
            problems.append(f"模块 {name} 登记路径 {m.get('path')!r} 与目录实际位置 {d['path']!r} 不一致")
        for exc in m.get("interaction_exceptions", []):
            when = exc.get("when_paths", [])
            also = exc.get("also_run", {})
            if not when or not (also.get("engine_class") or also.get("engine_category")):
                problems.append(f"模块 {name} 的交互例外 {exc.get('id')!r} 缺 when_paths 或 also_run")
            for wp in when:
                if not _has_wildcard(wp) and not (repo_root / wp).exists():
                    problems.append(f"模块 {name} 的交互例外 {exc.get('id')!r} 引用的文件不存在：{wp}")
    return problems


def main(argv: list[str] | None = None) -> int:
    ensure_utf8_stdio()
    parser = argparse.ArgumentParser(description="生成/自检 toolchain/module_map.json（ADR-0126）")
    parser.add_argument("--check", action="store_true", help="只读自检：未登记的子模块目录/登记了不存在的目录即退出码 1")
    parser.add_argument("--map", default=str(DEFAULT_MAP), help="模块表路径（默认 toolchain/module_map.json）")
    parser.add_argument("--repo-root", default=str(TOOLCHAIN_DIR.parent), help="仓库根（默认脚本所在 toolchain 的上一级）")
    args = parser.parse_args(argv)

    repo_root = Path(args.repo_root).resolve()
    map_path = Path(args.map)
    try:
        data = load_map(map_path)
    except MapError as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 2

    if args.check:
        problems = check_map(repo_root, data)
        if problems:
            print(f"模块表自检失败：{len(problems)} 个问题")
            for p in problems:
                print(f"  - {p}")
            return 1
        print(f"模块表自检通过：{len(data['modules'])} 个子模块、{len(data['layers'])} 层")
        return 0

    if "layers" not in data:
        print("错误：模块表缺少 layers 段，无法生成", file=sys.stderr)
        return 2
    discovered = discover_modules(repo_root, data["layers"])
    old_names = {m["name"] for m in data.get("modules", [])}
    new_names = {m["name"] for m in discovered}
    data["modules"] = merge_modules(data.get("modules", []), discovered)
    save_map(map_path, data)
    added = sorted(new_names - old_names)
    removed = sorted(old_names - new_names)
    print(f"已写入 {map_path}：{len(discovered)} 个子模块（新增 {len(added)}，移除 {len(removed)}）")
    if added:
        print(f"  新增：{', '.join(added)}（owner=TBD，请补人工字段）")
    if removed:
        print(f"  移除：{', '.join(removed)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
