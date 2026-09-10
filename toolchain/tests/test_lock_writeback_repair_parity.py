"""``toolchain/_lock_writeback.ps1``（TOOL-118-LOCK 根治，codex 第十八轮，
audit-d6fda65-20260911）回归测试。

背景：``.github/workflows/release.yml``"缺附件修复"分支（zip 已存在、lock 缺失时，从已验证 zip
直接抽取字节重建 lock，不触发新构建）此前手写了一份只覆盖六个核心 DLL 的锁对象构造逻辑，漏掉了
``headless_dlls``/``validator_dlls`` 两个字段——``toolchain/get_framework.ps1`` 遇到缺这两个字段的
锁文件会走"旧版本、跳过校验"的向后兼容分支，导致 ``Adapters.Stub.dll``/``Validator.dll`` 被篡改也
检测不出来（见 AUDIT_REPORT.md TOOL-118-LOCK、validation/release-repair 下的复现记录）。

根治：
  1. 锁对象构造/写出与"从一份已验证 zip 生成完整锁对象"两套逻辑都收进
     ``toolchain/_lock_writeback.ps1`` 一个文件（``New-WsGameLockObject``/``Write-WsGameLockFile``/
     ``New-WsGameLockObjectFromZip``），``build.ps1`` 正常路径与 ``release.yml`` 修复分支现在调用
     同一份实现，不会再各自漏字段。
  2. ``toolchain/get_framework.ps1`` 对锁文件 version >= 1.15.0（``headless_dlls``/
     ``validator_dlls`` 两个字段里更晚引入的那个）时，缺这两个字段直接判定锁文件损坏、拒绝落地，
     不再无条件跳过校验（该兼容跳过只保留给 version < 1.15.0 的真正旧版本锁文件）。

本文件覆盖三个场景：
  1. 用本机真实 ``dist/ws-game-<VERSION>.zip`` 走 ``New-WsGameLockObjectFromZip`` 重新生成一份
     锁文件，逐字段（``version``/``git_commit``/``dlls``/``headless_dlls``/``validator_dlls``/
     ``samples``）与仓库里已发布的 ``dist/ws-game-<VERSION>.lock`` 完全一致（本机没有这两个文件
     时跳过，不算测试失败——与其它 get_framework 测试同款"没有本机产物就 skip"约定）。
  2. 构造一份 fixture zip，篡改其中的 ``Adapters.Stub.dll`` 字节，分别用"正常路径风格"锁
     （``New-WsGameLockObject``，哈希取自篡改前的原始内容）与"修复分支风格"锁
     （``New-WsGameLockObjectFromZip``，同样取自篡改前的原始 zip）针对篡改后的 zip 跑
     ``get_framework.ps1 -FromLocalDist``——两者都必须非 0 退出、不落地任何内容，同等阻断。

运行：``python -m pytest toolchain/tests/test_lock_writeback_repair_parity.py -q`` 或
``python -m pytest toolchain/tests -q``。Windows-only（依赖 Windows PowerShell 执行 ``.ps1``），
非 Windows 环境下全部用例自动跳过（与本目录其它 get_framework 测试同款约定）。
"""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
GET_FRAMEWORK_SCRIPT = REPO_ROOT / "toolchain" / "get_framework.ps1"
HASH_SCRIPT = REPO_ROOT / "toolchain" / "_hash.ps1"
LOCK_WRITEBACK_SCRIPT = REPO_ROOT / "toolchain" / "_lock_writeback.ps1"

DLL_NAMES = [
    "Core.Foundation.dll",
    "Core.Numbers.dll",
    "Core.Rules.dll",
    "Core.Carriers.dll",
    "Core.Gameplay.dll",
    "Presentation.Common.dll",
]
PLUGINS_CORE_REL = (
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Core"
)
HEADLESS_DLL_NAME = "Adapters.Stub.dll"
VALIDATOR_DLL_NAME = "Validator.dll"

pytestmark = pytest.mark.skipif(
    sys.platform != "win32", reason="本文件依赖 Windows PowerShell 执行 .ps1"
)


def _find_powershell_executable() -> str | None:
    for candidate in ("powershell.exe", "powershell", "pwsh.exe", "pwsh"):
        path = shutil.which(candidate)
        if path:
            return path
    return None


POWERSHELL = _find_powershell_executable() if sys.platform == "win32" else None


def _run_powershell(script_body: str, timeout: int = 120) -> subprocess.CompletedProcess:
    if not POWERSHELL:
        pytest.skip("找不到 powershell.exe 或 pwsh")
    return subprocess.run(
        [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script_body],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )


def _run_get_framework(args: list[str], cwd: Path) -> subprocess.CompletedProcess:
    if not POWERSHELL:
        pytest.skip("找不到 powershell.exe 或 pwsh")
    cmd = [
        POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", str(GET_FRAMEWORK_SCRIPT),
    ] + args
    return subprocess.run(
        cmd, cwd=str(cwd), capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=120,
    )


def test_lock_generated_from_real_zip_matches_published_lock(tmp_path: Path) -> None:
    """用本机真实正式发布 zip 通过 New-WsGameLockObjectFromZip 重新生成锁文件，须与仓库里已发布
    的 dist/ws-game-<VERSION>.lock 逐字段一致（任务书 pytest 验收要求原文）。
    """
    version_file = REPO_ROOT / "VERSION"
    if not version_file.is_file():
        pytest.skip("找不到仓库根 VERSION 文件")
    version = version_file.read_text(encoding="utf-8").strip()
    zip_path = REPO_ROOT / "dist" / f"ws-game-{version}.zip"
    real_lock_path = REPO_ROOT / "dist" / f"ws-game-{version}.lock"
    if not zip_path.is_file() or not real_lock_path.is_file():
        pytest.skip(f"本机没有 dist/ws-game-{version}.zip|.lock，跳过真实产物回归")

    real_lock = json.loads(real_lock_path.read_text(encoding="utf-8"))
    samples_sha = None
    if isinstance(real_lock.get("samples"), dict):
        samples_sha = real_lock["samples"].get("sha256")

    generated_path = tmp_path / "regenerated.lock"
    script = (
        f". '{HASH_SCRIPT}'\n"
        f". '{LOCK_WRITEBACK_SCRIPT}'\n"
        f"$lockObj = New-WsGameLockObjectFromZip -ZipPath '{zip_path}' -Version '{version}' -SamplesSha256 '{samples_sha or ''}'\n"
        f"Write-WsGameLockFile -Path '{generated_path}' -LockObject $lockObj\n"
    )
    result = _run_powershell(script, timeout=300)
    assert result.returncode == 0, result.stdout + result.stderr
    assert generated_path.is_file(), "New-WsGameLockObjectFromZip 未生成锁文件"

    generated_lock = json.loads(generated_path.read_text(encoding="utf-8"))
    for field in ("version", "git_commit", "dlls", "headless_dlls", "validator_dlls", "samples"):
        assert generated_lock.get(field) == real_lock.get(field), (
            f"字段 {field!r} 不一致：\n"
            f"  重新生成 = {generated_lock.get(field)!r}\n"
            f"  已发布   = {real_lock.get(field)!r}"
        )


def _build_fixture_zip(zip_path: Path, version: str) -> dict:
    """构造一个最小但结构合法的 ws-game 发布 zip：六个核心 DLL + Adapters.Stub.dll +
    Validator.dll + MANIFEST.txt。返回 {"dlls": {...}, "headless_dlls": {...},
    "validator_dlls": {...}}，供调用方按需构造锁文件/篡改。
    """
    top_dir_name = f"ws-game-{version}"
    dll_hashes: dict[str, str] = {}
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for dll_name in DLL_NAMES:
            content = f"fixture-dll:{dll_name}:{version}".encode("utf-8")
            dll_hashes[dll_name] = hashlib.sha256(content).hexdigest()
            zf.writestr(f"{top_dir_name}/{PLUGINS_CORE_REL}/{dll_name}", content)
        headless_content = f"fixture-dll:{HEADLESS_DLL_NAME}:{version}".encode("utf-8")
        headless_sha = hashlib.sha256(headless_content).hexdigest()
        zf.writestr(f"{top_dir_name}/adapters/headless/{HEADLESS_DLL_NAME}", headless_content)
        validator_content = f"fixture-dll:{VALIDATOR_DLL_NAME}:{version}".encode("utf-8")
        validator_sha = hashlib.sha256(validator_content).hexdigest()
        zf.writestr(f"{top_dir_name}/toolchain/validator/bin/{VALIDATOR_DLL_NAME}", validator_content)
        zf.writestr(f"{top_dir_name}/MANIFEST.txt", f"version={version}\ngit_commit: deadbeef\n")
    return {
        "dlls": dll_hashes,
        "headless_dlls": {HEADLESS_DLL_NAME: headless_sha},
        "validator_dlls": {VALIDATOR_DLL_NAME: validator_sha},
    }


def _tamper_zip_entry(src_zip: Path, dest_zip: Path, entry_suffix: str) -> None:
    """复制一份 zip，把路径以 entry_suffix 结尾的条目内容替换成不同字节（模拟篡改/损坏）。"""
    with zipfile.ZipFile(src_zip, "r") as zin, zipfile.ZipFile(dest_zip, "w", zipfile.ZIP_DEFLATED) as zout:
        found = False
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename.endswith(entry_suffix):
                data = data + b"TAMPERED"
                found = True
            zout.writestr(item, data)
        assert found, f"未在 {src_zip} 内找到要篡改的条目（后缀 {entry_suffix!r}）"


def test_tampered_headless_dll_blocks_both_normal_and_repair_style_locks(tmp_path: Path) -> None:
    """普通路径风格锁（New-WsGameLockObject，哈希来自篡改前的原始内容）与修复分支风格锁
    （New-WsGameLockObjectFromZip，同样取自篡改前的原始 zip）针对同一份篡改后的 zip 必须同等阻断
    ——这是修复前的实际症状（validation/release-repair 复现记录）：普通 lock 正确阻断，
    repair 分支重建的 lock 却因为缺 headless_dlls 字段而跳过校验、成功落地。
    """
    # 判断记录：get_framework.ps1 的 -FromLocalDist 要求锁文件是 zip 的同目录同名兄弟文件
    # （$LockSourcePath = ChangeExtension($ZipPath, ".lock")，见该脚本判断记录），所以"普通路径
    # 风格锁"与"修复分支风格锁"两个场景必须放在各自独立的目录里、用同一个 zip 文件名
    # （"ws-game-<ver>.zip"）+ 各自那份锁文件，不能靠改锁文件名区分。
    version = "7.7.1"
    zip_basename = f"ws-game-{version}.zip"

    staging_dir = tmp_path / "staging"
    staging_dir.mkdir()
    original_zip = staging_dir / zip_basename
    fixture_hashes = _build_fixture_zip(original_zip, version)

    case_normal_dir = tmp_path / "case_normal"
    case_normal_dir.mkdir()
    tampered_zip_normal = case_normal_dir / zip_basename
    _tamper_zip_entry(original_zip, tampered_zip_normal, HEADLESS_DLL_NAME)

    case_repair_dir = tmp_path / "case_repair"
    case_repair_dir.mkdir()
    tampered_zip_repair = case_repair_dir / zip_basename
    _tamper_zip_entry(original_zip, tampered_zip_repair, HEADLESS_DLL_NAME)

    # 篡改后条目哈希应与原始不同（自检：确认 _tamper_zip_entry 真的改变了内容，不是误报）。
    with zipfile.ZipFile(tampered_zip_normal, "r") as zf:
        tampered_content = zf.read(f"ws-game-{version}/adapters/headless/{HEADLESS_DLL_NAME}")
    tampered_sha = hashlib.sha256(tampered_content).hexdigest()
    assert tampered_sha != fixture_hashes["headless_dlls"][HEADLESS_DLL_NAME]

    # 场景 A：普通路径风格锁（模拟 build.ps1 正常输出，哈希来自篡改前的原始内容），放进
    # case_normal 目录，与该目录下篡改后的 zip 同名配对。
    normal_lock_path = case_normal_dir / f"ws-game-{version}.lock"
    normal_lock_obj = {
        "version": version,
        "git_commit": "deadbeef",
        "dlls": fixture_hashes["dlls"],
        "headless_dlls": fixture_hashes["headless_dlls"],
        "validator_dlls": fixture_hashes["validator_dlls"],
    }
    normal_lock_path.write_text(json.dumps(normal_lock_obj, indent=2), encoding="utf-8")

    workspace_normal = tmp_path / "workspace_normal"
    workspace_normal.mkdir()
    result_normal = _run_get_framework(
        ["-Version", version, "-Target", str(workspace_normal / "packages"),
         "-FromLocalDist", str(tampered_zip_normal), "-LockPath", str(workspace_normal / "ws-game.lock")],
        cwd=workspace_normal,
    )
    assert result_normal.returncode != 0, (
        "普通路径风格锁针对篡改后的 Adapters.Stub.dll 未被阻断：\n"
        + result_normal.stdout + result_normal.stderr
    )
    assert "Adapters.Stub.dll" in (result_normal.stdout + result_normal.stderr)

    # 场景 B：修复分支风格锁——用 New-WsGameLockObjectFromZip 针对*原始未篡改* zip 生成（模拟
    # release.yml 从"已验证"的 zip 重建 lock 这一前提），放进 case_repair 目录，与该目录下（另一份
    # 独立篡改出的）zip 同名配对，模拟"lock 发布之后、消费方下载的 zip 内容被替换/损坏"这一威胁
    # 模型。
    repair_lock_path = case_repair_dir / f"ws-game-{version}.lock"
    script = (
        f". '{HASH_SCRIPT}'\n"
        f". '{LOCK_WRITEBACK_SCRIPT}'\n"
        f"$lockObj = New-WsGameLockObjectFromZip -ZipPath '{original_zip}' -Version '{version}'\n"
        f"Write-WsGameLockFile -Path '{repair_lock_path}' -LockObject $lockObj\n"
    )
    gen_result = _run_powershell(script)
    assert gen_result.returncode == 0, gen_result.stdout + gen_result.stderr
    repair_lock_obj = json.loads(repair_lock_path.read_text(encoding="utf-8"))
    # 自检：确认修复分支风格锁确实带上了 headless_dlls/validator_dlls（否则下面的阻断断言
    # 会因为"根本没有这两个字段所以走了兼容跳过"而通过，掩盖真正要测的回归点）。
    assert repair_lock_obj.get("headless_dlls") == fixture_hashes["headless_dlls"]
    assert repair_lock_obj.get("validator_dlls") == fixture_hashes["validator_dlls"]

    workspace_repair = tmp_path / "workspace_repair"
    workspace_repair.mkdir()
    result_repair = _run_get_framework(
        ["-Version", version, "-Target", str(workspace_repair / "packages"),
         "-FromLocalDist", str(tampered_zip_repair), "-LockPath", str(workspace_repair / "ws-game.lock")],
        cwd=workspace_repair,
    )
    assert result_repair.returncode != 0, (
        "修复分支风格锁（New-WsGameLockObjectFromZip 重建）针对篡改后的 Adapters.Stub.dll 未被"
        "阻断——这正是 TOOL-118-LOCK 修复前的症状（headless_dlls 字段缺失导致跳过校验）：\n"
        + result_repair.stdout + result_repair.stderr
    )
    assert "Adapters.Stub.dll" in (result_repair.stdout + result_repair.stderr)

    # 两者行为等价：都不应该落地任何内容。
    assert not (workspace_normal / "packages").exists() or not any((workspace_normal / "packages").iterdir())
    assert not (workspace_repair / "packages").exists() or not any((workspace_repair / "packages").iterdir())


def test_pre_115_lock_missing_headless_dlls_still_skips_not_rejected(tmp_path: Path) -> None:
    """version < 1.15.0 的锁文件缺 headless_dlls/validator_dlls 仍应走既有的"旧版本，跳过校验"
    分支（exit 0），不应被新的"版本 >= 1.15.0 缺字段视为损坏"逻辑误伤——这条兼容路径是任务书明确
    要求保留的（"只允许对 1.15.0 之前的旧版本"）。
    """
    version = "1.14.0"
    zip_path = tmp_path / f"ws-game-{version}.zip"
    top_dir_name = f"ws-game-{version}"
    dll_hashes: dict[str, str] = {}
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for dll_name in DLL_NAMES:
            content = f"fixture-dll:{dll_name}:{version}".encode("utf-8")
            dll_hashes[dll_name] = hashlib.sha256(content).hexdigest()
            zf.writestr(f"{top_dir_name}/{PLUGINS_CORE_REL}/{dll_name}", content)
        zf.writestr(f"{top_dir_name}/MANIFEST.txt", f"version={version}\n")

    lock_path = tmp_path / f"ws-game-{version}.lock"
    # 故意不带 headless_dlls/validator_dlls，模拟真正的旧版本锁文件。
    lock_obj = {"version": version, "git_commit": "deadbeef", "dlls": dll_hashes}
    lock_path.write_text(json.dumps(lock_obj, indent=2), encoding="utf-8")

    workspace = tmp_path / "workspace"
    workspace.mkdir()
    result = _run_get_framework(
        ["-Version", version, "-Target", str(workspace / "packages"), "-FromLocalDist", str(zip_path)],
        cwd=workspace,
    )
    assert result.returncode == 0, (
        "version=1.14.0（< 1.15.0）缺 headless_dlls/validator_dlls 的锁文件应仍被接受（跳过该项"
        "校验，不是判定损坏）：\n" + result.stdout + result.stderr
    )
    assert (workspace / "packages" / top_dir_name).is_dir()


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
