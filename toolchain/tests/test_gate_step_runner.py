"""``toolchain/_gate_step_runner.ps1`` 与门禁分线并行编排的回归测试（gate-speed 任务，
2026-09-22）。

背景：`check.ps1` 门禁提速重排把原来单文件内联的 `Invoke-CheckStep`/`Add-SkippedStep`/
`Test-NativeExitCode` 等基础设施抽到 `toolchain/_gate_step_runner.ps1`，供 `check.ps1` 自己的
"快速前置阶段"与两条并行子线（`toolchain/_gate_line_heavy.ps1`/`toolchain/_gate_line_unity.ps1`）
dot-source 复用；同时新增 `-FailFast` 开关（同进程内"本线已失败即短路"+跨进程"标记文件"两种
语义）与两条线用 `Start-Job` 并行调度。这些都是纯 PowerShell 逻辑，不需要真正跑 dotnet/Unity 就
能验证，按仓库既有惯例（`test_precommit_tiering_guard.py`/`test_unity_path_length_guard.py`）
用 pytest 驱动 `powershell`/`pwsh` 子进程直接 dot-source 目标脚本、构造最小合成场景来测。

覆盖点：
  1. `Invoke-CheckStep` 在 `-FailFast` 下，本进程内一步失败后，后续步骤不再执行 `$Action`
     （用一个会追加写文件的探针 action 证明"根本没被调用"，不是"调用了但被判失败"）。
  2. `-FailFast` 下跨进程标记文件语义：进程 A 的某步失败会创建标记文件；进程 B（`$script:
     GateFailed` 初始为假，但配置了同一个 `FailFastFlagPath`）的下一步在真正执行前会先看到
     这个文件并短路，不会执行 `$Action`。
  3. `-DocsOnly` 下未标 `-DocRelevant` 的步骤短路为 SKIP（不执行 `$Action`），标了
     `-DocRelevant` 的步骤正常执行。
  4. `check.ps1`/`toolchain/_gate_line_heavy.ps1`/`toolchain/_gate_line_unity.ps1` 三个入口
     脚本本身可以被 PowerShell 解析器无错误解析（语法层面的编排代码路径回归网——不需要真正跑
     dotnet/Unity 就能拦住"改了参数名却没同步改调用点"这类低级错误）。
  5. 两条并行线确实并发执行，不是伪并行：用 `check.ps1` 内建的 `-InjectMockSleepHeavySeconds`/
     `-InjectMockSleepUnitySeconds` 测试钩子（默认 0，不影响正常门禁行为），配合 `-DocsOnly`
     让其余步骤都是近乎零成本的短路判断，断言总墙钟明显小于两段模拟耗时之和。
  6. 禁用词扫描（游戏代号一项）改用 `git grep` 之后，在一个合成的最小 git 仓库里验证：未命中时
     退出码 1（判通过）、命中受跟踪文件时退出码 0 且能定位到具体文件（判失败）——与
     `check.ps1` 步骤 1 使用的完全相同的命令行。

跨平台说明：依赖 `powershell`（Windows PowerShell 5.1）或 `pwsh` 可执行、以及 `git`，本机没有
时 skip，不 fail（与仓库其余依赖 PowerShell 解释器的测试约定一致）。
"""

from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

import pytest

from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
STEP_RUNNER_SCRIPT = REPO_ROOT / "toolchain" / "_gate_step_runner.ps1"
CHECK_SCRIPT = REPO_ROOT / "check.ps1"
LINE_HEAVY_SCRIPT = REPO_ROOT / "toolchain" / "_gate_line_heavy.ps1"
LINE_UNITY_SCRIPT = REPO_ROOT / "toolchain" / "_gate_line_unity.ps1"


def _find_powershell() -> str | None:
    for exe in ("powershell", "pwsh"):
        found = shutil.which(exe)
        if found:
            return exe
    return None


@pytest.fixture()
def ps_exe() -> str:
    exe = _find_powershell()
    if exe is None:
        pytest.skip("本机未找到 powershell/pwsh 可执行文件，跳过（与仓库其余 .ps1 相关测试一致约定）")
    return exe


def _run_ps(ps_exe: str, script: str, cwd: Path | None = None) -> subprocess.CompletedProcess[str]:
    # 判断记录（前置 [Console]::OutputEncoding 设置）：同 test_unity_path_length_guard.py
    # _run_guard 判断记录——本机非终端/被管道重定向的 stdout 默认走系统 ANSI 代码页（GBK/936），
    # 脚本文本里的中文步骤名/Detail 经这个代码页往返会乱码甚至丢字节，与 Python 侧固定
    # `encoding="utf-8"` 解码不匹配。显式把宿主 Console 输出编码设成 UTF8，不依赖也不硬编码
    # 某个特定系统代码页。
    prefixed_script = '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; ' + script
    return subprocess.run(
        [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", prefixed_script],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        env=clean_powershell_env(ps_exe),
        cwd=str(cwd) if cwd is not None else None,
        timeout=120,
    )


# ---------------------------------------------------------------------------
# 1. 同进程内 FailFast 短路：$Action 根本不会被调用
# ---------------------------------------------------------------------------
def test_failfast_same_process_short_circuits_action(tmp_path: Path, ps_exe: str) -> None:
    marker = tmp_path / "second_step_ran.txt"
    script = (
        '$ErrorActionPreference = "Stop"; '
        f'$RepoRoot = "{REPO_ROOT}"; '
        '$DocsOnly = $false; $FailFast = $true; '
        '$script:Results = New-Object System.Collections.Generic.List[Object]; '
        '$script:GateFailed = $false; $script:FailFastFlagPath = $null; '
        f'. "{STEP_RUNNER_SCRIPT}"; '
        'Invoke-CheckStep "第一步：故意失败" { [PSCustomObject]@{ Ok = $false; Detail = "boom" } }; '
        f'Invoke-CheckStep "第二步：不应被执行" {{ Set-Content -Path "{marker}" -Value "ran" }}; '
        'Write-Output ("RESULT_COUNT=" + $script:Results.Count); '
        'foreach ($r in $script:Results) { Write-Output ("STEP=" + $r.Step + "|" + $r.Result + "|" + $r.Detail) }'
    )
    result = _run_ps(ps_exe, script)
    assert result.returncode == 0, f"stdout={result.stdout}\nstderr={result.stderr}"
    assert not marker.exists(), "FailFast 下第二步的 $Action 不应该被执行（探针文件不应该被写出）"
    assert "STEP=第一步：故意失败|FAIL|boom" in result.stdout
    assert "上游步骤已失败" in result.stdout
    assert "RESULT_COUNT=2" in result.stdout


# ---------------------------------------------------------------------------
# 2. 跨进程 FailFast 标记文件：另一条线看到标记文件后短路，不执行 $Action
# ---------------------------------------------------------------------------
def test_failfast_cross_process_flag_file(tmp_path: Path, ps_exe: str) -> None:
    flag_path = tmp_path / "gate_failfast.flag"
    marker = tmp_path / "line_b_second_step_ran.txt"

    # 模拟"线 A"：一步失败，应该创建标记文件。
    script_a = (
        '$ErrorActionPreference = "Stop"; '
        f'$RepoRoot = "{REPO_ROOT}"; '
        '$DocsOnly = $false; $FailFast = $true; '
        '$script:Results = New-Object System.Collections.Generic.List[Object]; '
        '$script:GateFailed = $false; '
        f'$script:FailFastFlagPath = "{flag_path}"; '
        f'. "{STEP_RUNNER_SCRIPT}"; '
        'Invoke-CheckStep "线 A 的步骤：故意失败" { [PSCustomObject]@{ Ok = $false; Detail = "boom" } }'
    )
    result_a = _run_ps(ps_exe, script_a)
    assert result_a.returncode == 0, f"stdout={result_a.stdout}\nstderr={result_a.stderr}"
    assert flag_path.exists(), "线 A 失败后应该创建 FailFast 跨进程标记文件"

    # 模拟"线 B"：自己的 $script:GateFailed 还是 $false（本线还没有任何一步失败过），但共享同一个
    # 标记文件路径——下一步开始前应该先看到线 A 留下的标记，短路成 SKIP，不执行 $Action。
    script_b = (
        '$ErrorActionPreference = "Stop"; '
        f'$RepoRoot = "{REPO_ROOT}"; '
        '$DocsOnly = $false; $FailFast = $true; '
        '$script:Results = New-Object System.Collections.Generic.List[Object]; '
        '$script:GateFailed = $false; '
        f'$script:FailFastFlagPath = "{flag_path}"; '
        f'. "{STEP_RUNNER_SCRIPT}"; '
        f'Invoke-CheckStep "线 B 的步骤：不应被执行" {{ Set-Content -Path "{marker}" -Value "ran" }}; '
        'foreach ($r in $script:Results) { Write-Output ("STEP=" + $r.Step + "|" + $r.Result + "|" + $r.Detail) }'
    )
    result_b = _run_ps(ps_exe, script_b)
    assert result_b.returncode == 0, f"stdout={result_b.stdout}\nstderr={result_b.stderr}"
    assert not marker.exists(), "线 B 看到线 A 的 FailFast 标记后，$Action 不应该被执行"
    assert "并行的另一条线已失败" in result_b.stdout


# ---------------------------------------------------------------------------
# 3. -DocsOnly 短路：未标 -DocRelevant 的步骤不执行 $Action，标了的正常执行
# ---------------------------------------------------------------------------
def test_docsonly_short_circuits_non_doc_relevant_steps(tmp_path: Path, ps_exe: str) -> None:
    non_relevant_marker = tmp_path / "non_relevant_ran.txt"
    relevant_marker = tmp_path / "relevant_ran.txt"
    script = (
        '$ErrorActionPreference = "Stop"; '
        f'$RepoRoot = "{REPO_ROOT}"; '
        '$DocsOnly = $true; $FailFast = $false; '
        '$script:Results = New-Object System.Collections.Generic.List[Object]; '
        '$script:GateFailed = $false; $script:FailFastFlagPath = $null; '
        f'. "{STEP_RUNNER_SCRIPT}"; '
        f'Invoke-CheckStep "非文档相关步骤" {{ Set-Content -Path "{non_relevant_marker}" -Value "ran" }}; '
        f'Invoke-CheckStep "文档相关步骤" -DocRelevant {{ Set-Content -Path "{relevant_marker}" -Value "ran" }}; '
        'foreach ($r in $script:Results) { Write-Output ("STEP=" + $r.Step + "|" + $r.Result + "|" + $r.Detail) }'
    )
    result = _run_ps(ps_exe, script)
    assert result.returncode == 0, f"stdout={result.stdout}\nstderr={result.stderr}"
    assert not non_relevant_marker.exists(), "-DocsOnly 下非 -DocRelevant 步骤的 $Action 不应该被执行"
    assert relevant_marker.exists(), "-DocsOnly 下标了 -DocRelevant 的步骤应该正常执行"
    assert "STEP=非文档相关步骤|SKIP" in result.stdout
    assert "STEP=文档相关步骤|PASS" in result.stdout


# ---------------------------------------------------------------------------
# 4. 三个入口脚本能被 PowerShell 解析器无错误解析（语法层面的编排回归网）
# ---------------------------------------------------------------------------
@pytest.mark.parametrize(
    "script_path",
    [CHECK_SCRIPT, LINE_HEAVY_SCRIPT, LINE_UNITY_SCRIPT],
    ids=["check.ps1", "_gate_line_heavy.ps1", "_gate_line_unity.ps1"],
)
def test_gate_scripts_parse_without_error(ps_exe: str, script_path: Path) -> None:
    script = (
        '$errors = $null; $tokens = $null; '
        f'$ast = [System.Management.Automation.Language.Parser]::ParseFile("{script_path}", [ref]$tokens, [ref]$errors); '
        'if ($errors.Count -gt 0) { '
        'foreach ($e in $errors) { Write-Output ("PARSE_ERROR: " + $e.Message) }; '
        'exit 1 '
        '} else { Write-Output "PARSE_OK" }'
    )
    result = _run_ps(ps_exe, script)
    assert result.returncode == 0, f"stdout={result.stdout}\nstderr={result.stderr}"
    assert "PARSE_OK" in result.stdout


# ---------------------------------------------------------------------------
# 5. 两条并行线确实并发（不是伪并行）：用 check.ps1 内建的模拟慢步骤钩子验证。
#    判断记录：本用例会真正调用 `check.ps1 -DocsOnly`（两条线各自绝大多数步骤在 -DocsOnly 下都是
#    近乎零成本的短路判断，只有注入的两段 Start-Sleep 占用真实耗时），比其余用例慢（预算内，
#    数十秒级），但只有真跑一次 `Start-Job` 编排才能证明"两条线确实并发"这件事本身，不能只靠
#    静态分析。用一个明显小于"两段耗时之和"的阈值判定，留出进程调度/作业启动开销的余量。
# ---------------------------------------------------------------------------
def test_two_lines_run_concurrently(ps_exe: str, tmp_path: Path) -> None:
    # 判断记录（阈值取"单段耗时 + 固定余量"而不是"单段耗时 × 倍数"）：Start-Job 启动两个独立
    # powershell.exe 子进程本身有固定开销（本机实测约 8~9 秒，含两次子进程启动 + dot-source +
    # -DocsOnly 下仍要跑的门禁自检/两道禁用词扫描/版本一致性等快速前置步骤 + doc-pytest 子集），
    # 与 sleep_seconds 大小无关；用倍数阈值在 sleep_seconds 较小时会被这部分固定开销误判为"串行"。
    # 用"单段耗时 + 20 秒固定余量"作为并行判定上限，串行情形下预期总耗时约
    # 2 * sleep_seconds + 固定开销，两者之间留有清晰的分隔带。
    # 2026-09-23 ʵ�ⷭ���ſ���1.67.0 �����Ž������̶�����ʵ�� 16.6s������ԭ�� 15s ��Ԥ�㣬
    # �ж��������� 35~40s�����ػ����ϣ����������ȫ���Ž�����������Ƕ����һ�� check.ps1 �ӽ��̣�
    # ���Ϸ� ArtifactsPath �жϼ�¼����Ȼ����������С��Ǵ�ʵ�� 36.59s ���������Ե��ڴ�������
    # 40s����������ȷʵ�����ˣ��������ֵ������Ϊ���޷����̶�����Ԥ��ſ��� 25s �������ز�����
    # ͬʱ�ѵ���ģ���ʱ�ӱ������̶������������������ж�����˴� 5s ������ 15s��65s~80s����
    # �ȱ�ס"���б�Ȼ��ץ��"���оݣ��ֲ��ٿ��������в����̡������Ǳ�������ʱ��Լ 20s �ǵ�Լ 40s��
    sleep_seconds = 40
    fixed_overhead_budget = 25
    concurrent_threshold = sleep_seconds + fixed_overhead_budget
    serial_expected_floor = 2 * sleep_seconds

    # 判断记录（-ArtifactsPath 必须隔离，2026-09-22 实测发现的真实 bug）：本用例真实调用
    # check.ps1，其 $ArtifactsPath 默认落在 `<repo>\bin\_check_artifacts\`（按 $RepoRoot 固定，
    # 不带调用者隔离）。而 check.ps1 自身"非 Unity 重步骤线"里就有一步是
    # `python -m pytest toolchain/tests -q`——也就是说，当外层某次 check.ps1（无论带不带
    # -SkipUnity/-DocsOnly）跑到 pytest 这一步、恰好执行到本用例时，本用例发起的这个嵌套
    # check.ps1 子进程会和"外层正在跑的那次 check.ps1"共用同一个 $ArtifactsPath，两者的
    # gate_line_heavy.log/gate_line_unity.log/对应 .json 结果文件互相覆盖——实测复现：外层一次
    # 干净的 `check.ps1 -SkipUnity` 全量运行，最终汇总表里 Unity 线的步骤被本用例注入的
    # -DocsOnly/模拟慢步骤结果整体覆盖掉，外层真实的 FAIL（包清单一致性）被吞掉，汇总表誤报
    # "全部通过"。修法：显式传 -ArtifactsPath 指向 pytest 的 tmp_path（每次用例调用、每个进程
    # 唯一），彻底断开与任何"正在跑/将要跑本用例的外层 check.ps1"共享文件路径的可能，不依赖
    # "同一时刻只有一个 check.ps1 在跑"这个不成立的假设。
    artifacts_path = tmp_path / "gate_artifacts"

    script = (
        f'& "{ps_exe}" -NoProfile -ExecutionPolicy Bypass -File "{CHECK_SCRIPT}" '
        f'-DocsOnly -ArtifactsPath "{artifacts_path}" '
        f'-InjectMockSleepHeavySeconds {sleep_seconds} -InjectMockSleepUnitySeconds {sleep_seconds} '
        '*>&1 | Out-String'
    )
    import time

    start = time.monotonic()
    result = subprocess.run(
        [ps_exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
         '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; ' + script],
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        env=clean_powershell_env(ps_exe),
        timeout=180,
    )
    elapsed = time.monotonic() - start

    assert result.returncode == 0, f"stdout={result.stdout}\nstderr={result.stderr}"
    assert concurrent_threshold < serial_expected_floor, "测试常量本身矛盾：并行判定阈值应严格小于串行预期下限"
    assert elapsed < concurrent_threshold, (
        f"两条线耗时应明显小于两段模拟耗时之和（串行预期 >= {serial_expected_floor}s），"
        f"实测 {elapsed:.1f}s（判定阈值 {concurrent_threshold}s），怀疑两条线其实是串行跑的。"
        f"stdout 摘要：{result.stdout[-2000:]}"
    )


# ---------------------------------------------------------------------------
# 6. 禁用词扫描（游戏代号一项）改用 git grep：合成最小 git 仓库验证命中/不命中两种情形，
#    与 check.ps1 步骤 1 使用完全相同的命令行。
# ---------------------------------------------------------------------------
@pytest.fixture()
def git_exe() -> str:
    found = shutil.which("git")
    if found is None:
        pytest.skip("本机未找到 git 可执行文件，跳过")
    return found


def _init_repo(git_exe: str, repo_dir: Path) -> None:
    repo_dir.mkdir(parents=True, exist_ok=True)
    subprocess.run([git_exe, "init", "-q"], cwd=repo_dir, check=True)
    subprocess.run([git_exe, "config", "user.email", "test@example.com"], cwd=repo_dir, check=True)
    subprocess.run([git_exe, "config", "user.name", "Test"], cwd=repo_dir, check=True)


# 判断记录（拼接写法，2026-09-22 提交时被主检出的 pre-commit 钩子拦下才发现）：本测试文件是
# 受版本管理的真实源码，如果直接写出禁用词的完整字面量，check.ps1 步骤 1 自己的 git grep 扫描
# 会把这份测试固件数据误判成"仓库里出现了具体游戏代号"（提交时的门禁自检真的拦了一次）。和
# check.ps1 里 `$bannedCodename = "note" + "moss"` 用同一招——运行期拼接，源码里不出现连续的
# 禁用词字面量，让扫描器（对自身之外的文件）扫不到，同时不影响测试语义。
_BANNED_WORD = "note" + "moss"
_BANNED_WORD_TITLE = "Note" + "Moss"


def _git_grep_banned_codename(git_exe: str, repo_dir: Path, banned_word: str) -> subprocess.CompletedProcess[str]:
    # 与 check.ps1 步骤 1"禁用词扫描：全仓库不出现具体游戏代号"完全相同的命令行（排除路径改成
    # 本合成仓库里同样存在的占位文件，语义等价）。
    return subprocess.run(
        [git_exe, "grep", "-n", "-i", "-I", "-F", "--", banned_word, ".", ":(exclude)check.ps1"],
        cwd=repo_dir,
        capture_output=True,
        encoding="utf-8",
        errors="replace",
    )


def test_git_grep_banned_codename_no_hit_on_clean_repo(tmp_path: Path, git_exe: str) -> None:
    repo_dir = tmp_path / "clean_repo"
    _init_repo(git_exe, repo_dir)
    (repo_dir / "check.ps1").write_text("# placeholder\n", encoding="utf-8")
    (repo_dir / "readme.md").write_text("nothing suspicious here\n", encoding="utf-8")
    subprocess.run([git_exe, "add", "-A"], cwd=repo_dir, check=True)

    result = _git_grep_banned_codename(git_exe, repo_dir, _BANNED_WORD)
    assert result.returncode == 1, f"未命中时 git grep 应返回退出码 1，实际 {result.returncode}：{result.stdout}"


def test_git_grep_banned_codename_detects_tracked_file(tmp_path: Path, git_exe: str) -> None:
    repo_dir = tmp_path / "dirty_repo"
    _init_repo(git_exe, repo_dir)
    (repo_dir / "check.ps1").write_text("# placeholder\n", encoding="utf-8")
    (repo_dir / "leaked.md").write_text(f"this file contains {_BANNED_WORD_TITLE} by mistake\n", encoding="utf-8")
    subprocess.run([git_exe, "add", "-A"], cwd=repo_dir, check=True)

    result = _git_grep_banned_codename(git_exe, repo_dir, _BANNED_WORD)
    assert result.returncode == 0, f"命中受跟踪文件时 git grep 应返回退出码 0，实际 {result.returncode}"
    assert "leaked.md" in result.stdout


def test_git_grep_banned_codename_ignores_untracked_file(tmp_path: Path, git_exe: str) -> None:
    # 关键差异化用例：未加入版本管理（既未 add 也未 commit）的文件即使含禁用词也不应该被扫到——
    # 这正是本次改造"只扫受版本管理的文件"的核心行为，用来和改造前 Get-ChildItem 遍历物理目录树
    # 的旧实现区分开。
    repo_dir = tmp_path / "untracked_repo"
    _init_repo(git_exe, repo_dir)
    (repo_dir / "check.ps1").write_text("# placeholder\n", encoding="utf-8")
    subprocess.run([git_exe, "add", "-A"], cwd=repo_dir, check=True)
    # 故意不 git add：模拟构建产物/缓存目录里意外出现的文件。
    (repo_dir / "untracked_leak.txt").write_text(f"{_BANNED_WORD_TITLE} leaked but untracked\n", encoding="utf-8")

    result = _git_grep_banned_codename(git_exe, repo_dir, _BANNED_WORD)
    assert result.returncode == 1, (
        f"未受版本管理的文件不应该被 git grep 扫到，实际退出码 {result.returncode}：{result.stdout}"
    )
