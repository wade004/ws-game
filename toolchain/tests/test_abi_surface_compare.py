"""`toolchain/abi_surface` 的 `compare` 子命令回归测试（PJ114-02 根治，外部审计
audit-76d16a5-20260910）：`toolchain/abi_probe/Program.cs` 此前只覆盖人工写死的少量签名——1.14.0
新增的 `SkillHost` 第十八个可选构造参数就漏判了（见 PJ114-01）。`toolchain/abi_surface` 改用
`System.Reflection.MetadataLoadContext` 反射整份 DLL 的公开/受保护 API 表面，本文件不经 dump
（不需要真的编译一份带特定签名的测试 DLL），直接构造 dump 输出格式的纯文本（见
`toolchain/abi_surface/SurfaceDumper.cs` 每种成员的行格式判断记录）覆盖 `compare` 的判定逻辑：
删方法、改参数、改返回类型、删枚举成员、接口新增 abstract 成员（应判破坏）、接口新增默认实现成员
与普通新增（不应判破坏）、allowlist 放行。

ABI-116-01 根治（外部审计 audit-24a11fe-20260910，codex 第十六轮）：此前 dump 不记录方法/构造/
字段/事件/属性访问器的可见性，`public -> protected` 这类收窄改动 dump 输出完全相同、
compare `breaks=0`，而旧 consumer 实际运行会抛 `System.MethodAccessException`（见本文件末尾
`test_public_to_protected_negative_oracle_end_to_end_via_real_dll`，用真实 `dotnet build` 复现
并验证 compare 与运行时结果一致）。修复后 `SurfaceDumper` 把可见性记进 TYPE/MEMBER 行（ctor/
method/field/event 的 flags 首 token；property 沿用既有的 `get:VIS,set:VIS`；TYPE 行 flags 首
token），`SurfaceCompareLogic` 新增"可见性放宽豁免"（放宽不算破坏，收窄/其它 flags 变化仍按原
规则 1 判破坏）——下方补充可见性收窄/放宽（method/ctor/field/event/property/TYPE 六类）、
virtual/abstract 变 sealed-override、实例↔静态、class 变 struct、泛型约束变化、非枚举 const
字段值变化的正负例。

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
# ABI-116-01 根治：可见性收窄/放宽——method/ctor/field/event/property/TYPE 六类，外加"同核对
# 清单里其它容易漏判的项"（virtual/abstract 变 sealed-override、实例↔静态、class 变 struct、泛型
# 约束变化、非枚举 const 字段值变化）。行格式对照 toolchain/abi_surface/SurfaceDumper.cs 当前实现：
# ctor/method/field/event 的 flags 首 token 固定是可见性（public/protected/protected-internal），
# TYPE 行同理（public/nested-public/nested-protected/nested-protected-internal），property 沿用
# 既有 get:VIS,set:VIS[,abstract]。
# -----------------------------------------------------------------------------


def test_visibility_narrowed_method_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\tpublic",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\tprotected",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report
    assert "Bar():System.Void\tpublic" in report


def test_visibility_widened_method_is_not_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """protected -> public 是放宽（调用方能力只增不减），不应判破坏——这正是 ABI-116-01 的负例：
    旧版 dump 不记可见性时，public -> protected 这类收窄同样会被这套"不算破坏"的逻辑误伤（因为
    两次 dump 完全相同），必须先证明放宽方向被正确排除在破坏之外，收窄方向才有意义。
    """
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\tprotected",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\tpublic",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "RESULT=OK" in report
    assert "visibility_widened" in report


def test_visibility_narrowed_constructor_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tctor\t.ctor(System.Int32)\tpublic",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tctor\t.ctor(System.Int32)\tprotected-internal",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_visibility_narrowed_field_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tfield\tCount:System.Int32\tpublic",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tfield\tCount:System.Int32\tprotected",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_visibility_narrowed_event_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tevent\tChanged:System.Action\tpublic",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tevent\tChanged:System.Action\tprotected",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_visibility_narrowed_property_getter_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tValue:System.Int32\tget:public,set:public",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tValue:System.Int32\tget:protected,set:public",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_visibility_widened_property_setter_is_not_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """新增一个 public setter（baseline 没有 set，current 有）：get 不变、set 从 none 放宽为
    public，两个方向都不收窄，不应判破坏。"""
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tValue:System.Int32\tget:public,set:none",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tValue:System.Int32\tget:public,set:public",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "RESULT=OK" in report
    assert "visibility_widened" in report


def test_visibility_narrowed_type_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Outer+Inner\tclass\tnested-public",
        "MEMBER\tNs.Outer+Inner\tmethod\tBar():System.Void\tpublic",
    ]
    current = [
        "TYPE\tNs.Outer+Inner\tclass\tnested-protected",
        "MEMBER\tNs.Outer+Inner\tmethod\tBar():System.Void\tpublic",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_visibility_widened_type_is_not_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Outer+Inner\tclass\tnested-protected",
    ]
    current = [
        "TYPE\tNs.Outer+Inner\tclass\tnested-public",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "RESULT=OK" in report
    assert "visibility_widened" in report


def test_virtual_becomes_sealed_override_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """派生方重写了这个 virtual 方法之后，若基线的 virtual 变成不可再重写（sealed-override 或
    完全去掉 virtual），派生方重编译会报"找不到可重写的成员"——判破坏。"""
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\tpublic,virtual",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\tpublic,sealed-override",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_instance_to_static_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\tpublic",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar():System.Void\tpublic,static",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_class_becomes_struct_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
    ]
    current = [
        "TYPE\tNs.Foo\tstruct\tpublic",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_generic_constraint_change_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Box`1\tclass\tpublic,generic:1",
    ]
    current = [
        "TYPE\tNs.Box`1\tclass\tpublic,generic:1,constraints:!0:class",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_const_field_value_change_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """非枚举 const 字段的值在编译期被内联进调用方 IL——值变化即使字段的类型签名没变，旧 consumer
    不重新编译就用的还是旧值，属于契约破坏。"""
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tfield\tMaxCount:System.Int32=5\tpublic,literal",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tfield\tMaxCount:System.Int32=6\tpublic,literal",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


# -----------------------------------------------------------------------------
# ABI-1162-01 根治（codex 第十七轮，audit-4faab73-20260910）：属性签名此前只编码
# `Name:PropertyType`，不含 `PropertyInfo.GetIndexParameters()`——public indexer 的索引参数类型
# 变化（如 `this[int]` 改 `this[string]`）dump 前后逐字节相同，`breaks=0`，但旧编译消费方运行期
# `MissingMethodException`（见 delivery/run5-console.log 30-39 行、AUDIT_REPORT.md ABI-1162-01）。
# 顺带核查同一批容易漏判的签名维度：运算符重载/转换运算符（此前被 IsSpecialName 整体跳过）、
# 事件 remove 访问器可见性（此前只记 add 半边）——这两项确认是真实的二进制破坏缺口，补齐编码。
#
# 另外两个维度（方法/索引器参数的 `params` 数组修饰、可选参数默认值存在性/取值）核查后确认
# **不**编码，与 ref/out/in 同一条既有设计（TypeNameFormatter.FormatParameters 判断记录）：
# 三者都是 C# 编译器侧语法糖，不改变 IL 物理签名。曾经短暂编码过这两项，但用
# `dist/ws-game-1.12.0.zip` 基线重跑当前工作树六个 DLL 时产生 5 处假破坏——框架给公开构造函数
# 新增尾部可选参数时的既有模式是"新增一个更长的可选参数重载，同时保留原始定长参数表的旧重载
# （旧重载参数改为不带默认值，仅作为物理兼容 shim）"，旧编译调用方物理上调用的正是这个保留的
# 定长重载，从未因为它是否声明默认值而受影响；按值/存在性编码后误将这种合法模式判成破坏。见
# `TypeNameFormatter.FormatParameters` 判断记录与下方
# `test_optional_default_value_or_params_modifier_change_alone_is_not_breaking`。
# -----------------------------------------------------------------------------


def test_indexer_parameter_type_change_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """ABI-1162-01 核心场景：public indexer `this[int]` 改 `this[string]`。"""
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tItem[System.Int32]:System.String\tget:public,set:public",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tItem[System.String]:System.String\tget:public,set:public",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report
    assert "Item[System.Int32]" in report


def test_indexer_new_overload_is_not_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """新增一个索引参数类型不同的重载（`this[int]` 保留、新增 `this[string]`）：旧签名仍在，纯新增
    不算破坏。"""
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tItem[System.Int32]:System.String\tget:public,set:public",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tItem[System.Int32]:System.String\tget:public,set:public",
        "MEMBER\tNs.Foo\tproperty\tItem[System.String]:System.String\tget:public,set:public",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "RESULT=OK" in report
    assert "新增" in report


def test_indexer_removed_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tproperty\tItem[System.Int32]:System.String\tget:public,set:public",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_ref_out_modifier_change_alone_is_not_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """确认性正例：ref/out/in 三者物理参数类型都是同一个 byref 类型 `T&`，签名文本本身不区分
    ref/out/in，两次 dump 对同一个 `ref int`/`out int` 参数产出完全相同的行，不应判破坏。"""
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar(System.Int32&):System.Void\tpublic",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\tBar(System.Int32&):System.Void\tpublic",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "RESULT=OK" in report


def test_params_and_optional_default_value_are_not_encoded_in_real_dump(
    abi_surface_dll: Path, tmp_path: Path
) -> None:
    """核查结论（不编码）的真实反射验证：`params` 修饰、可选参数默认值存在性/取值与 ref/out/in
    同属 C# 编译器语法糖，不改变 IL 物理签名，故意不编码进签名文本。用手写文本行无法验证"真的没有
    编码"（手写行本来就是想写什么就写什么），必须真实编译一个带 `params` 与可选参数默认值的库、
    走真实 `Dump()` 反射，确认输出行不含 `!params`/`!opt` 这类标记，且默认值从 1 改成 2 后 dump
    文本不变——证明这不是"忘记加标记"而是有意不区分。

    背景：曾经短暂编码过这两项，但用 `dist/ws-game-1.12.0.zip` 基线重跑当前工作树六个 DLL 时
    产生 5 处假破坏（`FieldSchema`/`EconomyContentValidationRule`/`LootContentValidationRule`/
    `SkillHost`/`ViewBinder` 的公开构造函数新增尾部可选参数时，框架统一保留原始定长参数表的旧
    重载作为物理兼容 shim，该旧重载改为不带默认值——旧编译调用方物理上调用的正是这个保留的
    重载，从未因为默认值存在与否而失败）。见 `TypeNameFormatter.FormatParameters` 判断记录。
    """
    proj_root = tmp_path / "params_opt_check"
    src_v1 = proj_root / "V1"
    src_v2 = proj_root / "V2"
    for d in (src_v1, src_v2):
        d.mkdir(parents=True)

    csproj_lib = (
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        "  <PropertyGroup>\n"
        "    <TargetFramework>netstandard2.1</TargetFramework>\n"
        "    <AssemblyName>ParamsOptContract</AssemblyName>\n"
        "    <Nullable>enable</Nullable>\n"
        "  </PropertyGroup>\n"
        "</Project>\n"
    )
    (src_v1 / "V1.csproj").write_text(csproj_lib, encoding="utf-8")
    (src_v2 / "V2.csproj").write_text(csproj_lib, encoding="utf-8")
    (src_v1 / "Api.cs").write_text(
        "namespace ParamsOptNs { public class Foo {\n"
        "    public void Bar(params int[] xs) { }\n"
        "    public void Baz(int x = 1) { }\n"
        "} }\n",
        encoding="utf-8",
    )
    (src_v2 / "Api.cs").write_text(
        "namespace ParamsOptNs { public class Foo {\n"
        "    public void Bar(int[] xs) { }\n"
        "    public void Baz(int x = 2) { }\n"
        "} }\n",
        encoding="utf-8",
    )

    build_v1_dir = proj_root / "build" / "v1"
    build_v2_dir = proj_root / "build" / "v2"
    for csproj, out_dir in ((src_v1 / "V1.csproj", build_v1_dir), (src_v2 / "V2.csproj", build_v2_dir)):
        r = subprocess.run(
            [DOTNET, "build", str(csproj), "-c", "Release", "--nologo", "-o", str(out_dir)],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
        )
        assert r.returncode == 0, "params/opt 核查库构建失败：\n" + r.stdout + r.stderr

    v1_dump = tmp_path / "v1-dump.txt"
    v2_dump = tmp_path / "v2-dump.txt"
    for dll_path, out_path in ((build_v1_dir / "ParamsOptContract.dll", v1_dump), (build_v2_dir / "ParamsOptContract.dll", v2_dump)):
        r = subprocess.run(
            [DOTNET, str(abi_surface_dll), "dump", "--out", str(out_path), str(dll_path)],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
        )
        assert r.returncode == 0, "abi_surface dump 失败：\n" + r.stdout + r.stderr

    v1_text = v1_dump.read_text(encoding="utf-8")
    v2_text = v2_dump.read_text(encoding="utf-8")
    assert "!params" not in v1_text, "`params` 修饰不应被编码进 dump 行：\n" + v1_text
    assert "!opt" not in v1_text, "可选参数默认值不应被编码进 dump 行：\n" + v1_text
    assert "Bar(System.Int32[]):System.Void" in v1_text
    assert "Baz(System.Int32):System.Void" in v1_text
    assert v1_text == v2_text, (
        "去掉 params 修饰、把默认值从 1 改成 2 都不应该改变 dump 文本（两者都是编译器语法糖，"
        "不改变 IL 物理签名）：\nv1=\n" + v1_text + "\nv2=\n" + v2_text
    )


def test_operator_overload_removed_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """运算符重载/转换运算符：此前 `IsSpecialName` 整体跳过 method 循环，`op_Addition`/
    `op_Implicit` 等完全不进 dump，删除或改签名都不会被 compare 抓到。"""
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\top_Addition(Ns.Foo,Ns.Foo):Ns.Foo\tpublic,static",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report
    assert "op_Addition" in report


def test_conversion_operator_signature_change_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\top_Implicit(Ns.Foo):System.Int32\tpublic,static",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tmethod\top_Implicit(Ns.Foo):System.Int64\tpublic,static",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_event_remove_accessor_narrowed_is_breaking(abi_surface_dll: Path, tmp_path: Path) -> None:
    """事件 remove 访问器单独收窄（add 仍 public）：此前 dump 只记 add 半边可见性，这类收窄两次
    dump 输出相同，旧消费方 `obj.Event -= handler;` 运行期 `MissingMethodException`。"""
    baseline = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tevent\tChanged:System.Action\tpublic,remove:public",
    ]
    current = [
        "TYPE\tNs.Foo\tclass\tpublic",
        "MEMBER\tNs.Foo\tevent\tChanged:System.Action\tpublic,remove:protected",
    ]
    result, report = _run_compare(abi_surface_dll, tmp_path, baseline, current)
    assert result.returncode == 2, result.stdout + result.stderr
    assert "RESULT=BREAKING" in report


def test_indexer_parameter_type_change_negative_oracle_end_to_end_via_real_dll(
    abi_surface_dll: Path, tmp_path: Path
) -> None:
    """ABI-1162-01 端到端负例：真实编译 baseline（`public string this[int i]`）与 current（同一
    indexer 改成 `this[string i]`）两份库，一个针对 baseline 编译好、不重新编译的旧 consumer 直接
    通过索引器读值——运行期必须 `MissingMethodException`（钉死这确实是一次二进制破坏，复现
    delivery/run5-console.log 的 indexer oracle），同时修复后的 `abi_surface dump`+`compare` 必须
    给出 `breaks>0`（RESULT=BREAKING）。"""
    proj_root = tmp_path / "indexer_oracle"
    baseline_src = proj_root / "ApiBaseline"
    current_src = proj_root / "ApiCurrent"
    consumer_src = proj_root / "Consumer"
    for d in (baseline_src, current_src, consumer_src):
        d.mkdir(parents=True)

    csproj_lib = (
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        "  <PropertyGroup>\n"
        "    <TargetFramework>netstandard2.1</TargetFramework>\n"
        "    <AssemblyName>IndexerContract</AssemblyName>\n"
        "    <Nullable>enable</Nullable>\n"
        "  </PropertyGroup>\n"
        "</Project>\n"
    )
    (baseline_src / "IndexerBaseline.csproj").write_text(csproj_lib, encoding="utf-8")
    (current_src / "IndexerCurrent.csproj").write_text(csproj_lib, encoding="utf-8")
    (baseline_src / "Api.cs").write_text(
        "namespace IndexerOracleNs { public class Foo { public string this[int i] => \"v\" + i; } }\n",
        encoding="utf-8",
    )
    (current_src / "Api.cs").write_text(
        "namespace IndexerOracleNs { public class Foo { public string this[string i] => \"v\" + i; } }\n",
        encoding="utf-8",
    )
    (consumer_src / "Consumer.csproj").write_text(
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        "  <PropertyGroup>\n"
        "    <OutputType>Exe</OutputType>\n"
        "    <TargetFramework>net8.0</TargetFramework>\n"
        "    <Nullable>enable</Nullable>\n"
        "  </PropertyGroup>\n"
        "  <ItemGroup>\n"
        "    <Reference Include=\"IndexerContract\"><HintPath>lib/IndexerContract.dll</HintPath></Reference>\n"
        "  </ItemGroup>\n"
        "</Project>\n",
        encoding="utf-8",
    )
    (consumer_src / "Program.cs").write_text(
        "using IndexerOracleNs;\n"
        "class Program\n"
        "{\n"
        "    static int Main()\n"
        "    {\n"
        "        var v = new Foo()[1];\n"
        "        System.Console.WriteLine(\"ORACLE_CONSUMER_OK:\" + v);\n"
        "        return 0;\n"
        "    }\n"
        "}\n",
        encoding="utf-8",
    )

    build_baseline_dir = proj_root / "build" / "baseline"
    build_current_dir = proj_root / "build" / "current"
    for csproj, out_dir in (
        (baseline_src / "IndexerBaseline.csproj", build_baseline_dir),
        (current_src / "IndexerCurrent.csproj", build_current_dir),
    ):
        r = subprocess.run(
            [DOTNET, "build", str(csproj), "-c", "Release", "--nologo", "-o", str(out_dir)],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
        )
        assert r.returncode == 0, "indexer oracle 库构建失败：\n" + r.stdout + r.stderr

    consumer_lib = consumer_src / "lib"
    consumer_lib.mkdir()
    shutil.copy(build_baseline_dir / "IndexerContract.dll", consumer_lib / "IndexerContract.dll")
    consumer_out = proj_root / "consumer-bin"
    r = subprocess.run(
        [DOTNET, "build", str(consumer_src / "Consumer.csproj"), "-c", "Release", "--nologo", "-o", str(consumer_out)],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
    )
    assert r.returncode == 0, "indexer oracle consumer 构建失败：\n" + r.stdout + r.stderr

    consumer_dll = consumer_out / "Consumer.dll"
    shutil.copy(build_baseline_dir / "IndexerContract.dll", consumer_out / "IndexerContract.dll")
    r = subprocess.run(
        [DOTNET, str(consumer_dll)], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
    )
    assert r.returncode == 0 and "ORACLE_CONSUMER_OK" in r.stdout, (
        "indexer oracle consumer 针对基线 DLL 自检应成功：\n" + r.stdout + r.stderr
    )

    shutil.copy(build_current_dir / "IndexerContract.dll", consumer_out / "IndexerContract.dll")
    r = subprocess.run(
        [DOTNET, str(consumer_dll)], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
    )
    assert r.returncode != 0, "换上 string 索引器版本后旧 consumer 应运行失败（MissingMethodException），实际却成功了"
    assert "MissingMethodException" in (r.stdout + r.stderr), (
        "期望 MissingMethodException，实际输出：\n" + r.stdout + r.stderr
    )

    baseline_dump = tmp_path / "indexer-baseline.txt"
    current_dump = tmp_path / "indexer-current.txt"
    for dll_path, out_path in (
        (build_baseline_dir / "IndexerContract.dll", baseline_dump),
        (build_current_dir / "IndexerContract.dll", current_dump),
    ):
        r = subprocess.run(
            [DOTNET, str(abi_surface_dll), "dump", "--out", str(out_path), str(dll_path)],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
        )
        assert r.returncode == 0, "abi_surface dump 失败：\n" + r.stdout + r.stderr

    baseline_dump_text = baseline_dump.read_text(encoding="utf-8")
    current_dump_text = current_dump.read_text(encoding="utf-8")
    assert "Item[System.Int32]" in baseline_dump_text, (
        "根治后 dump 必须把索引参数编进属性签名：\n" + baseline_dump_text
    )
    assert "Item[System.String]" in current_dump_text
    assert baseline_dump_text != current_dump_text, "修复前的症状：索引参数变化前后 dump 逐字节相同"

    report_path = tmp_path / "indexer-report.txt"
    r = subprocess.run(
        [DOTNET, str(abi_surface_dll), "compare", str(baseline_dump), str(current_dump), "--out", str(report_path)],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
    )
    report_text = report_path.read_text(encoding="utf-8") if report_path.is_file() else ""
    assert r.returncode == 2, (
        "修复后 compare 必须判定 breaks>0（旧 consumer 已实测 MissingMethodException）：\n" + report_text
    )
    assert "RESULT=BREAKING" in report_text
    assert "Item" in report_text


def test_public_to_protected_negative_oracle_end_to_end_via_real_dll(
    abi_surface_dll: Path, tmp_path: Path
) -> None:
    """ABI-116-01 端到端负例（audit-24a11fe-20260910 codex 第十六轮最小 oracle）：真实编译一个
    baseline 库（`public void Bar()`）与一份仅把该方法改成 `protected` 的 current 库，一个针对
    baseline 编译好、不重新编译的旧 consumer 直接调用 `Bar()`——运行期必须抛
    `System.MethodAccessException`（验证这确实是一次二进制破坏，不是本测试臆造的场景），同时
    `abi_surface dump`+`compare` 针对同一对 DLL 必须给出 `breaks>0`（RESULT=BREAKING）。两者在
    修复前不一致（旧 consumer 崩、compare 却 breaks=0）；本用例把这个不一致钉成回归测试。
    """
    proj_root = tmp_path / "oracle"
    baseline_src = proj_root / "ApiBaseline"
    current_src = proj_root / "ApiCurrent"
    consumer_src = proj_root / "Consumer"
    for d in (baseline_src, current_src, consumer_src):
        d.mkdir(parents=True)

    csproj_lib = (
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        "  <PropertyGroup>\n"
        "    <TargetFramework>netstandard2.1</TargetFramework>\n"
        "    <AssemblyName>ApiContract</AssemblyName>\n"
        "    <Nullable>enable</Nullable>\n"
        "  </PropertyGroup>\n"
        "</Project>\n"
    )
    (baseline_src / "ApiBaseline.csproj").write_text(csproj_lib, encoding="utf-8")
    (current_src / "ApiCurrent.csproj").write_text(csproj_lib, encoding="utf-8")
    (baseline_src / "Api.cs").write_text(
        "namespace OracleNs { public class Foo { public void Bar() { } } }\n", encoding="utf-8"
    )
    (current_src / "Api.cs").write_text(
        "namespace OracleNs { public class Foo { protected void Bar() { } } }\n", encoding="utf-8"
    )
    (consumer_src / "Consumer.csproj").write_text(
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        "  <PropertyGroup>\n"
        "    <OutputType>Exe</OutputType>\n"
        "    <TargetFramework>net8.0</TargetFramework>\n"
        "    <Nullable>enable</Nullable>\n"
        "  </PropertyGroup>\n"
        "  <ItemGroup>\n"
        "    <Reference Include=\"ApiContract\"><HintPath>lib/ApiContract.dll</HintPath></Reference>\n"
        "  </ItemGroup>\n"
        "</Project>\n",
        encoding="utf-8",
    )
    (consumer_src / "Program.cs").write_text(
        "using OracleNs;\n"
        "class Program\n"
        "{\n"
        "    static int Main()\n"
        "    {\n"
        "        new Foo().Bar();\n"
        "        System.Console.WriteLine(\"ORACLE_CONSUMER_OK\");\n"
        "        return 0;\n"
        "    }\n"
        "}\n",
        encoding="utf-8",
    )

    build_baseline_dir = proj_root / "build" / "baseline"
    build_current_dir = proj_root / "build" / "current"
    for csproj, out_dir in (
        (baseline_src / "ApiBaseline.csproj", build_baseline_dir),
        (current_src / "ApiCurrent.csproj", build_current_dir),
    ):
        r = subprocess.run(
            [DOTNET, "build", str(csproj), "-c", "Release", "--nologo", "-o", str(out_dir)],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
        )
        assert r.returncode == 0, "oracle 库构建失败：\n" + r.stdout + r.stderr

    consumer_lib = consumer_src / "lib"
    consumer_lib.mkdir()
    shutil.copy(build_baseline_dir / "ApiContract.dll", consumer_lib / "ApiContract.dll")
    consumer_out = proj_root / "consumer-bin"
    r = subprocess.run(
        [DOTNET, "build", str(consumer_src / "Consumer.csproj"), "-c", "Release", "--nologo", "-o", str(consumer_out)],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
    )
    assert r.returncode == 0, "oracle consumer 构建失败：\n" + r.stdout + r.stderr

    consumer_dll = consumer_out / "Consumer.dll"
    shutil.copy(build_baseline_dir / "ApiContract.dll", consumer_out / "ApiContract.dll")
    r = subprocess.run(
        [DOTNET, str(consumer_dll)], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
    )
    assert r.returncode == 0 and "ORACLE_CONSUMER_OK" in r.stdout, (
        "oracle consumer 针对基线 DLL 自检应成功：\n" + r.stdout + r.stderr
    )

    shutil.copy(build_current_dir / "ApiContract.dll", consumer_out / "ApiContract.dll")
    r = subprocess.run(
        [DOTNET, str(consumer_dll)], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
    )
    assert r.returncode != 0, "换上 protected 版本后旧 consumer 应运行失败（MethodAccessException），实际却成功了"
    assert "MethodAccessException" in (r.stdout + r.stderr), (
        "期望 MethodAccessException，实际输出：\n" + r.stdout + r.stderr
    )

    baseline_dump = tmp_path / "oracle-baseline.txt"
    current_dump = tmp_path / "oracle-current.txt"
    for dll_path, out_path in (
        (build_baseline_dir / "ApiContract.dll", baseline_dump),
        (build_current_dir / "ApiContract.dll", current_dump),
    ):
        r = subprocess.run(
            [DOTNET, str(abi_surface_dll), "dump", "--out", str(out_path), str(dll_path)],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
        )
        assert r.returncode == 0, "abi_surface dump 失败：\n" + r.stdout + r.stderr

    report_path = tmp_path / "oracle-report.txt"
    r = subprocess.run(
        [DOTNET, str(abi_surface_dll), "compare", str(baseline_dump), str(current_dump), "--out", str(report_path)],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
    )
    report_text = report_path.read_text(encoding="utf-8") if report_path.is_file() else ""
    assert r.returncode == 2, (
        "修复后 compare 必须判定 breaks>0（旧 consumer 已实测 MethodAccessException）：\n" + report_text
    )
    assert "RESULT=BREAKING" in report_text
    assert "Bar" in report_text


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
