<#
.SYNOPSIS
    `.githooks/pre-commit` 实际调用的分级判断 CLI 包装（2026-09-22，提交前钩子分级任务）：从
    标准输入读取本次提交的暂存文件清单（一行一个仓库相对路径，惯例同调用方 `git diff --cached
    --name-only` 的原始输出，不需要预先清洗空行/空白——本脚本与其 dot-source 的纯函数各自都会
    再清洗一遍），读取 `WS_GAME_RELEASE_COMMIT` 环境变量是否已设置（`build.ps1 -Release` 在
    "-Release 第 6 步"提交版本文件前设置，见该脚本判断记录），调用
    `toolchain/_precommit_tiering_guard.ps1` 的纯函数 `Get-PreCommitCheckTier` 得到判定结果，
    以 `TIER|REASON` 单行文本打印到标准输出（`.githooks/pre-commit` 是 POSIX sh 脚本，用这种
    最省心的纯文本协议解析，不引入 JSON 解析依赖）。

    判断记录（为什么单独拆一个 CLI 文件，不把这段 I/O 粘在 `_precommit_tiering_guard.ps1` 里）：
    同目录 `_hash.ps1`/`_unity_path_length_guard.ps1`/`_unity_smoke_wait_scope_guard.ps1` 等
    "_ 前缀"文件的既定约定是"只定义函数、无顶层副作用"，方便 pytest 直接 dot-source 调用而不
    触发任何读 stdin/读环境变量之类的副作用；真正有副作用的 CLI 入口另起一个不带下划线前缀的
    文件（如 `toolchain/abi_probe.ps1` 之于内部纯函数），本文件延续这个既定分工。

    用法（`.githooks/pre-commit` 里的实际调用，见该文件）：
    ```
    git diff --cached --name-only | powershell -NoProfile -ExecutionPolicy Bypass -File toolchain/precommit_tier.ps1
    ```
#>

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_precommit_tiering_guard.ps1")

$stagedRaw = [Console]::In.ReadToEnd()
$stagedPaths = @()
if ($stagedRaw) {
    foreach ($line in ($stagedRaw -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ($trimmed -ne "") {
            $stagedPaths += $trimmed
        }
    }
}

$releaseCommitEnvSet = -not [string]::IsNullOrEmpty($env:WS_GAME_RELEASE_COMMIT)

$result = Get-PreCommitCheckTier -StagedPaths $stagedPaths -ReleaseCommitEnvSet $releaseCommitEnvSet

Write-Output ($result.Tier + "|" + $result.Reason)
