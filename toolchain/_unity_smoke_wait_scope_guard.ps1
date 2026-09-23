<#
.SYNOPSIS
    从一条 Unity.exe 进程命令行，判断消费方演练（consumer_smoke.ps1）下一次拉起 Unity 批处理前
    是否需要等它退出（2026-09-21，Wait-NoResidualUnityProcess 等待范围收窄——见
    consumer_smoke.ps1 文件头 .NOTES"P07 根治之一"一节的最新口径）。

    背景：Wait-NoResidualUnityProcess 此前不按工程路径过滤，等系统里任何 Unity.exe 都退出——
    这个"不过滤"本身是故意的（check.ps1 全量门禁跑完框架自己的 adapters/unity 工程后紧接着跑
    本脚本、在另一个工程路径下拉起 Unity，上一个进程退出到它真正清理完之间有滞后窗口，按本脚本
    自己的工程路径过滤会漏掉这类残留），但代价是别的仓库里长时间正常运行的 Unity 进程（例如另一个
    项目自己的回归批处理）也会被一起等——那种进程不是"刚退出还在清理"，等多久都不会消失，等待
    没有意义，只会拖垮本脚本乃至整个门禁。

    收窄口径：把"要等"限定为可能与本次演练撞车的两类工程——本仓库根目录下的（框架自己的工程，
    正是要防的那种残留）、本脚本工作目录下的（消费方演练自己刚起的临时工程）；拿不到命令行或
    命令行里没有 -projectPath 时无法判断归属，按保守口径等；工程路径明确落在这两处之外的，判定
    不等。

    本文件只抽出"从命令行判断归属"这一段纯逻辑（无进程操作、无 I/O、无副作用），供
    toolchain/tests/test_unity_smoke_wait_scope_guard.py 用 pytest 驱动 PowerShell 直接调用
    验证——Wait-NoResidualUnityProcess 本身要真的去查系统进程表，没法在这里起 Unity 测试。

    独立成本文件（不直接写进 consumer_smoke.ps1 内联，同目录 _unity_path_length_guard.ps1/
    _hash.ps1/_version_writeback.ps1 同一模式）：只定义函数、无顶层副作用。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_unity_smoke_wait_scope_guard.ps1")
    Get-UnitySmokeProcessWaitDecision -CommandLine $proc.CommandLine -RepoRoot $RepoRoot -WorkDir $WorkDir
    # 返回 "Wait" / "NoWait" / "Unknown" 三态之一。
#>

# 路径比较统一走这里：大小写不敏感（Windows 文件系统语义）、正斜杠归一成反斜杠、去掉首尾引号与
# 结尾分隔符——命令行里的路径可能带引号（"D:\a\b"）、也可能用正斜杠（不常见但不排除下游改造过
# 启动脚本），归一化后才能用简单字符串比较。
function ConvertTo-NormalizedFsPath {
    param([string]$Path)
    if ([string]::IsNullOrEmpty($Path)) {
        return ""
    }
    $normalized = $Path.Trim()
    $normalized = $normalized.Trim('"')
    $normalized = $normalized.Replace('/', '\')
    $normalized = $normalized.TrimEnd('\')
    return $normalized.ToLowerInvariant()
}

# 判定 CandidatePath 是否等于或从属于 AncestorPath。判断记录（最容易写错的一处）：前缀比较必须
# 先在两侧路径后面补上一个分隔符再比较，不能直接用 StartsWith(ancestor) ——否则仓库根的同名前缀
# 目录（例如仓库根 "D:\workespace\ws-game"，另一个仓库 "D:\workespace\ws-game-wow\unity"）会被
# 误判成子目录："ws-game-wow" 以 "ws-game" 开头，但它不是 "ws-game" 目录下的任何东西。见
# toolchain/tests/test_unity_smoke_wait_scope_guard.py 里 "sibling repo with shared prefix" 一例。
function Test-FsPathIsUnderOrEqual {
    param([string]$CandidatePath, [string]$AncestorPath)
    $candidate = ConvertTo-NormalizedFsPath -Path $CandidatePath
    $ancestor = ConvertTo-NormalizedFsPath -Path $AncestorPath
    if ($candidate -eq "" -or $ancestor -eq "") {
        return $false
    }
    if ($candidate -eq $ancestor) {
        return $true
    }
    return $candidate.StartsWith($ancestor + '\')
}

# 从进程命令行里解析 -projectPath 的值：支持带引号（含引号内带空格）与不带引号两种写法，大小写
# 不敏感匹配 "-projectPath" 本身（Unity 官方固定拼写就是这个大小写，这里放宽只是防御性的）。解析
# 不到时返回 $null（调用方按"无法判断"处理）。
#
# 判断记录（2026-09-23，1.67.0 发布门禁实测翻红后修）：开关名自身后面必须允许一个可选的闭合
# 引号。按逐参数加引号的方式启动进程时，命令行形如
#   "...\Unity.exe" "-batchmode" "-projectPath" "D:\some\project" ...
# 此时 "-projectPath" 后面紧跟的是引号而不是空白，原来的 -projectPath 加 \s+ 匹配不上，整条
# 命令行被判成"没有 -projectPath" -> Unknown -> 保守等待，于是别的仓库里正常运行的 Unity 批
# 处理也会被等满超时并判失败（实测：1.67.0 发布门禁的消费方演练因此连挂 3 步）。
function Get-ProjectPathFromCommandLine {
    param([string]$CommandLine)
    if ([string]::IsNullOrEmpty($CommandLine)) {
        return $null
    }
    $match = [System.Text.RegularExpressions.Regex]::Match(
        $CommandLine,
        '(?i)-projectPath"?\s+("(?<quoted>[^"]*)"|(?<bare>\S+))'
    )
    if (-not $match.Success) {
        return $null
    }
    if ($match.Groups['quoted'].Success) {
        return $match.Groups['quoted'].Value
    }
    return $match.Groups['bare'].Value
}

# 主入口：三态返回值——
#   "Wait"    ：-projectPath 落在本仓库根或本脚本工作目录之下（等）。
#   "NoWait"  ：-projectPath 明确落在上述两处之外（别的工程，不等）。
#   "Unknown" ：拿不到命令行，或命令行里没有 -projectPath（无法判断归属，调用方按保守口径当作
#               "要等"处理，但用独立返回值区分开，不与真正判定为"本工程"的 Wait 混为一谈）。
function Get-UnitySmokeProcessWaitDecision {
    param(
        [string]$CommandLine,
        [string]$RepoRoot,
        [string]$WorkDir
    )
    $projectPath = Get-ProjectPathFromCommandLine -CommandLine $CommandLine
    if ([string]::IsNullOrEmpty($projectPath)) {
        return "Unknown"
    }
    if (Test-FsPathIsUnderOrEqual -CandidatePath $projectPath -AncestorPath $RepoRoot) {
        return "Wait"
    }
    if (Test-FsPathIsUnderOrEqual -CandidatePath $projectPath -AncestorPath $WorkDir) {
        return "Wait"
    }
    return "NoWait"
}
