"""``toolchain`` 生成器脚本写文本文件时都传了 ``newline="\\n"`` 的静态回归测试（消费方反馈 E7
根治，2026-09-10，见 architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E7）。

背景：``Path.write_text(...)``/内置 ``open(..., "w", ...)`` 在没有显式 ``newline=`` 参数时，
Python 会做"universal newline"写入转换——字符串里的 ``\n`` 在 Windows 上会被转成平台默认的
``os.linesep``（即 ``\r\n``）再写入磁盘，与仓库约定的"生成的文本文件一律 LF"冲突。复现：
``toolchain/gen_placeholder_assets.py`` 有 6 处 ``write_text(...)`` 调用漏传 ``newline="\n"``
（``MANIFEST.json``、两处 ``anchors.json``、``frames.json``、两处 ``README.md``），Windows 上跑
该脚本会现场写出 CRLF 字节（``.gitattributes`` 里 ``*.json``/``*.md`` 的 ``text eol=lf`` 声明会在
``git add``/提交时把 CRLF 规范化回 LF，因此单看 ``git ls-files --eol`` 看不出这个源头问题——但脚本
生成的原始字节、以及任何不经过 git 直接读取这些文件的下游工具，仍会看到 CRLF）。

根治：``toolchain/gen_placeholder_assets.py`` 的全部 6 处 ``write_text`` 调用已补上
``newline="\\n"``；``toolchain/asset_import/common.py`` 的两个共享写入函数
（``write_envelope``/``write_json_pretty``）与 ``toolchain/gen_event_constants.py`` 本来就已经
正确传了该参数。本文件对 ``toolchain/`` 下的生成器脚本做静态扫描（不依赖真的跑一遍脚本、不依赖
运行平台是否为 Windows），断言任何写文本文件的调用（``.write_text(...)`` 方法调用、内置 ``open``
以写模式打开）都显式传了 ``newline="\\n"``——防止未来新增/修改生成器时再次遗漏。

运行：``python -m pytest toolchain/tests/test_generators_write_lf.py -q`` 或
``python -m pytest toolchain/tests -q``。跨平台可跑（纯 AST 静态分析）。
"""

from __future__ import annotations

import ast
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]

# 判断记录：只扫描"生成器"脚本本身（写数据/文档/常量文件到仓库的工具），不扫描 tests/ 下的测试
# 代码（测试代码写临时文件用于断言，不是"生成随仓库分发的文本产物"，newline 策略不受本规则约束）、
# 不扫描 registry/（私服基础设施脚本，写的是运行期配置/日志，不是随仓库分发的内容产物）、不扫描
# __pycache__（编译产物）。
EXCLUDED_DIR_NAMES = {"tests", "registry", "__pycache__", ".venv"}


def _iter_generator_scripts() -> list[Path]:
    scripts: list[Path] = []
    for path in TOOLCHAIN_DIR.rglob("*.py"):
        if any(part in EXCLUDED_DIR_NAMES for part in path.relative_to(TOOLCHAIN_DIR).parts[:-1]):
            continue
        scripts.append(path)
    return sorted(scripts)


def _has_newline_kwarg(call: ast.Call) -> bool:
    return any(kw.arg == "newline" for kw in call.keywords)


def _open_call_is_write_mode(call: ast.Call) -> bool:
    """粗略判断一个 open(...) 调用是否以写模式打开：位置参数第二个、或 mode= 关键字参数，
    取值字符串里含 'w'/'a'/'x'（不区分是否同时有 'b'——'wb' 这类二进制模式本身就不该传
    newline，不在本规则约束范围，调用方需要自行用字符串是否含 'b' 排除，见下方过滤）。
    """
    mode_str = None
    if len(call.args) >= 2 and isinstance(call.args[1], ast.Constant) and isinstance(call.args[1].value, str):
        mode_str = call.args[1].value
    else:
        for kw in call.keywords:
            if kw.arg == "mode" and isinstance(kw.value, ast.Constant) and isinstance(kw.value.value, str):
                mode_str = kw.value.value
    if mode_str is None:
        return False
    if "b" in mode_str:
        return False  # 二进制模式，不适用本规则。
    return any(m in mode_str for m in ("w", "a", "x"))


def _find_violations(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    try:
        tree = ast.parse(text, filename=str(path))
    except SyntaxError:
        return []

    violations: list[str] = []
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue

        # 情形一：<expr>.write_text(...) 方法调用（Path.write_text 惯用写法）。
        if isinstance(node.func, ast.Attribute) and node.func.attr == "write_text":
            if not _has_newline_kwarg(node):
                violations.append(f"{path.relative_to(TOOLCHAIN_DIR)}:{node.lineno}: write_text(...) 缺少 newline=\"\\n\"")
            continue

        # 情形二：内置 open(...)（含 <expr>.open(...)，如 Path(...).open(...)）以文本写模式打开。
        func_name = None
        if isinstance(node.func, ast.Name):
            func_name = node.func.id
        elif isinstance(node.func, ast.Attribute):
            func_name = node.func.attr
        if func_name == "open" and _open_call_is_write_mode(node):
            if not _has_newline_kwarg(node):
                violations.append(f"{path.relative_to(TOOLCHAIN_DIR)}:{node.lineno}: open(..., 写模式) 缺少 newline=\"\\n\"")

    return violations


@pytest.mark.parametrize("script_path", _iter_generator_scripts(), ids=lambda p: str(p.relative_to(TOOLCHAIN_DIR)))
def test_generator_script_text_writes_pass_newline_lf(script_path: Path) -> None:
    violations = _find_violations(script_path)
    assert not violations, (
        "以下写文本文件的调用缺少 newline=\"\\n\"（Windows 上会写出 CRLF，见文件头判断记录 E7）：\n"
        + "\n".join(violations)
    )


if __name__ == "__main__":
    import sys

    sys.exit(pytest.main([__file__, "-q"]))
