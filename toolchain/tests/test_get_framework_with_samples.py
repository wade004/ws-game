"""``toolchain/get_framework.ps1 -WithSamples`` 回归测试（消费方反馈 E4 根治，2026-09-10，见
architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E4）。

背景：主 zip（``ws-game-<ver>.zip``）从不带 ``data/_sample``、``assets/_sample``——那是本仓库自测
用的验收数据集，不代表任何真实游戏内容，因此不随 ``-Dist`` 打进 ``dist/<ver>/`` 快照。消费方反馈：
新工程接入后想验证"框架端到端能不能跑起来"缺一份现成的、已知合法的样例数据/资源可用。根治：
``build.ps1 -Dist``/``-Release`` 额外单独打一份 ``dist/ws-game-<ver>-samples.zip``（主 zip 内容不变，
向后兼容），``get_framework.ps1 -WithSamples`` 下载/解压并按锁文件新增的 ``samples.sha256`` 字段校验，
合并落地到与主 zip 相同的 ``<Target>/ws-game-<Version>/`` 目录下。

覆盖场景：
  1. 不传 ``-WithSamples``（默认）：行为与改动前完全一致，落地结果不含 ``data/_sample``/
     ``assets/_sample``，也不下载/校验 samples 包。
  2. 传 ``-WithSamples``：samples 包哈希校验通过后，``data/_sample``、``assets/_sample`` 与主 zip
     解压结果合并落地到同一棵目录树；已有的 ``data/_framework``、``assets/_placeholder`` 等不受
     影响。
  3. 传 ``-WithSamples`` 但同目录找不到 ``<zip-basename>-samples.zip``：报错退出（非 0），不落地
     任何内容（沿用主 zip"校验通过才落地"的语义）。
  4. 传 ``-WithSamples``、samples 包存在但内容被篡改（sha256 与锁文件不一致）：报错退出（非 0），
     不落地任何内容。
  5. 传 ``-WithSamples``，但锁文件没有 ``samples.sha256`` 字段（模拟早于 E4 落地的旧版本锁文件）：
     报错退出（非 0），给出明确提示，不是静默忽略或误报内容不一致。

运行：``python -m pytest toolchain/tests/test_get_framework_with_samples.py -q`` 或
``python -m pytest toolchain/tests -q``。Windows-only（依赖 Windows PowerShell 执行 ``.ps1``），
非 Windows 环境下全部用例自动跳过（与 ``test_get_framework_path_boundary.py`` 同款约定）。
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
SCRIPT_PATH = REPO_ROOT / "toolchain" / "get_framework.ps1"

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

pytestmark = pytest.mark.skipif(
    sys.platform != "win32", reason="get_framework.ps1 只在 Windows PowerShell 下运行"
)


def _find_powershell_executable() -> str | None:
    for candidate in ("powershell.exe", "powershell", "pwsh.exe", "pwsh"):
        path = shutil.which(candidate)
        if path:
            return path
    return None


POWERSHELL = _find_powershell_executable() if sys.platform == "win32" else None


def _build_fixture_zip(zip_path: Path, version: str) -> dict:
    """构造一个最小但结构合法的 ws-game 发布主 zip：顶层目录 + 六个 DLL。"""
    top_dir_name = f"ws-game-{version}"
    dll_hashes: dict[str, str] = {}
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for dll_name in DLL_NAMES:
            content = f"fixture-dll:{dll_name}:{version}".encode("utf-8")
            dll_hashes[dll_name] = hashlib.sha256(content).hexdigest()
            zf.writestr(f"{top_dir_name}/{PLUGINS_CORE_REL}/{dll_name}", content)
        # 主 zip 也带一份 data/_framework，贴近真实产物结构（不是 -WithSamples 的校验对象，但
        # 确认合并 samples 内容时不会误伤既有的框架级数据）。
        zf.writestr(f"{top_dir_name}/data/_framework/found.marker.json", '{"table":"found.marker"}')
        zf.writestr(f"{top_dir_name}/MANIFEST.txt", f"version={version}\n")
    return dll_hashes


def _build_fixture_samples_zip(zip_path: Path, version: str) -> None:
    """构造一个最小的 samples zip：顶层目录（与主 zip 同一约定）+ data/_sample、assets/_sample
    两棵目录树各放一个文件。
    """
    top_dir_name = f"ws-game-{version}"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr(
            f"{top_dir_name}/data/_sample/quest/quest.sample_hunt.json",
            '{"table":"quest.sample_hunt","schema_version":1,"rows":[]}',
        )
        zf.writestr(f"{top_dir_name}/assets/_sample/sprites/hero.marker.txt", "fixture-sprite-marker")


def _sha256_of(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _write_lock(
    lock_path: Path,
    version: str,
    dll_hashes: dict,
    git_commit: str = "deadbeef",
    samples_sha256: str | None = None,
) -> None:
    lock_obj: dict = {"version": version, "git_commit": git_commit, "dlls": dll_hashes}
    if samples_sha256 is not None:
        lock_obj["samples"] = {"sha256": samples_sha256}
    lock_path.write_text(json.dumps(lock_obj, indent=2), encoding="utf-8")


def _run_script(args: list[str], cwd: Path) -> subprocess.CompletedProcess:
    if not POWERSHELL:
        pytest.skip("找不到 powershell.exe 或 pwsh，跳过 get_framework.ps1 -WithSamples 测试")
    cmd = [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT_PATH)] + args
    return subprocess.run(
        cmd, cwd=str(cwd), capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=120,
    )


def _make_fixture_set(tmp_path: Path, version: str, with_samples_zip: bool = True):
    """在 tmp_path 下构造一套 zip + lock（+ 可选 samples zip），返回
    (zip_path, dll_hashes, samples_zip_path_or_None, samples_sha256_or_None)。
    """
    zip_path = tmp_path / f"ws-game-{version}.zip"
    lock_path = tmp_path / f"ws-game-{version}.lock"
    dll_hashes = _build_fixture_zip(zip_path, version)

    samples_zip_path = None
    samples_sha256 = None
    if with_samples_zip:
        samples_zip_path = tmp_path / f"ws-game-{version}-samples.zip"
        _build_fixture_samples_zip(samples_zip_path, version)
        samples_sha256 = _sha256_of(samples_zip_path)

    return zip_path, lock_path, dll_hashes, samples_zip_path, samples_sha256


def test_without_with_samples_flag_unaffected(tmp_path: Path) -> None:
    """不传 -WithSamples：即便同目录存在 samples zip，也不下载/合并，落地结果不含
    data/_sample、assets/_sample（向后兼容，行为与改动前完全一致）。
    """
    version = "6.6.1"
    zip_path, lock_path, dll_hashes, _samples_zip, samples_sha256 = _make_fixture_set(tmp_path, version)
    _write_lock(lock_path, version, dll_hashes, samples_sha256=samples_sha256)

    workspace = tmp_path / "workspace"
    workspace.mkdir()
    target = workspace / "packages"

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path)],
        cwd=workspace,
    )
    assert result.returncode == 0, result.stdout + result.stderr

    landed_dir = target / f"ws-game-{version}"
    assert landed_dir.is_dir()
    assert not (landed_dir / "data" / "_sample").exists()
    assert not (landed_dir / "assets" / "_sample").exists()
    assert (landed_dir / "data" / "_framework").is_dir()


def test_with_samples_merges_into_landed_tree(tmp_path: Path) -> None:
    """传 -WithSamples：samples 内容与主 zip 解压结果合并落地，既有框架数据不受影响。"""
    version = "6.6.2"
    zip_path, lock_path, dll_hashes, samples_zip_path, samples_sha256 = _make_fixture_set(tmp_path, version)
    _write_lock(lock_path, version, dll_hashes, samples_sha256=samples_sha256)

    workspace = tmp_path / "workspace"
    workspace.mkdir()
    target = workspace / "packages"

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path), "-WithSamples"],
        cwd=workspace,
    )
    assert result.returncode == 0, result.stdout + result.stderr

    landed_dir = target / f"ws-game-{version}"
    assert (landed_dir / "data" / "_sample" / "quest" / "quest.sample_hunt.json").is_file()
    assert (landed_dir / "assets" / "_sample" / "sprites" / "hero.marker.txt").is_file()
    # 既有框架级数据/主 zip 内容不受合并影响。
    assert (landed_dir / "data" / "_framework" / "found.marker.json").is_file()

    written_lock = json.loads((workspace / "ws-game.lock").read_text(encoding="utf-8"))
    assert written_lock["samples"]["sha256"] == samples_sha256


def test_with_samples_missing_sibling_zip_rejected(tmp_path: Path) -> None:
    """传 -WithSamples 但同目录找不到 samples zip：报错退出，不落地任何内容。"""
    version = "6.6.3"
    zip_path, lock_path, dll_hashes, _samples_zip, samples_sha256 = _make_fixture_set(
        tmp_path, version, with_samples_zip=False
    )
    # 锁文件仍然声明了 samples.sha256（模拟"锁文件描述有 samples，但物理文件缺失/未下载"的场景），
    # 用一个任意但格式合法的哈希占位即可——本用例要验证的是"找不到文件"这一步先被拦下，不会走到
    # 哈希比对。
    _write_lock(lock_path, version, dll_hashes, samples_sha256="0" * 64)

    workspace = tmp_path / "workspace"
    workspace.mkdir()
    target = workspace / "packages"

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path), "-WithSamples"],
        cwd=workspace,
    )
    assert result.returncode != 0
    assert not target.exists() or not any(target.iterdir())


def test_with_samples_hash_mismatch_rejected(tmp_path: Path) -> None:
    """samples zip 存在但内容被篡改（sha256 与锁文件不一致）：报错退出，不落地任何内容。"""
    version = "6.6.4"
    zip_path, lock_path, dll_hashes, samples_zip_path, _samples_sha256 = _make_fixture_set(tmp_path, version)
    # 锁文件记一个错误的（不匹配实际内容的）sha256，模拟下载损坏/被篡改。
    _write_lock(lock_path, version, dll_hashes, samples_sha256="f" * 64)

    workspace = tmp_path / "workspace"
    workspace.mkdir()
    target = workspace / "packages"

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path), "-WithSamples"],
        cwd=workspace,
    )
    assert result.returncode != 0
    assert not target.exists() or not any(target.iterdir())


def test_with_samples_legacy_lock_without_samples_field_rejected(tmp_path: Path) -> None:
    """传 -WithSamples，但锁文件没有 samples 字段（模拟早于 E4 落地的旧版本锁文件）：报错退出，
    给出明确提示，不是静默忽略或误判为内容不一致。
    """
    version = "6.6.5"
    zip_path, lock_path, dll_hashes, samples_zip_path, _samples_sha256 = _make_fixture_set(tmp_path, version)
    _write_lock(lock_path, version, dll_hashes, samples_sha256=None)  # 旧格式：没有 samples 字段

    workspace = tmp_path / "workspace"
    workspace.mkdir()
    target = workspace / "packages"

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path), "-WithSamples"],
        cwd=workspace,
    )
    assert result.returncode != 0
    assert not target.exists() or not any(target.iterdir())


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
