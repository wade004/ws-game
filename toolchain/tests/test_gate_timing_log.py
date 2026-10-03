"""门禁耗时自动记录（``toolchain/_gate_timing.ps1`` + ``_gate_step_runner.ps1`` 的 start/end 记录）与
耗时统计脚本（``toolchain/timing_report.py``）的测试（"门禁耗时自动记录"切片，2026-10-01，AGENTS.md 1c）。

两类用例（测试 = 复现 + 不变量各至少一条）：

- **复现用例**：用真实的步骤运行器（``Invoke-CheckStep``/``Add-SkippedStep``）在主进程 + 两个真正并行的
  ``Start-Job`` 子线里跑一批带 ``-Id`` 的合成步骤（睡眠 + 通过/失败/跳过），经 ``Write-GateResultsJson`` ->
  ``Import-GateLineResults`` 合并，再 ``Write-GateTimingFromRun`` 写进临时 git 仓库的 ``timing/``：行字段齐全且
  顺序固定、step 是 Id 不是中文显示名、start/end 是真实起止（end-start 约等于 seconds；两条并行线的步骤时间
  真的重叠；同一线内后一步 start 晚于前一步 end）、末行是 ``_total``、文件名按分支去前缀。
- **不变量用例**：任意行集合下统计脚本的合计 = 各行 seconds 之和（phase 汇总不含 ``_total`` 行）、
  中位数 <= P90 <= 最大值、次数 = 行数；过滤结果是全集的子集且各自满足不变量。

另有静态用例：所有步骤调用点都带 ``-Id``（没有 Id 的步骤会在时间记录里退化成中文显示名，跨任务汇总失效）、
``-NoTiming`` 接线（预提交钩子、``build.ps1 -Release``）、写入失败不抛异常、JSON 特殊字符往返、分支简名
PowerShell 与 Python 两份实现口径一致。

跨平台说明：依赖 PowerShell 宿主与 git，本机没有时 skip（与仓库其余 .ps1 相关测试一致）。

运行：``python -m pytest toolchain/tests/test_gate_timing_log.py -q``。
"""

from __future__ import annotations

import json
import random
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

import pytest

TESTS_DIR = Path(__file__).resolve().parent
TOOLCHAIN_DIR = TESTS_DIR.parent
REPO_ROOT = TOOLCHAIN_DIR.parent
for _p in (str(TOOLCHAIN_DIR), str(TESTS_DIR)):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import timing_report as tr  # noqa: E402
from _git_env import init_temp_repo, run_git  # noqa: E402
from _ps_harness import ps_quote, run_ps_json  # noqa: E402

TIMING_PS1 = TOOLCHAIN_DIR / "_gate_timing.ps1"
RUNNER_PS1 = TOOLCHAIN_DIR / "_gate_step_runner.ps1"
CHECK_PS1 = REPO_ROOT / "check.ps1"
HEAVY_PS1 = TOOLCHAIN_DIR / "_gate_line_heavy.ps1"
UNITY_PS1 = TOOLCHAIN_DIR / "_gate_line_unity.ps1"
BUILD_PS1 = REPO_ROOT / "build.ps1"
PRE_COMMIT = REPO_ROOT / ".githooks" / "pre-commit"

FIELDS = ["task", "branch", "phase", "step", "start", "end", "seconds", "result", "note"]


def _parse_ts(text: str) -> datetime:
    return datetime.strptime(text, "%Y-%m-%dT%H:%M:%S")


def _init_repo(path: Path, branch: str) -> Path:
    init_temp_repo(path, branch=branch)
    (path / "f.txt").write_text("x\n", encoding="utf-8")
    run_git(path, "add", "f.txt")
    run_git(path, "commit", "-q", "-m", "init")
    return path


# ---------------------------------------------------------------------------
# 复现：模拟一次门禁运行（主进程步骤 + 两条真正并行的线），读回写出的 jsonl
# ---------------------------------------------------------------------------

_SIMULATED_RUN = r"""
$Toolchain = %TOOLCHAIN%
$Repo = %REPO%
$JsonHeavy = %JSON_HEAVY%
$JsonUnity = %JSON_UNITY%

$DocsOnly = $false
$FailFast = $false
$script:Results = New-Object System.Collections.Generic.List[Object]
$script:GateFailed = $false
$script:FailFastFlagPath = $null
. (Join-Path $Toolchain "_gate_step_runner.ps1")
. (Join-Path $Toolchain "_gate_timing.ps1")

$runStart = Get-Date
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# 主进程自己的"快速前置阶段"：一个通过的步骤 + 一个开关跳过的步骤
Invoke-CheckStep "快速前置步骤（中文显示名）" -Id "pre_step" { Start-Sleep -Milliseconds 300; $true }
Add-SkippedStep "被开关跳过的步骤（中文显示名）" "-Quick" -Id "skip_step"

# 两条并行线：各自子进程、各自在步骤前后取真实时间；线内串行
$lineBody = {
    param($Toolchain, $JsonPath, $Specs)
    $DocsOnly = $false
    $FailFast = $false
    $script:Results = New-Object System.Collections.Generic.List[Object]
    $script:GateFailed = $false
    $script:FailFastFlagPath = $null
    . (Join-Path $Toolchain "_gate_step_runner.ps1")
    try {
        foreach ($spec in $Specs) {
            $ms = [int]$spec.Ms
            $ok = [bool]$spec.Ok
            $action = { Start-Sleep -Milliseconds $ms; $ok }.GetNewClosure()
            Invoke-CheckStep ("线内步骤中文名 " + $spec.Id) -Id ([string]$spec.Id) $action
        }
    } finally {
        Write-GateResultsJson -Path $JsonPath
    }
}
$heavySpecs = @(@{ Id = "h1"; Ms = 3000; Ok = $true }, @{ Id = "h2"; Ms = 500; Ok = $false })
$unitySpecs = @(@{ Id = "u1"; Ms = 3000; Ok = $true })
$jobH = Start-Job -ScriptBlock $lineBody -ArgumentList $Toolchain, $JsonHeavy, $heavySpecs
$jobU = Start-Job -ScriptBlock $lineBody -ArgumentList $Toolchain, $JsonUnity, $unitySpecs
Wait-Job -Job $jobH, $jobU | Out-Null
foreach ($j in @($jobH, $jobU)) { Receive-Job -Job $j -ErrorAction SilentlyContinue | Out-Null; Remove-Job -Job $j -Force }

Import-GateLineResults -JsonPath $JsonHeavy -LineLabel "非 Unity 重步骤线" -LineId "line_heavy"
Import-GateLineResults -JsonPath $JsonUnity -LineLabel "Unity 串行线" -LineId "line_unity"

$sw.Stop()
$runEnd = Get-Date
$err = Write-GateTimingFromRun -RepoRoot $Repo -Results $script:Results -Task "合成任务：一句话" `
    -Phase "定向门禁" -TotalStart $runStart -TotalEnd $runEnd -TotalSeconds ([Math]::Round($sw.Elapsed.TotalSeconds, 1)) `
    -TotalResult "FAIL" -TotalNote "parallel_wall=0s"
$expected = Get-GateTimingFilePath -RepoRoot $Repo -Branch (Get-GateTimingBranch -RepoRoot $Repo) -Date $runStart
@{ Error = $err; ExpectedFile = $expected; RunStart = (Format-GateTimingTimestamp $runStart); ResultCount = $script:Results.Count } |
    ConvertTo-Json | Out-File -FilePath $ResultPath -Encoding utf8
"""


@pytest.fixture(scope="module")
def simulated_run(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("timing_sim")
    repo = _init_repo(tmp / "repo", "feature/sim-task_20261001")
    body = (
        _SIMULATED_RUN.replace("%TOOLCHAIN%", ps_quote(TOOLCHAIN_DIR))
        .replace("%REPO%", ps_quote(repo))
        .replace("%JSON_HEAVY%", ps_quote(tmp / "heavy.json"))
        .replace("%JSON_UNITY%", ps_quote(tmp / "unity.json"))
    )
    info = run_ps_json(tmp, body, timeout=240, name="sim")
    assert not info["Error"], f"写入 timing 失败：{info['Error']}"
    path = Path(info["ExpectedFile"])
    assert path.exists(), f"没有写出 {path}；timing 目录内容：{list((repo / 'timing').glob('*')) if (repo / 'timing').exists() else '无'}"
    raw = path.read_bytes()
    assert not raw.startswith(b"\xef\xbb\xbf"), "jsonl 不得带 BOM"
    assert b"\r" not in raw, "jsonl 行尾必须是 LF"
    assert raw.endswith(b"\n")
    rows = [json.loads(line) for line in raw.decode("utf-8").split("\n") if line]
    return {"rows": rows, "path": path, "info": info}


def test_simulated_run_rows_have_all_fields_in_order(simulated_run: dict) -> None:
    rows = simulated_run["rows"]
    # 5 个步骤行（pre_step、skip_step、h1、h2、u1）+ _total
    assert len(rows) == 6
    for row in rows:
        assert list(row.keys()) == FIELDS
        assert row["task"] == "合成任务：一句话"
        assert row["branch"] == "feature/sim-task_20261001"
        assert row["phase"] == "定向门禁"
        assert row["result"] in ("PASS", "FAIL", "SKIP")
        assert isinstance(row["seconds"], (int, float)) and row["seconds"] >= 0
        assert isinstance(row["note"], str)
        _parse_ts(row["start"])
        _parse_ts(row["end"])


def test_simulated_run_file_name_strips_branch_prefix(simulated_run: dict) -> None:
    name = simulated_run["path"].name
    day = _parse_ts(simulated_run["info"]["RunStart"]).strftime("%Y%m%d")
    assert name == f"{day}_sim-task_20261001.jsonl"
    assert simulated_run["path"].parent.name == "timing"


def test_simulated_run_step_is_stable_id_not_display_name(simulated_run: dict) -> None:
    steps = [r["step"] for r in simulated_run["rows"]]
    assert sorted(steps) == sorted(["pre_step", "skip_step", "h1", "h2", "u1", "_total"])
    assert not any("中文" in s for s in steps), "step 必须是 -Id，不是中文显示名"
    assert steps[-1] == "_total", "_total 行写在最后"


def test_simulated_run_start_end_are_real_times(simulated_run: dict) -> None:
    rows = {r["step"]: r for r in simulated_run["rows"]}
    for step in ("pre_step", "h1", "h2", "u1", "_total"):
        r = rows[step]
        span = (_parse_ts(r["end"]) - _parse_ts(r["start"])).total_seconds()
        # start/end 都截断到秒，seconds 取 Stopwatch 1 位小数：差值最多不到 1 秒（留 0.15 余量给取时刻的先后差）
        assert abs(span - r["seconds"]) <= 1.15, (step, span, r["seconds"])
    # 睡眠步骤的 seconds 不低于睡眠时长（防"写死 0"）
    assert rows["h1"]["seconds"] >= 3.0 and rows["u1"]["seconds"] >= 3.0 and rows["h2"]["seconds"] >= 0.5
    # 跳过步骤：SKIP、0 秒
    assert rows["skip_step"]["result"] == "SKIP" and rows["skip_step"]["seconds"] == 0
    assert rows["skip_step"]["note"] == "-Quick"
    assert rows["h2"]["result"] == "FAIL" and rows["h1"]["result"] == "PASS"
    assert rows["_total"]["result"] == "FAIL"


def test_simulated_run_parallel_lines_really_overlap(simulated_run: dict) -> None:
    rows = {r["step"]: r for r in simulated_run["rows"]}
    h1s, h1e = _parse_ts(rows["h1"]["start"]), _parse_ts(rows["h1"]["end"])
    u1s, u1e = _parse_ts(rows["u1"]["start"]), _parse_ts(rows["u1"]["end"])
    # 两条线第一步各 3 秒、几乎同时开始：时间区间必须重叠（"整次起点 + 秒数"推算做不到这一点：
    # 那样两条线会被排成先后）
    assert h1s < u1e and u1s < h1e, (rows["h1"], rows["u1"])
    # 同一线内串行：h2 在 h1 结束之后才开始，且 start 间隔不小于 h1 的睡眠时长（截断到秒留 1 秒余量）
    h2s = _parse_ts(rows["h2"]["start"])
    assert (h2s - h1s).total_seconds() >= 3 - 1
    assert h2s >= h1e
    # 主进程前置步骤早于两条线的步骤
    assert _parse_ts(rows["pre_step"]["end"]) <= u1s
    # _total 覆盖全部步骤
    ts, te = _parse_ts(rows["_total"]["start"]), _parse_ts(rows["_total"]["end"])
    for r in simulated_run["rows"]:
        assert ts <= _parse_ts(r["start"]) and _parse_ts(r["end"]) <= te


def test_simulated_run_rows_sorted_by_start(simulated_run: dict) -> None:
    body = simulated_run["rows"][:-1]
    starts = [r["start"] for r in body]
    assert starts == sorted(starts)


# ---------------------------------------------------------------------------
# 写入函数本身：JSON 转义往返、写入失败不抛、默认任务描述、分支简名口径
# ---------------------------------------------------------------------------

_HELPERS_DRIVER = r"""
$Toolchain = %TOOLCHAIN%
. (Join-Path $Toolchain "_gate_timing.ps1")
$result = @{}

# 1. JSON 特殊字符往返
$nasty = 'say "hi" \ back <tag> & 单引号'' 与' + "`t制表"
$result.Line = ConvertTo-GateTimingLine -Task $nasty -Branch "b" -Phase "编码" -Step "s" `
    -Start "2026-10-01T00:00:00" -End "2026-10-01T00:00:01" -Seconds 1.5 -Result "DONE" -Note $nasty

# 2. 写入失败不抛异常：仓库根不存在 -> 返回错误文本
$threw = $false
$err = $null
try {
    $err = Write-GateTimingFromRun -RepoRoot %MISSING% -Results @() -Task "t" -Phase "定向门禁" `
        -TotalStart (Get-Date) -TotalEnd (Get-Date) -TotalSeconds 0
} catch { $threw = $true }
$result.WriteFailThrew = $threw
$result.WriteFailError = $err

# 3. 空结果集也能产出（只有 _total）
$result.EmptyLines = @(New-GateTimingLines -Results @() -Task "t" -Branch "b" -Phase "全量门禁" `
    -TotalStart (Get-Date) -TotalEnd (Get-Date) -TotalSeconds 0.5)
$result.NullLines = @(New-GateTimingLines -Results $null -Task "t" -Branch "b" -Phase "全量门禁" `
    -TotalStart (Get-Date) -TotalEnd (Get-Date) -TotalSeconds 0.5)

# 4. 默认任务描述
$bound = [ordered]@{ Changed = "HEAD"; SkipUnity = [System.Management.Automation.SwitchParameter]$true; Modules = @("a", "b");
    NoTiming = [System.Management.Automation.SwitchParameter]$true; TargetedInner = [System.Management.Automation.SwitchParameter]$true;
    ArtifactsPath = "X:\skip\me"; Quick = [System.Management.Automation.SwitchParameter]$false }
$result.DefaultTask = Get-GateTimingDefaultTask -BoundParameters $bound
$result.DefaultTaskEmpty = Get-GateTimingDefaultTask -BoundParameters ([ordered]@{})

# 5. 分支简名
$result.Slugs = @{}
foreach ($b in %BRANCHES%) { $result.Slugs[$b] = Get-GateTimingBranchSlug -Branch $b }

$result | ConvertTo-Json -Depth 6 | Out-File -FilePath $ResultPath -Encoding utf8
"""

BRANCHES = [
    "main",
    "feature/gate-timing-autolog_20261001",
    "bugfix/cold-load-attach-key_20261001",
    "release/1.12.x",
    "fix/gate-parallel-ctrlc",
    "feature/a/b",
    "detached",
]


@pytest.fixture(scope="module")
def helpers(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("timing_helpers")
    branches = "@(" + ",".join(ps_quote(b) for b in BRANCHES) + ")"
    body = (
        _HELPERS_DRIVER.replace("%TOOLCHAIN%", ps_quote(TOOLCHAIN_DIR))
        .replace("%MISSING%", ps_quote(tmp / "no_such_repo"))
        .replace("%BRANCHES%", branches)
    )
    return run_ps_json(tmp, body, name="helpers")


def test_json_line_escaping_round_trips(helpers: dict) -> None:
    row = json.loads(helpers["Line"])
    assert list(row.keys()) == FIELDS
    nasty = 'say "hi" \\ back <tag> & 单引号\' 与\t制表'
    assert row["task"] == nasty and row["note"] == nasty
    assert row["seconds"] == 1.5 and row["result"] == "DONE"


def test_write_failure_is_swallowed_and_reported(helpers: dict) -> None:
    assert helpers["WriteFailThrew"] is False
    assert isinstance(helpers["WriteFailError"], str) and helpers["WriteFailError"]


def test_empty_results_still_produce_total_line(helpers: dict) -> None:
    for key in ("EmptyLines", "NullLines"):
        lines = helpers[key]
        if isinstance(lines, str):  # ConvertTo-Json 把单元素数组压成标量
            lines = [lines]
        assert len(lines) == 1
        row = json.loads(lines[0])
        assert row["step"] == "_total" and row["phase"] == "全量门禁" and row["seconds"] == 0.5


def test_default_task_text(helpers: dict) -> None:
    assert helpers["DefaultTask"] == "check.ps1 -Changed HEAD -SkipUnity -Modules a,b"
    assert helpers["DefaultTaskEmpty"] == "check.ps1"


def test_branch_slug_matches_python_implementation(helpers: dict) -> None:
    for b in BRANCHES:
        assert helpers["Slugs"][b] == tr.branch_slug(b), b
    assert helpers["Slugs"]["main"] == "main"
    assert helpers["Slugs"]["feature/gate-timing-autolog_20261001"] == "gate-timing-autolog_20261001"
    assert helpers["Slugs"]["release/1.12.x"] == "release-1.12.x"


# ---------------------------------------------------------------------------
# 非正常终止（未被 Invoke-CheckStep 接住的异常）也留记录：复现 + 不变量
# ---------------------------------------------------------------------------

_ABORT_DRIVER = r"""
$Toolchain = %TOOLCHAIN%
$RepoRoot = %REPO%
$NoTiming = [System.Management.Automation.SwitchParameter]$false
$TargetedMode = $false
$TimingTask = "abort task"
$script:TimingScriptStart = (Get-Date).AddSeconds(-2)
$script:Results = New-Object System.Collections.Generic.List[Object]
$script:GateFailed = $false
$script:FailFastFlagPath = $null
. (Join-Path $Toolchain "_gate_step_runner.ps1")
. (Join-Path $Toolchain "_gate_timing.ps1")
%TRAP%
Invoke-CheckStep "步骤一（中文显示名）" -Id "step_one" { $true }
Invoke-CheckStep "步骤二（中文显示名）" -Id "step_two" { $false }
throw "synthetic abort outside Invoke-CheckStep"
"""


def _extract_check_trap() -> str:
    text = CHECK_PS1.read_text(encoding="utf-8-sig")
    m = re.search(r"^trap \{\n.*?^\}\n", text, re.S | re.M)
    assert m, "check.ps1 里找不到顶层 trap 块"
    return m.group(0)


def test_abort_writes_partial_timing_with_total_marked_aborted(tmp_path: Path) -> None:
    """复现：脚本被未接住的异常中断。期望：已完成步骤行照写，_total 为 FAIL 且 note 含 aborted 与异常消息。"""
    repo = _init_repo(tmp_path / "repo", "feature/abort-sim_20261003")
    body = (
        _ABORT_DRIVER.replace("%TOOLCHAIN%", ps_quote(TOOLCHAIN_DIR))
        .replace("%REPO%", ps_quote(repo))
        .replace("%TRAP%", _extract_check_trap())
    )
    from _ps_harness import run_ps_script

    proc = run_ps_script(tmp_path, body, name="abort")
    assert proc.returncode != 0, "异常没有传出，用例前提不成立"
    files = sorted((repo / "timing").glob("*abort-sim_20261003.jsonl"))
    assert len(files) == 1, f"应恰好一个 timing 文件：{files}\nstdout={proc.stdout}\nstderr={proc.stderr}"
    rows = [json.loads(line) for line in files[0].read_text(encoding="utf-8").splitlines() if line]
    steps = [r["step"] for r in rows]
    assert steps == ["step_one", "step_two", "_total"], steps
    total = rows[-1]
    assert total["result"] == "FAIL" and total["phase"] == "全量门禁"
    assert total["note"].startswith("aborted:") and "synthetic abort" in total["note"]
    # 墙钟取到中断时刻：不小于起点之前人为回拨的 2 秒
    assert total["seconds"] >= 2.0
    assert [r["result"] for r in rows[:2]] == ["PASS", "FAIL"]


def test_abort_record_written_once_and_skipped_after_normal_finish(tmp_path: Path) -> None:
    """不变量：同一次运行只写一次——正常收尾已置标记后再触发中断补写，什么都不追加；连续两次中断补写也只写一次。"""
    repo = _init_repo(tmp_path / "repo", "feature/abort-once_20261003")
    body = r"""
$Toolchain = %TOOLCHAIN%
. (Join-Path $Toolchain "_gate_timing.ps1")
$start = (Get-Date).AddSeconds(-1)
$first = Write-GateTimingOnAbort -RepoRoot %REPO% -Results @() -Task "t" -Phase "全量门禁" -TotalStart $start -Reason "boom 1"
$second = Write-GateTimingOnAbort -RepoRoot %REPO% -Results @() -Task "t" -Phase "全量门禁" -TotalStart $start -Reason "boom 2"
$script:GateTimingWritten = $false
$script:GateTimingWritten = $true   # 模拟正常收尾已写
$third = Write-GateTimingOnAbort -RepoRoot %REPO% -Results @() -Task "t" -Phase "全量门禁" -TotalStart $start -Reason "boom 3"
@{ First = $first; Second = $second; Third = $third } | ConvertTo-Json | Out-File -FilePath $ResultPath -Encoding utf8
""".replace("%TOOLCHAIN%", ps_quote(TOOLCHAIN_DIR)).replace("%REPO%", ps_quote(repo))
    info = run_ps_json(tmp_path, body, name="abort_once")
    assert info["First"] is None and info["Second"] is None and info["Third"] is None
    files = sorted((repo / "timing").glob("*abort-once_20261003.jsonl"))
    assert len(files) == 1
    rows = [json.loads(line) for line in files[0].read_text(encoding="utf-8").splitlines() if line]
    assert len(rows) == 1 and "boom 1" in rows[0]["note"]


def test_check_ps1_sets_written_flag_before_normal_timing_write() -> None:
    check = CHECK_PS1.read_text(encoding="utf-8-sig")
    assert "$script:GateTimingWritten = $true" in check
    assert check.index("$script:GateTimingWritten = $true") < check.index("Write-GateTimingFromRun -RepoRoot $RepoRoot")


# ---------------------------------------------------------------------------
# 静态：步骤调用点都带 -Id；-NoTiming 接线
# ---------------------------------------------------------------------------

def test_every_step_call_site_passes_an_id() -> None:
    call = re.compile(r"^\s*(Invoke-CheckStep|Add-SkippedStep)\b")
    offenders: list[str] = []
    checked = 0
    for path in (CHECK_PS1, HEAVY_PS1, UNITY_PS1, RUNNER_PS1):
        for no, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if call.match(line) and not line.lstrip().startswith("#"):
                checked += 1
                if not re.search(r"-Id\s+(\"[a-z0-9_]+\"|\$Id\b)", line):
                    offenders.append(f"{path.name}:{no}: {line.strip()[:100]}")
    assert checked > 40, "扫描到的调用点数量异常偏少，正则可能失效"
    assert offenders == [], "这些步骤调用点没有 -Id，时间记录里会退化成中文显示名：\n" + "\n".join(offenders)


def test_no_timing_wiring_in_hook_and_release() -> None:
    hook = PRE_COMMIT.read_text(encoding="utf-8")
    invocations = [ln for ln in hook.splitlines() if re.search(r"-File check\.ps1\b", ln)]
    assert len(invocations) == 3
    for ln in invocations:
        assert "-NoTiming" in ln, f"预提交钩子调用门禁必须带 -NoTiming：{ln.strip()}"
    build = BUILD_PS1.read_text(encoding="utf-8-sig")
    assert '$checkArgs = @("-AbiStrict", "-FailFast", "-NoTiming")' in build
    check = CHECK_PS1.read_text(encoding="utf-8-sig")
    assert "[switch]$NoTiming" in check and "[string]$TimingTask" in check
    # 定向模式父进程把 -NoTiming / -TimingTask 传给干活的子进程
    assert '"NoTiming")' in check and '"-TimingTask", $TimingTask' in check
    assert "if (-not $NoTiming)" in check


# ---------------------------------------------------------------------------
# 统计脚本：不变量（任意行集合）
# ---------------------------------------------------------------------------

PHASES = ["勘察", "设计", "编码", "定向门禁", "全量门禁", "提交", "汇报", "等待"]
STEPS = ["dotnet_build", "dotnet_test", "toolchain_pytest", "unity_playmode", "编码一", "_total"]
BRANCHES_FOR_ROWS = ["feature/alpha_20261001", "bugfix/beta_20261002", "main"]


def _random_rows(rng: random.Random, n: int) -> list[dict]:
    rows = []
    for _ in range(n):
        day = rng.choice(["2026-09-30", "2026-10-01", "2026-10-02"])
        rows.append(
            {
                "task": "t",
                "branch": rng.choice(BRANCHES_FOR_ROWS),
                "phase": rng.choice(PHASES),
                "step": rng.choice(STEPS),
                "start": f"{day}T10:00:00",
                "end": f"{day}T10:00:05",
                "seconds": round(rng.uniform(0, 900), 1),
                "result": "PASS",
                "note": "",
            }
        )
    return rows


def _write_timing_dir(tmp: Path, rows: list[dict], files: int = 3) -> Path:
    d = tmp / "timing"
    d.mkdir()
    for i in range(files):
        chunk = rows[i::files]
        (d / f"2026100{i + 1}_x{i}.jsonl").write_text(
            "".join(json.dumps(r, ensure_ascii=False) + "\n" for r in chunk), encoding="utf-8"
        )
    return d


def _check_invariants(report: dict, rows: list[dict]) -> None:
    eps = 1e-6
    body = [r for r in rows if r["step"] != "_total"]
    # phase 汇总 = 该 phase 下非 _total 行
    assert {p["phase"] for p in report["phases"]} == {r["phase"] for r in body}
    for p in report["phases"]:
        vals = [r["seconds"] for r in body if r["phase"] == p["phase"]]
        assert p["count"] == len(vals)
        assert abs(p["total"] - sum(vals)) < eps
        assert p["median"] <= p["p90"] + eps <= p["max"] + 2 * eps
        assert abs(p["max"] - max(vals)) < eps
    # step 汇总 = 该 (phase, step) 下全部行（含 _total）
    assert {(s["phase"], s["step"]) for s in report["steps"]} == {(r["phase"], r["step"]) for r in rows}
    for s in report["steps"]:
        vals = [r["seconds"] for r in rows if r["phase"] == s["phase"] and r["step"] == s["step"]]
        assert s["count"] == len(vals)
        assert abs(s["total"] - sum(vals)) < eps
        assert min(vals) - eps <= s["median"] <= s["p90"] + eps <= s["max"] + 2 * eps
        assert abs(s["max"] - max(vals)) < eps
    assert sum(s["count"] for s in report["steps"]) == len(rows) == report["rows"]


@pytest.mark.parametrize("seed", [1, 2, 3, 20261001])
def test_report_invariants_on_random_row_sets(tmp_path: Path, seed: int) -> None:
    rng = random.Random(seed)
    rows = _random_rows(rng, rng.randint(1, 120))
    d = _write_timing_dir(tmp_path, rows)
    report, bad = tr.build_report(d)
    assert bad == 0
    _check_invariants(report, rows)


def test_report_invariants_single_sample_and_ties(tmp_path: Path) -> None:
    rows = [
        {"task": "t", "branch": "main", "phase": "编码", "step": "a", "start": "2026-10-01T00:00:00",
         "end": "2026-10-01T00:00:01", "seconds": 7.5, "result": "DONE", "note": ""},
    ]
    d = _write_timing_dir(tmp_path, rows, files=1)
    report, _ = tr.build_report(d)
    _check_invariants(report, rows)
    s = report["steps"][0]
    assert s["median"] == s["p90"] == s["max"] == s["total"] == 7.5


def test_report_filters_are_consistent_subsets(tmp_path: Path) -> None:
    rng = random.Random(7)
    rows = _random_rows(rng, 150)
    d = _write_timing_dir(tmp_path, rows)

    only_phase, _ = tr.build_report(d, phase="全量门禁")
    expect = [r for r in rows if r["phase"] == "全量门禁"]
    assert only_phase["rows"] == len(expect)
    _check_invariants(only_phase, expect)

    since, _ = tr.build_report(d, since=tr.normalize_date("20261001"))
    expect = [r for r in rows if r["start"][:10] >= "2026-10-01"]
    assert since["rows"] == len(expect)
    _check_invariants(since, expect)

    for spelling in ("feature/alpha_20261001", "alpha_20261001"):
        by_branch, _ = tr.build_report(d, branch=spelling)
        expect = [r for r in rows if r["branch"] == "feature/alpha_20261001"]
        assert by_branch["rows"] == len(expect), spelling
        _check_invariants(by_branch, expect)

    combo, _ = tr.build_report(d, phase="编码", branch="main", since="2026-10-02")
    expect = [r for r in rows if r["phase"] == "编码" and r["branch"] == "main" and r["start"][:10] >= "2026-10-02"]
    assert combo["rows"] == len(expect)
    _check_invariants(combo, expect)


def test_report_cli_json_and_text_to_stdout_only(tmp_path: Path) -> None:
    rng = random.Random(11)
    rows = _random_rows(rng, 40)
    d = _write_timing_dir(tmp_path, rows)
    # 坏行：不中断，只在 stderr 报数
    (d / "20261009_bad.jsonl").write_text('not json\n{"phase": "编码"}\n\n', encoding="utf-8")
    before = sorted(p.name for p in d.iterdir())
    script = TOOLCHAIN_DIR / "timing_report.py"
    proc = subprocess.run(
        [sys.executable, str(script), "--json", "--timing-dir", str(d), "--phase", "编码"],
        capture_output=True, encoding="utf-8", timeout=60,
    )
    assert proc.returncode == 0, proc.stderr
    report = json.loads(proc.stdout)
    expect = [r for r in rows if r["phase"] == "编码"]
    _check_invariants(report, expect)
    assert "跳过 2 个坏行" in proc.stderr
    text = subprocess.run(
        [sys.executable, str(script), "--timing-dir", str(d)], capture_output=True, encoding="utf-8", timeout=60
    )
    assert text.returncode == 0 and "按 phase" in text.stdout and "按 step" in text.stdout
    assert sorted(p.name for p in d.iterdir()) == before, "统计脚本不得写任何文件"
    bad_arg = subprocess.run(
        [sys.executable, str(script), "--since", "昨天", "--timing-dir", str(d)],
        capture_output=True, encoding="utf-8", timeout=60,
    )
    assert bad_arg.returncode == 2


def test_report_on_missing_directory_is_empty_not_error(tmp_path: Path) -> None:
    report, bad = tr.build_report(tmp_path / "nope")
    assert report["rows"] == 0 and report["phases"] == [] and report["steps"] == [] and bad == 0


def test_real_repo_timing_files_are_parseable() -> None:
    """已入库的 timing/*.jsonl 全部能被统计脚本读通（字段齐全），防手抄行写坏格式。"""
    d = REPO_ROOT / "timing"
    report, bad = tr.build_report(d)
    assert bad == 0, "timing/ 里有无法解析或缺 phase/step/seconds 的行"
    for path in d.glob("*.jsonl"):
        for no, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip():
                continue
            row = json.loads(line)
            assert sorted(row.keys()) == sorted(FIELDS), f"{path.name}:{no} 字段与 AGENTS.md 1c 不一致：{list(row.keys())}"
