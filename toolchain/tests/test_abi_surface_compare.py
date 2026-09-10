"""`toolchain/abi_surface` 的 `compare` 子命令回归测试（PJ114-02 根治，外部审计
audit-76d16a5-20260910）：`toolchain/abi_probe/Program.cs` 此前只覆盖人工写死的少量签名——1.14.0
新增的 `SkillHost` 第十八个可选构造参数就漏判了（见 PJ114-01）。`toolchain/abi_surface` 改用
`System.Reflection.MetadataLoadContext` 反射整份 DLL 的公开/受保护 API 表面，本文件不经 dump
（不需要真的编译一份带特定签名的测试 DLL），直接构造 dump 输出格式的纯文本（见
`toolchain/abi_surface/SurfaceDumper.cs` 每种成员的行格式判断记录）覆盖 `compare` 的判定逻辑：
删方法、改参数、改返回类型、删枚举成员、接口新增 abstract 成员（应判破坏）、接口新增默认实现成员
与普通新增（不应判破坏）、allowlist 放行。

另有一个"真实基线"端到端用例：本机 `dist/ws-game-1.12.0.zip` 存在时跑一遍
`toolchain/abi_probe.ps1`（内部会构建并调用 `toolchain/abi_surface`），应 exit 0；zip 不存在则
skip（不是 pass——`abi_probe.ps1` 现在对基线缺失的默认行为是可见 SKIP/退出码 3，见该脚本
`.PARAMETER SkipIfBaselineMissing` 判断记录，本文件的 skip 与它是同一件事的两处独立体现）。

运行：

```
python -m pytest toolchain/tests/test_abi_surface_compare.py -q
```
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
ABI_SURFACE_PROJ = REPO_ROOT / "toolchain" / "abi_surface" / "AbiSurface.csproj"
ABI_PROBE_SCRIPT = REPO_ROOT / "toolchain" / "abi_probe.ps1"

DOTNET = shutil.which("dotnet")

pytestmark = pytest.mark.skipif(DOTNET is None, reason="本机找不到 dotnet，跳过 abi_surface 测试")


@pytest.fixture(scope="session")
def abi_surface_dll(tmp_path_factory: pytest.TempPathFactory) -> Path:
    """构建一次 `toolchain/abi_surface`（session 级缓存，避免每个用例都重新 `dotnet build`）。"""
    out_dir = tmp_path_factory.mktemp("abi_surface_build")
    result = subprocess.run(
        [DOTNET, "build", str(ABI_SURFACE_PROJ), "-c", "Release", "--nologo", "-o", str(out_dir)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=300,
    )
    assert result.returncode == 0, "abi_surface 构建失败：\n" + result.stdout + result.stderr
    dll = out_dir / "AbiSurface.dll"
    assert dll.is_file(), f"未找到构建产物：{dll}"
    return dll


def _run_compare(
    abi_surface_dll: Path,
    tmp_path: Path,
    baseline_lines: list[str],
    current_lines: list[str],
    allowlist_lines: list[str] | None = None,
) -> subprocess.CompletedProcess:
    baseline_file = tmp_path / "baseline.txt"
    current_file = tmp_path / "current.txt"
    report_file = tmp_path / "report.txt"
    baseline_file.write_text("\n".join(baseline_lines) + "\n", encoding="utf-8")
    current_file.write_text("\n".join(current_lines) + "\n", encoding="utf-8")

    args = [DOTNET, str(abi_surface_dll), "compare", str(baseline_file), str(current_file), "--out", str(report_file)]
    if allowlist_lines is not None:
        allowlist_file = tmp_path / "allowlist.txt"
        allowlist_file.write_text("\n".join(allowlist_lines) + "\n", encoding="utf-8")
        args += ["--allowlist", str(allowlist_file)]

    result = subprocess.run(
        args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60
    )
    result_report = report_file.read_text(encoding="utf-8") if report_file.is_file() else ""
    return result, result_report


def test_deleted_method_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\t-",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\t-",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "Bar" in report
    assert "RESULT=BREAKING" in report


def test_changed_parameter_type_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar(System.Int32):System.Void\t-",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar(System.String):System.Void\t-",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "Bar(System.Int32)" in report


def test_changed_return_type_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Int32\t-",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar():System.String\t-",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "Bar():System.Int32" in report


def test_deleted_enum_member_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Kind\tenum\t-",
        "MEMBER\tNs.Kind\tenumvalue\tA=0\t-",
        "MEMBER\tNs.Kind\tenumvalue\tB=1\t-",
    ]
    current = [
        "TYPE\tNs.Kind\tenum\t-",
        "MEMBER\tNs.Kind\tenumvalue\tA=0\t-",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "B=1" in report


def test_interface_new_abstract_method_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """既有接口新增 abstract 成员：没有默认实现体，既有实现方（旧 consumer）不会自动获得实现，
    重新编译会报"未实现接口成员"——见 SurfaceCompare.cs 规则 2 判断记录。
    """
    baseline = [
        "TYPE\tNs.IFoo\tinterface\t-",
        "MEMBER\tNs.IFoo\tmethod\tBar():System.Void\tabstract",
    ]
    current = [
        "TYPE\tNs.IFoo\tinterface\t-",
        "MEMBER\tNs.IFoo\tmethod\tBar():System.Void\tabstract",
        "MEMBER\tNs.IFoo\tmethod\tBaz():System.Void\tabstract",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "Baz" in report
    assert "interface_new_abstract_member" in report


def test_interface_new_default_implementation_method_is_not_breaking(
    abi_surface_dll: Path, tmp_path: Path
) -> None:
    """接口新增默认实现方法（C# 8+ default interface method，没有 abstract 标记）：既有实现方
    不需要跟着实现新成员，不算破坏。
    """
    baseline = [
        "TYPE\tNs.IFoo\tinterface\t-",
        "MEMBER\tNs.IFoo\tmethod\tBar():System.Void\tabstract",
    ]
    current = [
        "TYPE\tNs.IFoo\tinterface\t-",
        "MEMBER\tNs.IFoo\tmethod\tBar():System.Void\tabstract",
        "MEMBER\tNs.IFoo\tmethod\tBaz():System.Void\t-",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "RESULT=OK" in report


def test_plain_new_member_is_not_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\t-",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\t-",
        "MEMBER\tNs.Foo\tmethod\tQuux():System.Void\t-",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "RESULT=OK" in report
    assert "新增" in report


def test_allowlist_permits_documented_break(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\t-",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\t-",
    ]
    removed_line = "MEMBER\tNs.Foo\tmethod\tBar():System.Void\t-"
    allowlist = [
        "# 测试用例注释行，应被跳过",
        f"{removed_line} | 测试放行示例 | ADR-0099",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current, allowlist_lines=allowlist)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "RESULT=OK" in report
    assert "ALLOWED" in report
    assert "ADR-0099" in report


def test_allowlist_does_not_permit_undocumented_break(abi_surface_dll: Path, tmp_path: Path) -> None:
    """allowlist 里没有覆盖到的破坏项仍然照常判破坏——防止一条宽泛的 allowlist 记录意外放行了
    不相关的签名。
    """
    baseline = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\t-",
        "MEMBER\tNs.Foo\tmethod\tOther():System.Void\t-",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\t-",
        "MEMBER\tNs.Foo\tmethod\tOther():System.Void\t-",
    ]
    allowlist = [
        "MEMBER\tNs.Foo\tmethod\tUnrelated():System.Void\t- | 不相关的放行记录 | ADR-0001",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current, allowlist_lines=allowlist)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "Bar" in report
    assert "RESULT=BREAKING" in report


# -----------------------------------------------------------------------------
# 真实基线端到端：本机有 dist/ws-game-1.12.0.zip 时跑一遍 toolchain/abi_probe.ps1（内部会构建并
# 调用 abi_surface），应 exit 0；zip 不存在则 skip（不是 pass）。Windows-only（依赖 PowerShell）。
# -----------------------------------------------------------------------------

AVAILABLE_POWERSHELL = shutil.which("powershell.exe") or shutil.which("powershell") or shutil.which("pwsh.exe") or shutil.which("pwsh")


@pytest.mark.skipif(sys.platform != "win32", reason="abi_probe.ps1 只在 Windows PowerShell 下运行")
@pytest.mark.skipif(AVAILABLE_POWERSHELL is None, reason="本机找不到 powershell/pwsh")
def test_real_baseline_end_to_end_via_abi_probe(tmp_path: Path) -> None:
    baseline_version_file = REPO_ROOT / "toolchain" / "abi_probe_baseline.txt"
    assert baseline_version_file.is_file()
    baseline_version = baseline_version_file.read_text(encoding="utf-8").strip()
    baseline_zip = REPO_ROOT / "dist" / f"ws-game-{baseline_version}.zip"
    if not baseline_zip.is_file():
        pytest.skip(f"本机没有 dist/ws-game-{baseline_version}.zip，跳过真实基线端到端测试（不是 pass）")

    out_dir = tmp_path / "abi_probe_out"
    result = subprocess.run(
        [
            AVAILABLE_POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ABI_PROBE_SCRIPT),
            "-BaselineVersion", baseline_version,
            "-BaselineZip", str(baseline_zip),
            "-OutDir", str(out_dir),
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=300,
    )
    assert result.returncode == 0, (
        f"abi_probe.ps1 针对真实基线 {baseline_version} 应 exit 0：\n" + result.stdout + result.stderr
    )
    summary = out_dir / "summary.txt"
    assert summary.is_file(), "summary.txt 未生成"
    summary_text = summary.read_text(encoding="utf-8")
    assert "结论=PASS" in summary_text
