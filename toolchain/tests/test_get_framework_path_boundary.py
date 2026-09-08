"""`toolchain/get_framework.ps1` 的路径边界回归测试（PJ150-01 根治）。

背景（第八轮审计，2026-09-08，见
``architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md`` PJ150-01）：`-AllowVersionMismatch`
放行版本不一致时，脚本此前把锁文件里未经格式校验的 ``version`` 字段直接赋给
``$EffectiveVersion`` 并拼进落地目录路径；把该字段构造成形如 ``x/../../outside_sentinel``
的值，拼出的路径规范化后会落到 ``-Target`` 之外的任意兄弟目录，脚本随后对该目录执行
``Remove-Item -Recurse -Force``——六个 DLL 的哈希校验不覆盖这个元数据字段，看不出异常，且脚本
以 exit 0 收尾。审计用自建哨兵目录复现：哨兵在越界删除前存在、之后不存在。

根治两层，均需要用例覆盖：
  1. 锁文件 ``version`` 字段与 ``-Version`` 参数同样严格校验语义化版本格式（不允许出现路径分
     隔符/``..``/空白等字符）——本文件的恶意 version 用例本身就落在这一层被拦截，同时验证了两层
     防御中格式校验这一层确实生效。
  2. 落地目录规范化后必须仍是 ``-Target`` 的严格子目录，任何写入/删除前完成校验（``Test-IsStrictSubPath``
     函数，同时也用于 zip 内条目路径的 zip slip 校验）。

覆盖场景：正常版本（回归，同时验证本机 ``dist/ws-game-<VERSION>.zip`` 真实产物可用）、合法版本
不一致（``-AllowVersionMismatch`` 放行两个都合法的不同版本号）、锁文件 ``version`` 含路径分隔符
/``..``/绝对路径等非法构造（应被拒绝、不删除/不写入 ``-Target`` 与邻居目录）、zip 内条目路径带
``../``（zip slip，应被拒绝、不解压任何内容到目标外）。

运行：

```
python -m pytest toolchain/tests/test_get_framework_path_boundary.py -q
```

或作为 ``toolchain`` 套件的一部分：``python -m pytest toolchain/tests -q``（``check.ps1`` 已在跑，
不需要改 ``check.ps1``）。Windows-only（依赖 Windows PowerShell 执行 ``.ps1``），非 Windows
环境下全部用例自动跳过。

判断记录（第九轮审计工具链条目，2026-09-08，见
``architecture/落地计划/audit-85f1f4f-20260908/``）：本文件 ``_run_script`` 此前
``subprocess.run(..., text=True)`` 未指定 ``encoding``，本机 GBK 控制台下解码子进程输出会直接
``UnicodeDecodeError``，测试连 ``get_framework.ps1`` 是否正常工作都验证不到；已改为显式
``encoding="utf-8", errors="replace"``。同一轮还发现 ``get_framework.ps1`` 依赖的内置
``Get-FileHash`` 在本机部分 Windows PowerShell 5.1 环境下不可用（已改用
``toolchain/_hash.ps1`` 的 ``Get-Sha256FileHash`` 兜底），本文件的关键用例（正常落地、真实
dist 产物回归、合法版本不一致、恶意锁文件 version 拒绝、zip slip 拒绝）相应改为经 ``shell``
fixture 在本机可用的 ``powershell``/``pwsh`` 宿主下各跑一遍（只有其中一个时只跑那一个，两个都
没有时整体跳过），而不是只信任固定找到的第一个宿主。
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


def _find_powershell_executables() -> list[str]:
    """返回本机可用的 PowerShell 可执行文件列表（去重，按 Windows PowerShell 5.1 优先、pwsh 其次
    的顺序）。第九轮审计工具链条目：本机曾出现 Windows PowerShell 5.1 下内置 `Get-FileHash`
    不可用、而 `pwsh`（PowerShell 7）下同一台机器可正常调用的环境差异（原因未查明，疑似
    PSModulePath 问题）。`get_framework.ps1` 已改为经 `toolchain/_hash.ps1` 的
    `Get-Sha256FileHash` 在两种宿主下都不依赖该 cmdlet 也能算出正确哈希，但"两种宿主下都实测通过"
    本身就是这条根治的验收标准之一，因此本文件的关键用例改为两个宿主都在时都跑一遍，而不是只信任
    其中一个（哪个都没有则整体跳过，行为与此前一致）。
    """
    found: list[str] = []
    seen_resolved: set[str] = set()
    for candidate in ("powershell.exe", "powershell", "pwsh.exe", "pwsh"):
        path = shutil.which(candidate)
        if not path:
            continue
        try:
            resolved = str(Path(path).resolve()).lower()
        except OSError:
            resolved = path.lower()
        if resolved in seen_resolved:
            continue
        seen_resolved.add(resolved)
        found.append(path)
    return found


AVAILABLE_POWERSHELLS: list[str] = (
    _find_powershell_executables() if sys.platform == "win32" else []
)
_SHELL_IDS = [Path(p).stem.lower() for p in AVAILABLE_POWERSHELLS] or ["no-shell-found"]


@pytest.fixture(params=(AVAILABLE_POWERSHELLS or [None]), ids=_SHELL_IDS)
def shell(request) -> str:
    """关键用例的 PowerShell 宿主 fixture：本机找到几个可用宿主（`powershell`/`pwsh`去重后），
    这个 fixture 就把对应用例自动参数化跑几遍；一个都找不到时用例整体跳过。
    """
    if request.param is None:
        pytest.skip("找不到 powershell.exe 或 pwsh，跳过 get_framework.ps1 路径边界测试")
    return request.param


def _build_fixture_zip(zip_path: Path, version: str, top_dir_name: str | None = None) -> dict:
    """构造一个最小但结构合法的 ws-game 发布 zip：顶层目录 + 六个 DLL（内容任意，哈希随生成的
    lock.json 记录一致）。返回 dlls 哈希字典，供调用方写 lock.json。
    """
    top_dir_name = top_dir_name or f"ws-game-{version}"
    dll_hashes: dict[str, str] = {}
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for dll_name in DLL_NAMES:
            content = f"fixture-dll:{dll_name}:{version}".encode("utf-8")
            dll_hashes[dll_name] = hashlib.sha256(content).hexdigest()
            arcname = f"{top_dir_name}/{PLUGINS_CORE_REL}/{dll_name}"
            zf.writestr(arcname, content)
        # MANIFEST.txt 不是脚本强制要求的，但贴近真实产物结构，便于人工核对 fixture。
        zf.writestr(f"{top_dir_name}/MANIFEST.txt", f"version={version}\n")
    return dll_hashes


def _write_lock(lock_path: Path, version: str, dll_hashes: dict, git_commit: str = "deadbeef") -> None:
    lock_obj = {
        "version": version,
        "git_commit": git_commit,
        "dlls": dll_hashes,
    }
    lock_path.write_text(json.dumps(lock_obj, indent=2), encoding="utf-8")


def _run_script(args: list[str], cwd: Path, powershell: str | None = None) -> subprocess.CompletedProcess:
    """根治点（第九轮审计工具链条目）：此前 `text=True` 不带 `encoding` 时，Python 用
    `locale.getpreferredencoding()` 解码子进程输出——本机 GBK 控制台下该值是 `cp936`，
    `get_framework.ps1` 打印的中文提示按 UTF-8 生成/console 混合编码时会撞上非法字节序列，
    `subprocess.run` 内部解码阶段直接抛 `UnicodeDecodeError`（在拿到 `CompletedProcess` 之前
    发生，调用方连 `result.stdout`/`result.stderr` 都还没到手，测试就整个崩掉，看不出脚本本身
    是否正常工作）。显式传 `encoding="utf-8", errors="replace"` 后不再依赖控制台代码页，任何
    非 UTF-8 字节替换成 U+FFFD 而不是让解码整体失败，保证 `result.stdout`/`result.stderr`
    在断言失败时永远是可用的字符串（不会是 `None`，`f"{a}" + f"{b}"` 形式的拼接不会因为
    `None + str` 抛 `TypeError` 把子进程真正的 stderr 内容吞掉）。
    """
    powershell = powershell or (AVAILABLE_POWERSHELLS[0] if AVAILABLE_POWERSHELLS else None)
    if not powershell:
        pytest.skip("找不到 powershell.exe 或 pwsh，跳过 get_framework.ps1 路径边界测试")
    cmd = [
        powershell,
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(SCRIPT_PATH),
    ] + args
    return subprocess.run(
        cmd,
        cwd=str(cwd),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=120,
    )


def _make_sentinel_layout(tmp_path: Path) -> tuple[Path, Path, Path]:
    """搭一个 workspace/target + workspace 同级 neighbor 哨兵目录的布局，供越界删除探测使用。"""
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    target = workspace / "packages"
    target.mkdir()
    neighbor = tmp_path / "outside_sentinel"
    neighbor.mkdir()
    (neighbor / "keep.txt").write_text("must-survive", encoding="utf-8")
    return workspace, target, neighbor


def test_normal_version_lands_and_hashes_match(tmp_path: Path, shell: str) -> None:
    """正常版本：zip/lock 版本号与 -Version 一致，应成功落地且六个 DLL 字节与锁文件哈希一致。
    关键用例，本机 powershell/pwsh 都在时两者都跑一遍（见 shell fixture 判断记录）。
    """
    workspace, target, neighbor = _make_sentinel_layout(tmp_path)
    version = "9.9.9"
    zip_path = tmp_path / f"ws-game-{version}.zip"
    lock_path = tmp_path / f"ws-game-{version}.lock"
    dll_hashes = _build_fixture_zip(zip_path, version)
    _write_lock(lock_path, version, dll_hashes)

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path)],
        cwd=workspace,
        powershell=shell,
    )

    assert result.returncode == 0, result.stdout + result.stderr
    landed_dir = target / f"ws-game-{version}"
    assert landed_dir.is_dir()
    for dll_name in DLL_NAMES:
        dll_file = landed_dir / PLUGINS_CORE_REL.replace("/", "\\") / dll_name
        assert dll_file.is_file(), f"{dll_file} 未落地"
        actual = hashlib.sha256(dll_file.read_bytes()).hexdigest()
        assert actual == dll_hashes[dll_name]
    # 邻居哨兵必须原封不动。
    assert (neighbor / "keep.txt").read_text(encoding="utf-8") == "must-survive"


def test_real_dist_package_regression(tmp_path: Path, shell: str) -> None:
    """本地正常路径回归：用仓库 dist/ 下真实发布的 zip/lock 走一遍完整流程（若本机没有对应产物则跳过，
    不把"没有产物"误判为测试失败——CI/干净 checkout 环境可能没有先跑过 build.ps1 -Release）。
    关键用例，本机 powershell/pwsh 都在时两者都跑一遍（见 shell fixture 判断记录）。
    """
    version_file = REPO_ROOT / "VERSION"
    if not version_file.is_file():
        pytest.skip("找不到仓库根 VERSION 文件")
    version = version_file.read_text(encoding="utf-8").strip()
    zip_path = REPO_ROOT / "dist" / f"ws-game-{version}.zip"
    lock_path = REPO_ROOT / "dist" / f"ws-game-{version}.lock"
    if not zip_path.is_file() or not lock_path.is_file():
        pytest.skip(f"本机没有 dist/ws-game-{version}.zip|.lock，跳过真实产物回归")

    workspace, target, neighbor = _make_sentinel_layout(tmp_path)
    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path)],
        cwd=workspace,
        powershell=shell,
    )

    assert result.returncode == 0, result.stdout + result.stderr
    landed_dir = target / f"ws-game-{version}"
    assert landed_dir.is_dir()
    assert (neighbor / "keep.txt").read_text(encoding="utf-8") == "must-survive"


def test_legal_version_mismatch_allowed(tmp_path: Path, shell: str) -> None:
    """合法版本不一致：锁文件里的真实版本号与请求的 -Version 都合法但不同，-AllowVersionMismatch
    放行后应落地到以锁文件真实版本号命名的目录，请求版本号对应目录不应被创建。
    关键用例，本机 powershell/pwsh 都在时两者都跑一遍（见 shell fixture 判断记录）。
    """
    workspace, target, neighbor = _make_sentinel_layout(tmp_path)
    real_version = "2.3.4"
    requested_version = "2.3.5"
    zip_path = tmp_path / f"ws-game-{requested_version}.zip"
    lock_path = tmp_path / f"ws-game-{requested_version}.lock"
    dll_hashes = _build_fixture_zip(zip_path, real_version, top_dir_name=f"ws-game-{real_version}")
    _write_lock(lock_path, real_version, dll_hashes)

    result = _run_script(
        [
            "-Version", requested_version,
            "-Target", str(target),
            "-FromLocalDist", str(zip_path),
            "-AllowVersionMismatch",
        ],
        cwd=workspace,
        powershell=shell,
    )

    assert result.returncode == 0, result.stdout + result.stderr
    assert (target / f"ws-game-{real_version}").is_dir()
    assert not (target / f"ws-game-{requested_version}").exists()
    assert (neighbor / "keep.txt").read_text(encoding="utf-8") == "must-survive"


MALICIOUS_LOCK_VERSIONS = [
    pytest.param("1.0.0/../../outside_sentinel", id="forward-slash-dotdot"),
    pytest.param("1.0.0\\..\\..\\outside_sentinel", id="backslash-dotdot"),
    pytest.param("../../outside_sentinel", id="leading-dotdot"),
    pytest.param("..", id="bare-dotdot"),
    pytest.param("C:\\Windows\\Temp\\evil", id="windows-absolute-path"),
    pytest.param("/etc/passwd", id="posix-absolute-path"),
    pytest.param("1.0.0 ", id="trailing-whitespace"),
    pytest.param("1.0.0; Remove-Item C:\\", id="shell-metacharacters"),
]


@pytest.mark.parametrize("malicious_version", MALICIOUS_LOCK_VERSIONS)
def test_malicious_lock_version_rejected_before_any_write(
    tmp_path: Path, shell: str, malicious_version: str
) -> None:
    """PJ150-01 核心复现用例：锁文件 version 字段构造成路径穿越/绝对路径/非法字符，-AllowVersionMismatch
    放行请求后必须在任何写入/删除之前被拒绝——已存在的 -Target 内容与邻居哨兵目录都不能变化，
    脚本必须以非 0 退出码结束（审计中的缺陷表现是 exit 0）。关键用例，本机 powershell/pwsh 都在时
    两者都跑一遍（见 shell fixture 判断记录）。
    """
    workspace, target, neighbor = _make_sentinel_layout(tmp_path)
    # Target 内先放一个"应该保持原样"的既有版本目录，验证恶意请求不会触发对它的删除或覆盖。
    preexisting = target / "ws-game-1.0.0"
    preexisting.mkdir()
    (preexisting / "marker.txt").write_text("pre-existing", encoding="utf-8")

    requested_version = "1.0.0"
    zip_path = tmp_path / f"ws-game-{requested_version}.zip"
    lock_path = tmp_path / f"ws-game-{requested_version}.lock"
    # zip 内顶层目录名沿用真实约定（与 version 无关，DLL 内容本身合法可解压）。
    dll_hashes = _build_fixture_zip(zip_path, requested_version, top_dir_name="ws-game-1.0.0")
    _write_lock(lock_path, malicious_version, dll_hashes)

    result = _run_script(
        [
            "-Version", requested_version,
            "-Target", str(target),
            "-FromLocalDist", str(zip_path),
            "-AllowVersionMismatch",
        ],
        cwd=workspace,
        powershell=shell,
    )

    assert result.returncode != 0, (
        f"恶意 lock.version={malicious_version!r} 未被拒绝，脚本以 exit 0 结束：\n"
        + result.stdout + result.stderr
    )
    # 邻居哨兵必须原封不动——这是审计报告里实际被删除的对象。
    assert (neighbor / "keep.txt").read_text(encoding="utf-8") == "must-survive"
    assert not (neighbor.parent / "1.0.0").exists()
    # Target 内既有目录不能被删除/覆盖。
    assert preexisting.is_dir()
    assert (preexisting / "marker.txt").read_text(encoding="utf-8") == "pre-existing"
    # Target 目录本身不应该多出任何新的落地内容。
    landed_names = {p.name for p in target.iterdir()}
    assert landed_names == {"ws-game-1.0.0"}


def test_zip_entry_path_traversal_rejected(tmp_path: Path, shell: str) -> None:
    """zip slip：zip 内条目路径带 `../`，即便六个 DLL 本身哈希对得上，也必须在落地前被拒绝，
    不得把任何内容解压到预期目标目录之外。关键用例，本机 powershell/pwsh 都在时两者都跑一遍
    （见 shell fixture 判断记录）。
    """
    workspace, target, neighbor = _make_sentinel_layout(tmp_path)
    version = "3.1.4"
    zip_path = tmp_path / f"ws-game-{version}.zip"
    lock_path = tmp_path / f"ws-game-{version}.lock"

    dll_hashes: dict[str, str] = {}
    top_dir_name = f"ws-game-{version}"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for dll_name in DLL_NAMES:
            content = f"fixture-dll:{dll_name}:{version}".encode("utf-8")
            dll_hashes[dll_name] = hashlib.sha256(content).hexdigest()
            zf.writestr(f"{top_dir_name}/{PLUGINS_CORE_REL}/{dll_name}", content)
        # 恶意条目：借助 `../` 试图逃逸到解压临时目录之外。
        zf.writestr(f"{top_dir_name}/../../zip_slip_evil.txt", "evil-payload")
    _write_lock(lock_path, version, dll_hashes)

    result = _run_script(
        ["-Version", version, "-Target", str(target), "-FromLocalDist", str(zip_path)],
        cwd=workspace,
        powershell=shell,
    )

    assert result.returncode != 0, (
        "zip slip 条目未被拒绝，脚本以 exit 0 结束：\n" + result.stdout + result.stderr
    )
    assert not (target / f"ws-game-{version}").exists()
    assert (neighbor / "keep.txt").read_text(encoding="utf-8") == "must-survive"
    # 恶意文件不应该出现在临时目录的上两级（模拟越界逃逸的落点）。
    escaped_candidate = zip_path.parent.parent / "zip_slip_evil.txt"
    assert not escaped_candidate.exists()
