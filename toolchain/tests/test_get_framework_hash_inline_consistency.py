"""``toolchain/get_framework.ps1`` 内联的 ``Get-Sha256FileHash`` 函数体与
``toolchain/_hash.ps1`` 原版保持一致的静态回归测试（消费方反馈 E2 根治，2026-09-10，见
architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E2）。

背景：``get_framework.ps1`` 此前 dot-source 同目录 ``_hash.ps1`` 加载 ``Get-Sha256FileHash``
共享函数——但游戏仓库侧引用本脚本的典型方式是"只下载 get_framework.ps1 单个文件"（GitHub Release
附件里单独下载；本脚本自身尚未落地前，也无法先跑它去拉取 ``toolchain/`` 整个目录），形成"跑本脚本前
得先有 ``_hash.ps1``，但拿到 ``_hash.ps1`` 的唯一途径是先用本脚本拉取"的引导死锁。

根治：把 ``Get-Sha256FileHash`` 函数体原样内联进 ``get_framework.ps1``（``# BEGIN INLINE
Get-Sha256FileHash`` / ``# END INLINE Get-Sha256FileHash`` 标记之间），``toolchain/_hash.ps1``
本身继续保留供仓库内其它脚本（``build.ps1``、``sync_package_content.ps1``）共用。两处函数体今后
若各自独立演进（例如只改了其中一处的兜底逻辑而忘了同步另一处），会产生"两条本该完全等价的哈希计算
路径实际行为不同"这类难以察觉的缺陷——本文件做纯文本静态比对（提取两处 ``function
Get-Sha256FileHash { ... }`` 代码块并归一化空白后逐字比较），把这类遗漏在提交时就拦下来，不依赖
真实跑一遍两条路径去比较哈希结果（那样测不出"代码本身是否同步"，只能测出"当前这次输入恰好两边算出
同一个值"）。

运行：``python -m pytest toolchain/tests/test_get_framework_hash_inline_consistency.py -q`` 或
``python -m pytest toolchain/tests -q``。跨平台可跑（纯文本比对，不依赖 PowerShell 宿主）。
"""

from __future__ import annotations

import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HASH_SCRIPT_PATH = REPO_ROOT / "toolchain" / "_hash.ps1"
GET_FRAMEWORK_PATH = REPO_ROOT / "toolchain" / "get_framework.ps1"

FUNCTION_BLOCK_RE = re.compile(
    r"function\s+Get-Sha256FileHash\s*\{.*?\n\}", re.DOTALL,
)

INLINE_MARKER_RE = re.compile(
    r"# BEGIN INLINE Get-Sha256FileHash.*?\n(function\s+Get-Sha256FileHash\s*\{.*?\n\})"
    r"\n# END INLINE Get-Sha256FileHash",
    re.DOTALL,
)


def _normalize(text: str) -> str:
    """归一化空白差异（行尾空格、CRLF/LF、连续空行数量），只比较真正的代码/注释内容。"""
    lines = [line.rstrip() for line in text.replace("\r\n", "\n").split("\n")]
    return "\n".join(lines).strip()


def _extract_hash_ps1_function() -> str:
    text = HASH_SCRIPT_PATH.read_text(encoding="utf-8")
    match = FUNCTION_BLOCK_RE.search(text)
    assert match is not None, (
        f"{HASH_SCRIPT_PATH} 里找不到 'function Get-Sha256FileHash {{ ... }}' 代码块——"
        "该函数是否被重命名/删除？本测试的正则假设需要同步更新。"
    )
    return match.group(0)


def _extract_get_framework_inline_function() -> str:
    text = GET_FRAMEWORK_PATH.read_text(encoding="utf-8")
    match = INLINE_MARKER_RE.search(text)
    assert match is not None, (
        f"{GET_FRAMEWORK_PATH} 里找不到 '# BEGIN INLINE Get-Sha256FileHash' / "
        "'# END INLINE Get-Sha256FileHash' 标记之间的函数体——消费方反馈 E2 根治要求 "
        "get_framework.ps1 内联一份与 toolchain/_hash.ps1 逐字节一致的 Get-Sha256FileHash "
        "函数体，标记是否被误删/改名？"
    )
    return match.group(1)


def test_get_framework_ps1_is_self_contained_no_hash_ps1_dot_source() -> None:
    """get_framework.ps1 不应再 dot-source 同目录 _hash.ps1（消费方反馈 E2 根治点本身：
    单独下载本脚本一个文件即可使用，不依赖同目录任何其它文件）。
    """
    text = GET_FRAMEWORK_PATH.read_text(encoding="utf-8")
    assert ". $hashScriptPath" not in text
    assert '". (Join-Path $PSScriptRoot "_hash.ps1")"' not in text
    assert "Join-Path $PSScriptRoot \"_hash.ps1\"" not in text, (
        "get_framework.ps1 不应再引用同目录 _hash.ps1（应改为内联函数体，见文件头判断记录）"
    )


def test_inlined_function_body_matches_hash_ps1() -> None:
    """get_framework.ps1 内联的 Get-Sha256FileHash 函数体应与 toolchain/_hash.ps1 原版逐字一致
    （归一化行尾空白/换行符后比较）。
    """
    hash_ps1_body = _normalize(_extract_hash_ps1_function())
    inline_body = _normalize(_extract_get_framework_inline_function())

    assert inline_body == hash_ps1_body, (
        "get_framework.ps1 内联的 Get-Sha256FileHash 函数体与 toolchain/_hash.ps1 不一致——"
        "两处本该是同一份实现的两份拷贝，任一处改动后必须同步另一处（见 E2 判断记录）。\n\n"
        f"--- toolchain/_hash.ps1 ---\n{hash_ps1_body}\n\n"
        f"--- get_framework.ps1 内联段 ---\n{inline_body}\n"
    )


if __name__ == "__main__":
    import sys
    import pytest

    sys.exit(pytest.main([__file__, "-q"]))
