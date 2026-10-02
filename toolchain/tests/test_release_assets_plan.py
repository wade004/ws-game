"""release.yml"Check for existing release assets"步骤的附件处置判定回归（2026-10-02）。

缺陷：v1.85.0～v1.91.0 的 Release 运行红灯。GitHub 上当时没有对应 Release，工作流走"全部附件缺失 ->
托管运行器全量重建"路径，调用 ``build.ps1 -SyncOnly -Dist <ver> -Zip``；tag 触发的运行检出的就是标签提交
本身，``build.ps1`` 的发布不可变守卫（``_dist_immutability_guard.ps1``）看到标签已存在而拒绝写产物，运行在
最后一步才红灯，日志只有"拒绝覆盖已发布版本"。

处置（见 ``toolchain/_release_assets_plan.ps1`` 头判断记录）：zip 缺失一律在附件核对步骤**提前明确失败**并
打印人工指引，工作流不再有任何构建步骤；守卫一行未改。

用例：
- 复现：``test_all_assets_missing_blocks_with_manual_guidance``——"Release 上一个附件都没有"这一原缺陷输入，
  判定为 Blocked/NoVerifiedAssets（旧行为是进入重建），指引文案含补传命令与 workflow_dispatch 重跑。
- 不变量：``test_plan_invariants_hold_for_every_subset_of_existing_assets``——对九件应有附件的全部 512 种
  "已有子集"逐一判定：zip 缺失 -> 一律 Blocked（不存在任何通往重建/上传的结论）；zip 在 -> 只可能 Skip
  （无缺）或 Repair（有缺）；Blocked 的 Reason 与"是否仍有批次敏感附件"一致；文案纯 ASCII。
- 工作流本体：``test_workflow_step_*``——把 ``Check for existing release assets`` 步骤的 ``run:`` 正文原样抽出，
  用桩 ``gh`` 在本机模拟 tag 触发的运行（Release 不存在 / 部分存在 / 齐全 / zip 提交号不符），断言退出码、
  ``GITHUB_OUTPUT`` 与指引文案——这就是"本机模拟 tag 触发的全量重建路径"的做法。
- 静态守卫：``test_release_yml_has_no_build_steps``——工作流代码行不再含 ``build.ps1``/``-SyncOnly``/
  ``zip_missing``，所有非注释行纯 ASCII。

Windows-only（PowerShell 子进程）；无宿主时 skip（沿用 ``_ps_harness`` 约定）。
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import zipfile
from pathlib import Path

import pytest

from _git_env import clean_git_env
from _ps_harness import REPO_ROOT, TOOLCHAIN_DIR, find_powershell, ps_quote, run_ps_json, run_ps_script

PLAN_SCRIPT = TOOLCHAIN_DIR / "_release_assets_plan.ps1"
RELEASE_YML = REPO_ROOT / ".github" / "workflows" / "release.yml"
STEP_NAME = "Check for existing release assets"

VERSION = "9.9.9"
TAG = f"v{VERSION}"
ZIP = f"ws-game-{VERSION}.zip"
LOCK = f"ws-game-{VERSION}.lock"
SAMPLES = f"ws-game-{VERSION}-samples.zip"
TGZS = [
    f"com.gamefoundation.adapter.unity-{VERSION}.tgz",
    f"com.gamefoundation.framework-data-{VERSION}.tgz",
    f"com.gamefoundation.toolchain-{VERSION}.tgz",
    f"com.gamefoundation.adapter.headless-{VERSION}.tgz",
]
PLAIN = ["get_framework.ps1", "_hash.ps1"]
REQUIRED = [ZIP, LOCK, SAMPLES, *TGZS, *PLAIN]


# --------------------------------------------------------------------------- 判定函数

def _plan_all_subsets(tmp_path: Path) -> list[dict]:
    required = ",".join(ps_quote(n) for n in REQUIRED)
    body = (
        f". {ps_quote(PLAN_SCRIPT)}\n"
        f"$required = @({required})\n"
        "$out = @()\n"
        "for ($mask = 0; $mask -lt (1 -shl $required.Count); $mask++) {\n"
        "  $existing = @()\n"
        "  for ($i = 0; $i -lt $required.Count; $i++) { if ($mask -band (1 -shl $i)) { $existing += $required[$i] } }\n"
        f"  $p = Get-WsGameReleaseAssetsPlan -Tag {ps_quote(TAG)} -Version {ps_quote(VERSION)} "
        "-ExistingNames $existing -RequiredNames $required\n"
        "  $out += [pscustomobject]@{ mask = $mask; action = $p.Action; reason = $p.Reason; "
        "missing = @($p.MissingNames); message = @($p.Message) }\n"
        "}\n"
        "ConvertTo-Json -InputObject @($out) -Depth 5 | Set-Content -LiteralPath $ResultPath -Encoding UTF8\n"
    )
    return run_ps_json(tmp_path, body, name="plan_subsets", timeout=300)


def test_all_assets_missing_blocks_with_manual_guidance(tmp_path: Path) -> None:
    # 原缺陷输入：Release 上一个附件都没有（gh release view 失败 / Release 尚不存在）。
    body = (
        f". {ps_quote(PLAN_SCRIPT)}\n"
        f"$required = @({','.join(ps_quote(n) for n in REQUIRED)})\n"
        f"$p = Get-WsGameReleaseAssetsPlan -Tag {ps_quote(TAG)} -Version {ps_quote(VERSION)} "
        "-ExistingNames @() -RequiredNames $required\n"
        "[pscustomobject]@{ action = $p.Action; reason = $p.Reason; missing = @($p.MissingNames); "
        "message = (@($p.Message) -join \"`n\") } | ConvertTo-Json -Depth 4 | "
        "Set-Content -LiteralPath $ResultPath -Encoding UTF8\n"
    )
    plan = run_ps_json(tmp_path, body, name="plan_empty")
    assert plan["action"] == "Blocked"  # 旧行为：进入全量重建，再被发布不可变守卫拒绝
    assert plan["reason"] == "NoVerifiedAssets"
    assert sorted(plan["missing"]) == sorted(REQUIRED)
    msg = plan["message"]
    assert f"gh release create {TAG}" in msg, "指引必须告诉人怎么补传（本机 build.ps1 -Release 打印的命令）"
    assert "workflow_dispatch" in msg and f"tag={TAG}" in msg, "指引必须说明补传后如何重跑验证"
    assert "does not rebuild" in msg


def test_plan_invariants_hold_for_every_subset_of_existing_assets(tmp_path: Path) -> None:
    results = _plan_all_subsets(tmp_path)
    assert len(results) == 1 << len(REQUIRED)
    batch_sensitive = [n for n in REQUIRED if n not in PLAIN]
    actions = {"Skip": 0, "Repair": 0, "Blocked": 0}
    for r in results:
        existing = [n for i, n in enumerate(REQUIRED) if r["mask"] & (1 << i)]
        missing = [n for n in REQUIRED if n not in existing]
        assert sorted(r["missing"]) == sorted(missing)
        actions[r["action"]] += 1
        if ZIP in existing:
            # zip 在：只可能"无缺 -> Skip"或"有缺 -> Repair（从已验证 zip 抽取）"，绝不阻断、绝不重建。
            assert r["action"] == ("Skip" if not missing else "Repair"), (existing, r)
        else:
            # zip 缺：一律阻断；没有任何通往"重建并上传"的结论。
            assert r["action"] == "Blocked", (existing, r)
            others = [n for n in existing if n in batch_sensitive]
            assert r["reason"] == ("MixedBatch" if others else "NoVerifiedAssets"), (existing, r)
        assert r["message"], "每个结论都必须带说明文案"
        assert all(line.isascii() for line in r["message"]), "文案会被 powershell 5.1 的 run: 步骤打印，必须纯 ASCII"
    assert actions == {"Skip": 1, "Repair": (1 << (len(REQUIRED) - 1)) - 1, "Blocked": 1 << (len(REQUIRED) - 1)}


# --------------------------------------------------------------------------- 工作流步骤本体

def _extract_step_run_body(step_name: str) -> str:
    lines = RELEASE_YML.read_text(encoding="utf-8").split("\n")
    start = next(i for i, ln in enumerate(lines) if ln.strip() == f"- name: {step_name}")
    run_idx = next(i for i in range(start + 1, len(lines)) if lines[i].strip() == "run: |")
    indent = len(lines[run_idx + 1]) - len(lines[run_idx + 1].lstrip())
    body: list[str] = []
    for ln in lines[run_idx + 1:]:
        if ln.strip() == "":
            body.append("")
            continue
        if len(ln) - len(ln.lstrip()) < indent:
            break
        body.append(ln[indent:])
    return "\n".join(body)


def _head_sha() -> str:
    out = subprocess.run(
        ["git", "-C", str(REPO_ROOT), "rev-parse", "HEAD"],
        capture_output=True, text=True, env=clean_git_env(dict(os.environ)),
    )
    if out.returncode != 0 or not re.fullmatch(r"[0-9a-f]{40}", out.stdout.strip()):
        pytest.skip("需要 git 仓库的 HEAD 提交")
    return out.stdout.strip()


_GH_STUB = r"""
function gh {
  $global:LASTEXITCODE = 0
  if ($args[0] -eq 'release' -and $args[1] -eq 'view') {
    if ($env:SIM_VIEW_JSON) { return $env:SIM_VIEW_JSON }
    $global:LASTEXITCODE = 1
    return
  }
  if ($args[0] -eq 'release' -and $args[1] -eq 'download') {
    Copy-Item -LiteralPath (Join-Path $env:SIM_ASSET_DIR $args[4]) -Destination (Join-Path $args[6] $args[4]) -Force
    return
  }
  $global:LASTEXITCODE = 2
}
"""


def _simulate_step(tmp_path: Path, existing: list[str] | None, recorded_commit: str | None = None):
    """在本机模拟 tag 触发运行里的"附件核对"步骤：``existing`` 为 None 表示 Release 不存在（gh release view 失败）。"""
    find_powershell()
    head = _head_sha()
    body = _extract_step_run_body(STEP_NAME)
    assert "${{ steps.version.outputs.tag }}" in body and "${{ steps.version.outputs.version }}" in body
    body = body.replace("${{ steps.version.outputs.tag }}", TAG).replace("${{ steps.version.outputs.version }}", VERSION)
    assert "${{" not in body, "步骤正文还有未替换的表达式"

    asset_dir = tmp_path / "assets"
    asset_dir.mkdir()
    commit = recorded_commit or head[:8]
    with zipfile.ZipFile(asset_dir / ZIP, "w") as zf:
        zf.writestr(f"ws-game-{VERSION}/MANIFEST.txt", f"git_commit: {commit}\n")
    (asset_dir / LOCK).write_text(json.dumps({"git_commit": commit}), encoding="utf-8")

    runner_temp = tmp_path / "runner_temp"
    runner_temp.mkdir()
    github_output = tmp_path / "github_output.txt"
    github_output.write_text("", encoding="utf-8")

    driver = (
        f"Set-Location -LiteralPath {ps_quote(REPO_ROOT)}\n"
        + _GH_STUB
        + body
    )
    env_extra = {
        "GITHUB_OUTPUT": str(github_output),
        "RUNNER_TEMP": str(runner_temp),
        "SIM_ASSET_DIR": str(asset_dir),
        "SIM_VIEW_JSON": "" if existing is None else json.dumps({"assets": [{"name": n} for n in existing]}),
    }
    proc = run_ps_script(tmp_path, driver, env_extra=env_extra, name="sim_step")
    return proc, github_output.read_text(encoding="utf-8")


def test_workflow_step_blocks_when_release_does_not_exist(tmp_path: Path) -> None:
    # tag 触发的运行里 Release 尚不存在：旧行为 = 输出 skip=false/zip_missing=true 后进入重建并被守卫拒绝；
    # 新行为 = 本步骤自己以非零退出并打印指引，且不产出任何"继续"信号。
    proc, out = _simulate_step(tmp_path, existing=None)
    assert proc.returncode == 1, proc.stdout + proc.stderr
    assert "BLOCKED" in proc.stdout and "does not rebuild" in proc.stdout
    assert f"gh release create {TAG}" in proc.stdout
    assert "NoVerifiedAssets" in proc.stdout
    assert "skip=" not in out and "zip_missing" not in out and "missing_files" not in out


def test_workflow_step_blocks_mixed_batch_when_zip_missing_but_lock_present(tmp_path: Path) -> None:
    proc, out = _simulate_step(tmp_path, existing=[LOCK, *TGZS])
    assert proc.returncode == 1, proc.stdout + proc.stderr
    assert "MixedBatch" in proc.stdout
    assert "skip=" not in out


def test_workflow_step_skips_when_all_assets_present(tmp_path: Path) -> None:
    proc, out = _simulate_step(tmp_path, existing=list(REQUIRED))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert "skip=true" in out
    assert "commit verified" in proc.stdout


def test_workflow_step_repairs_from_zip_when_only_some_assets_missing(tmp_path: Path) -> None:
    existing = [ZIP, LOCK, *PLAIN]
    proc, out = _simulate_step(tmp_path, existing=existing)
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert "skip=false" in out
    listed = out.split("missing_files<<GF_EOF", 1)[1].split("GF_EOF", 1)[0].split()
    expected = sorted(
        [f"dist/{SAMPLES}"] + [f"dist/{VERSION}/packages/{n}" for n in TGZS]
    )
    assert sorted(listed) == expected, "上传清单只含缺失附件，且不含已有的 zip/lock"


def test_workflow_step_still_blocks_zip_built_from_another_commit(tmp_path: Path) -> None:
    # 不变量：放宽/改写处置逻辑不能放过"zip 声称是这个标签、实际来自另一个提交"。
    proc, out = _simulate_step(tmp_path, existing=[ZIP, LOCK], recorded_commit="deadbeef")
    assert proc.returncode == 1, proc.stdout + proc.stderr
    assert "was built from commit deadbeef" in proc.stdout
    assert "skip=" not in out


# --------------------------------------------------------------------------- 静态守卫

def _code_lines() -> list[str]:
    return [ln for ln in RELEASE_YML.read_text(encoding="utf-8").split("\n") if not ln.lstrip().startswith("#")]


def test_release_yml_has_no_build_steps() -> None:
    code = "\n".join(_code_lines())
    assert "build.ps1" not in code, "工作流不得在托管运行器上构建发布产物（见 _release_assets_plan.ps1 头判断记录）"
    assert "-SyncOnly" not in code and "zip_missing" not in code
    assert "dotnet tool restore" not in code
    assert "Get-WsGameReleaseAssetsPlan" in code
    assert code.count('. "toolchain\\_release_assets_plan.ps1"') == 1


def test_release_yml_non_comment_lines_are_ascii() -> None:
    # run: 正文由 Windows PowerShell 5.1 按 ANSI 代码页读取，必须纯 ASCII（见 release.yml 文件头判断记录）。
    bad = [ln for ln in _code_lines() if not ln.isascii()]
    assert not bad, bad[:3]


def test_plan_script_has_utf8_bom_because_it_contains_non_ascii_comments() -> None:
    raw = PLAN_SCRIPT.read_bytes()
    if not raw.isascii():
        assert raw.startswith(b"\xef\xbb\xbf")
