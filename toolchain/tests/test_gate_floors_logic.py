"""``toolchain/_gate_test_floors.ps1``（门禁测试步骤用例数下限的解析与判定）的回归测试
（2026-10-01，复盘 docs/复盘/测试覆盖梳理-2026-10-01.md 第 4 节 I-2）。

背景：dotnet test / pytest / Unity EditMode / Unity PlayMode 四个门禁步骤此前只看退出码或
result==Passed，整批用例被悄悄丢掉（程序集没编进来、发现规则坏了、批量 Assert.Ignore）时剩下的
照样全绿。现在四步都解析结果文件里的 total/passed/skipped/inconclusive，与
``toolchain/gate_floors.json`` 比较：passed 低于下限 FAIL、skipped+inconclusive 超过允许值 FAIL。

本文件验证的东西（复现 + 不变量各一支，不写死生产下限数字）：
  1. 复现：对三种结果格式（dotnet test 的 trx、pytest 的 junitxml、Unity 的 NUnit XML）造伪造
     夹具——低于下限 -> FAIL；skipped 超过允许值 -> FAIL；inconclusive 计入 skip 预算 -> FAIL；
     failed>0 -> FAIL；正常 -> PASS；skip 恰在允许值内 -> PASS；下限恰等于 passed -> PASS。
  2. 不变量：缺套件登记 / 缺结果文件 / 缺下限文件一律 FAIL（不静默放行）；trx 多个文件求和；
     步骤 Detail 恒含 total/passed/skipped/inconclusive 四个数。
  3. 登记文件与接线：真实 ``toolchain/gate_floors.json`` 四个套件齐全，min_passed 与 max_skipped
     符合"实测 passed 的 90% 向下取整到十位 / 实测 skipped"的取值规则；四个测试步骤确实调用了
     ``Invoke-GateTestFloorCheck``（防止有人删掉接线让下限形同虚设）。

所有 PowerShell 判定在一次 Windows PowerShell 5.1 子进程里批量执行（门禁的实际宿主，
见 ``_ps_subprocess_env.py``），结果落 JSON 文件再读回，避免控制台编码问题。

跨平台说明：本机没有 powershell/pwsh 时判定类用例 skip，不 fail（与仓库其余 .ps1 相关测试一致）。

运行：``python -m pytest toolchain/tests/test_gate_floors_logic.py -q``。
"""

from __future__ import annotations

import json
import math
import shutil
import subprocess
from pathlib import Path

import pytest

from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
HELPER = REPO_ROOT / "toolchain" / "_gate_test_floors.ps1"
FLOORS_JSON = REPO_ROOT / "toolchain" / "gate_floors.json"
HEAVY_LINE = REPO_ROOT / "toolchain" / "_gate_line_heavy.ps1"
UNITY_LINE = REPO_ROOT / "toolchain" / "_gate_line_unity.ps1"
CHECK_SCRIPT = REPO_ROOT / "check.ps1"

SUITES = ("dotnet_test", "pytest", "unity_editmode", "unity_playmode")
KINDS = ("Trx", "JUnit", "NUnit")

# 测试用的伪造下限：与生产 gate_floors.json 的数字无关（生产数字随套件增长会变）。
FAKE_MIN_PASSED = 100
FAKE_MAX_SKIPPED = 2

# (case_id, passed, failed, skipped, inconclusive, expected_ok)
# 所有用例的 total = passed + failed + skipped + inconclusive。
COUNT_CASES = [
    ("normal_pass", 150, 0, 0, 0, True),
    ("passed_equals_floor_pass", FAKE_MIN_PASSED, 0, 0, 0, True),
    ("skip_at_allowance_pass", 150, 0, FAKE_MAX_SKIPPED, 0, True),
    ("below_floor_fail", FAKE_MIN_PASSED - 1, 0, 0, 0, False),
    ("way_below_floor_fail", 3, 0, 0, 0, False),
    ("skip_over_allowance_fail", 150, 0, FAKE_MAX_SKIPPED + 1, 0, False),
    ("failed_present_fail", 150, 1, 0, 0, False),
]
# JUnit（pytest）没有 inconclusive 概念，只对 Trx/NUnit 造"inconclusive 计入 skip 预算"用例。
INCONCLUSIVE_CASES = [
    ("inconclusive_counts_against_skip_budget_fail", 150, 0, FAKE_MAX_SKIPPED, 1, False),
    ("inconclusive_within_budget_pass", 150, 0, 0, FAKE_MAX_SKIPPED, True),
]


# ----------------------------------------------------------------------------
# 伪造结果文件
# ----------------------------------------------------------------------------

def _write_trx(directory: Path, name: str, total: int, passed: int, failed: int,
               skipped: int, inconclusive: int, report_not_executed: bool = True) -> None:
    """report_not_executed=False 模拟 xunit 的真实行为：被 Skip 的用例只体现在 total>executed，
    Counters.notExecuted 仍是 0（2026-10-01 在本仓库 trx 里实测）。"""
    directory.mkdir(parents=True, exist_ok=True)
    not_executed = skipped if report_not_executed else 0
    (directory / name).write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<TestRun id="00000000-0000-0000-0000-000000000000" '
        'xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        '<ResultSummary outcome="Completed">'
        f'<Counters total="{total}" executed="{total - skipped}" passed="{passed}" failed="{failed}" '
        f'error="0" timeout="0" aborted="0" inconclusive="{inconclusive}" passedButRunAborted="0" '
        f'notRunnable="0" notExecuted="{not_executed}" disconnected="0" warning="0" completed="0" '
        'inProgress="0" pending="0" />'
        '</ResultSummary></TestRun>\n',
        encoding="utf-8",
    )


def _write_junit(path: Path, total: int, failed: int, skipped: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<testsuites name="pytest tests">'
        f'<testsuite name="pytest" errors="0" failures="{failed}" skipped="{skipped}" tests="{total}" '
        'time="1.0"></testsuite></testsuites>\n',
        encoding="utf-8",
    )


def _write_nunit(path: Path, total: int, passed: int, failed: int, skipped: int,
                 inconclusive: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    result = "Passed" if failed == 0 else "Failed"
    path.write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f'<test-run id="2" testcasecount="{total}" result="{result}" total="{total}" passed="{passed}" '
        f'failed="{failed}" inconclusive="{inconclusive}" skipped="{skipped}" asserts="0" '
        'engine-version="3.5.0.0" clr-version="4.0.30319.42000"></test-run>\n',
        encoding="utf-8",
    )


def _write_floors(path: Path, suites: tuple[str, ...] = SUITES) -> None:
    path.write_text(
        json.dumps(
            {
                "_comment": ["伪造下限，仅供 test_gate_floors_logic.py"],
                "suites": {
                    s: {"min_passed": FAKE_MIN_PASSED, "max_skipped": FAKE_MAX_SKIPPED} for s in suites
                },
            },
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )


def _build_case(root: Path, kind: str, case_id: str, passed: int, failed: int, skipped: int,
                inconclusive: int) -> Path:
    total = passed + failed + skipped + inconclusive
    target = root / f"{kind}_{case_id}"
    if kind == "Trx":
        _write_trx(target, "a.trx", total, passed, failed, skipped, inconclusive)
        return target
    target = target.with_suffix(".xml")
    if kind == "JUnit":
        _write_junit(target, total, failed, skipped)
    else:
        _write_nunit(target, total, passed, failed, skipped, inconclusive)
    return target


# ----------------------------------------------------------------------------
# 批量 PowerShell 判定
# ----------------------------------------------------------------------------

_DRIVER = r"""
$ErrorActionPreference = "Stop"
. '__HELPER__'
$manifest = Get-Content -LiteralPath '__MANIFEST__' -Raw -Encoding UTF8 | ConvertFrom-Json
$out = @()
foreach ($c in @($manifest)) {
    $r = Invoke-GateTestFloorCheck -Kind $c.kind -Path $c.path -Suite $c.suite -FloorsPath $c.floors
    $out += [PSCustomObject]@{ name = $c.name; ok = [bool]$r.Ok; detail = [string]$r.Detail }
}
ConvertTo-Json -InputObject @($out) -Depth 4 | Out-File -LiteralPath '__RESULT__' -Encoding utf8
"""


def _find_powershell() -> str | None:
    for exe in ("powershell", "pwsh"):
        if shutil.which(exe):
            return exe
    return None


def _run_manifest(ps_exe: str, tmp: Path, manifest: list[dict]) -> dict[str, dict]:
    manifest_path = tmp / "manifest.json"
    result_path = tmp / "result.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
    driver = (
        _DRIVER.replace("__HELPER__", str(HELPER).replace("'", "''"))
        .replace("__MANIFEST__", str(manifest_path).replace("'", "''"))
        .replace("__RESULT__", str(result_path).replace("'", "''"))
    )
    proc = subprocess.run(
        [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", driver],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        env=clean_powershell_env(ps_exe),
        timeout=120,
    )
    assert proc.returncode == 0, f"stdout={proc.stdout}\nstderr={proc.stderr}"
    rows = json.loads(result_path.read_text(encoding="utf-8-sig"))
    return {row["name"]: row for row in rows}


@pytest.fixture(scope="module")
def verdicts(tmp_path_factory: pytest.TempPathFactory) -> dict[str, dict]:
    ps_exe = _find_powershell()
    if ps_exe is None:
        pytest.skip("本机未找到 powershell/pwsh 可执行文件，跳过（与仓库其余 .ps1 相关测试一致约定）")
    tmp = tmp_path_factory.mktemp("gate_floors")
    floors = tmp / "floors.json"
    _write_floors(floors)
    manifest: list[dict] = []

    for kind in KINDS:
        cases = list(COUNT_CASES)
        if kind != "JUnit":
            cases += INCONCLUSIVE_CASES
        for case_id, passed, failed, skipped, inconclusive, _expected in cases:
            path = _build_case(tmp, kind, case_id, passed, failed, skipped, inconclusive)
            manifest.append({"name": f"{kind}:{case_id}", "kind": kind, "path": str(path),
                             "suite": "dotnet_test", "floors": str(floors)})

    # trx 多文件求和：三个测试工程各 40 通过 -> 合计 120，刚好过 100 的下限；单个文件都不够。
    multi = tmp / "Trx_multi"
    for i in range(3):
        _write_trx(multi, f"p{i}.trx", 40, 40, 0, 0, 0)
    manifest.append({"name": "Trx:multi_file_sum_pass", "kind": "Trx", "path": str(multi),
                     "suite": "dotnet_test", "floors": str(floors)})

    # xunit 风格的 Skip（notExecuted 计数为 0，只有 total>executed）也要算进 skip 预算。
    xunit = tmp / "Trx_xunit_style_skip"
    _write_trx(xunit, "a.trx", 150 + FAKE_MAX_SKIPPED + 1, 150, 0, FAKE_MAX_SKIPPED + 1, 0,
               report_not_executed=False)
    manifest.append({"name": "Trx:xunit_style_skip_over_allowance_fail", "kind": "Trx", "path": str(xunit),
                     "suite": "dotnet_test", "floors": str(floors)})
    xunit_ok = tmp / "Trx_xunit_style_skip_ok"
    _write_trx(xunit_ok, "a.trx", 150 + FAKE_MAX_SKIPPED, 150, 0, FAKE_MAX_SKIPPED, 0,
               report_not_executed=False)
    manifest.append({"name": "Trx:xunit_style_skip_within_allowance_pass", "kind": "Trx", "path": str(xunit_ok),
                     "suite": "dotnet_test", "floors": str(floors)})

    # 不变量：缺失类一律 FAIL。
    good = _build_case(tmp, "NUnit", "for_missing", 150, 0, 0, 0)
    manifest.append({"name": "missing_suite_registration", "kind": "NUnit", "path": str(good),
                     "suite": "no_such_suite", "floors": str(floors)})
    manifest.append({"name": "missing_result_file", "kind": "NUnit", "path": str(tmp / "nope.xml"),
                     "suite": "unity_editmode", "floors": str(floors)})
    manifest.append({"name": "missing_trx_dir", "kind": "Trx", "path": str(tmp / "nope_dir"),
                     "suite": "dotnet_test", "floors": str(floors)})
    empty_dir = tmp / "empty_trx_dir"
    empty_dir.mkdir()
    manifest.append({"name": "empty_trx_dir", "kind": "Trx", "path": str(empty_dir),
                     "suite": "dotnet_test", "floors": str(floors)})
    manifest.append({"name": "missing_floors_file", "kind": "NUnit", "path": str(good),
                     "suite": "unity_editmode", "floors": str(tmp / "no_floors.json")})

    # 四个套件名各自都能取到登记（用同一份伪造下限，逐个套件名判一次）。
    for suite in SUITES:
        manifest.append({"name": f"suite_registered:{suite}", "kind": "NUnit", "path": str(good),
                         "suite": suite, "floors": str(floors)})

    return _run_manifest(ps_exe, tmp, manifest)


# ----------------------------------------------------------------------------
# 1. 复现：三种格式 x 各类计数
# ----------------------------------------------------------------------------

def _param_cases():
    for kind in KINDS:
        cases = list(COUNT_CASES)
        if kind != "JUnit":
            cases += INCONCLUSIVE_CASES
        for case_id, passed, failed, skipped, inconclusive, expected in cases:
            yield pytest.param(kind, case_id, passed, skipped, inconclusive, expected,
                               id=f"{kind}-{case_id}")


@pytest.mark.parametrize("kind,case_id,passed,skipped,inconclusive,expected_ok", list(_param_cases()))
def test_floor_verdict_per_format(verdicts, kind, case_id, passed, skipped, inconclusive, expected_ok):
    row = verdicts[f"{kind}:{case_id}"]
    assert row["ok"] is expected_ok, row["detail"]
    # 不变量：无论成败，Detail 都带四个数，且数值与夹具一致。
    assert f"passed={passed}" in row["detail"], row["detail"]
    assert f"skipped={skipped}" in row["detail"], row["detail"]
    assert f"inconclusive={inconclusive}" in row["detail"], row["detail"]
    assert "total=" in row["detail"], row["detail"]


def test_multiple_trx_files_are_summed(verdicts):
    row = verdicts["Trx:multi_file_sum_pass"]
    assert row["ok"] is True, row["detail"]
    assert "passed=120" in row["detail"], row["detail"]


def test_xunit_style_skips_are_counted_via_total_minus_executed(verdicts):
    over = verdicts["Trx:xunit_style_skip_over_allowance_fail"]
    assert over["ok"] is False, over["detail"]
    assert f"skipped={FAKE_MAX_SKIPPED + 1}" in over["detail"], over["detail"]
    within = verdicts["Trx:xunit_style_skip_within_allowance_pass"]
    assert within["ok"] is True, within["detail"]
    assert f"skipped={FAKE_MAX_SKIPPED}" in within["detail"], within["detail"]


# ----------------------------------------------------------------------------
# 2. 不变量：缺失一律 FAIL
# ----------------------------------------------------------------------------

@pytest.mark.parametrize("name", [
    "missing_suite_registration",
    "missing_result_file",
    "missing_trx_dir",
    "empty_trx_dir",
    "missing_floors_file",
])
def test_missing_inputs_fail_instead_of_passing_silently(verdicts, name):
    assert verdicts[name]["ok"] is False, verdicts[name]["detail"]


@pytest.mark.parametrize("suite", SUITES)
def test_each_suite_name_is_a_valid_registration_key(verdicts, suite):
    assert verdicts[f"suite_registered:{suite}"]["ok"] is True, verdicts[f"suite_registered:{suite}"]["detail"]


# ----------------------------------------------------------------------------
# 3. 真实登记文件与接线
# ----------------------------------------------------------------------------

def _load_real_floors() -> dict:
    return json.loads(FLOORS_JSON.read_text(encoding="utf-8-sig"))


def test_real_floors_file_has_all_suites_and_follows_the_rule():
    floors = _load_real_floors()["suites"]
    assert set(floors) == set(SUITES)
    for suite, entry in floors.items():
        measured = entry["measured_passed"]
        assert entry["min_passed"] == math.floor(measured * 0.9 / 10) * 10, (
            f"{suite}: min_passed 应为实测 passed 的 90% 向下取整到十位（取值规则见 check.ps1 头部判断记录 7)）"
        )
        assert entry["min_passed"] > 0
        assert entry["max_skipped"] == entry["measured_skipped"], (
            f"{suite}: max_skipped 应等于实测 skipped 数"
        )


def test_gate_lines_are_wired_to_the_floor_check():
    heavy = HEAVY_LINE.read_text(encoding="utf-8-sig")
    unity = UNITY_LINE.read_text(encoding="utf-8-sig")
    assert '-Kind Trx' in heavy and '-Suite "dotnet_test"' in heavy
    assert '-Kind JUnit' in heavy and '-Suite "pytest"' in heavy
    assert '-Kind NUnit' in unity and '-Suite "unity_editmode"' in unity
    assert '-Suite "unity_playmode"' in unity
    for text in (heavy, unity):
        assert "_gate_test_floors.ps1" in text


def test_dotnet_test_step_name_does_not_hardcode_project_count():
    heavy = HEAVY_LINE.read_text(encoding="utf-8-sig")
    assert "六工程，含 Perf" not in heavy, "测试工程数应由脚本从 Core.sln 数出来（复盘 I-11）"
    assert "$TestProjectCount" in heavy


def test_check_script_runs_template_data_root_in_the_cheap_batch():
    text = CHECK_SCRIPT.read_text(encoding="utf-8-sig")
    marker = "--data-root games/_template/data/game"
    assert marker in text
    # 不能被 -Quick/-SkipUnity 门控：调用点不得落在 `if ($Quick)` / `if ($SkipUnity)` 分支里，
    # 且必须出现在两条并行线派生之前（"便宜的先跑"那批）。
    step_pos = text.index(marker)
    parallel_pos = text.index("阶段二：两条线并行")
    assert step_pos < parallel_pos
