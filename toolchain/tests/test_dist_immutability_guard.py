"""``toolchain/_dist_immutability_guard.ps1`` 与 ``build.ps1`` 三条打包入口
（``-Release``/``-Dist``/``-Zip``）的"已发布版本不可覆盖"强制校验回归测试
（2026-09-20，消费方反馈第 8 条根治）。

背景（见 ``toolchain/_dist_immutability_guard.ps1`` 文件头判断记录）：仓库对外承诺"标签 +
dist zip + lock 不可变发布"，但此前 ``build.ps1``"打分发包 dist/<版本>/"一节、"打 zip + lock"
一节、``toolchain/_lock_writeback.ps1`` 的 ``Write-WsGameLockFile`` 三处均无版本存在性校验，
会无条件覆盖同名产物；唯一的防线（``-Release`` 第 7 步的 ``git tag -a``）在产物已被覆盖之后
才执行，且 ``-Dist``/``-Zip`` 两条独立路径完全不经过这道防线。复现：发布过 vX 后单独执行
``build.ps1 -Dist X -Zip``，三个产物被静默覆盖。

本文件覆盖：

1. ``Assert-DistVersionNotAlreadyReleased``（独立函数，dot-source 后直接调用，不跑完整
   ``build.ps1``）：
   a. 版本未发布（无对应 ``v<版本>`` 标签）时放行，不抛异常。
   b. 版本已发布（存在对应标签）时拒绝，异常信息包含关键指引——版本号、已发布证据
      （标签名）、拒绝原因（不可变发布承诺）、正确做法（发布新版本号）、例外开关名
      （``-AllowOverwriteDist``）。
   c. 显式传 ``-AllowOverwrite`` 时跳过校验放行，但会打印醒目警告点出版本号与即将覆盖的
      产物描述。
   d. 带 ``-dryrun`` 后缀的版本字符串（``-Release -DryRun``/``-Dist X.Y.Z-dryrun`` 两条
      dry-run 路径实际使用的打包版本字符串）即使"干净"版本号已发布，也不会被误判——因为
      校验对象是完整的带后缀字符串，真实发布从不会给带后缀的字符串打标签。

2. ``build.ps1`` 真实入口的接线（不 mock，真实调用脚本，但用 ``-SyncContent`` 跳过耗时的
   ``dotnet build``/``test``/DLL 同步——校验点在这些步骤之后、真正写 dist 之前，用
   ``-SyncContent`` 能让测试在几秒内跑到校验点，不需要等一次完整构建）：
   a. ``-Dist <已发布版本>`` 被拦，且不产生 ``dist/<版本>/`` 目录。
   b. ``-Dist <已发布版本> -Zip`` 同样被拦（在到达 zip/lock 那一步之前，已经被"打分发包"
      一步的同一道校验先挡住——见静态结构测试 3 的说明，两处校验都存在，不依赖谁先拦）。
   已发布版本号动态从仓库真实标签里取（``git tag -l "v*"``，取第一个满足 ``vX.Y.Z`` 严格格式
   的），不硬编码具体版本号，也不新建任何标签——完全只读，不污染仓库。本机没有任何符合格式的
   标签时跳过这两个用例。

3. 静态结构校验（不依赖 PowerShell 解释器，纯文本断言，验证 ``-Release`` 入口的覆盖面——
   本文件不能真的跑 ``build.ps1 -Release``，见 AGENTS.md §5"发布脚本只由主会话前台执行"）：
   ``-Release`` 内部把自己转译成一次 ``-Dist`` 请求（无条件 ``$DistRequested = $true``），
   复用与 ``-Dist``/``-Zip`` 完全相同的两处共享代码块，因此断言"三处实际写产物的语句
   （``Remove-Item -Path $DistRoot``、两次 ``Compress-Archive``、``Write-WsGameLockFile``）
   在全文件里各只出现一次，且都排在对应的校验调用之后"，就足以证明 ``-Release`` 不存在另一条
   绕开校验的独立写入路径——不是"信任代码注释"，是对实际会执行的语句做唯一性 + 顺序断言。

跨平台说明：第 1、2 类依赖 ``powershell``（Windows PowerShell 5.1）或 ``pwsh`` 可执行，本机
没有时 skip，不 fail（与仓库其余 ``.ps1`` 相关测试一致约定）；第 3 类是纯文本静态检查，不
依赖 PowerShell 解释器，任何平台都跑。

运行：``python -m pytest toolchain/tests/test_dist_immutability_guard.py -q`` 或
``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import re
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD_SCRIPT = REPO_ROOT / "toolchain" / "_dist_immutability_guard.ps1"
BUILD_SCRIPT = REPO_ROOT / "build.ps1"

_RELEASED_TAG_VERSION_PATTERN = re.compile(r"^v(\d+\.\d+\.\d+)$")


def _find_powershell() -> str | None:
    for exe in ("powershell", "pwsh"):
        found = shutil.which(exe)
        if found:
            return exe
    return None


@pytest.fixture()
def ps_exe() -> str:
    exe = _find_powershell()
    if exe is None:
        pytest.skip("本机未找到 powershell/pwsh 可执行文件，跳过（与仓库其余 .ps1 相关测试一致约定）")
    return exe


def _init_git_repo(root: Path, tag_versions: list[str] | None = None) -> None:
    """在 root 下建一个最小 git 仓库（一个空提交），可选打若干 v<版本> 标签。与 ws-game 仓库
    本身的内容完全无关——`Assert-DistVersionNotAlreadyReleased` 只依赖 `git -C <root> tag -l`
    能不能查到指定标签，不读取仓库里的任何文件内容，用一个空仓库即可完整覆盖其逻辑，天然不会
    与真实仓库的标签互相干扰。
    """
    root.mkdir(parents=True, exist_ok=True)
    subprocess.run(["git", "init", "-q"], cwd=root, check=True)
    subprocess.run(
        ["git", "-c", "user.email=test@example.com", "-c", "user.name=test", "commit", "-q", "--allow-empty", "-m", "init"],
        cwd=root,
        check=True,
    )
    for v in tag_versions or []:
        subprocess.run(["git", "tag", f"v{v}"], cwd=root, check=True)


def _run_guard(
    ps_exe: str,
    repo_root: Path,
    version_for_path: str,
    artifact_descriptions: list[str],
    allow_overwrite: bool = False,
) -> subprocess.CompletedProcess[str]:
    """dot-source 独立的守卫脚本并直接调用 `Assert-DistVersionNotAlreadyReleased`，不跑
    `build.ps1` 本身。用法与 `toolchain/tests/test_unity_path_length_guard.py` 的 `_run_guard`
    同一模式：显式把 Console 输出编码设成 UTF8、把调用包一层 try/catch，异常信息与警告一律走
    stdout，Python 侧固定 `encoding="utf-8"` 解码，不依赖宿主机系统代码页。
    """
    descriptions_literal = ", ".join(f'"{d}"' for d in artifact_descriptions)
    allow_flag = "-AllowOverwrite" if allow_overwrite else ""
    script = (
        '$ErrorActionPreference = "Stop"; '
        '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; '
        f'. "{GUARD_SCRIPT}"; '
        'try { '
        f'Assert-DistVersionNotAlreadyReleased -RepoRoot "{repo_root}" -VersionForPath "{version_for_path}" '
        f'-ArtifactDescriptions @({descriptions_literal}) {allow_flag}; '
        'Write-Output "GUARD_PASSED" '
        '} catch { '
        'Write-Output "GUARD_THROWN:"; '
        'Write-Output $_.Exception.Message; '
        'exit 1 '
        '}'
    )
    return subprocess.run(
        [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        cwd=repo_root,
        capture_output=True,
        text=True,
        encoding="utf-8",
        env=clean_powershell_env(ps_exe),
    )


# ---------------------------------------------------------------------------
# 1. 独立函数逻辑测试（合成的空 git 仓库，不涉及 ws-game 仓库本身的任何标签）
# ---------------------------------------------------------------------------


def test_not_released_version_passes(tmp_path: Path, ps_exe: str) -> None:
    repo = tmp_path / "repo"
    _init_git_repo(repo, tag_versions=["1.0.0"])
    result = _run_guard(ps_exe, repo, "9.9.9", ["dist\\9.9.9\\ 目录"])
    assert result.returncode == 0, f"未发布版本应放行，实际输出：{result.stdout}\n{result.stderr}"
    assert "GUARD_PASSED" in result.stdout


def test_released_version_blocked_with_guidance(tmp_path: Path, ps_exe: str) -> None:
    repo = tmp_path / "repo"
    _init_git_repo(repo, tag_versions=["1.2.3"])
    result = _run_guard(ps_exe, repo, "1.2.3", ["dist\\1.2.3\\ 目录（打包内容）"])
    assert result.returncode != 0, f"已发布版本应被拦截，实际输出：{result.stdout}\n{result.stderr}"
    assert "GUARD_THROWN:" in result.stdout
    message = result.stdout
    # 关键指引缺一不可：版本号、已发布证据（标签）、拒绝原因（不可变承诺）、正确做法（新版本号）、
    # 例外开关名。
    assert "1.2.3" in message
    assert "v1.2.3" in message and "已存在" in message
    assert "不可变" in message
    assert "新的版本号" in message or "新版本号" in message
    assert "-AllowOverwriteDist" in message
    assert "dist\\1.2.3\\ 目录" in message


def test_allow_overwrite_bypasses_with_warning(tmp_path: Path, ps_exe: str) -> None:
    repo = tmp_path / "repo"
    _init_git_repo(repo, tag_versions=["1.2.3"])
    result = _run_guard(
        ps_exe, repo, "1.2.3", ["dist\\1.2.3\\ 目录（打包内容）"], allow_overwrite=True
    )
    assert result.returncode == 0, f"-AllowOverwrite 应放行，实际输出：{result.stdout}\n{result.stderr}"
    assert "GUARD_PASSED" in result.stdout
    assert "警告" in result.stdout
    assert "1.2.3" in result.stdout
    assert "dist\\1.2.3\\ 目录" in result.stdout


def test_dryrun_suffixed_version_not_confused_with_released_tag(tmp_path: Path, ps_exe: str) -> None:
    """`-Release X.Y.Z -DryRun`/`-Dist X.Y.Z-dryrun` 两条 dry-run 路径实际写盘用的版本字符串
    带 `-dryrun` 后缀；即便"干净"版本号 1.2.3 已经真实发布过（标签 v1.2.3 存在），校验对象是
    完整带后缀的字符串 "1.2.3-dryrun"，标签 "v1.2.3-dryrun" 不存在，应当放行——见守卫脚本头
    判断记录"校验对象统一用...VersionForPath"一节。
    """
    repo = tmp_path / "repo"
    _init_git_repo(repo, tag_versions=["1.2.3"])
    result = _run_guard(ps_exe, repo, "1.2.3-dryrun", ["dist\\1.2.3-dryrun\\ 目录"])
    assert result.returncode == 0, f"dry-run 后缀版本不应被已发布的干净版本号误伤，实际输出：{result.stdout}\n{result.stderr}"
    assert "GUARD_PASSED" in result.stdout


# ---------------------------------------------------------------------------
# 2. build.ps1 真实入口接线测试（真实调用脚本，用 -SyncContent 跳过耗时的 dotnet 步骤；
#    版本号动态取自仓库真实标签，不新建标签、不污染仓库）
# ---------------------------------------------------------------------------


def _pick_existing_released_version() -> str | None:
    result = subprocess.run(
        ["git", "-C", str(REPO_ROOT), "tag", "-l", "v*"],
        capture_output=True,
        text=True,
        check=True,
    )
    for line in result.stdout.splitlines():
        m = _RELEASED_TAG_VERSION_PATTERN.match(line.strip())
        if m:
            return m.group(1)
    return None


@pytest.fixture()
def released_version() -> str:
    version = _pick_existing_released_version()
    if version is None:
        pytest.skip("仓库当前没有任何符合 vX.Y.Z 格式的标签，跳过（不新建标签，避免污染仓库）")
    return version


def _run_build_dist(ps_exe: str, extra_args: list[str], version: str) -> subprocess.CompletedProcess[str]:
    dist_dir = REPO_ROOT / "dist" / version
    assert not dist_dir.exists(), (
        f"{dist_dir} 在测试开始前不应存在（dist/ 已 .gitignore，正常工作树不应残留），"
        "否则无法区分'守卫拦住了'还是'目录本来就在'"
    )
    args = [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(BUILD_SCRIPT),
             "-Dist", version, "-SyncContent"] + extra_args
    try:
        result = subprocess.run(
            args,
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=clean_powershell_env(ps_exe),
            timeout=300,
        )
    finally:
        # 防御性清理：即便断言失败或守卫本身有缺陷导致真的写出了目录，也不留痕在工作树里
        # （任务要求"不要真的写 dist/"）。
        if dist_dir.exists():
            shutil.rmtree(dist_dir, ignore_errors=True)
    return result


def test_build_dist_entry_blocked_for_already_released_version(ps_exe: str, released_version: str) -> None:
    result = _run_build_dist(ps_exe, [], released_version)
    assert result.returncode != 0, (
        f"-Dist {released_version}（已发布版本）应被拦截，实际输出：\n{result.stdout}\n{result.stderr}"
    )
    combined = result.stdout + result.stderr
    assert released_version in combined
    assert f"v{released_version}" in combined
    assert "不可变" in combined
    assert "-AllowOverwriteDist" in combined


def test_build_zip_entry_blocked_for_already_released_version(ps_exe: str, released_version: str) -> None:
    result = _run_build_dist(ps_exe, ["-Zip"], released_version)
    assert result.returncode != 0, (
        f"-Dist {released_version} -Zip（已发布版本）应被拦截，实际输出：\n{result.stdout}\n{result.stderr}"
    )
    combined = result.stdout + result.stderr
    assert released_version in combined
    assert f"v{released_version}" in combined
    assert "不可变" in combined
    assert "-AllowOverwriteDist" in combined
    zip_path = REPO_ROOT / "dist" / f"ws-game-{released_version}.zip"
    assert not zip_path.exists(), "被拦截时不应产生 zip 产物"
    if zip_path.exists():
        zip_path.unlink()


# ---------------------------------------------------------------------------
# 3. 静态结构校验（纯文本，不依赖 PowerShell 解释器；证明 -Release 复用同一份被校验的代码块，
#    不存在另一条绕开校验的独立写入路径）
# ---------------------------------------------------------------------------


def _read_build_script_lines() -> list[str]:
    return BUILD_SCRIPT.read_text(encoding="utf-8-sig").splitlines()


def _first_line_index(lines: list[str], needle: str) -> int:
    for i, line in enumerate(lines):
        if needle in line:
            return i
    raise AssertionError(f"未在 build.ps1 中找到：{needle}")


def _all_line_indices(lines: list[str], needle: str) -> list[int]:
    return [i for i, line in enumerate(lines) if needle in line]


def test_allow_overwrite_dist_param_declared() -> None:
    lines = _read_build_script_lines()
    assert any("[switch]$AllowOverwriteDist" in line for line in lines), (
        "build.ps1 应声明 -AllowOverwriteDist 开关参数（发布不可变校验的例外通道）"
    )


def test_dist_dir_write_guarded_exactly_once() -> None:
    lines = _read_build_script_lines()
    guard_calls = _all_line_indices(lines, "Assert-DistVersionNotAlreadyReleased")
    remove_dist_root = _all_line_indices(lines, "Remove-Item -Path $DistRoot -Recurse -Force -Confirm:$false")
    assert len(remove_dist_root) == 1, (
        "dist/<版本>/ 目录只应有一处覆盖写入语句（Remove-Item -Path $DistRoot ...），"
        f"实际找到 {len(remove_dist_root)} 处——如果 -Release 另有一条独立写入路径，"
        "本次新增的校验就可能被绕过"
    )
    assert len(guard_calls) >= 2, "预期 dist 目录、zip+lock 两处各有一次校验调用"
    assert any(g < remove_dist_root[0] for g in guard_calls), (
        "Assert-DistVersionNotAlreadyReleased 必须排在 Remove-Item -Path $DistRoot 之前，"
        "否则会先删后报错"
    )


def test_zip_and_lock_write_guarded_exactly_once() -> None:
    lines = _read_build_script_lines()
    guard_calls = _all_line_indices(lines, "Assert-DistVersionNotAlreadyReleased")
    main_zip = _all_line_indices(lines, "Compress-Archive -Path $zipStagingDir -DestinationPath $zipPath")
    samples_zip = _all_line_indices(lines, "Compress-Archive -Path $samplesStagingDir -DestinationPath $samplesZipPath")
    lock_write = _all_line_indices(lines, "Write-WsGameLockFile -Path $lockPath")
    assert len(main_zip) == 1, f"dist/ws-game-<版本>.zip 主 zip 只应有一处写入语句，实际 {len(main_zip)} 处"
    assert len(samples_zip) == 1, f"samples zip 只应有一处写入语句，实际 {len(samples_zip)} 处"
    assert len(lock_write) == 1, f".lock 只应有一处写入语句，实际 {len(lock_write)} 处"
    assert len(guard_calls) == 2, f"预期 dist 目录、zip+lock 两处各恰好一次校验调用，实际 {len(guard_calls)} 处"
    zip_lock_guard = guard_calls[1]
    assert zip_lock_guard < main_zip[0] < samples_zip[0], (
        "第二处 Assert-DistVersionNotAlreadyReleased 必须排在主 zip、samples zip 写入之前"
    )
    assert zip_lock_guard < lock_write[0], (
        "第二处 Assert-DistVersionNotAlreadyReleased 必须排在 .lock 写入之前"
    )


def test_release_entry_reuses_shared_guarded_dist_requested_flag() -> None:
    """`-Release` 不应该有另一条独立于 `-Dist`/`-Zip` 的 dist/zip/lock 写入路径——它通过在自己
    的前置校验一节里无条件把 `$DistRequested` 置为 `$true`，转译成一次等价的 `-Dist` 请求，
    落到与 `-Dist`/`-Zip` 完全相同、且已经被上面两个测试确认"只有一处、且排在校验之后"的共享
    代码块。结合 `test_dist_dir_write_guarded_exactly_once`/
    `test_zip_and_lock_write_guarded_exactly_once`（证明真正的写入语句全仓库只各出现一次），
    这足以证明 -Release 无法绕开校验——不是"信任代码注释里写了这句话"，是对唯一写入点在
    `-Release` 分支里同样必经的事实做断言。
    """
    lines = _read_build_script_lines()
    release_requested_idx = _first_line_index(lines, '$ReleaseRequested = ($Release -ne "")')
    dist_requested_true_idx = _first_line_index(lines, "$DistRequested = $true")
    assert release_requested_idx < dist_requested_true_idx, (
        "$DistRequested = $true 应出现在 -Release 前置校验一节里（在 $ReleaseRequested 解析之后）"
    )
    # 确认这一行紧跟在 -Release 版本号/工作树/CHANGELOG 三步校验之后、且在"打分发包"共享代码块
    # （由 test_dist_dir_write_guarded_exactly_once 断言的 Remove-Item $DistRoot 所在位置）之前，
    # 即 -Release 走的正是同一段共享逻辑，不是自己另起一段。
    remove_dist_root_idx = _first_line_index(lines, "Remove-Item -Path $DistRoot -Recurse -Force -Confirm:$false")
    assert dist_requested_true_idx < remove_dist_root_idx


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
