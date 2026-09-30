"""``toolchain/simrunner``（数值仿真命令行入口）的进程级契约测试（测试覆盖梳理 I-3，2026-10-01）。

背景：``toolchain/_gate_line_heavy.ps1`` 的"数值仿真基线比对"步骤和 ``toolchain/sim_baseline.ps1``
都靠 ``simrunner`` 的退出码与场景摘要行判定基线是否漂移，但此前没有任何自动化用例把这个 CLI 当黑盒
跑一遍——退出码语义、输出格式、``--update-baseline``/``--runs``/``--progress`` 三个开关全靠人工验证。
本文件用子进程跑真实构建的 ``SimRunner.dll``（定位/构建方式见 ``_dotnet_cli.py``），数据集与基线与
门禁完全一致（``data/_framework`` + ``core/sim/tests/data`` + ``core/sim/tests/baseline``，工作目录
仓库根），基线一律先拷到 tmp 再改，绝不动仓库里的基线文件。

退出码契约（``toolchain/simrunner/Program.cs`` 类型头判断记录，本文件逐一实测）：

    0 无 Exceeded/Removed（**Added 不阻断**——门禁另行解析 ``added=<n>``，见
      ``test_gate_sim_added_guard.py``）；1 存在 Exceeded/Removed；2 参数/数据装载错误；
    3 传了 ``--baseline-dir`` 但基线文件缺失且未传 ``--update-baseline``；4 Ctrl+C 取消。

退出码 4 需要向子进程发送控制台 Ctrl+C 信号，跨平台且会波及 pytest 自身进程组，本文件不覆盖。

用例只跑 ``sim_coverage_all`` 一个场景（完整 runs 约 1.5 秒，``--runs 2`` 更快），不跑 ``all``
（约 20 秒，那是门禁的职责）。
"""

from __future__ import annotations

import json
import re
import shutil
from pathlib import Path

import pytest

from _dotnet_cli import REPO_ROOT, get_cli_dll, run_cli

SCENARIO = "sim_coverage_all"
SCENARIO_FULL_ID = "sim.scenario.sim_coverage_all"
REAL_BASELINE_DIR = REPO_ROOT / "core" / "sim" / "tests" / "baseline"

SUMMARY_RE = re.compile(
    r"^scenario=(?P<id>\S+) kind=(?P<kind>\S+) stats=(?P<stats>\d+) exceeded=(?P<exceeded>\d+) "
    r"added=(?P<added>\d+) removed=(?P<removed>\d+) result=(?P<result>PASS|FAIL)$"
)


@pytest.fixture(scope="module")
def dll() -> Path:
    return get_cli_dll("SimRunner")


@pytest.fixture()
def env(tmp_path: Path) -> dict:
    """tmp 输出目录 + 真实基线的 tmp 拷贝。"""
    baseline_dir = tmp_path / "baseline"
    shutil.copytree(REAL_BASELINE_DIR, baseline_dir)
    out_dir = tmp_path / "out"
    return {"tmp": tmp_path, "baseline": baseline_dir, "out": out_dir}


def _common_args(out_dir: Path, scenario: str = SCENARIO) -> list[str]:
    return [
        "run", "--scenario", scenario,
        "--framework-root", "data/_framework",
        "--data-root", "core/sim/tests/data",
        "--out", str(out_dir),
    ]


def _summary(stdout: str) -> re.Match:
    lines = stdout.splitlines()
    assert lines, "标准输出为空"
    m = SUMMARY_RE.match(lines[0])
    assert m, f"首行不是场景摘要行格式：{lines[0]!r}"
    return m


def _baseline_path(env: dict) -> Path:
    return env["baseline"] / f"{SCENARIO}.json"


def _first_positive_stat_line(baseline_text: str) -> tuple[str, float]:
    """基线文件里每条统计量占一行；返回第一条数值大于 0 的统计量路径与值。"""
    stats = json.loads(baseline_text)["stats"]
    for path, value in stats.items():
        if isinstance(value, (int, float)) and value > 0:
            return path, float(value)
    raise AssertionError("基线里没有正数统计量，无法构造扰动")


def _edit_one_stat_value(baseline_file: Path, path: str, new_value: float) -> None:
    text = baseline_file.read_text(encoding="utf-8")
    pattern = re.compile(r'^(\s*"' + re.escape(path) + r'":\s*)[^,\n]+(,?)$', re.M)
    new_text, n = pattern.subn(lambda m: f"{m.group(1)}{new_value!r}{m.group(2)}", text)
    assert n == 1, f"应恰好改动 1 行，实际 {n}"
    baseline_file.write_text(new_text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------- 参数错误 -> 2


def test_no_arguments_exits_2_with_usage(dll):
    proc = run_cli(dll, [])
    assert proc.returncode == 2
    assert "用法" in proc.stderr


def test_first_argument_must_be_run(dll):
    proc = run_cli(dll, ["execute", "--scenario", "all"])
    assert proc.returncode == 2
    assert "用法" in proc.stderr


@pytest.mark.parametrize(
    "dropped",
    ["--scenario", "--framework-root", "--data-root", "--out"],
)
def test_missing_required_argument_exits_2(dll, tmp_path, dropped):
    full = {
        "--scenario": SCENARIO,
        "--framework-root": "data/_framework",
        "--data-root": "core/sim/tests/data",
        "--out": str(tmp_path / "out"),
    }
    args = ["run"]
    for key, value in full.items():
        if key != dropped:
            args += [key, value]
    proc = run_cli(dll, args)
    assert proc.returncode == 2
    assert "参数错误" in proc.stderr
    assert dropped in proc.stderr


def test_unknown_argument_exits_2(dll, env):
    proc = run_cli(dll, _common_args(env["out"]) + ["--no-such-flag"])
    assert proc.returncode == 2
    assert "未知参数" in proc.stderr and "--no-such-flag" in proc.stderr


@pytest.mark.parametrize("option", ["--scenario", "--framework-root", "--data-root", "--out", "--baseline-dir", "--runs", "--version"])
def test_option_without_value_exits_2(dll, option):
    proc = run_cli(dll, ["run", option])
    assert proc.returncode == 2
    assert "参数错误" in proc.stderr


@pytest.mark.parametrize("bad_runs", ["0", "-3", "abc", "1.5", ""])
def test_invalid_runs_value_exits_2(dll, env, bad_runs):
    proc = run_cli(dll, _common_args(env["out"]) + ["--runs", bad_runs])
    assert proc.returncode == 2
    assert "--runs" in proc.stderr


def test_update_baseline_without_baseline_dir_exits_2(dll, env):
    proc = run_cli(dll, _common_args(env["out"]) + ["--update-baseline"])
    assert proc.returncode == 2
    assert "--update-baseline" in proc.stderr and "--baseline-dir" in proc.stderr


def test_unknown_scenario_exits_2(dll, env):
    proc = run_cli(dll, _common_args(env["out"], scenario="no_such_scenario"))
    assert proc.returncode == 2
    assert "sim.scenario.no_such_scenario" in proc.stderr


def test_nonexistent_data_root_exits_2(dll, env):
    args = _common_args(env["out"])
    args[args.index("core/sim/tests/data")] = str(env["tmp"] / "no_such_dir")
    proc = run_cli(dll, args)
    assert proc.returncode == 2
    assert "目录不存在" in proc.stderr


def test_data_root_without_any_scenario_exits_2(dll, env):
    empty_root = env["tmp"] / "empty_root"
    empty_root.mkdir()
    args = _common_args(env["out"])
    args[args.index("core/sim/tests/data")] = str(empty_root)
    proc = run_cli(dll, args)
    assert proc.returncode == 2
    assert "sim.scenario" in proc.stderr


# ---------------------------------------------------------------- 比对与退出码


def test_zero_diff_against_real_baseline_exits_0_and_first_line_is_summary(dll, env):
    baseline_stats = json.loads(_baseline_path(env).read_text(encoding="utf-8"))["stats"]

    proc = run_cli(dll, _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"])])

    assert proc.returncode == 0, proc.stderr
    m = _summary(proc.stdout)
    assert m["id"] == SCENARIO_FULL_ID
    assert m["kind"] == "coverage"
    assert int(m["stats"]) == len(baseline_stats)  # 期望由基线文件现算，不写死
    assert (m["exceeded"], m["added"], m["removed"], m["result"]) == ("0", "0", "0", "PASS")
    assert proc.stdout.splitlines()[-1] == "RESULT=OK"

    for suffix in ("report.json", "diff.txt", "diff.json"):
        assert (env["out"] / f"{SCENARIO}.{suffix}").is_file(), suffix
    diff_first = (env["out"] / f"{SCENARIO}.diff.txt").read_text(encoding="utf-8").splitlines()[0]
    assert re.search(r"\badded=0 removed=0$", diff_first), diff_first


def test_scenario_accepts_short_and_full_id(dll, env):
    for scenario in (SCENARIO, SCENARIO_FULL_ID):
        proc = run_cli(dll, _common_args(env["out"], scenario=scenario))
        assert proc.returncode == 0, proc.stderr
        assert _summary(proc.stdout)["id"] == SCENARIO_FULL_ID
    # 未传 --baseline-dir：只产出报告，不比对（摘要行仍在，计数全 0，PASS）。
    assert (env["out"] / f"{SCENARIO}.report.json").is_file()
    assert not (env["out"] / f"{SCENARIO}.diff.txt").exists()


def test_perturbed_baseline_line_exits_1_with_exceeded_row(dll, env):
    baseline_file = _baseline_path(env)
    path, value = _first_positive_stat_line(baseline_file.read_text(encoding="utf-8"))
    _edit_one_stat_value(baseline_file, path, value * 10 + 1)

    proc = run_cli(dll, _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"])])

    assert proc.returncode == 1
    m = _summary(proc.stdout)
    assert (m["exceeded"], m["added"], m["removed"], m["result"]) == ("1", "0", "0", "FAIL")
    assert proc.stdout.splitlines()[-1] == "RESULT=FAIL"
    diff_text = (env["out"] / f"{SCENARIO}.diff.txt").read_text(encoding="utf-8")
    assert re.search(r"^Exceeded\s+" + re.escape(path) + r"\s", diff_text, re.M)


def test_baseline_with_extra_stat_exits_1_removed(dll, env):
    baseline_file = _baseline_path(env)
    data = json.loads(baseline_file.read_text(encoding="utf-8"))
    ghost = "coverage.creature.creature.ghost_not_in_current.ttk_seconds"
    text = baseline_file.read_text(encoding="utf-8")
    text = text.replace('  "stats": {\n', f'  "stats": {{\n    "{ghost}": 3.5,\n', 1)
    assert ghost not in data["stats"] and ghost in text
    baseline_file.write_text(text, encoding="utf-8", newline="\n")

    proc = run_cli(dll, _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"])])

    assert proc.returncode == 1
    m = _summary(proc.stdout)
    assert (m["exceeded"], m["added"], m["removed"], m["result"]) == ("0", "0", "1", "FAIL")
    assert re.search(r"^Removed\s+" + re.escape(ghost), (env["out"] / f"{SCENARIO}.diff.txt").read_text(encoding="utf-8"), re.M)


def test_baseline_missing_one_stat_line_is_added_and_does_not_block_exit_code(dll, env):
    """契约钉子：Added 单独不改变退出码（仍为 0），只体现在摘要行 ``added=<n>``——这正是门禁必须另行
    解析该字段的原因（AGENTS.md 第 4 节 4 天潜伏事故）。若哪天 simrunner 把 Added 也算阻断，本用例
    会红，届时门禁的额外拦截与本用例一起更新。"""
    baseline_file = _baseline_path(env)
    path, _ = _first_positive_stat_line(baseline_file.read_text(encoding="utf-8"))
    lines = baseline_file.read_text(encoding="utf-8").splitlines(keepends=True)
    kept = [ln for ln in lines if not ln.lstrip().startswith(f'"{path}":')]
    assert len(kept) == len(lines) - 1
    # 删掉的若是最后一条统计量，前一行的尾逗号会残留；测试选的是第一条正数统计量，非末条即可，
    # 这里显式断言 JSON 仍合法，避免夹具本身写坏。
    baseline_file.write_text("".join(kept), encoding="utf-8", newline="\n")
    json.loads(baseline_file.read_text(encoding="utf-8"))

    proc = run_cli(dll, _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"])])

    assert proc.returncode == 0
    m = _summary(proc.stdout)
    assert (m["exceeded"], m["added"], m["removed"], m["result"]) == ("0", "1", "0", "PASS")
    assert re.search(r"^Added\s+" + re.escape(path), (env["out"] / f"{SCENARIO}.diff.txt").read_text(encoding="utf-8"), re.M)


def test_missing_baseline_file_exits_3(dll, env):
    _baseline_path(env).unlink()

    proc = run_cli(dll, _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"])])

    assert proc.returncode == 3
    assert "缺少基线" in proc.stderr
    assert proc.stdout.splitlines()[-1] == "RESULT=FAIL"
    assert _summary(proc.stdout)["result"] == "FAIL"


def test_json_flag_prints_diff_json_before_summary(dll, env):
    proc = run_cli(dll, _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"]), "--json"])

    assert proc.returncode == 0
    start = proc.stdout.index("{")
    end = proc.stdout.rindex("}") + 1
    diff = json.loads(proc.stdout[start:end])
    assert diff["scenario_id"] == SCENARIO_FULL_ID
    assert diff["added_count"] == 0 and diff["removed_count"] == 0 and diff["exceeded_count"] == 0
    assert SUMMARY_RE.search(proc.stdout[end:].strip().splitlines()[0])


# ---------------------------------------------------------------- --update-baseline


def test_update_baseline_then_rerun_exits_0(dll, env):
    baseline_file = _baseline_path(env)
    path, value = _first_positive_stat_line(baseline_file.read_text(encoding="utf-8"))
    _edit_one_stat_value(baseline_file, path, value * 10 + 1)
    args = _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"]), "--version", "9.9.9-test"]

    assert run_cli(dll, args).returncode == 1  # 前置：改坏的基线确实红

    update = run_cli(dll, args + ["--update-baseline"])
    assert update.returncode == 0, update.stderr
    assert "更新基线" in update.stdout
    updated = json.loads(baseline_file.read_text(encoding="utf-8"))
    assert updated["generated_with_version"] == "9.9.9-test"
    # 覆盖后的该条统计量回到仓库真实基线里的值（数据集与代码未变，输出可复现）。
    real = json.loads((REAL_BASELINE_DIR / f"{SCENARIO}.json").read_text(encoding="utf-8"))
    assert updated["stats"][path] == pytest.approx(real["stats"][path])

    rerun = run_cli(dll, args)
    assert rerun.returncode == 0, rerun.stdout
    m = _summary(rerun.stdout)
    assert (m["exceeded"], m["added"], m["removed"], m["result"]) == ("0", "0", "0", "PASS")


def test_update_baseline_creates_missing_baseline_file(dll, env):
    _baseline_path(env).unlink()
    args = _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"])]

    proc = run_cli(dll, args + ["--update-baseline"])

    assert proc.returncode == 0, proc.stderr
    assert "新建基线" in proc.stdout
    assert _baseline_path(env).is_file()
    assert run_cli(dll, args).returncode == 0


# ---------------------------------------------------------------- --runs / --progress


def test_runs_override_is_recorded_and_roundtrips_through_update_baseline(dll, env):
    args = _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"])]

    plain = run_cli(dll, _common_args(env["out"]))
    assert plain.returncode == 0
    assert json.loads((env["out"] / f"{SCENARIO}.report.json").read_text(encoding="utf-8"))["runs_override"] is None

    # --runs 2：报告里记录覆盖值；用它更新基线后，同样的 --runs 2 重跑零差异。
    update = run_cli(dll, args + ["--runs", "2", "--update-baseline"])
    assert update.returncode == 0, update.stderr
    report = json.loads((env["out"] / f"{SCENARIO}.report.json").read_text(encoding="utf-8"))
    assert report["runs_override"] == 2
    rerun = run_cli(dll, args + ["--runs", "2"])
    assert rerun.returncode == 0, rerun.stdout
    assert _summary(rerun.stdout)["exceeded"] == "0"


def test_progress_flag_writes_progress_lines_to_stderr_only(dll, env):
    with_progress = run_cli(dll, _common_args(env["out"]) + ["--runs", "2", "--progress"])
    assert with_progress.returncode == 0

    progress_re = re.compile(r"^PROGRESS scenario=(\S+) stage=(\S+) completed=(\d+) total=(\d+)$")
    progress = [progress_re.match(ln) for ln in with_progress.stderr.splitlines() if ln.startswith("PROGRESS")]
    assert progress and all(progress), with_progress.stderr[:500]
    assert all(m.group(1) == SCENARIO for m in progress)
    # 契约（core/sim SimProgress）：Completed 跨阶段单调递增（不逐阶段清零），Total 全程不变，最后一条
    # completed == total；阶段名随所处阶段切换。期望值由输出自身的 total 现算。
    totals = {int(m.group(4)) for m in progress}
    assert len(totals) == 1, totals
    total = totals.pop()
    assert [int(m.group(3)) for m in progress] == list(range(1, total + 1))
    stages = [m.group(2) for m in progress]
    assert stages == sorted(stages, key=["coverage.skill", "coverage.item", "coverage.creature"].index)
    assert "PROGRESS" not in with_progress.stdout

    without = run_cli(dll, _common_args(env["out"]) + ["--runs", "2"])
    assert "PROGRESS" not in without.stderr


# ---------------------------------------------------------------- 已知缺陷（待设计层确认）


@pytest.mark.xfail(
    strict=True,
    reason="已知缺陷：基线文件损坏（非法 JSON / 缺字段）时 toolchain/simrunner/Program.cs 的 RunCommand 只捕获 "
    "InvalidOperationException/ArgumentException/DirectoryNotFoundException，SimBaseline.Parse 抛出的 "
    "JsonParseException/KeyNotFoundException 未被捕获，进程以 Unhandled exception 崩溃而不是按'数据装载阻断'退出码 2。"
    "本用例断言期望行为；修复后 strict xfail 会转红，届时删掉 xfail 标记。待设计层确认修法。",
)
@pytest.mark.parametrize("content", ["{bad", '{"schema_version": 1}'])
def test_corrupt_baseline_file_should_exit_2_not_crash(dll, env, content):
    _baseline_path(env).write_text(content, encoding="utf-8")

    proc = run_cli(dll, _common_args(env["out"]) + ["--baseline-dir", str(env["baseline"])])

    assert "Unhandled exception" not in proc.stderr
    assert proc.returncode == 2
