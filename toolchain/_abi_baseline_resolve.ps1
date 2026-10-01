# ABI 基线发行包路径解析（供 toolchain/abi_probe.ps1 点源；只定义函数、无顶层副作用，
# toolchain/tests/test_abi_baseline_fallback.py 也直接点源后单独测试）。
#
# 判断记录（2026-10-02，dist 瘦身与基线回落）：
#   - 背景：`dist/` 是 .gitignore 的本机构建缓存，只存在于主检出；并行会话在链接工作树里跑门禁时找不到
#     `dist\ws-game-<基线版本>.zip`，于是有人把整个 dist（十几 GB）复制进每个工作树。根因是探针只看
#     `$RepoRoot\dist`，所以这里让它在"本工作树没有、且本目录是链接工作树"时回落到主检出的 dist，
#     从机制上消除复制的理由。
#   - 主检出根的求法：`git -C <RepoRoot> rev-parse --path-format=absolute --git-common-dir` 的父目录。
#     链接工作树的 `--git-dir` 是 `<主检出>/.git/worktrees/<名>`，与 `--git-common-dir`（`<主检出>/.git`）
#     不同；两者相同说明本目录就是主检出（或普通克隆），不存在"另一个更权威的 dist"，不回落。
#   - 求值时临时摘掉 GIT_DIR / GIT_WORK_TREE / GIT_INDEX_FILE / GIT_COMMON_DIR / GIT_PREFIX：预提交钩子里
#     跑本脚本会继承到这些变量，让 git 的答案指向钩子的仓库而不是 -C 指定的目录（见
#     toolchain/tests/_git_env.py 的 2026-10-01 事故说明）；函数返回前原样还原。
#   - 任何一步失败（没有 git、不是仓库、版本太旧不认 --path-format）一律返回 $null，调用方按"本工作树没有
#     基线"的既有路径处理，不新增失败模式。
#   - 回落只在默认解析时发生；显式传 -BaselineZip 的调用方不经过本函数。

function Get-MainWorktreeRoot {
    param([string]$RepoRoot)

    $hazardVars = @("GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_PREFIX")
    $saved = @{}
    foreach ($name in $hazardVars) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
        # 用 Env: 驱动器删除：[Environment]::SetEnvironmentVariable(name, $null, ...) 在 PowerShell 7 下会
        # 把变量置成空串而不是删除，空的 GIT_DIR 会让 git 报 "not a git repository: ''"。
        Remove-Item -LiteralPath ("Env:\" + $name) -ErrorAction SilentlyContinue
    }
    try {
        $gitDirOut = & git -C $RepoRoot rev-parse --path-format=absolute --git-dir 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$gitDirOut")) { return $null }
        $commonDirOut = & git -C $RepoRoot rev-parse --path-format=absolute --git-common-dir 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$commonDirOut")) { return $null }

        $gitDir = [System.IO.Path]::GetFullPath(("$gitDirOut").Trim()).TrimEnd('\', '/')
        $commonDir = [System.IO.Path]::GetFullPath(("$commonDirOut").Trim()).TrimEnd('\', '/')
        if ([string]::Equals($gitDir, $commonDir, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $null
        }
        $mainRoot = Split-Path -Parent $commonDir
        if ([string]::IsNullOrWhiteSpace($mainRoot)) { return $null }
        return $mainRoot
    } catch {
        return $null
    } finally {
        foreach ($name in $hazardVars) {
            if ($null -ne $saved[$name]) {
                Set-Item -LiteralPath ("Env:\" + $name) -Value $saved[$name]
            }
        }
    }
}

# 返回 [PSCustomObject]@{ Path; FromMainWorktree; Found }：
#   1. `<RepoRoot>\dist\ws-game-<ver>.zip` 存在 → 用它（FromMainWorktree=$false）。
#   2. 否则、RepoRoot 是链接工作树、且主检出的 `dist\ws-game-<ver>.zip` 存在 → 用主检出的
#      （FromMainWorktree=$true）。
#   3. 都没有 → Path 仍是第 1 条的本地路径、Found=$false（调用方沿用既有"缺基线"提示）。
function Resolve-AbiBaselineZip {
    param(
        [string]$RepoRoot,
        [string]$BaselineVersion
    )

    $zipName = "ws-game-" + $BaselineVersion + ".zip"
    $localPath = Join-Path $RepoRoot ("dist\" + $zipName)
    if (Test-Path -LiteralPath $localPath) {
        return [PSCustomObject]@{ Path = $localPath; FromMainWorktree = $false; Found = $true }
    }

    $mainRoot = Get-MainWorktreeRoot -RepoRoot $RepoRoot
    if ($null -ne $mainRoot) {
        $mainPath = Join-Path $mainRoot ("dist\" + $zipName)
        if (Test-Path -LiteralPath $mainPath) {
            return [PSCustomObject]@{ Path = $mainPath; FromMainWorktree = $true; Found = $true }
        }
    }

    return [PSCustomObject]@{ Path = $localPath; FromMainWorktree = $false; Found = $false }
}
