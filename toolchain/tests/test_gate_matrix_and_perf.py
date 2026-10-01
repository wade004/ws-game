"""门禁第四批（测试覆盖第四批，2026-10-01）新增逻辑的回归测试：I-2 余项、I-5 缩减版、I-6、I-12 余项。

覆盖对象与断言口径（复现 + 不变量各有）：

1. ``Get-PerfDiagnosticLines``（``toolchain/_gate_test_floors.ps1``，I-2 余项）：伪造 trx 夹具——有诊断行
   -> 返回去重后的行（剥掉 ``<StdOut>`` 标签）；没有诊断行/目录不存在/没有 trx -> 返回空（调用点据此判
   FAIL）；静态接线断言"缺失判 FAIL"不再只是黄色警告。
2. ``Test-GateExtraPytestRun``（I-5 缩减版两个矩阵步骤的判据）：junit 可解析、failed=0、
   skipped+inconclusive=0（环境性 skip 一律 FAIL）、passed 不低于要求；缺文件 FAIL。
3. ``Get-EnvironmentalSkipRows``（``_gate_step_runner.ps1``，I-12 余项）：开关/短路造成的 SKIP 不算，
   ABI 无基线、样例导入因工作树脏而跳过这类才算环境性 SKIP。
4. ``toolchain/tests/conftest.py`` 的 ``WS_GAME_PS_HOST`` 宿主矩阵开关（I-5）：起子 pytest 进程验证
   5.1 / 7 / 未设置 / 非法值 / 宿主缺失五种情形下 ``shutil.which`` 对 PowerShell 宿主名的解析结果。
5. 门禁接线（静态）：矩阵两步只在全量门禁里跑（-Quick / -SkipUnity 登记 SKIP）、check.ps1 把
   -SkipUnity 传给 heavy 线、IL2CPP 默认不进日常门禁（pre-commit / CI / 默认 check.ps1 都不传）。
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, TOOLCHAIN_DIR, ps_quote, run_ps_json

FLOORS_PS = TOOLCHAIN_DIR / "_gate_test_floors.ps1"
STEP_RUNNER = TOOLCHAIN_DIR / "_gate_step_runner.ps1"
HEAVY_LINE = TOOLCHAIN_DIR / "_gate_line_heavy.ps1"
UNITY_LINE = TOOLCHAIN_DIR / "_gate_line_unity.ps1"
CHECK_SCRIPT = REPO_ROOT / "check.ps1"
CONFTEST = TOOLCHAIN_DIR / "tests" / "conftest.py"

PERF_LINE_A = "perf Combat_WithinBaselineThreshold median=12.5 factor=1.02 reference=11.9"
PERF_LINE_B = "perf Sim_WithinBaselineThreshold median=3.1 factor=0.98 reference=3.2"


def _trx(path: Path, stdout_lines: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "\n".join(f"        <StdOut>{line}</StdOut>" for line in stdout_lines)
    path.write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">\n'
        "  <Results>\n    <UnitTestResult testName=\"t\"><Output>\n"
        f"{body}\n"
        "    </Output></UnitTestResult>\n  </Results>\n</TestRun>\n",
        encoding="utf-8",
    )


def _junit(path: Path, total: int, failed: int, skipped: int) -> None:
    path.write_text(
        '<?xml version="1.0" encoding="utf-8"?><testsuites>'
        f'<testsuite name="pytest" errors="0" failures="{failed}" skipped="{skipped}" tests="{total}">'
        "</testsuite></testsuites>\n",
        encoding="utf-8",
    )


@pytest.fixture(scope="module")
def results(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("gate_matrix_perf")

    # --- perf 诊断行 ---
    with_lines = tmp / "trx_with"
    _trx(with_lines / "a.trx", [PERF_LINE_A, "some other stdout", PERF_LINE_B])
    _trx(with_lines / "b.trx", [PERF_LINE_A])  # 跨文件重复 -> 去重
    without_lines = tmp / "trx_without"
    _trx(without_lines / "a.trx", ["hello", "perf Combat_NotTheRightSuffix median=1 factor=1 reference=1"])
    empty_dir = tmp / "trx_empty"
    empty_dir.mkdir()
    missing_dir = tmp / "trx_missing"

    # --- 额外 pytest 运行的判据 ---
    junit = {
        "ok": (tmp / "j_ok.xml", 300, 0, 0, 100),
        "skipped": (tmp / "j_skip.xml", 300, 0, 1, 100),
        "failed": (tmp / "j_fail.xml", 300, 1, 0, 100),
        "below_min": (tmp / "j_low.xml", 50, 0, 0, 100),
        "exactly_min": (tmp / "j_min.xml", 100, 0, 0, 100),
        "missing": (tmp / "j_missing.xml", 0, 0, 0, 100),
    }
    for key, (path, total, failed, skipped, _min) in junit.items():
        if key != "missing":
            _junit(path, total, failed, skipped)

    skip_rows = [
        {"Step": "a", "Result": "PASS", "Detail": ""},
        {"Step": "b", "Result": "SKIP", "Detail": "-Quick"},
        {"Step": "c", "Result": "SKIP", "Detail": "-SkipUnity（环境矩阵只在全量门禁里跑，CI 的 -SkipUnity 形态不含）"},
        {"Step": "d", "Result": "SKIP", "Detail": "-DocsOnly（非文档相关步骤，仅纯文档改动的提交跳过）"},
        {"Step": "e", "Result": "SKIP", "Detail": "-SkipConsumer"},
        {"Step": "f", "Result": "SKIP", "Detail": "未传 -Il2cpp"},
        {"Step": "g", "Result": "SKIP", "Detail": "上游步骤已失败（-FailFast，本线不再启动新步骤）"},
        {"Step": "h", "Result": "SKIP", "Detail": "并行的另一条线已失败（-FailFast，本线不再启动新步骤）"},
        {"Step": "i", "Result": "SKIP", "Detail": "前置快速检查失败（-FailFast），本线未启动"},
        {"Step": "ABI", "Result": "SKIP", "Detail": "基线发行包不存在（详见 x.log）"},
        {"Step": "样例导入", "Result": "SKIP", "Detail": "data/_sample 或 assets/_sample 已有未提交改动，先提交/还原后再跑本步骤"},
        {"Step": "emptyDetailSkip", "Result": "SKIP", "Detail": ""},
        {"Step": "failWithEnvLikeDetail", "Result": "FAIL", "Detail": "基线发行包不存在"},
    ]
    rows_path = tmp / "skip_rows.json"
    rows_path.write_text(json.dumps(skip_rows, ensure_ascii=False), encoding="utf-8")

    junit_manifest = [
        {"key": key, "path": str(path), "min": minimum}
        for key, (path, _t, _f, _s, minimum) in junit.items()
    ]
    manifest_path = tmp / "junit_manifest.json"
    manifest_path.write_text(json.dumps(junit_manifest), encoding="utf-8")

    body = f"""
$DocsOnly = $false
$FailFast = $false
$script:Results = New-Object System.Collections.Generic.List[Object]
$script:GateFailed = $false
$script:FailFastFlagPath = $null
. {ps_quote(STEP_RUNNER)}
. {ps_quote(FLOORS_PS)}
$out = [ordered]@{{}}

$out.perf_with = @(Get-PerfDiagnosticLines -TrxDir {ps_quote(with_lines)})
$out.perf_without = @(Get-PerfDiagnosticLines -TrxDir {ps_quote(without_lines)})
$out.perf_empty_dir = @(Get-PerfDiagnosticLines -TrxDir {ps_quote(empty_dir)})
$out.perf_missing_dir = @(Get-PerfDiagnosticLines -TrxDir {ps_quote(missing_dir)})

$out.junit = [ordered]@{{}}
$cases = Get-Content -LiteralPath {ps_quote(manifest_path)} -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($c in $cases) {{
    $r = Test-GateExtraPytestRun -JUnitPath $c.path -Label ("case " + $c.key) -MinPassed ([int]$c.min)
    $out.junit[$c.key] = [ordered]@{{ ok = [bool]$r.Ok; detail = [string]$r.Detail }}
}}

$rows = Get-Content -LiteralPath {ps_quote(rows_path)} -Raw -Encoding UTF8 | ConvertFrom-Json
$env = @(Get-EnvironmentalSkipRows -Results $rows)
$out.env_skips = @($env | ForEach-Object {{ $_.Step }})
$out.env_skips_empty = @(Get-EnvironmentalSkipRows -Results @()).Count
# check.ps1 实际传入的是 List[Object]（$script:Results）；形参曾声明成 [object[]]，汇总段抛 "Argument types do not match"。
$lst = New-Object System.Collections.Generic.List[Object]
foreach ($r in $rows) {{ $lst.Add($r) }}
$out.env_skips_from_list = @(Get-EnvironmentalSkipRows -Results $lst | ForEach-Object {{ $_.Step }})
$emptyLst = New-Object System.Collections.Generic.List[Object]
$out.env_skips_empty_list = @(Get-EnvironmentalSkipRows -Results $emptyLst).Count

$out | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    return run_ps_json(tmp, body, name="matrix_perf")


# ----------------------------------------------------------------------------
# 1. Perf 诊断行解析
# ----------------------------------------------------------------------------

def _as_list(value) -> list:
    if value is None:
        return []
    return value if isinstance(value, list) else [value]


def test_perf_lines_are_extracted_stripped_and_deduplicated(results: dict) -> None:
    lines = _as_list(results["perf_with"])
    assert sorted(lines) == sorted([PERF_LINE_A, PERF_LINE_B])
    assert all("<StdOut>" not in line and "</StdOut>" not in line for line in lines)


@pytest.mark.parametrize("key", ["perf_without", "perf_empty_dir", "perf_missing_dir"])
def test_missing_perf_lines_yield_empty_result_so_callers_can_fail(results: dict, key: str) -> None:
    assert _as_list(results[key]) == []


def test_heavy_line_fails_the_dotnet_test_step_when_perf_lines_are_missing() -> None:
    text = HEAVY_LINE.read_text(encoding="utf-8-sig")
    assert "$perfDiagOk" in text
    assert "($ok -and $floorResult.Ok -and $perfDiagOk)" in text, "Perf 诊断行缺失必须让步骤 Ok=false"
    assert "警告：未能从 trx 结果中找到性能基线诊断行" not in text, "不得退回只打黄色警告的旧写法"


# ----------------------------------------------------------------------------
# 2. 额外 pytest 运行的判据
# ----------------------------------------------------------------------------

def test_extra_pytest_run_passes_clean_run(results: dict) -> None:
    assert results["junit"]["ok"]["ok"] is True, results["junit"]["ok"]["detail"]
    assert results["junit"]["exactly_min"]["ok"] is True


def test_extra_pytest_run_fails_on_any_skip(results: dict) -> None:
    row = results["junit"]["skipped"]
    assert row["ok"] is False
    assert "skipped" in row["detail"]


def test_extra_pytest_run_fails_on_failures(results: dict) -> None:
    assert results["junit"]["failed"]["ok"] is False


def test_extra_pytest_run_fails_below_minimum_passed(results: dict) -> None:
    row = results["junit"]["below_min"]
    assert row["ok"] is False
    assert "低于要求" in row["detail"]


def test_extra_pytest_run_fails_when_result_file_is_missing(results: dict) -> None:
    row = results["junit"]["missing"]
    assert row["ok"] is False
    assert "未找到可解析的 junit 结果" in row["detail"]


# ----------------------------------------------------------------------------
# 3. 环境性 SKIP
# ----------------------------------------------------------------------------

def test_only_environment_caused_skips_are_listed(results: dict) -> None:
    assert sorted(_as_list(results["env_skips"])) == sorted(["ABI", "样例导入", "emptyDetailSkip"])


def test_environmental_skip_filter_handles_empty_input(results: dict) -> None:
    assert results["env_skips_empty"] == 0
    assert results["env_skips_empty_list"] == 0


def test_environmental_skip_filter_accepts_the_generic_list_check_ps1_passes(results: dict) -> None:
    assert sorted(_as_list(results["env_skips_from_list"])) == sorted(_as_list(results["env_skips"]))


# ----------------------------------------------------------------------------
# 4. conftest 宿主矩阵开关
# ----------------------------------------------------------------------------

PROBE_TEST = '''
import json, os, shutil
from pathlib import Path


def _stem(path):
    return None if path is None else Path(path).stem.lower()


def test_probe():
    data = {
        "powershell_exe": _stem(shutil.which("powershell.exe")),
        "powershell": _stem(shutil.which("powershell")),
        "pwsh": _stem(shutil.which("pwsh")),
        "pwsh_exe": _stem(shutil.which("pwsh.exe")),
        "python_resolves": shutil.which("python") is not None,
    }
    Path(os.environ["PROBE_OUT"]).write_text(json.dumps(data), encoding="utf-8")
'''


def _real_hosts() -> dict:
    env = {k: v for k, v in os.environ.items() if k != "WS_GAME_PS_HOST"}
    proc = subprocess.run(
        [sys.executable, "-c",
         "import shutil, json; print(json.dumps({'ps': shutil.which('powershell') is not None, 'pwsh': shutil.which('pwsh') is not None}))"],
        capture_output=True, text=True, env=env, timeout=60,
    )
    return json.loads(proc.stdout)


def _run_probe(tmp: Path, host: str | None, *, path_override: str | None = None):
    probe_dir = tmp / (f"probe_{host or 'unset'}_{'nopath' if path_override is not None else 'path'}")
    probe_dir.mkdir()
    (probe_dir / "conftest.py").write_text(CONFTEST.read_text(encoding="utf-8"), encoding="utf-8")
    # conftest 现在还依赖同目录的 _git_env.py（git 环境隔离与配置守卫，2026-10-01 事故修复）。
    (probe_dir / "_git_env.py").write_text((CONFTEST.parent / "_git_env.py").read_text(encoding="utf-8"), encoding="utf-8")
    (probe_dir / "test_probe.py").write_text(PROBE_TEST, encoding="utf-8")
    out = probe_dir / "out.json"
    env = {k: v for k, v in os.environ.items() if k != "WS_GAME_PS_HOST"}
    env["PROBE_OUT"] = str(out)
    env["PYTHONUTF8"] = "1"
    if host is not None:
        env["WS_GAME_PS_HOST"] = host
    if path_override is not None:
        env["PATH"] = path_override
    proc = subprocess.run(
        [sys.executable, "-m", "pytest", str(probe_dir), "-q", "-p", "no:cacheprovider", "--rootdir", str(probe_dir)],
        capture_output=True, text=True, encoding="utf-8", errors="replace", env=env, timeout=180, cwd=str(probe_dir),
    )
    data = json.loads(out.read_text(encoding="utf-8")) if out.exists() else None
    return proc, data


@pytest.fixture(scope="module")
def hosts() -> dict:
    if sys.platform != "win32":
        pytest.skip("宿主矩阵开关只在 Windows 下验证")
    return _real_hosts()


def test_conftest_without_env_leaves_resolution_alone(tmp_path: Path, hosts: dict) -> None:
    proc, data = _run_probe(tmp_path, None)
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert data["python_resolves"] is True
    if hosts["ps"]:
        assert data["powershell_exe"] == "powershell"
    if hosts["pwsh"]:
        assert data["pwsh"] == "pwsh"


def test_conftest_host_7_resolves_every_powershell_name_to_pwsh(tmp_path: Path, hosts: dict) -> None:
    if not hosts["pwsh"]:
        pytest.skip("本机没有 pwsh（PowerShell 7）")
    proc, data = _run_probe(tmp_path, "7")
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert {data["powershell_exe"], data["powershell"], data["pwsh"], data["pwsh_exe"]} == {"pwsh"}
    assert data["python_resolves"] is True, "非 PowerShell 命令的解析不得被改动"


def test_conftest_host_51_resolves_every_powershell_name_to_windows_powershell(tmp_path: Path, hosts: dict) -> None:
    if not hosts["ps"]:
        pytest.skip("本机没有 Windows PowerShell 5.1")
    proc, data = _run_probe(tmp_path, "5.1")
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert {data["powershell_exe"], data["powershell"], data["pwsh"], data["pwsh_exe"]} == {"powershell"}


def test_conftest_rejects_unknown_host_value(tmp_path: Path, hosts: dict) -> None:
    proc, data = _run_probe(tmp_path, "9")
    assert proc.returncode != 0
    assert data is None, "非法取值时不得跑到测试体"
    assert "WS_GAME_PS_HOST" in (proc.stdout + proc.stderr)


def test_conftest_aborts_instead_of_silently_degrading_when_requested_host_is_missing(tmp_path: Path, hosts: dict) -> None:
    empty_path_dir = tmp_path / "empty_path"
    empty_path_dir.mkdir()
    proc, data = _run_probe(tmp_path, "7", path_override=str(empty_path_dir))
    assert proc.returncode == 3, proc.stdout + proc.stderr
    assert data is None
    assert "找不到对应的 PowerShell 宿主" in proc.stdout + proc.stderr


# ----------------------------------------------------------------------------
# 5. 门禁接线（静态）
# ----------------------------------------------------------------------------

def _between(text: str, start: str, end: str) -> str:
    a = text.index(start)
    return text[a:text.index(end, a)]


def test_matrix_steps_only_run_in_the_full_gate() -> None:
    text = HEAVY_LINE.read_text(encoding="utf-8-sig")
    assert "[switch]$SkipUnity" in text
    assert '$matrixSkipReason = if ($Quick) { "-Quick" } elseif ($SkipUnity) { "-SkipUnity' in text
    for step_marker in ("环境矩阵 6c：不设 PYTHONUTF8", "环境矩阵 6d：PowerShell 脚本类用例在 5.1 与 7 两个宿主各跑一遍"):
        skipped = re.search(r'Add-SkippedStep "[^"]*' + re.escape(step_marker) + r'[^"]*" \$matrixSkipReason', text)
        checked = re.search(r'Invoke-CheckStep "[^"]*' + re.escape(step_marker) + r'[^"]*"', text)
        assert skipped and checked, f"{step_marker}：必须同时有 SKIP 登记和真正执行两条分支"


def test_matrix_unset_pythonutf8_and_both_hosts_are_wired() -> None:
    text = HEAVY_LINE.read_text(encoding="utf-8-sig")
    block = text[text.index("# 6c / 6d."):]
    assert "Remove-Item Env:\\PYTHONUTF8" in block
    assert '@("5.1", "7")' in block
    assert "$env:WS_GAME_PS_HOST = $psHost" in block
    assert "Test-GateExtraPytestRun" in block


def test_check_script_passes_skipunity_to_the_heavy_line_and_il2cpp_stays_opt_in() -> None:
    text = CHECK_SCRIPT.read_text(encoding="utf-8-sig")
    heavy_params = _between(text, "$heavyParams = @{", "$unityParams = @{")
    assert "SkipUnity                  = [bool]$SkipUnity" in heavy_params
    # IL2CPP 默认不进日常门禁：开关默认关、只透传调用方给的值。
    assert "[switch]$Il2cpp" in text
    assert "Il2cpp                     = [bool]$Il2cpp" in text
    unity = UNITY_LINE.read_text(encoding="utf-8-sig")
    assert 'Add-SkippedStep "IL2CPP 独立版构建" "未传 -Il2cpp"' in unity


@pytest.mark.parametrize("rel", [".githooks/pre-commit", ".github/workflows/ci.yml", ".github/workflows/release.yml"])
def test_daily_and_ci_entrypoints_do_not_enable_il2cpp(rel: str) -> None:
    path = REPO_ROOT / rel
    if not path.exists():
        pytest.skip(f"{rel} 不存在")
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if "check.ps1" in line and not line.lstrip().startswith("#"):
            assert "-Il2cpp" not in line, f"{rel}: 日常/CI 入口不得跑 IL2CPP（只进 build.ps1 -Release），行：{line.strip()}"


def test_check_script_passes_results_list_directly_not_wrapped_in_array_subexpression() -> None:
    """``@($script:Results)``（List[Object] 包进 @()）在 PowerShell 5.1 抛 ArgumentException，在 7 里传给
    [object[]] 形参抛 "Argument types do not match"（第四批首跑 check.ps1 汇总段实际踩到）；调用点必须
    直接传 List，形参不得声明成 [object[]]。"""
    text = CHECK_SCRIPT.read_text(encoding="utf-8-sig")
    assert "Get-EnvironmentalSkipRows -Results $script:Results" in text
    assert "Get-EnvironmentalSkipRows -Results @($script:Results)" not in text
    runner = STEP_RUNNER.read_text(encoding="utf-8-sig")
    signature = re.search(r"function Get-EnvironmentalSkipRows \{\s*param\((.*?)\)\s*\n", runner, re.S)
    assert signature and "[object[]]" not in signature.group(1)


def test_gate_line_scripts_parse_cleanly_on_the_selected_host(tmp_path: Path) -> None:
    scripts = [CHECK_SCRIPT, REPO_ROOT / "build.ps1", HEAVY_LINE, UNITY_LINE, STEP_RUNNER, FLOORS_PS,
               TOOLCHAIN_DIR / "_gate_unity_verdicts.ps1", TOOLCHAIN_DIR / "_release_regression_guard.ps1",
               TOOLCHAIN_DIR / "_gate_timing.ps1"]
    body = f"""
$out = [ordered]@{{}}
foreach ($p in @({", ".join(ps_quote(s) for s in scripts)})) {{
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($p, [ref]$tokens, [ref]$errors)
    $out[[System.IO.Path]::GetFileName($p)] = @($errors | ForEach-Object {{ $_.Message }})
}}
$out | ConvertTo-Json -Depth 4 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    parsed = run_ps_json(tmp_path, body, name="parse")
    for name, errors in parsed.items():
        assert _as_list(errors) == [], f"{name} 语法错误：{errors}"
