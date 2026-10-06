"""包边界与独立版边界判定的回归测试（ADR-0160，P1 拆分）。

覆盖对象：``toolchain/_gate_package_boundary.ps1``（``Get-PackageBoundaryProblems``、
``Get-ReleaseTreeBoundaryProblems``、``Get-PlayerBoundaryProblems``）。

复现 + 不变量各有：复现 = 往运行时包里夹带任何一个实验室文件、演示文件、引用实验室的程序集、点名实验室的
InternalsVisibleTo，或让实验室 Unity 包的程序集/插件在独立版里可见，判定必须拦住（反例）；不变量 = 合格的运行时包、
合格的实验室包、合格的发布树与独立版产物不被误拦（对照）。

所有判定在一次 PowerShell 子进程里批量执行（``_ps_harness.py``，进入 PowerShell 5.1/7 宿主矩阵）。
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, TOOLCHAIN_DIR, ps_quote, run_ps_json

BOUNDARY = TOOLCHAIN_DIR / "_gate_package_boundary.ps1"

UNITY_PKG = "com.gamefoundation.adapter.unity"
TOOLCHAIN_PKG = "com.gamefoundation.toolchain"
DATA_PKG = "com.gamefoundation.framework-data"
HEADLESS_PKG = "com.gamefoundation.adapter.headless"
LAB_UNITY = "com.gamefoundation.feel-lab.unity"
LAB_HEADLESS = "com.gamefoundation.feel-lab.headless"

# 合格的运行时包清单（路径取自真实包形状，不含任何实验室/演示内容）。
CLEAN_RUNTIME = {
    UNITY_PKG: [
        "package.json", "README.md", "Runtime/Adapter.Unity.asmdef", "Runtime/AssemblyInfo.cs",
        "Runtime/EngineAdapter/UnityCamera.cs", "Runtime/Plugins/Core/Core.Foundation.dll",
        "Runtime/Resources/GameFoundation/models/placeholder_biped.prefab", "Editor/Adapter.Unity.Editor.asmdef",
        "Tests/Runtime/Adapter.Unity.Tests.Runtime.asmdef",
    ],
    TOOLCHAIN_PKG: ["package.json", "Tools~/validator/bin/Validator.dll", "Tools~/simrunner/bin/SimRunner.dll", "Tools~/validate_data.py"],
    DATA_PKG: ["package.json", "Data~/data/_framework/found/found.time_model.json", "Data~/data/_feel/feel/feel.preset.json",
               "Data~/data/_feel_templates/feel/feel.preset.json", "Data~/assets/_placeholder/sprites/a.png"],
    HEADLESS_PKG: ["package.json", "Lib~/Core.Sim.dll", "Lib~/Adapters.Stub.dll"],
}

CLEAN_LAB = {
    LAB_UNITY: ["package.json", "Runtime/LabHost/EngineLabHost.cs", "Runtime/Plugins/Lab.Kernel.dll", "Editor/FeelLabWindow.cs",
                "LabRoot~/data/_lab/lab/lab.scenario.json", "LabRoot~/lab/fixtures/scripts/feel_kill.script.json"],
    LAB_HEADLESS: ["package.json", "Tools~/feellab/bin/FeelLab.dll", "Tools~/feellab/labroot/data/_equip/item/item.template.json",
                   "Tools~/feellab/labroot/lab/fixtures/baselines/feel_kill.baseline.json"],
}

# 只许在实验室包里出现的路径：塞进任何运行时包都必须被拦。
LAB_ONLY_PATHS = [
    "Runtime/LabHost/EngineLabHost.cs",
    "Editor/LabHost/FeelLabWindow.cs",
    "Runtime/Plugins/Lab/Lab.Kernel.dll",
    "Tools~/feellab/bin/FeelLab.dll",
    "Tools~/feellab/labroot/data/_lab/lab/lab.scenario.json",
    "Data~/data/_lab_action/ai/ai.rotation.json",
    "Data~/data/_equip/item/item.template.json",
    "Data~/lab/fixtures/scripts/feel_kill.script.json",
    "Runtime/EngineLabStage.cs",
    "Runtime/LabPlayground.cs",
]

SHOWCASE_PATHS = [
    "Runtime/ShowcaseDirector.cs",
    "Data~/assets/_showcase/sprites/creature_show_hero/front/body.png",
    "Data~/data/_showcase/display/display.anim_set.json",
    "Data~/data/_showcase_3d/display/display.anim_set.json",
    "Runtime/Models/quaternius_rpg/Cleric.fbx",
    "Editor/ModelPackBuilder.cs",
    "Runtime/ModelGroundUpright.cs",
    "Scenes/LabShowcase_3d_action.unity",
]


def _write(root: Path, rel: str, text: str) -> None:
    p = root / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8", newline="\n")


def _asmdef(name: str, *, references=(), precompiled=(), define_constraints=(), include_platforms=()) -> str:
    return json.dumps(
        {
            "name": name,
            "references": list(references),
            "precompiledReferences": list(precompiled),
            "defineConstraints": list(define_constraints),
            "includePlatforms": list(include_platforms),
        },
        ensure_ascii=False,
    )


def _plugin_meta(any_enabled: int, editor_enabled: int) -> str:
    return (
        "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n"
        "  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 1\n"
        "  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      Any: \n    second:\n"
        f"      enabled: {any_enabled}\n      settings: {{}}\n  - first:\n      Editor: Editor\n    second:\n"
        f"      enabled: {editor_enabled}\n      settings:\n        DefaultValueInitialized: true\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    )


@pytest.fixture(scope="module")
def results(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("boundary")
    dirs = tmp / "dirs"

    list_cases: list[dict] = []

    def add_list(name: str, pkg: str, paths: list[str]) -> None:
        list_cases.append({"name": name, "pkg": pkg, "paths": paths, "dir": ""})

    for pkg, paths in {**CLEAN_RUNTIME, **CLEAN_LAB}.items():
        add_list(f"clean:{pkg}", pkg, paths)
    for pkg, paths in CLEAN_RUNTIME.items():
        for bad in LAB_ONLY_PATHS:
            add_list(f"lab_in_runtime:{pkg}:{bad}", pkg, paths + [bad])
    for pkg, paths in {**CLEAN_RUNTIME, **CLEAN_LAB}.items():
        for bad in SHOWCASE_PATHS:
            add_list(f"showcase:{pkg}:{bad}", pkg, paths + [bad])
    add_list("backslash_lab_path", DATA_PKG, CLEAN_RUNTIME[DATA_PKG] + ["Runtime\\LabHost\\EngineLabHost.cs"])
    add_list("showcase_case_insensitive", DATA_PKG, CLEAN_RUNTIME[DATA_PKG] + ["Data~/assets/_SHOWCASE/x.png"])

    # 磁盘判定：asmdef 引用、InternalsVisibleTo、实验室包的编辑器限定与插件元数据。
    def add_dir(name: str, pkg: str, files: dict[str, str]) -> None:
        d = dirs / name
        for rel, text in files.items():
            _write(d, rel, text)
        list_cases.append({"name": name, "pkg": pkg, "paths": list(files.keys()), "dir": str(d)})

    clean_asm = {
        "Runtime/Adapter.Unity.asmdef": _asmdef("Adapter.Unity", precompiled=["Core.Foundation.dll", "Core.Rules.dll"]),
        "Runtime/AssemblyInfo.cs": '[assembly: InternalsVisibleTo("Adapter.Unity.Tests.Editor")]\n[assembly: InternalsVisibleTo("Adapter.Unity.Tests.Runtime")]\n',
    }
    add_dir("disk_clean_runtime", UNITY_PKG, clean_asm)
    add_dir("disk_runtime_refs_lab_asmdef", UNITY_PKG,
            {**clean_asm, "Runtime/Other.asmdef": _asmdef("Other", references=["Adapter.Unity", "FeelLab.Unity"])})
    add_dir("disk_runtime_refs_labhost_old_name", UNITY_PKG,
            {**clean_asm, "Runtime/Other.asmdef": _asmdef("Other", references=["Adapter.Unity.LabHost"])})
    add_dir("disk_runtime_precompiled_lab_kernel", UNITY_PKG,
            {**clean_asm, "Runtime/Other.asmdef": _asmdef("Other", precompiled=["Lab.Kernel.dll"])})
    add_dir("disk_runtime_refs_showcase", UNITY_PKG,
            {**clean_asm, "Runtime/Other.asmdef": _asmdef("Other", references=["Samples.Showcase"])})
    add_dir("disk_runtime_internals_lab", UNITY_PKG,
            {**clean_asm, "Runtime/AssemblyInfo.cs": '[assembly: InternalsVisibleTo("Adapter.Unity.LabHost")]\n'})
    add_dir("disk_runtime_internals_feel_lab", UNITY_PKG,
            {**clean_asm, "Runtime/AssemblyInfo.cs": '[assembly: InternalsVisibleTo( "FeelLab.Unity" )]\n'})
    add_dir("disk_runtime_internals_lab_tests", UNITY_PKG,
            {**clean_asm, "Runtime/AssemblyInfo.cs": '[assembly: InternalsVisibleTo("Adapter.Unity.Tests.LabHost")]\n'})

    lab_ok = {
        "Runtime/FeelLab.Unity.asmdef": _asmdef("FeelLab.Unity", references=["Adapter.Unity"], define_constraints=["UNITY_EDITOR"]),
        "Editor/FeelLab.Unity.Editor.asmdef": _asmdef("FeelLab.Unity.Editor", include_platforms=["Editor"]),
        "Tests/Runtime/FeelLab.Unity.Tests.asmdef": _asmdef("FeelLab.Unity.Tests", references=["FeelLab.Unity"], define_constraints=["UNITY_INCLUDE_TESTS"]),
        "Runtime/Plugins/Lab.Kernel.dll.meta": _plugin_meta(0, 1),
    }
    add_dir("disk_clean_lab_unity", LAB_UNITY, lab_ok)
    add_dir("disk_lab_asmdef_not_editor_only", LAB_UNITY,
            {**lab_ok, "Runtime/FeelLab.Unity.asmdef": _asmdef("FeelLab.Unity", references=["Adapter.Unity"])})
    add_dir("disk_lab_asmdef_editor_platform_plus_other", LAB_UNITY,
            {**lab_ok, "Editor/FeelLab.Unity.Editor.asmdef": _asmdef("FeelLab.Unity.Editor", include_platforms=["Editor", "WindowsStandalone64"])})
    add_dir("disk_lab_plugin_any_enabled", LAB_UNITY, {**lab_ok, "Runtime/Plugins/Lab.Kernel.dll.meta": _plugin_meta(1, 1)})
    add_dir("disk_lab_plugin_editor_disabled", LAB_UNITY, {**lab_ok, "Runtime/Plugins/Lab.Kernel.dll.meta": _plugin_meta(0, 0)})
    add_dir("disk_lab_plugin_meta_unparsable", LAB_UNITY, {**lab_ok, "Runtime/Plugins/Lab.Kernel.dll.meta": "fileFormatVersion: 2\n"})
    # 实验室包自己引用实验室程序集是允许的（依赖方向是实验室依赖运行时）。
    add_dir("disk_lab_headless_is_not_asmdef_checked", LAB_HEADLESS, {"Tools~/feellab/x.asmdef": _asmdef("X", references=["FeelLab.Unity"])})

    zip_cases = [
        {"name": "zip_clean", "paths": ["Runtime/x.dll", "data/_framework/a.json", "manual/index.html", "packages/com.gamefoundation.adapter.unity-2.0.0.tgz"]},
        {"name": "zip_lab_package_dir_leaked", "paths": ["packages/com.gamefoundation.feel-lab.unity/package.json", "packages/com.gamefoundation.feel-lab.unity/Runtime/LabHost/a.cs"]},
        {"name": "zip_lab_tgz_leaked", "paths": ["packages/com.gamefoundation.feel-lab.headless-2.0.0.tgz"]},
    ]

    tree_cases = [
        {"name": "tree_clean", "paths": ["Runtime/x.dll", "packages/com.gamefoundation.adapter.unity/package.json", "data/_framework/a.json", "manual/index.html"]},
        {"name": "tree_lab_in_lab_package_dir", "paths": ["packages/com.gamefoundation.feel-lab.unity/Runtime/LabHost/EngineLabHost.cs",
                                                          "packages/com.gamefoundation.feel-lab.headless/Tools~/feellab/bin/FeelLab.dll",
                                                          "packages/com.gamefoundation.feel-lab.headless-2.0.0.tgz"]},
        {"name": "tree_lab_data_at_root", "paths": ["data/_lab/lab/lab.scenario.json"]},
        {"name": "tree_lab_fixtures_at_root", "paths": ["lab/fixtures/scripts/x.json"]},
        {"name": "tree_feellab_cli_in_toolchain", "paths": ["toolchain/feellab/bin/FeelLab.dll"]},
        {"name": "tree_lab_in_adapter_package_dir", "paths": ["packages/com.gamefoundation.adapter.unity/Runtime/LabHost/EngineLabHost.cs"]},
        {"name": "tree_lab_package_prefix_lookalike", "paths": ["packages/com.gamefoundation.feel-lab.unity-evil/Runtime/LabHost/a.cs"]},
        {"name": "tree_showcase_anywhere", "paths": ["packages/com.gamefoundation.feel-lab.unity/Runtime/ShowcaseDirector.cs"]},
        {"name": "tree_showcase_assets", "paths": ["assets/_showcase/sprites/a.png"]},
        {"name": "tree_backslash", "paths": ["adapters\\unity\\Packages\\com.gamefoundation.adapter.unity\\Runtime\\LabHost\\a.cs"]},
        {"name": "tree_manual_lab_api_pages_are_documentation", "paths": ["manual/api/Lab.Kernel.LabHost.html", "manual/concepts/lab/README.html"]},
        {"name": "tree_manual_showcase_still_rejected", "paths": ["manual/concepts/showcase/README.html"]},
    ]

    player_cases: list[dict] = []

    def add_player(name: str, files: dict[str, str], scripting: str | None = "default") -> None:
        d = tmp / "players" / name
        for rel, text in files.items():
            _write(d, rel, text)
        if scripting == "default":
            scripting = '{"names":["UnityEngine.CoreModule.dll","Assembly-CSharp.dll","Adapter.Unity.dll","Game.Template.dll"],"types":[]}'
        if scripting is not None:
            _write(d, "Shell_Data/ScriptingAssemblies.json", scripting)
        player_cases.append({"name": name, "dir": str(d)})

    base_player = {"Shell.exe": "x", "Shell_Data/Managed/Adapter.Unity.dll": "x", "Shell_Data/Managed/Game.Template.dll": "x",
                   "Shell_Data/StreamingAssets/GameFoundation/data/_framework/a.json": "{}"}
    add_player("player_clean", base_player)
    add_player("player_lab_dll_in_managed", {**base_player, "Shell_Data/Managed/FeelLab.Unity.dll": "x"})
    add_player("player_labhost_old_dll", {**base_player, "Shell_Data/Managed/Adapter.Unity.LabHost.dll": "x"})
    add_player("player_lab_kernel_dll", {**base_player, "Shell_Data/Managed/Lab.Kernel.dll": "x"})
    add_player("player_lab_data_in_streaming", {**base_player, "Shell_Data/StreamingAssets/GameFoundation/data/_lab/lab/lab.scenario.json": "{}"})
    add_player("player_showcase_assets", {**base_player, "Shell_Data/StreamingAssets/GameFoundation/sprites/_showcase/a.png": "x"})
    add_player("player_scripting_names_lab", base_player,
               scripting='{"names":["Adapter.Unity.dll","Adapter.Unity.LabHost.dll"],"types":[]}')
    add_player("player_scripting_names_showcase", base_player, scripting='{"names":["Samples.Showcase.dll"],"types":[]}')
    add_player("player_no_scripting_json", base_player, scripting=None)
    player_cases.append({"name": "player_missing_dir", "dir": str(tmp / "players" / "does_not_exist")})

    payload = {"lists": list_cases, "trees": tree_cases, "zips": zip_cases, "players": player_cases}
    payload_path = tmp / "payload.json"
    payload_path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")

    body = f"""
. {ps_quote(BOUNDARY)}
$p = Get-Content -LiteralPath {ps_quote(payload_path)} -Raw -Encoding UTF8 | ConvertFrom-Json
$out = [ordered]@{{}}
$out.lists = [ordered]@{{}}
foreach ($c in @($p.lists)) {{
    if ($c.dir -ne "") {{
        $r = @(Get-PackageBoundaryProblems -PackageName $c.pkg -EntryPaths ([string[]]@($c.paths)) -PackageDir $c.dir)
    }} else {{
        $r = @(Get-PackageBoundaryProblems -PackageName $c.pkg -EntryPaths ([string[]]@($c.paths)))
    }}
    $out.lists[$c.name] = @($r)
}}
$out.trees = [ordered]@{{}}
foreach ($c in @($p.trees)) {{
    $out.trees[$c.name] = @(Get-ReleaseTreeBoundaryProblems -RelativePaths ([string[]]@($c.paths)))
}}
$out.zips = [ordered]@{{}}
foreach ($c in @($p.zips)) {{
    $out.zips[$c.name] = @(Get-ReleaseTreeBoundaryProblems -RelativePaths ([string[]]@($c.paths)) -AllowLabPackages $false)
}}
$out.players = [ordered]@{{}}
foreach ($c in @($p.players)) {{
    $out.players[$c.name] = @(Get-PlayerBoundaryProblems -BuildDir $c.dir)
}}
$out | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    return run_ps_json(tmp, body, name="boundary")


# ----------------------------------------------------------------------------
# 清单判定
# ----------------------------------------------------------------------------

@pytest.mark.parametrize("pkg", list({**CLEAN_RUNTIME, **CLEAN_LAB}))
def test_clean_package_listings_pass(results: dict, pkg: str) -> None:
    assert results["lists"][f"clean:{pkg}"] == []


def _lab_in_runtime_cases():
    for pkg in CLEAN_RUNTIME:
        for bad in LAB_ONLY_PATHS:
            yield pytest.param(pkg, bad, id=f"{pkg.split('.')[-1]}-{bad}")


@pytest.mark.parametrize("pkg,bad", list(_lab_in_runtime_cases()))
def test_lab_content_in_a_runtime_package_is_rejected(results: dict, pkg: str, bad: str) -> None:
    problems = results["lists"][f"lab_in_runtime:{pkg}:{bad}"]
    assert len(problems) == 1, problems
    assert pkg in problems[0] and "实验室" in problems[0] and bad.replace("\\", "/") in problems[0]


def _showcase_cases():
    for pkg in list(CLEAN_RUNTIME) + list(CLEAN_LAB):
        for bad in SHOWCASE_PATHS:
            yield pytest.param(pkg, bad, id=f"{pkg.split('.')[-1]}-{bad}")


@pytest.mark.parametrize("pkg,bad", list(_showcase_cases()))
def test_showcase_content_is_rejected_in_every_package_including_lab_packages(results: dict, pkg: str, bad: str) -> None:
    problems = results["lists"][f"showcase:{pkg}:{bad}"]
    shown = [p for p in problems if "演示" in p]
    assert len(shown) == 1, problems
    assert bad in shown[0]


def test_backslash_paths_are_normalized(results: dict) -> None:
    problems = results["lists"]["backslash_lab_path"]
    assert len(problems) == 1 and "LabHost/EngineLabHost.cs" in problems[0]


def test_showcase_match_is_case_insensitive(results: dict) -> None:
    problems = results["lists"]["showcase_case_insensitive"]
    assert len(problems) == 1 and "演示" in problems[0]


# ----------------------------------------------------------------------------
# 磁盘判定
# ----------------------------------------------------------------------------

def test_clean_runtime_package_dir_passes(results: dict) -> None:
    assert results["lists"]["disk_clean_runtime"] == []


@pytest.mark.parametrize("name,needle", [
    ("disk_runtime_refs_lab_asmdef", "FeelLab.Unity"),
    ("disk_runtime_refs_labhost_old_name", "Adapter.Unity.LabHost"),
    ("disk_runtime_precompiled_lab_kernel", "Lab.Kernel.dll"),
    ("disk_runtime_refs_showcase", "Samples.Showcase"),
])
def test_runtime_assembly_referencing_lab_or_showcase_is_rejected(results: dict, name: str, needle: str) -> None:
    problems = results["lists"][name]
    assert len(problems) == 1, problems
    assert "引用" in problems[0] and needle in problems[0]


@pytest.mark.parametrize("name,needle", [
    ("disk_runtime_internals_lab", "Adapter.Unity.LabHost"),
    ("disk_runtime_internals_feel_lab", "FeelLab.Unity"),
    ("disk_runtime_internals_lab_tests", "Adapter.Unity.Tests.LabHost"),
])
def test_internals_visible_to_a_lab_assembly_is_rejected(results: dict, name: str, needle: str) -> None:
    problems = results["lists"][name]
    assert len(problems) == 1, problems
    assert "InternalsVisibleTo" in problems[0] and needle in problems[0]


def test_clean_lab_unity_package_dir_passes(results: dict) -> None:
    assert results["lists"]["disk_clean_lab_unity"] == []


def test_lab_assembly_that_compiles_for_players_is_rejected(results: dict) -> None:
    problems = results["lists"]["disk_lab_asmdef_not_editor_only"]
    assert len(problems) == 1 and "只在编辑器编译" in problems[0] and "Runtime/FeelLab.Unity.asmdef" in problems[0]


def test_editor_platform_plus_a_player_platform_is_not_editor_only(results: dict) -> None:
    problems = results["lists"]["disk_lab_asmdef_editor_platform_plus_other"]
    assert len(problems) == 1 and "FeelLab.Unity.Editor.asmdef" in problems[0]


def test_test_assemblies_are_exempt_from_the_editor_only_rule(results: dict) -> None:
    # disk_clean_lab_unity 里的测试 asmdef 只有 UNITY_INCLUDE_TESTS 约束，仍然合格。
    assert results["lists"]["disk_clean_lab_unity"] == []


@pytest.mark.parametrize("name", ["disk_lab_plugin_any_enabled", "disk_lab_plugin_editor_disabled", "disk_lab_plugin_meta_unparsable"])
def test_lab_plugin_that_is_not_editor_only_is_rejected(results: dict, name: str) -> None:
    problems = results["lists"][name]
    assert len(problems) == 1 and "插件 DLL" in problems[0] and "Lab.Kernel.dll.meta" in problems[0]


def test_headless_lab_package_has_no_asmdef_rules(results: dict) -> None:
    assert results["lists"]["disk_lab_headless_is_not_asmdef_checked"] == []


# ----------------------------------------------------------------------------
# 发布树判定
# ----------------------------------------------------------------------------

def test_clean_release_tree_passes(results: dict) -> None:
    assert results["trees"]["tree_clean"] == []


def test_lab_package_directories_and_tgz_are_allowed_inside_the_release_tree(results: dict) -> None:
    assert results["trees"]["tree_lab_in_lab_package_dir"] == []


@pytest.mark.parametrize("name", [
    "tree_lab_data_at_root", "tree_lab_fixtures_at_root", "tree_feellab_cli_in_toolchain",
    "tree_lab_in_adapter_package_dir", "tree_lab_package_prefix_lookalike", "tree_backslash",
])
def test_lab_content_outside_the_lab_packages_is_rejected_in_the_release_tree(results: dict, name: str) -> None:
    problems = results["trees"][name]
    assert len(problems) == 1 and "实验室" in problems[0], problems


@pytest.mark.parametrize("name", ["tree_showcase_anywhere", "tree_showcase_assets"])
def test_showcase_content_is_rejected_anywhere_in_the_release_tree(results: dict, name: str) -> None:
    problems = results["trees"][name]
    assert any("演示" in p for p in problems), problems


def test_manual_documentation_pages_about_the_lab_are_not_lab_content(results: dict) -> None:
    assert results["trees"]["tree_manual_lab_api_pages_are_documentation"] == []
    problems = results["trees"]["tree_manual_showcase_still_rejected"]
    assert any("演示" in p for p in problems), problems


def test_main_zip_has_no_room_for_lab_packages_at_all(results: dict) -> None:
    assert results["zips"]["zip_clean"] == []
    for name in ("zip_lab_package_dir_leaked", "zip_lab_tgz_leaked"):
        problems = results["zips"][name]
        assert len(problems) == 1 and "实验室" in problems[0], (name, problems)


# ----------------------------------------------------------------------------
# 独立版产物判定
# ----------------------------------------------------------------------------

def test_clean_player_output_passes(results: dict) -> None:
    assert results["players"]["player_clean"] == []


@pytest.mark.parametrize("name,needle", [
    ("player_lab_dll_in_managed", "FeelLab.Unity.dll"),
    ("player_labhost_old_dll", "Adapter.Unity.LabHost.dll"),
    ("player_lab_kernel_dll", "Lab.Kernel.dll"),
    ("player_lab_data_in_streaming", "data/_lab"),
    ("player_showcase_assets", "_showcase"),
])
def test_lab_or_showcase_files_in_a_player_build_are_rejected(results: dict, name: str, needle: str) -> None:
    problems = results["players"][name]
    assert len(problems) == 1, problems
    assert "独立版" in problems[0] and needle in problems[0]


@pytest.mark.parametrize("name,needle", [
    ("player_scripting_names_lab", "Adapter.Unity.LabHost.dll"),
    ("player_scripting_names_showcase", "Samples.Showcase.dll"),
])
def test_scripting_assembly_list_naming_lab_or_showcase_is_rejected(results: dict, name: str, needle: str) -> None:
    problems = results["players"][name]
    assert len(problems) == 1 and "ScriptingAssemblies.json" in problems[0] and needle in problems[0], problems


def test_player_without_scripting_assemblies_json_is_rejected(results: dict) -> None:
    problems = results["players"]["player_no_scripting_json"]
    assert len(problems) == 1 and "ScriptingAssemblies.json" in problems[0]


def test_missing_player_directory_is_rejected(results: dict) -> None:
    problems = results["players"]["player_missing_dir"]
    assert len(problems) == 1 and "不存在" in problems[0]


def test_boundary_script_has_utf8_bom_and_lf() -> None:
    raw = BOUNDARY.read_bytes()
    assert raw.startswith(b"\xef\xbb\xbf"), "含中文，必须 UTF-8 with BOM"
    assert b"\r" not in raw, "必须 LF 换行"
