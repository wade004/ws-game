#!/usr/bin/env python3
"""定向门禁的影响集判定（ADR-0126）：改动路径 -> 级别 T0～T3 -> 要跑的测试工程/步骤/引擎侧分类。

输入：改动路径集合（来源：基线提交 ``--base``、暂存区 ``--staged``、单个提交 ``--commit``、标准输入/参数
``--paths-from-stdin`` / ``--paths``），外加可选的手动模块列表 ``--modules a,b``。
输出：JSON（``--format json``，默认）或可读文本（``--format text``）；``--out`` 把 JSON 另存成文件，
供 ``check.ps1`` 读取。判定依据全部来自 ``toolchain/module_map.json``（模块表，唯一出处），本脚本只含
判定算法，不含任何路径常量。

四级影响集（每个路径单独判级，整体取最高级；路径集合为空时判 T0 并标注 ``empty``）：

- T0 文档：``*.md``（模块 schema 目录里的除外）、``architecture/**``、``docs/**``。
- T1 子模块内部：模块目录下除 ``contracts``/``schema``/``generated`` 之外的内容（含该模块的 tests），
  以及层级测试工程目录 ``core/<层>/tests/**``、``presentation/tests/**``，以及共享目录（assembly/common）里
  的 ``tests/`` 子树（只影响所在层的测试工程；``tier_rules.shared_tests_globs`` 清空即退回严格口径）。
- T2 公开面：模块的 ``contracts/``、``schema/``、``generated/``，以及模块内非测试代码里的
  ``I*.cs`` / ``*Events.cs`` 公开类型文件。
- T3 共享面：tier_rules.shared_globs 命中的路径（assembly/common/data/stub/工具链/引擎侧包 Runtime/
  csproj/sln/门禁脚本/层根文件…），以及**未被任何规则覆盖的路径（保守）**。

判级顺序（先到先得，见 module_map.json 注释）：模块公开面 -> 文档 -> 共享面 -> 模块内部 -> 层级测试工程
-> 默认 T3。取舍（ADR-0126）：切片级只跑下游一层，剩余风险由里程碑全量兜底。

返回码：0 成功；2 参数/模块表/git 出错。
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Iterable

sys.path.insert(0, str(Path(__file__).resolve().parent))

from _console import ensure_utf8_stdio  # noqa: E402

TOOLCHAIN_DIR = Path(__file__).resolve().parent
DEFAULT_MAP = TOOLCHAIN_DIR / "module_map.json"
LEVEL_NAMES = ["T0", "T1", "T2", "T3"]
MAX_TEXT_FILE_LINES = 40


class ImpactError(Exception):
    """输入/配置有误（映射成退出码 2）。"""


# ---------------------------------------------------------------------------
# glob：`*` 不跨目录，`**` 跨目录，`**/` 可匹配零层目录；整串匹配，大小写不敏感。
# ---------------------------------------------------------------------------

_GLOB_CACHE: dict[str, re.Pattern[str]] = {}


def glob_to_regex(pattern: str) -> re.Pattern[str]:
    cached = _GLOB_CACHE.get(pattern)
    if cached is not None:
        return cached
    out: list[str] = []
    i = 0
    while i < len(pattern):
        c = pattern[i]
        if c == "*":
            if pattern.startswith("**/", i):
                out.append("(?:.*/)?")
                i += 3
                continue
            if pattern.startswith("**", i):
                out.append(".*")
                i += 2
                continue
            out.append("[^/]*")
            i += 1
            continue
        if c == "?":
            out.append("[^/]")
            i += 1
            continue
        out.append(re.escape(c))
        i += 1
    compiled = re.compile("^" + "".join(out) + "$", re.IGNORECASE)
    _GLOB_CACHE[pattern] = compiled
    return compiled


def glob_match(pattern: str, path: str) -> bool:
    return glob_to_regex(pattern).match(path) is not None


def any_match(patterns: Iterable[str], path: str) -> str | None:
    for pat in patterns:
        if glob_match(pat, path):
            return pat
    return None


def normalize_path(path: str) -> str:
    p = path.strip().replace("\\", "/")
    while p.startswith("./"):
        p = p[2:]
    return p


# ---------------------------------------------------------------------------
# 模块表
# ---------------------------------------------------------------------------


def load_module_map(path: Path | str = DEFAULT_MAP) -> dict[str, Any]:
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ImpactError(f"找不到模块表：{path}") from exc
    except json.JSONDecodeError as exc:
        raise ImpactError(f"模块表 JSON 非法：{path}：{exc}") from exc
    for key in ("layers", "tier_rules", "steps", "modules"):
        if key not in data:
            raise ImpactError(f"模块表缺少 '{key}' 段：{path}")
    return data


def _modules_by_prefix(module_map: dict[str, Any]) -> list[dict[str, Any]]:
    return sorted(module_map["modules"], key=lambda m: len(m["path"]), reverse=True)


def _find_module(modules_sorted: list[dict[str, Any]], path: str) -> dict[str, Any] | None:
    for m in modules_sorted:
        if path.startswith(m["path"]):
            return m
    return None


def _find_layer_by_path(module_map: dict[str, Any], path: str) -> str | None:
    for layer_name, layer in module_map["layers"].items():
        if path.startswith(layer["path"]):
            return layer_name
    return None


def _find_layer_by_tests_dir(module_map: dict[str, Any], path: str) -> str | None:
    for layer_name, layer in module_map["layers"].items():
        tests_dir = layer["test_project"].rsplit("/", 1)[0] + "/"
        if path.startswith(tests_dir):
            return layer_name
    return None


# ---------------------------------------------------------------------------
# 单路径判级
# ---------------------------------------------------------------------------


def classify_path(path: str, module_map: dict[str, Any], _sorted: list[dict[str, Any]] | None = None) -> dict[str, Any]:
    """返回 {path, level(0..3), rule, module, layer}。未被任何规则覆盖 -> T3（保守）。"""
    p = normalize_path(path)
    rules = module_map["tier_rules"]
    modules_sorted = _sorted if _sorted is not None else _modules_by_prefix(module_map)
    mod = _find_module(modules_sorted, p)
    top = ""
    if mod is not None:
        rel = p[len(mod["path"]):]
        top = rel.split("/", 1)[0] if "/" in rel else ""

    def result(level: int, rule: str, module: dict[str, Any] | None = mod, layer: str | None = None) -> dict[str, Any]:
        return {
            "path": p,
            "level": level,
            "rule": rule,
            "module": module["name"] if module else None,
            "layer": layer if layer is not None else (module["layer"] if module else None),
        }

    # 1. 模块公开面目录
    if mod is not None and top in rules.get("public_dirs", []):
        return result(2, f"模块公开面目录 {top}/")
    # 2. 文档
    hit = any_match(rules.get("doc_globs", []), p)
    if hit:
        return result(0, f"文档（{hit}）", mod)
    # 3a. 共享目录里的测试代码：只影响所在层的测试工程，归 T1（层级，不计入任何子模块）。
    #     见 module_map.json tier_rules.shared_tests_globs；清空该列表即退回『共享目录一律 T3』的严格口径。
    hit = any_match(rules.get("shared_tests_globs", []), p)
    if hit:
        layer = _find_layer_by_path(module_map, p)
        if layer is not None:
            return result(1, f"共享目录内的测试代码（{hit}）", None, layer)
    # 3. 共享面
    hit = any_match(rules.get("shared_globs", []), p)
    if hit:
        return result(3, f"共享面（{hit}）", None, None)
    # 4. 模块内部
    if mod is not None:
        if top == "tests":
            return result(1, "模块内部测试")
        base = p.rsplit("/", 1)[-1]
        for rx in rules.get("public_file_regex", []):
            if re.match(rx, base):
                return result(2, f"模块公开类型文件（{base} 命中 {rx}）")
        return result(1, "模块内部")
    # 5. 层级测试工程目录
    layer = _find_layer_by_tests_dir(module_map, p)
    if layer is not None:
        return result(1, "层级测试工程目录", None, layer)
    # 6. 默认
    return result(3, "未被任何规则覆盖（保守按 T3）", None, None)


# ---------------------------------------------------------------------------
# 整体判定
# ---------------------------------------------------------------------------


def _trigger_hit(step: dict[str, Any], classified: list[dict[str, Any]]) -> str | None:
    triggers = step.get("triggers", [])
    if not triggers:
        return None
    allow_md = bool(step.get("trigger_md"))
    for c in classified:
        if c["path"].lower().endswith(".md") and not allow_md:
            continue
        if any_match(triggers, c["path"]):
            return c["path"]
    return None


def _exception_hits(module_map: dict[str, Any], paths: list[str]) -> list[dict[str, Any]]:
    hits: list[dict[str, Any]] = []
    for m in module_map["modules"]:
        for exc in m.get("interaction_exceptions", []):
            matched = [p for p in paths if any_match(exc.get("when_paths", []), p)]
            if matched:
                also = exc.get("also_run", {})
                hits.append(
                    {
                        "id": exc.get("id"),
                        "module": m["name"],
                        "matched_paths": matched,
                        "engine_class": also.get("engine_class"),
                        "engine_category": also.get("engine_category"),
                        "why": exc.get("why", ""),
                    }
                )
    return hits


def plan_from_paths(
    paths: Iterable[str],
    module_map: dict[str, Any],
    manual_modules: Iterable[str] = (),
    source: str = "",
) -> dict[str, Any]:
    """纯函数：改动路径集合 -> 判定结果（可 JSON 序列化的 dict）。"""
    modules_sorted = _modules_by_prefix(module_map)
    by_name = {m["name"]: m for m in module_map["modules"]}
    layers_order = list(module_map["layers"].keys())

    norm: list[str] = []
    seen: set[str] = set()
    for raw in paths:
        p = normalize_path(raw)
        if p and p not in seen:
            seen.add(p)
            norm.append(p)
    classified = [classify_path(p, module_map, modules_sorted) for p in norm]

    manual = []
    for name in manual_modules:
        name = name.strip()
        if not name:
            continue
        if name not in by_name:
            raise ImpactError(f"未知模块 {name!r}；已登记模块：{', '.join(sorted(by_name))}")
        m = by_name[name]
        manual.append(name)
        classified.append(
            {
                "path": f"{m['path']}（手动指定模块）",
                "level": 1,
                "rule": "手动指定模块（-Modules），按子模块内部处理",
                "module": name,
                "layer": m["layer"],
                "manual": True,
            }
        )

    empty = len(classified) == 0
    level = max((c["level"] for c in classified), default=0)
    level_name = LEVEL_NAMES[level]

    # 命中模块与层
    hit_modules: dict[str, dict[str, Any]] = {}
    layer_hits: set[str] = set()
    public_layers: set[str] = set()
    public_modules: set[str] = set()
    for c in classified:
        if c["level"] in (1, 2) and c["layer"]:
            layer_hits.add(c["layer"])
        if c["level"] == 2 and c["layer"]:
            public_layers.add(c["layer"])
            if c["module"]:
                public_modules.add(c["module"])
        if c["module"] and c["level"] >= 1:
            entry = hit_modules.setdefault(c["module"], {"name": c["module"], "layer": c["layer"], "max_level": 0, "files": 0})
            entry["max_level"] = max(entry["max_level"], c["level"])
            entry["files"] += 1
    modules_out = sorted(hit_modules.values(), key=lambda e: (layers_order.index(e["layer"]) if e["layer"] in layers_order else 99, e["name"]))
    for e in modules_out:
        e["max_level"] = LEVEL_NAMES[e["max_level"]]

    # 测试工程
    test_layers: list[str] = []
    if 1 <= level <= 2:
        want = set(layer_hits)
        for pl in public_layers:
            want.update(module_map["layers"][pl].get("downstream", []))
        test_layers = [l for l in layers_order if l in want]
    dotnet_projects = [module_map["layers"][l]["test_project"] for l in test_layers]
    dotnet_mode = "solution" if level == 3 else ("projects" if dotnet_projects else "none")

    # 交互例外
    all_paths = [c["path"] for c in classified if not c.get("manual")]
    exceptions = _exception_hits(module_map, all_paths)

    # 引擎侧待跑分类
    engine_cfg = module_map.get("engine", {})
    shared_cat = engine_cfg.get("shared_category", "module:shared")
    categories: list[str] = []
    classes: list[str] = []
    if level == 3:
        engine_mode = "all"
    elif level == 0:
        engine_mode = "none"
    else:
        engine_mode = "filtered"
        if level == 2:
            for name in sorted(public_modules):
                cat = by_name[name].get("engine_category") or f"{engine_cfg.get('category_prefix', 'module:')}{name}"
                categories.append(cat)
            categories.append(shared_cat)
        for ex in exceptions:
            if ex.get("engine_category"):
                categories.append(ex["engine_category"])
            if ex.get("engine_class"):
                classes.append(ex["engine_class"])
        categories = list(dict.fromkeys(categories))
        classes = list(dict.fromkeys(classes))
        if not categories and not classes:
            engine_mode = "none"
    playmode_filter = ";".join(categories)

    # 步骤选择
    steps_run: list[dict[str, str]] = []
    steps_skip: list[dict[str, str]] = []
    chosen: dict[str, str] = {}
    engine_pending = engine_mode in ("filtered",) and bool(categories)
    for step in module_map["steps"]:
        sid = step["id"]
        if level == 3:
            chosen[sid] = "T3 共享面/未知路径，全量"
            continue
        if level_name in step.get("base_tiers", []):
            chosen[sid] = f"{level_name} 基础步骤"
            continue
        if level >= 1:
            hit = _trigger_hit(step, classified)
            if hit:
                chosen[sid] = f"触发路径 {hit}"
                continue
            if step.get("engine_gated") and engine_pending:
                chosen[sid] = "引擎侧有待跑分类，需先同步 DLL/再跑 PlayMode"
                continue
    # 依赖拉入
    changed = True
    while changed:
        changed = False
        for step in module_map["steps"]:
            if step["id"] in chosen:
                for dep in step.get("requires", []):
                    if dep not in chosen:
                        chosen[dep] = f"被 {step['id']} 依赖"
                        changed = True
    for step in module_map["steps"]:
        sid = step["id"]
        if sid in chosen:
            steps_run.append({"id": sid, "label": step.get("label", sid), "reason": chosen[sid]})
        else:
            steps_skip.append({"id": sid, "label": step.get("label", sid), "reason": f"{level_name} 未触发"})

    notes: list[str] = []
    if empty:
        notes.append("没有任何改动路径，按 T0 处理（只跑文档相关基础步骤）")
    unknown = [c["path"] for c in classified if c["rule"].startswith("未被任何规则覆盖")]
    if unknown:
        notes.append(f"{len(unknown)} 个路径未被任何规则覆盖，已保守判 T3")
    if level == 2:
        notes.append("T2 只跑下游一层的测试工程，更远的下游由里程碑全量兜底（ADR-0126 取舍）")

    return {
        "source": source,
        "level": level_name,
        "level_rank": level,
        "empty": empty,
        "changed_files": len([c for c in classified if not c.get("manual")]),
        "modules": modules_out,
        "layers": [l for l in layers_order if l in layer_hits],
        "public_layers": [l for l in layers_order if l in public_layers],
        "dotnet_test": {"mode": dotnet_mode, "projects": dotnet_projects, "layers": test_layers},
        "engine": {
            "mode": engine_mode,
            "categories": categories,
            "classes": classes,
            "playmode_filter": playmode_filter,
        },
        "exceptions": exceptions,
        "steps": {"run": steps_run, "skip": steps_skip},
        "reasons": [
            {
                "path": c["path"],
                "level": LEVEL_NAMES[c["level"]],
                "rule": c["rule"],
                "module": c["module"],
            }
            for c in classified
        ],
        "notes": notes,
    }


# ---------------------------------------------------------------------------
# 文本渲染
# ---------------------------------------------------------------------------


def render_text(plan: dict[str, Any]) -> str:
    lines: list[str] = []
    lines.append("==== 本次判定（定向门禁，ADR-0126） ====")
    if plan.get("source"):
        lines.append(f"改动来源：{plan['source']}（{plan['changed_files']} 个文件）")
    lines.append(f"级别：{plan['level']}（取各文件最高级）")
    if plan["modules"]:
        lines.append("命中模块：" + "、".join(f"{m['name']}({m['layer']},{m['max_level']})" for m in plan["modules"]))
    else:
        lines.append("命中模块：（无）")
    dn = plan["dotnet_test"]
    if dn["mode"] == "solution":
        lines.append("dotnet 测试：Core.sln 全量")
    elif dn["mode"] == "projects":
        lines.append("dotnet 测试工程：" + "、".join(dn["projects"]))
    else:
        lines.append("dotnet 测试：不跑")
    run = plan["steps"]["run"]
    skip = plan["steps"]["skip"]
    lines.append(f"将跑的步骤（{len(run)}）：" + "、".join(s["id"] for s in run))
    if skip:
        lines.append(f"将跳过的步骤（{len(skip)}，{plan['level']} 未触发）：" + "、".join(s["id"] for s in skip))
    eng = plan["engine"]
    if eng["mode"] == "all":
        lines.append("引擎侧 PlayMode：T3 全量（不过滤；agent 不能开引擎，由主会话在主检出执行）")
    elif eng["mode"] == "filtered":
        lines.append("引擎侧待跑 PlayMode（agent 不能开引擎，由主会话在主检出执行）：")
        lines.append(f"  分类过滤串：{eng['playmode_filter'] or '（无）'}")
        for ex in plan["exceptions"]:
            lines.append(f"  交互例外 {ex['id']}（模块 {ex['module']}）：{ex['engine_class']} / {ex['engine_category']}")
    else:
        lines.append("引擎侧 PlayMode：不需要")
    for n in plan["notes"]:
        lines.append(f"注：{n}")
    lines.append("判定理由（逐文件）：")
    reasons = plan["reasons"]
    for r in reasons[:MAX_TEXT_FILE_LINES]:
        lines.append(f"  {r['level']}  {r['path']}  <- {r['rule']}")
    if len(reasons) > MAX_TEXT_FILE_LINES:
        lines.append(f"  …另有 {len(reasons) - MAX_TEXT_FILE_LINES} 个文件（见 JSON 的 reasons）")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# git 取改动路径
# ---------------------------------------------------------------------------


def _git(repo_root: Path, *args: str) -> str:
    try:
        r = subprocess.run(["git", *args], cwd=str(repo_root), capture_output=True, check=False)
    except OSError as exc:
        raise ImpactError(f"无法执行 git：{exc}") from exc
    if r.returncode != 0:
        raise ImpactError(f"git {' '.join(args)} 失败：{r.stderr.decode('utf-8', errors='replace').strip()}")
    return r.stdout.decode("utf-8", errors="replace")


def _split_z(raw: str) -> list[str]:
    return [p for p in raw.split("\0") if p]


def paths_since_base(repo_root: Path, base: str) -> tuple[list[str], str]:
    """工作树（含暂存、未暂存、未跟踪）相对 base 与 HEAD 的 merge-base 的全部改动路径。"""
    try:
        mb = _git(repo_root, "merge-base", base, "HEAD").strip()
    except ImpactError:
        mb = ""
    ref = mb or base
    diff = _split_z(_git(repo_root, "diff", "--name-only", "--no-renames", "-z", ref))
    untracked = _split_z(_git(repo_root, "ls-files", "--others", "--exclude-standard", "-z"))
    short = ref[:8] if mb else ref
    return diff + untracked, f"基线 {base}（merge-base {short}）至当前工作树"


def paths_staged(repo_root: Path) -> tuple[list[str], str]:
    return _split_z(_git(repo_root, "diff", "--cached", "--name-only", "--no-renames", "-z")), "暂存区"


def paths_of_commit(repo_root: Path, sha: str) -> tuple[list[str], str]:
    parents = _git(repo_root, "rev-list", "--parents", "-n", "1", sha).split()
    if len(parents) <= 1:
        out = _git(repo_root, "diff-tree", "--root", "--no-commit-id", "--name-only", "-r", "--no-renames", "-z", sha)
    else:
        out = _git(repo_root, "diff", "--name-only", "--no-renames", "-z", f"{sha}^1", sha)
    return _split_z(out), f"提交 {sha[:8]}"


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def main(argv: list[str] | None = None) -> int:
    ensure_utf8_stdio()
    parser = argparse.ArgumentParser(description="定向门禁影响集判定（ADR-0126）")
    src = parser.add_mutually_exclusive_group()
    src.add_argument("--base", help="基线提交/分支：工作树相对 merge-base(base, HEAD) 的全部改动（含未提交与未跟踪）")
    src.add_argument("--staged", action="store_true", help="只看暂存区（预提交钩子用）")
    src.add_argument("--commit", help="单个提交的改动（回放用）")
    src.add_argument("--paths-from-stdin", action="store_true", help="从标准输入读改动路径（一行一个）")
    src.add_argument("--paths", nargs="+", help="直接给改动路径")
    parser.add_argument("--modules", default="", help="手动指定模块，逗号分隔（按 T1 子模块内部处理）")
    parser.add_argument("--map", default=str(DEFAULT_MAP), help="模块表路径")
    parser.add_argument("--repo-root", default=str(TOOLCHAIN_DIR.parent), help="仓库根")
    parser.add_argument("--format", choices=["json", "text"], default="json")
    parser.add_argument("--out", help="把 JSON 判定结果另存到该文件")
    parser.add_argument("--print-level", action="store_true", help="只打印级别（T0～T3），供钩子脚本使用")
    parser.add_argument("--text-out", help="把可读文本版判定另存到该文件（UTF-8），供 PowerShell 读取以避开管道代码页问题")
    parser.add_argument("--quiet", action="store_true", help="不向标准输出打印（配合 --out/--text-out）")
    args = parser.parse_args(argv)

    repo_root = Path(args.repo_root).resolve()
    try:
        module_map = load_module_map(args.map)
        paths: list[str] = []
        source = ""
        if args.base:
            paths, source = paths_since_base(repo_root, args.base)
        elif args.staged:
            paths, source = paths_staged(repo_root)
        elif args.commit:
            paths, source = paths_of_commit(repo_root, args.commit)
        elif args.paths_from_stdin:
            paths = [ln for ln in sys.stdin.read().splitlines() if ln.strip()]
            source = "标准输入"
        elif args.paths:
            paths = list(args.paths)
            source = "命令行路径"
        manual = [m for m in args.modules.split(",") if m.strip()]
        if manual and not source:
            source = "手动指定模块"
        plan = plan_from_paths(paths, module_map, manual, source)
    except ImpactError as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 2

    if args.out:
        Path(args.out).write_text(json.dumps(plan, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    if args.text_out:
        Path(args.text_out).write_text(render_text(plan) + "\n", encoding="utf-8", newline="\n")
    if args.quiet:
        return 0
    if args.print_level:
        print(plan["level"])
    elif args.format == "text":
        print(render_text(plan))
    else:
        print(json.dumps(plan, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
