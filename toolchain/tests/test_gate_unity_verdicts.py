"""门禁 Unity 线内部判定逻辑的回归测试（测试覆盖第四批，2026-10-01，复盘 I-8 余项）。

覆盖对象：
  - ``toolchain/_gate_unity_verdicts.ps1``：``Get-UnityTestRunVerdict``（Unity 结果 XML 判定）、
    ``Test-SmokeLogText``（冒烟日志判定）、``Get-PackageManifestProblems``（npm 包清单排除名单与各包
    必需文件清单）。这些判定此前内联在 ``_gate_line_unity.ps1`` 的 scriptblock 里，只有真跑 Unity 才
    执行得到，判错放行/判错拦截都要等线上才发现。
  - ``toolchain/_gate_step_runner.ps1``：``Resolve-UnityExe``、``Invoke-NativeAndWait``、
    ``Test-NoResidualUnityProcess``（后者用同名函数遮蔽 ``Get-CimInstance`` 造伪进程，不需要真起
    Unity）。

复现 + 不变量各有：复现 = 清单少一个必需文件/命中排除名单/结果 XML 为 Failed -> 判定必须拦住；
不变量 = 合格清单、预编译工具产物豁免路径、Passed 的 XML 不被误拦。必需文件清单在本文件里**独立写
一份**，与 ``_gate_unity_verdicts.ps1`` 里的清单互为对照（谁单边改了，这里立刻红）。

所有判定在一次 PowerShell 子进程里批量执行（``_ps_harness.py``，会进入 PowerShell 5.1/7 宿主矩阵）。
"""

from __future__ import annotations

from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, TOOLCHAIN_DIR, ps_quote, run_ps_json

VERDICTS = TOOLCHAIN_DIR / "_gate_unity_verdicts.ps1"
STEP_RUNNER = TOOLCHAIN_DIR / "_gate_step_runner.ps1"

UNITY_PKG = "com.gamefoundation.adapter.unity"
TOOLCHAIN_PKG = "com.gamefoundation.toolchain"
HEADLESS_PKG = "com.gamefoundation.adapter.headless"
DATA_PKG = "com.gamefoundation.framework-data"

REQUIRED = {
    UNITY_PKG: [
        "Runtime/Resources/GameFoundation/models/placeholder_biped.prefab",
        "Runtime/Resources/GameFoundation/models/placeholder_biped.controller",
        "Runtime/Resources/GameFoundation/anim_clips/idle.anim",
        "Runtime/Resources/GameFoundation/anim_clips/attack.anim",
        "Runtime/Resources/GameFoundation/anim_clips/cast.anim",
        "Runtime/Resources/GameFoundation/anim_clips/hit.anim",
        "Editor/GeneratePlaceholderModelAssets.cs",
    ],
    TOOLCHAIN_PKG: [
        "Tools~/validator/bin/Validator.dll",
        "Tools~/validator/Directory.Build.props",
        "Tools~/simrunner/bin/SimRunner.dll",
        "Tools~/simrunner/Directory.Build.props",
        "Tools~/feellab/bin/FeelLab.dll",
        "Tools~/feellab/bin/Lab.Kernel.dll",
        "Tools~/feellab/Directory.Build.props",
        "Tools~/feellab/labroot/data/_feel/feel/feel.calibration.json",
        "Tools~/feellab/labroot/data/_feel_templates/feel/feel.preset.json",
        "Tools~/feellab/labroot/data/_lab/lab/lab.scenario.json",
        "Tools~/feellab/labroot/data/_equip/item/item.template.json",
        "Tools~/feellab/labroot/lab/fixtures/scripts/feel_kill.script.json",
        "Tools~/feellab/labroot/lab/fixtures/baselines/feel_kill.baseline.json",
    ],
    HEADLESS_PKG: ["Lib~/Core.Sim.dll"],
    DATA_PKG: [
        "Data~/data/_feel/feel/feel.calibration.json",
        "Data~/data/_feel/feel/feel.preset.json",
        "Data~/data/_feel_templates/feel/feel.preset.json",
    ],
}


def _full_listing(pkg: str) -> list[str]:
    return ["package.json", "README.md"] + [f"pkg/{p}" for p in REQUIRED[pkg]]


# ----------------------------------------------------------------------------
# 批量 PowerShell 判定
# ----------------------------------------------------------------------------

def _nunit_xml(result: str, failed: int) -> str:
    return (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f'<test-run id="2" testcasecount="3" result="{result}" total="3" passed="{3 - failed}" '
        f'failed="{failed}" inconclusive="0" skipped="0"></test-run>\n'
    )


@pytest.fixture(scope="module")
def verdicts(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("unity_verdicts")
    passed_xml = tmp / "passed.xml"
    passed_xml.write_text(_nunit_xml("Passed", 0), encoding="utf-8")
    failed_xml = tmp / "failed.xml"
    failed_xml.write_text(_nunit_xml("Failed(Child)", 1), encoding="utf-8")
    missing_xml = tmp / "missing.xml"

    # 清单用例：(name, package, entry paths)。
    manifest_cases: list[dict] = []

    def add(name: str, pkg: str, paths: list[str]) -> None:
        manifest_cases.append({"name": name, "pkg": pkg, "paths": paths})

    for pkg in REQUIRED:
        add(f"complete:{pkg}", pkg, _full_listing(pkg))
        for missing in REQUIRED[pkg]:
            add(f"missing:{pkg}:{missing}", pkg, [p for p in _full_listing(pkg) if not p.endswith(missing)])
    add("empty_listing:unity", UNITY_PKG, [])
    add("empty_listing:data", DATA_PKG, [])
    for segment in ("__pycache__", "bin", "obj", "storage"):
        add(f"forbidden:{segment}", DATA_PKG, _full_listing(DATA_PKG) + [f"Data~/{segment}/x.bin"])
    add("forbidden_backslash_path", DATA_PKG, _full_listing(DATA_PKG) + ["Data~\\obj\\x.dll"])
    add("exempt:validator_bin", TOOLCHAIN_PKG, _full_listing(TOOLCHAIN_PKG))
    add("exempt:simrunner_bin_only_for_those", TOOLCHAIN_PKG,
        _full_listing(TOOLCHAIN_PKG) + ["pkg/Tools~/other/bin/x.dll"])
    add("two_forbidden_segments_reported_once_each", DATA_PKG,
        _full_listing(DATA_PKG) + ["a/bin/x", "b/bin/y", "c/obj/z"])
    # 必需文件按"路径以该后缀结尾"匹配：前缀目录不同也算齐；文件名只是子串则不算。
    add("suffix_match_other_prefix", HEADLESS_PKG, ["whatever/deep/Lib~/Core.Sim.dll"])
    add("substring_is_not_suffix", HEADLESS_PKG, ["Lib~/Core.Sim.dll.meta"])

    smoke_cases = [
        ("smoke_ok", "boot...\n[GF-SMOKE] RESULT=OK\n", False),
        ("smoke_fail_marker", "[GF-SMOKE] RESULT=FAIL\n", False),
        ("smoke_empty", "", False),
        ("smoke_discrete_needs_round", "[GF-SMOKE] RESULT=OK\n", True),
        ("smoke_discrete_ok", "[GF-SMOKE] RESULT=OK\nstep=discrete_round ok\n", True),
        ("smoke_discrete_round_but_no_result", "step=discrete_round ok\n", True),
        ("smoke_continuous_ignores_round", "[GF-SMOKE] RESULT=OK\nstep=discrete_round ok\n", False),
    ]

    import json
    payload = {
        "xml": {"passed": str(passed_xml), "failed": str(failed_xml), "missing": str(missing_xml)},
        "manifest": manifest_cases,
        "smoke": [{"name": n, "text": t, "discrete": d} for n, t, d in smoke_cases],
    }
    payload_path = tmp / "payload.json"
    payload_path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")

    body = f"""
. {ps_quote(VERDICTS)}
$p = Get-Content -LiteralPath {ps_quote(payload_path)} -Raw -Encoding UTF8 | ConvertFrom-Json
$out = [ordered]@{{}}
$out.xml = [ordered]@{{}}
foreach ($k in @("passed","failed","missing")) {{
    $v = Get-UnityTestRunVerdict -ResultsXml $p.xml.$k
    $out.xml[$k] = [ordered]@{{ exists = [bool]$v.Exists; ok = [bool]$v.Ok; result = [string]$v.Result; failed = [string]$v.Failed }}
}}
$out.manifest = [ordered]@{{}}
foreach ($c in @($p.manifest)) {{
    $problems = @(Get-PackageManifestProblems -PackageName $c.pkg -EntryPaths ([string[]]@($c.paths)))
    $out.manifest[$c.name] = @($problems)
}}
$out.smoke = [ordered]@{{}}
foreach ($c in @($p.smoke)) {{
    if ($c.discrete) {{ $r = Test-SmokeLogText -LogText $c.text -Discrete }} else {{ $r = Test-SmokeLogText -LogText $c.text }}
    $out.smoke[$c.name] = [bool]$r
}}
$out | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    return run_ps_json(tmp, body, name="verdicts")


# ----------------------------------------------------------------------------
# Unity 结果 XML 判定
# ----------------------------------------------------------------------------

def test_passed_result_xml_is_ok(verdicts: dict) -> None:
    v = verdicts["xml"]["passed"]
    assert v == {"exists": True, "ok": True, "result": "Passed", "failed": "0"}


def test_failed_result_xml_is_rejected_and_reports_failed_count(verdicts: dict) -> None:
    v = verdicts["xml"]["failed"]
    assert v["exists"] is True
    assert v["ok"] is False
    assert v["result"] == "Failed(Child)"
    assert v["failed"] == "1"


def test_missing_result_xml_reports_not_exists(verdicts: dict) -> None:
    assert verdicts["xml"]["missing"]["exists"] is False
    assert verdicts["xml"]["missing"]["ok"] is False


# ----------------------------------------------------------------------------
# 冒烟日志判定
# ----------------------------------------------------------------------------

@pytest.mark.parametrize(
    "name,expected",
    [
        ("smoke_ok", True),
        ("smoke_fail_marker", False),
        ("smoke_empty", False),
        ("smoke_discrete_needs_round", False),
        ("smoke_discrete_ok", True),
        ("smoke_discrete_round_but_no_result", False),
        ("smoke_continuous_ignores_round", True),
    ],
)
def test_smoke_log_verdict(verdicts: dict, name: str, expected: bool) -> None:
    assert verdicts["smoke"][name] is expected


# ----------------------------------------------------------------------------
# 包清单：必需文件与排除名单
# ----------------------------------------------------------------------------

@pytest.mark.parametrize("pkg", list(REQUIRED))
def test_complete_listing_has_no_problems(verdicts: dict, pkg: str) -> None:
    assert verdicts["manifest"][f"complete:{pkg}"] == []


def _missing_cases():
    for pkg, files in REQUIRED.items():
        for f in files:
            yield pytest.param(pkg, f, id=f"{pkg}-{f}")


@pytest.mark.parametrize("pkg,missing", list(_missing_cases()))
def test_each_required_file_missing_is_reported(verdicts: dict, pkg: str, missing: str) -> None:
    problems = verdicts["manifest"][f"missing:{pkg}:{missing}"]
    assert len(problems) == 1, problems
    assert pkg in problems[0]
    assert missing in problems[0], problems[0]
    assert "缺失" in problems[0]


def test_unity_pkg_missing_message_cites_the_audit_item(verdicts: dict) -> None:
    problems = verdicts["manifest"][f"missing:{UNITY_PKG}:Editor/GeneratePlaceholderModelAssets.cs"]
    assert "PJ130-02" in problems[0]


def test_empty_listing_reports_missing_for_packages_with_requirements(verdicts: dict) -> None:
    unity = verdicts["manifest"]["empty_listing:unity"]
    assert len(unity) == 1 and "缺失" in unity[0]
    # framework-data 的必需文件是 data/_feel 手感框架数据（S1 发版打包断言）：空清单报缺失。
    data = verdicts["manifest"]["empty_listing:data"]
    assert len(data) == 1 and "缺失" in data[0] and "data/_feel" in data[0], data


@pytest.mark.parametrize("segment", ["__pycache__", "bin", "obj", "storage"])
def test_forbidden_segment_in_listing_is_reported(verdicts: dict, segment: str) -> None:
    problems = verdicts["manifest"][f"forbidden:{segment}"]
    assert len(problems) == 1, problems
    assert "排除名单" in problems[0]
    assert segment in problems[0]


def test_forbidden_segment_detected_with_backslash_paths(verdicts: dict) -> None:
    problems = verdicts["manifest"]["forbidden_backslash_path"]
    assert len(problems) == 1 and "obj" in problems[0]


def test_prebuilt_tool_bin_directories_are_exempt_from_the_exclusion_list(verdicts: dict) -> None:
    assert verdicts["manifest"]["exempt:validator_bin"] == []


def test_exemption_does_not_cover_other_bin_directories(verdicts: dict) -> None:
    problems = verdicts["manifest"]["exempt:simrunner_bin_only_for_those"]
    assert len(problems) == 1 and "bin" in problems[0], problems


def test_each_forbidden_segment_is_reported_once_even_if_it_repeats(verdicts: dict) -> None:
    problems = verdicts["manifest"]["two_forbidden_segments_reported_once_each"]
    assert len(problems) == 1
    text = problems[0]
    assert text.count("bin") == 1 and text.count("obj") == 1


def test_required_files_match_by_path_suffix(verdicts: dict) -> None:
    assert verdicts["manifest"]["suffix_match_other_prefix"] == []
    problems = verdicts["manifest"]["substring_is_not_suffix"]
    assert len(problems) == 1 and "Core.Sim.dll" in problems[0]


# ----------------------------------------------------------------------------
# 步骤运行器里的环境函数：Resolve-UnityExe / Invoke-NativeAndWait / Test-NoResidualUnityProcess
# ----------------------------------------------------------------------------

@pytest.fixture(scope="module")
def runner_results(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("step_runner_env")
    program_files = tmp / "pf"
    unity_exe = program_files / "Unity" / "Hub" / "Editor" / "6000.3.23f1" / "Editor" / "Unity.exe"
    unity_exe.parent.mkdir(parents=True)
    unity_exe.write_bytes(b"not a real exe")
    empty_pf = tmp / "pf_empty"
    empty_pf.mkdir()

    body = f"""
# Invoke-CheckStep 等函数引用的调用方变量，dot-source 前先声明。
$DocsOnly = $false
$FailFast = $false
$script:Results = New-Object System.Collections.Generic.List[Object]
$script:GateFailed = $false
$script:FailFastFlagPath = $null
. {ps_quote(STEP_RUNNER)}
$out = [ordered]@{{}}

# --- Resolve-UnityExe ---
$env:ProgramFiles = {ps_quote(program_files)}
$out.resolve_explicit = Resolve-UnityExe -Explicit "D:\\custom\\Unity.exe"
$out.resolve_hub = Resolve-UnityExe -Explicit ""
$env:ProgramFiles = {ps_quote(empty_pf)}
$out.resolve_fallback = Resolve-UnityExe -Explicit ""

# --- Invoke-NativeAndWait ---
$exit3 = Invoke-NativeAndWait -Exe $env:ComSpec -ArgList @("/c", "exit", "3")
$out.wait_exit3 = [ordered]@{{ code = [int]$exit3.ExitCode; timedOut = [bool]$exit3.TimedOut }}
$quick = Invoke-NativeAndWait -Exe $env:ComSpec -ArgList @("/c", "exit", "0") -TimeoutSeconds 60
$out.wait_ok_with_timeout = [ordered]@{{ code = [int]$quick.ExitCode; timedOut = [bool]$quick.TimedOut }}
$slow = Invoke-NativeAndWait -Exe "ping.exe" -ArgList @("-n", "40", "127.0.0.1") -TimeoutSeconds 1
$out.wait_timeout = [ordered]@{{ code = [int]$slow.ExitCode; timedOut = [bool]$slow.TimedOut }}

# --- Test-NoResidualUnityProcess：遮蔽 Get-CimInstance 造伪进程 ---
function Get-CimInstance {{
    param($ClassName, $Filter, $ErrorAction)
    if ($script:FakeCimMode -eq "throw") {{ throw "cim unavailable" }}
    return @(
        [PSCustomObject]@{{ ProcessId = 111; CommandLine = 'C:\\Unity\\Unity.exe -batchmode -projectPath D:\\other\\proj -quit' }},
        [PSCustomObject]@{{ ProcessId = 222; CommandLine = 'C:\\Unity\\Unity.exe -batchmode -projectPath D:\\ws\\adapters\\unity -runTests' }},
        [PSCustomObject]@{{ ProcessId = 333; CommandLine = $null }}
    )
}}
function Invoke-Residual([string]$projectPath) {{
    try {{ Test-NoResidualUnityProcess -ProjectPath $projectPath; return [ordered]@{{ threw = $false; message = "" }} }}
    catch {{ return [ordered]@{{ threw = $true; message = $_.Exception.Message }} }}
}}
$script:FakeCimMode = "ok"
$out.residual_hit = Invoke-Residual "D:\\ws\\adapters\\unity"
$out.residual_hit_trailing_slash = Invoke-Residual "D:\\ws\\adapters\\unity\\"
$out.residual_miss = Invoke-Residual "D:\\ws\\somewhere\\else"
$script:FakeCimMode = "throw"
$out.residual_cim_unavailable = Invoke-Residual "D:\\ws\\adapters\\unity"

$out | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    return run_ps_json(tmp, body, name="runner_env", timeout=240)


def test_resolve_unity_exe_prefers_explicit_path(runner_results: dict) -> None:
    assert runner_results["resolve_explicit"] == "D:\\custom\\Unity.exe"


def test_resolve_unity_exe_uses_hub_install_when_present(runner_results: dict) -> None:
    resolved = runner_results["resolve_hub"].replace("\\", "/")
    assert resolved.endswith("Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe")
    assert "/pf/" in resolved


def test_resolve_unity_exe_falls_back_to_bare_name(runner_results: dict) -> None:
    assert runner_results["resolve_fallback"] == "Unity.exe"


def test_invoke_native_and_wait_returns_real_exit_code(runner_results: dict) -> None:
    assert runner_results["wait_exit3"] == {"code": 3, "timedOut": False}


def test_invoke_native_and_wait_with_timeout_still_reports_exit_code(runner_results: dict) -> None:
    assert runner_results["wait_ok_with_timeout"] == {"code": 0, "timedOut": False}


def test_invoke_native_and_wait_kills_and_flags_timeout(runner_results: dict) -> None:
    assert runner_results["wait_timeout"] == {"code": -1, "timedOut": True}


def test_residual_unity_process_for_same_project_is_rejected(runner_results: dict) -> None:
    hit = runner_results["residual_hit"]
    assert hit["threw"] is True
    assert "222" in hit["message"], hit["message"]
    assert "111" not in hit["message"], "别的工程的 Unity 进程不应被算进来"


def test_residual_check_ignores_trailing_slash_on_project_path(runner_results: dict) -> None:
    assert runner_results["residual_hit_trailing_slash"]["threw"] is True


def test_residual_check_passes_when_no_process_matches_the_project(runner_results: dict) -> None:
    assert runner_results["residual_miss"]["threw"] is False


def test_residual_check_does_not_block_when_process_listing_is_unavailable(runner_results: dict) -> None:
    # 取不到进程列表时降级为放行（既有语义，见函数 try/catch）；这里钉住，防止被悄悄改成抛错或吞掉别的东西。
    assert runner_results["residual_cim_unavailable"]["threw"] is False


def test_verdict_files_have_utf8_bom_and_lf() -> None:
    for path in (VERDICTS, REPO_ROOT / "toolchain" / "_release_regression_guard.ps1"):
        raw = path.read_bytes()
        assert raw.startswith(b"\xef\xbb\xbf"), f"{path.name} 含中文，必须 UTF-8 with BOM"
        assert b"\r" not in raw, f"{path.name} 必须 LF 换行"
