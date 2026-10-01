"""``toolchain/change_impact.py`` 与 ``toolchain/module_map.json``（定向门禁，ADR-0126）的测试。

两类用例（AGENTS.md / 全局约定：测试 = 复现 + 不变量各至少一条）：

- **复现用例**：给定一组改动路径，得到期望的级别、命中模块、要跑的 dotnet 测试工程、步骤集合与引擎侧分类
  过滤串（期望值在用例里写成字面量，对着模块表的设计意图，不是把实现的输出抄回来）。
- **不变量用例**：任意路径集合的判定级别单调（追加路径不会降低级别）；任何未知路径必为 T3；T3 触发全部步骤；
  T1 不会选中工具链 pytest / 数据校验 / 引擎线；check.ps1 与两条并行线脚本里出现的 ``-Id`` 与模块表 steps 登记
  逐一对得上（防"脚本加了步骤、表忘了登记 -> 该步骤在定向模式下被当成未知而漏判"）。

另外覆盖：glob 语义、模块表自检（真实仓库 + 临时仓库里未登记/幽灵目录）、引擎侧 PlayMode 测试类的
``[Category("module:…")]`` 全覆盖自检、``check.ps1 -DryRun`` 真的把分类过滤串打印出来。
"""

from __future__ import annotations

import itertools
import json
import random
import re
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))
if str(Path(__file__).resolve().parent) not in sys.path:
    sys.path.insert(0, str(Path(__file__).resolve().parent))

import change_impact as ci  # noqa: E402
import gen_module_map as gmm  # noqa: E402
from _ps_subprocess_env import clean_powershell_env  # noqa: E402

PLAYMODE_TESTS_DIR = REPO_ROOT / "adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime"


@pytest.fixture(scope="module")
def mmap() -> dict:
    return ci.load_module_map()


def _plan(mmap: dict, *paths: str, modules: tuple[str, ...] = ()) -> dict:
    return ci.plan_from_paths(list(paths), mmap, list(modules))


def _run_ids(plan: dict) -> set[str]:
    return {s["id"] for s in plan["steps"]["run"]}


def _skip_ids(plan: dict) -> set[str]:
    return {s["id"] for s in plan["steps"]["skip"]}


# ---------------------------------------------------------------------------
# glob 语义
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "pattern,path,expected",
    [
        ("**/*.md", "README.md", True),
        ("**/*.md", "a/b/c.md", True),
        ("**/*.md", "a/b/c.mdx", False),
        ("docs/**", "docs/x/y.png", True),
        ("docs/**", "adocs/x.png", False),
        ("core/*/assembly/**", "core/gameplay/assembly/A.cs", True),
        ("core/*/assembly/**", "core/gameplay/x/assembly/A.cs", False),
        ("core/*/*", "core/numbers/NumericGuard.cs", True),
        ("core/*/*", "core/numbers/stat_block/x.cs", False),
        ("core/**/schema/**", "core/foundation/event_bus/schema/a.md", True),
        ("adapters/unity/Packages/**/Runtime/**", "adapters/unity/Packages/com.x/Runtime/A/B.cs", True),
        # `**` 跨任意层目录：包内 Tests/Runtime 下的 PlayMode 测试也命中（引擎侧测试改动同样判 T3，保守）
        ("adapters/unity/Packages/**/Runtime/**", "adapters/unity/Packages/com.x/Tests/Runtime/B.cs", True),
        ("adapters/unity/Packages/**/Runtime/**", "adapters/unity/Packages/com.x/Editor/B.cs", False),
        ("**/*.csproj", "core/foundation/Core.Foundation.csproj", True),
        ("check.ps1", "Check.PS1", True),  # 大小写不敏感
    ],
)
def test_glob_semantics(pattern: str, path: str, expected: bool) -> None:
    assert ci.glob_match(pattern, path) is expected


# ---------------------------------------------------------------------------
# 复现用例：路径集合 -> 期望判定
# ---------------------------------------------------------------------------


def test_repro_t0_docs_only_runs_exactly_the_docs_subset(mmap: dict) -> None:
    plan = _plan(mmap, "architecture/00_架构总则.md", "docs/复盘/x.png", "core/foundation/event_bus/README.md", "CHANGELOG.md")
    assert plan["level"] == "T0"
    # 与现有 DocsOnly 档完全等价：门禁自检 + 两道禁用词 + 版本一致性 + 分支名规范 + 文档 pytest 子集
    assert _run_ids(plan) == {"self_check", "ban_codename", "ban_arch_terms", "version_consistency", "branch_name", "docs_pytest"}
    assert plan["dotnet_test"]["mode"] == "none"
    assert plan["engine"]["mode"] == "none"


def test_repro_timing_log_is_t0_not_unknown(mmap: dict) -> None:
    # 复现：AGENTS.md §1c 的 timing/*.jsonl 入库后，未登记时被「未知路径 = T3」判成全量，
    # 一次改动夹带耗时记录就让切片级门禁退化为全量。它是统计原始数据，与文档同属 T0。
    plan = _plan(mmap, "timing/20261001_ai-transformation_20261001.jsonl")
    assert plan["level"] == "T0"
    assert _run_ids(plan) == {"self_check", "ban_codename", "ban_arch_terms", "version_consistency", "branch_name", "docs_pytest"}
    # 夹带一个 T1 改动时只升到 T1，不升到 T3
    plan2 = _plan(mmap, "timing/x.jsonl", "core/foundation/event_bus/core/EventBus.cs")
    assert plan2["level"] == "T1"


def test_repro_t1_module_internal(mmap: dict) -> None:
    plan = _plan(mmap, "core/foundation/event_bus/core/EventBus.cs", "core/foundation/event_bus/tests/EventBusTests.cs")
    assert plan["level"] == "T1"
    assert [m["name"] for m in plan["modules"]] == ["event_bus"]
    # 整个层的测试工程，不做模块级过滤；不带下游层
    assert plan["dotnet_test"] == {
        "mode": "projects",
        "projects": ["core/foundation/tests/Tests.Foundation.csproj"],
        "layers": ["foundation"],
    }
    run = _run_ids(plan)
    assert {"dotnet_build", "dotnet_test", "module_map_check", "crlf_check"} <= run
    # T1 不跑工具链 pytest、不跑数据校验、不跑 ABI、不跑引擎线、不跑数值仿真基线（只在 T2 及以上，复盘拍板 2026-10-01）
    for forbidden in ("sim_baseline", "toolchain_pytest", "validate_merged", "validate_framework", "validate_sim_data", "validate_template_data",
                      "abi_probe", "unity_compile", "unity_editmode", "unity_playmode", "unity_build_smoke", "consumer_drill", "sync_dll"):
        assert forbidden in _skip_ids(plan), forbidden
    assert plan["engine"]["mode"] == "none"
    assert all(s["reason"] == "T1 未触发" for s in plan["steps"]["skip"])


def test_repro_sim_baseline_only_from_t2_up_for_core_changes(mmap: dict) -> None:
    # 复现：此前 T1 改 core 模块内部就触发仿真基线比对（十几秒），拍板挪出 T1，只在 T2 及以上触发。
    t1 = _plan(mmap, "core/foundation/event_bus/core/EventBus.cs")
    assert t1["level"] == "T1"
    assert "sim_baseline" in _skip_ids(t1)
    assert "sim_baseline" not in {s["id"] for s in t1["steps"]["run"]}
    t2 = _plan(mmap, "core/foundation/event_bus/contracts/IEventBus.cs")
    assert t2["level"] == "T2"
    assert "sim_baseline" in _run_ids(t2)
    # 依赖仍被拉入：仿真基线依赖 dotnet_build
    assert "dotnet_build" in _run_ids(t2)
    # 表达式不变量：min_level 之下，即使路径命中 triggers 也不触发
    sim_step = next(s for s in mmap["steps"] if s["id"] == "sim_baseline")
    assert sim_step["min_level"] == 2 and sim_step["base_tiers"] == []


def test_sim_baseline_inputs_and_baselines_trigger_even_at_t1(mmap: dict) -> None:
    # 仿真基线自己的输入数据与基线文件：改了必须比对，不受 min_level 限制（否则这类改动会漏检）。
    for path in ("core/sim/tests/data/found/x.json", "core/sim/tests/baseline/scenario_a.json"):
        plan = _plan(mmap, path)
        assert plan["level"] == "T1", path
        assert "sim_baseline" in _run_ids(plan), path


def test_repro_t1_presentation_does_not_touch_sim_baseline(mmap: dict) -> None:
    plan = _plan(mmap, "presentation/render/core/Foo.cs")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["projects"] == ["presentation/tests/Tests.PresentationCommon.csproj"]
    assert "sim_baseline" in _skip_ids(plan)  # presentation 不进 core/**，仿真基线与它无关


def test_repro_t1_movement_exception_adds_engine_category_only(mmap: dict) -> None:
    plan = _plan(mmap, "core/carriers/unit/core/MovementTickHandler.cs")
    assert plan["level"] == "T1"
    assert plan["engine"]["mode"] == "filtered"
    assert plan["engine"]["playmode_filter"] == "interaction:movement_stop_blocking"
    assert plan["engine"]["classes"] == ["MovementStopAndBlockingPlayModeTests"]
    # 引擎侧要跑，所以 DLL 同步与 PlayMode 步骤被拉进来；编译检查/EditMode/构建冒烟仍不跑
    assert {"sync_dll", "unity_playmode"} <= _run_ids(plan)
    assert {"unity_compile", "unity_editmode", "unity_build_smoke"} <= _skip_ids(plan)


def test_repro_t2_contracts_adds_downstream_abi_and_module_category(mmap: dict) -> None:
    plan = _plan(mmap, "core/foundation/event_bus/contracts/IEventBus.cs")
    assert plan["level"] == "T2"
    assert plan["dotnet_test"]["layers"] == ["foundation", "numbers"]  # 本层 + 下游一层（不是更远）
    assert "core/rules/tests/Tests.Rules.csproj" not in plan["dotnet_test"]["projects"]
    assert {"abi_probe", "dotnet_build", "dotnet_test"} <= _run_ids(plan)
    assert plan["engine"]["playmode_filter"] == "module:event_bus;module:shared"
    assert "toolchain_pytest" in _skip_ids(plan)


def test_repro_t2_navigation_contract_combines_module_shared_and_exception(mmap: dict) -> None:
    plan = _plan(mmap, "core/foundation/engine_adapter/contracts/INavigation2D.cs")
    assert plan["level"] == "T2"
    assert plan["engine"]["playmode_filter"] == "module:engine_adapter;module:shared;interaction:movement_stop_blocking"


def test_repro_t2_gameplay_downstream_includes_sim_and_presentation(mmap: dict) -> None:
    plan = _plan(mmap, "core/gameplay/quest/schema/quest.def.md")
    assert plan["level"] == "T2"
    assert plan["dotnet_test"]["layers"] == ["gameplay", "sim", "presentation"]
    # schema 变更要触发数据校验类步骤（md 路径本身也算，因为 schema 目录下的 md 是公开面）
    # —— 这里的 md 不触发 triggers（默认忽略 md），但 validate_* 在非 md 的 schema 变更里才触发：
    plan2 = _plan(mmap, "core/gameplay/quest/schema/QuestSchemas.cs")
    assert {"validate_merged", "schema_audit", "schema_order", "validate_sim_data", "validate_template_data"} <= _run_ids(plan2)


def test_repro_public_type_file_outside_contracts_is_t2(mmap: dict) -> None:
    plan = _plan(mmap, "core/rules/expr_host/IExprGroupProvider.cs")
    assert plan["level"] == "T2"
    # 测试文件名长得像接口（I*.cs）不算公开类型
    assert _plan(mmap, "core/rules/skill/tests/ISkillHostSkillBookContractTests.cs")["level"] == "T1"


def test_repro_generated_dir_is_public_surface(mmap: dict) -> None:
    plan = _plan(mmap, "core/foundation/event_bus/generated/EventKeys.g.cs")
    assert plan["level"] == "T2"
    assert "event_constants" in _run_ids(plan)


@pytest.mark.parametrize(
    "path",
    [
        "core/gameplay/assembly/GameplayAssembly.cs",
        "core/foundation/common/contracts/Id.cs",
        "presentation/common/core/ResourceReferenceTracker.cs",
        "presentation/assembly/PresentationAssembly.cs",
        "data/_framework/found/found.event_catalog.json",
        "Directory.Build.props",
        "adapters/stub/StubNavigation2D.cs",
        "check.ps1",
        "build.ps1",
        "toolchain/change_impact.py",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs",
        ".gitattributes",
        "Core.sln",
        "core/foundation/Core.Foundation.csproj",
        "core/numbers/NumericGuard.cs",  # 层根文件：共享面
    ],
)
def test_repro_shared_surface_is_t3_and_runs_everything(mmap: dict, path: str) -> None:
    plan = _plan(mmap, path)
    assert plan["level"] == "T3", path
    assert _skip_ids(plan) == set()
    assert plan["dotnet_test"]["mode"] == "solution"
    assert plan["engine"]["mode"] == "all"


def test_repro_tests_inside_shared_dirs_are_layer_t1(mmap: dict) -> None:
    # 偏离字面 spec 的一处细化（见 module_map.json tier_rules.shared_tests_globs 与 ADR-0126）：
    # assembly/common 目录里的 tests/ 子树只影响所在层的测试工程。
    plan = _plan(mmap, "core/gameplay/assembly/tests/EndToEnd.cs", "presentation/common/tests/NullGuardMethodTests.cs")
    assert plan["level"] == "T1"
    assert plan["modules"] == []
    assert plan["dotnet_test"]["layers"] == ["gameplay", "presentation"]
    # 共享目录里的生产代码仍是 T3
    assert _plan(mmap, "core/gameplay/assembly/tests/X.cs", "core/gameplay/assembly/Y.cs")["level"] == "T3"


def test_repro_sim_tests_data_is_t1_but_triggers_sim_data_validation_and_baseline(mmap: dict) -> None:
    plan = _plan(mmap, "core/sim/tests/data/found/x.json")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["projects"] == ["core/sim/tests/Tests.Sim.csproj"]
    assert {"validate_sim_data", "sim_baseline"} <= _run_ids(plan)
    assert "validate_merged" in _skip_ids(plan)


def test_repro_md_in_t1_change_triggers_docs_pytest_but_not_other_md_triggers(mmap: dict) -> None:
    plan = _plan(mmap, "presentation/ui/core/Foo.cs", "presentation/ui/README.md")
    assert plan["level"] == "T1"
    assert "docs_pytest" in _run_ids(plan)
    # README.md 在 core/** 之外；换成 core 下的 md，不应因为 md 触发仿真基线
    plan2 = _plan(mmap, "presentation/ui/core/Foo.cs", "core/rules/skill/README.md")
    assert "sim_baseline" in _skip_ids(plan2)


def test_repro_manual_modules_are_t1_and_unknown_module_is_rejected(mmap: dict) -> None:
    plan = _plan(mmap, modules=("unit", "render"))
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["layers"] == ["carriers", "presentation"]
    with pytest.raises(ci.ImpactError):
        _plan(mmap, modules=("no_such_module",))


def test_repro_empty_change_is_t0_with_note(mmap: dict) -> None:
    plan = _plan(mmap)
    assert plan["level"] == "T0" and plan["empty"] is True and plan["notes"]


# ---------------------------------------------------------------------------
# 不变量
# ---------------------------------------------------------------------------


def _path_pool(mmap: dict) -> list[str]:
    """覆盖四个级别、各类规则的样本路径池（含未知路径）。"""
    pool = [
        "README.md", "docs/a.md", "architecture/x.md", "CHANGELOG.md",
        "core/foundation/event_bus/core/EventBus.cs",
        "core/foundation/event_bus/contracts/IEventBus.cs",
        "core/foundation/event_bus/schema/found.event_catalog.md",
        "core/foundation/event_bus/generated/EventKeys.g.cs",
        "core/foundation/event_bus/tests/EventBusTests.cs",
        "core/carriers/unit/core/MovementTickHandler.cs",
        "core/sim/core/FightRunner.cs", "core/sim/schema/SimSchemas.cs", "core/sim/tests/data/x.json",
        "presentation/render/core/X.cs", "presentation/ui/contracts/IUi.cs",
        "core/gameplay/assembly/tests/T.cs", "core/gameplay/assembly/A.cs", "core/foundation/common/core/B.cs",
        "data/_framework/x.json", "toolchain/x.py", "check.ps1", "adapters/stub/S.cs",
        "adapters/unity/Packages/p/Runtime/R.cs", "adapters/unity/Packages/p/Tests/Runtime/T.cs",
        "totally/unknown/file.bin", ".github/workflows/x.yml", "games/_template/x.cs", "assets/a.png",
    ]
    for m in mmap["modules"]:
        pool.append(f"{m['path']}core/X.cs")
        pool.append(f"{m['path']}contracts/IX.cs")
    return pool


def test_invariant_level_is_monotonic_under_adding_paths(mmap: dict) -> None:
    pool = _path_pool(mmap)
    rng = random.Random(20261001)
    for _ in range(300):
        base = rng.sample(pool, rng.randint(0, 6))
        extra = rng.choice(pool)
        lo = ci.plan_from_paths(base, mmap)["level_rank"]
        hi = ci.plan_from_paths(base + [extra], mmap)["level_rank"]
        assert hi >= lo, (base, extra)
    # 单路径：集合级别 = 各路径级别的最大值
    for combo in itertools.combinations(pool[:12], 2):
        ranks = [ci.plan_from_paths([p], mmap)["level_rank"] for p in combo]
        assert ci.plan_from_paths(list(combo), mmap)["level_rank"] == max(ranks)


def test_invariant_unknown_path_is_always_t3(mmap: dict) -> None:
    rng = random.Random(7)
    unknown_dirs = ["zzz", "weird/place", "core", "presentation", "adapters/unity/Assets", "games", ".config"]
    for _ in range(100):
        p = f"{rng.choice(unknown_dirs)}/{rng.choice('abcdef')}{rng.randint(0, 99)}.{rng.choice(['cs', 'json', 'bin', 'txt', 'png'])}"
        # core/<随机>/ 若恰好撞上已登记模块目录会被判为模块内部，这里只取确定不属于任何模块的路径
        if any(p.startswith(m["path"]) for m in mmap["modules"]):
            continue
        plan = ci.plan_from_paths([p], mmap)
        assert plan["level"] == "T3", p
        # 任何已知规则都不应把"未知 + 文档"之外的东西降级：未知路径 + T0 路径仍是 T3
        assert ci.plan_from_paths([p, "README.md"], mmap)["level"] == "T3"


def test_invariant_t3_runs_all_steps_and_lower_levels_skip_with_level_reason(mmap: dict) -> None:
    all_ids = {s["id"] for s in mmap["steps"]}
    t3 = _plan(mmap, "check.ps1")
    assert _run_ids(t3) == all_ids
    for path in ("README.md", "core/foundation/event_bus/core/EventBus.cs", "core/foundation/event_bus/contracts/IEventBus.cs"):
        plan = _plan(mmap, path)
        assert _run_ids(plan) | _skip_ids(plan) == all_ids
        assert not (_run_ids(plan) & _skip_ids(plan))
        for s in plan["steps"]["skip"]:
            assert s["reason"] == f"{plan['level']} 未触发"


def test_invariant_dependencies_are_pulled_in(mmap: dict) -> None:
    deps = {s["id"]: s.get("requires", []) for s in mmap["steps"]}
    for path in _path_pool(mmap):
        plan = _plan(mmap, path)
        run = _run_ids(plan)
        for sid in run:
            for dep in deps[sid]:
                assert dep in run, (path, sid, dep)


def test_invariant_engine_filter_never_empty_when_mode_filtered(mmap: dict) -> None:
    for path in _path_pool(mmap):
        plan = _plan(mmap, path)
        if plan["engine"]["mode"] == "filtered":
            assert plan["engine"]["playmode_filter"]
        else:
            assert plan["engine"]["playmode_filter"] == ""


def test_invariant_step_ids_in_scripts_match_module_map(mmap: dict) -> None:
    """脚本里每个 -Id 都在模块表登记，表里每个 id 都在脚本里被用到（防漂移）。"""
    sources = [REPO_ROOT / "check.ps1", TOOLCHAIN_DIR / "_gate_line_heavy.ps1", TOOLCHAIN_DIR / "_gate_line_unity.ps1"]
    used: set[str] = set()
    for src in sources:
        used.update(re.findall(r'-Id "([a-z0-9_]+)"', src.read_text(encoding="utf-8-sig")))
    registered = {s["id"] for s in mmap["steps"]}
    assert used - registered == set(), f"脚本里有未在模块表登记的步骤 id：{used - registered}"
    assert registered - used == set(), f"模块表登记了脚本里不存在的步骤 id：{registered - used}"


# ---------------------------------------------------------------------------
# 模块表自检
# ---------------------------------------------------------------------------


def test_module_map_matches_directories_in_real_repo() -> None:
    data = gmm.load_map(gmm.DEFAULT_MAP)
    assert gmm.check_map(REPO_ROOT, data) == []


def _make_fake_repo(tmp_path: Path, mmap: dict) -> Path:
    repo = tmp_path / "repo"
    for layer in mmap["layers"].values():
        tp = repo / layer["test_project"]
        tp.parent.mkdir(parents=True, exist_ok=True)
        tp.write_text("<Project/>", encoding="utf-8")
    for m in mmap["modules"]:
        d = repo / m["path"] / "core"
        d.mkdir(parents=True, exist_ok=True)
        (d / "X.cs").write_text("// x\n", encoding="utf-8")
    for m in mmap["modules"]:
        for exc in m.get("interaction_exceptions", []):
            for wp in exc["when_paths"]:
                f = repo / wp
                f.parent.mkdir(parents=True, exist_ok=True)
                f.write_text("// x\n", encoding="utf-8")
    return repo


def test_module_map_check_flags_unregistered_and_ghost_dirs(tmp_path: Path, mmap: dict) -> None:
    repo = _make_fake_repo(tmp_path, mmap)
    assert gmm.check_map(repo, mmap) == []
    # 新增一个未登记的子模块目录
    (repo / "core/gameplay/brand_new/core").mkdir(parents=True)
    (repo / "core/gameplay/brand_new/core/Y.cs").write_text("// y\n", encoding="utf-8")
    problems = gmm.check_map(repo, mmap)
    assert any("未登记" in p and "brand_new" in p for p in problems), problems
    # common/assembly/tests/bin/obj 不算子模块
    for name in ("common", "assembly", "tests", "bin", "obj"):
        (repo / f"core/gameplay/{name}").mkdir(parents=True, exist_ok=True)
        (repo / f"core/gameplay/{name}/Z.cs").write_text("// z\n", encoding="utf-8")
    assert [p for p in gmm.check_map(repo, mmap) if "未登记" in p] == [p for p in problems if "未登记" in p]
    # 幽灵登记：表里有、目录没有
    shutil.rmtree(repo / "core/gameplay/brand_new")
    shutil.rmtree(repo / "core/gameplay/quest")
    problems = gmm.check_map(repo, mmap)
    assert any("不存在的目录" in p and "core/gameplay/quest/" in p for p in problems), problems


def test_gen_module_map_preserves_manual_fields_and_adds_tbd(tmp_path: Path, mmap: dict) -> None:
    repo = _make_fake_repo(tmp_path, mmap)
    (repo / "core/gameplay/brand_new/core").mkdir(parents=True)
    (repo / "core/gameplay/brand_new/core/Y.cs").write_text("// y\n", encoding="utf-8")
    map_copy = tmp_path / "map.json"
    data = json.loads(json.dumps(mmap))
    for m in data["modules"]:
        if m["name"] == "quest":
            m["owner"] = "alice"
    map_copy.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    rc = gmm.main(["--map", str(map_copy), "--repo-root", str(repo)])
    assert rc == 0
    out = json.loads(map_copy.read_text(encoding="utf-8"))
    by = {m["name"]: m for m in out["modules"]}
    assert by["quest"]["owner"] == "alice"
    assert by["brand_new"]["owner"] == "TBD" and by["brand_new"]["engine_category"] == "module:brand_new"
    assert by["unit"]["interaction_exceptions"], "人工填的交互例外必须保留"
    assert gmm.check_map(repo, out) == []


# ---------------------------------------------------------------------------
# 引擎侧 PlayMode 测试类全部打了模块分类
# ---------------------------------------------------------------------------

_TEST_ATTR = re.compile(r"^\s*\[(UnityTest|Test|TestCase)\b", re.MULTILINE)
_CATEGORY_ATTR = re.compile(r'\[Category\("([^"]+)"\)\]')


def test_every_playmode_test_file_has_a_module_category(mmap: dict) -> None:
    known = {m["engine_category"] for m in mmap["modules"]} | {mmap["engine"]["shared_category"]}
    interaction = {
        exc["also_run"]["engine_category"]
        for m in mmap["modules"]
        for exc in m.get("interaction_exceptions", [])
        if exc["also_run"].get("engine_category")
    }
    missing: list[str] = []
    bad: list[str] = []
    seen_files = 0
    for f in sorted(PLAYMODE_TESTS_DIR.glob("*.cs")):
        text = f.read_text(encoding="utf-8-sig")
        if not _TEST_ATTR.search(text):
            continue
        seen_files += 1
        cats = _CATEGORY_ATTR.findall(text)
        module_cats = [c for c in cats if c.startswith(mmap["engine"]["category_prefix"])]
        if not module_cats:
            missing.append(f.name)
        for c in cats:
            if c not in known and c not in interaction:
                bad.append(f"{f.name}:{c}")
    assert seen_files > 40, "PlayMode 测试文件数异常少，目录路径是否变了？"
    assert missing == [], f"这些 PlayMode 测试文件没有 [Category(\"module:...\")]：{missing}"
    assert bad == [], f"出现模块表里没有的分类名：{bad}"


def test_every_test_class_in_playmode_files_has_a_category() -> None:
    """文件级之外再卡一层：含测试方法的每个 class 声明前的属性块里都要有 module 分类。"""
    class_decl = re.compile(r"^(?P<attrs>(?:[ \t]*\[[^\n]*\]\s*\n|[ \t]*///[^\n]*\n)*)[ \t]*(?:public|internal)?\s*(?:sealed |abstract |static )*class (?P<name>\w+)", re.MULTILINE)
    offenders: list[str] = []
    for f in sorted(PLAYMODE_TESTS_DIR.glob("*.cs")):
        text = f.read_text(encoding="utf-8-sig")
        matches = list(class_decl.finditer(text))
        for i, m in enumerate(matches):
            end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
            body = text[m.end():end]
            if _TEST_ATTR.search(body) and "module:" not in m.group("attrs"):
                offenders.append(f"{f.name}:{m.group('name')}")
    assert offenders == [], f"含测试方法的类缺 [Category(\"module:...\")]：{offenders}"


def test_movement_exception_class_carries_the_interaction_category(mmap: dict) -> None:
    unit = next(m for m in mmap["modules"] if m["name"] == "unit")
    exc = unit["interaction_exceptions"][0]
    text = (PLAYMODE_TESTS_DIR / f"{exc['also_run']['engine_class']}.cs").read_text(encoding="utf-8-sig")
    assert f'[Category("{exc["also_run"]["engine_category"]}")]' in text


# ---------------------------------------------------------------------------
# check.ps1 -DryRun 真的把引擎侧分类过滤串打出来（临时 git 仓库，不动真实仓库）
# ---------------------------------------------------------------------------


def _find_powershell() -> str | None:
    return shutil.which("powershell") or shutil.which("pwsh")


@pytest.mark.skipif(_find_powershell() is None, reason="本机没有 PowerShell")
def test_check_dryrun_prints_playmode_category_filter(tmp_path: Path, mmap: dict) -> None:
    repo = tmp_path / "fake_repo"
    (repo / "toolchain").mkdir(parents=True)
    shutil.copy(REPO_ROOT / "check.ps1", repo / "check.ps1")
    for name in ("change_impact.py", "_console.py", "module_map.json", "_gate_timing.ps1"):
        shutil.copy(TOOLCHAIN_DIR / name, repo / "toolchain" / name)

    def git(*args: str) -> None:
        r = subprocess.run(["git", *args], cwd=str(repo), capture_output=True, text=True, encoding="utf-8", errors="replace")
        assert r.returncode == 0, r.stderr

    git("init", "-q")
    git("config", "user.email", "t@example.com")
    git("config", "user.name", "T")
    git("add", "-A")
    git("commit", "-q", "-m", "init")
    nav = repo / "core/foundation/engine_adapter/contracts/INavigation2D.cs"
    nav.parent.mkdir(parents=True)
    nav.write_text("// contract\n", encoding="utf-8")
    git("add", "core/foundation/engine_adapter/contracts/INavigation2D.cs")

    ps = _find_powershell()
    assert ps is not None
    result = subprocess.run(
        [ps, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(repo / "check.ps1"), "-Staged", "-DryRun"],
        cwd=str(repo), capture_output=True, env=clean_powershell_env(ps), timeout=120,
    )
    out = result.stdout.decode("utf-8", errors="replace") + result.stdout.decode("gbk", errors="replace")
    assert result.returncode == 0, out + result.stderr.decode("utf-8", errors="replace")
    plan = json.loads((repo / "bin/_check_artifacts/change_impact_plan.json").read_text(encoding="utf-8"))
    assert plan["level"] == "T2"
    assert plan["engine"]["playmode_filter"] == "module:engine_adapter;module:shared;interaction:movement_stop_blocking"
    # 控制台上也确实打印了（PowerShell 输出代码页不定，只核对 ASCII 部分）
    assert '-testCategory "module:engine_adapter;module:shared;interaction:movement_stop_blocking"' in out
    assert "-DryRun" in out
    # DryRun 不执行任何步骤：没有步骤日志
    assert not (repo / "bin/_check_artifacts/check_targeted.log").exists()
