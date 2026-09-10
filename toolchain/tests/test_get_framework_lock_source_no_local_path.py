"""``toolchain/get_framework.ps1`` 写 ``ws-game.lock`` 时不再携带本机绝对路径的回归测试
（消费方反馈 E3 根治，2026-09-10，见 architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E3）。

背景：``ws-game.lock`` 是约定提交进游戏仓库的文件（见 get_framework.ps1 ``.PARAMETER LockPath``
说明）。``-FromLocalDist`` 场景此前把调用方本机的绝对路径原样写进 ``source.local_path``——消费方
提交这份锁文件后换一台机器（不同本地路径）重新运行本脚本，``source.local_path`` 就会变化，产生一次
与"框架引用内容"完全无关、纯粹因本机路径不同而触发的锁文件 diff。

根治：a) ``source`` 不再写绝对路径，只写 ``channel``（沿用，``editor/docs/编辑器产品文档.md`` 已将
``source.channel`` 记为契约字段）与相对的 ``zip_file_name``；b) 判定"锁文件是否需要改写"时改为只比较
``version``/``git_commit``/``dlls``/``headless_dlls``/``validator_dlls``/``samples`` 这几个描述框架
引用内容本身的字段（``Test-LockContentEquivalent`` 函数），``source`` 字段的任何差异（含新旧字段名
不同）不再触发改写判定。

本文件覆盖三个场景：
  1. 两台"机器"（不同本地绝对路径）对同一份 zip 内容跑 ``-FromLocalDist``，生成的锁文件里
     ``source`` 不出现任一台机器的本地路径字符串。
  2. 机器 A 生成锁文件后，机器 B（不同本地路径，同一份 zip 内容）重新运行——锁文件不应被重新
     写入（用文件 mtime 判定，不解析本机 PowerShell 控制台代码页下的本地化提示文本：本机
     Windows PowerShell 5.1 控制台默认代码页不是 UTF-8，``Write-Host`` 打印的中文提示按
     ``subprocess`` 管道读出后再按 UTF-8 解码会乱码，与
     ``test_get_framework_path_boundary.py`` 判断记录"第九轮审计工具链条目"同一类问题；本文件
     通过比较锁文件写入时间戳而不是断言本地化输出文本内容，天然避开这一编码陷阱）。
  3. 预置一份带旧格式 ``source.local_path`` 字段的锁文件（模拟改动前生成、已提交的锁文件），重新
     运行 ``-FromLocalDist``——脚本仍应正常工作、退出码为 0，且不重新写入锁文件（旧格式锁文件不因
     ``source`` 字段形状不同而被误判为"内容不同"）。

运行：``python -m pytest toolchain/tests/test_get_framework_lock_source_no_local_path.py -q`` 或
``python -m pytest toolchain/tests -q``。Windows-only（依赖 Windows PowerShell 执行 ``.ps1``），
非 Windows 环境下全部用例自动跳过（与 ``test_get_framework_path_boundary.py`` 同款约定）。
"""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
import time
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
    """本文件用例数量少（三个场景，非路径边界那种攻防用例），只取本机第一个可用的
    PowerShell 宿主即可，不像 test_get_framework_path_boundary.py 那样对每个宿主各跑一遍。
    """
    for candidate in ("powershell.exe", "powershell", "pwsh.exe", "pwsh"):
        path = shutil.which(candidate)
        if path:
            return path
    return None


POWERSHELL = _find_powershell_executable() if sys.platform == "win32" else None


def _build_fixture_zip(zip_path: Path, version: str) -> dict:
    """构造一个最小但结构合法的 ws-game 发布 zip：顶层目录 + 六个 DLL（内容固定，两次调用
    传入同一个 version 会产出内容完全相同的 zip，模拟"同一份发布产物"）。
    """
    top_dir_name = f"ws-game-{version}"
    dll_hashes: dict[str, str] = {}
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for dll_name in DLL_NAMES:
            content = f"fixture-dll:{dll_name}:{version}".encode("utf-8")
            dll_hashes[dll_name] = hashlib.sha256(content).hexdigest()
            zf.writestr(f"{top_dir_name}/{PLUGINS_CORE_REL}/{dll_name}", content)
        zf.writestr(f"{top_dir_name}/MANIFEST.txt", f"version={version}\n")
    return dll_hashes


def _write_lock(lock_path: Path, version: str, dll_hashes: dict, git_commit: str = "deadbeef") -> None:
    lock_obj = {"version": version, "git_commit": git_commit, "dlls": dll_hashes}
    lock_path.write_text(json.dumps(lock_obj, indent=2), encoding="utf-8")


def _run_script(args: list[str], cwd: Path) -> subprocess.CompletedProcess:
    if not POWERSHELL:
        pytest.skip("找不到 powershell.exe 或 pwsh，跳过 get_framework.ps1 锁文件 source 字段测试")
    cmd = [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT_PATH)] + args
    return subprocess.run(
        cmd, cwd=str(cwd), capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=120,
    )


def test_lock_source_has_no_absolute_local_path(tmp_path: Path) -> None:
    """-FromLocalDist 生成的锁文件 source 字段不包含本机绝对路径字符串。"""
    version = "8.8.8"
    machine_a_dir = tmp_path / "machine_a_home" / "downloads"
    machine_a_dir.mkdir(parents=True)
    zip_path = machine_a_dir / f"ws-game-{version}.zip"
    lock_source_path = machine_a_dir / f"ws-game-{version}.lock"
    dll_hashes = _build_fixture_zip(zip_path, version)
    _write_lock(lock_source_path, version, dll_hashes)

    workspace = tmp_path / "workspace_a"
    workspace.mkdir()
    target = workspace / "packages"
    game_lock_path = workspace / "ws-game.lock"

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path),
         "-LockPath", str(game_lock_path)],
        cwd=workspace,
    )
    assert result.returncode == 0, result.stdout + result.stderr

    written_lock = json.loads(game_lock_path.read_text(encoding="utf-8"))
    assert written_lock["source"]["channel"] == "zip"
    assert "local_path" not in written_lock["source"], (
        "source 字段不应再出现 local_path（含本机绝对路径），见消费方反馈 E3"
    )
    source_json_text = json.dumps(written_lock["source"])
    assert str(machine_a_dir) not in source_json_text
    assert "zip_file_name" in written_lock["source"]
    assert written_lock["source"]["zip_file_name"] == zip_path.name


def test_two_machines_different_local_paths_no_spurious_rewrite(tmp_path: Path) -> None:
    """机器 A、机器 B 用不同本地绝对路径引用同一份发布内容（相同 version/dlls），机器 B 重新运行
    时脚本应判定"内容已一致，无需改写"，不应打印"已存在...改写为"这类触发改写的提示。
    """
    version = "8.8.9"

    # 机器 A：在 tmp_path 下一个较深的子目录里放 zip/lock，产出 ws-game.lock。
    machine_a_downloads = tmp_path / "machine_a" / "Users" / "alice" / "Downloads"
    machine_a_downloads.mkdir(parents=True)
    zip_path_a = machine_a_downloads / f"ws-game-{version}.zip"
    lock_source_a = machine_a_downloads / f"ws-game-{version}.lock"
    dll_hashes = _build_fixture_zip(zip_path_a, version)
    _write_lock(lock_source_a, version, dll_hashes)

    workspace = tmp_path / "shared_game_repo"
    workspace.mkdir()
    target = workspace / "packages"
    game_lock_path = workspace / "ws-game.lock"

    result_a = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path_a),
         "-LockPath", str(game_lock_path)],
        cwd=workspace,
    )
    assert result_a.returncode == 0, result_a.stdout + result_a.stderr
    lock_after_a = game_lock_path.read_text(encoding="utf-8")

    # 机器 B：完全不同的本地绝对路径（不同盘符风格的目录名），但 zip 内容（六个 DLL 字节、
    # version）与机器 A 相同——模拟"提交了机器 A 生成的 ws-game.lock 后，机器 B 重新拉取同一个
    # 发布版本"。故意重建一份新的 zip/lock（内容相同、路径不同），而不是直接复用 zip_path_a，
    # 更贴近"不同机器各自下载/持有一份物理不同的文件"这一真实场景。
    machine_b_dir = tmp_path / "machine_b" / "var" / "cache" / "ws-game-downloads"
    machine_b_dir.mkdir(parents=True)
    zip_path_b = machine_b_dir / f"ws-game-{version}.zip"
    lock_source_b = machine_b_dir / f"ws-game-{version}.lock"
    dll_hashes_b = _build_fixture_zip(zip_path_b, version)
    assert dll_hashes_b == dll_hashes, "两次构造的 fixture zip 内容应完全一致（同一 version 输入）"
    _write_lock(lock_source_b, version, dll_hashes_b)

    # 判断记录：不解析本地化的 Write-Host 提示文本判定"是否改写"（本机控制台代码页解码中文会
    # 乱码，见文件头判断记录），改用锁文件的写入时间戳——真正发生改写会调用
    # [System.IO.File]::WriteAllText 产生一个新的 LastWriteTimeUtc；跳过改写分支完全不触碰该
    # 文件，时间戳原样保留。运行前先记录机器 A 那次写入后的时间戳，中间加一个足够短暂的等待，
    # 确保"如果真的发生了第二次写入"时间戳一定可观测地变化（Windows NTFS mtime 精度到 100ns，
    # 正常不需要这个等待也能感知到变化，这里只是加一层保险，避免同一时钟节拍内两次写入被误判为
    # 时间戳相同）。
    mtime_after_a_ns = game_lock_path.stat().st_mtime_ns
    time.sleep(0.05)

    result_b = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path_b),
         "-LockPath", str(game_lock_path)],
        cwd=workspace,
    )
    assert result_b.returncode == 0, result_b.stdout + result_b.stderr

    mtime_after_b_ns = game_lock_path.stat().st_mtime_ns
    assert mtime_after_b_ns == mtime_after_a_ns, (
        "机器 B 用不同本地路径引用同一份发布内容时，锁文件不应被重新写入（mtime 应保持机器 A "
        "那次写入时的原值）——mtime 变化说明脚本仍然因 source 字段（本机路径）不同而触发了一次"
        "不必要的改写"
    )

    lock_after_b = game_lock_path.read_text(encoding="utf-8")
    assert lock_after_a == lock_after_b, "未触发改写时，磁盘上的锁文件内容应保持机器 A 那次写入的原样"


def test_legacy_lock_with_source_local_path_still_accepted(tmp_path: Path) -> None:
    """预置一份带旧格式 source.local_path 字段的锁文件（模拟本次改动前生成、已提交的锁文件），
    重新运行 -FromLocalDist（新内容与旧锁文件描述的引用内容一致）应正常工作、判定内容一致，
    不因 source 字段形状不同（旧的 local_path vs 新的 zip_file_name）而报错或误判为不同。
    """
    version = "8.8.10"
    zip_dir = tmp_path / "some_local_path"
    zip_dir.mkdir()
    zip_path = zip_dir / f"ws-game-{version}.zip"
    lock_source_path = zip_dir / f"ws-game-{version}.lock"
    dll_hashes = _build_fixture_zip(zip_path, version)
    _write_lock(lock_source_path, version, dll_hashes)

    workspace = tmp_path / "workspace_legacy"
    workspace.mkdir()
    target = workspace / "packages"
    game_lock_path = workspace / "ws-game.lock"

    # 预置一份"旧格式"锁文件：version/git_commit/dlls 与本次要拉取的内容完全一致，但 source
    # 字段还是改动前的形状（channel + local_path，指向一个跟本次调用毫不相干的路径）。
    legacy_lock_obj = {
        "version": version,
        "git_commit": "deadbeef",
        "dlls": dll_hashes,
        "source": {
            "channel": "zip",
            "local_path": r"C:\some\completely\different\old\machine\path\ws-game-8.8.10.zip",
        },
    }
    game_lock_path.write_text(json.dumps(legacy_lock_obj, indent=2), encoding="utf-8")
    mtime_before_ns = game_lock_path.stat().st_mtime_ns
    time.sleep(0.05)

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path),
         "-LockPath", str(game_lock_path)],
        cwd=workspace,
    )

    assert result.returncode == 0, result.stdout + result.stderr
    # 判断记录：同上一个用例，用 mtime 而不是本地化输出文本判定是否改写。
    mtime_after_ns = game_lock_path.stat().st_mtime_ns
    assert mtime_after_ns == mtime_before_ns, (
        "带旧格式 source.local_path 的既有锁文件，在引用内容（version/dlls）一致时应被接受、"
        "判定为无需改写（mtime 不应变化），而不是因 source 字段形状不同报错或被误判为内容不同"
    )
    # 内容本身也不应发生变化（既然判定为无需改写，文件字节层面也保持预置的旧格式原样）。
    assert json.loads(game_lock_path.read_text(encoding="utf-8")) == legacy_lock_obj


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
