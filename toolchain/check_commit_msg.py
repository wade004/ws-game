#!/usr/bin/env python3
"""提交信息乱码拦截（`.githooks/commit-msg` 调用）。

背景：Bash/PowerShell 工具下 `git commit -m "中文"` 会按系统代码页（GBK）把字节写进提交信息，入库后是非法 UTF-8
（`git log` 显示成一串 U+FFFD），提交一旦产生历史无法订正（4b4abb55 是实例）。本脚本在提交产生之前拒绝：

1. 非法 UTF-8 字节；
2. 含 U+FFFD 替换字符；
3. 典型误解码特征：把 UTF-8 中文字节当 GBK / Latin-1 / cp1252 解码后再存成 UTF-8 的"锟斤拷"类文本
   （逆向重编码后恰好是合法 UTF-8 且含汉字）；
4. 中文被替换成问号：同一行里成串的 ``??``（反引号包起来的代码片段、``??=`` 与 ``?? `` 空合并运算符除外）。

用法：``python check_commit_msg.py <提交信息文件>``，通过退出 0，拒绝退出 1。纯函数 :func:`find_problems`
供测试直接调用。
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

HINT = "把提交信息先写进 UTF-8 文件，再 git commit -F <文件> -- <路径…>（不要在命令行里用 -m 写中文）。"

_CODE_SPAN = re.compile(r"`[^`]*`")
_NULL_COALESCING = re.compile(r"\?\?=|(?<=[\w)\]]) \?\? (?=[\w(\[])")
_QUESTION_RUN = re.compile(r"\?{2,}")
_REVERSE_ENCODINGS = ("gbk", "cp1252", "latin-1")


def _has_cjk(text: str) -> bool:
    return any("一" <= ch <= "鿿" for ch in text)


def _looks_like_mojibake(line: str) -> str | None:
    """行内至少 2 个非 ASCII 字符，且按某个单字节/GBK 编码还原字节后恰好是含汉字的合法 UTF-8，返回还原结果。"""
    if sum(1 for ch in line if ord(ch) > 127) < 2:
        return None
    for enc in _REVERSE_ENCODINGS:
        try:
            fixed = line.encode(enc).decode("utf-8")
        except (UnicodeEncodeError, UnicodeDecodeError):
            continue
        if fixed != line and _has_cjk(fixed):
            return fixed
    return None


def _question_mark_garbling(line: str) -> bool:
    stripped = _CODE_SPAN.sub("", line)
    stripped = _NULL_COALESCING.sub(" ", stripped)
    runs = _QUESTION_RUN.findall(stripped)
    if any(len(run) >= 3 for run in runs):
        return True
    return len(runs) >= 2


def find_problems(raw: bytes) -> list[str]:
    """返回问题描述列表；空列表 = 通过。"""
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as exc:
        return [f"提交信息不是合法 UTF-8（字节偏移 {exc.start} 处：{exc.reason}）——多半是命令行 -m 按 GBK 写入的中文"]
    problems: list[str] = []
    if "�" in text:
        problems.append("提交信息含替换字符 U+FFFD——中文已经在到达 git 之前被损坏")
    for number, line in enumerate(text.splitlines(), start=1):
        if "�" in line:
            continue
        fixed = _looks_like_mojibake(line)
        if fixed is not None:
            problems.append(f"第 {number} 行是典型的 GBK/Latin-1 误解码乱码（还原后应为：{fixed.strip()[:60]}）")
        elif _question_mark_garbling(line):
            problems.append(f"第 {number} 行含成串问号，像是中文被替换成了 ?")
    return problems


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("用法：check_commit_msg.py <提交信息文件>", file=sys.stderr)
        return 2
    raw = Path(argv[1]).read_bytes()
    problems = find_problems(raw)
    if not problems:
        return 0
    print("[commit-msg] 提交信息疑似乱码，已拒绝提交：", file=sys.stderr)
    for problem in problems:
        print(f"  - {problem}", file=sys.stderr)
    print(f"[commit-msg] {HINT}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    # Windows 控制台默认代码页下，中文提示可能无法编码：stderr 一律按 UTF-8 输出。
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main(sys.argv))
