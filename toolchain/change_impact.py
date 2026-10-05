#!/usr/bin/env python3
"""定向门禁的影响集判定（ADR-0126）：改动路径 -> 级别 T0～T3 -> 要跑的测试工程/步骤/引擎侧分类。

输入：改动路径集合（来源：基线提交 ``--base``、暂存区 ``--staged``、单个提交 ``--commit``、标准输入/参数
``--paths-from-stdin`` / ``--paths``），外加可选的手动模块列表 ``--modules a,b``。
输出：JSON（``--format json``，默认）或可读文本（``--format text``）；``--out`` 把 JSON 另存成文件，
供 ``check.ps1`` 读取。判定依据全部来自 ``toolchain/module_map.json``（模块表，唯一出处），本脚本只含
判定算法，不含任何路径常量。

四级影响集（每个路径单独判级，整体取最高级；路径集合为空时判 T0 并标注 ``empty``）：

- T0 文档：``*.md``（模块 schema 目录里的除外）、``architecture/**``、``docs/**``、``timing/**``、``.github/**``。
- T1 子模块内部：模块目录下除 ``contracts``/``schema``/``generated`` 之外的内容（含该模块的 tests），
  以及层级测试工程目录 ``core/<层>/tests/**``、``presentation/tests/**``，以及共享目录（assembly/common）里
  的 ``tests/`` 子树（只影响所在层的测试工程；``tier_rules.shared_tests_globs`` 清空即退回严格口径）。
- T2 公开面：模块的 ``contracts/``、``schema/``、``generated/``，以及模块内非测试代码里的
  ``I*.cs`` / ``*Events.cs`` 公开类型文件；层级范围（层内共享面 ``core/*/common|assembly``、
  ``presentation/common|assembly`` 与层根文件，见 ``path_rules`` 的 ``scope=layer``）；适配层的
  运行时/编辑器代码与一致性套件（``path_rules``）。
- T3 共享面：tier_rules.shared_globs 命中的路径（data/stub 生产代码/工具链/csproj/sln/门禁脚本…），以及
  **未被任何规则覆盖的路径（保守）**。

``path_rules``（``module_map.json`` 的 ``tier_rules.path_rules``）把"路径类别 -> 级别 + 额外要跑的测试工程/
引擎侧分类/步骤"登记成数据：同一路径命中的多条规则取并集、级别取最高；规则可带 ``except`` 排除子集
（例如 csproj 仍归共享面）。脚本里没有任何路径常量。

判级顺序（先到先得，见 module_map.json 注释）：模块公开面 -> 文档 -> 共享目录内测试 -> 路径规则 -> 共享面
-> 模块内部 -> 层级测试工程 -> 默认 T3。取舍（ADR-0126）：切片级只跑下游一层，剩余风险由里程碑全量兜底。

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
# 路径规则（tier_rules.path_rules）
# ---------------------------------------------------------------------------


def _match_path_rules(rules: dict[str, Any], path: str, module_map: dict[str, Any]) -> list[dict[str, Any]]:
    out: list[dict[str, Any]] = []
    for r in rules.get("path_rules", []):
        if not any_match(r.get("globs", []), path) or any_match(r.get("except", []), path):
            continue
        if r.get("scope") == "layer" and _find_layer_by_path(module_map, path) is None:
            continue
        out.append(r)
    return out


def _merge_rule_effects(matched: list[dict[str, Any]], layer_scope: bool) -> dict[str, Any]:
    eff: dict[str, Any] = {
        "rule_ids": [r["id"] for r in matched],
        "layer_scope": layer_scope,
        "dotnet_projects": [],
        "engine_categories": [],
        "engine_from_file": False,
        "engine_all": False,
        "steps": [],
        "needs_dll_sync": False,
    }
    for r in matched:
        for key in ("dotnet_projects", "engine_categories", "steps"):
            for v in r.get(key, []):
                if v not in eff[key]:
                    eff[key].append(v)
        eff["engine_from_file"] = eff["engine_from_file"] or bool(r.get("engine_from_file"))
        eff["engine_all"] = eff["engine_all"] or bool(r.get("engine_all"))
        eff["needs_dll_sync"] = eff["needs_dll_sync"] or bool(r.get("needs_dll_sync"))
    return eff


_CATEGORY_RE = re.compile(r'\[Category\("([^"]+)"\)\]')


def read_file_categories(repo_root: Path, rel_path: str) -> list[str] | None:
    """引擎侧测试文件里标的 ``[Category("…")]`` 值（去重保序）；文件不存在返回 None。

    ``.meta`` 取它所属的源文件（去掉 ``.meta`` 后缀）。
    """
    rel = rel_path[:-5] if rel_path.lower().endswith(".meta") else rel_path
    f = repo_root / rel
    if not f.is_file():
        return None
    try:
        text = f.read_text(encoding="utf-8-sig", errors="replace")
    except OSError:
        return None
    return list(dict.fromkeys(_CATEGORY_RE.findall(text)))


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

    def result(
        level: int,
        rule: str,
        module: dict[str, Any] | None = mod,
        layer: str | None = None,
        effects: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        return {
            "path": p,
            "level": level,
            "rule": rule,
            "module": module["name"] if module else None,
            "layer": layer if layer is not None else (module["layer"] if module else None),
            "effects": effects or {},
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
    # 3b. 路径规则（tier_rules.path_rules）：多条同时命中取并集、级别取最高；scope=layer 的规则把改动
    #     记到所在层（层级范围），找不到所在层的该规则不生效（回落到后面的共享面/未知路径 T3）。
    matched = _match_path_rules(rules, p, module_map)
    if matched:
        layer_scope = any(r.get("scope") == "layer" for r in matched)
        layer = _find_layer_by_path(module_map, p) if layer_scope else None
        return result(
            max(int(r["level"]) for r in matched),
            "路径规则 " + "、".join(r["id"] for r in matched),
            None,
            layer,
            _merge_rule_effects(matched, layer_scope),
        )
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


def _trigger_hit(step: dict[str, Any], classified: list[dict[str, Any]], key: str = "triggers") -> str | None:
    triggers = step.get(key, [])
    if not triggers:
        return None
    allow_md = bool(step.get("trigger_md"))
    # 步骤可登记 triggers_except：命中它的路径不触发本步骤（只作用于 triggers，不影响 always_triggers 与路径规则
    # 显式登记的 steps）。例：toolchain_pytest 的 toolchain/** 触发，排除 gate_floors.json（它只需 floors_pytest）。
    excluded = step.get("triggers_except", []) if key == "triggers" else []
    for c in classified:
        if c["path"].lower().endswith(".md") and not allow_md:
            continue
        if excluded and any_match(excluded, c["path"]):
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
    repo_root: Path | str | None = None,
) -> dict[str, Any]:
    """改动路径集合 -> 判定结果（可 JSON 序列化的 dict）。

    唯一的非纯部分：路径规则标 ``engine_from_file`` 的引擎侧测试文件要读 ``repo_root`` 下该文件里的
    ``[Category]`` 标注（默认脚本所在仓库）。
    """
    repo_root = Path(repo_root) if repo_root is not None else TOOLCHAIN_DIR.parent
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
    scope_layers: set[str] = set()  # path_rules scope=layer 命中的层（层级范围）
    for c in classified:
        eff = c.get("effects") or {}
        if c["level"] in (1, 2) and c["layer"]:
            layer_hits.add(c["layer"])
        if c["level"] == 2 and c["layer"]:
            public_layers.add(c["layer"])
            if c["module"]:
                public_modules.add(c["module"])
        if eff.get("layer_scope") and c["layer"]:
            scope_layers.add(c["layer"])
        if c["module"] and c["level"] >= 1:
            entry = hit_modules.setdefault(c["module"], {"name": c["module"], "layer": c["layer"], "max_level": 0, "files": 0})
            entry["max_level"] = max(entry["max_level"], c["level"])
            entry["files"] += 1
    modules_out = sorted(hit_modules.values(), key=lambda e: (layers_order.index(e["layer"]) if e["layer"] in layers_order else 99, e["name"]))
    for e in modules_out:
        e["max_level"] = LEVEL_NAMES[e["max_level"]]

    # 测试工程：命中层（T2/层级范围另加下游一层）的测试工程 + 路径规则登记的显式测试工程
    test_layers: list[str] = []
    dotnet_projects: list[str] = []
    if 1 <= level <= 2:
        want = set(layer_hits)
        for pl in public_layers:
            want.update(module_map["layers"][pl].get("downstream", []))
        test_layers = [l for l in layers_order if l in want]
        dotnet_projects = [module_map["layers"][l]["test_project"] for l in test_layers]
        for c in classified:
            for proj in (c.get("effects") or {}).get("dotnet_projects", []):
                if proj not in dotnet_projects:
                    dotnet_projects.append(proj)
    dotnet_mode = "solution" if level == 3 else ("projects" if dotnet_projects else "none")

    # 交互例外
    all_paths = [c["path"] for c in classified if not c.get("manual")]
    exceptions = _exception_hits(module_map, all_paths)

    notes: list[str] = []

    # 引擎侧待跑分类
    engine_cfg = module_map.get("engine", {})
    shared_cat = engine_cfg.get("shared_category", "module:shared")
    cat_prefix = engine_cfg.get("category_prefix", "module:")
    categories: list[str] = []
    classes: list[str] = []
    rule_categories: list[str] = []
    rule_engine_all = False
    rule_dll_sync = False
    for c in classified:
        eff = c.get("effects") or {}
        rule_categories.extend(eff.get("engine_categories", []))
        rule_engine_all = rule_engine_all or bool(eff.get("engine_all"))
        rule_dll_sync = rule_dll_sync or bool(eff.get("needs_dll_sync"))
        if eff.get("engine_from_file") and level < 3:
            # 引擎侧测试文件：只跑该文件自己标的分类；文件里没有任何模块分类（辅助类/asmdef 等，被哪些
            # 用例用到看不出来）就保守跑全部 PlayMode；文件已不存在（提交里删除）不贡献分类。
            found = read_file_categories(repo_root, c["path"])
            if found is None:
                notes.append(f"{c['path']} 在工作树里不存在（已删除？），不贡献引擎侧分类")
                continue
            usable = [x for x in found if x.startswith(cat_prefix) or x.startswith("interaction:")]
            if usable:
                rule_categories.extend(usable)
            else:
                rule_engine_all = True
                notes.append(f"{c['path']} 里没有模块分类标注（辅助文件/程序集定义），引擎侧保守跑全部 PlayMode")
    engine_dll_sync = rule_dll_sync
    if level == 3:
        engine_mode = "all"
    elif level == 0:
        engine_mode = "none"
    else:
        engine_mode = "filtered"
        if public_modules or scope_layers:
            for name in sorted(public_modules):
                cat = by_name[name].get("engine_category") or f"{cat_prefix}{name}"
                categories.append(cat)
            for m in module_map["modules"]:
                if m["layer"] in scope_layers:
                    categories.append(m.get("engine_category") or f"{cat_prefix}{m['name']}")
            categories.append(shared_cat)
            engine_dll_sync = True
        categories.extend(rule_categories)
        for ex in exceptions:
            if ex.get("engine_category"):
                categories.append(ex["engine_category"])
                engine_dll_sync = True
            if ex.get("engine_class"):
                classes.append(ex["engine_class"])
        categories = list(dict.fromkeys(categories))
        classes = list(dict.fromkeys(classes))
        if rule_engine_all:
            engine_mode = "all"
            categories = []
        elif not categories and not classes:
            engine_mode = "none"
    playmode_filter = ";".join(categories) if engine_mode == "filtered" else ""

    # 步骤选择
    steps_run: list[dict[str, str]] = []
    steps_skip: list[dict[str, str]] = []
    chosen: dict[str, str] = {}
    engine_pending = (engine_mode == "filtered" and bool(categories)) or (engine_mode == "all" and level < 3)
    rule_steps: dict[str, str] = {}
    for c in classified:
        eff = c.get("effects") or {}
        for sid in eff.get("steps", []):
            rule_steps.setdefault(sid, f"路径规则 {eff['rule_ids'][0]}（{c['path']}）")

    def needs_met(step: dict[str, Any]) -> bool:
        # 步骤字段 needs：基础步骤只在确有对象时才跑（没有测试工程就不编译不跑 dotnet test；没有层级公开面就不跑 ABI 探针）
        need = step.get("needs")
        if need == "dotnet_projects":
            return bool(dotnet_projects)
        if need == "public_layers":
            return bool(public_layers)
        return True

    for step in module_map["steps"]:
        sid = step["id"]
        if level == 3:
            chosen[sid] = "T3 共享面/未知路径，全量"
            continue
        if level_name in step.get("base_tiers", []) and needs_met(step):
            chosen[sid] = f"{level_name} 基础步骤"
            continue
        # 步骤可登记 min_level：改动级别低于它时，触发路径不生效（例：数值仿真基线比对只在 T2 及以上
        # 触发，复盘拍板 2026-10-01——T1 只动模块内部，基线十几秒与其收益不成比例，T2 改契约才值得跑）。
        # always_triggers 不受 min_level 限制：它们是该步骤自己的输入/基线文件（改了必须比对，否则漏检）。
        if level >= 1:
            if sid in rule_steps:
                chosen[sid] = rule_steps[sid]
                continue
            hit = None
            if level >= int(step.get("min_level", 1)):
                hit = _trigger_hit(step, classified)
            if not hit:
                hit = _trigger_hit(step, classified, "always_triggers")
            if hit:
                chosen[sid] = f"触发路径 {hit}"
                continue
            if step.get("engine_gated") and engine_pending:
                chosen[sid] = "引擎侧有待跑分类，需再跑 PlayMode"
                continue
            if step.get("dll_sync_gated") and engine_dll_sync and engine_pending:
                chosen[sid] = "引擎侧有待跑分类且改动涉及核心/适配代码，需先同步 DLL"
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
    needs_unity_ids = {st["id"] for st in module_map["steps"] if st.get("needs_unity")}
    for step in module_map["steps"]:
        sid = step["id"]
        if sid in chosen:
            steps_run.append({"id": sid, "label": step.get("label", sid), "reason": chosen[sid]})
        else:
            steps_skip.append({"id": sid, "label": step.get("label", sid), "reason": f"{level_name} 未触发"})
    unity_steps = [st["id"] for st in steps_run if st["id"] in needs_unity_ids]
    rule_ids_hit: list[str] = []
    for c in classified:
        for rid in (c.get("effects") or {}).get("rule_ids", []):
            if rid not in rule_ids_hit:
                rule_ids_hit.append(rid)

    if empty:
        notes.append("没有任何改动路径，按 T0 处理（只跑文档相关基础步骤）")
    unknown = [c["path"] for c in classified if c["rule"].startswith("未被任何规则覆盖")]
    if unknown:
        notes.append(f"{len(unknown)} 个路径未被任何规则覆盖，已保守判 T3")
    if level == 2 and public_layers:
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
            "dll_sync": bool(engine_dll_sync and engine_pending),
            "steps": unity_steps,
        },
        "rules": rule_ids_hit,
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
        why = "T3 全量" if plan["level"] == "T3" else f"{plan['level']} 路径规则要求跑全部 PlayMode"
        lines.append(f"引擎侧 PlayMode：{why}（不过滤；在主检出或路径足够短的工作树里由有引擎权限的任务执行；深层 scratchpad 工作树交主会话）")
    elif eng["mode"] == "filtered":
        lines.append("引擎侧待跑 PlayMode（在主检出或路径足够短的工作树里由有引擎权限的任务执行；深层 scratchpad 工作树交主会话）：")
        lines.append(f"  分类过滤串：{eng['playmode_filter'] or '（无）'}")
        for ex in plan["exceptions"]:
            lines.append(f"  交互例外 {ex['id']}（模块 {ex['module']}）：{ex['engine_class']} / {ex['engine_category']}")
    else:
        lines.append("引擎侧 PlayMode：不需要")
    if plan["level"] != "T3" and eng.get("steps"):
        sync = "（先同步 DLL）" if eng.get("dll_sync") else ""
        lines.append("引擎侧待跑步骤（需 Unity，-SkipUnity 下会跳过，交主检出/短路径工作树执行）：" + "、".join(eng["steps"]) + sync)
    if plan.get("rules"):
        lines.append("命中路径规则：" + "、".join(plan["rules"]))
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
        plan = plan_from_paths(paths, module_map, manual, source, repo_root)
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
