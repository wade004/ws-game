"""``toolchain/_upm_evidence.ps1``（包管理器子进程中途消失时的失败现场自动抓取）的回归测试
（2026-10-01，bugfix/upm-evidence-stale-pid）。

背景：门禁里的 Unity 批处理偶发几秒内退出，引擎日志含 ``IPC stream failed to read (Not connected)`` /
``Failed to resolve packages: operation cancelled.``——包管理器子进程（UnityPackageManager.exe）在解析
途中消失，但 ``upm.log`` 与子进程退出码会被下一次运行覆盖，事后无从对证。抓取函数在引擎退出码非零且
日志含该签名时，立即把 ``upm.log``、引擎日志片段、子进程退出码存进门禁产物目录并给步骤 Detail 一行摘要。

用例分层（AGENTS.md：复现 + 不变量各至少一条）：

- 复现：构造含签名的引擎日志 + 假 ``upm.log``，断言抓取函数复制了文件、摘要含退出码与留证目录；退出码
  按 Unity 6 实测的"反引号 + 无符号 32 位"格式解析（4294967295 折回 -1），多次退出按顺序全列。
- 不变量：不含签名 / 引擎退出码为 0 时不抓取（不建目录、摘要为空）；抓取函数内部抛错、upm.log 路径不存在、
  留证根目录不可写、引擎日志不存在时都不抛出，步骤的 Ok 判定（只看引擎退出码）保持不变；静态检查
  两个调用方（``consumer_smoke.ps1``、``_gate_line_unity.ps1``）里的摘要变量只进 Detail、不进 Ok。

所有判定在一次 PowerShell 子进程里批量执行（``_ps_harness.py``，会进入 PowerShell 5.1/7 宿主矩阵）。
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, TOOLCHAIN_DIR, ps_quote, run_ps_json

EVIDENCE_PS = TOOLCHAIN_DIR / "_upm_evidence.ps1"
CONSUMER_SMOKE = TOOLCHAIN_DIR / "consumer_smoke.ps1"
UNITY_LINE = TOOLCHAIN_DIR / "_gate_line_unity.ps1"

# Unity 6 实测的引擎日志片段（2026-10-01 在消费方演练里结束 UnityPackageManager.exe 复现）。
LOG_KILLED_TWICE = (
    '[Package Manager] Connected to IPC stream "Upm-44188" after 0.2 seconds.\n'
    "IPCStream (Upm-44188): IPC stream failed to read (Not connected)\n"
    "[Package Manager] Server process stopped with exit code `4294967295`\n"
    "[Package Manager] Server process restart attempt #2\n"
    "IPCStream (Upm-44188): IPC stream failed to read (Not connected)\n"
    "[Package Manager] Server process stopped with exit code `4294967295`\n"
    "[Package Manager] Failed to resolve packages: operation cancelled.\n"
)
LOG_CRASH_101 = (
    "IPCStream (Upm-1): IPC stream failed to read (Not connected)\n"
    "[Package Manager] Server process stopped with exit code `101`\n"
)
LOG_CRASH_ACCESS_VIOLATION = (
    "IPCStream (Upm-1): IPC stream failed to read (Not connected)\n"
    "[Package Manager] Server process stopped with exit code 3221225477\n"
)
LOG_SIGNATURE_NO_CODE = (
    "IPCStream (Upm-1): IPC stream failed to read (Not connected)\n"
    "[Package Manager] Failed to resolve packages: operation cancelled.\n"
)
LOG_NO_SIGNATURE = (
    "[Package Manager] Done resolving packages in 8.84 seconds\n"
    "Library\\PackageCache\\x.cs(1,1): error CS0246: The type or namespace name 'Core' could not be found\n"
)

UPM_LOG_CONTENT = "[2026-10-01T08:30:07.790Z][INFO] Command-line: UnityPackageManager.exe server\nFAKE-UPM-LOG-A\n"
UPM_LOG_CONTENT_B = "FAKE-UPM-LOG-B-second-candidate\n"


@pytest.fixture(scope="module")
def results(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("upm_evidence")
    logs = tmp / "logs"
    logs.mkdir()

    def write(name: str, text: str) -> Path:
        p = logs / name
        p.write_text(text, encoding="utf-8", newline="\n")
        return p

    killed = write("killed.log", LOG_KILLED_TWICE)
    crash101 = write("crash101.log", LOG_CRASH_101)
    access = write("access.log", LOG_CRASH_ACCESS_VIOLATION)
    nocode = write("nocode.log", LOG_SIGNATURE_NO_CODE)
    nosig = write("nosig.log", LOG_NO_SIGNATURE)
    upm_a = write("upm_a.log", UPM_LOG_CONTENT)
    upm_b = write("upm_b.log", UPM_LOG_CONTENT_B)
    missing_upm = logs / "does_not_exist_upm.log"
    missing_engine = logs / "no_such_engine.log"
    not_a_dir = write("i_am_a_file.txt", "x")  # 当作"不可写的留证根目录"

    ev = lambda name: tmp / name  # noqa: E731

    body = f"""
. {ps_quote(EVIDENCE_PS)}
$out = [ordered]@{{}}

function Summarize($r) {{
    [ordered]@{{
        Matched = [bool]$r.Matched; Dir = [string]$r.Dir; ExitCode = $r.ExitCode
        ExitCodes = @($r.ExitCodes | Where-Object {{ $null -ne $_ }}); Copied = @($r.Copied | Where-Object {{ $null -ne $_ }}); Summary = [string]$r.Summary
    }}
}}

# --- 复现：两次被外部结束（无符号 4294967295 折回 -1）+ 两个 upm.log 候选 + 一个不存在的候选 ---
$out.killed = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(killed)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_killed"))} -Tag 'unity compile/1' `
    -UpmLogCandidates @({ps_quote(upm_a)}, {ps_quote(missing_upm)}, {ps_quote(upm_b)}))
$out.killed_files = @(if (Test-Path {ps_quote(ev("ev_killed"))}) {{ Get-ChildItem {ps_quote(ev("ev_killed"))} -Recurse -File | ForEach-Object {{ $_.FullName }} }})

# --- 解析：101（自身崩溃）与 0xC0000005 的无符号打印 ---
$out.crash101 = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(crash101)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_101"))} -Tag t101 -UpmLogCandidates @())
$out.access = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(access)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_access"))} -Tag tav -UpmLogCandidates @())
$out.nocode = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(nocode)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_nocode"))} -Tag tnc -UpmLogCandidates @())

# --- 不变量：不该抓取的情形 ---
$out.nosig = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(nosig)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_nosig"))} -Tag tns -UpmLogCandidates @({ps_quote(upm_a)}))
$out.exit0 = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(killed)} -EngineExitCode 0 `
    -EvidenceRoot {ps_quote(ev("ev_exit0"))} -Tag te0 -UpmLogCandidates @({ps_quote(upm_a)}))
$out.nosig_dir_exists = (Test-Path {ps_quote(ev("ev_nosig"))})
$out.exit0_dir_exists = (Test-Path {ps_quote(ev("ev_exit0"))})
$out.suffix_nosig = Get-UpmEvidenceDetailSuffix -EngineLogPath {ps_quote(nosig)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_sfx0"))} -Tag s0 -UpmLogCandidates @()
$out.suffix_exit0 = Get-UpmEvidenceDetailSuffix -EngineLogPath {ps_quote(killed)} -EngineExitCode 0 `
    -EvidenceRoot {ps_quote(ev("ev_sfx1"))} -Tag s1 -UpmLogCandidates @()
$out.suffix_hit = Get-UpmEvidenceDetailSuffix -EngineLogPath {ps_quote(killed)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_sfx2"))} -Tag s2 -UpmLogCandidates @({ps_quote(upm_a)})

# --- 纯判定函数 ---
$out.sig_cases = [ordered]@{{
    hit = (Test-UpmIpcFailureSignature -LogText 'x IPC stream failed to read (Not connected)' -ExitCode 1)
    exit0 = (Test-UpmIpcFailureSignature -LogText 'x IPC stream failed to read (Not connected)' -ExitCode 0)
    nosig = (Test-UpmIpcFailureSignature -LogText 'all good' -ExitCode 1)
    empty = (Test-UpmIpcFailureSignature -LogText '' -ExitCode 1)
    null = (Test-UpmIpcFailureSignature -LogText $null -ExitCode 1)
}}

# --- 不变量：抓取自身出任何问题都不抛、不改步骤结论 ---
# 模拟步骤判定：只看引擎退出码，本例引擎退出码 1 -> Ok=$false
function Get-StepOk([int]$EngineExit) {{ return ($EngineExit -eq 0) }}
$before = Get-StepOk 1

# a) 留证根目录其实是个文件 -> 建目录失败，仍返回命中 + 部分失败说明，不抛
$out.unwritable = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(killed)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(not_a_dir)} -Tag tun -UpmLogCandidates @({ps_quote(upm_a)}))
# b) 引擎日志不存在 -> 不抛，视为未命中
$out.missing_engine = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(missing_engine)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_me"))} -Tag tme -UpmLogCandidates @())
# c) 引擎日志路径是目录 -> 读失败，不抛
$out.engine_is_dir = Summarize (Save-UpmFailureEvidence -EngineLogPath {ps_quote(logs)} -EngineExitCode 1 `
    -EvidenceRoot {ps_quote(ev("ev_dir"))} -Tag tdir -UpmLogCandidates @())
# d) 抓取函数本身抛错（用同名函数遮蔽）-> 便捷封装吞掉并返回字符串
function Save-UpmFailureEvidence {{ throw 'boom from Save-UpmFailureEvidence' }}
$thrown = $null
$suffixBoom = $null
try {{
    $suffixBoom = Get-UpmEvidenceDetailSuffix -EngineLogPath {ps_quote(killed)} -EngineExitCode 1 -EvidenceRoot {ps_quote(ev("ev_boom"))} -Tag tb
}} catch {{ $thrown = $_.Exception.Message }}
$out.boom_thrown = $thrown
$out.boom_suffix = $suffixBoom
$after = Get-StepOk 1
$out.step_ok_before = $before
$out.step_ok_after = $after
$out | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    parsed = run_ps_json(tmp, body, name="upm_evidence")
    parsed["_tmp"] = str(tmp)
    return parsed


def _as_list(value) -> list:
    if value is None:
        return []
    return value if isinstance(value, list) else [value]


# ----------------------------------------------------------------------------
# 复现
# ----------------------------------------------------------------------------

def test_signature_with_nonzero_exit_copies_upm_log_and_summarizes_exit_code(results: dict) -> None:
    r = results["killed"]
    assert r["Matched"] is True
    # 无符号 4294967295 折回有符号 -1；两次退出按出现顺序全列，ExitCode 取最后一次。
    assert _as_list(r["ExitCodes"]) == [-1, -1]
    assert r["ExitCode"] == -1
    assert "包管理器子进程退出码=-1,-1" in r["Summary"]
    assert "-1/1 疑似外部结束，101 疑似自身崩溃" in r["Summary"]
    assert r["Dir"] and r["Dir"] in r["Summary"], "摘要里要带留证目录路径"
    assert f"现场已存 {r['Dir']}" in r["Summary"]

    files = {Path(p).name: Path(p) for p in results["killed_files"]}
    # upm.log 候选：第 1、3 个存在被复制（按候选序号命名），第 2 个不存在被跳过且不报错。
    assert files["upm_candidate1.log"].read_text(encoding="utf-8") == UPM_LOG_CONTENT
    assert files["upm_candidate3.log"].read_text(encoding="utf-8") == UPM_LOG_CONTENT_B
    assert "upm_candidate2.log" not in files
    # 引擎日志片段含签名行与退出码行；进程快照与 summary 一并落盘。
    excerpt = files["engine_log_excerpt.txt"].read_text(encoding="utf-8")
    assert "IPC stream failed to read" in excerpt
    assert "Server process stopped with exit code `4294967295`" in excerpt
    assert "unity_processes.txt" in files
    assert "-1,-1" in files["summary.txt"].read_text(encoding="utf-8")
    # 留证目录在给定根目录之下，目录名带 Tag（非法字符已替换）。
    assert Path(r["Dir"]).parent.name == "ev_killed"
    assert "unity_compile_1" in Path(r["Dir"]).name


def test_exit_code_parsing_formats(results: dict) -> None:
    assert _as_list(results["crash101"]["ExitCodes"]) == [101]
    assert "包管理器子进程退出码=101（" in results["crash101"]["Summary"]
    # 0xC0000005 的无符号打印 3221225477 折回 -1073741819。
    assert _as_list(results["access"]["ExitCodes"]) == [-1073741819]
    # 日志里找不到退出码行：摘要明说"未在引擎日志中找到"，不猜；仍然命中并留证。
    nocode = results["nocode"]
    assert nocode["Matched"] is True
    assert _as_list(nocode["ExitCodes"]) == [] and nocode["ExitCode"] is None
    assert "包管理器子进程退出码=未在引擎日志中找到" in nocode["Summary"]


def test_suffix_wrapper_returns_one_line_summary_on_hit(results: dict) -> None:
    suffix = results["suffix_hit"]
    assert suffix.startswith("；包管理器子进程退出码=-1,-1（")
    assert "现场已存 " in suffix and "\n" not in suffix


# ----------------------------------------------------------------------------
# 不变量
# ----------------------------------------------------------------------------

def test_no_capture_without_signature_or_with_exit_zero(results: dict) -> None:
    for key in ("nosig", "exit0"):
        r = results[key]
        assert r["Matched"] is False and r["Summary"] == "" and _as_list(r["Copied"]) == [], key
    assert results["nosig_dir_exists"] is False, "未命中签名不应建留证目录"
    assert results["exit0_dir_exists"] is False, "引擎退出码 0 不应建留证目录"
    assert results["suffix_nosig"] == "" and results["suffix_exit0"] == ""
    cases = results["sig_cases"]
    assert cases == {"hit": True, "exit0": False, "nosig": False, "empty": False, "null": False}


def test_capture_failures_never_throw_and_never_change_step_verdict(results: dict) -> None:
    # 留证根目录其实是个文件：建目录失败，仍命中并在摘要里写明部分失败，但不抛、不丢退出码。
    unwritable = results["unwritable"]
    assert unwritable["Matched"] is True and unwritable["Dir"] == ""
    assert "包管理器子进程退出码=-1,-1" in unwritable["Summary"]
    assert "现场抓取部分失败" in unwritable["Summary"]
    # 引擎日志不存在 / 是目录：视为未命中，不抛。
    assert results["missing_engine"]["Matched"] is False
    assert results["engine_is_dir"]["Matched"] is False
    # 抓取函数自身抛错：便捷封装吞掉，只返回带说明的字符串，调用方不会看到异常。
    assert results["boom_thrown"] is None
    assert "包管理器现场抓取异常" in results["boom_suffix"]
    assert "boom from Save-UpmFailureEvidence" in results["boom_suffix"]
    # 步骤结论只由引擎退出码决定，抓取前后一致。
    assert results["step_ok_before"] is False and results["step_ok_after"] is False


@pytest.mark.parametrize("path", [CONSUMER_SMOKE, UNITY_LINE], ids=lambda p: p.name)
def test_callers_use_summary_only_in_detail_not_in_ok(path: Path) -> None:
    """静态不变量：调用方里 ``$upmNote`` 只能出现在 Detail 字符串里，绝不能参与 ``Ok =`` 判定。"""
    text = path.read_text(encoding="utf-8-sig")
    assert "_upm_evidence.ps1" in text, f"{path.name} 要 dot-source 抓取脚本"
    calls = re.findall(r"Get-UpmEvidenceDetailSuffix\b", text)
    assert len(calls) >= 5, f"{path.name} 的每个引擎子步骤都要接上抓取（实际 {len(calls)} 处）"
    for line in text.splitlines():
        if "$upmNote" not in line or "Get-UpmEvidence" in line:
            continue  # 不含摘要变量的行，或"给摘要变量赋值"的那一行
        note_at = line.index("$upmNote")
        assert "Detail" in line and line.index("Detail") < note_at, (
            f"{path.name}：$upmNote 只应出现在 Detail 字符串里（不得进入 Ok 判定）：{line.strip()}"
        )
        assert "$upmNote" not in line[: line.index("Detail")], f"{path.name}：{line.strip()}"


def test_evidence_script_is_bom_lf_and_parses(tmp_path: Path) -> None:
    raw = EVIDENCE_PS.read_bytes()
    assert raw[:3] == b"\xef\xbb\xbf", "含非 ASCII 字符的 .ps1 必须带 UTF-8 BOM"
    assert b"\r\n" not in raw, "新文件统一 LF"
    body = f"""
$tokens = $null; $errors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile({ps_quote(EVIDENCE_PS)}, [ref]$tokens, [ref]$errors)
@{{ errors = @($errors | ForEach-Object {{ $_.Message }}) }} | ConvertTo-Json | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    parsed = run_ps_json(tmp_path, body, name="parse_upm")
    assert _as_list(parsed["errors"]) == []
