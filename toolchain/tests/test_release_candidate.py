"""发布候选阶段的回归测试（ADR-0160，P1 拆分）。

覆盖对象：``toolchain/_release_candidate.ps1``（候选序号、样板仓库定位与就绪判定、``Invoke-ReleaseCandidateStage`` 的
成功/失败/放弃/续跑各路径）；以及 ``build.ps1`` 与续跑状态里与候选阶段有关的接线（阶段顺序、参数校验、`-Dist X-rc.N`）。

阶段函数的外部动作（构建、发布、查询、升级样板、跑样板门禁）全部走可注入的脚本块，这里用伪造实现记录调用顺序，
不依赖私服、Unity 与样板仓库。端到端（真实 build.ps1 + 桩命令）的接线测试在 ``test_release_resume.py``。
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, ps_quote, run_ps_json

RESUME_LIB = REPO_ROOT / "toolchain" / "_release_resume.ps1"
CANDIDATE_LIB = REPO_ROOT / "toolchain" / "_release_candidate.ps1"
BUILD_PS1 = REPO_ROOT / "build.ps1"
PACKAGES = ["pkg.a", "pkg.b", "pkg.c"]


def _preamble() -> str:
    return f". {ps_quote(RESUME_LIB)}\n. {ps_quote(CANDIDATE_LIB)}\n"


def _make_repo(tmp: Path) -> Path:
    repo = tmp / "repo"
    (repo / "toolchain" / "registry").mkdir(parents=True)
    (repo / "toolchain" / "registry" / "registry.json").write_text(
        json.dumps({"url": "http://registry.invalid/", "packages": PACKAGES}), encoding="utf-8"
    )
    (repo / "dist").mkdir()
    return repo


def _make_samples(tmp: Path, *, gate: bool = True, upgrade: bool = True, git: bool = True) -> Path:
    samples = tmp / "ws-game-samples"
    (samples / "tools").mkdir(parents=True)
    if git:
        (samples / ".git").mkdir()
    if gate:
        (samples / "check.ps1").write_text("exit 0\n", encoding="utf-8")
    if upgrade:
        (samples / "tools" / "upgrade_framework.ps1").write_text("exit 0\n", encoding="utf-8")
    return samples


# ----------------------------------------------------------------------------
# 纯函数
# ----------------------------------------------------------------------------

def test_next_candidate_version_and_npm_json_parsing(tmp_path: Path) -> None:
    body = _preamble() + """
$out = [ordered]@{}
$out.none = Get-NextCandidateVersion -Version '2.0.0' -ExistingVersions @()
$out.only_stable = Get-NextCandidateVersion -Version '2.0.0' -ExistingVersions @('1.100.0','2.0.0')
$out.rc1_present = Get-NextCandidateVersion -Version '2.0.0' -ExistingVersions @('2.0.0-rc.1')
$out.gap = Get-NextCandidateVersion -Version '2.0.0' -ExistingVersions @('2.0.0-rc.1','2.0.0-rc.7','2.0.0-rc.3')
$out.numeric_not_lexical = Get-NextCandidateVersion -Version '2.0.0' -ExistingVersions @('2.0.0-rc.9','2.0.0-rc.10')
$out.other_version_rc_ignored = Get-NextCandidateVersion -Version '2.0.0' -ExistingVersions @('2.0.1-rc.5','1.9.9-rc.4','2.0.0-dryrun','2.0.0-rc.x')
$out.json_array = @(ConvertFrom-NpmVersionsJson -Text '["1.0.0","2.0.0-rc.1"]')
$out.json_single = @(ConvertFrom-NpmVersionsJson -Text '"1.0.0"')
$out.json_empty = @(ConvertFrom-NpmVersionsJson -Text '')
$out.json_error = @(ConvertFrom-NpmVersionsJson -Text '{"error":{"code":"E404"}}')
$out.json_garbage = @(ConvertFrom-NpmVersionsJson -Text 'not json')
$out | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    r = run_ps_json(tmp_path, body, name="cand_pure")
    assert r["none"] == "2.0.0-rc.1"
    assert r["only_stable"] == "2.0.0-rc.1"
    assert r["rc1_present"] == "2.0.0-rc.2"
    assert r["gap"] == "2.0.0-rc.8"
    assert r["numeric_not_lexical"] == "2.0.0-rc.11"
    assert r["other_version_rc_ignored"] == "2.0.0-rc.1"
    assert r["json_array"] == ["1.0.0", "2.0.0-rc.1"]
    assert r["json_single"] == ["1.0.0"]
    for key in ("json_empty", "json_error", "json_garbage"):
        assert r[key] in ([], None), key


def test_samples_repo_resolution_and_readiness(tmp_path: Path) -> None:
    ready = _make_samples(tmp_path / "ready")
    no_gate = _make_samples(tmp_path / "nogate", gate=False)
    no_upgrade = _make_samples(tmp_path / "noupg", upgrade=False)
    no_git = _make_samples(tmp_path / "nogit", git=False)
    missing = tmp_path / "does_not_exist"
    body = _preamble() + f"""
$out = [ordered]@{{}}
$out.explicit = Resolve-SamplesRepoPath -RepoRoot {ps_quote(tmp_path)} -Explicit {ps_quote(ready)} -EnvValue {ps_quote(no_gate)}
$out.env = Resolve-SamplesRepoPath -RepoRoot {ps_quote(tmp_path)} -EnvValue {ps_quote(no_gate)}
$out.ready = @(Get-SamplesRepoProblems -SamplesRepo {ps_quote(ready)})
$out.no_gate = @(Get-SamplesRepoProblems -SamplesRepo {ps_quote(no_gate)})
$out.no_upgrade = @(Get-SamplesRepoProblems -SamplesRepo {ps_quote(no_upgrade)})
$out.no_git = @(Get-SamplesRepoProblems -SamplesRepo {ps_quote(no_git)})
$out.missing = @(Get-SamplesRepoProblems -SamplesRepo {ps_quote(missing)})
$out | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    r = run_ps_json(tmp_path, body, name="cand_samples")
    assert Path(r["explicit"]) == ready
    assert Path(r["env"]) == no_gate
    assert r["ready"] in ([], None)
    assert len(r["no_gate"]) == 1 and "check.ps1" in r["no_gate"][0]
    assert len(r["no_upgrade"]) == 1 and "upgrade_framework.ps1" in r["no_upgrade"][0]
    assert len(r["no_git"]) == 1 and ".git" in r["no_git"][0]
    assert len(r["missing"]) == 1 and "不存在" in r["missing"][0]


# ----------------------------------------------------------------------------
# 阶段函数
# ----------------------------------------------------------------------------

_STAGE_DRIVER = """
$repo = {repo}
$statePath = Join-Path $repo 'dist\\release-2.0.0.state.json'
$names = @({names})
$s = New-ReleaseState -Version '2.0.0' -ParentCommit 'p' -PreviousVersion '1.100.0' -GateConclusion 'g' -PackageNames $names
{state_setup}
Save-ReleaseState -Path $statePath -State $s
$script:log = New-Object System.Collections.Generic.List[string]
$registryHas = {existing}
$list = {{ param($pkg) $script:log.Add("list $pkg"); return @($registryHas) }}
$build = {{ param($rc) $script:log.Add("build $rc"); {build_body} return (Join-Path $repo ('dist\\' + $rc)) }}
$publish = {{ param($pkgDir, $rc) $script:log.Add("publish " + (Split-Path -Leaf $pkgDir) + " $rc"); {publish_body} }}
$upgrade = {{ param($samples, $rc, $url) $script:log.Add("upgrade $rc"); {upgrade_body} }}
$gate = {{ param($samples) $script:log.Add("gate"); return [PSCustomObject]@{{ ExitCode = {gate_exit}; Conclusion = '{gate_conclusion}' }} }}
foreach ($n in $names) {{ foreach ($k in 1..4) {{ New-Item -ItemType Directory -Force -Path (Join-Path $repo ('dist\\2.0.0-rc.' + $k + '\\packages\\' + $n)) | Out-Null }} }}
$thrown = ''
$result = $null
try {{
    $result = Invoke-ReleaseCandidateStage -RepoRoot $repo -Version '2.0.0' -StatePath $statePath -SamplesRepo {samples} `
        -RegistryUrl 'http://registry.invalid/' -NpmrcPath 'x.npmrc' {flags} `
        -ListVersions $list -BuildPackages $build -PublishPackage $publish -UpgradeSamples $upgrade -RunSamplesGate $gate
}} catch {{
    $thrown = $_.Exception.Message
}}
$state = Read-ReleaseState -Path $statePath
$out = [ordered]@{{
    log = @($script:log)
    thrown = $thrown
    status = $(if ($result) {{ $result.Status }} else {{ $null }})
    candidate = $(if ($result) {{ $result.Candidate }} else {{ $null }})
    candidateDone = [bool]$state['stages']['candidate']['done']
    candidateDetail = $state['stages']['candidate']['detail']
}}
$out | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""


def _run_stage(tmp: Path, *, samples: Path | None = None, existing: list[str] | None = None, build_body: str = "",
               publish_body: str = "", upgrade_body: str = "", gate_exit: int = 0, gate_conclusion: str = "ok",
               flags: str = "", state_setup: str = "") -> dict:
    repo = _make_repo(tmp)
    samples = samples if samples is not None else _make_samples(tmp)
    existing_text = "@(" + ",".join(ps_quote(v) for v in (existing or [])) + ")"
    body = _preamble() + _STAGE_DRIVER.format(
        repo=ps_quote(repo), names=",".join(ps_quote(n) for n in PACKAGES), state_setup=state_setup,
        existing=existing_text, build_body=build_body, publish_body=publish_body, upgrade_body=upgrade_body,
        gate_exit=gate_exit, gate_conclusion=gate_conclusion, samples=ps_quote(samples), flags=flags,
    )
    return run_ps_json(tmp, body, name="cand_stage")


def test_candidate_stage_green_path_publishes_rc_then_upgrades_samples_then_runs_its_gate(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, gate_conclusion="samples gate ok 42 passed")
    assert r["thrown"] == "", r["thrown"]
    assert r["status"] == "Passed" and r["candidate"] == "2.0.0-rc.1"
    expected = (
        [f"list {p}" for p in PACKAGES]
        + ["build 2.0.0-rc.1"]
        + [f"publish {p} 2.0.0-rc.1" for p in PACKAGES]
        + ["upgrade 2.0.0-rc.1", "gate"]
    )
    assert r["log"] == expected
    assert r["candidateDone"] is True
    assert "2.0.0-rc.1" in r["candidateDetail"] and "samples gate ok 42 passed" in r["candidateDetail"]


def test_candidate_stage_takes_the_next_rc_number_from_the_registry(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, existing=["1.100.0", "2.0.0-rc.1", "2.0.0-rc.2"])
    assert r["thrown"] == "", r["thrown"]
    assert r["candidate"] == "2.0.0-rc.3"
    assert "build 2.0.0-rc.3" in r["log"] and "publish pkg.a 2.0.0-rc.3" in r["log"]


def test_red_samples_gate_stops_the_release_before_it_can_tag(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, gate_exit=1, gate_conclusion="")
    assert "未通过" in r["thrown"] and "打标签前终止" in r["thrown"] and "-Resume" in r["thrown"], r["thrown"]
    assert r["log"][-2:] == ["upgrade 2.0.0-rc.1", "gate"]
    assert r["candidateDone"] is False, "红的候选不得标记完成：发布流程据此不继续打包/打标签"
    assert r["status"] is None


def test_candidate_build_failure_publishes_nothing(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, build_body="throw '构建候选包失败(注入)'; ")
    assert "构建候选包失败" in r["thrown"]
    assert not any(line.startswith("publish") for line in r["log"])
    assert "upgrade 2.0.0-rc.1" not in r["log"] and "gate" not in r["log"]
    assert r["candidateDone"] is False


def test_candidate_publish_failure_does_not_upgrade_or_run_samples(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, publish_body="if ($pkgDir -like '*pkg.b') { throw '发布失败(注入)' }")
    assert "发布失败" in r["thrown"]
    assert [x for x in r["log"] if x.startswith("publish")] == ["publish pkg.a 2.0.0-rc.1", "publish pkg.b 2.0.0-rc.1"]
    assert "gate" not in r["log"] and r["candidateDone"] is False


def test_candidate_upgrade_failure_does_not_run_the_samples_gate(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, upgrade_body="throw '升级失败(注入)'")
    assert "升级失败" in r["thrown"] and "gate" not in r["log"] and r["candidateDone"] is False


def test_missing_samples_repo_refuses_unless_explicitly_opted_out(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, samples=tmp_path / "no_such_samples")
    assert "拒绝发布" in r["thrown"] and "-SkipSamplesCandidate" in r["thrown"], r["thrown"]
    assert r["log"] == [], "拒绝时不得做任何外部动作（不构建、不发布）"
    assert r["candidateDone"] is False


def test_samples_repo_without_gate_script_refuses(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, samples=_make_samples(tmp_path / "x", gate=False))
    assert "拒绝发布" in r["thrown"] and "check.ps1" in r["thrown"]
    assert r["log"] == []


def test_opt_out_is_allowed_and_leaves_a_record(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, samples=tmp_path / "no_such_samples", flags="-SkipSamplesCandidate")
    assert r["thrown"] == "" and r["status"] == "OptOut"
    assert r["log"] == [], "放弃候选验证时不构建、不发布 rc、不跑样板"
    assert r["candidateDone"] is True and "OPT-OUT" in r["candidateDetail"]


def test_resume_skips_a_candidate_that_is_already_green(tmp_path: Path) -> None:
    setup = "$s['stages']['candidate']['done'] = $true; $s['stages']['candidate']['detail'] = 'earlier rc ok'"
    r = _run_stage(tmp_path, flags="-Resume", state_setup=setup)
    assert r["thrown"] == "" and r["status"] == "AlreadyDone"
    assert r["log"] == []


def test_resume_after_a_red_candidate_publishes_a_fresh_rc_and_never_overwrites(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, flags="-Resume", existing=["2.0.0-rc.1"])
    assert r["thrown"] == "" and r["candidate"] == "2.0.0-rc.2"
    assert "publish pkg.a 2.0.0-rc.2" in r["log"] and "publish pkg.a 2.0.0-rc.1" not in r["log"]


def test_missing_gate_conclusion_line_is_recorded_honestly(tmp_path: Path) -> None:
    r = _run_stage(tmp_path, gate_conclusion="")
    assert r["thrown"] == "" and "未捕获到" in r["candidateDetail"]


# ----------------------------------------------------------------------------
# 接线：阶段顺序、续跑计划、build.ps1 参数
# ----------------------------------------------------------------------------

def test_candidate_stage_sits_between_commit_and_packaging_in_the_stage_order(tmp_path: Path) -> None:
    body = _preamble() + """
$ids = @(Get-ReleaseStageIds -PackageNames @('a','b'))
$names = @('a','b')
$state = New-ReleaseState -Version '2.0.0' -ParentCommit 'p' -PreviousVersion '1.0.0' -GateConclusion 'g' -PackageNames $names
$state['stages']['commit']['done'] = $true
$plan = Get-ReleaseResumePlan -State $state -PackageNames $names
$state['stages']['candidate']['done'] = $true
$plan2 = Get-ReleaseResumePlan -State $state -PackageNames $names
$state['stages']['packaging']['done'] = $true; $state['stages']['selfCheck']['done'] = $true; $state['stages']['tag']['done'] = $true
$state['stages']['candidate']['done'] = $false
$plan3 = Get-ReleaseResumePlan -State $state -PackageNames $names
$out = [ordered]@{
  ids = $ids
  first_after_commit = $plan.FirstUnfinished
  candidate_required = [bool](@($plan.Stages | Where-Object { $_.Id -eq 'candidate' })[0].Required)
  first_after_candidate = $plan2.FirstUnfinished
  need_packaging_with_candidate_open = $plan3.NeedPackaging
  first_when_only_candidate_open = $plan3.FirstUnfinished
  inconsistent_when_only_candidate_open = $plan3.Inconsistent
}
$out | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    r = run_ps_json(tmp_path, body, name="cand_order")
    assert r["ids"][:4] == ["gate", "commit", "candidate", "packaging"]
    assert r["first_after_commit"] == "candidate" and r["candidate_required"] is True
    assert r["first_after_candidate"] == "packaging"
    assert r["first_when_only_candidate_open"] == "candidate", "候选没过就不能直接进打标签"
    assert r["need_packaging_with_candidate_open"] is False
    assert r["inconsistent_when_only_candidate_open"] == "", "候选在打包之前，打包后的阶段已完成时候选未完成不算状态矛盾"


def test_build_script_wires_the_candidate_stage_before_packaging_and_validates_flags() -> None:
    text = BUILD_PS1.read_text(encoding="utf-8-sig")
    assert '[string]$SamplesRepo = ""' in text and "[switch]$SkipSamplesCandidate" in text
    assert text.count("Invoke-ReleaseCandidateStep") >= 3, "定义 + 正常流程 + 续跑各一处调用"
    first_call = text.index("        Invoke-ReleaseCandidateStep\n")
    assert first_call < text.index("# 从这里到脚本末尾包在一个 try/finally 里"), "候选阶段必须在打包节之前"
    assert "DistVersionCandidatePattern" in text
    assert "-SamplesRepo/-SkipSamplesCandidate 仅在同传 -Release" in text


def test_prune_dist_treats_candidate_directories_as_disposable() -> None:
    text = (REPO_ROOT / "toolchain" / "prune_dist.ps1").read_text(encoding="utf-8-sig")
    assert r"(-dryrun|-rc\.\d+)?" in text


def test_candidate_lib_has_utf8_bom_and_lf() -> None:
    raw = CANDIDATE_LIB.read_bytes()
    assert raw.startswith(b"\xef\xbb\xbf") and b"\r" not in raw
