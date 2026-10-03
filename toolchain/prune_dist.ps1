<#
.SYNOPSIS
    dist/ 瘦身：只留当前发布需要的部分，历史版本的大产物删除（可随时按标签重建）。

.DESCRIPTION
    `dist/` 是 `.gitignore` 排除的本机构建缓存（每个版本约 100～300 MB，整个目录可累积到十几 GB）。
    消费方走私服（registry 通道）或 GitHub Release 附件，不读本机 `dist/`；`toolchain/_dist_immutability_guard.ps1`
    判定"已发布"靠 `git tag`，不靠 `dist/` 下有没有文件——所以清掉历史版本产物既不影响消费方，也不会让
    不可变发布守卫误放行。

    只在 `<RepoRoot>\dist` 这一层按下面的形态操作（形态里 `<ver>` 是 `X.Y.Z`，带 `-dryrun` 后缀的是
    `build.ps1 -Release -DryRun` / `-Dist X.Y.Z-dryrun` 的验证产物）：

      目录   `<ver>\`、`<ver>-dryrun\`
      文件   `ws-game-<ver>.zip`、`ws-game-<ver>-samples.zip`、`ws-game-<ver>.lock`、
             `release-notes-<ver>.txt`，以及它们的 `-dryrun` 形态

    保留规则（按序判定）：

      1. 保留版本 = `VERSION` 文件里的当前版本 + `dist/` 里按语义化版本排序紧邻其下的若干个正式版本，
         合计 `-KeepVersions` 个（默认 2）。保留版本的 `<ver>\`、`ws-game-<ver>.zip`、
         `ws-game-<ver>-samples.zip` 全留。"正式版本"指 `dist/` 里有非 dryrun 的 `<ver>\` 目录或
         `ws-game-<ver>.zip` 的版本。
      2. 所有版本的 `ws-game-<ver>.lock` 与 `release-notes-<ver>.txt` 一律保留（很小，是发布记录）。
      3. 所有带 `-dryrun` 的目录与文件一律删除（包括当前版本的；门禁每次会重打）。
      4. 非保留版本的 `<ver>\`、`ws-game-<ver>.zip`、`ws-game-<ver>-samples.zip` 删除。
      5. `tag-message-<ver>.txt`（`build.ps1 -Release` 打标签失败时残留的标签说明文件）：标签 `v<ver>` 已存在则删，
         不存在则按"未识别"保留（可能是眼下失败的发布现场）。
      6. 认不出形态的条目不动，输出里单列"未识别，已保留"。

    在任务书规则之外多保留两类（宁少删勿多删）：

      - ABI 基线发行包：`toolchain/abi_probe_baseline.txt` 记录的版本（人工维护的"最后一个已知二进制兼容"
        锚点，当前为 1.12.0）的 `ws-game-<基线版本>.zip`。`build.ps1 -Release` 固定带 `-AbiStrict` 跑
        `abi_probe.ps1`，pytest 的真实基线端到端用例也读它；删了发布会 FAIL、门禁会因 skip 上限为 0 而红。
        只保留 zip，不保留该版本的 `<ver>\` 目录与 samples 包。
      - 高于当前 `VERSION` 的版本（发布中途残留，或维护分支上 `VERSION` 低于 main 的场景）：不动。

    安全：
      - `dist` 本身是重解析点（junction/symlink）时拒绝并报错退出；待删条目自身或其内部任何一层
        含重解析点时同样拒绝（整次运行不删任何东西，先报错）。
      - 每个待删条目的完整路径解析后必须仍在 `<RepoRoot>\dist\` 之下才删。
      - 不传 `-Apply` 只列清单，什么都不删。

    输出：每类（删除/保留 × 目录/zip/samples zip/dryrun…）的条目数与字节数（删除前实测大小求和）、
    删除清单、保留的大产物清单、未识别条目清单。退出码：0 正常；1 拒绝（重解析点、VERSION 无效、
    删除失败等）。

    历史版本怎么重建：检出对应标签（`git worktree add D:\wt\<name> v<ver>`，或在维护分支上），
    运行 `build.ps1 -Dist <ver>` 重建 `dist/<ver>/` 目录：`v<ver>` 标签已存在，发布不可变守卫会拒绝，所以哪怕不加 `-Zip`
    也必须带 `-AllowOverwriteDist`（该开关只跳过守卫并打印将被覆盖的产物清单）；`-Zip` 才会重写 zip/lock/samples zip，覆盖
    本机同名原件（ABI 基线版本的 zip 不要重打），只为查阅历史内容时不需要。
    注意：重建出的 zip 的 sha 不保证与当年发布时的 `.lock` 相同（打包时间戳、压缩实现等不保证逐字节
    一致），所以 `.lock` 仅作发布记录，不能用来校验重建出的 zip。

    自动触发：`build.ps1 -Release`（非 `-DryRun`）全部成功并打完标签之后，尽力而为地调用本脚本
    `-Apply`（默认保留 2 个版本）；失败只警告，不影响发布结果与退出码。手动：
    `pwsh toolchain\prune_dist.ps1 -Apply`（先不带 `-Apply` 看清单）。

.PARAMETER RepoRoot
    仓库根目录（其下的 `dist\` 是操作对象）。默认脚本所在仓库根。并行会话的工作树里没有 `dist\`，
    清理主检出的 `dist` 时用 `-RepoRoot D:\workespace\ws-game` 指过去。

.PARAMETER KeepVersions
    保留的版本个数（含当前版本），默认 2，不小于 1。

.PARAMETER Apply
    真正删除。不传只打印清单。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。本文件含中文，UTF-8 with BOM。
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [int]$KeepVersions = 2,
    [switch]$Apply
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\', '/')

function Write-PruneLine {
    param([string]$Message)
    Write-Host "[prune_dist] $Message"
}

function Stop-Prune {
    param([string]$Message)
    Write-Host "[prune_dist] 错误：$Message" -ForegroundColor Red
    exit 1
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1GB) { return ("{0:N2} GB" -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ("{0:N1} MB" -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ("{0:N1} KB" -f ($Bytes / 1KB)) }
    return ("{0} B" -f $Bytes)
}

function Test-IsReparsePoint {
    param([System.IO.FileSystemInfo]$Info)
    return (($Info.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

# 实测一个条目的大小，同时找内部的重解析点（不下钻进重解析点）。返回
# @{ Bytes; Files; Reparse = 内部（含自身）重解析点路径列表; Errors = 无法枚举的路径列表 }。
function Measure-DistEntry {
    param([System.IO.FileSystemInfo]$Info)
    $result = @{ Bytes = [long]0; Files = 0; Reparse = (New-Object System.Collections.Generic.List[string]); Errors = (New-Object System.Collections.Generic.List[string]) }
    if (Test-IsReparsePoint $Info) {
        $result.Reparse.Add($Info.FullName)
        return $result
    }
    if (-not $Info.PSIsContainer) {
        $result.Bytes = [long]$Info.Length
        $result.Files = 1
        return $result
    }
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($Info.FullName)
    while ($stack.Count -gt 0) {
        $dirPath = $stack.Pop()
        try {
            $dirInfo = New-Object System.IO.DirectoryInfo($dirPath)
            foreach ($child in $dirInfo.EnumerateFileSystemInfos()) {
                if (Test-IsReparsePoint $child) {
                    $result.Reparse.Add($child.FullName)
                    continue
                }
                if ($child -is [System.IO.DirectoryInfo]) {
                    $stack.Push($child.FullName)
                } else {
                    $result.Bytes += [long]$child.Length
                    $result.Files++
                }
            }
        } catch {
            $result.Errors.Add($dirPath + "（" + $_.Exception.Message + "）")
        }
    }
    return $result
}

# 本仓库里标签 v<ver> 是否已存在（取不到 git、不是仓库一律当作"不存在"，宁可多留）。求值时临时摘掉预提交钩子
# 注入的 GIT_* 变量，做法同 _abi_baseline_resolve.ps1 的 Get-MainWorktreeRoot（Env: 驱动器删除而非置空串）。
function Test-ReleaseTagExists {
    param([string]$RepoRoot, [string]$Version)
    $hazardVars = @("GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_PREFIX")
    $saved = @{}
    foreach ($name in $hazardVars) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
        Remove-Item -LiteralPath ("Env:\" + $name) -ErrorAction SilentlyContinue
    }
    try {
        & git -C $RepoRoot rev-parse -q --verify ("refs/tags/v" + $Version) 2>$null | Out-Null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    } finally {
        foreach ($name in $hazardVars) {
            if ($null -ne $saved[$name]) {
                Set-Item -LiteralPath ("Env:\" + $name) -Value $saved[$name]
            }
        }
    }
}

function Remove-DistEntry {
    param([System.IO.FileSystemInfo]$Info)
    if ($Info.PSIsContainer) {
        try {
            [System.IO.Directory]::Delete($Info.FullName, $true)
        } catch {
            # 只读文件等会让 .NET 递归删除失败，换 Remove-Item -Force 再试一次。
            Remove-Item -LiteralPath $Info.FullName -Recurse -Force -ErrorAction Stop
        }
    } else {
        Remove-Item -LiteralPath $Info.FullName -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $Info.FullName) {
        throw "删除后条目仍然存在：$($Info.FullName)"
    }
}

if ($KeepVersions -lt 1) {
    Stop-Prune "-KeepVersions 不能小于 1（当前值 $KeepVersions）。"
}

$distDir = Join-Path $RepoRoot "dist"
if (-not (Test-Path -LiteralPath $distDir)) {
    Write-PruneLine "$distDir 不存在，无事可做。"
    exit 0
}
$distItem = Get-Item -LiteralPath $distDir -Force
if (-not $distItem.PSIsContainer) {
    Stop-Prune "$distDir 不是目录。"
}
if (Test-IsReparsePoint $distItem) {
    Stop-Prune "$distDir 是重解析点（junction/symlink），拒绝操作。"
}
$distFull = [System.IO.Path]::GetFullPath($distDir).TrimEnd('\', '/')
$distPrefix = $distFull + "\"

# 当前版本
$versionFile = Join-Path $RepoRoot "VERSION"
if (-not (Test-Path -LiteralPath $versionFile)) {
    Stop-Prune "找不到 $versionFile，无法确定当前版本，未删除任何内容。"
}
$currentText = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($currentText -notmatch '^\d+\.\d+\.\d+$') {
    Stop-Prune "VERSION 内容不是 X.Y.Z 格式：'$currentText'，未删除任何内容。"
}
$currentVersion = [version]$currentText

# ABI 基线版本（可选；文件缺失或格式不对则不额外保留，不报错）
$baselineVersion = $null
$baselineFile = Join-Path $RepoRoot "toolchain\abi_probe_baseline.txt"
if (Test-Path -LiteralPath $baselineFile) {
    $baselineText = (Get-Content -LiteralPath $baselineFile -Raw).Trim()
    if ($baselineText -match '^\d+\.\d+\.\d+$') {
        $baselineVersion = [version]$baselineText
    }
}

# --- 分类 -------------------------------------------------------------------
$reDir = '^(\d+\.\d+\.\d+)(-dryrun)?$'
$reZip = '^ws-game-(\d+\.\d+\.\d+)(-dryrun)?(-samples)?\.zip$'
$reLock = '^ws-game-(\d+\.\d+\.\d+)(-dryrun)?\.lock$'
$reNotes = '^release-notes-(\d+\.\d+\.\d+)(-dryrun)?\.txt$'
$reTagMsg = '^tag-message-(\d+\.\d+\.\d+)\.txt$'

$entries = New-Object System.Collections.Generic.List[object]
foreach ($item in (Get-ChildItem -LiteralPath $distDir -Force)) {
    $kind = "Unknown"
    $ver = $null
    $isDry = $false
    $name = $item.Name
    if ($item.PSIsContainer) {
        if ($name -match $reDir) {
            $kind = "Dir"; $ver = [version]$Matches[1]; $isDry = [bool]$Matches[2]
        }
    } else {
        if ($name -match $reZip) {
            $ver = [version]$Matches[1]; $isDry = [bool]$Matches[2]
            if ($Matches[3]) { $kind = "SamplesZip" } else { $kind = "Zip" }
        } elseif ($name -match $reLock) {
            $kind = "Lock"; $ver = [version]$Matches[1]; $isDry = [bool]$Matches[2]
        } elseif ($name -match $reNotes) {
            $kind = "Notes"; $ver = [version]$Matches[1]; $isDry = [bool]$Matches[2]
        } elseif ($name -match $reTagMsg) {
            $kind = "TagMessage"; $ver = [version]$Matches[1]
        }
    }
    $entries.Add([PSCustomObject]@{
        Info = $item; Name = $name; Kind = $kind; Version = $ver; DryRun = $isDry
        Action = "Keep"; Category = ""; Reason = ""; Bytes = [long]0; Files = 0
    })
}

# --- 保留版本 -----------------------------------------------------------------
$formalVersions = New-Object System.Collections.Generic.List[version]
foreach ($e in $entries) {
    if (($e.Kind -eq "Dir" -or $e.Kind -eq "Zip") -and (-not $e.DryRun)) {
        if (-not $formalVersions.Contains($e.Version)) { $formalVersions.Add($e.Version) }
    }
}
$keepSet = New-Object System.Collections.Generic.List[version]
$keepSet.Add($currentVersion)
$lowerVersions = @($formalVersions | Where-Object { $_ -lt $currentVersion } | Sort-Object -Descending)
foreach ($v in ($lowerVersions | Select-Object -First ($KeepVersions - 1))) {
    $keepSet.Add($v)
}

# --- 判定 ---------------------------------------------------------------------
foreach ($e in $entries) {
    if ($e.Kind -eq "Unknown") {
        $e.Action = "Keep"; $e.Category = "未识别"; $e.Reason = "认不出形态，不动"
        continue
    }
    if ($e.Kind -eq "TagMessage") {
        # build.ps1 -Release 打标签前写 dist\tag-message-<ver>.txt、打成功后立刻删；留下来说明打标签失败过。
        # 标签 v<ver> 已经存在 = 这份残留早已没用（重试发布会整份重写它）-> 删；标签不存在 = 可能是眼下
        # 失败的发布现场 -> 按"未识别"保留，交人判断。
        if (Test-ReleaseTagExists -RepoRoot $RepoRoot -Version $e.Version.ToString()) {
            $e.Action = "Delete"; $e.Category = "打标签残留"; $e.Reason = "标签 v$($e.Version) 已存在，残留的标签说明文件无用"
        } else {
            $e.Action = "Keep"; $e.Category = "未识别"; $e.Reason = "标签 v$($e.Version) 不存在，可能是失败的发布现场，不动"
        }
        continue
    }
    if ($e.DryRun) {
        $e.Action = "Delete"; $e.Category = "dryrun 产物"; $e.Reason = "-dryrun 验证产物，门禁每次重打"
        continue
    }
    if ($e.Kind -eq "Lock" -or $e.Kind -eq "Notes") {
        $e.Action = "Keep"; $e.Category = "发布记录"; $e.Reason = "lock / release-notes 一律保留"
        continue
    }
    # Dir / Zip / SamplesZip，非 dryrun
    $catName = switch ($e.Kind) { "Dir" { "版本目录" } "Zip" { "发行 zip" } default { "samples zip" } }
    if ($keepSet.Contains($e.Version)) {
        $e.Action = "Keep"; $e.Category = $catName; $e.Reason = "保留版本"
    } elseif ($e.Version -gt $currentVersion) {
        $e.Action = "Keep"; $e.Category = $catName; $e.Reason = "高于当前 VERSION，不动"
    } elseif ($e.Kind -eq "Zip" -and $null -ne $baselineVersion -and $e.Version -eq $baselineVersion) {
        $e.Action = "Keep"; $e.Category = $catName; $e.Reason = "ABI 基线发行包（abi_probe_baseline.txt）"
    } else {
        $e.Action = "Delete"; $e.Category = $catName; $e.Reason = "非保留版本"
    }
}

# --- 实测大小 + 重解析点预检（整次运行先全部验完再删）--------------------------------
$measureErrors = New-Object System.Collections.Generic.List[string]
$reparseFound = New-Object System.Collections.Generic.List[string]
foreach ($e in $entries) {
    if ($e.Kind -eq "Unknown") { continue }
    $m = Measure-DistEntry -Info $e.Info
    $e.Bytes = $m.Bytes
    $e.Files = $m.Files
    if ($e.Action -eq "Delete") {
        foreach ($r in $m.Reparse) { $reparseFound.Add($r) }
        foreach ($er in $m.Errors) { $measureErrors.Add($er) }
        $full = [System.IO.Path]::GetFullPath($e.Info.FullName)
        if (-not $full.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            Stop-Prune "待删条目解析后不在 $distFull 之下：$full，未删除任何内容。"
        }
    }
}
if ($reparseFound.Count -gt 0) {
    Write-Host "[prune_dist] 以下路径是重解析点（junction/symlink），拒绝操作：" -ForegroundColor Red
    foreach ($r in $reparseFound) { Write-Host "  $r" -ForegroundColor Red }
    Stop-Prune "待删条目含重解析点，未删除任何内容。"
}
if ($measureErrors.Count -gt 0) {
    Write-Host "[prune_dist] 以下路径无法枚举：" -ForegroundColor Red
    foreach ($er in $measureErrors) { Write-Host "  $er" -ForegroundColor Red }
    Stop-Prune "待删条目有无法枚举的内容，未删除任何内容。"
}

# --- 报告 ---------------------------------------------------------------------
$modeText = if ($Apply) { "执行删除（-Apply）" } else { "只列清单（未传 -Apply，不删除任何内容）" }
Write-PruneLine "模式：$modeText"
Write-PruneLine "RepoRoot=$RepoRoot"
Write-PruneLine "dist=$distFull 条目总数=$($entries.Count)"
Write-PruneLine "VERSION=$currentText -KeepVersions=$KeepVersions"
Write-PruneLine ("保留版本：" + (($keepSet | ForEach-Object { $_.ToString() }) -join ", "))
if ($null -ne $baselineVersion) {
    Write-PruneLine "ABI 基线版本（额外保留其发行 zip）：$baselineVersion"
}

Write-Host ""
Write-PruneLine "分类汇总（条目数 / 实测字节）"
$groups = $entries | Group-Object -Property Action, Category | Sort-Object Name
foreach ($g in $groups) {
    $sum = [long]0
    foreach ($x in $g.Group) { $sum += $x.Bytes }
    $act = if ($g.Group[0].Action -eq "Delete") { "删除" } else { "保留" }
    Write-Host ("  {0}  {1,-12} {2,5} 项  {3,12}  ({4} 字节)" -f $act, $g.Group[0].Category, $g.Count, (Format-Size $sum), $sum)
}

$toDelete = @($entries | Where-Object { $_.Action -eq "Delete" })
$deleteBytes = [long]0
foreach ($e in $toDelete) { $deleteBytes += $e.Bytes }
$toKeep = @($entries | Where-Object { $_.Action -eq "Keep" })
$keepBytes = [long]0
foreach ($e in $toKeep) { $keepBytes += $e.Bytes }

Write-Host ""
Write-PruneLine ("待删合计：{0} 项，{1}（{2} 字节）" -f $toDelete.Count, (Format-Size $deleteBytes), $deleteBytes)
Write-PruneLine ("保留合计：{0} 项，{1}（{2} 字节）" -f $toKeep.Count, (Format-Size $keepBytes), $keepBytes)

Write-Host ""
Write-PruneLine "保留清单（大产物；lock / release-notes 发布记录不逐项列出）"
$keptHeavy = @($toKeep | Where-Object { $_.Category -ne "发布记录" -and $_.Category -ne "未识别" } | Sort-Object Name)
foreach ($e in $keptHeavy) {
    Write-Host ("  保留  {0,-34} {1,12}  {2}" -f $e.Name, (Format-Size $e.Bytes), $e.Reason)
}
$recordCount = @($toKeep | Where-Object { $_.Category -eq "发布记录" }).Count
Write-Host "  保留  发布记录（lock / release-notes）共 $recordCount 项"

$unknown = @($toKeep | Where-Object { $_.Category -eq "未识别" })
Write-Host ""
Write-PruneLine "未识别，已保留：$($unknown.Count) 项"
foreach ($e in $unknown) { Write-Host "  未识别，已保留  $($e.Name)" }

Write-Host ""
Write-PruneLine "删除清单：$($toDelete.Count) 项"
foreach ($e in ($toDelete | Sort-Object Name)) {
    Write-Host ("  删除  {0,-34} {1,12}  {2}" -f $e.Name, (Format-Size $e.Bytes), $e.Category)
}

if (-not $Apply) {
    Write-Host ""
    Write-PruneLine "未传 -Apply：以上只是清单，未删除任何内容。"
    exit 0
}

# --- 执行 ---------------------------------------------------------------------
Write-Host ""
Write-PruneLine "开始删除..."
$deletedCount = 0
$deletedBytes = [long]0
$failed = New-Object System.Collections.Generic.List[string]
foreach ($e in $toDelete) {
    try {
        Remove-DistEntry -Info $e.Info
        $deletedCount++
        $deletedBytes += $e.Bytes
    } catch {
        $failed.Add($e.Name + "：" + $_.Exception.Message)
        Write-Host "  删除失败  $($e.Name)：$($_.Exception.Message)" -ForegroundColor Red
    }
}
Write-Host ""
Write-PruneLine ("已删除 {0} / {1} 项，释放 {2}（{3} 字节，按删除前实测大小求和）" -f $deletedCount, $toDelete.Count, (Format-Size $deletedBytes), $deletedBytes)
if ($failed.Count -gt 0) {
    Stop-Prune ("有 {0} 项删除失败（见上方），其余已删除。" -f $failed.Count)
}
exit 0
