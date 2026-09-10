"""`toolchain/abi_probe.ps1` 输出目录边界回归测试（PJ114-03 根治，外部审计
audit-76d16a5-20260910）：`abi_probe.ps1` 此前对显式已存在的 `-OutDir` 直接
`Remove-Item -Recurse -Force`，没有任何边界检查——调用方一旦传了一个真实有内容的目录，会被整个
递归删除，且这个删除发生在脚本"是否有基线可用"之前的探针主流程之外，不受 `-SkipIfBaselineMissing`
影响。

根治后：脚本把 OutDir 校验放在最前面（早于基线版本/zip 解析，见该脚本判断记录）——省略时用
时间戳+随机后缀生成一个全新临时目录；显式传入且已存在时，非空直接拒绝（退出码 1），任何分支都不再
对已存在目录做任何删除。本文件用哨兵文件验证"非空目录被拒绝且哨兵仍在"这条路径：故意传一个不存在
的 `-BaselineZip`（脚本本该在 OutDir 校验之后才会检查基线，但由于 OutDir 校验现在排在最前面，
这个不存在的基线路径根本不会被读到；这里选用不存在的路径只是为了不依赖本机是否有真实 dist/ 产物，
不代表在测试基线缺失分支）。

运行：

```
python -m pytest toolchain/tests/test_abi_probe_outdir_safety.py -q
```

Windows-only（依赖 Windows PowerShell 执行 `.ps1`），非 Windows 环境下用例自动跳过。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "toolchain" / "abi_probe.ps1"

pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="abi_probe.ps1 只在 Windows PowerShell 下运行")

AVAILABLE_POWERSHELL = (
    shutil.which("powershell.exe")
    or shutil.which("powershell")
    or shutil.which("pwsh.exe")
    or shutil.which("pwsh")
)


def _run_probe(args: list[str]) -> subprocess.CompletedProcess:
    if not AVAILABLE_POWERSHELL:
        pytest.skip("找不到 powershell.exe 或 pwsh，跳过 abi_probe.ps1 OutDir 安全测试")
    cmd = [AVAILABLE_POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT_PATH)] + args
    return subprocess.run(
        cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60
    )


def test_nonempty_existing_outdir_rejected_and_sentinel_survives(tmp_path: Path) -> None:
    """显式 -OutDir 指向一个已存在且非空的目录：脚本必须拒绝（exit 1），且不得删除/修改目录内
    既有内容——这是审计报告里实际会被 `Remove-Item -Recurse -Force` 删掉的对象。
    """
    out_dir = tmp_path / "existing_nonempty"
    out_dir.mkdir()
    sentinel = out_dir / "sentinel.txt"
    sentinel.write_text("must-survive", encoding="utf-8")
    nested_dir = out_dir / "nested"
    nested_dir.mkdir()
    (nested_dir / "also_must_survive.txt").write_text("still here", encoding="utf-8")

    result = _run_probe(
        [
            "-BaselineZip", str(tmp_path / "does-not-exist.zip"),
            "-OutDir", str(out_dir),
        ]
    )

    assert result.returncode == 1, (
        "非空既有 -OutDir 应被拒绝（exit 1）：\n" + result.stdout + result.stderr
    )
    assert sentinel.is_file(), "哨兵文件被删除——OutDir 边界检查失效"
    assert sentinel.read_text(encoding="utf-8") == "must-survive"
    assert nested_dir.is_dir(), "OutDir 内嵌套目录被删除"
    assert (nested_dir / "also_must_survive.txt").is_file()


def test_empty_existing_outdir_is_reused_not_rejected(tmp_path: Path) -> None:
    """显式 -OutDir 指向一个已存在但为空的目录：应直接复用（不拒绝、不删除），脚本会继续往下走
    （多半在后续因基线 zip 不存在而失败/跳过，但那是另一条判定路径，不是本用例要覆盖的对象——本
    用例只关心 OutDir 本身没有被当成"非空目录"误拒）。
    """
    out_dir = tmp_path / "existing_empty"
    out_dir.mkdir()

    result = _run_probe(
        [
            "-BaselineZip", str(tmp_path / "does-not-exist.zip"),
            "-OutDir", str(out_dir),
        ]
    )

    # 空目录不应触发"非空拒绝"这条 exit 1 分支——用输出文本区分，而不是只看退出码（退出码 1 还
    # 可能来自别的原因，例如基线缺失且严格模式）。
    assert "已存在且非空" not in (result.stdout + result.stderr)
    assert out_dir.is_dir(), "复用分支不应该把目录本身删掉"


def test_default_outdir_is_created_fresh_each_run(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """省略 -OutDir 时，脚本应该用 $env:TEMP 下带时间戳 + 随机后缀的全新目录，不需要传 -OutDir
    也能正常创建（不依赖调用方预先准备好目录）——这里只验证"能找到一个新建的 ws-game-abi-probe-*
    目录并且里面出现了 consumer 子目录"，不深入探针主流程本身（主流程有基线 zip 依赖，属于
    test_abi_surface_compare.py 的"真实基线端到端"用例覆盖范围）。
    """
    fake_temp = tmp_path / "fake_temp_root"
    fake_temp.mkdir()
    monkeypatch.setenv("TEMP", str(fake_temp))
    monkeypatch.setenv("TMP", str(fake_temp))

    result = _run_probe(["-BaselineZip", str(tmp_path / "does-not-exist.zip")])

    candidates = list(fake_temp.glob("ws-game-abi-probe-*"))
    assert len(candidates) == 1, f"期望在 {fake_temp} 下恰好新建一个默认 OutDir，实际：{candidates}"
    assert (candidates[0] / "consumer").is_dir()
    # 基线 zip 不存在，默认 -SkipIfBaselineMissing $true，应判定为 SKIP（退出码 3），不是 FAIL。
    assert result.returncode == 3, result.stdout + result.stderr
