"""发布提速（2026-10-05，ADR-0156）测试：发布第 5 步复用全量记录改跑定向门禁、合并提交的 MergeSkip 档、
只改 ``toolchain/gate_floors.json`` 的提交只跑下限登记文件的 pytest。

背景：1.99.0 发布实测约 3 小时——合并前在集成工作树跑过一次全量（54 分钟，登记为 full-20261001-42），
``build.ps1 -Release`` 第 5 步又在主检出把同一份全量原样重跑一遍（再 54 分钟，用例数逐项相同），而第 3b 步的发布守卫
此前已经证明"该全量记录之后只改了文档类文件"；另有只改 ``gate_floors.json`` 的提交、已被合并分支的合并提交各被提交钩子
多跑了约 20 分钟。每个行为一支复现 + 一支不变量：

1. ``Get-ReleaseGatePlan``（``toolchain/_release_regression_guard.ps1``）选择逻辑（临时 git 仓库 + 伪造 REGRESSION_LOG，
   纯 PowerShell 判定）：
   - 复现：守卫放行 + 工作树只有版本写回改动 -> 定向，基线是记录提交（全 40 位），参数 ``-Changed <sha> -AbiStrict -FailFast -NoTiming``；
     记录就是 HEAD 时同理；
   - 不变量：记录缺失 / 只有 -SkipUnity 记录 / 记录之后改过代码 / 工作树有版本文件之外的改动 / 显式 -FullRegate / -DryRun -> 全量，
     全量参数恒为 ``-AbiStrict -FailFast -NoTiming``（不得带 -Changed/-Il2cpp/-SkipUnity）。
2. 端到端（真实 ``build.ps1`` 跑在 ``_release_skeleton.py`` 的骨架仓库里，``check.ps1`` 是记录参数的桩）：默认发布 ->
   桩 check.ps1 收到 ``-Changed <骨架里的全量记录提交>``，日志与状态文件写明复用的记录与结论行；``-FullRegate`` -> 全量参数；
   ``-DryRun`` -> 全量参数；``-FullRegate`` 的非法组合被拒绝。
3. 提交钩子 MergeSkip（``Get-PreCommitMergeSkip`` + ``toolchain/precommit_tier.ps1`` 的 CLI 输出）：
   - 复现：``git merge --no-ff --no-commit`` 一个已有含 Unity 全量记录背书、且主线是其祖先的分支 -> 合并结果的树与第二父提交的树逐字节
     相同 -> ``MergeSkip``；
   - 不变量：主线有独立改动（树不同）/ 第二父提交没有记录 / 章鱼合并 / 非合并提交 -> 不跳过（CLI 仍输出 ``Full``）。
4. 静态：``check.ps1`` 的 ``floors_pytest`` 步骤只跑 ``test_gate_floors_logic.py``、只在定向模式出现；钩子脚本认 MergeSkip。
   （``gate_floors.json`` 单独改动的影响集判定本身在 ``test_change_impact.py``。）
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
from pathlib import Path

import pytest

from _git_env import clean_git_env, init_temp_repo, run_git
from _ps_harness import REPO_ROOT, find_powershell, ps_quote, run_ps_json
from _ps_subprocess_env import clean_powershell_env
from _release_skeleton import Skeleton, build_skeleton

GUARD = REPO_ROOT / "toolchain" / "_release_regression_guard.ps1"
TIERING = REPO_ROOT / "toolchain" / "_precommit_tiering_guard.ps1"
TIER_CLI = REPO_ROOT / "toolchain" / "precommit_tier.ps1"
CHECK_SCRIPT = REPO_ROOT / "check.ps1"
BUILD_SCRIPT = REPO_ROOT / "build.ps1"
HOOK = REPO_ROOT / ".githooks" / "pre-commit"

LOG_HEADER = "| run_id | 通过/失败 | 对应提交 sha | 日期 |\n| --- | --- | --- | --- |\n"
UNITY_FULL = "通过（46 步全过，含 Unity 全量，PlayMode 647/647）"
FULL_ARGS = ["-AbiStrict", "-FailFast", "-NoTiming"]
WRITEBACK = [
    "VERSION",
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json",
    "adapters/unity/Packages/packages-lock.json",
    "games/_template/package.json",
    "CHANGELOG.md",
]


def _log(rows: list[tuple[str, str]]) -> str:
    lines = [LOG_HEADER.rstrip("\n")]
    for i, (result, sha) in enumerate(rows, start=1):
        lines.append(f"| full-20261005-{i:02d} | {result} | {sha} | 2026-10-05 |")
    return "\n".join(lines) + "\n"


def _commit(repo: Path, files: dict[str, str], message: str) -> str:
    for rel, content in files.items():
        path = repo / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")
        run_git(repo, "add", "--", rel)
    run_git(repo, "commit", "-q", "-m", message)
    return run_git(repo, "rev-parse", "HEAD").stdout.strip()


def _new_repo(root: Path, name: str) -> Path:
    repo = init_temp_repo(root / name, branch="main")
    run_git(repo, "config", "core.autocrlf", "false")
    return repo


def _touch_writeback(repo: Path) -> None:
    """模拟发布第 4 步：工作树里改写版本文件（不提交）。"""
    for rel in WRITEBACK[:4]:
        path = repo / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("1.2.1\n", encoding="utf-8", newline="\n")


# ----------------------------------------------------------------------------
# 1. Get-ReleaseGatePlan 选择逻辑
# ----------------------------------------------------------------------------


def _build_plan_scenarios(root: Path) -> list[dict]:
    items: list[dict] = []

    def add(name: str, repo: Path, **flags) -> None:
        items.append({"name": name, "repo": str(repo), **flags})

    def seeded(name: str) -> tuple[Path, str]:
        """带版本文件的仓库：代码提交 -> 返回 (仓库, 被测提交)。"""
        repo = _new_repo(root, name)
        tested = _commit(repo, {"core/a.cs": "class A {}", "VERSION": "1.2.0\n",
                                "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json": "{}\n",
                                "adapters/unity/Packages/packages-lock.json": "{}\n",
                                "games/_template/package.json": "{}\n"}, "tested")
        return repo, tested

    # A. 复现：记录是祖先，其后只有文档类提交，工作树只有版本写回改动 -> 定向。
    repo, tested = seeded("a_valid_ancestor")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested[:8])]), "CHANGELOG.md": "# c\n",
                   "docs/x.md": "x\n", "timing/20261005_x.jsonl": "{}\n"}, "docs + log row")
    _touch_writeback(repo)
    add("a_valid_ancestor", repo)
    items[-1]["tested"] = tested

    # A2. 记录就是 HEAD，但日志文件只是工作树里的未跟踪文件（守卫读工作树）：它属于"版本文件之外的改动"，必须回退全量，
    #     不能因为守卫放行就把混进来的未提交文件放进定向判定。
    repo, tested = seeded("a2_log_untracked_is_foreign")
    (repo / "REGRESSION_LOG.md").write_text(_log([(UNITY_FULL, tested)]), encoding="utf-8", newline="\n")
    _touch_writeback(repo)
    add("a2_log_untracked_is_foreign", repo)

    # B. 记录之后改过代码 -> 全量，原因点名文件。
    repo, tested = seeded("b_code_after_record")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested)])}, "log row")
    _commit(repo, {"core/a.cs": "class A { int x; }"}, "code")
    _touch_writeback(repo)
    add("b_code_after_record", repo)

    # C. 没有回归记录文件 -> 全量。
    repo, _tested = seeded("c_no_log")
    _touch_writeback(repo)
    add("c_no_log", repo)

    # D. 只有 -SkipUnity 的记录 -> 全量。
    repo, tested = seeded("d_skipunity_record")
    _commit(repo, {"REGRESSION_LOG.md": _log([("通过（check.ps1 -SkipUnity -Quick 30 步通过；未跑 Unity 步骤）", tested)])}, "log row")
    _touch_writeback(repo)
    add("d_skipunity_record", repo)

    # E. 守卫放行但显式 -FullRegate -> 全量。
    repo, tested = seeded("e_full_regate")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested)])}, "log row")
    _touch_writeback(repo)
    add("e_full_regate_flag", repo, full_regate=True)

    # F. 守卫放行但 -DryRun（不写回版本文件）-> 全量。
    repo, tested = seeded("f_dry_run")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested)])}, "log row")
    add("f_dry_run", repo, dry_run=True)

    # G. 守卫放行，但工作树除版本文件外还有别的改动（已跟踪的代码改动 / 未跟踪文件）-> 全量，点名文件。
    repo, tested = seeded("g_foreign_dirty")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested)])}, "log row")
    _touch_writeback(repo)
    (repo / "core" / "a.cs").write_text("class A { int dirty; }", encoding="utf-8", newline="\n")
    add("g_tracked_code_dirty", repo)
    repo, tested = seeded("g2_untracked_stray")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested)])}, "log row")
    _touch_writeback(repo)
    (repo / "stray.txt").write_text("x", encoding="utf-8", newline="\n")
    add("g2_untracked_stray", repo)

    # H. 守卫放行且工作树干净（还没写回版本文件）-> 仍定向（"没有额外改动"也满足"只有版本文件改动"）。
    repo, tested = seeded("h_clean_tree")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested)])}, "log row")
    add("h_clean_tree", repo)
    items[-1]["tested"] = tested

    return items


@pytest.fixture(scope="module")
def plans(tmp_path_factory: pytest.TempPathFactory) -> dict:
    find_powershell()
    tmp = tmp_path_factory.mktemp("gate_plan")
    items = _build_plan_scenarios(tmp)
    manifest = tmp / "plans.json"
    manifest.write_text(json.dumps(items, ensure_ascii=False), encoding="utf-8")
    writeback = "@(" + ", ".join(ps_quote(p) for p in WRITEBACK) + ")"
    body = f"""
. {ps_quote(GUARD)}
$items = Get-Content -LiteralPath {ps_quote(manifest)} -Raw -Encoding UTF8 | ConvertFrom-Json
$out = [ordered]@{{}}
foreach ($s in @($items)) {{
    $full = $false; if ($s.PSObject.Properties.Name -contains 'full_regate') {{ $full = [bool]$s.full_regate }}
    $dry = $false; if ($s.PSObject.Properties.Name -contains 'dry_run') {{ $dry = [bool]$s.dry_run }}
    $p = Get-ReleaseGatePlan -RepoRoot $s.repo -AllowedDirtyFiles {writeback} -FullRegate:$full -DryRun:$dry
    $out[$s.name] = [ordered]@{{ mode = [string]$p.Mode; reason = [string]$p.Reason; base = [string]$p.BaseCommit; runId = [string]$p.RunId; args = @($p.CheckArgs | ForEach-Object {{ [string]$_ }}) }}
}}
$out | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    result = run_ps_json(tmp, body, name="gate_plan", timeout=300)
    result["__items__"] = {s["name"]: s for s in items}
    return result


def _args(plan_row: dict) -> list[str]:
    value = plan_row["args"]
    return value if isinstance(value, list) else [value]


def test_valid_guard_and_writeback_only_chooses_targeted_with_record_commit_as_base(plans: dict) -> None:
    """复现：守卫放行 + 工作树只有版本写回改动 -> 定向，基线是全量记录提交（不是 HEAD、不是 main）。"""
    row = plans["a_valid_ancestor"]
    tested = plans["__items__"]["a_valid_ancestor"]["tested"]
    assert row["mode"] == "Targeted", row["reason"]
    assert row["base"] == tested and re.fullmatch(r"[0-9a-f]{40}", row["base"])
    assert row["runId"] == "full-20261005-01"
    assert _args(row) == ["-Changed", tested, *FULL_ARGS]
    assert tested in row["reason"] and "full-20261005-01" in row["reason"], "原因要写明复用的是哪条记录"


def test_targeted_base_is_an_ancestor_of_head_and_clean_tree_is_still_targeted(plans: dict) -> None:
    """不变量：定向的基线必须是 HEAD 的祖先（否则 -Changed 的 merge-base 会变）；工作树干净（尚未写回版本）同样定向。"""
    row = plans["h_clean_tree"]
    assert row["mode"] == "Targeted", row["reason"]
    assert row["base"] == plans["__items__"]["h_clean_tree"]["tested"]
    repo = plans["__items__"]["h_clean_tree"]["repo"]
    proc = run_git(repo, "merge-base", "--is-ancestor", row["base"], "HEAD", check=False)
    assert proc.returncode == 0


@pytest.mark.parametrize("name,needle", [
    ("b_code_after_record", "core/a.cs"),
    ("c_no_log", "找不到回归记录文件"),
    ("d_skipunity_record", "没有任何'含 Unity 全量通过'记录"),
    ("e_full_regate_flag", "-FullRegate"),
    ("f_dry_run", "-DryRun"),
    ("g_tracked_code_dirty", "core/a.cs"),
    ("g2_untracked_stray", "stray.txt"),
    ("a2_log_untracked_is_foreign", "REGRESSION_LOG.md"),
])
def test_everything_else_falls_back_to_full_gate_with_a_reason(plans: dict, name: str, needle: str) -> None:
    """不变量：记录缺失 / 只有 -SkipUnity 记录 / 记录之后改过代码 / -FullRegate / -DryRun / 工作树有版本文件之外的改动 -> 全量，
    原因文字点名为什么没能复用。"""
    row = plans[name]
    assert row["mode"] == "Full", row["reason"]
    assert needle in row["reason"], row["reason"]
    assert row["base"] == "" and row["runId"] == ""


def test_full_args_are_exactly_the_historical_three_switches(plans: dict) -> None:
    """不变量：全量形态的参数与发布提速之前逐项相同，只有三个开关——不带 -Changed（否则成了定向）、不带 -Il2cpp
    （复盘 I-6 回退）、不带 -SkipUnity（发布门禁没有跳过 Unity 的开关）。"""
    for name in ("b_code_after_record", "c_no_log", "d_skipunity_record", "e_full_regate_flag", "f_dry_run",
                 "g_tracked_code_dirty", "g2_untracked_stray"):
        assert _args(plans[name]) == FULL_ARGS, name


# ----------------------------------------------------------------------------
# 2. 端到端：真实 build.ps1 + 骨架仓库 + 桩 check.ps1
# ----------------------------------------------------------------------------

V = "1.2.1"


def _check_calls(skel: Skeleton) -> list[list[str]]:
    return [c["args"] for c in skel.calls() if c["tool"] == "check.ps1"]


@pytest.fixture(scope="module")
def default_release(tmp_path_factory):
    tmp = tmp_path_factory.mktemp("targeted_default")
    skel = build_skeleton(tmp)
    proc = skel.run_build("-Release", V, "-SkipManual")
    assert proc.returncode == 0, f"默认发布在骨架里应成功：\n{proc.stdout_text}\n{proc.stderr_text}"
    return skel, proc


def test_release_step5_reuses_full_record_and_runs_targeted_gate(default_release) -> None:
    """端到端复现：默认 -Release 的第 5 步只调用一次 check.ps1，参数是 -Changed <骨架里全量记录的提交> 加三个开关；
    控制台与状态文件写明复用的记录（run_id + 提交）与门禁结论行；正常流程的其余阶段（发布提交、打包、标签）不受影响。"""
    skel, proc = default_release
    record_commit = _record_commit(skel)
    assert _check_calls(skel) == [["-Changed", record_commit, *FULL_ARGS]], _check_calls(skel)
    out = proc.stdout_text
    assert "full-20261004-01" in out and record_commit in out, "日志要写明复用的是哪条全量记录（run_id + 提交）"
    state = skel.read_state()
    assert "full-20261004-01" in state["gateConclusion"] and record_commit in state["gateConclusion"]
    assert "门禁通过" in state["gateConclusion"], "结论行仍取自 check.ps1 输出的'门禁通过'行"
    assert state["stages"]["gate"]["done"] and state["stages"]["tag"]["done"]
    assert skel.git("cat-file", "-t", f"refs/tags/{skel.tag}") == "tag"


def _record_commit(skel: Skeleton) -> str:
    """骨架里 REGRESSION_LOG 记录的被测提交 = 日志提交之前的提交。"""
    log_commit = skel.git("log", "-1", "--format=%H", "--", "REGRESSION_LOG.md")
    return skel.git("rev-parse", f"{log_commit}~1")


def test_release_full_regate_switch_forces_the_historical_full_gate(tmp_path: Path) -> None:
    """-FullRegate：即使守卫放行也走全量，参数就是此前的三个开关，不带 -Changed。"""
    skel = build_skeleton(tmp_path)
    proc = skel.run_build("-Release", V, "-SkipManual", "-FullRegate")
    assert proc.returncode == 0, f"{proc.stdout_text}\n{proc.stderr_text}"
    assert _check_calls(skel) == [FULL_ARGS]
    assert "-Changed" not in proc.stdout_text
    assert "-FullRegate" in proc.stdout_text


def test_release_dry_run_keeps_the_full_gate(tmp_path: Path) -> None:
    """-DryRun 不写回版本文件，定向门禁看不到发布特有的改动，保持全量演练。"""
    skel = build_skeleton(tmp_path)
    proc = skel.run_build("-Release", V, "-DryRun", "-SkipManual")
    assert proc.returncode == 0, f"{proc.stdout_text}\n{proc.stderr_text}"
    assert _check_calls(skel) == [FULL_ARGS]


@pytest.mark.parametrize("args", [
    ["-FullRegate"],
    ["-Release", V, "-Resume", "-FullRegate"],
])
def test_full_regate_invalid_combinations_are_refused(tmp_path: Path, args: list[str]) -> None:
    """不变量：-FullRegate 只与 -Release 同传有效；与 -Resume（续跑不进入第 5 步）同传被拒绝，且没有任何外部命令被调用。"""
    skel = build_skeleton(tmp_path)
    proc = skel.run_build(*args)
    assert proc.returncode == 1
    assert "-FullRegate" in proc.stdout_text
    assert skel.calls() == []


# ----------------------------------------------------------------------------
# 3. 提交钩子 MergeSkip
# ----------------------------------------------------------------------------


def _copy_hook_scripts(repo: Path) -> None:
    for src in (TIER_CLI, TIERING, GUARD):
        dest = repo / "toolchain" / src.name
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, dest)


def _merge_repo(root: Path, name: str, *, main_diverges: bool = False, record_on_feature: bool = True,
                octopus: bool = False) -> tuple[Path, str]:
    """主线 main + 特性分支 feat：feat 上有代码提交 + 记录行提交（记录指向代码提交）；在 main 上执行
    `git merge --no-ff --no-commit feat`（留下 MERGE_HEAD 与暂存区，即钩子被调用时的现场）。
    返回 (仓库, octopus 时第三条分支 feat2 的提交，否则空串)。"""
    repo = _new_repo(root, name)
    _copy_hook_scripts(repo)
    _commit(repo, {"core/base.cs": "class Base {}", "REGRESSION_LOG.md": LOG_HEADER}, "base")
    run_git(repo, "add", "toolchain")
    run_git(repo, "commit", "-q", "-m", "toolchain scripts")
    run_git(repo, "checkout", "-q", "-b", "feat")
    tested = _commit(repo, {"core/feat.cs": "class Feat {}"}, "feat code")
    if record_on_feature:
        _commit(repo, {"REGRESSION_LOG.md": LOG_HEADER + f"| full-20261005-01 | {UNITY_FULL} | {tested[:8]} | 2026-10-05 |\n"}, "log row")
    other = ""
    if octopus:
        run_git(repo, "checkout", "-q", "main")
        run_git(repo, "checkout", "-q", "-b", "feat2")
        other = _commit(repo, {"core/feat2.cs": "class Feat2 {}"}, "feat2 code")
    run_git(repo, "checkout", "-q", "main")
    if main_diverges:
        _commit(repo, {"core/main_only.cs": "class MainOnly {}"}, "main independent change")
    merge_args = ["merge", "--no-ff", "--no-commit", "feat"] + (["feat2"] if octopus else [])
    run_git(repo, *merge_args)
    return repo, other


def _merge_skip_verdicts(tmp: Path, repos: dict[str, Path]) -> dict:
    items = [{"name": n, "repo": str(r)} for n, r in repos.items()]
    manifest = tmp / "merge_items.json"
    manifest.write_text(json.dumps(items), encoding="utf-8")
    body = f"""
. {ps_quote(GUARD)}
. {ps_quote(TIERING)}
$items = Get-Content -LiteralPath {ps_quote(manifest)} -Raw -Encoding UTF8 | ConvertFrom-Json
$out = [ordered]@{{}}
foreach ($s in @($items)) {{
    $r = Get-PreCommitMergeSkip -RepoRoot $s.repo
    $out[$s.name] = [ordered]@{{ isMerge = [bool]$r.IsMerge; skip = [bool]$r.Skip; reason = [string]$r.Reason }}
}}
$out | ConvertTo-Json -Depth 4 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    return run_ps_json(tmp, body, name="merge_skip", timeout=300)


def _run_tier_cli(repo: Path) -> str:
    """在仓库根里跑钩子实际调用的 toolchain/precommit_tier.ps1（stdin 给暂存清单），返回档位（`TIER|REASON` 的 TIER 段）。"""
    exe = find_powershell()
    staged = run_git(repo, "diff", "--cached", "--name-only").stdout
    proc = subprocess.run(
        [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(repo / "toolchain" / "precommit_tier.ps1")],
        cwd=str(repo), input=staged.encode("utf-8"), capture_output=True, timeout=120,
        env=clean_git_env(clean_powershell_env(exe)),
    )
    assert proc.returncode == 0, proc.stderr.decode("utf-8", errors="replace")
    text = proc.stdout.decode("utf-8", errors="replace").strip()
    return text.split("|", 1)[0].strip()


@pytest.fixture(scope="module")
def merges(tmp_path_factory: pytest.TempPathFactory) -> dict:
    find_powershell()
    tmp = tmp_path_factory.mktemp("merge_skip")
    repos: dict[str, Path] = {}
    repos["clean_ff_like_merge"], _ = _merge_repo(tmp, "clean_ff_like_merge")
    repos["main_diverged"], _ = _merge_repo(tmp, "main_diverged", main_diverges=True)
    repos["no_record"], _ = _merge_repo(tmp, "no_record", record_on_feature=False)
    repos["octopus"], _ = _merge_repo(tmp, "octopus", octopus=True)
    plain = _new_repo(tmp, "plain_commit")
    _copy_hook_scripts(plain)
    _commit(plain, {"core/base.cs": "class Base {}", "REGRESSION_LOG.md": LOG_HEADER}, "base")
    run_git(plain, "add", "toolchain")
    run_git(plain, "commit", "-q", "-m", "toolchain scripts")
    (plain / "core" / "more.cs").write_text("class More {}", encoding="utf-8", newline="\n")
    run_git(plain, "add", "core/more.cs")
    repos["plain_commit"] = plain
    verdicts = _merge_skip_verdicts(tmp, repos)
    verdicts["__repos__"] = repos
    return verdicts


def test_merge_of_gated_branch_with_identical_tree_is_merge_skip(merges: dict) -> None:
    """复现（1.99.0 合并提交的缩影）：特性分支有含 Unity 全量记录背书、主线是它的祖先 -> 合并结果的树与第二父提交的树相同
    -> Skip；钩子 CLI 输出 MergeSkip（不再跑约 20 分钟的 -Staged -Quick 全量）。"""
    row = merges["clean_ff_like_merge"]
    assert row["isMerge"] is True and row["skip"] is True, row["reason"]
    assert "full-20261005-01" in row["reason"]
    assert _run_tier_cli(merges["__repos__"]["clean_ff_like_merge"]) == "MergeSkip"


@pytest.mark.parametrize("name,needle", [
    ("main_diverged", None),
    ("no_record", None),
    ("octopus", "章鱼合并"),
])
def test_merge_skip_is_refused_unless_tree_identical_and_second_parent_has_record(merges: dict, name: str, needle) -> None:
    """不变量：主线有第二父提交没包含的独立改动（树不同）/ 第二父提交没有含 Unity 全量记录 / 章鱼合并 -> 都不跳过，
    钩子 CLI 仍输出 Full（照常跑 -Staged -Quick）。"""
    row = merges[name]
    assert row["isMerge"] is True
    assert row["skip"] is False, row["reason"]
    if needle:
        assert needle in row["reason"], row["reason"]
    assert _run_tier_cli(merges["__repos__"][name]) == "Full"


def test_non_merge_commit_is_never_merge_skip(merges: dict) -> None:
    """不变量：没有 MERGE_HEAD 的普通提交 -> IsMerge=false、Skip=false，档位仍是 Full。"""
    row = merges["plain_commit"]
    assert row["isMerge"] is False and row["skip"] is False and row["reason"] == ""
    assert _run_tier_cli(merges["__repos__"]["plain_commit"]) == "Full"


def test_diverged_merge_reason_explains_the_tree_difference(merges: dict) -> None:
    assert "树不同" in merges["main_diverged"]["reason"]
    assert "含 Unity 全量记录" in merges["no_record"]["reason"]


# ----------------------------------------------------------------------------
# 4. 静态接线
# ----------------------------------------------------------------------------


def test_check_script_floors_step_runs_only_the_floors_test_and_only_in_targeted_mode() -> None:
    text = CHECK_SCRIPT.read_text(encoding="utf-8-sig")
    marker = '-Id "floors_pytest"'
    assert text.count(marker) == 1
    block_start = text.rindex("if ($script:GateStepPlan) {", 0, text.index(marker))
    block = text[block_start: text.index("\n}\n", text.index(marker))]
    assert "toolchain/tests/test_gate_floors_logic.py" in block
    assert set(re.findall(r"toolchain/tests/[A-Za-z0-9_]+\.py", block)) == {"toolchain/tests/test_gate_floors_logic.py"}, \
        "floors_pytest 只许跑下限登记文件那一个 pytest 文件"
    assert "toolchain/tests\"" not in block, "不得退回整个 toolchain/tests 目录"


def test_hook_script_accepts_merge_skip_alongside_release_skip() -> None:
    text = HOOK.read_text(encoding="utf-8")
    assert '[ "$tier" = "MergeSkip" ]' in text and '[ "$tier" = "ReleaseSkip" ]' in text
    assert "MergeSkip" in TIER_CLI.read_text(encoding="utf-8-sig")


def test_build_script_declares_full_regate_and_documents_the_decision() -> None:
    text = BUILD_SCRIPT.read_text(encoding="utf-8-sig")
    assert re.search(r"\[switch\]\$FullRegate\b", text)
    synopsis = text.split("#>", 1)[0]
    assert ".PARAMETER FullRegate" in synopsis and "ADR-0156" in synopsis
    assert "Get-ReleaseGatePlan" in text
