"""``toolchain/_sim_added_guard.ps1``（``Get-SimRunnerAddedVerdict``）的回归测试（测试覆盖梳理 I-3，
2026-10-01）。

背景（AGENTS.md 第 4 节，一次潜伏 4 天的事故）：``simrunner`` 退出码 0 只承诺"无 Exceeded/Removed"，
**不把 Added（基线里从未记录过的统计量）算作阻断**；2026-09-16 一次新增测试技能漏烘焙 coverage 基线，
Added 差异因此在 31/31 全 PASS 下潜伏 4 天、跨两次发布才被复审发现。根治是门禁
（``toolchain/_gate_line_heavy.ps1`` "数值仿真基线比对"步骤）额外解析 simrunner 每个场景摘要行里的
``added=<n>`` 字段并拦截 n>0。这道护栏此前是内联在门禁线脚本里的一段正则，没有任何自动化测试：
正则被改坏、摘要行格式被改动，门禁都会静默放行。

本文件覆盖抽出的纯函数 ``Get-SimRunnerAddedVerdict``（dot-source 后在子进程里直接调用，不跑完整
门禁）：

1. 伪造输出：added=0 放行；任一场景 added>0 拦截并指出场景与数量；多场景部分为 0 时只列出 >0 的。
2. 缺首行（输出里一行场景摘要都没有）、摘要行格式漂移（以 ``scenario=`` 开头却匹配不上）→ 拦截。
   这是对原内联实现的一处有意收紧：原实现对"解析不出任何摘要行"是静默放行（``$addedScenarios`` 为空
   即通过），与 4 天潜伏事故同属"门禁判据比规则字面更松"；simrunner 退出码 0 时必然至少打印一行
   摘要，解析不到说明格式被改动，理应拦截而不是当作"没有 Added"。
3. 真实联动：用真实 ``SimRunner.dll`` 的标准输出喂函数——零差异放行；把基线里删一行统计量（simrunner
   自身仍退出 0）后函数必须拦截。这一条钉住"simrunner 摘要行格式"与"门禁正则"两端的真实契约。

4. 接线静态检查：门禁线脚本确实 dot-source 了本文件并调用该函数（防止以后又把判定内联回去、
   函数变成无人调用的死代码）。

跨平台说明：1～3 依赖 ``powershell`` / ``pwsh`` 可执行，本机没有时 skip（与仓库其余 .ps1 相关测试
约定一致）；3 另需 dotnet；4 是纯文本断言，任何平台都跑。
"""

from __future__ import annotations

import re
import shutil
import subprocess
from pathlib import Path

import pytest

from _dotnet_cli import REPO_ROOT, get_cli_dll, run_cli
from _ps_subprocess_env import clean_powershell_env

GUARD_SCRIPT = REPO_ROOT / "toolchain" / "_sim_added_guard.ps1"
GATE_SCRIPT = REPO_ROOT / "toolchain" / "_gate_line_heavy.ps1"


def _summary_line(scenario: str, added: int, *, exceeded: int = 0, removed: int = 0, result: str = "PASS") -> str:
    return (
        f"scenario=sim.scenario.{scenario} kind=coverage stats=106 "
        f"exceeded={exceeded} added={added} removed={removed} result={result}"
    )


@pytest.fixture()
def ps_exe() -> str:
    for exe in ("powershell", "pwsh"):
        if shutil.which(exe):
            return exe
    pytest.skip("本机未找到 powershell/pwsh 可执行文件，跳过（与仓库其余 .ps1 相关测试一致约定）")


def _run_verdict(ps_exe: str, tmp_path: Path, lines: list[str]) -> dict:
    """把 ``lines`` 写成 UTF-8 文件，在 PowerShell 子进程里 dot-source 守卫脚本并调用函数，返回解析后的
    判定（Blocked/Reason/SummaryLineCount/Added 列表/Malformed 行数）。"""
    lines_file = tmp_path / "simrunner_output.txt"
    lines_file.write_text("\n".join(lines) + ("\n" if lines else ""), encoding="utf-8", newline="\n")
    script = (
        '$ErrorActionPreference = "Stop"; '
        '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; '
        f'. "{GUARD_SCRIPT}"; '
        f'$lines = @(Get-Content -LiteralPath "{lines_file}" -Encoding UTF8); '
        '$v = Get-SimRunnerAddedVerdict -OutputLines $lines; '
        'Write-Output ("BLOCKED=" + $v.Blocked); '
        'Write-Output ("REASON=" + $v.Reason); '
        'Write-Output ("SUMMARY_LINES=" + $v.SummaryLineCount); '
        'Write-Output ("MALFORMED=" + @($v.MalformedLines).Count); '
        'Write-Output ("ADDED=" + ((@($v.AddedScenarios) | ForEach-Object { $_.Id + "(+" + $_.Count + ")" }) -join ","))'
    )
    proc = subprocess.run(
        [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", env=clean_powershell_env(ps_exe),
    )
    assert proc.returncode == 0, f"守卫脚本调用失败：{proc.stdout}\n{proc.stderr}"
    parsed = dict(line.split("=", 1) for line in proc.stdout.strip().splitlines() if "=" in line)
    return {
        "blocked": parsed["BLOCKED"] == "True",
        "reason": parsed["REASON"],
        "summary_lines": int(parsed["SUMMARY_LINES"]),
        "malformed": int(parsed["MALFORMED"]),
        "added": parsed["ADDED"],
    }


# ---------------------------------------------------------------- 伪造输出


def test_added_zero_passes(tmp_path, ps_exe):
    lines = [
        _summary_line("sim_arena_matrix", 0),
        _summary_line("sim_coverage_all", 0),
        _summary_line("sim_growth_full", 0),
        "RESULT=OK",
    ]
    v = _run_verdict(ps_exe, tmp_path, lines)
    assert v == {"blocked": False, "reason": "", "summary_lines": 3, "malformed": 0, "added": ""}


def test_added_one_blocks_and_names_scenario(tmp_path, ps_exe):
    v = _run_verdict(ps_exe, tmp_path, [_summary_line("sim_coverage_all", 1), "RESULT=OK"])
    assert v["blocked"] is True
    assert v["reason"] == "added"
    assert v["added"] == "sim.scenario.sim_coverage_all(+1)"


def test_only_scenarios_with_added_are_listed(tmp_path, ps_exe):
    lines = [
        _summary_line("sim_arena_matrix", 0),
        _summary_line("sim_coverage_all", 12),
        _summary_line("sim_growth_full", 3),
        "RESULT=OK",
    ]
    v = _run_verdict(ps_exe, tmp_path, lines)
    assert v["blocked"] is True and v["summary_lines"] == 3
    assert v["added"] == "sim.scenario.sim_coverage_all(+12),sim.scenario.sim_growth_full(+3)"


def test_multi_digit_added_is_parsed_as_a_whole_number(tmp_path, ps_exe):
    v = _run_verdict(ps_exe, tmp_path, [_summary_line("sim_coverage_all", 100)])
    assert v["added"] == "sim.scenario.sim_coverage_all(+100)"


# ---------------------------------------------------------------- 缺首行 / 格式漂移 -> 拦截


def test_missing_summary_lines_blocks(tmp_path, ps_exe):
    v = _run_verdict(ps_exe, tmp_path, ["RESULT=OK"])
    assert v["blocked"] is True
    assert v["reason"] == "no_summary_line"
    assert v["summary_lines"] == 0


def test_empty_output_blocks(tmp_path, ps_exe):
    v = _run_verdict(ps_exe, tmp_path, [])
    assert v["blocked"] is True and v["reason"] == "no_summary_line"


def test_renamed_added_field_is_format_drift_and_blocks(tmp_path, ps_exe):
    drifted = "scenario=sim.scenario.sim_coverage_all kind=coverage stats=106 exceeded=0 new=5 removed=0 result=PASS"
    v = _run_verdict(ps_exe, tmp_path, [drifted, "RESULT=OK"])
    assert v["blocked"] is True
    assert v["reason"] == "malformed_summary_line"
    assert v["malformed"] == 1 and v["summary_lines"] == 0


def test_one_malformed_line_among_good_ones_still_blocks(tmp_path, ps_exe):
    drifted = "scenario=sim.scenario.sim_growth_full kind=growth stats=99 added=0 result=PASS"
    lines = [_summary_line("sim_arena_matrix", 0), drifted, _summary_line("sim_coverage_all", 0)]
    v = _run_verdict(ps_exe, tmp_path, lines)
    assert v["blocked"] is True and v["reason"] == "malformed_summary_line"
    assert v["summary_lines"] == 2 and v["malformed"] == 1


def test_noise_lines_are_ignored(tmp_path, ps_exe):
    lines = ["更新基线：x.json", "some log line scenario=inside added=9", _summary_line("sim_coverage_all", 0)]
    v = _run_verdict(ps_exe, tmp_path, lines)
    assert v["blocked"] is False and v["summary_lines"] == 1


# ---------------------------------------------------------------- 与真实 simrunner 联动


def test_real_simrunner_output_zero_diff_passes_and_missing_stat_blocks(tmp_path, ps_exe):
    dll = get_cli_dll("SimRunner")
    real_baseline = REPO_ROOT / "core" / "sim" / "tests" / "baseline"
    baseline_dir = tmp_path / "baseline"
    shutil.copytree(real_baseline, baseline_dir)
    args = [
        "run", "--scenario", "sim_coverage_all",
        "--framework-root", "data/_framework", "--data-root", "core/sim/tests/data",
        "--out", str(tmp_path / "out"), "--baseline-dir", str(baseline_dir),
    ]

    clean = run_cli(dll, args)
    assert clean.returncode == 0, clean.stderr
    v = _run_verdict(ps_exe, tmp_path, clean.stdout.splitlines())
    assert v["blocked"] is False and v["summary_lines"] == 1

    # 删掉基线里一行统计量：simrunner 自身仍退出 0（Added 不阻断），但摘要行 added=1，门禁必须拦。
    baseline_file = baseline_dir / "sim_coverage_all.json"
    lines = baseline_file.read_text(encoding="utf-8").splitlines(keepends=True)
    stat_idx = next(i for i, ln in enumerate(lines) if re.match(r'\s+"coverage\..+": [0-9]', ln))
    del lines[stat_idx]
    baseline_file.write_text("".join(lines), encoding="utf-8", newline="\n")

    drifted = run_cli(dll, args)
    assert drifted.returncode == 0, "前置：Added 单独不应改变 simrunner 退出码（契约见 core/sim/README.md）"
    v = _run_verdict(ps_exe, tmp_path, drifted.stdout.splitlines())
    assert v["blocked"] is True and v["reason"] == "added"
    assert v["added"] == "sim.scenario.sim_coverage_all(+1)"


# ---------------------------------------------------------------- 接线静态检查


def test_gate_line_dot_sources_guard_and_calls_the_function():
    text = GATE_SCRIPT.read_text(encoding="utf-8-sig")
    assert re.search(r'^\s*\.\s+\(Join-Path \$RepoRoot "toolchain\\_sim_added_guard\.ps1"\)\s*$', text, re.M), (
        "门禁线脚本必须 dot-source toolchain\\_sim_added_guard.ps1"
    )
    assert "Get-SimRunnerAddedVerdict -OutputLines" in text, "门禁线脚本必须调用 Get-SimRunnerAddedVerdict"
    # 不许再把 added 正则内联回门禁线脚本（判定只有函数一份）。
    assert "added=(\\d+)" not in text, "added=(\\d+) 正则应只存在于 _sim_added_guard.ps1"
