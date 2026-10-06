"""发布续跑（`build.ps1 -Release <版本> -Resume`，2026-10-04）测试。

背景：1.96.1 发布三次失败（打包阶段 dotnet test 偶发红、docfx 崩溃），每次都只能 `git reset --soft` 回退再重跑整条
`-Release`，约 50 分钟的全量门禁白跑。续跑能力的设计与判断记录见 `toolchain/_release_resume.ps1` 文件头、
`build.ps1` `.PARAMETER Resume`、`toolchain/README.md` 发布一节。

分三层，每个机制都有"复现用例 + 不变量用例"：

1. 库函数单元测试（PowerShell 子进程 dot-source `_release_resume.ps1`，宿主跟随 `WS_GAME_PS_HOST` 矩阵）：
   状态文件读写/时间戳互通、前置校验的每一种拒绝、续跑计划、私服/GitHub 判定表、标签/远端状态、npm 查询的 E404 区分。
2. 端到端（真实 `build.ps1` 跑在 `_release_skeleton.py` 搭的最小仓库骨架里；`dotnet`/`npm`/`gh` 是 PATH 上的桩，
   私服与 GitHub Release 是 JSON 文件，`git push` 推到本地裸仓库；绝不触及真实私服/GitHub）：
   正常 `-Release` 的阶段顺序与逐阶段状态；打包失败后续跑（不重跑门禁/不重新提交）；标签已建而私服未发；
   私服内容一致跳过 / 不一致拒绝 / 查询失败中止；推送与 GitHub Release 的跳过/补传/拒绝；各项前置拒绝。
3. 静态：`build.ps1` 里阶段调用顺序、状态标记位置、失败提示与文档登记没有被改掉。

没有被续跑覆盖的情形（写在判断记录里，这里给复现）：第 5 步（全量门禁）失败 -> 没有状态文件、没有发布提交，
`-Resume` 被拒绝（NoState），要修好问题后重跑 `-Release`。
"""

from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

from _git_env import init_temp_repo, run_git
from _ps_harness import REPO_ROOT, run_ps_json
from _release_skeleton import PACKAGE_NAMES, Skeleton, build_skeleton, make_stub_dir

LIB_PATH = REPO_ROOT / "toolchain" / "_release_resume.ps1"
BUILD_PS1 = REPO_ROOT / "build.ps1"

V = "1.2.1"
STAGE_IDS = (
    ["gate", "commit", "candidate", "packaging", "selfCheck", "tag"]
    + [f"registry:{n}" for n in PACKAGE_NAMES]
    + ["push", "githubRelease"]
)
VERSION_FILES = [
    "VERSION",
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json",
    "adapters/unity/Packages/packages-lock.json",
    "games/_template/package.json",
    "CHANGELOG.md",
]


def stage_ids_for(names: list[str]) -> list[str]:
    return ["gate", "commit", "candidate", "packaging", "selfCheck", "tag"] + [f"registry:{n}" for n in names] + ["push", "githubRelease"]


def ps_lit(value: object) -> str:
    return "'" + str(value).replace("'", "''") + "'"


def lib_preamble() -> str:
    return f". {ps_lit(LIB_PATH)}\n"


# ---------------------------------------------------------------------------
# 1. 库函数单元测试
# ---------------------------------------------------------------------------


def test_state_roundtrip_timestamps_and_stage_marks(tmp_path: Path) -> None:
    """状态文件：写读往返、时间戳是带时区的 ISO 字符串（PS7 的 ConvertFrom-Json 会把它解析成 DateTime，读取时必须
    规整回字符串）、Set-ReleaseStage 标记/取消标记、缺文件返回 null、损坏文件抛异常。"""
    body = lib_preamble() + f"""
$path = Join-Path {ps_lit(tmp_path)} 'dist\\release-1.2.1.state.json'
$names = @('a.pkg','b.pkg')
$s = New-ReleaseState -Version '1.2.1' -ParentCommit 'abc123' -PreviousVersion '1.2.0' -GateConclusion 'gate ok' -PackageNames $names
Save-ReleaseState -Path $path -State $s
Set-ReleaseCommitRecorded -StatePath $path -ReleaseCommit 'def456'
Set-ReleaseStage -StatePath $path -Stage 'packaging' -Detail 'pk'
Set-ReleaseStage -StatePath $path -Stage 'selfCheck' -Detail 'sc'
Set-ReleaseStage -StatePath $path -Stage 'selfCheck' -Done $false
$r = Read-ReleaseState -Path $path
$missing = Read-ReleaseState -Path (Join-Path {ps_lit(tmp_path)} 'nope.json')
$bad = Join-Path {ps_lit(tmp_path)} 'bad.json'
[System.IO.File]::WriteAllText($bad, 'not json', (New-Object System.Text.UTF8Encoding($false)))
$badThrew = $false
try {{ Read-ReleaseState -Path $bad | Out-Null }} catch {{ $badThrew = $true }}
$out = [ordered]@{{
  ids = @(Get-ReleaseStageIds -PackageNames $names)
  version = $r['version']; parent = $r['parentCommit']; release = $r['releaseCommit']
  createdAtType = $r['createdAt'].GetType().Name
  createdAt = $r['createdAt']; updatedAt = $r['updatedAt']; gateAt = $r['stages']['gate']['at']
  gateDone = (Test-ReleaseStageDone -State $r -Stage 'gate')
  commitDone = (Test-ReleaseStageDone -State $r -Stage 'commit')
  packagingDone = (Test-ReleaseStageDone -State $r -Stage 'packaging')
  selfCheckDone = (Test-ReleaseStageDone -State $r -Stage 'selfCheck')
  selfCheckAt = $r['stages']['selfCheck']['at']
  unknownDone = (Test-ReleaseStageDone -State $r -Stage 'nonexistent')
  missingIsNull = ($null -eq $missing)
  badThrew = $badThrew
  tmpLeft = (Test-Path ($path + '.tmp'))
}}
[System.IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject $out -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
"""
    r = run_ps_json(tmp_path, body)
    assert r["ids"] == ["gate", "commit", "candidate", "packaging", "selfCheck", "tag", "registry:a.pkg", "registry:b.pkg", "push", "githubRelease"]
    assert (r["version"], r["parent"], r["release"]) == ("1.2.1", "abc123", "def456")
    assert r["createdAtType"] == "String"
    iso = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2}$")
    for key in ("createdAt", "updatedAt", "gateAt"):
        assert iso.match(r[key]), f"{key}={r[key]!r} 不是带时区偏移的 ISO 8601"
    assert r["gateDone"] and r["commitDone"] and r["packagingDone"]
    assert not r["selfCheckDone"] and r["selfCheckAt"] is None
    assert not r["unknownDone"]
    assert r["missingIsNull"] and r["badThrew"]
    assert not r["tmpLeft"], "写状态文件的临时文件不得残留"


def test_state_file_is_written_utf8_lf_without_bom(tmp_path: Path) -> None:
    body = lib_preamble() + f"""
$path = Join-Path {ps_lit(tmp_path)} 'release.state.json'
$s = New-ReleaseState -Version '1.2.1' -ParentCommit 'p' -PreviousVersion '1.2.0' -GateConclusion '门禁通过：中文' -PackageNames @('a')
Save-ReleaseState -Path $path -State $s
[System.IO.File]::WriteAllText($ResultPath, '{{}}', (New-Object System.Text.UTF8Encoding($false)))
"""
    run_ps_json(tmp_path, body)
    raw = (tmp_path / "release.state.json").read_bytes()
    assert not raw.startswith(b"\xef\xbb\xbf")
    assert b"\r" not in raw
    assert raw.endswith(b"\n")
    assert "门禁通过：中文" in raw.decode("utf-8")


def _plan_case(done: list[str], *, registry: bool, publish: bool) -> dict:
    return {"done": done, "registry": registry, "publish": publish}


def test_resume_plan_first_unfinished_and_required_stages(tmp_path: Path) -> None:
    """续跑计划：必需阶段 = gate/commit/packaging/selfCheck/tag 恒必需，registry:* 仅 -PublishRegistry，
    push/githubRelease 仅 -Publish；打包与自检成组（selfCheck 未完成则 packaging 也要重跑）；自相矛盾的状态被识别。"""
    names = ["p1", "p2"]
    base = ["gate", "commit", "candidate", "packaging", "selfCheck"]
    cases = {
        "after_commit_only": _plan_case(["gate", "commit"], registry=False, publish=False),
        "packaging_done_selfcheck_not": _plan_case(["gate", "commit", "candidate", "packaging"], registry=False, publish=False),
        "through_selfcheck": _plan_case(base, registry=False, publish=False),
        "through_tag_default_flags": _plan_case(base + ["tag"], registry=False, publish=False),
        "through_tag_registry_flag": _plan_case(base + ["tag"], registry=True, publish=False),
        "registry_partial": _plan_case(base + ["tag", "registry:p1"], registry=True, publish=False),
        "registry_done_publish_flag": _plan_case(base + ["tag", "registry:p1", "registry:p2"], registry=True, publish=True),
        "everything": _plan_case(base + ["tag", "registry:p1", "registry:p2", "push", "githubRelease"], registry=True, publish=True),
        "inconsistent": _plan_case(["gate", "commit", "tag"], registry=False, publish=False),
    }
    body = lib_preamble() + f"""
$names = @('p1','p2')
$cases = ConvertFrom-Json {ps_lit(json.dumps(cases))}
$result = [ordered]@{{}}
foreach ($prop in $cases.PSObject.Properties) {{
  $c = $prop.Value
  $state = New-ReleaseState -Version '1.2.1' -ParentCommit 'p' -PreviousVersion '1.2.0' -GateConclusion 'g' -PackageNames $names
  foreach ($id in @($c.done)) {{ $state['stages'][$id]['done'] = $true }}
  $plan = Get-ReleaseResumePlan -State $state -PackageNames $names -PublishRegistry:([bool]$c.registry) -Publish:([bool]$c.publish)
  $result[$prop.Name] = [ordered]@{{
    first = $plan.FirstUnfinished
    needPackaging = $plan.NeedPackaging
    inconsistent = $plan.Inconsistent
    required = @($plan.Stages | Where-Object {{ $_.Required }} | ForEach-Object {{ $_.Id }})
  }}
}}
[System.IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject $result -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
"""
    r = run_ps_json(tmp_path, body)
    assert r["after_commit_only"]["first"] == "candidate" and r["after_commit_only"]["needPackaging"] is True
    assert r["packaging_done_selfcheck_not"]["first"] == "packaging", "自检未完成时打包也要重跑（成组）"
    assert r["packaging_done_selfcheck_not"]["needPackaging"] is True
    assert r["through_selfcheck"]["first"] == "tag" and r["through_selfcheck"]["needPackaging"] is False
    assert r["through_tag_default_flags"]["first"] is None
    assert r["through_tag_default_flags"]["required"] == ["gate", "commit", "candidate", "packaging", "selfCheck", "tag"]
    assert r["through_tag_registry_flag"]["first"] == "registry:p1"
    assert r["registry_partial"]["first"] == "registry:p2"
    assert r["registry_done_publish_flag"]["first"] == "push"
    assert r["everything"]["first"] is None
    assert r["everything"]["required"] == stage_ids_for(names)
    assert r["inconsistent"]["inconsistent"] != "" and "tag" in r["inconsistent"]["inconsistent"]
    for name in ("after_commit_only", "through_selfcheck", "everything"):
        assert r[name]["inconsistent"] == "", f"{name} 不应被判为矛盾"


def test_registry_and_github_decision_tables(tmp_path: Path) -> None:
    """私服判定（Publish / SkipIdentical / RefuseDifferent）与 GitHub Release 判定（Create / Skip / Upload / Refuse）。"""
    body = lib_preamble() + f"""
$local = [PSCustomObject]@{{ Integrity = 'sha512-AAA'; Shasum = 'aaa' }}
function NewRemote($found, $integ, $sha) {{ [PSCustomObject]@{{ Found = $found; Integrity = $integ; Shasum = $sha }} }}
$reg = [ordered]@{{
  absent = (Get-RegistryPublishDecision -Local $local -Remote (NewRemote $false '' ''))
  identical = (Get-RegistryPublishDecision -Local $local -Remote (NewRemote $true 'sha512-AAA' 'aaa'))
  identicalMulti = (Get-RegistryPublishDecision -Local $local -Remote (NewRemote $true 'sha1-xxx sha512-AAA' ''))
  different = (Get-RegistryPublishDecision -Local $local -Remote (NewRemote $true 'sha512-BBB' 'aaa'))
  shasumOnlyIdentical = (Get-RegistryPublishDecision -Local $local -Remote (NewRemote $true '' 'aaa'))
  shasumOnlyDifferent = (Get-RegistryPublishDecision -Local $local -Remote (NewRemote $true '' 'bbb'))
  nothingKnown = (Get-RegistryPublishDecision -Local $local -Remote (NewRemote $true '' ''))
}}
$f1 = Join-Path {ps_lit(tmp_path)} 'f1.zip'; [System.IO.File]::WriteAllBytes($f1, [byte[]](1,2,3))
$f2 = Join-Path {ps_lit(tmp_path)} 'f2.lock'; [System.IO.File]::WriteAllBytes($f2, [byte[]](1,2,3,4))
$paths = @($f1, $f2)
$gh = [ordered]@{{
  create = (Get-ReleaseGitHubPlan -ExistingAssets $null -LocalPaths $paths).Action
  skip = (Get-ReleaseGitHubPlan -ExistingAssets @{{ 'f1.zip' = 3; 'f2.lock' = 4 }} -LocalPaths $paths).Action
  upload = (Get-ReleaseGitHubPlan -ExistingAssets @{{ 'f1.zip' = 3 }} -LocalPaths $paths).Action
  uploadMissing = @((Get-ReleaseGitHubPlan -ExistingAssets @{{ 'f1.zip' = 3 }} -LocalPaths $paths).Missing | ForEach-Object {{ Split-Path -Leaf $_ }})
  refuse = (Get-ReleaseGitHubPlan -ExistingAssets @{{ 'f1.zip' = 3; 'f2.lock' = 99 }} -LocalPaths $paths).Action
  refuseNames = @((Get-ReleaseGitHubPlan -ExistingAssets @{{ 'f1.zip' = 3; 'f2.lock' = 99 }} -LocalPaths $paths).Mismatched)
  unknownSizeSkips = (Get-ReleaseGitHubPlan -ExistingAssets @{{ 'f1.zip' = -1; 'f2.lock' = -1 }} -LocalPaths $paths).Action
}}
[System.IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject ([ordered]@{{ reg = $reg; gh = $gh }}) -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
"""
    r = run_ps_json(tmp_path, body)
    reg, gh = r["reg"], r["gh"]
    assert reg["absent"] == "Publish"
    assert reg["identical"] == "SkipIdentical" and reg["identicalMulti"] == "SkipIdentical"
    assert reg["different"] == "RefuseDifferent"
    assert reg["shasumOnlyIdentical"] == "SkipIdentical" and reg["shasumOnlyDifferent"] == "RefuseDifferent"
    assert reg["nothingKnown"] == "RefuseDifferent", "私服没给任何可比对的摘要时不得当作'一致'放行"
    assert gh["create"] == "Create" and gh["skip"] == "Skip"
    assert gh["upload"] == "Upload" and gh["uploadMissing"] == ["f2.lock"]
    assert gh["refuse"] == "Refuse" and gh["refuseNames"] == ["f2.lock"]
    assert gh["unknownSizeSkips"] == "Skip"


def test_npm_tarball_integrity_matches_independent_computation(tmp_path: Path) -> None:
    """不变量：Get-NpmTarballIntegrity 的 sha512(base64)/sha1 与 Python hashlib 对同一文件算出的一致
    （私服比对依赖这个口径与 `npm pack`/`npm publish` 一致）。"""
    import base64
    import hashlib

    blob = tmp_path / "pkg-1.0.0.tgz"
    blob.write_bytes(bytes(range(256)) * 40)
    body = lib_preamble() + f"""
$i = Get-NpmTarballIntegrity -Path {ps_lit(blob)}
[System.IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject $i), (New-Object System.Text.UTF8Encoding($false)))
"""
    r = run_ps_json(tmp_path, body)
    data = blob.read_bytes()
    assert r["Integrity"] == "sha512-" + base64.b64encode(hashlib.sha512(data).digest()).decode("ascii")
    assert r["Shasum"] == hashlib.sha1(data).hexdigest()


# --- 前置校验：每一种拒绝 ---------------------------------------------------


def _make_release_repo(tmp_path: Path, *, release_touches_version: bool = True, extra_file_in_release: bool = False) -> dict:
    """临时仓库：基线提交（VERSION=1.0.0）+ 发布提交（VERSION=1.0.1、CHANGELOG 变）。返回两个提交 sha。"""
    repo = tmp_path / "repo"
    init_temp_repo(repo, branch="main")
    (repo / "VERSION").write_text("1.0.0", encoding="utf-8")
    (repo / "CHANGELOG.md").write_text("# log\n", encoding="utf-8")
    (repo / "other.txt").write_text("o\n", encoding="utf-8")
    (repo / ".gitignore").write_text("dist/\n", encoding="utf-8")
    run_git(repo, "add", "VERSION", "CHANGELOG.md", "other.txt", ".gitignore")
    run_git(repo, "commit", "-q", "-m", "base")
    parent = run_git(repo, "rev-parse", "HEAD").stdout.strip()
    if release_touches_version:
        (repo / "VERSION").write_text("1.0.1", encoding="utf-8")
    (repo / "CHANGELOG.md").write_text("# log\n## [1.0.1]\n", encoding="utf-8")
    add = ["VERSION", "CHANGELOG.md"]
    if extra_file_in_release:
        (repo / "other.txt").write_text("changed\n", encoding="utf-8")
        add.append("other.txt")
    run_git(repo, "add", *add)
    run_git(repo, "commit", "-q", "-m", "release")
    release = run_git(repo, "rev-parse", "HEAD").stdout.strip()
    return {"repo": repo, "parent": parent, "release": release}


def _write_state(repo: Path, **overrides) -> Path:
    """用库函数写一份与仓库吻合的状态文件，再按 overrides 改字段（None 表示置空）。"""
    info = overrides.pop("_info")
    state = {
        "schema": 1, "version": "1.0.1", "previousVersion": "1.0.0",
        "parentCommit": info["parent"], "releaseCommit": info["release"],
        "gateConclusion": "gate ok", "createdAt": "2026-10-04T00:00:00+00:00", "updatedAt": "2026-10-04T00:00:00+00:00",
        "stages": {sid: {"done": sid in ("gate", "commit"), "at": None, "detail": None} for sid in ["gate", "commit", "candidate", "packaging", "selfCheck", "tag", "push", "githubRelease"]},
    }
    state.update(overrides)
    path = repo / "dist" / "release-1.0.1.state.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(state, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return path


def _run_preconditions(tmp_path: Path, repo: Path) -> dict:
    body = lib_preamble() + f"""
$check = Test-ReleaseResumePreconditions -RepoRoot {ps_lit(repo)} -Version '1.0.1' -AllowedCommitFiles @('VERSION','CHANGELOG.md')
$out = [ordered]@{{
  ok = $check.Ok
  codes = @($check.Failures | ForEach-Object {{ $_.Code }})
  nextEmpty = @($check.Failures | Where-Object {{ [string]::IsNullOrWhiteSpace($_.Next) -or [string]::IsNullOrWhiteSpace($_.Message) }}).Count
}}
[System.IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject $out -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
"""
    return run_ps_json(tmp_path, body, name="pre")


def test_preconditions_pass_when_everything_matches(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    _write_state(info["repo"], _info=info)
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is True and r["codes"] == []


def test_preconditions_refuse_no_state(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and r["codes"] == ["NoState"] and r["nextEmpty"] == 0


def test_preconditions_refuse_unreadable_state(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    path = _write_state(info["repo"], _info=info)
    path.write_text("{not json", encoding="utf-8")
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and "StateUnreadable" in r["codes"]


def test_preconditions_refuse_version_mismatch(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    _write_state(info["repo"], _info=info, version="1.0.2")
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and r["codes"] == ["VersionMismatch"] and r["nextEmpty"] == 0


def test_preconditions_refuse_state_without_release_commit(tmp_path: Path) -> None:
    """失败发生在第 6 步提交之前：状态文件有门禁记录但没有发布提交 -> 不在续跑范围。"""
    info = _make_release_repo(tmp_path)
    _write_state(info["repo"], _info=info, releaseCommit=None)
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and r["codes"] == ["NoReleaseCommit"] and r["nextEmpty"] == 0


def test_preconditions_refuse_head_moved(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    _write_state(info["repo"], _info=info)
    (info["repo"] / "other.txt").write_text("later\n", encoding="utf-8")
    run_git(info["repo"], "add", "other.txt")
    run_git(info["repo"], "commit", "-q", "-m", "later commit")
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and r["codes"] == ["HeadMismatch"] and r["nextEmpty"] == 0


def test_preconditions_refuse_parent_mismatch(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    _write_state(info["repo"], _info=info, parentCommit="0" * 40)
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and r["codes"] == ["ParentMismatch"] and r["nextEmpty"] == 0


def test_preconditions_refuse_release_commit_with_extra_files(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path, extra_file_in_release=True)
    _write_state(info["repo"], _info=info)
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and r["codes"] == ["CommitFiles"] and r["nextEmpty"] == 0


def test_preconditions_refuse_dirty_working_tree(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    _write_state(info["repo"], _info=info)
    (info["repo"] / "stray.txt").write_text("x\n", encoding="utf-8")
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and r["codes"] == ["DirtyTree"] and r["nextEmpty"] == 0


def test_preconditions_refuse_local_version_mismatch(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path, release_touches_version=False)
    _write_state(info["repo"], _info=info)
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False and r["codes"] == ["LocalVersionMismatch"] and r["nextEmpty"] == 0


def test_preconditions_report_every_failure_not_just_the_first(tmp_path: Path) -> None:
    """不变量：多项同时不满足时全部列出（用户一次看全，不是修一项再撞一项）。"""
    info = _make_release_repo(tmp_path, release_touches_version=False, extra_file_in_release=True)
    _write_state(info["repo"], _info=info, parentCommit="0" * 40)
    (info["repo"] / "stray.txt").write_text("x\n", encoding="utf-8")
    r = _run_preconditions(tmp_path, info["repo"])
    assert r["ok"] is False
    assert set(r["codes"]) == {"ParentMismatch", "CommitFiles", "DirtyTree", "LocalVersionMismatch"}


# --- 标签 / 远端 / 私服查询 -------------------------------------------------


def test_tag_status_and_stage_idempotence(tmp_path: Path) -> None:
    """标签：Absent -> 创建；再 -Resume 调用时 AtHead 跳过（标签对象不变）；指向别处则拒绝（不移动）。"""
    info = _make_release_repo(tmp_path)
    repo = info["repo"]
    state_path = repo / "dist" / "release-1.0.1.state.json"
    body = lib_preamble() + f"""
$repo = {ps_lit(repo)}
$sp = {ps_lit(state_path)}
$s = New-ReleaseState -Version '1.0.1' -ParentCommit 'p' -PreviousVersion '1.0.0' -GateConclusion 'g' -PackageNames @('a')
Save-ReleaseState -Path $sp -State $s
$st0 = (Get-ReleaseTagStatus -RepoRoot $repo -Tag 'v1.0.1').Status
Invoke-ReleaseTagStage -RepoRoot $repo -Version '1.0.1' -ChangelogSection 'section text' -StatePath $sp | Out-Null
$obj1 = (& git -C $repo rev-parse 'refs/tags/v1.0.1').Trim()
$st1 = (Get-ReleaseTagStatus -RepoRoot $repo -Tag 'v1.0.1').Status
Set-ReleaseStage -StatePath $sp -Stage 'tag' -Done $false
Invoke-ReleaseTagStage -RepoRoot $repo -Version '1.0.1' -ChangelogSection 'section text' -StatePath $sp -Resume | Out-Null
$obj2 = (& git -C $repo rev-parse 'refs/tags/v1.0.1').Trim()
$doneAfterSkip = Test-ReleaseStageDone -State (Read-ReleaseState -Path $sp) -Stage 'tag'
# 标签挪到别处
& git -C $repo tag -d v1.0.1 | Out-Null
& git -C $repo tag v1.0.1 {info['parent']} | Out-Null
$st2 = (Get-ReleaseTagStatus -RepoRoot $repo -Tag 'v1.0.1').Status
Set-ReleaseStage -StatePath $sp -Stage 'tag' -Done $false
$threw = $false
try {{ Invoke-ReleaseTagStage -RepoRoot $repo -Version '1.0.1' -ChangelogSection 's' -StatePath $sp -Resume | Out-Null }} catch {{ $threw = $true }}
$obj3 = (& git -C $repo rev-parse 'refs/tags/v1.0.1').Trim()
$doneAfterRefuse = Test-ReleaseStageDone -State (Read-ReleaseState -Path $sp) -Stage 'tag'
$out = [ordered]@{{ st0 = $st0; st1 = $st1; st2 = $st2; same = ($obj1 -eq $obj2); doneAfterSkip = $doneAfterSkip; threw = $threw; moved = ($obj3 -ne {ps_lit(info['parent'])}); doneAfterRefuse = $doneAfterRefuse }}
[System.IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject $out), (New-Object System.Text.UTF8Encoding($false)))
"""
    r = run_ps_json(tmp_path, body)
    assert (r["st0"], r["st1"], r["st2"]) == ("Absent", "AtHead", "Elsewhere")
    assert r["same"] is True, "标签已在 HEAD 时续跑不得重建标签（对象 id 必须不变）"
    assert r["doneAfterSkip"] is True
    assert r["threw"] is True and r["doneAfterRefuse"] is False
    assert run_git(repo, "rev-parse", "refs/tags/v1.0.1").stdout.strip() == info["parent"], "拒绝时不得移动已有标签"


def test_created_tag_is_annotated_with_changelog_section(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    repo = info["repo"]
    state_path = repo / "dist" / "release-1.0.1.state.json"
    body = lib_preamble() + f"""
$sp = {ps_lit(state_path)}
Save-ReleaseState -Path $sp -State (New-ReleaseState -Version '1.0.1' -ParentCommit 'p' -PreviousVersion '1.0.0' -GateConclusion 'g' -PackageNames @('a'))
Invoke-ReleaseTagStage -RepoRoot {ps_lit(repo)} -Version '1.0.1' -ChangelogSection "## [1.0.1]`nbody line" -StatePath $sp | Out-Null
[System.IO.File]::WriteAllText($ResultPath, '{{}}', (New-Object System.Text.UTF8Encoding($false)))
"""
    run_ps_json(tmp_path, body)
    assert run_git(repo, "cat-file", "-t", "refs/tags/v1.0.1").stdout.strip() == "tag"
    message = run_git(repo, "tag", "-l", "--format=%(contents)", "v1.0.1").stdout
    assert message.startswith("v1.0.1") and "body line" in message
    assert not (repo / "dist" / "tag-message-1.0.1.txt").exists(), "临时标签说明文件不得残留"


def test_remote_status_need_push_already_pushed_and_tag_elsewhere(tmp_path: Path) -> None:
    info = _make_release_repo(tmp_path)
    repo = info["repo"]
    origin = tmp_path / "origin.git"
    origin.mkdir()
    run_git(origin, "init", "-q", "--bare", "-b", "main")
    run_git(repo, "remote", "add", "origin", str(origin))
    run_git(repo, "tag", "-a", "v1.0.1", "-m", "v1.0.1")

    def status() -> dict:
        body = lib_preamble() + f"""
$s = Get-ReleaseRemoteStatus -RepoRoot {ps_lit(repo)} -Tag 'v1.0.1' -Branch 'main'
[System.IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject $s), (New-Object System.Text.UTF8Encoding($false)))
"""
        return run_ps_json(tmp_path, body, name="remote")

    assert status()["Status"] == "NeedPush"
    run_git(repo, "push", "-q", "origin", "main", "refs/tags/v1.0.1")
    r = status()
    assert r["Status"] == "AlreadyPushed" and r["RemoteTagCommit"] == info["release"] == r["RemoteBranchCommit"]
    # 只推了标签但分支落后 -> 仍要推（NeedPush）
    run_git(repo, "push", "-q", "-f", "origin", f"{info['parent']}:refs/heads/main")
    assert status()["Status"] == "NeedPush"
    # 远端标签指向别的提交 -> TagElsewhere
    run_git(repo, "push", "-q", "-f", "origin", f"{info['parent']}:refs/tags/v1.0.1")
    r = status()
    assert r["Status"] == "TagElsewhere" and r["RemoteTagCommit"] == info["parent"]


def test_registry_query_distinguishes_absent_from_failure(tmp_path: Path) -> None:
    """私服查询：E404 才算"没发布过"；其它错误（连不上等）必须抛异常，不得当作可以发布。"""
    stub_dir = tmp_path / "stubs"
    make_stub_dir(stub_dir)
    store = tmp_path / "npm_registry.json"
    store.write_text(json.dumps({"pkg.a@1.0.1": {"integrity": "sha512-ZZZ", "shasum": "zzz"}}), encoding="utf-8")
    npmrc = tmp_path / ".npmrc"
    npmrc.write_text("x\n", encoding="utf-8")
    body = lib_preamble() + f"""
$env:PATH = {ps_lit(stub_dir)} + ';' + $env:PATH
function Q($name) {{ Get-RegistryPackageDist -PackageName $name -Version '1.0.1' -RegistryUrl 'http://registry.invalid/' -NpmrcPath {ps_lit(npmrc)} }}
$found = Q 'pkg.a'
$absent = Q 'pkg.b'
$env:NPM_STUB_VIEW_ERROR = '1'
$threw = $false
try {{ Q 'pkg.a' | Out-Null }} catch {{ $threw = $true }}
$out = [ordered]@{{ found = $found.Found; integrity = $found.Integrity; absent = $absent.Found; threw = $threw }}
[System.IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject $out), (New-Object System.Text.UTF8Encoding($false)))
"""
    r = run_ps_json(tmp_path, body, env_extra={"NPM_STUB_REGISTRY": str(store), "STUB_LOG": str(tmp_path / "calls.jsonl")})
    assert r["found"] is True and r["integrity"] == "sha512-ZZZ"
    assert r["absent"] is False
    assert r["threw"] is True


# ---------------------------------------------------------------------------
# 2. 端到端（真实 build.ps1 + 骨架仓库 + 外部命令桩）
# ---------------------------------------------------------------------------


def _undo(skel: Skeleton, *stages: str) -> None:
    def mutate(state: dict) -> None:
        for s in stages:
            state["stages"][s] = {"done": False, "at": None, "detail": None}

    skel.edit_state(mutate)


def _resume(skel: Skeleton, *flags: str, env: dict | None = None):
    return skel.run_build("-Release", V, "-Resume", "-SkipManual", *flags, env_extra=env)


def _restore(skel: Skeleton, snap: dict) -> None:
    run_git(skel.root, "reset", "-q", "--hard", snap["head"])
    run_git(skel.root, "clean", "-fdq")
    for tag in run_git(skel.root, "tag", "-l").stdout.split():
        run_git(skel.root, "tag", "-d", tag)
    run_git(skel.root, "update-ref", f"refs/tags/{skel.tag}", snap["tag_obj"])
    run_git(skel.root, "push", "-q", "-f", "origin", f"{snap['head']}:refs/heads/main")
    remote_tags = run_git(skel.root, "ls-remote", "--tags", "origin").stdout
    for line in remote_tags.splitlines():
        ref = line.split()[1]
        if ref.endswith("^{}"):
            continue
        run_git(skel.root, "push", "-q", "origin", f":{ref}", check=False)
    run_git(skel.root, "push", "-q", "-f", "origin", f"{snap['tag_obj']}:refs/tags/{skel.tag}")
    skel.state_path.write_text(snap["state"], encoding="utf-8")
    skel.npm_store_path.write_text(snap["npm"], encoding="utf-8")
    skel.gh_store_path.write_text(snap["gh"], encoding="utf-8")


@pytest.fixture(scope="module")
def released(tmp_path_factory):
    """跑一次完整的正常 `-Release -PublishRegistry -Publish`（骨架仓库 + 桩），留下全部阶段完成的现场与快照。"""
    tmp = tmp_path_factory.mktemp("release_flow")
    skel = build_skeleton(tmp)
    proc = skel.run_build("-Release", V, "-SkipManual", "-PublishRegistry", "-Publish")
    assert proc.returncode == 0, f"正常发布在骨架里应成功：\n{proc.stdout_text}\n{proc.stderr_text}"
    snap = {
        "state": skel.state_path.read_text(encoding="utf-8"),
        "npm": skel.npm_store_path.read_text(encoding="utf-8"),
        "gh": skel.gh_store_path.read_text(encoding="utf-8"),
        "head": skel.head(),
        "tag_obj": skel.git("rev-parse", f"refs/tags/{skel.tag}"),
        "calls": skel.call_labels(),
        "stdout": proc.stdout_text,
    }
    return skel, snap


@pytest.fixture
def rel(released):
    skel, snap = released
    _restore(skel, snap)
    yield skel
    _restore(skel, snap)


def test_normal_release_stage_order_and_state_updated_at_every_stage(released) -> None:
    """正常 `-Release` 不变量：外部命令的调用顺序与改动前一致（门禁 -> 构建/测试 -> 打包 -> 私服发布 -> GitHub），
    状态文件每个阶段都写了完成标记且时间戳按阶段顺序不递减，发布提交只含版本文件，工作树最后干净，状态文件没被瘦身删掉。"""
    skel, snap = released
    labels = snap["calls"]
    rc = f"{V}-rc.1"
    expected = (
        ["check.ps1"]
        # 候选阶段（ADR-0160）：查私服已有 rc 序号 -> 构建并打候选包 -> 发 rc 到私服 -> 样板升级 -> 样板门禁
        + [f"npm view {n}" for n in PACKAGE_NAMES]
        + ["dotnet build"]
        + [f"npm pack {n}" for n in PACKAGE_NAMES]
        + [f"npm publish {n}" for n in PACKAGE_NAMES]
        + [f"samples-upgrade {rc}", "samples-check.ps1"]
        # 正式打包与发布
        + ["dotnet build", "dotnet test"]
        + [f"npm pack {n}" for n in PACKAGE_NAMES]
        + [f"npm publish {n}" for n in PACKAGE_NAMES]
        + ["gh release create"]
    )
    assert labels == expected

    state = skel.read_state()
    assert list(state["stages"].keys()) == STAGE_IDS
    assert all(st["done"] for st in state["stages"].values()), state["stages"]
    ats = [state["stages"][sid]["at"] for sid in STAGE_IDS]
    assert ats == sorted(ats), f"阶段完成时间应随阶段顺序不递减：{ats}"
    assert "门禁通过" in state["gateConclusion"], "结论行应取自 check.ps1 输出里的'门禁通过'行"
    assert state["version"] == V and state["previousVersion"] == skel.base_version
    assert state["parentCommit"] == skel.base_commit
    assert state["releaseCommit"] == snap["head"] == skel.head()

    changed = skel.git("diff", "--name-only", "HEAD~1", "HEAD").splitlines()
    assert changed and set(changed) <= set(VERSION_FILES), changed
    assert skel.git("status", "--porcelain") == ""
    assert skel.state_path.exists(), "发版后 dist 瘦身（prune_dist）不得删掉状态文件"
    assert skel.git("cat-file", "-t", f"refs/tags/{skel.tag}") == "tag"
    assert skel.git("ls-remote", "origin", f"refs/tags/{skel.tag}") != ""
    assert (skel.root / "dist" / f"ws-game-{V}.zip").exists() and (skel.root / "dist" / f"ws-game-{V}.lock").exists()


def test_resume_does_nothing_when_all_required_stages_done(rel: Skeleton) -> None:
    """不变量：本次命令行要求的阶段全部完成时，续跑什么外部命令都不调用、不改仓库、退出 0。"""
    n = len(rel.calls())
    head = rel.head()
    proc = _resume(rel, "-PublishRegistry", "-Publish")
    assert proc.returncode == 0, proc.stdout_text
    assert rel.calls_since(n) == []
    assert rel.head() == head and rel.git("status", "--porcelain") == ""


@pytest.mark.parametrize("case", ["no_state", "head_moved", "dirty_tree"])
def test_resume_refusals_end_to_end(rel: Skeleton, case: str) -> None:
    """复现：前置校验在真实 build.ps1 里生效——拒绝时退出码 1、打印拒绝码与"下一步"、不调用任何外部命令、不改任何东西。"""
    expected_code = {"no_state": "NoState", "head_moved": "HeadMismatch", "dirty_tree": "DirtyTree"}[case]
    if case == "no_state":
        rel.state_path.unlink()
    elif case == "head_moved":
        (rel.root / "extra.txt").write_text("x\n", encoding="utf-8")
        run_git(rel.root, "add", "extra.txt")
        run_git(rel.root, "commit", "-q", "-m", "extra")
    else:
        (rel.root / "stray.txt").write_text("x\n", encoding="utf-8")
    n = len(rel.calls())
    head = rel.head()
    proc = _resume(rel, "-PublishRegistry", "-Publish")
    assert proc.returncode == 1, proc.stdout_text
    assert expected_code in proc.stdout_text
    assert rel.calls_since(n) == [], "被拒绝的续跑不得调用任何外部命令"
    assert rel.head() == head
    assert not (rel.root / "dist" / "tag-message-1.2.1.txt").exists()


@pytest.mark.parametrize("args", [
    ["-Resume"],
    ["-Release", V, "-Resume", "-DryRun"],
    ["-Release", V, "-Resume", "-AllowOverwriteDist"],
])
def test_resume_invalid_argument_combinations_refused(rel: Skeleton, args: list[str]) -> None:
    n = len(rel.calls())
    proc = rel.run_build(*args)
    assert proc.returncode == 1, proc.stdout_text
    assert rel.calls_since(n) == []


def test_resume_refuses_inconsistent_state(rel: Skeleton) -> None:
    """状态自相矛盾（打包/自检未完成，但后续阶段已标记完成）：拒绝，不动任何东西。"""
    _undo(rel, "selfCheck")
    n = len(rel.calls())
    proc = _resume(rel)
    assert proc.returncode == 1, proc.stdout_text
    assert rel.calls_since(n) == []
    assert not rel.stage_done("selfCheck")


def test_resume_repackaging_refused_once_tag_exists(rel: Skeleton) -> None:
    """复现 + 不变量：自检未完成要求重打包，但标签已存在时，沿用既有"发布不可变"守卫拒绝覆盖产物（不传
    -AllowOverwriteDist 就不会绕过），dist 产物字节不变。"""
    _undo(rel, "selfCheck", "tag", "push", "githubRelease", *[f"registry:{n}" for n in PACKAGE_NAMES])
    zip_path = rel.root / "dist" / f"ws-game-{V}.zip"
    before = zip_path.read_bytes()
    n = len(rel.calls())
    proc = _resume(rel)
    assert proc.returncode != 0, proc.stdout_text
    assert zip_path.read_bytes() == before
    assert "dotnet build" not in rel.call_labels(rel.calls_since(n)), "守卫应在任何写盘/构建动作之前拒绝"


# --- 私服 -----------------------------------------------------------------


def test_resume_registry_identical_package_skips_publish(rel: Skeleton) -> None:
    """私服已有且内容一致：跳过 npm publish，补记完成标记，私服内容不变。"""
    _undo(rel, *[f"registry:{n}" for n in PACKAGE_NAMES])
    store_before = rel.npm_store_path.read_text(encoding="utf-8")
    n = len(rel.calls())
    proc = _resume(rel, "-PublishRegistry")
    assert proc.returncode == 0, proc.stdout_text
    labels = rel.call_labels(rel.calls_since(n))
    assert not any(l.startswith("npm publish") for l in labels), labels
    assert [l for l in labels if l.startswith("npm view")] == [f"npm view {p}@{V}" for p in PACKAGE_NAMES]
    state = rel.read_state()
    for p in PACKAGE_NAMES:
        st = state["stages"][f"registry:{p}"]
        assert st["done"] and "sha512-" in st["detail"]
        assert st["detail"].endswith(rel.tgz_integrity(p)[0])
    assert rel.npm_store_path.read_text(encoding="utf-8") == store_before


def test_resume_registry_different_content_refused_never_overwritten(rel: Skeleton) -> None:
    """私服已有同版本号但内容不同：拒绝（发布不可变），不发布任何包，私服内容不被改动，完成标记不写。"""
    store = json.loads(rel.npm_store_path.read_text(encoding="utf-8"))
    store[f"{PACKAGE_NAMES[0]}@{V}"]["integrity"] = "sha512-DIFFERENT"
    store[f"{PACKAGE_NAMES[0]}@{V}"]["shasum"] = "0" * 40
    rel.npm_store_path.write_text(json.dumps(store), encoding="utf-8")
    store_before = rel.npm_store_path.read_text(encoding="utf-8")
    _undo(rel, *[f"registry:{n}" for n in PACKAGE_NAMES])
    n = len(rel.calls())
    proc = _resume(rel, "-PublishRegistry")
    assert proc.returncode != 0, proc.stdout_text
    assert not any(l.startswith("npm publish") for l in rel.call_labels(rel.calls_since(n)))
    assert rel.npm_store_path.read_text(encoding="utf-8") == store_before
    assert not rel.stage_done(f"registry:{PACKAGE_NAMES[0]}")
    assert f"-Release {V} -Resume" in proc.stdout_text, "失败后仍应给出续跑命令"


def test_resume_registry_query_failure_aborts_instead_of_publishing(rel: Skeleton) -> None:
    _undo(rel, *[f"registry:{n}" for n in PACKAGE_NAMES])
    n = len(rel.calls())
    proc = _resume(rel, "-PublishRegistry", env={"NPM_STUB_VIEW_ERROR": "1"})
    assert proc.returncode != 0
    assert not any(l.startswith("npm publish") for l in rel.call_labels(rel.calls_since(n))), "查询失败不得当作'没发布过'去发布"


def test_resume_registry_partial_publish_only_the_missing_packages(rel: Skeleton) -> None:
    """复现：tag 已建、私服只发了前两个包（第三个发布失败）-> 续跑只发剩下的包，不重打包、不重新打标签。"""
    store = json.loads(rel.npm_store_path.read_text(encoding="utf-8"))
    for p in PACKAGE_NAMES[2:]:
        del store[f"{p}@{V}"]
    rel.npm_store_path.write_text(json.dumps(store), encoding="utf-8")
    _undo(rel, *[f"registry:{p}" for p in PACKAGE_NAMES[2:]], "push", "githubRelease")
    tag_obj = rel.git("rev-parse", f"refs/tags/{rel.tag}")
    n = len(rel.calls())
    proc = _resume(rel, "-PublishRegistry")
    assert proc.returncode == 0, proc.stdout_text
    labels = rel.call_labels(rel.calls_since(n))
    assert [l for l in labels if l.startswith("npm publish")] == [f"npm publish {p}" for p in PACKAGE_NAMES[2:]]
    assert not any(l.startswith("npm pack") or l == "dotnet build" or l == "check.ps1" for l in labels)
    assert rel.git("rev-parse", f"refs/tags/{rel.tag}") == tag_obj
    final_keys = {k for k in json.loads(rel.npm_store_path.read_text(encoding="utf-8")) if "-rc." not in k}
    assert final_keys == {f"{p}@{V}" for p in PACKAGE_NAMES}
    assert all(rel.stage_done(f"registry:{p}") for p in PACKAGE_NAMES)
    assert not rel.stage_done("push"), "没传 -Publish 时推送/GitHub 阶段不是必需阶段，不应被执行或标记"


def test_resume_without_registry_flag_does_not_touch_registry_stages(rel: Skeleton) -> None:
    _undo(rel, *[f"registry:{p}" for p in PACKAGE_NAMES])
    n = len(rel.calls())
    proc = _resume(rel)
    assert proc.returncode == 0
    assert rel.calls_since(n) == []
    assert not any(rel.stage_done(f"registry:{p}") for p in PACKAGE_NAMES)


# --- 标签 -----------------------------------------------------------------


def test_resume_tag_already_at_head_is_skipped(rel: Skeleton) -> None:
    _undo(rel, "tag")
    tag_obj = rel.git("rev-parse", f"refs/tags/{rel.tag}")
    proc = _resume(rel)
    assert proc.returncode == 0, proc.stdout_text
    assert rel.git("rev-parse", f"refs/tags/{rel.tag}") == tag_obj
    assert rel.stage_done("tag") and "HEAD" in rel.read_state()["stages"]["tag"]["detail"]


def test_resume_tag_missing_is_created_without_repackaging(rel: Skeleton) -> None:
    """复现：标签还没建（打包+自检已完成）-> 续跑直接从打标签起，创建带注释标签，不重新打包。"""
    run_git(rel.root, "tag", "-d", rel.tag)
    _undo(rel, "tag", *[f"registry:{p}" for p in PACKAGE_NAMES], "push", "githubRelease")
    n = len(rel.calls())
    proc = _resume(rel)
    assert proc.returncode == 0, proc.stdout_text
    assert rel.git("cat-file", "-t", f"refs/tags/{rel.tag}") == "tag"
    assert rel.git("rev-parse", f"refs/tags/{rel.tag}^{{commit}}") == rel.head()
    assert rel.calls_since(n) == []
    assert rel.stage_done("tag")


def test_resume_tag_pointing_elsewhere_is_refused_and_not_moved(rel: Skeleton) -> None:
    run_git(rel.root, "tag", "-d", rel.tag)
    run_git(rel.root, "tag", rel.tag, rel.base_commit)
    _undo(rel, "tag")
    proc = _resume(rel)
    assert proc.returncode != 0
    assert rel.git("rev-parse", f"refs/tags/{rel.tag}") == rel.base_commit
    assert not rel.stage_done("tag")


# --- 推送 -----------------------------------------------------------------


def test_resume_push_already_on_remote_is_skipped(rel: Skeleton) -> None:
    _undo(rel, "push", "githubRelease")
    remote_tag = rel.git("ls-remote", "origin", f"refs/tags/{rel.tag}")
    proc = _resume(rel, "-Publish")
    assert proc.returncode == 0, proc.stdout_text
    assert rel.git("ls-remote", "origin", f"refs/tags/{rel.tag}") == remote_tag
    assert rel.read_state()["stages"]["push"]["detail"] == "远端已就绪"


def test_resume_push_runs_when_remote_lacks_tag(rel: Skeleton) -> None:
    run_git(rel.root, "push", "-q", "origin", f":refs/tags/{rel.tag}")
    _undo(rel, "push", "githubRelease")
    proc = _resume(rel, "-Publish")
    assert proc.returncode == 0, proc.stdout_text
    assert rel.git("ls-remote", "origin", f"refs/tags/{rel.tag}") != ""
    assert rel.read_state()["stages"]["push"]["detail"] == "已推送"


def test_resume_push_refused_when_remote_tag_points_elsewhere(rel: Skeleton) -> None:
    run_git(rel.root, "push", "-q", "-f", "origin", f"{rel.base_commit}:refs/tags/{rel.tag}")
    remote_before = rel.git("ls-remote", "origin", f"refs/tags/{rel.tag}")
    _undo(rel, "push", "githubRelease")
    proc = _resume(rel, "-Publish")
    assert proc.returncode != 0
    assert rel.git("ls-remote", "origin", f"refs/tags/{rel.tag}") == remote_before, "不覆盖远端标签"
    assert not rel.stage_done("push")


# --- GitHub Release -------------------------------------------------------


def _gh_assets(rel: Skeleton) -> dict:
    return json.loads(rel.gh_store_path.read_text(encoding="utf-8")).get(rel.tag)


def test_resume_github_release_complete_is_skipped(rel: Skeleton) -> None:
    _undo(rel, "githubRelease")
    n = len(rel.calls())
    proc = _resume(rel, "-Publish")
    assert proc.returncode == 0, proc.stdout_text
    labels = rel.call_labels(rel.calls_since(n))
    assert labels == ["gh release view"], labels
    assert rel.stage_done("githubRelease")


def test_resume_github_release_uploads_only_missing_assets_without_clobber(rel: Skeleton) -> None:
    store = json.loads(rel.gh_store_path.read_text(encoding="utf-8"))
    kept = dict(store[rel.tag])
    missing = [f"ws-game-{V}-samples.zip", "get_framework.ps1"]
    for m in missing:
        del store[rel.tag][m]
    rel.gh_store_path.write_text(json.dumps(store), encoding="utf-8")
    _undo(rel, "githubRelease")
    n = len(rel.calls())
    proc = _resume(rel, "-Publish")
    assert proc.returncode == 0, proc.stdout_text
    new_calls = rel.calls_since(n)
    uploads = [c for c in new_calls if c["tool"] == "gh" and c["args"][:2] == ["release", "upload"]]
    assert len(uploads) == 1 and not [c for c in new_calls if c["args"][:2] == ["release", "create"]]
    args = uploads[0]["args"]
    assert "--clobber" not in args
    assert sorted(Path(a).name for a in args[3:]) == sorted(missing)
    assert _gh_assets(rel) == kept


def test_resume_github_release_absent_is_created_with_all_five_assets(rel: Skeleton) -> None:
    store = json.loads(rel.gh_store_path.read_text(encoding="utf-8"))
    kept = dict(store[rel.tag])
    del store[rel.tag]
    rel.gh_store_path.write_text(json.dumps(store), encoding="utf-8")
    _undo(rel, "githubRelease")
    proc = _resume(rel, "-Publish")
    assert proc.returncode == 0, proc.stdout_text
    assert _gh_assets(rel) == kept and len(kept) == 5


def test_resume_github_release_size_mismatch_refused_never_clobbered(rel: Skeleton) -> None:
    store = json.loads(rel.gh_store_path.read_text(encoding="utf-8"))
    store[rel.tag][f"ws-game-{V}.zip"] = 1
    rel.gh_store_path.write_text(json.dumps(store), encoding="utf-8")
    store_before = rel.gh_store_path.read_text(encoding="utf-8")
    _undo(rel, "githubRelease")
    n = len(rel.calls())
    proc = _resume(rel, "-Publish")
    assert proc.returncode != 0
    assert rel.call_labels(rel.calls_since(n)) == ["gh release view"]
    assert rel.gh_store_path.read_text(encoding="utf-8") == store_before
    assert not rel.stage_done("githubRelease")


def test_resume_github_release_create_failure_keeps_hint_and_state(rel: Skeleton) -> None:
    store = json.loads(rel.gh_store_path.read_text(encoding="utf-8"))
    del store[rel.tag]
    rel.gh_store_path.write_text(json.dumps(store), encoding="utf-8")
    _undo(rel, "githubRelease")
    proc = _resume(rel, "-Publish", env={"GH_STUB_FAIL_CREATE": "1"})
    assert proc.returncode != 0
    assert not rel.stage_done("githubRelease") and rel.stage_done("push")
    assert f"build.ps1 -Release {V} -Resume -Publish" in proc.stdout_text


# --- 失败后续跑（独立现场）-----------------------------------------------


def test_packaging_failure_then_resume_skips_gate_and_does_not_recommit(tmp_path: Path) -> None:
    """复现 1.96.1 的场景：门禁通过、发布提交已产生，打包阶段失败。
    - 失败后：状态文件保留（门禁/提交已完成，打包未完成）、没有标签、失败提示推荐 -Resume 命令；
    - -Resume：不再调用 check.ps1、不重新提交（HEAD 与提交数不变）、不再跑 dotnet test，重新打包 -> 自检 -> 打标签。"""
    skel = build_skeleton(tmp_path)
    first = skel.run_build("-Release", V, "-SkipManual", env_extra={"NPM_STUB_FAIL_PACK_VERSION": V})
    assert first.returncode != 0
    state = skel.read_state()
    assert state["stages"]["gate"]["done"] and state["stages"]["commit"]["done"]
    assert not state["stages"]["packaging"]["done"] and not state["stages"]["tag"]["done"]
    release_commit = skel.head()
    assert state["releaseCommit"] == release_commit and state["parentCommit"] == skel.base_commit
    assert skel.git("tag", "-l") == ""
    assert f"build.ps1 -Release {V} -Resume" in first.stdout_text
    labels1 = skel.call_labels()
    assert labels1.count("check.ps1") == 1 and "dotnet test" in labels1
    assert skel.stage_done("candidate"), "候选阶段在打包之前已通过并记录，续跑不重做"
    commits_before = skel.git("rev-list", "--count", "HEAD")

    n = len(skel.calls())
    second = skel.run_build("-Release", V, "-Resume", "-SkipManual")
    assert second.returncode == 0, f"{second.stdout_text}\n{second.stderr_text}"
    labels2 = skel.call_labels(skel.calls_since(n))
    assert "check.ps1" not in labels2, "续跑不得重跑全量门禁"
    assert "dotnet test" not in labels2, "续跑的打包阶段跳过 dotnet test（门禁已对同一棵代码树跑过）"
    assert labels2.count("dotnet build") == 1
    assert not any(l.startswith("samples-") for l in labels2), "候选已通过，续跑不重跑样板门禁"
    assert [l for l in labels2 if l.startswith("npm pack")] == [f"npm pack {p}" for p in PACKAGE_NAMES]
    assert skel.head() == release_commit and skel.git("rev-list", "--count", "HEAD") == commits_before
    assert skel.git("rev-parse", f"refs/tags/{skel.tag}^{{commit}}") == release_commit
    state2 = skel.read_state()
    for sid in ("gate", "commit", "candidate", "packaging", "selfCheck", "tag"):
        assert state2["stages"][sid]["done"], sid
    assert state2["releaseCommit"] == release_commit
    assert f"powershell -File build.ps1 -Release {V} -Resume" not in second.stdout_text, "成功完成后不应再打印失败恢复提示"
    assert skel.git("status", "--porcelain") == ""


def test_failure_hint_printed_on_exit_path_too(tmp_path: Path) -> None:
    """失败提示在 throw 路径（上一个用例）与 `exit` 路径（这里：dotnet build 非零退出码）都要打印。"""
    skel = build_skeleton(tmp_path)
    proc = skel.run_build("-Release", V, "-SkipManual", env_extra={"DOTNET_STUB_EXIT": "3", "DOTNET_STUB_EXIT_FROM_BUILD": "2"})
    assert proc.returncode == 3
    assert f"build.ps1 -Release {V} -Resume" in proc.stdout_text
    assert skel.stage_done("commit") and not skel.stage_done("packaging")


def test_resume_hint_keeps_the_same_publish_switches(tmp_path: Path) -> None:
    skel = build_skeleton(tmp_path)
    proc = skel.run_build("-Release", V, "-SkipManual", "-PublishRegistry", "-Publish",
                          env_extra={"DOTNET_STUB_EXIT": "3", "DOTNET_STUB_EXIT_FROM_BUILD": "2"})
    assert proc.returncode == 3
    assert f"build.ps1 -Release {V} -Resume -PublishRegistry -Publish -SkipManual -SamplesRepo {skel.samples}" in proc.stdout_text


def test_gate_failure_is_not_resumable_and_gives_no_resume_hint(tmp_path: Path) -> None:
    """不在续跑范围的复现：全量门禁（第 5 步）失败 -> 没有状态文件、没有发布提交；失败不推荐 -Resume；
    之后 -Resume 被拒绝（NoState），要修好问题重跑 -Release。"""
    skel = build_skeleton(tmp_path)
    first = skel.run_build("-Release", V, "-SkipManual", env_extra={"CHECK_STUB_EXIT": "1"})
    assert first.returncode == 1
    assert not skel.state_path.exists()
    assert skel.head() == skel.base_commit
    assert f"-Release {V} -Resume" not in first.stdout_text
    n = len(skel.calls())
    second = skel.run_build("-Release", V, "-Resume", "-SkipManual")
    assert second.returncode == 1 and "NoState" in second.stdout_text
    assert skel.calls_since(n) == []


def test_new_gate_pass_invalidates_previous_attempt_state(tmp_path: Path) -> None:
    """不变量：进入第 5 步前作废同版本号的旧状态文件——门禁失败时不会留着上一次尝试的凭据。"""
    skel = build_skeleton(tmp_path)
    skel.state_path.parent.mkdir(parents=True, exist_ok=True)
    skel.state_path.write_text('{"stale": true}\n', encoding="utf-8")
    proc = skel.run_build("-Release", V, "-SkipManual", env_extra={"CHECK_STUB_EXIT": "1"})
    assert proc.returncode == 1
    assert not skel.state_path.exists()


# ---------------------------------------------------------------------------
# 3. 静态
# ---------------------------------------------------------------------------


def _build_text() -> str:
    return BUILD_PS1.read_text(encoding="utf-8-sig")


def test_build_ps1_declares_resume_and_documents_decisions() -> None:
    text = _build_text()
    assert re.search(r"\[switch\]\$Resume\b", text)
    synopsis = text.split("#>", 1)[0]
    assert ".PARAMETER Resume" in synopsis
    assert "-Resume" in synopsis and "reset --soft" in synopsis
    for needle in ("续跑前置校验", "第 5 步", "不在续跑范围"):
        assert needle in synopsis or needle in text, needle


def test_release_final_stages_run_in_the_documented_order() -> None:
    text = _build_text()
    body = text.split("function Invoke-ReleaseFinalStages", 1)[1].split("\n}\n", 1)[0]
    order = [
        "Invoke-ReleaseTagStage",
        "Invoke-ReleaseRegistryStage",
        "Invoke-ReleasePushStage",
        "Invoke-ReleaseGitHubStage",
        "prune_dist.ps1",
    ]
    positions = [body.index(name) for name in order]
    assert positions == sorted(positions), f"阶段顺序被改动：{dict(zip(order, positions))}"


def test_build_ps1_marks_stages_in_flow_order() -> None:
    text = _build_text()
    marks = [
        text.index("New-ReleaseState"),
        text.index("Set-ReleaseCommitRecorded"),
        text.index('-Stage "packaging" -Detail'),
        text.index('-Stage "selfCheck" -Detail'),
        text.index("Invoke-ReleaseFinalStages\n        } elseif"),
    ]
    assert marks == sorted(marks), "状态标记的位置应与流程顺序一致：门禁 -> 提交 -> 打包 -> 自检 -> 后续阶段"


def test_failure_messages_recommend_resume_and_keep_reset_only_as_abandon_path() -> None:
    text = _build_text()
    self_check_msg = text.split("打包完成自检失败", 1)[1].split("exit 1", 1)[0]
    assert "Get-ReleaseResumeCommandText" in self_check_msg
    assert "仅当要放弃本次发布时才回退" in self_check_msg
    lib = LIB_PATH.read_text(encoding="utf-8-sig")
    assert "放弃本次发布：git reset --soft" in lib


def test_toolchain_readme_records_the_resume_decisions() -> None:
    readme = (REPO_ROOT / "toolchain" / "README.md").read_text(encoding="utf-8")
    assert "-Resume" in readme and "release-<ver>.state.json" in readme
    assert "不在续跑范围" in readme


# --- 发布候选阶段（ADR-0160）端到端 ----------------------------------------------


def test_red_samples_gate_stops_before_tag_and_resume_publishes_a_fresh_rc(tmp_path: Path) -> None:
    """样板门禁红 -> 在打标签前终止：没有标签、没有打包产物、没有正式版发布，发布提交与状态文件保留，失败提示推荐 -Resume；
    样板修好后 -Resume：不重跑框架门禁、不重新提交，发布新的 rc.2（不覆盖 rc.1），样板门禁绿后继续打包/打标签/发布。"""
    skel = build_skeleton(tmp_path)
    first = skel.run_build("-Release", V, "-SkipManual", "-PublishRegistry", env_extra={"SAMPLES_STUB_EXIT": "1"})
    assert first.returncode != 0, first.stdout_text
    assert skel.git("tag", "-l") == "", "候选红了不得打标签"
    state = skel.read_state()
    assert state["stages"]["gate"]["done"] and state["stages"]["commit"]["done"]
    assert not state["stages"]["candidate"]["done"] and not state["stages"]["packaging"]["done"] and not state["stages"]["tag"]["done"]
    labels1 = skel.call_labels()
    assert "samples-check.ps1" in labels1
    assert not (skel.root / "dist" / V).exists(), "候选红了不得走到正式打包"
    store1 = set(json.loads(skel.npm_store_path.read_text(encoding="utf-8")))
    assert store1 == {f"{p}@{V}-rc.1" for p in PACKAGE_NAMES}, "私服里只有候选，没有正式版"
    assert f"build.ps1 -Release {V} -Resume" in first.stdout_text
    release_commit = skel.head()
    commits_before = skel.git("rev-list", "--count", "HEAD")

    n = len(skel.calls())
    second = skel.run_build("-Release", V, "-Resume", "-SkipManual", "-PublishRegistry")
    assert second.returncode == 0, f"{second.stdout_text}\n{second.stderr_text}"
    labels2 = skel.call_labels(skel.calls_since(n))
    assert "check.ps1" not in labels2 and skel.head() == release_commit and skel.git("rev-list", "--count", "HEAD") == commits_before
    assert f"samples-upgrade {V}-rc.2" in labels2 and "samples-check.ps1" in labels2
    keys = set(json.loads(skel.npm_store_path.read_text(encoding="utf-8")))
    assert {f"{p}@{V}-rc.1" for p in PACKAGE_NAMES} <= keys and {f"{p}@{V}-rc.2" for p in PACKAGE_NAMES} <= keys
    assert {f"{p}@{V}" for p in PACKAGE_NAMES} <= keys
    assert skel.git("rev-parse", f"refs/tags/{skel.tag}^{{commit}}") == release_commit
    assert skel.stage_done("candidate") and "rc.2" in skel.read_state()["stages"]["candidate"]["detail"]


def test_release_refuses_without_a_samples_repo_and_opt_out_is_recorded(tmp_path: Path) -> None:
    skel = build_skeleton(tmp_path)
    missing = tmp_path / "no_samples_here"
    refused = skel.run_build("-Release", V, "-SkipManual", "-SamplesRepo", str(missing))
    assert refused.returncode != 0
    assert not skel.stage_done("candidate") and skel.git("tag", "-l") == ""
    assert not any(l.startswith("npm publish") for l in skel.call_labels()), "拒绝时不得向私服发任何东西"

    n = len(skel.calls())
    opted = skel.run_build("-Release", V, "-Resume", "-SkipManual", "-SkipSamplesCandidate")
    assert opted.returncode == 0, f"{opted.stdout_text}\n{opted.stderr_text}"
    detail = skel.read_state()["stages"]["candidate"]["detail"]
    assert "OPT-OUT" in detail
    assert not any(l.startswith("samples-") for l in skel.call_labels(skel.calls_since(n)))
    assert skel.git("rev-parse", f"refs/tags/{skel.tag}^{{commit}}") == skel.head()


def test_samples_flags_require_release(tmp_path: Path) -> None:
    skel = build_skeleton(tmp_path)
    for flag in (["-SkipSamplesCandidate"], ["-SamplesRepo", str(tmp_path)]):
        proc = skel.run_build("-Dist", "1.2.1-dryrun", *flag)
        assert proc.returncode == 1
        assert "-SamplesRepo/-SkipSamplesCandidate" in proc.stdout_text
