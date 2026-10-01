"""消费方演练默认工作目录按检出隔离的回归测试（2026-10-01，bugfix/upm-evidence-stale-pid）。

背景：``toolchain/consumer_smoke.ps1`` 的默认 WorkDir 原先是 ``<TEMP>\\gf_consumer_smoke``，主检出与每个
工作树共用——运行前整目录清空重建，并行/先后的演练会互相清空对方的工程，也会把对方的 Unity 进程算成
"要等的残留"。现在由 ``Get-ConsumerSmokeDefaultWorkDir``（``_unity_smoke_wait_scope_guard.ps1``）按仓库根
派生：``<TEMP>\\gf_consumer_smoke_<归一化仓库根路径 SHA-256 前 8 位十六进制>``，显式 ``-WorkDir`` 仍然优先。

覆盖：
- 复现：两个不同仓库根派生出不同 WorkDir（隔离的前提）。
- 不变量：同一根多次派生稳定；大小写/结尾分隔符/正斜杠写法不同的同一根派生结果相同；结果恒为
  ``<TempRoot>\\gf_consumer_smoke_<8 位小写十六进制>``；本检出的工程被判"要等"、另一检出的工程被判"不等"
  （沿用既有等待范围判定，隔离不破坏它）；``consumer_smoke.ps1`` 不再写死共用路径、仍保留 ``-WorkDir`` 覆盖。
"""

from __future__ import annotations

import re

from _ps_harness import TOOLCHAIN_DIR, ps_quote, run_ps_json

GUARD = TOOLCHAIN_DIR / "_unity_smoke_wait_scope_guard.ps1"
CONSUMER_SMOKE = TOOLCHAIN_DIR / "consumer_smoke.ps1"

TEMP_ROOT = r"C:\Users\tester\AppData\Local\Temp"
ROOT_MAIN = r"D:\workespace\ws-game"
ROOT_WT = r"D:\wt\upm-evidence"


def test_default_workdir_is_isolated_per_repo_root_and_stable(tmp_path) -> None:
    variants = [ROOT_MAIN, ROOT_MAIN + "\\", ROOT_MAIN.upper(), ROOT_MAIN.replace("\\", "/"), '"' + ROOT_MAIN + '"']
    body = f"""
. {ps_quote(GUARD)}
$temp = {ps_quote(TEMP_ROOT)}
$out = [ordered]@{{}}
$out.main = Get-ConsumerSmokeDefaultWorkDir -RepoRoot {ps_quote(ROOT_MAIN)} -TempRoot $temp
$out.main_again = Get-ConsumerSmokeDefaultWorkDir -RepoRoot {ps_quote(ROOT_MAIN)} -TempRoot $temp
$out.worktree = Get-ConsumerSmokeDefaultWorkDir -RepoRoot {ps_quote(ROOT_WT)} -TempRoot $temp
$out.variants = @({", ".join(f"(Get-ConsumerSmokeDefaultWorkDir -RepoRoot {ps_quote(v)} -TempRoot $temp)" for v in variants)})
# 隔离不破坏既有的"要不要等"判定：本检出工作目录下的 Unity 工程要等，另一个检出工作目录下的不等。
$cmd = {{ param($proj) '"C:\\Unity\\Unity.exe" -batchmode -projectPath "' + $proj + '" -quit' }}
$out.wait_own = Get-UnitySmokeProcessWaitDecision -CommandLine (& $cmd ($out.main + '\\ConsumerProject')) -RepoRoot {ps_quote(ROOT_MAIN)} -WorkDir $out.main
$out.wait_other_checkout = Get-UnitySmokeProcessWaitDecision -CommandLine (& $cmd ($out.worktree + '\\ConsumerProject')) -RepoRoot {ps_quote(ROOT_MAIN)} -WorkDir $out.main
$out | ConvertTo-Json -Depth 4 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    r = run_ps_json(tmp_path, body, name="workdir")
    pattern = re.compile(re.escape(TEMP_ROOT) + r"\\gf_consumer_smoke_[0-9a-f]{8}$")
    assert pattern.match(r["main"]), r["main"]
    assert pattern.match(r["worktree"]), r["worktree"]
    assert r["main"] != r["worktree"], "不同仓库根必须派生出不同 WorkDir"
    assert r["main"] == r["main_again"], "同一仓库根派生结果必须稳定"
    assert set(r["variants"]) == {r["main"]}, f"同一根的不同写法应派生同一目录：{r['variants']}"
    assert r["wait_own"] == "Wait"
    assert r["wait_other_checkout"] == "NoWait", "另一个检出的演练进程不应被当作要等的残留"


def test_consumer_smoke_no_longer_hardcodes_shared_workdir_but_keeps_override() -> None:
    text = CONSUMER_SMOKE.read_text(encoding="utf-8-sig")
    assert 'Join-Path $env:TEMP "gf_consumer_smoke"' not in text, "默认 WorkDir 不得再写死成所有检出共用的路径"
    assert "Get-ConsumerSmokeDefaultWorkDir -RepoRoot $RepoRoot -TempRoot $env:TEMP" in text
    # 显式传参仍然优先：只在 WorkDir 为空时才用派生值。
    assert '[string]$WorkDir = ""' in text
    assert re.search(r'if \(\$WorkDir -eq ""\) \{\s*\$WorkDir = Get-ConsumerSmokeDefaultWorkDir', text)
