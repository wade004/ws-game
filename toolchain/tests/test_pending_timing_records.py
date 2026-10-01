"""待领耗时记录（"待领耗时记录"切片，2026-10-01，AGENTS.md §1b/§1c）的测试。

背景：``check.ps1`` 在 main 上跑门禁若追加到已入库的 ``timing/<日期>_main.jsonl``，主检出就变脏，挡住
``build.ps1 -Release`` 与 ``git merge --ff-only``。设计：main/游离 HEAD 上改写被 .gitignore 覆盖的
``timing/_pending/<年月日>_main_<时分秒>.jsonl``，下一条分支用 ``toolchain/claim_pending_records.py`` 领走。

两类用例（测试 = 复现 + 不变量各至少一条）：

- **复现用例**：在临时 git 仓库（main + 一个 feature 工作树 + 一个游离 HEAD 工作树）里用真实的写入函数
  ``Write-GateTimingFromRun`` 模拟 main 上跑一次写耗时，断言写进了待领目录、已跟踪的 main 记录文件字节不变、
  ``git status --porcelain`` 为空；再在 feature 工作树里运行领取脚本，断言行被并入、待领文件被删。
- **不变量用例**：任意次数在 main 上写耗时后主检出 ``git status --porcelain`` 恒为空；领取脚本对同一批待领
  文件重复运行是幂等的（第二次领 0 个文件、目标文件字节不变）；随机待领内容下"领走后的目标文件 = 原有行 +
  去重后的新行（按时间顺序）"。

另有：拒绝场景（在 main/游离 HEAD 上运行、与 --from 同一目录、不同仓库、已跟踪的待领文件、文件名不合规）、
``--from`` 缺省发现、``--dry-run``、统计脚本默认含待领目录与 ``--no-pending``、待领目录被 .gitignore 覆盖且不影响
判级（仍是 T0）、PowerShell 与 Python 两份"main/游离 HEAD"判定口径一致。

写入路径的 PowerShell 用例经 ``_ps_harness``，跟随 ``WS_GAME_PS_HOST`` 矩阵（Windows PowerShell 5.1 / pwsh 7）。
本组用例**不**真实调用 ``check.ps1``（写入路径由写入函数覆盖；check.ps1 的真实端到端由切片汇报里的实测记录）。

运行：``python -m pytest toolchain/tests/test_pending_timing_records.py -q``。
"""

from __future__ import annotations

import json
import random
import subprocess
import sys
from pathlib import Path

import pytest

TESTS_DIR = Path(__file__).resolve().parent
TOOLCHAIN_DIR = TESTS_DIR.parent
REPO_ROOT = TOOLCHAIN_DIR.parent
for _p in (str(TOOLCHAIN_DIR), str(TESTS_DIR)):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import timing_report as tr  # noqa: E402
import version_label as vl  # noqa: E402
from _ps_harness import git_env, ps_quote, run_ps_json  # noqa: E402

TIMING_PS1 = TOOLCHAIN_DIR / "_gate_timing.ps1"
CLAIM_PY = TOOLCHAIN_DIR / "claim_pending_records.py"
CHECK_PS1 = REPO_ROOT / "check.ps1"

FIELDS = ["task", "branch", "phase", "step", "start", "end", "seconds", "result", "note"]
FEATURE = "feature/claim-test_20261001"


# ---------------------------------------------------------------------------
# 临时仓库工具
# ---------------------------------------------------------------------------

def _git(repo: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", "-C", str(repo), *args],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        env=git_env(),
        check=check,
    )


def _status(repo: Path) -> str:
    return _git(repo, "status", "--porcelain").stdout


def row(step: str, start: str, *, branch: str = "main", seconds: float = 1.0, result: str = "PASS") -> str:
    """一行合法的耗时 JSON（与 AGENTS.md §1c 字段一致）。"""
    return json.dumps(
        {
            "task": "t", "branch": branch, "phase": "全量门禁", "step": step, "start": start,
            "end": start, "seconds": seconds, "result": result, "note": "",
        },
        ensure_ascii=False,
    )


def make_main(path: Path) -> Path:
    """main 检出：已入库的当天 main 记录 + 覆盖待领目录的 .gitignore（模拟真实仓库的状态）。"""
    path.mkdir(parents=True)
    _git(path, "init", "-q", "-b", "main")
    _git(path, "config", "user.email", "t@example.invalid")
    _git(path, "config", "user.name", "t")
    _git(path, "config", "core.autocrlf", "false")
    (path / ".gitignore").write_text("timing/_pending/\n", encoding="utf-8", newline="\n")
    (path / "f.txt").write_text("x\n", encoding="utf-8", newline="\n")
    (path / "timing").mkdir()
    (path / "timing" / "20261001_main.jsonl").write_text(
        row("old_step", "2026-10-01T08:00:00") + "\n", encoding="utf-8", newline="\n"
    )
    _git(path, "add", ".gitignore", "f.txt", "timing/20261001_main.jsonl")
    _git(path, "commit", "-q", "-m", "init")
    return path


def add_worktree(main: Path, name: str, *, branch: str | None = FEATURE) -> Path:
    wt = main.parent / name
    if branch is None:
        _git(main, "worktree", "add", "-q", "--detach", str(wt))
    else:
        _git(main, "worktree", "add", "-q", "-b", branch, str(wt))
    return wt


def write_pending(main: Path, name: str, lines: list[str], *, newline: str = "\n", final_newline: bool = True) -> Path:
    pending = main / "timing" / "_pending"
    pending.mkdir(parents=True, exist_ok=True)
    path = pending / name
    text = newline.join(lines) + (newline if final_newline else "")
    path.write_bytes(text.encode("utf-8"))
    return path


def claim(target: Path, *extra: str, source: Path | None = None) -> subprocess.CompletedProcess[str]:
    args = [sys.executable, str(CLAIM_PY), "--repo-root", str(target), *extra]
    if source is not None:
        args += ["--from", str(source)]
    return subprocess.run(args, capture_output=True, encoding="utf-8", errors="replace", env=git_env())


def pending_files(main: Path) -> list[str]:
    d = main / "timing" / "_pending"
    return sorted(p.name for p in d.iterdir()) if d.is_dir() else []


@pytest.fixture()
def env(tmp_path: Path) -> dict:
    main = make_main(tmp_path / "main")
    wt = add_worktree(main, "wt")
    return {"main": main, "wt": wt, "tmp": tmp_path}


# ---------------------------------------------------------------------------
# 复现：真实写入函数在 main / 游离 HEAD / feature 分支上的落点
# ---------------------------------------------------------------------------

_WRITER = r"""
$Toolchain = %TOOLCHAIN%
. (Join-Path $Toolchain "_gate_timing.ps1")
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$errors = New-Object System.Collections.Generic.List[string]
$usesPending = $null
foreach ($repo in %REPOS%) {
    foreach ($s in %STARTS%) {
        $start = [datetime]::ParseExact($s, 'yyyy-MM-ddTHH:mm:ss', $inv)
        $end = $start.AddSeconds(2)
        $res = @([PSCustomObject]@{ Id = 'step_a'; Step = '中文步骤'; Start = $s; End = (Format-GateTimingTimestamp $end);
            Seconds = 1.5; Result = 'PASS'; Detail = 'd' })
        $err = Write-GateTimingFromRun -RepoRoot $repo -Results $res -Task '合成任务' -Phase '全量门禁' `
            -TotalStart $start -TotalEnd $end -TotalSeconds 2.0
        if ($err) { $errors.Add([string]$err) }
    }
}
$branches = New-Object System.Collections.Generic.List[string]
$pend = New-Object System.Collections.Generic.List[string]
foreach ($repo in %REPOS%) {
    $b = Get-GateTimingBranch -RepoRoot $repo
    $branches.Add($b)
    $pend.Add([string](Test-GateTimingUsesPending -Branch $b))
}
@{ Errors = ($errors -join '|'); Branches = ($branches -join '|'); Pending = ($pend -join '|') } |
    ConvertTo-Json | Out-File -FilePath $ResultPath -Encoding utf8
"""


def run_writer(tmp: Path, repos: list[Path], starts: list[str], name: str = "writer") -> dict:
    body = (
        _WRITER.replace("%TOOLCHAIN%", ps_quote(TOOLCHAIN_DIR))
        .replace("%REPOS%", "@(" + ",".join(ps_quote(r) for r in repos) + ")")
        .replace("%STARTS%", "@(" + ",".join(ps_quote(s) for s in starts) + ")")
    )
    info = run_ps_json(tmp, body, timeout=240, name=name)
    assert not info["Errors"], f"写入 timing 失败：{info['Errors']}"
    return info


STARTS = ["2026-10-01T10:00:05", "2026-10-01T10:00:06", "2026-10-01T11:30:00"]


@pytest.fixture(scope="module")
def written(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("pending_write")
    main = make_main(tmp / "main")
    tracked_before = (main / "timing" / "20261001_main.jsonl").read_bytes()
    feat = add_worktree(main, "wt_feat")
    det = add_worktree(main, "wt_det", branch=None)
    info = run_writer(tmp, [main, feat, det], STARTS)
    return {"main": main, "feat": feat, "det": det, "tracked_before": tracked_before, "info": info}


def test_main_run_writes_pending_files_not_tracked_file(written: dict) -> None:
    main: Path = written["main"]
    assert pending_files(main) == [
        "20261001_main_100005.jsonl", "20261001_main_100006.jsonl", "20261001_main_113000.jsonl"
    ]
    # 已跟踪的 main 记录文件一个字节都没动
    assert (main / "timing" / "20261001_main.jsonl").read_bytes() == written["tracked_before"]
    # 主检出保持干净：不挡 build.ps1 -Release，也不挡下一次 git merge --ff-only
    assert _status(main) == ""
    rows = [
        json.loads(line)
        for line in (main / "timing" / "_pending" / "20261001_main_100005.jsonl").read_text(encoding="utf-8").splitlines()
    ]
    assert [list(r.keys()) for r in rows] == [FIELDS, FIELDS]  # 一个步骤行 + _total 行
    assert [r["step"] for r in rows] == ["step_a", "_total"]
    assert all(r["branch"] == "main" for r in rows)


def test_detached_head_run_also_writes_pending(written: dict) -> None:
    det: Path = written["det"]
    assert pending_files(det) == [
        "20261001_detached_100005.jsonl", "20261001_detached_100006.jsonl", "20261001_detached_113000.jsonl"
    ]
    assert not list((det / "timing").glob("*detached*.jsonl")), "游离 HEAD 不得写 timing/ 下的普通文件"
    assert _status(det) == ""


def test_feature_branch_run_keeps_old_behaviour(written: dict) -> None:
    feat: Path = written["feat"]
    target = feat / "timing" / "20261001_claim-test_20261001.jsonl"
    assert target.exists(), "feature 分支仍写 timing/<日期>_<分支名去前缀>.jsonl"
    lines = target.read_text(encoding="utf-8").splitlines()
    assert len(lines) == 2 * len(STARTS)  # 每次运行一个步骤行 + 一个 _total，三次追加到同一文件
    assert pending_files(feat) == []


def test_python_and_powershell_agree_on_main_and_detached(written: dict) -> None:
    """PowerShell 的"写待领目录"判定与 version_label.py 的 main/游离 HEAD 判定同口径。"""
    info = written["info"]
    branches = info["Branches"].split("|")
    flags = [x == "True" for x in info["Pending"].split("|")]
    assert branches == ["main", FEATURE, "detached"]
    for repo, ps_branch, ps_pending in zip((written["main"], written["feat"], written["det"]), branches, flags):
        py_branch, sha = vl.current_branch(repo)
        assert (py_branch or "detached") == ps_branch
        label = vl.compute_label("1.0.0", py_branch, sha)
        label_says_pending = label.endswith("_release") or "_detached-" in label
        assert ps_pending == label_says_pending, (ps_branch, label)


@pytest.mark.parametrize("seed", [1, 2, 3])
def test_invariant_main_checkout_stays_clean_after_any_number_of_runs(tmp_path: Path, seed: int) -> None:
    rng = random.Random(seed)
    main = make_main(tmp_path / "main")
    tracked_before = (main / "timing" / "20261001_main.jsonl").read_bytes()
    n = rng.randint(1, 6)
    starts = sorted({f"2026-10-01T{rng.randint(0, 23):02d}:{rng.randint(0, 59):02d}:{rng.randint(0, 59):02d}" for _ in range(n)})
    run_writer(tmp_path, [main], starts)
    assert len(pending_files(main)) == len(starts)
    assert _status(main) == ""
    assert (main / "timing" / "20261001_main.jsonl").read_bytes() == tracked_before


def test_e2e_main_write_then_claim_in_feature_worktree(tmp_path: Path) -> None:
    """复现整条链路：main 上写 -> 主检出干净 -> 分支工作树领走 -> 行并入、待领文件被删。"""
    main = make_main(tmp_path / "main")
    wt = add_worktree(main, "wt")
    run_writer(tmp_path, [main], STARTS)
    written_rows = []
    for name in pending_files(main):
        written_rows += (main / "timing" / "_pending" / name).read_text(encoding="utf-8").splitlines()
    assert len(written_rows) == 2 * len(STARTS)
    assert _status(main) == ""

    proc = claim(wt, source=main)
    assert proc.returncode == 0, proc.stderr
    assert f"领走 {len(STARTS)} 个文件、{2 * len(STARTS)} 行" in proc.stdout
    assert pending_files(main) == [], "被领走的待领文件必须从主检出删除"
    assert _status(main) == "", "领取后主检出仍然干净"
    target = wt / "timing" / "20261001_main.jsonl"
    merged = target.read_text(encoding="utf-8").splitlines()
    assert merged[0] == row("old_step", "2026-10-01T08:00:00"), "原有行保持在最前"
    assert merged[1:] == written_rows, "新行按时间顺序追加"
    assert _git(wt, "status", "--porcelain").stdout.strip() == "M timing/20261001_main.jsonl"


# ---------------------------------------------------------------------------
# 领取脚本：合并规则、幂等、拒绝场景
# ---------------------------------------------------------------------------

def test_claim_merges_by_day_in_time_order_and_deletes_claimed(env: dict) -> None:
    main, wt = env["main"], env["wt"]
    a, b, c = row("a", "2026-10-01T09:00:00"), row("b", "2026-10-01T10:00:00"), row("c", "2026-10-01T11:00:00")
    d = row("d", "2026-10-02T09:00:00")
    # 故意以与时间相反的创建顺序写入；合并必须按文件名里的时分秒排
    write_pending(main, "20261001_main_110000.jsonl", [c])
    write_pending(main, "20261001_main_090000.jsonl", [a])
    write_pending(main, "20261001_detached_100000.jsonl", [b])
    write_pending(main, "20261002_main_090000.jsonl", [d])
    proc = claim(wt, source=main)
    assert proc.returncode == 0, proc.stderr
    assert "领走 4 个文件、4 行" in proc.stdout
    day1 = (wt / "timing" / "20261001_main.jsonl").read_text(encoding="utf-8").splitlines()
    assert day1 == [row("old_step", "2026-10-01T08:00:00"), a, b, c]
    assert (wt / "timing" / "20261002_main.jsonl").read_text(encoding="utf-8").splitlines() == [d]
    assert pending_files(main) == []
    assert _status(main) == ""


def test_claim_is_idempotent(env: dict) -> None:
    main, wt = env["main"], env["wt"]
    write_pending(main, "20261001_main_090000.jsonl", [row("a", "2026-10-01T09:00:00"), row("z", "2026-10-01T09:00:01")])
    first = claim(wt, source=main)
    assert first.returncode == 0 and "领走 1 个文件、2 行" in first.stdout, first.stdout + first.stderr
    snapshot = {p.name: p.read_bytes() for p in (wt / "timing").iterdir() if p.is_file()}
    second = claim(wt, source=main)
    assert second.returncode == 0, second.stderr
    assert "领走 0 个文件、0 行" in second.stdout
    assert {p.name: p.read_bytes() for p in (wt / "timing").iterdir() if p.is_file()} == snapshot
    assert _status(main) == ""


def test_claim_drops_byte_identical_lines(env: dict) -> None:
    main, wt = env["main"], env["wt"]
    old = row("old_step", "2026-10-01T08:00:00")  # 与目标文件已有行逐字节相同
    x = row("x", "2026-10-01T09:00:00")
    write_pending(main, "20261001_main_090000.jsonl", [old, x])
    write_pending(main, "20261001_main_100000.jsonl", [x, row("y", "2026-10-01T10:00:00")])
    proc = claim(wt, source=main)
    assert proc.returncode == 0, proc.stderr
    assert "领走 2 个文件、2 行（丢弃逐字节重复 2 行）" in proc.stdout
    lines = (wt / "timing" / "20261001_main.jsonl").read_text(encoding="utf-8").splitlines()
    assert lines == [old, x, row("y", "2026-10-01T10:00:00")]


def test_claim_handles_target_without_trailing_newline_and_crlf_pending(env: dict) -> None:
    main, wt = env["main"], env["wt"]
    target = wt / "timing" / "20261001_main.jsonl"
    target.write_bytes(row("old_step", "2026-10-01T08:00:00").encode("utf-8"))  # 无结尾换行
    a = row("a", "2026-10-01T09:00:00")
    write_pending(main, "20261001_main_090000.jsonl", [a], newline="\r\n")
    assert claim(wt, source=main).returncode == 0
    raw = target.read_bytes()
    assert b"\r" not in raw and raw.endswith(b"\n") and not raw.startswith(b"\xef\xbb\xbf")
    assert raw.decode("utf-8").splitlines() == [row("old_step", "2026-10-01T08:00:00"), a]


def test_claim_without_pending_directory_is_a_noop(env: dict) -> None:
    proc = claim(env["wt"], source=env["main"])
    assert proc.returncode == 0, proc.stderr
    assert "领走 0 个文件、0 行" in proc.stdout
    assert _git(env["wt"], "status", "--porcelain").stdout == ""


def test_claim_default_source_is_the_worktree_that_checks_out_main(env: dict) -> None:
    main, wt = env["main"], env["wt"]
    write_pending(main, "20261001_main_090000.jsonl", [row("a", "2026-10-01T09:00:00")])
    proc = claim(wt)  # 不给 --from
    assert proc.returncode == 0, proc.stderr
    assert "领走 1 个文件、1 行" in proc.stdout
    assert pending_files(main) == []


def test_claim_dry_run_changes_nothing(env: dict) -> None:
    main, wt = env["main"], env["wt"]
    write_pending(main, "20261001_main_090000.jsonl", [row("a", "2026-10-01T09:00:00")])
    proc = claim(wt, "--dry-run", source=main)
    assert proc.returncode == 0, proc.stderr
    assert "将领走 1 个文件、1 行" in proc.stdout
    assert pending_files(main) == ["20261001_main_090000.jsonl"]
    assert _git(wt, "status", "--porcelain").stdout == ""


def test_claim_refuses_on_main_itself(env: dict) -> None:
    main = env["main"]
    pending = write_pending(main, "20261001_main_090000.jsonl", [row("a", "2026-10-01T09:00:00")])
    tracked_before = (main / "timing" / "20261001_main.jsonl").read_bytes()
    proc = claim(main, source=main)
    assert proc.returncode == 1
    assert "拒绝" in proc.stderr and "main" in proc.stderr
    assert pending.exists(), "被拒绝时一个待领文件也不能删"
    assert (main / "timing" / "20261001_main.jsonl").read_bytes() == tracked_before
    assert _status(main) == ""


def test_claim_refuses_on_main_even_with_default_source(env: dict) -> None:
    main = env["main"]
    write_pending(main, "20261001_main_090000.jsonl", [row("a", "2026-10-01T09:00:00")])
    proc = claim(main)
    assert proc.returncode == 1 and "拒绝" in proc.stderr
    assert pending_files(main) == ["20261001_main_090000.jsonl"]


def test_claim_refuses_on_detached_head(env: dict) -> None:
    det = add_worktree(env["main"], "wt_det", branch=None)
    write_pending(env["main"], "20261001_main_090000.jsonl", [row("a", "2026-10-01T09:00:00")])
    proc = claim(det, source=env["main"])
    assert proc.returncode == 1 and "游离 HEAD" in proc.stderr
    assert pending_files(env["main"]) == ["20261001_main_090000.jsonl"]


def test_claim_refuses_when_source_is_not_main_or_other_repo(env: dict) -> None:
    main, wt, tmp = env["main"], env["wt"], env["tmp"]
    write_pending(main, "20261001_main_090000.jsonl", [row("a", "2026-10-01T09:00:00")])
    # --from 指向一个检出别的分支的工作树
    other_wt = add_worktree(main, "wt2", branch="feature/other_20261001")
    proc = claim(wt, source=other_wt)
    assert proc.returncode == 1 and "不是 main" in proc.stderr
    # --from 指向别的仓库的 main
    stranger = make_main(tmp / "stranger")
    write_pending(stranger, "20261001_main_090000.jsonl", [row("s", "2026-10-01T09:00:00")])
    proc = claim(wt, source=stranger)
    assert proc.returncode == 1 and "不属于同一个仓库" in proc.stderr
    assert pending_files(main) == ["20261001_main_090000.jsonl"]
    assert pending_files(stranger) == ["20261001_main_090000.jsonl"]


def test_claim_never_touches_tracked_or_badly_named_pending_files(env: dict) -> None:
    main, wt = env["main"], env["wt"]
    good = row("good", "2026-10-01T09:00:00")
    write_pending(main, "20261001_main_090000.jsonl", [good])
    tracked = write_pending(main, "20261001_main_120000.jsonl", [row("tracked", "2026-10-01T12:00:00")])
    _git(main, "add", "-f", "timing/_pending/20261001_main_120000.jsonl")
    _git(main, "commit", "-q", "-m", "oops tracked pending")
    stray = write_pending(main, "notes.jsonl", ["{}"])
    write_pending(main, "notes.txt", ["hi"])
    _git(wt, "merge", "-q", "--ff-only", "main")
    proc = claim(wt, source=main)
    assert proc.returncode == 0, proc.stderr
    assert "领走 1 个文件、1 行" in proc.stdout
    assert tracked.exists() and stray.exists()
    assert (main / "timing" / "_pending" / "notes.txt").exists()
    assert "已被 git 跟踪" in proc.stderr and "notes.jsonl" in proc.stderr
    assert (wt / "timing" / "20261001_main.jsonl").read_text(encoding="utf-8").splitlines()[-1] == good
    assert _status(main) == "", "领取不得改动主检出里任何已跟踪文件"


@pytest.mark.parametrize("seed", [11, 12, 13])
def test_invariant_claimed_target_is_existing_plus_deduped_new_lines(env: dict, seed: int) -> None:
    rng = random.Random(seed)
    main, wt = env["main"], env["wt"]
    pool = [row(f"s{i}", f"2026-10-01T{i // 60:02d}:{i % 60:02d}:00") for i in range(12)]
    existing = [row("old_step", "2026-10-01T08:00:00")]
    expected = list(existing)
    seen = set(existing)
    file_count = rng.randint(1, 4)
    clocks = sorted(rng.sample(range(0, 235959, 7919), file_count))
    total_files = 0
    for clock in clocks:
        lines = [rng.choice(pool + existing) for _ in range(rng.randint(0, 5))]
        write_pending(main, f"20261001_main_{clock:06d}.jsonl", lines)
        total_files += 1
        for line in lines:
            if line not in seen:
                seen.add(line)
                expected.append(line)
    proc = claim(wt, source=main)
    assert proc.returncode == 0, proc.stderr
    assert f"领走 {total_files} 个文件、{len(expected) - 1} 行" in proc.stdout
    got = (wt / "timing" / "20261001_main.jsonl").read_text(encoding="utf-8").splitlines()
    assert got == expected
    assert pending_files(main) == []
    # 幂等：同一批领完再领，结果不变
    again = claim(wt, source=main)
    assert again.returncode == 0 and "领走 0 个文件、0 行" in again.stdout
    assert (wt / "timing" / "20261001_main.jsonl").read_text(encoding="utf-8").splitlines() == expected
    assert _status(main) == ""


# ---------------------------------------------------------------------------
# 统计脚本、.gitignore、判级
# ---------------------------------------------------------------------------

def _write_rows(path: Path, rows: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(rows) + "\n", encoding="utf-8", newline="\n")


def test_timing_report_includes_pending_by_default_and_no_pending_excludes(tmp_path: Path) -> None:
    timing = tmp_path / "timing"
    _write_rows(timing / "20261001_feat.jsonl", [row("a", "2026-10-01T09:00:00", seconds=10.0, branch="feature/feat_20261001")])
    _write_rows(
        timing / "_pending" / "20261001_main_100000.jsonl",
        [row("a", "2026-10-01T10:00:00", seconds=30.0), row("_total", "2026-10-01T10:00:00", seconds=40.0)],
    )
    with_pending, bad = tr.build_report(timing)
    assert bad == 0 and with_pending["files"] == 2 and with_pending["rows"] == 3
    step_a = next(s for s in with_pending["steps"] if s["step"] == "a")
    assert step_a["count"] == 2 and step_a["total"] == 40.0
    without, _ = tr.build_report(timing, include_pending=False)
    assert without["files"] == 1 and without["rows"] == 1
    # 命令行开关
    base = [sys.executable, str(TOOLCHAIN_DIR / "timing_report.py"), "--timing-dir", str(timing), "--json"]
    default = json.loads(subprocess.run(base, capture_output=True, encoding="utf-8").stdout)
    excluded = json.loads(subprocess.run(base + ["--no-pending"], capture_output=True, encoding="utf-8").stdout)
    assert (default["files"], default["rows"]) == (2, 3)
    assert (excluded["files"], excluded["rows"]) == (1, 1)


def test_timing_report_totals_are_unchanged_by_claiming(env: dict) -> None:
    """不变量：领取前（主检出待领目录）与领取后（分支 timing/）统计的行数与合计一致，不重复、不丢。"""
    main, wt = env["main"], env["wt"]
    lines = [row("a", "2026-10-01T09:00:00", seconds=3.0), row("b", "2026-10-01T09:00:03", seconds=4.5)]
    write_pending(main, "20261001_main_090000.jsonl", lines)
    # 主检出的已跟踪记录已含 old_step（1 行 1.0s）；待领 2 行 7.5s
    before, _ = tr.build_report(main / "timing")
    assert before["rows"] == 3
    assert claim(wt, source=main).returncode == 0
    after_main, _ = tr.build_report(main / "timing")
    after_wt, _ = tr.build_report(wt / "timing")
    assert after_main["rows"] == 1  # 主检出只剩已跟踪的那一行
    assert after_wt["rows"] == 3  # 并入后分支侧 = 原有 1 行 + 领来的 2 行
    total = lambda rep: sum(p["total"] for p in rep["phases"])  # noqa: E731
    assert total(before) == total(after_wt) == 8.5


def test_pending_directory_is_gitignored_and_judged_t0() -> None:
    probe = "timing/_pending/20261001_main_100005.jsonl"
    ignored = subprocess.run(
        ["git", "-C", str(REPO_ROOT), "check-ignore", "-q", probe], env=git_env(), capture_output=True
    )
    assert ignored.returncode == 0, "timing/_pending/ 必须被 .gitignore 覆盖（生成物不进 git）"
    level = subprocess.run(
        [sys.executable, str(TOOLCHAIN_DIR / "change_impact.py"), "--repo-root", str(REPO_ROOT), "--paths", probe, "--print-level"],
        capture_output=True,
        encoding="utf-8",
        env=git_env(),
    )
    assert level.returncode == 0, level.stderr
    assert level.stdout.strip() == "T0", "待领目录仍落在模块表 timing/** 的 T0 里"


def test_check_ps1_does_not_build_timing_paths_itself() -> None:
    """路径只由 Get-GateTimingFilePath 决定：check.ps1 不得自己拼 timing 路径绕过"main 写待领目录"。"""
    check = CHECK_PS1.read_text(encoding="utf-8-sig")
    assert "Write-GateTimingFromRun" in check
    assert "Get-GateTimingFilePath" not in check
    assert '"timing"' not in check.replace("写入 timing/", "")
    gate = TIMING_PS1.read_text(encoding="utf-8-sig")
    assert "_pending" in gate and "Test-GateTimingUsesPending" in gate
