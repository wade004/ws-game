<#
.SYNOPSIS
    `.githooks/pre-commit` 分级判断的纯函数（2026-09-22，提交前钩子分级任务）：根据本次提交的
    暂存改动清单，判定门禁该跑"文档档"（只跑与文档相关的几步）、"跳过"（`build.ps1 -Release`
    自己的发布提交，版本文件写回已经在全量门禁通过后才发生）、还是"全量档"（照旧跑
    `check.ps1 -SkipUnity -Quick`）。

    背景（见任务书"问题"一节）：一轮发布里主会话有 4～5 次提交，其中 CHANGELOG 定版、答复稿、
    回归记录只改 `.md`；`build.ps1 -Release` 在 32 步全量门禁通过后做的"发布 X.Y.Z"提交只改
    `VERSION`/两个 `package.json`/`packages-lock.json`/`CHANGELOG.md` 五个版本文件——这两类提交
    再跑一遍 `check.ps1 -SkipUnity -Quick`（32 步，约 100 秒）都是重复验证。

    三档判定规则：
      1. **DocsOnly**：暂存清单非空，且每一条路径都以 `.md` 结尾（大小写不敏感）。
      2. **ReleaseSkip**：`-ReleaseCommitEnvSet` 为真（`build.ps1 -Release` 在
         `git commit` 前设置的环境变量，见该脚本"-Release 第 6 步"判断记录），且暂存清单非空、
         每一条路径都属于 `build.ps1` 第 6 步实际 `git add` 的那五个版本文件（见本文件
         `$ReleaseWritebackFiles`）——两个条件缺一都不判定为这一档，落回下面的 Full。
      3. **Full**：其余任何情况，含暂存清单为空（按最保守档处理，不做"空清单等价全部满足"的
         真空真判定）。

    本文件只定义函数，无顶层副作用（同目录 `_hash.ps1`/`_unity_path_length_guard.ps1`/
    `_version_writeback.ps1`/`_unity_smoke_wait_scope_guard.ps1` 同一模式），供
    `toolchain/tests/test_precommit_tiering_guard.py` dot-source 后直接调用
    `Get-PreCommitCheckTier` 验证，也供 `toolchain/precommit_tier.ps1`（`.githooks/pre-commit`
    实际调用的 CLI 包装）dot-source 复用——两处共享同一份判断逻辑，不重复实现、不会互相漂移。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_precommit_tiering_guard.ps1")
    Get-PreCommitCheckTier -StagedPaths @("architecture/00_foo.md") -ReleaseCommitEnvSet $false
    # 返回 [PSCustomObject]@{ Tier = "DocsOnly"; Reason = "..." }
#>

# build.ps1 "-Release 第 6 步" 实际 `git add` 的版本文件清单（见 build.ps1 该行判断记录）——
# 单一来源写在这里，`toolchain/precommit_tier.ps1`/测试都从 `Get-PreCommitCheckTier` 的返回值
# 间接复用，不在别处另抄一份，避免 build.ps1 改了写回清单却忘了同步这里。
$script:ReleaseWritebackFiles = @(
    "VERSION",
    "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json",
    "adapters/unity/Packages/packages-lock.json",
    "games/_template/package.json",
    "CHANGELOG.md"
)

# 判定 $Paths 里每一条路径的扩展名（大小写不敏感）是否都是 $Extension（形如 ".md"）。
# $Paths 为空时返回 $false——调用方（Get-PreCommitCheckTier）已经在更早处理了空清单的整体分支，
# 这里不需要、也不应该对空集合给出"全部满足"的真空真判定。
function Test-AllPathsHaveExtension {
    param([string[]]$Paths, [string]$Extension)
    if (-not $Paths -or $Paths.Count -eq 0) {
        return $false
    }
    foreach ($p in $Paths) {
        if (-not $p.ToLowerInvariant().EndsWith($Extension.ToLowerInvariant())) {
            return $false
        }
    }
    return $true
}

# 判定 $Paths 里每一条路径是否都落在 $AllowedSet 集合内（逐条精确匹配，不做前缀/通配）。
# 同样对空 $Paths 返回 $false，理由同上。
function Test-AllPathsInSet {
    param([string[]]$Paths, [string[]]$AllowedSet)
    if (-not $Paths -or $Paths.Count -eq 0) {
        return $false
    }
    foreach ($p in $Paths) {
        if ($AllowedSet -notcontains $p) {
            return $false
        }
    }
    return $true
}

# 主入口：见文件头三档判定规则。$StagedPaths 传入前应已经是"一行一个仓库相对路径"的干净清单
# （`git diff --cached --name-only` 的输出按行拆分、去掉空行/首尾空白，交给调用方
# toolchain/precommit_tier.ps1 处理——本函数本身不关心清单是怎么来的，只做纯判断，不读环境、
# 不跑 git 命令，便于 pytest 直接喂任意构造的清单）。
function Get-PreCommitCheckTier {
    param(
        [string[]]$StagedPaths,
        [bool]$ReleaseCommitEnvSet
    )

    # 规范化：去掉 null/空白项（防御性处理——上游按行拆分理论上不该漏，这里不假设调用方一定
    # 已经清洗过）。
    $paths = @()
    if ($StagedPaths) {
        foreach ($p in $StagedPaths) {
            if ($p -and $p.Trim() -ne "") {
                $paths += $p.Trim()
            }
        }
    }

    if ($paths.Count -eq 0) {
        return [PSCustomObject]@{
            Tier   = "Full"
            Reason = "暂存清单为空，按最保守档处理"
        }
    }

    if (Test-AllPathsHaveExtension -Paths $paths -Extension ".md") {
        return [PSCustomObject]@{
            Tier   = "DocsOnly"
            Reason = "暂存改动全部是 .md 文档（$($paths.Count) 个文件）"
        }
    }

    if ($ReleaseCommitEnvSet -and (Test-AllPathsInSet -Paths $paths -AllowedSet $script:ReleaseWritebackFiles)) {
        return [PSCustomObject]@{
            Tier   = "ReleaseSkip"
            Reason = "build.ps1 -Release 发布提交，暂存清单确认只含版本写回文件（$($paths.Count) 个文件），全量门禁已在此之前跑过"
        }
    }

    return [PSCustomObject]@{
        Tier   = "Full"
        Reason = "常规改动（$($paths.Count) 个文件，非纯文档、非发布版本写回），跑完整 -SkipUnity -Quick 门禁"
    }
}

# 合并提交的 MergeSkip 档（发布提速，2026-10-05，ADR-0156）。
#
# 背景：把"合并前已在集成工作树跑过含 Unity 全量"的分支 `git merge --no-ff` 进 main 时，暂存清单是该分支带来的全部改动，
# 判为 T3，钩子再跑一遍 `check.ps1 -Staged -SkipUnity -Quick`（含 dotnet test 全量，1.99.0 实测约 20 分钟）——而被提交的
# 内容与已测过的内容逐字节相同，这 20 分钟没有新增任何信息。
#
# 规则（三条同时满足才放行，任一不满足照常跑，不静默放行）：
#   1. MERGE_HEAD 恰好一个（单个第二父提交，章鱼合并不放行）；
#   2. 即将提交的树（`git write-tree`，即暂存区）与第二父提交的树**逐字节相同**——这等价于"合并结果就是第二父提交本身"
#      （主线是第二父提交的祖先，或主线上的独立改动已被它完全包含）；只要主线有分支没测过的独立改动被合进来，树就不同；
#   3. `REGRESSION_LOG.md` 有对第二父提交的"含 Unity 全量通过"记录（记录提交就是第二父提交，或是它的祖先且其后只改文档类
#      文件）——判定直接复用发布守卫 `Test-ReleaseRegressionRecord -Target <第二父提交>`，规则与记录行格式同发布第 3b 步。
# 为什么安全：被提交的树 = 一棵有含 Unity 全量记录背书的树（外加其后的文档类改动），钩子要把关的"这次提交的内容是否测过"
# 已被记录直接回答；合并前后各一次全量门禁（AGENTS.md 第 1b 节）仍照常要求，不受影响。
#
# 前置：调用方须已 dot-source `toolchain/_release_regression_guard.ps1`（提供 Test-ReleaseRegressionRecord/Invoke-GuardGit）。
# 返回 IsMerge（是否在合并提交中）、Skip、Reason；非合并提交 IsMerge=$false、Skip=$false、Reason 为空。
function Get-PreCommitMergeSkip {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$LogPath = ""
    )
    $result = {
        param([bool]$IsMerge, [bool]$Skip, [string]$Reason)
        [PSCustomObject]@{ IsMerge = $IsMerge; Skip = $Skip; Reason = $Reason }
    }

    $pathResult = Invoke-GuardGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "--git-path", "MERGE_HEAD")
    if ($pathResult.ExitCode -ne 0 -or $pathResult.Lines.Count -eq 0) {
        return (& $result $false $false "")
    }
    $mergeHeadFile = $pathResult.Lines[0].Trim()
    if (-not [System.IO.Path]::IsPathRooted($mergeHeadFile)) {
        $mergeHeadFile = Join-Path $RepoRoot $mergeHeadFile
    }
    if (-not (Test-Path -LiteralPath $mergeHeadFile)) {
        return (& $result $false $false "")
    }
    $heads = @(Get-Content -LiteralPath $mergeHeadFile -Encoding ASCII | ForEach-Object { "$_".Trim() } | Where-Object { $_ -ne "" })
    if ($heads.Count -ne 1) {
        return (& $result $true $false "合并提交有 $($heads.Count) 个第二父提交（章鱼合并），不放行")
    }
    $mergeHead = $heads[0]

    $indexTree = Invoke-GuardGit -RepoRoot $RepoRoot -GitArgs @("write-tree")
    if ($indexTree.ExitCode -ne 0 -or $indexTree.Lines.Count -eq 0) {
        return (& $result $true $false "git write-tree 失败（暂存区有未解决的冲突？），不放行")
    }
    $mergeTree = Invoke-GuardGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "--verify", "--quiet", "$mergeHead^{tree}")
    if ($mergeTree.ExitCode -ne 0 -or $mergeTree.Lines.Count -eq 0) {
        return (& $result $true $false "无法解析第二父提交 $mergeHead 的树，不放行")
    }
    if ($indexTree.Lines[0].Trim() -ne $mergeTree.Lines[0].Trim()) {
        return (& $result $true $false "合并结果的树与第二父提交的树不同（主线上有第二父提交没包含的独立改动，或手工改过合并结果），需要现场验证")
    }

    $verdict = Test-ReleaseRegressionRecord -RepoRoot $RepoRoot -LogPath $LogPath -Target $mergeHead
    if (-not $verdict.Ok) {
        return (& $result $true $false "第二父提交没有可采纳的含 Unity 全量记录：$($verdict.Reason)")
    }
    return (& $result $true $true "合并结果的树与第二父提交 $($mergeHead.Substring(0, 8)) 的树逐字节相同，且该提交有含 Unity 全量记录背书（$($verdict.Reason)）")
}
