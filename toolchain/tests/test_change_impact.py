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
from _git_env import git_env, init_temp_repo, run_git  # noqa: E402
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
        "data/_framework/found/found.event_catalog.json",
        "Directory.Build.props",
        "adapters/stub/StubNavigation2D.cs",
        "adapters/stub/LayerMarker.cs",
        "check.ps1",
        "build.ps1",
        "toolchain/change_impact.py",
        ".gitattributes",
        "Core.sln",
        "core/foundation/Core.Foundation.csproj",
        "core/numbers/Core.Numbers.csproj",  # 层根 csproj：层级规则的 except 守住，仍归共享面
        "presentation/Presentation.Common.csproj",
        "adapters/unity/DiagnosticsForwarding/Adapters.Unity.DiagnosticsForwarding.csproj",
        "adapters/unity/Assets/Editor/GreyBoxSceneBuilder.cs",  # 未登记的引擎侧工作台工程内容：保守 T3
        "totally/unknown/file.bin",
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
    # 共享目录里的生产代码是层级范围（T2），不再是 T3；测试与生产代码同改时取最高级 T2
    assert _plan(mmap, "core/gameplay/assembly/tests/X.cs", "core/gameplay/assembly/Y.cs")["level"] == "T2"


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
# 复现用例：路径规则（层级范围、适配层、模板、资产、钩子……，ADR-0126 层级范围与适配层判级）
# ---------------------------------------------------------------------------

PKG = "adapters/unity/Packages/com.gamefoundation.adapter.unity"
TESTS_FOUNDATION = "core/foundation/tests/Tests.Foundation.csproj"
TESTS_DIAG = "adapters/unity/DiagnosticsForwarding/tests/Tests.Adapters.Unity.DiagnosticsForwarding.csproj"


def _filter_set(plan: dict) -> set[str]:
    return set(filter(None, plan["engine"]["playmode_filter"].split(";")))


def test_repro_layer_root_file_is_layer_level_t2(mmap: dict) -> None:
    # 复现：此前 core/numbers/NumericGuard.cs（层根文件）被判 T3 全量。层级范围：本层测试工程 + 下游一层 +
    # ABI 探针 + 本层所有模块的引擎侧分类，仍记 T2。
    plan = _plan(mmap, "core/numbers/NumericGuard.cs")
    assert plan["level"] == "T2"
    assert plan["dotnet_test"] == {
        "mode": "projects",
        "projects": ["core/numbers/tests/Tests.Numbers.csproj", "core/rules/tests/Tests.Rules.csproj"],
        "layers": ["numbers", "rules"],
    }
    assert {"dotnet_build", "dotnet_test", "abi_probe", "sim_baseline", "sync_dll", "unity_playmode"} <= _run_ids(plan)
    numbers_modules = {m["engine_category"] for m in mmap["modules"] if m["layer"] == "numbers"}
    assert numbers_modules and _filter_set(plan) == numbers_modules | {"module:shared"}
    assert plan["engine"]["mode"] == "filtered"
    assert "toolchain_pytest" in _skip_ids(plan)


@pytest.mark.parametrize(
    "path,layers",
    [
        ("core/gameplay/assembly/GameplayAssembly.cs", ["gameplay", "sim", "presentation"]),
        ("core/foundation/common/contracts/Id.cs", ["foundation", "numbers"]),
        ("core/rules/assembly/RulesAssembly.cs", ["rules", "carriers"]),
        ("core/foundation/LayerMarker.cs", ["foundation", "numbers"]),
        ("presentation/common/core/ResourceReferenceTracker.cs", ["presentation", "lab"]),
        ("presentation/assembly/PresentationAssembly.cs", ["presentation", "lab"]),
        ("presentation/LayerMarker.cs", ["presentation", "lab"]),
    ],
)
def test_repro_layer_shared_surface_runs_own_layer_plus_one_downstream(mmap: dict, path: str, layers: list[str]) -> None:
    plan = _plan(mmap, path)
    assert plan["level"] == "T2", path
    assert plan["dotnet_test"]["layers"] == layers, path
    assert "abi_probe" in _run_ids(plan)
    assert plan["engine"]["mode"] == "filtered" and "module:shared" in _filter_set(plan)


def test_repro_stub_tests_are_t1_foundation_tests_but_stub_code_stays_t3(mmap: dict) -> None:
    plan = _plan(mmap, "adapters/stub/tests/StubAudioTests.cs")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["projects"] == [TESTS_FOUNDATION]  # 只被 Tests.Foundation 编译
    assert plan["engine"]["mode"] == "none"
    # 被所有测试工程引用的桩生产代码仍是共享面
    assert _plan(mmap, "adapters/stub/StubAudio.cs")["level"] == "T3"


def test_repro_conformance_is_t2_with_foundation_tests_and_engine_adapter_category(mmap: dict) -> None:
    plan = _plan(mmap, "adapters/conformance/Runtime/AudioScenarios.cs")
    assert plan["level"] == "T2"
    assert plan["dotnet_test"]["projects"] == [TESTS_FOUNDATION]
    assert plan["engine"]["playmode_filter"] == "module:engine_adapter"
    assert {"unity_compile", "unity_editmode", "unity_playmode", "sync_dll", "unity_meta"} <= _run_ids(plan)
    # 不是层级公开面：没有下游一层、没有 ABI 探针、不跑消费方演练（一致性套件不进 dist 分发包）
    assert {"abi_probe", "consumer_drill", "sim_baseline"} <= _skip_ids(plan)


def test_repro_engine_runtime_test_runs_only_its_own_category_without_dotnet(mmap: dict) -> None:
    plan = _plan(mmap, f"{PKG}/Tests/Runtime/UiSuiteTests.cs")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"] == {"mode": "none", "projects": [], "layers": []}
    assert plan["engine"]["mode"] == "filtered"
    assert plan["engine"]["playmode_filter"] == "module:ui"
    run = _run_ids(plan)
    assert "unity_playmode" in run
    # 没有 dotnet 步骤，也不同步 DLL、不做 Unity 编译/EditMode/演练
    assert not ({"dotnet_build", "dotnet_test", "abi_probe", "sim_baseline", "sync_dll", "unity_compile", "unity_editmode", "consumer_drill"} & run)
    assert plan["engine"]["dll_sync"] is False


def test_repro_engine_runtime_test_with_two_categories_and_meta_and_helper(mmap: dict) -> None:
    both = _plan(mmap, f"{PKG}/Tests/Runtime/MovementStopAndBlockingPlayModeTests.cs")
    assert _filter_set(both) == {"interaction:movement_stop_blocking", "module:unit"}
    # .meta 取所属源文件的分类
    assert _plan(mmap, f"{PKG}/Tests/Runtime/UiSuiteTests.cs.meta")["engine"]["playmode_filter"] == "module:ui"
    # 辅助类（没有任何分类标注）看不出被哪些用例用，保守跑全部 PlayMode，但仍无 dotnet
    helper = _plan(mmap, f"{PKG}/Tests/Runtime/CombatStanceAnimFixture.cs")
    assert helper["level"] == "T1" and helper["engine"]["mode"] == "all" and helper["engine"]["playmode_filter"] == ""
    assert helper["dotnet_test"]["mode"] == "none"
    # 文件已删除（回放旧提交/删除测试）：不贡献分类，不凭空要求全量
    gone = _plan(mmap, f"{PKG}/Tests/Runtime/NoSuchTests.cs")
    assert gone["engine"]["mode"] == "none" and any("不存在" in n for n in gone["notes"])


def test_repro_engine_editmode_test_runs_editmode_only(mmap: dict) -> None:
    plan = _plan(mmap, f"{PKG}/Tests/Editor/UnityClockTests.cs")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["mode"] == "none"
    assert plan["engine"]["mode"] == "none"
    assert "unity_editmode" in _run_ids(plan)
    assert not ({"unity_playmode", "sync_dll", "dotnet_build", "consumer_drill"} & _run_ids(plan))


def test_repro_engine_runtime_code_is_t2_with_compile_category_drill_and_sync(mmap: dict) -> None:
    ui = _plan(mmap, f"{PKG}/Runtime/Ui/UiRoot.cs")
    assert ui["level"] == "T2"
    assert _filter_set(ui) == {"module:ui", "module:shell", "module:shared"}
    assert {"unity_compile", "unity_playmode", "consumer_drill", "sync_dll", "pkg_manifest", "unity_meta"} <= _run_ids(ui)
    assert ui["dotnet_test"]["mode"] == "none" and "abi_probe" in _skip_ids(ui)
    # 目录面太宽的运行时代码：全部 PlayMode
    wide = _plan(mmap, f"{PKG}/Runtime/EngineAdapter/UnityNavigation2D.cs")
    assert wide["level"] == "T2" and wide["engine"]["mode"] == "all"
    assert {"unity_compile", "unity_playmode", "consumer_drill", "sync_dll"} <= _run_ids(wide)
    # 被转发测试工程按引用编译的运行时源文件：另跑该 dotnet 测试工程
    fwd = _plan(mmap, f"{PKG}/Runtime/Diagnostics/DiagnosticsHub.cs")
    assert fwd["dotnet_test"]["projects"] == [TESTS_DIAG]
    assert {"dotnet_build", "dotnet_test"} <= _run_ids(fwd)
    assert _filter_set(fwd) == {"module:shared"}
    # 编辑器代码：EditMode + 编译
    ed = _plan(mmap, f"{PKG}/Editor/EditorSetupTypes.cs")
    assert ed["level"] == "T2" and {"unity_compile", "unity_editmode", "consumer_drill"} <= _run_ids(ed)


def test_repro_diag_forwarding_project_tests_are_t1_and_test_csproj_t2_but_production_csproj_is_shared(mmap: dict) -> None:
    plan = _plan(mmap, "adapters/unity/DiagnosticsForwarding/tests/DiagnosticsHubTests.cs")
    assert plan["level"] == "T1" and plan["dotnet_test"]["projects"] == [TESTS_DIAG]
    # 测试工程文件：不在任何核心层里，只跑它自己的测试工程（T2，无 ABI 探针）
    csproj = _plan(mmap, "adapters/unity/DiagnosticsForwarding/tests/Tests.Adapters.Unity.DiagnosticsForwarding.csproj")
    assert csproj["level"] == "T2" and csproj["dotnet_test"]["projects"] == [TESTS_DIAG]
    assert csproj["public_layers"] == [] and "abi_probe" not in _run_ids(csproj)
    # 生产工程文件仍是共享面 T3
    assert _plan(mmap, "adapters/unity/DiagnosticsForwarding/Adapters.Unity.DiagnosticsForwarding.csproj")["level"] == "T3"


def test_repro_toolchain_tests_and_floors_are_t1_toolchain_pytest_only(mmap: dict) -> None:
    # 复现：此前 toolchain/tests/**、gate_floors.json 归共享面 T3（全量门禁）。只改 pytest 用例或下限数，只需 toolchain_pytest。
    for path in ("toolchain/tests/test_change_impact.py", "toolchain/tests/conftest.py"):
        plan = _plan(mmap, path)
        assert plan["level"] == "T1", path
        assert plan["dotnet_test"]["mode"] == "none" and plan["engine"]["mode"] == "none", path
        ids = _run_ids(plan)
        assert {"toolchain_pytest", "self_check", "module_map_check", "crlf_check"} <= ids, path
        assert not ({"dotnet_build", "dotnet_test", "abi_probe", "sync_dll", "sim_baseline", "registry_pytest", "unity_playmode"} & ids), path
    # 单独改私服回归用例时顺带跑它自己的隔离步骤
    assert "registry_pytest" in _run_ids(_plan(mmap, "toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py"))
    # toolchain/ 下其它路径仍是共享面 T3
    for path in ("toolchain/change_impact.py", "toolchain/module_map.json", "toolchain/_gate_line_heavy.ps1", "toolchain/validator/Validator.csproj"):
        assert _plan(mmap, path)["level"] == "T3", path
    # 与别的 T1 改动同批仍是 T1；与 T3 路径同批仍是 T3
    assert _plan(mmap, "toolchain/gate_floors.json", "core/foundation/event_bus/Impl.cs")["level"] == "T1"
    assert _plan(mmap, "toolchain/tests/conftest.py", "toolchain/change_impact.py")["level"] == "T3"


def test_repro_gate_floors_only_runs_only_the_floors_test_not_full_pytest(mmap: dict) -> None:
    """复现（2026-10-05 发布提速，ADR-0156）：只改 toolchain/gate_floors.json 的提交，此前判 T1 + toolchain_pytest（全量 pytest，
    钩子里约 20 分钟）。它唯一的消费者是 test_gate_floors_logic.py，所以只需跑 floors_pytest（一个文件）+ 秒级基础步骤（含文档类检查）。"""
    plan = _plan(mmap, "toolchain/gate_floors.json")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["mode"] == "none" and plan["engine"]["mode"] == "none"
    ids = _run_ids(plan)
    assert "floors_pytest" in ids
    assert "toolchain_pytest" not in ids, "gate_floors.json 单独改动不得再跑 toolchain 全量 pytest"
    assert {"self_check", "ban_codename", "ban_arch_terms", "version_consistency", "module_map_check"} <= ids
    heavy = {"dotnet_build", "dotnet_test", "abi_probe", "sim_baseline", "sync_dll", "registry_pytest", "unity_playmode",
             "pkg_manifest", "consumer_drill", "unity_compile"}
    assert not (heavy & ids)


def test_invariant_floors_pytest_runs_only_with_gate_floors_and_full_pytest_still_runs_for_other_toolchain_changes(mmap: dict) -> None:
    """不变量：floors_pytest 只因 gate_floors.json 出现；gate_floors.json 与 toolchain/tests 改动同批时两个都跑（全量 pytest 不丢）；
    toolchain 下其它路径（T3 全量）里 toolchain_pytest 照旧在。"""
    both = _plan(mmap, "toolchain/gate_floors.json", "toolchain/tests/conftest.py")
    assert {"floors_pytest", "toolchain_pytest"} <= _run_ids(both)
    assert "floors_pytest" not in _run_ids(_plan(mmap, "toolchain/tests/conftest.py"))
    t3 = _plan(mmap, "toolchain/gate_floors.json", "toolchain/change_impact.py")
    assert t3["level"] == "T3" and "toolchain_pytest" in _run_ids(t3)


def test_repro_release_version_files_are_t1_with_version_sensitive_steps_only(mmap: dict) -> None:
    """复现（2026-10-05 发布提速，ADR-0156）：build.ps1 -Release 第 4 步写回的四个版本文件此前"未被任何规则覆盖"，一律保守判 T3，
    使发布第 5 步的 `check.ps1 -Changed <全量记录提交>` 退化成全量。现在记 T1，只跑受版本号影响的步骤。"""
    files = [
        "VERSION",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json",
        "adapters/unity/Packages/packages-lock.json",
        "games/_template/package.json",
    ]
    for f in files:
        plan = _plan(mmap, f)
        assert plan["level"] == "T1", f
        assert plan["dotnet_test"]["mode"] == "none", f
        assert plan["engine"]["mode"] == "none", f
        ids = _run_ids(plan)
        assert {"pkg_manifest", "sync_dll", "unity_compile", "consumer_drill", "unity_meta", "version_consistency"} <= ids, f
        assert not ({"dotnet_build", "dotnet_test", "abi_probe", "sim_baseline", "toolchain_pytest", "unity_playmode"} & ids), f
    # 整批写回 + 文档类改动（发布第 5 步实际看到的改动集）仍是 T1，不会因为文档改动升级。
    batch = _plan(mmap, *files, "CHANGELOG.md", "REGRESSION_LOG.md", "docs/复盘/x.md", "timing/20261005_x.jsonl")
    assert batch["level"] == "T1"
    assert {"pkg_manifest", "consumer_drill", "docs_pytest"} <= _run_ids(batch)


def test_invariant_release_version_files_do_not_hide_code_changes(mmap: dict) -> None:
    """不变量：版本文件规则只覆盖这四个确切路径；同批里出现任何代码/共享面改动，级别与步骤照常升高（不会被版本文件规则吞掉）。"""
    assert _plan(mmap, "VERSION", "core/foundation/event_bus/Impl.cs")["level"] == "T1"
    assert _plan(mmap, "VERSION", "build.ps1")["level"] == "T3"
    assert _plan(mmap, "VERSION", "adapters/unity/Packages/manifest.json")["level"] == "T3"
    assert _plan(mmap, "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Ui/X.cs", "VERSION")["level"] == "T2"


@pytest.mark.parametrize(
    "layer,own,downstream",
    [
        ("foundation", "core/foundation/tests/Tests.Foundation.csproj", ["core/numbers/tests/Tests.Numbers.csproj"]),
        ("numbers", "core/numbers/tests/Tests.Numbers.csproj", ["core/rules/tests/Tests.Rules.csproj"]),
        ("rules", "core/rules/tests/Tests.Rules.csproj", ["core/carriers/tests/Tests.Carriers.csproj"]),
        ("carriers", "core/carriers/tests/Tests.Carriers.csproj", ["core/gameplay/tests/Tests.Gameplay.csproj"]),
        ("gameplay", "core/gameplay/tests/Tests.Gameplay.csproj", ["core/sim/tests/Tests.Sim.csproj", "presentation/tests/Tests.PresentationCommon.csproj"]),
        ("sim", "core/sim/tests/Tests.Sim.csproj", ["lab/tests/Tests.Lab.csproj"]),
        ("presentation", "presentation/tests/Tests.PresentationCommon.csproj", ["lab/tests/Tests.Lab.csproj"]),
        ("lab", "lab/tests/Tests.Lab.csproj", []),
    ],
)
def test_repro_layer_test_csproj_is_layer_level_t2(mmap: dict, layer: str, own: str, downstream: list[str]) -> None:
    # 复现：此前任何测试工程 csproj 都归共享面 T3（全量）。按该测试工程所在层的层级范围记 T2：
    # 本层测试工程 + 下游一层 + ABI 探针 + 本层所有模块的引擎侧分类。
    plan = _plan(mmap, own)
    assert plan["level"] == "T2", own
    assert plan["layers"] == [layer] and plan["public_layers"] == [layer]
    assert plan["dotnet_test"]["mode"] == "projects"
    assert set(plan["dotnet_test"]["projects"]) == {own, *downstream}
    assert {"dotnet_build", "dotnet_test", "abi_probe"} <= _run_ids(plan)
    assert plan["rules"] == ["layer_test_projects"]
    # 同层的生产 csproj 仍是 T3
    prod = {
        "foundation": "core/foundation/Core.Foundation.csproj",
        "numbers": "core/numbers/Core.Numbers.csproj",
        "rules": "core/rules/Core.Rules.csproj",
        "carriers": "core/carriers/Core.Carriers.csproj",
        "gameplay": "core/gameplay/Core.Gameplay.csproj",
        "sim": "core/sim/Core.Sim.csproj",
        "presentation": "presentation/Presentation.Common.csproj",
        "lab": "lab/Lab.Kernel.csproj",
    }[layer]
    assert _plan(mmap, prod)["level"] == "T3", prod


def test_repro_sln_and_build_props_stay_t3_after_test_csproj_rule(mmap: dict) -> None:
    for path in ("Core.sln", "Directory.Build.props", "adapters/stub/Adapters.Stub.csproj"):
        plan = _plan(mmap, path)
        assert plan["level"] == "T3" and plan["dotnet_test"]["mode"] == "solution", path


def test_repro_game_template_is_t1_with_template_validation_and_drill(mmap: dict) -> None:
    plan = _plan(mmap, "games/_template/data/game/rules/example.json")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["mode"] == "none" and plan["engine"]["mode"] == "none"
    run = _run_ids(plan)
    assert {"validate_template_data", "consumer_drill", "unity_meta", "pkg_manifest"} <= run
    assert not ({"dotnet_build", "dotnet_test", "validate_merged", "toolchain_pytest", "sync_dll"} & run)


def test_repro_assets_is_t1_with_placeholder_and_sample_import_checks(mmap: dict) -> None:
    plan = _plan(mmap, "assets/_sample/icons/a.png")
    assert plan["level"] == "T1"
    run = _run_ids(plan)
    assert {"placeholder_assets", "sample_import_idem", "import_assets_check"} <= run
    assert not ({"dotnet_build", "dotnet_test", "unity_meta", "toolchain_pytest"} & run)


def test_repro_github_workflows_are_t0_hooks_and_gitignore_are_t1_hooks_pytest(mmap: dict) -> None:
    gh = _plan(mmap, ".github/workflows/ci.yml")
    assert gh["level"] == "T0"
    assert _run_ids(gh) == {"self_check", "ban_codename", "ban_arch_terms", "version_consistency", "branch_name", "docs_pytest"}
    for path in (".githooks/pre-commit", ".gitignore"):
        plan = _plan(mmap, path)
        assert plan["level"] == "T1", path
        run = _run_ids(plan)
        assert {"self_check", "hooks_pytest"} <= run, path
        assert not ({"dotnet_build", "dotnet_test", "toolchain_pytest", "unity_playmode", "sync_dll"} & run), path
        assert plan["dotnet_test"]["mode"] == "none" and plan["engine"]["mode"] == "none"


def test_repro_shared_perf_calibration_file_also_runs_tests_sim(mmap: dict) -> None:
    # 复现（用「所列测试工程含自己所在测试工程」不变量扫仓库时发现）：Tests.Sim.csproj 用 Link 直接编译
    # core/gameplay/tests/Perf/PerfMachineCalibration.cs，该文件在 gameplay 测试工程目录里，只判 Tests.Gameplay 会漏跑 Tests.Sim。
    plan = _plan(mmap, "core/gameplay/tests/Perf/PerfMachineCalibration.cs")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["projects"] == ["core/gameplay/tests/Tests.Gameplay.csproj", "core/sim/tests/Tests.Sim.csproj"]


def test_repro_rule_driven_t2_does_not_add_module_shared_unless_layer_or_module_scope(mmap: dict) -> None:
    # 层级/模块公开面 T2 才附带 module:shared（归属不明的用例）；适配层规则只带自己登记的分类。
    assert "module:shared" in _filter_set(_plan(mmap, "core/foundation/event_bus/contracts/IEventBus.cs"))
    assert _filter_set(_plan(mmap, "adapters/conformance/Runtime/AudioScenarios.cs")) == {"module:engine_adapter"}


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
        "core/numbers/NumericGuard.cs", "core/foundation/LayerMarker.cs", "presentation/common/core/B.cs",
        "adapters/conformance/Runtime/AudioScenarios.cs", "adapters/stub/tests/StubAudioTests.cs", "adapters/stub/StubAudio.cs",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/UiSuiteTests.cs",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/CombatStanceAnimFixture.cs",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Editor/UnityClockTests.cs",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Ui/UiRoot.cs",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Editor/EditorSetupTypes.cs",
        "adapters/unity/DiagnosticsForwarding/tests/DiagnosticsHubTests.cs",
        ".githooks/pre-commit", ".gitignore",
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
    # presentation/<随便>/ 与 core/<随便>/<随便>/ 是"还没登记成子模块的新目录"，同样必须是 T3（层根单文件 presentation/x.cs、
    # core/<层>/x.cs 则是层级范围的 T2，不在这里）
    unknown_dirs = ["zzz", "weird/place", "core", "presentation/zzz_unregistered", "core/zzz_layer/zzz_unregistered", "adapters/unity/Assets",
                    "adapters/unity/ProjectSettings", "adapters/headless", "games", ".config"]
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
    for rule in mmap["tier_rules"].get("path_rules", []):
        for proj in rule.get("dotnet_projects", []):
            f = repo / proj
            f.parent.mkdir(parents=True, exist_ok=True)
            f.write_text("<Project/>", encoding="utf-8")
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
    for name in ("change_impact.py", "_console.py", "module_map.json", "_gate_timing.ps1", "_gate_lock.ps1"):
        shutil.copy(TOOLCHAIN_DIR / name, repo / "toolchain" / name)

    # 走 _git_env：钩子里继承的 GIT_DIR/GIT_INDEX_FILE 会让 init/config/add 写进真实仓库（2026-10-01 事故）。
    def git(*args: str) -> None:
        run_git(repo, *args)

    init_temp_repo(repo)
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


# ---------------------------------------------------------------------------
# 不变量：路径规则登记的测试工程与 csproj 的真实引用关系一致（防漏跑自己）
# ---------------------------------------------------------------------------


def _tracked(*patterns: str) -> list[str]:
    r = subprocess.run(["git", "ls-files", "-z", "--", *patterns], cwd=str(REPO_ROOT), capture_output=True, check=True, env=git_env())
    return [p for p in r.stdout.decode("utf-8", errors="replace").split("\0") if p]


def _csproj_compile_facts(csproj_rel: str) -> dict:
    """解析一个 csproj：自己所在目录、Compile Include/Remove（换算成仓库相对 glob）、ProjectReference（仓库相对路径）。"""
    import posixpath
    import xml.etree.ElementTree as ET

    base = posixpath.dirname(csproj_rel)
    root = ET.fromstring((REPO_ROOT / csproj_rel).read_text(encoding="utf-8-sig"))

    def rel(item: str) -> str:
        return posixpath.normpath(posixpath.join(base, item.replace("\\", "/")))

    return {
        "dir": base,
        "include": [rel(e.attrib["Include"]) for e in root.iter("Compile") if "Include" in e.attrib],
        "remove": [rel(e.attrib["Remove"]) for e in root.iter("Compile") if "Remove" in e.attrib],
        "refs": [rel(e.attrib["Include"]) for e in root.iter("ProjectReference")],
    }


def _test_projects_compiling(cs_file: str, facts: dict[str, dict]) -> set[str]:
    """哪些测试工程（Tests.*.csproj）会把这个 .cs 编进自己：直接编译（默认 glob/显式 Include），或经 ProjectReference
    引用了一个用显式 Include 把该文件按引用拉进来的非测试工程（诊断转发工程的做法）。"""
    def compiles(proj: str) -> bool:
        f = facts[proj]
        explicit = any(ci.glob_match(g, cs_file) for g in f["include"])
        if explicit:
            return True
        default = cs_file.startswith(f["dir"] + "/") and not any(ci.glob_match(g, cs_file) for g in f["remove"])
        return default

    direct = {pr for pr in facts if posixpath_name(pr).startswith("Tests.") and compiles(pr)}
    for proj, f in facts.items():
        if posixpath_name(proj).startswith("Tests."):
            continue
        # 非测试工程：只认"显式 Include 到自己目录之外的源文件"（默认 glob 是它自己的生产源码，不属于某个测试工程）
        if any(ci.glob_match(g, cs_file) for g in f["include"]) and not cs_file.startswith(f["dir"] + "/"):
            for tp, tf in facts.items():
                if posixpath_name(tp).startswith("Tests.") and proj in tf["refs"]:
                    direct.add(tp)
    return direct


def posixpath_name(p: str) -> str:
    return p.rsplit("/", 1)[-1]


def test_invariant_judged_t1_t2_lists_every_test_project_that_compiles_the_changed_file(mmap: dict) -> None:
    """任何被判 T1/T2 的单文件改动，所列 dotnet 测试工程都包含"把该文件编进自己"的那些测试工程。

    期望值来自 csproj 的真实引用关系（Compile Include/Remove + ProjectReference），不是把模块表的输出抄回来；
    模块表漏登记（例如 Tests.Sim.csproj 用 Link 直接编译 gameplay 测试目录里的 PerfMachineCalibration.cs）会在这里红。
    """
    facts = {c: _csproj_compile_facts(c) for c in _tracked("*.csproj")}
    cs_files = _tracked("*.cs")
    assert len(cs_files) > 1000
    checked = 0
    offenders: list[str] = []
    for f in cs_files:
        owners = _test_projects_compiling(f, facts)
        if not owners:
            continue
        plan = _plan(mmap, f)
        if plan["level"] not in ("T1", "T2"):
            continue  # T0 不跑代码；T3 跑 Core.sln 全量（含全部测试工程）
        checked += 1
        missing = owners - set(plan["dotnet_test"]["projects"])
        if missing:
            offenders.append(f"{f} -> 漏列 {sorted(missing)}（实际列了 {plan['dotnet_test']['projects']}）")
    assert checked > 500, "被判 T1/T2 且属于某测试工程的文件数异常少，解析是否失效？"
    assert offenders == [], "\n".join(offenders[:20])


def test_invariant_conformance_rule_categories_cover_playmode_files_that_reference_conformance(mmap: dict) -> None:
    """一致性套件规则里静态登记的引擎侧分类，必须覆盖所有引用 Adapters.Conformance 的 PlayMode 测试文件的分类。"""
    rule = next(r for r in mmap["tier_rules"]["path_rules"] if r["id"] == "conformance")
    cats: set[str] = set()
    users = 0
    for f in sorted(PLAYMODE_TESTS_DIR.glob("*.cs")):
        text = f.read_text(encoding="utf-8-sig")
        if "Adapters.Conformance" in text:
            users += 1
            cats.update(_CATEGORY_ATTR.findall(text))
    assert users >= 1, "没有 PlayMode 测试引用 Adapters.Conformance，规则里登记的分类是否已过期？"
    assert cats <= set(rule["engine_categories"]), f"引用一致性套件的 PlayMode 用例分类 {sorted(cats)} 未被规则覆盖 {rule['engine_categories']}"


def test_invariant_path_rule_steps_projects_and_categories_exist(mmap: dict) -> None:
    step_ids = {s["id"] for s in mmap["steps"]}
    known_cats = {m["engine_category"] for m in mmap["modules"]} | {mmap["engine"]["shared_category"]}
    for r in mmap["tier_rules"]["path_rules"]:
        assert set(r.get("steps", [])) <= step_ids, r["id"]
        assert set(r.get("engine_categories", [])) <= known_cats, r["id"]
        for proj in r.get("dotnet_projects", []):
            assert (REPO_ROOT / proj).is_file(), (r["id"], proj)
        assert r["level"] in (1, 2), r["id"]


def test_invariant_path_rules_never_swallow_shared_surface_files(mmap: dict) -> None:
    """路径规则的 except 守住共享面：sln/Directory.Build.props 与所有生产 csproj 无论落在哪条规则的目录下都仍是 T3；
    只有测试工程文件（Tests.*.csproj）可以按所在层的层级范围记 T2。"""
    for f in _tracked("*.sln", "Directory.Build.props"):
        assert _plan(mmap, f)["level"] == "T3", f
    test_csprojs = [f for f in _tracked("*.csproj") if posixpath_name(f).startswith("Tests.")]
    assert len(test_csprojs) >= 8
    for f in _tracked("*.csproj"):
        expected = "T2" if f in test_csprojs else "T3"
        assert _plan(mmap, f)["level"] == expected, f
    # 层级共享面规则本身不吞任何 csproj
    layer_rule = next(r for r in mmap["tier_rules"]["path_rules"] if r["id"] == "layer_shared_surface")
    for f in _tracked("*.csproj"):
        assert not (ci.any_match(layer_rule["globs"], f) and not ci.any_match(layer_rule["except"], f)), f


def test_invariant_every_test_csproj_change_lists_its_own_test_project(mmap: dict) -> None:
    """改任何一个测试工程文件，所列测试工程必含它自己（层级范围或显式规则二选一，不能漏跑自己）。"""
    seen = 0
    for f in _tracked("*.csproj"):
        if not posixpath_name(f).startswith("Tests."):
            continue
        seen += 1
        plan = _plan(mmap, f)
        assert plan["level"] == "T2", f
        assert f in plan["dotnet_test"]["projects"], f"{f} 的改动没有跑它自己：{plan['dotnet_test']['projects']}"
    assert seen >= 8


def test_invariant_every_toolchain_tests_file_runs_only_toolchain_pytest(mmap: dict) -> None:
    """toolchain/tests 下的任何受版本管理文件：T1，不跑 dotnet、不跑引擎侧，除 toolchain_pytest 外
    不带别的 pytest/重步骤（私服回归用例文件自己的隔离步骤例外）。gate_floors.json 单列，见
    test_repro_gate_floors_only_runs_only_the_floors_test_not_full_pytest。"""
    files = _tracked("toolchain/tests")
    assert len(files) > 50
    heavy = {"dotnet_build", "dotnet_test", "abi_probe", "sim_baseline", "sync_dll", "docs_pytest", "hooks_pytest", "unity_playmode"}
    for f in files:
        plan = _plan(mmap, f)
        assert plan["level"] == "T1", f
        assert plan["dotnet_test"]["mode"] == "none" and plan["engine"]["mode"] == "none", f
        ids = _run_ids(plan)
        assert "toolchain_pytest" in ids, f
        assert not (heavy & ids), f
        if posixpath_name(f) != "test_registry_stop_pidfile_rewrite_timestamp.py":
            assert "registry_pytest" not in ids, f


def test_invariant_every_tracked_file_under_coverage_roots_is_registered(mmap: dict) -> None:
    """路径类别登记的覆盖自检：coverage_roots 下的受版本管理文件不落到「未被任何规则覆盖」（除登记的允许项）。"""
    roots = mmap["tier_rules"]["coverage_roots"]
    allow = mmap["tier_rules"]["coverage_allow_unknown_globs"]
    seen = 0
    for f in _tracked(*[r.rstrip("/") for r in roots]):
        if ci.any_match(allow, f):
            continue
        seen += 1
        assert not ci.classify_path(f, mmap)["rule"].startswith("未被任何规则覆盖"), f
    assert seen > 300


def test_module_map_check_flags_uncovered_path_category_and_ghost_rule(tmp_path: Path) -> None:
    """自检扩展到新登记的路径类别：coverage_roots 下新增未登记的子目录、规则匹配不到任何文件，都要红。"""
    repo = tmp_path / "gitrepo"
    (repo / "adapters/widget/Runtime").mkdir(parents=True)
    (repo / "adapters/widget/Runtime/A.cs").write_text("// a\n", encoding="utf-8")
    (repo / "adapters/widget/Samples").mkdir(parents=True)
    (repo / "adapters/widget/Samples/S.cs").write_text("// s\n", encoding="utf-8")
    init_temp_repo(repo, identity=False)
    run_git(repo, "add", "-A")
    data = {
        "layers": {},
        "modules": [],
        "engine": {},
        "steps": [{"id": "self_check"}],
        "tier_rules": {
            "path_rules": [
                {"id": "widget_runtime", "globs": ["adapters/widget/Runtime/**"], "level": 2},
                {"id": "ghost", "globs": ["adapters/gone/**"], "level": 1},
            ],
            "coverage_roots": ["adapters/widget/"],
            "coverage_allow_unknown_globs": [],
        },
    }
    problems = gmm.check_path_rules(repo, data)
    assert any("幽灵规则" in p and "ghost" in p for p in problems), problems
    assert any("未被任何规则覆盖" in p and "Samples/S.cs" in p for p in problems), problems
    assert not any("Runtime/A.cs" in p for p in problems), problems
    # 规则自身不合法：未知步骤/分类/测试工程、级别越界
    bad = json.loads(json.dumps(data))
    bad["tier_rules"]["path_rules"][0].update({"steps": ["no_such_step"], "engine_categories": ["module:nope"], "dotnet_projects": ["x/none.csproj"], "level": 3})
    msgs = " | ".join(gmm.check_path_rules(repo, bad))
    for needle in ("no_such_step", "module:nope", "x/none.csproj", "level"):
        assert needle in msgs, msgs


def test_repro_lab_dataset_and_fixtures_stay_t1_with_their_own_steps(mmap: dict) -> None:
    # 复现：data/** 一律共享面 T3，改一行实验室数据集就跑全量。data/_lab 只被实验室内核与命令行读取，
    # 归 T1：只跑实验室测试工程 + 实验室数据集校验 + 指纹基线比对，其余 data/** 仍是 T3。
    plan = _plan(mmap, "data/_lab/lab/lab.scenario.json")
    assert plan["level"] == "T1"
    assert plan["dotnet_test"]["projects"] == ["lab/tests/Tests.Lab.csproj"]
    assert {"validate_lab_data", "feel_lab_suite", "dotnet_test"} <= _run_ids(plan)
    assert "toolchain_pytest" in _skip_ids(plan)
    assert _plan(mmap, "data/_framework/lab/lab.scenario.json")["level"] == "T3"
    # 不变量：改基线夹具（lab/ 模块内部）同样触发指纹基线比对；校验步骤只看数据。
    fixture_plan = _plan(mmap, "lab/fixtures/baselines/wall.baseline.json")
    assert fixture_plan["level"] == "T1" and "feel_lab_suite" in _run_ids(fixture_plan)
    assert "validate_lab_data" not in _run_ids(fixture_plan)


def test_repro_equip_dataset_is_t1_with_its_own_steps(mmap: dict) -> None:
    # 复现：data/** 一律共享面 T3，改占位装备集数据（data/_equip，手感设计/08 的参照实现）就跑全量。它只被装备完整性
    # 校验与 validate_equip_data 读取，归 T1：只跑这两步，不跑 dotnet / 工具链 pytest；其余 data/** 仍是 T3。
    plan = _plan(mmap, "data/_equip/item/item.template.json")
    assert plan["level"] == "T1"
    assert {"validate_equip_data", "equip_pack_check"} <= _run_ids(plan)
    assert "toolchain_pytest" in _skip_ids(plan)
    assert _plan(mmap, "data/_framework/display/display.anim_set.json")["level"] == "T3"
    # assets/** 改动同样触发装备完整性检查（层剪辑、图标、皮肤都在 assets/_placeholder 下）。
    assets_plan = _plan(mmap, "assets/_placeholder/ui/skin/default/theme.json")
    assert assets_plan["level"] == "T1" and "equip_pack_check" in _run_ids(assets_plan)
