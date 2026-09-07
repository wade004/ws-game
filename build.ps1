<#
.SYNOPSIS
    构建 Core.sln，把六个核心 DLL 同步进 Unity 工作台工程的引擎适配层包，
    并可选地打一份分发包到 dist/<version>/。

.PARAMETER Configuration
    dotnet 构建配置，默认 Release。

.PARAMETER SkipTests
    跳过 dotnet test。

.PARAMETER SyncOnly
    跳过 dotnet build/test，只把 core\*\bin\$Configuration\netstandard2.1\ 下已有的构建产物
    同步进 Runtime/Plugins/Core/（要求这些产物已经存在，即之前至少成功 build 过一次）。
    同步步骤本身（无论是否传 -SyncOnly）一律按文件哈希比较，未变化的 DLL 不重新拷贝，
    避免每次都触发 Unity 重新导入全部六个 DLL。

.PARAMETER SyncContent
    U2-1 新增。内容数据集同步（data/_sample、assets/_placeholder、assets/_sample -> Unity 工程
    Assets/StreamingAssets/GameFoundation/）本身在默认构建流程与 -SyncOnly 下都会无条件执行
    （见下）；本开关单独传且不带 -SyncOnly 时，额外跳过 dotnet build/test 与 DLL 同步两步，
    只做内容同步（"只改了 data/_sample 或 assets/_placeholder、assets/_sample、没有改任何 C# 代码"
    时的快速路径）。内容同步一律按文件哈希比较、只拷变化的文件，并镜像删除源目录里已经不存在、
    但上次同步残留在目标目录里的文件。

.PARAMETER Dist
    版本可追溯任务新增语义（见 11_工程规范与测试.md 第 7 节"版本号必须可追溯到对应的架构文档
    版本与数据 schema 版本组合"；单一版本源见仓库根 VERSION 文件）：
      - 传入具体版本号（如 0.2.0）：以参数为准，并校验格式必须形如 X.Y.Z（三段纯数字，用点号
        分隔），格式非法直接报错退出，不落地任何 dist 产物。
      - 传入字面量 "auto"：不由调用方指定具体版本号，改为读取仓库根 VERSION 文件的内容作为
        本次打包版本（"不传版本时读它"——PowerShell 5.1 的 [string] 类型参数无法在完全不给值的
        情况下识别"传了这个开关但没给值"，因此用 "auto" 这个不会是合法版本号的字面量表达
        "不指定版本、从单一版本源取值"这一语义，同时保留 `-Dist <显式版本号>` 这一行之前就有、
        任务验收命令仍在用的调用方式不变）。
      - 不传（默认空字符串）：跳过打包步骤，行为与此前一致。
    打包时会把最终解析出的版本号写入 dist 内两个 package.json 的 version 字段（含
    games/_template/package.json 对适配层包的依赖版本号），并生成扩展后的 MANIFEST.txt
    （version/date/git_commit/各目录文件数/architecture_docs/data_schemas/core_assemblies）。

.PARAMETER Release
    版本管理方案新增：走一次完整的"发布"流程（校验 -> 更新版本号 -> 全量门禁 -> 打包 -> 提交 ->
    打标签），产出可直接对外发布的版本快照。传入目标版本号（形如 X.Y.Z），流程：
      1. 校验版本号格式，且必须严格大于仓库根 VERSION 文件当前值（语义化版本数值比较，不是字符串
         比较）。
      2. 校验 `git status --porcelain` 为空（工作树干净），否则报错退出——发布快照必须对应一个
         干净的提交状态，不能夹带未提交的改动。
      3. 校验仓库根 CHANGELOG.md 已存在形如 `## [X.Y.Z]` 的条目（不含该条目直接报错退出，提示先
         在 CHANGELOG.md 补齐该版本的变更记录）。
      4. 非 `-DryRun` 时：把该版本号写回仓库根 VERSION 文件与两个 package.json（含
         `games/_template/package.json` 对适配层包的依赖版本号）——这一步是本次发布"成为新的当前
         版本"的唯一写入点，`-DryRun` 时跳过，不触碰任何源码文件。
      5. 跑一遍 `check.ps1`（默认全量，含 Unity 相关步骤与消费方演练；`-ReleaseSkipUnity` 传
         `-SkipUnity` 给 check.ps1，用于没有装 Unity 的机器，但默认要求全量门禁通过才能发布）。
      6. 打包 `dist/<ver>/`（复用 `-Dist` 打包逻辑）、`dist/ws-game-<ver>.zip`（`Compress-Archive`，
         zip 内顶层目录为 `ws-game-<ver>/`）与 `dist/ws-game-<ver>.lock`（版本号、git_commit、
         六个核心 DLL 的 sha256，供游戏仓库复制为自己的 `ws-game.lock`）。
      7. 非 `-DryRun` 时：提交 VERSION/两个 package.json/CHANGELOG.md 的改动（提交信息
         `发布 <ver>`），打带注释标签 `v<ver>`（标签信息取 CHANGELOG.md 该版本条目正文），并在
         `dist/release-notes-<ver>.txt` 落一份同样内容供 `gh release create --notes-file` 使用。
      8. 打印后续需要人工/设计层执行的两条命令（`git push origin main --tags` 与
         `gh release create v<ver> ...`）；若版本号的 MAJOR 或 MINOR 段发生了变化（而不仅是
         PATCH 递增），额外打印建议的维护分支创建命令 `git branch release/X.Y.x vX.Y.0`（见仓库根
         README.md"维护分支与 PATCH 发布流程"一节）。

.PARAMETER DryRun
    仅与 `-Release` 同传有效。跑完上面第 1～3、5、6 步的全部校验与打包（打包目标目录/文件名额外带
    `-dryrun` 后缀，如 `dist/1.0.0-dryrun/`、`dist/ws-game-1.0.0-dryrun.zip`，避免与真实发布产物
    混淆或互相覆盖），但跳过第 4、7 步——不改写 VERSION/package.json/CHANGELOG.md、不
    `git commit`、不 `git tag`。用于在真正发布前验证整条发布流水线是否能跑通。

.PARAMETER Publish
    仅与 `-Release`（且未传 `-DryRun`）同传有效。第 7 步打完标签后，自动依次执行第 8 步打印的两条
    命令（`git push origin main --tags`、`gh release create ...`），不再需要人工另行复制粘贴执行。
    省略时（默认）只打印这两条命令，不自动执行，由人工/设计层确认后自行运行。

.PARAMETER ReleaseSkipUnity
    仅与 `-Release` 同传有效。第 5 步跑 `check.ps1` 时额外传 `-SkipUnity`，跳过 Unity 相关四步与
    消费方演练（没有装 Unity 或 Unity 被占用的机器上用）。省略时（默认）要求 `check.ps1` 全量通过
    才能发布——"发布"这个动作本身就意味着要对外承诺质量，默认不放宽。

.PARAMETER Zip
    独立于 `-Release` 使用：与 `-Dist`/`-Dist auto` 同传时，额外打一份 `dist/ws-game-<ver>.zip`
    与 `dist/ws-game-<ver>.lock`（与 `-Release` 第 6 步同一份打包逻辑），但不做 `-Release`
    的版本号校验、写回、`check.ps1` 门禁、提交、打标签——只是"把已经存在的 dist/<ver>/ 目录再打成
    zip+lock 两个可上传附件"这一件事。用途：`.github/workflows/release.yml` 在 CI 里对一个已经由
    本机 `-Release`（未传 `-Publish`）提交并打好标签的版本重新打包上传附件，这种场景不需要也不
    应该重新走版本号写回/提交/打标签（那些已经在本机完成）。`-Release` 本身已经隐含这份打包
    （不需要再显式传 `-Zip`）。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SyncOnly,
    [switch]$SyncContent,
    [string]$Dist = "",
    [string]$Release = "",
    [switch]$DryRun,
    [switch]$Publish,
    [switch]$ReleaseSkipUnity,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot "Core.sln"
$PluginsCoreDir = Join-Path $RepoRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"
$VersionFilePath = Join-Path $RepoRoot "VERSION"
$VersionFormatPattern = '^\d+\.\d+\.\d+$'

# 版本可追溯任务新增：单一版本源读取 + -Dist 参数解析（见上方 .PARAMETER Dist 说明）。
# 校验在脚本一开始就做（哪怕本次调用根本不带 -Dist），提前暴露 VERSION 文件本身格式错误。
function Get-FrameworkVersionFromFile {
    if (-not (Test-Path $VersionFilePath)) {
        throw "找不到版本文件：$VersionFilePath（单一版本源，见 11_工程规范与测试.md 第 7 节）"
    }
    $v = (Get-Content -Path $VersionFilePath -Raw).Trim()
    if ($v -notmatch $VersionFormatPattern) {
        throw "$VersionFilePath 内容格式非法：'$v'（需形如 X.Y.Z）"
    }
    return $v
}

$DistRequested = ($Dist -ne "")
$ResolvedDistVersion = ""
$DistDirVersion = ""
if ($DistRequested) {
    if ($Dist -eq "auto") {
        $ResolvedDistVersion = Get-FrameworkVersionFromFile
        Write-Host "-Dist auto：从 $VersionFilePath 读取版本号 -> $ResolvedDistVersion" -ForegroundColor Cyan
    } else {
        if ($Dist -notmatch $VersionFormatPattern) {
            Write-Host "-Dist 版本号格式非法：'$Dist'（需形如 X.Y.Z，或传 'auto' 从 VERSION 文件读取）" -ForegroundColor Red
            exit 1
        }
        $ResolvedDistVersion = $Dist
    }
    $DistDirVersion = $ResolvedDistVersion
}

# -----------------------------------------------------------------------------
# 版本管理方案新增：-Release 发布流程（见上方 .PARAMETER Release/DryRun/Publish/ReleaseSkipUnity
# 说明）。本节只做第 1～3 步的前置校验并把 -Release 转译成等价的 -Dist 请求（复用下方既有的打包
# 逻辑，只是打包目录名在 -DryRun 时额外带 -dryrun 后缀，见 $DistDirVersion）；第 4 步（写回源码
# 版本号）紧跟本节之后单独一节；check.ps1 门禁、zip/lock 产物、提交与打标签在打包完成之后（脚本
# 末尾）另起一节，因为它们依赖打包已经产出的 dist/<dir>/ 目录与其中的 DLL 哈希。
# -----------------------------------------------------------------------------
$ReleaseRequested = ($Release -ne "")

if ((-not $ReleaseRequested) -and ($DryRun -or $Publish -or $ReleaseSkipUnity)) {
    Write-Host "-DryRun/-Publish/-ReleaseSkipUnity 仅在同传 -Release 时有效" -ForegroundColor Red
    exit 1
}
if ($DryRun -and $Publish) {
    Write-Host "-DryRun 与 -Publish 不能同传（-DryRun 语义上不产生任何可发布的提交/标签）" -ForegroundColor Red
    exit 1
}
if ($Zip -and (-not $DistRequested) -and (-not $ReleaseRequested)) {
    Write-Host "-Zip 需要同传 -Dist/-Dist auto（或 -Release，其本身已隐含 -Zip 的效果）——没有 dist/<ver>/ 目录可打包" -ForegroundColor Red
    exit 1
}

# 用于判断"MAJOR/MINOR 是否变化"（决定是否建议开维护分支）与语义化版本数值比较（不能用字符串比较，
# 例如 "10.0.0" 按字符串比较会小于 "9.0.0"）。
function ConvertTo-SemVerParts {
    param([string]$Version)
    $segments = $Version -split '\.'
    return [PSCustomObject]@{
        Major = [int]$segments[0]
        Minor = [int]$segments[1]
        Patch = [int]$segments[2]
    }
}

# 返回 -1/0/1，语义同 [string]::Compare 但按数值逐段比较。
function Compare-SemVer {
    param([string]$VersionA, [string]$VersionB)
    $a = ConvertTo-SemVerParts -Version $VersionA
    $b = ConvertTo-SemVerParts -Version $VersionB
    if ($a.Major -ne $b.Major) { if ($a.Major -gt $b.Major) { return 1 } else { return -1 } }
    if ($a.Minor -ne $b.Minor) { if ($a.Minor -gt $b.Minor) { return 1 } else { return -1 } }
    if ($a.Patch -ne $b.Patch) { if ($a.Patch -gt $b.Patch) { return 1 } else { return -1 } }
    return 0
}

$ReleaseBumpIsMajorOrMinor = $false
$ReleaseChangelogSection = ""
$ReleaseCurrentVersion = ""

if ($ReleaseRequested) {
    Write-Step "-Release $Release：发布流程前置校验"

    # 第 1 步：版本号格式 + 严格大于当前 VERSION。
    if ($Release -notmatch $VersionFormatPattern) {
        Write-Host "-Release 版本号格式非法：'$Release'（需形如 X.Y.Z）" -ForegroundColor Red
        exit 1
    }
    $ReleaseCurrentVersion = Get-FrameworkVersionFromFile
    $cmp = Compare-SemVer -VersionA $Release -VersionB $ReleaseCurrentVersion
    if ($cmp -le 0) {
        Write-Host "目标版本 $Release 必须严格大于当前版本 $ReleaseCurrentVersion（语义化版本数值比较）" -ForegroundColor Red
        exit 1
    }
    $oldParts = ConvertTo-SemVerParts -Version $ReleaseCurrentVersion
    $newParts = ConvertTo-SemVerParts -Version $Release
    if (($newParts.Major -ne $oldParts.Major) -or ($newParts.Minor -ne $oldParts.Minor)) {
        $ReleaseBumpIsMajorOrMinor = $true
    }
    Write-Host "  版本号校验通过：$ReleaseCurrentVersion -> $Release"

    # 第 2 步：工作树必须干净（发布快照不能夹带未提交的改动）。DryRun 同样校验——DryRun 的目的是
    # 验证"整条发布流水线打完收工时工作树会是什么状态"，跳过这一步校验会让 DryRun 失去意义。
    Push-Location $RepoRoot
    try {
        $releaseGitStatus = & git status --porcelain
        $releaseGitDirty = $false
        if ($null -ne $releaseGitStatus) {
            $releaseGitStatusJoined = ($releaseGitStatus -join "`n").Trim()
            if ($releaseGitStatusJoined -ne "") { $releaseGitDirty = $true }
        }
    } finally {
        Pop-Location
    }
    if ($releaseGitDirty) {
        Write-Host "工作树不干净（git status --porcelain 非空），发布前请先提交或清理改动" -ForegroundColor Red
        exit 1
    }
    Write-Host "  git 工作树干净：校验通过"

    # 第 3 步：CHANGELOG.md 必须已有该版本的条目（形如 "## [X.Y.Z]"，允许行尾附日期等其余文本）。
    $changelogPath = Join-Path $RepoRoot "CHANGELOG.md"
    if (-not (Test-Path $changelogPath)) {
        Write-Host "找不到 $changelogPath，无法核对该版本的变更记录" -ForegroundColor Red
        exit 1
    }
    $changelogLines = Get-Content -Path $changelogPath -Encoding UTF8
    $headingPattern = '^##\s*\[' + [regex]::Escape($Release) + '\]'
    $headingIndex = -1
    for ($i = 0; $i -lt $changelogLines.Count; $i++) {
        if ($changelogLines[$i] -match $headingPattern) { $headingIndex = $i; break }
    }
    if ($headingIndex -lt 0) {
        Write-Host "CHANGELOG.md 缺少 '## [$Release]' 条目，请先在 CHANGELOG.md 中补充该版本的变更记录再发布" -ForegroundColor Red
        exit 1
    }
    $sectionEndIndex = $changelogLines.Count
    for ($i = $headingIndex + 1; $i -lt $changelogLines.Count; $i++) {
        if ($changelogLines[$i] -match '^##\s*\[') { $sectionEndIndex = $i; break }
    }
    $ReleaseChangelogSection = ($changelogLines[$headingIndex..($sectionEndIndex - 1)] -join "`n").Trim()
    Write-Host "  CHANGELOG.md 已找到 [$Release] 条目：校验通过"

    # -Dist 与 -Release 二选一：-Release 内部转译为一次 -Dist 请求，复用下方既有打包逻辑；
    # 若调用方同时显式传了 -Dist，以 -Release 为准（更明确的意图），并提示一句。
    if ($DistRequested -and ($ResolvedDistVersion -ne $Release)) {
        Write-Host "  同时传了 -Dist $Dist 与 -Release $Release，以 -Release 的版本号为准" -ForegroundColor Yellow
    }
    $DistRequested = $true
    $ResolvedDistVersion = $Release
    if ($DryRun) {
        $DistDirVersion = $Release + "-dryrun"
        Write-Host "  -DryRun：打包目录/产物名额外带 -dryrun 后缀（$DistDirVersion），不写回任何源码文件、不提交、不打标签"
    } else {
        $DistDirVersion = $Release
    }

    # 第 4 步：写回源码版本号（VERSION + 两个 package.json，含模板对适配层包的依赖版本号）。
    # DryRun 时跳过——这是本次发布"成为新的当前版本"的唯一写入点。
    if (-not $DryRun) {
        Write-Step "写回版本号 $Release -> VERSION、两个 package.json"

        # 判断记录：VERSION 文件是不带 BOM、不带尾随换行的纯 ASCII 文本（既有约定，见仓库根
        # VERSION 文件实际内容）；两个 package.json 是不带 BOM 的 UTF-8（含中文 description 字段）。
        # PowerShell 5.1 的 `Set-Content -Encoding utf8` 固定带 BOM，因此这里改用
        # [System.IO.File]::WriteAllText + 显式 UTF8Encoding($false) 避免污染已提交的源码文件
        # （与本文件下方 Set-DistPackageJsonVersion 只作用于 dist/ 构建产物、允许带 BOM 不同——
        # 那些是不入库的构建产物，这里是要提交进仓库的源码文件）。
        [System.IO.File]::WriteAllText($VersionFilePath, $Release, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  已写回 $VersionFilePath -> $Release"

        function Set-SourcePackageJsonVersion {
            param([string]$JsonPath, [string]$Version)
            if (-not (Test-Path $JsonPath)) {
                throw "找不到 $JsonPath，无法回写版本号"
            }
            $obj = (Get-Content -Path $JsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
            $obj.version = $Version
            if (($obj.PSObject.Properties.Name -contains "dependencies") -and
                ($obj.dependencies.PSObject.Properties.Name -contains "com.gamefoundation.adapter.unity")) {
                $obj.dependencies."com.gamefoundation.adapter.unity" = $Version
            }
            $jsonText = ($obj | ConvertTo-Json -Depth 10)
            [System.IO.File]::WriteAllText($JsonPath, $jsonText, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "  已写回 $JsonPath -> $Version"
        }

        Set-SourcePackageJsonVersion -JsonPath (Join-Path $RepoRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\package.json") -Version $Release
        Set-SourcePackageJsonVersion -JsonPath (Join-Path $RepoRoot "games\_template\package.json") -Version $Release
    }

    # 第 5 步：全量门禁（-ReleaseSkipUnity 时传 -SkipUnity 给 check.ps1）。DryRun 同样跑——
    # DryRun 的目的正是验证"发布流水线全流程能否走通"，门禁本身不写文件，天然安全。
    Write-Step "check.ps1 门禁（-Release 第 5 步）"
    $checkScript = Join-Path $RepoRoot "check.ps1"
    $checkArgs = @()
    if ($ReleaseSkipUnity) { $checkArgs += "-SkipUnity" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript @checkArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "check.ps1 未通过（退出码 $LASTEXITCODE），发布流程终止" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "  check.ps1 通过"
}

# 六个需要发布给 Unity 端的核心 DLL；不拷贝 Adapters.Stub、不拷贝任何测试或 xunit 相关程序集。
$CoreAssemblies = @(
    @{ Name = "Core.Foundation"; Dir = "core\foundation" },
    @{ Name = "Core.Numbers"; Dir = "core\numbers" },
    @{ Name = "Core.Rules"; Dir = "core\rules" },
    @{ Name = "Core.Carriers"; Dir = "core\carriers" },
    @{ Name = "Core.Gameplay"; Dir = "core\gameplay" },
    @{ Name = "Presentation.Common"; Dir = "presentation" }
)

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# 只有目标文件不存在或哈希不同才真正拷贝；返回 $true 表示发生了拷贝，$false 表示跳过。
# 用哈希而不是时间戳/文件大小比较，避免"内容相同但时间戳不同"（例如同一份产物被重复构建）
# 触发不必要的拷贝，从而不必要地让 Unity 重新导入插件 DLL（编辑器重新加载程序集很慢）。
function Copy-IfChanged {
    param(
        [string]$SourcePath,
        [string]$DestPath
    )

    if (Test-Path $DestPath) {
        $srcHash = (Get-FileHash -Path $SourcePath -Algorithm SHA256).Hash
        $dstHash = (Get-FileHash -Path $DestPath -Algorithm SHA256).Hash
        if ($srcHash -eq $dstHash) {
            return $false
        }
    }

    $destDir = Split-Path -Parent $DestPath
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }

    Copy-Item -Path $SourcePath -Destination $DestPath -Force
    return $true
}

# U2-1 判断记录："只做内容同步"的快速路径是 -SyncContent 单独传（不带 -SyncOnly）；
# 若两者同传，-SyncOnly 的语义（跳过 build/test、仍同步 DLL）优先，内容同步照常无条件执行。
$ContentOnlyMode = $SyncContent -and (-not $SyncOnly)

if ($ContentOnlyMode) {
    Write-Step "已启用 -SyncContent（未同时传 -SyncOnly）：只做内容同步，跳过 dotnet build/test 与 DLL 同步"
} elseif ($SyncOnly) {
    Write-Step "已启用 -SyncOnly：跳过 dotnet build/test"
} else {
    # ---------------------------------------------------------------------------
    # 1. dotnet build
    # ---------------------------------------------------------------------------
    Write-Step "dotnet build Core.sln -c $Configuration"
    & dotnet build $SolutionPath -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Host "dotnet build 失败，退出码 $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    # ---------------------------------------------------------------------------
    # 2. dotnet test（可跳过）
    # ---------------------------------------------------------------------------
    if (-not $SkipTests) {
        Write-Step "dotnet test Core.sln -c $Configuration --no-build"
        & dotnet test $SolutionPath -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) {
            Write-Host "dotnet test 失败，退出码 $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    } else {
        Write-Step "已跳过 dotnet test（-SkipTests）"
    }
}

# ---------------------------------------------------------------------------
# 3. 同步六个核心 DLL 到 Unity 引擎适配层包的 Runtime/Plugins/Core/（哈希不同才拷贝）
#    -SyncContent 单独传（ContentOnlyMode）时跳过本步——那条路径明确"只改了内容数据/资产，
#    没有改任何 C# 代码"，DLL 本身没有变化。
# ---------------------------------------------------------------------------
if (-not $ContentOnlyMode) {
    Write-Step "同步核心 DLL 到 Runtime/Plugins/Core/（哈希不同才拷贝）"

    if (-not (Test-Path $PluginsCoreDir)) {
        New-Item -ItemType Directory -Force -Path $PluginsCoreDir | Out-Null
    }

    $syncedCount = 0
    $skippedCount = 0

    foreach ($asm in $CoreAssemblies) {
        $srcPath = Join-Path $RepoRoot ($asm.Dir + "\bin\$Configuration\netstandard2.1\" + $asm.Name + ".dll")
        if (-not (Test-Path $srcPath)) {
            Write-Host "找不到构建产物：$srcPath" -ForegroundColor Red
            if ($SyncOnly) {
                Write-Host "（-SyncOnly 要求产物已存在，请先不带 -SyncOnly 跑一次完整构建）" -ForegroundColor Red
            }
            exit 1
        }

        $destPath = Join-Path $PluginsCoreDir ($asm.Name + ".dll")
        $changed = Copy-IfChanged -SourcePath $srcPath -DestPath $destPath
        $sizeBytes = (Get-Item $destPath).Length

        if ($changed) {
            $syncedCount++
            Write-Host ("  [同步] {0}.dll  ({1} bytes)" -f $asm.Name, $sizeBytes)
        } else {
            $skippedCount++
            Write-Host ("  [跳过] {0}.dll  ({1} bytes，内容未变化）" -f $asm.Name, $sizeBytes)
        }
    }

    Write-Host ("已同步 {0} 个 / 跳过 {1} 个" -f $syncedCount, $skippedCount)

    $copiedDllCount = (Get-ChildItem -Path $PluginsCoreDir -Filter "*.dll" -File).Count
    if ($copiedDllCount -ne 6) {
        Write-Host "Runtime/Plugins/Core/ 下 DLL 数量应为 6，实际为 $copiedDllCount" -ForegroundColor Red
        exit 1
    }
    Write-Host "Runtime/Plugins/Core/ 下 DLL 数量核对通过：$copiedDllCount"
} else {
    Write-Step "ContentOnlyMode：跳过 DLL 同步"
}

# ---------------------------------------------------------------------------
# 4. 内容数据集同步（U2-1 新增，见 -SyncContent 参数说明；数据目录框架/游戏分层任务追加
#    data/_framework 同步）：
#      data/_framework          -> Assets/StreamingAssets/GameFoundation/data/_framework
#        （框架级数据表，见 data/README.md"两类目录"一节；与 data/_sample 各自独立子目录，
#        不合并成一份文件树——Unity 侧引导代码按两个数据根分别加载，见 EngineAdapter 判断记录）
#      data/_sample            -> Assets/StreamingAssets/GameFoundation/data/_sample
#      assets/_placeholder     -> Assets/StreamingAssets/GameFoundation/assets/_placeholder（整体镜像）
#      assets/_placeholder/sprites + assets/_sample/sprites -> Assets/StreamingAssets/GameFoundation/sprites
#        （UnityResourceLoader 期望的 Image 路径规则，见该类型顶部注释；两个数据集的精灵集同步进
#        同一棵目标目录树，_sample 侧目录名已按 "<category>_<name>" 规则与 _placeholder 侧的占位
#        精灵集不同名，正常不会互相覆盖）
#      assets/_placeholder/sfx + assets/_sample/sfx -> Assets/StreamingAssets/GameFoundation/audio
#        （UnityResourceLoader 期望的 Audio 路径规则；源目录名 "sfx" 与目标目录名 "audio" 不同，
#        是加载器一侧的固定子目录约定，见该类型判断记录）
#      assets/_placeholder/vfx + assets/_sample/vfx -> Assets/StreamingAssets/GameFoundation/vfx
#    以上 sprites/audio/vfx 三处传两个源目录（Sync-ContentTree 的 $SourceDirs 数组，见其函数
#    注释），任一源目录不存在仍照旧跳过，不影响另一个正常同步。
#    无条件执行（默认构建流程、-SyncOnly、-SyncContent 三种模式下都会执行，见参数说明）。
# ---------------------------------------------------------------------------
Write-Step "同步内容数据集到 StreamingAssets/GameFoundation/（哈希不同才拷贝，镜像删除源目录已不存在的文件）"

$StreamingAssetsRoot = Join-Path $RepoRoot "adapters\unity\Assets\StreamingAssets\GameFoundation"

# 按（一个或多个）源目录 -> 目标目录镜像同步；返回 拷贝/跳过/删除 计数，一律用绝对路径
# （不含尾部分隔符）参与 Substring 计算相对路径，避免路径分隔符/结尾斜杠的边界情况算错相对路径。
# $SourceDirs 支持传多个源目录（数组）：keepRelative（决定目标目录里保留哪些相对路径、
# 删除哪些残留文件）取全部源目录相对路径的并集；多个源目录出现同名相对路径时，按数组顺序
# 后列源目录覆盖前者（内容以后列为准）并打印警告——调用方按"优先级从低到高"的顺序传入。
# 单个源目录不存在时照旧跳过它（不影响其余源目录正常同步）；全部源目录都不存在则整体跳过。
function Sync-ContentTree {
    param(
        [string[]]$SourceDirs,
        [string]$DestDir
    )

    $resolvedSources = @()
    foreach ($dir in $SourceDirs) {
        if (Test-Path $dir) {
            $resolvedSources += (Resolve-Path $dir).Path.TrimEnd('\', '/')
        } else {
            Write-Host "  源目录不存在，跳过同步：$dir" -ForegroundColor Yellow
        }
    }
    if ($resolvedSources.Count -eq 0) {
        return @{ Copied = 0; Skipped = 0; Removed = 0; Total = 0 }
    }

    if (-not (Test-Path $DestDir)) {
        New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    }
    $resolvedDest = (Resolve-Path $DestDir).Path.TrimEnd('\', '/')

    $copied = 0
    $skipped = 0
    $keepRelative = New-Object System.Collections.Generic.HashSet[string]

    foreach ($resolvedSource in $resolvedSources) {
        $sourceFiles = Get-ChildItem -Path $resolvedSource -Recurse -File
        foreach ($file in $sourceFiles) {
            $relative = $file.FullName.Substring($resolvedSource.Length).TrimStart('\', '/')
            if ($keepRelative.Contains($relative)) {
                Write-Host ("  警告：多个源目录都提供了相对路径 '{0}'，以后列源目录为准（当前来自 '{1}'）" -f $relative, $resolvedSource) -ForegroundColor Yellow
            }
            [void]$keepRelative.Add($relative)
            $destPath = Join-Path $resolvedDest $relative
            $changed = Copy-IfChanged -SourcePath $file.FullName -DestPath $destPath
            if ($changed) { $copied++ } else { $skipped++ }
        }
    }

    $removed = 0
    if (Test-Path $resolvedDest) {
        $destFiles = Get-ChildItem -Path $resolvedDest -Recurse -File
        foreach ($destFile in $destFiles) {
            $relative = $destFile.FullName.Substring($resolvedDest.Length).TrimStart('\', '/')
            if (-not $keepRelative.Contains($relative)) {
                Remove-Item -Path $destFile.FullName -Force
                $removed++
            }
        }
    }

    return @{ Copied = $copied; Skipped = $skipped; Removed = $removed; Total = $keepRelative.Count }
}

$dataFrameworkSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "data\_framework")) -DestDir (Join-Path $StreamingAssetsRoot "data\_framework")
Write-Host ("  data/_framework -> StreamingAssets/GameFoundation/data/_framework：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $dataFrameworkSyncResult.Total, $dataFrameworkSyncResult.Copied, $dataFrameworkSyncResult.Skipped, $dataFrameworkSyncResult.Removed)

$dataSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "data\_sample")) -DestDir (Join-Path $StreamingAssetsRoot "data\_sample")
Write-Host ("  data/_sample -> StreamingAssets/GameFoundation/data/_sample：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $dataSyncResult.Total, $dataSyncResult.Copied, $dataSyncResult.Skipped, $dataSyncResult.Removed)

# games/_template 自带的最小数据集（见 games/_template/data/README.md）同步进工作台 StreamingAssets，
# 供 games/_template/Runtime/GameBootstrap.cs 的 PlayMode 测试（在工作台里跑，见
# games/_template/Tests/Runtime）默认 GameOptions（_gameDatasetRoot = "data/game"）能找到数据；
# 与 data/_framework、data/_sample 同一治理方式（构建期产物、gitignore，不进源码库）。
$templateDataSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "games\_template\data\game")) -DestDir (Join-Path $StreamingAssetsRoot "data\game")
Write-Host ("  games/_template/data/game -> StreamingAssets/GameFoundation/data/game（模板自带最小数据集，供模板 PlayMode 测试使用）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $templateDataSyncResult.Total, $templateDataSyncResult.Copied, $templateDataSyncResult.Skipped, $templateDataSyncResult.Removed)

$placeholderMirrorResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "assets\_placeholder")) -DestDir (Join-Path $StreamingAssetsRoot "assets\_placeholder")
Write-Host ("  assets/_placeholder -> StreamingAssets/GameFoundation/assets/_placeholder（整体镜像）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $placeholderMirrorResult.Total, $placeholderMirrorResult.Copied, $placeholderMirrorResult.Skipped, $placeholderMirrorResult.Removed)

# sprites/audio/vfx 三处同时同步 assets/_placeholder/<x>（占位素材）与 assets/_sample/<x>
# （toolchain/import_sample_assets.py 导入的样例资产）到同一棵目标目录树，见上方 4 节头注释。
$spritesSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "assets\_placeholder\sprites"), (Join-Path $RepoRoot "assets\_sample\sprites")) -DestDir (Join-Path $StreamingAssetsRoot "sprites")
Write-Host ("  assets/_placeholder/sprites + assets/_sample/sprites -> StreamingAssets/GameFoundation/sprites（加载器路径规则）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $spritesSyncResult.Total, $spritesSyncResult.Copied, $spritesSyncResult.Skipped, $spritesSyncResult.Removed)

$audioSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "assets\_placeholder\sfx"), (Join-Path $RepoRoot "assets\_sample\sfx")) -DestDir (Join-Path $StreamingAssetsRoot "audio")
Write-Host ("  assets/_placeholder/sfx + assets/_sample/sfx -> StreamingAssets/GameFoundation/audio（加载器路径规则）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $audioSyncResult.Total, $audioSyncResult.Copied, $audioSyncResult.Skipped, $audioSyncResult.Removed)

# ADR-0016 决策 5 新增 ResourceKind.Effect：UnityResourceLoader.ResolveEffectDir 按
# "GameFoundation/vfx/<name>/" 解析（见该方法判断记录），与 assets/_placeholder/vfx/<name>/
# 同一套相对路径，因此整棵 vfx 目录树同步过去、不改名（不同于 sprites/sfx 需要改名到加载器
# 期望的扁平子目录，vfx 本身已经是"<kind 子目录>/<name>/"两级结构，直接对应）。
$vfxSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "assets\_placeholder\vfx"), (Join-Path $RepoRoot "assets\_sample\vfx")) -DestDir (Join-Path $StreamingAssetsRoot "vfx")
Write-Host ("  assets/_placeholder/vfx + assets/_sample/vfx -> StreamingAssets/GameFoundation/vfx（ResourceKind.Effect 加载器路径规则）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $vfxSyncResult.Total, $vfxSyncResult.Copied, $vfxSyncResult.Skipped, $vfxSyncResult.Removed)

$totalContentFiles = (Get-ChildItem -Path $StreamingAssetsRoot -Recurse -File -ErrorAction SilentlyContinue).Count
Write-Host ("StreamingAssets/GameFoundation/ 下文件总数（含以上五棵树的并集，sprites/audio/vfx 与 assets/_placeholder 下同名文件各自独立计数）：{0}" -f $totalContentFiles)

# ---------------------------------------------------------------------------
# 4.05 字体资源同步（缺口 1 新增）：assets/_placeholder/fonts/*.otf|*.ttf 哈希比对同步到
#      adapters/unity/Assets/Framework/Resources/Fonts/（注意目标不是 StreamingAssets——字体
#      资源不走 UnityResourceLoader 的通用"后台读字节"路径，必须是已被 Unity 资产管线导入过的
#      UnityEngine.Font 对象，见该类型顶部"判断记录（Font 资源种类）"）；供 UnityResourceLoader/
#      UnityUISurface 按 "font.<name>" id 规则（去掉 "font." 前缀、点号换下划线）以
#      "Resources.Load<Font>(\"Fonts/<name>\")" 解析，规则见包 README"资源 id → 路径规则"。
#      只同步 .otf/.ttf 两个扩展名本身（不含该目录下 README.md/LICENSE-OFL.txt 等说明文件——
#      Assets/Framework/Resources/ 是 Unity 会整体扫描导入的目录，混入非字体文件会被当成多余
#      资产一并导入，不属于本步骤职责）；已提交的同名文件哈希一致则不拷贝（避免不必要的 Unity
#      重新导入）；源目录里已删除的字体文件会被镜像删除（连同其 .meta，否则下次放回同名文件会
#      被 Unity 复用旧 .meta 里过期的导入设置）。
# ---------------------------------------------------------------------------
$fontsSourceDir = Join-Path $RepoRoot "assets\_placeholder\fonts"
$fontsDestDir = Join-Path $RepoRoot "adapters\unity\Assets\Framework\Resources\Fonts"
$fontCopied = 0
$fontSkipped = 0
$fontRemoved = 0
$fontTotal = 0

if (Test-Path $fontsSourceDir) {
    if (-not (Test-Path $fontsDestDir)) {
        New-Item -ItemType Directory -Force -Path $fontsDestDir | Out-Null
    }

    $fontSourceFiles = Get-ChildItem -Path $fontsSourceDir -Recurse -File |
        Where-Object { $_.Extension -eq ".otf" -or $_.Extension -eq ".ttf" }
    $fontKeepNames = New-Object System.Collections.Generic.HashSet[string]
    foreach ($fontFile in $fontSourceFiles) {
        $fontTotal++
        [void]$fontKeepNames.Add($fontFile.Name)
        $destPath = Join-Path $fontsDestDir $fontFile.Name
        $changed = Copy-IfChanged -SourcePath $fontFile.FullName -DestPath $destPath
        if ($changed) { $fontCopied++ } else { $fontSkipped++ }
    }

    if (Test-Path $fontsDestDir) {
        $existingFontFiles = Get-ChildItem -Path $fontsDestDir -File |
            Where-Object { $_.Extension -eq ".otf" -or $_.Extension -eq ".ttf" }
        foreach ($existing in $existingFontFiles) {
            if (-not $fontKeepNames.Contains($existing.Name)) {
                Remove-Item -Path $existing.FullName -Force
                $fontRemoved++
                $metaPath = $existing.FullName + ".meta"
                if (Test-Path $metaPath) {
                    Remove-Item -Path $metaPath -Force
                }
            }
        }
    }
} else {
    Write-Host "  源目录不存在，跳过字体同步：$fontsSourceDir" -ForegroundColor Yellow
}

Write-Host ("  assets/_placeholder/fonts/*.otf|*.ttf -> Assets/Framework/Resources/Fonts（字体资源 id -> 路径规则）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $fontTotal, $fontCopied, $fontSkipped, $fontRemoved)

# ---------------------------------------------------------------------------
# 4.1 占位场景/导航资源文件（供 Core.Foundation.SceneRouter.SceneRouter.LoadScene 通过 world.map
#     记录里既有的 scene_ref="scene.sample_field"/nav_ref="nav.sample_field" 分别以
#     ResourceKind.Scene/ResourceKind.NavMesh 解析出的 UnityResourceLoader 路径
#     StreamingAssets/GameFoundation/scene/sample_field.json、
#     StreamingAssets/GameFoundation/nav_mesh/sample_field.json 各自找到一个可读文件（ADR-0016
#     决策 5 给 ResourceKind 增补了 Scene/NavMesh 专用取值，两类资源不再共用同一个 "data/"
#     路径——此前两者去掉类别前缀后恰好是同一个文件名，借用同一份字节，是 Scene/NavMesh 取值
#     补齐之前的过渡写法，现按各自子目录分别生成，内容仍然不重要：SceneRouter 只要求文件存在
#     且可解码为文本，从不解析其内容，见 SceneRouter.cs 类型注释）。
#
#     判断记录（为什么在 build.ps1 生成而不是放进 data/_sample 或 assets/_placeholder）：
#     data/_sample 的可改动范围限定于"UI/Shell 需要补的示例行"（登记表数据），这个文件不是任何
#     登记表的一行，塞进去会被 toolchain/validate_data.py 当成一张缺失 schema 的表校验报错；
#     assets/_placeholder 是本任务硬性规则明确不动的目录。该资源在 SceneRouter 眼里是纯粹的
#     "占位字节"（内容完全不解析），属于构建期产物而非源内容，因此选择在 build.ps1（内容同步
#     步骤，允许改动）里直接生成，与 StreamingAssets/GameFoundation/ 其余产物同一治理方式
#     （构建时产生、.gitignore、不进源码库）。框架层面的"场景/导航资源内容管线"仍是已知能力
#     缺口（ResourceKind 已经区分种类，但具体场景/导航数据格式与生产管线不在本次范围内）。
# ---------------------------------------------------------------------------
$sceneResourceDir = Join-Path $StreamingAssetsRoot "scene"
if (-not (Test-Path $sceneResourceDir)) {
    New-Item -ItemType Directory -Force -Path $sceneResourceDir | Out-Null
}
$sceneResourcePath = Join-Path $sceneResourceDir "sample_field.json"
$sceneResourceContent = '{"_placeholder":true,"_note":"SceneRouter 场景资源占位字节，内容不被解析，见 build.ps1 判断记录"}'
Set-Content -Path $sceneResourcePath -Value $sceneResourceContent -NoNewline -Encoding utf8
Write-Host "  已生成占位场景资源：$sceneResourcePath（scene.sample_field）"

$navResourceDir = Join-Path $StreamingAssetsRoot "nav_mesh"
if (-not (Test-Path $navResourceDir)) {
    New-Item -ItemType Directory -Force -Path $navResourceDir | Out-Null
}
$navResourcePath = Join-Path $navResourceDir "sample_field.json"
$navResourceContent = '{"_placeholder":true,"_note":"SceneRouter 导航资源占位字节，内容不被解析，见 build.ps1 判断记录"}'
Set-Content -Path $navResourcePath -Value $navResourceContent -NoNewline -Encoding utf8
Write-Host "  已生成占位导航资源：$navResourcePath（nav.sample_field）"

# 同上，为 games/_template 自带的最小地图（world.template_field，scene_ref=scene.template_field/
# nav_ref=nav.template_field，见 games/_template/data/game/world/world.map.json）补一份同款占位
# 场景/导航资源，供模板 PlayMode 测试里的 SceneRouter.LoadScene 找到可读文件（否则新游戏会在
# LoadScene 这一步失败，见 presentation/shell/core/ShellHost.cs NewGame 判断记录）。
$templateSceneResourcePath = Join-Path $sceneResourceDir "template_field.json"
Set-Content -Path $templateSceneResourcePath -Value $sceneResourceContent -NoNewline -Encoding utf8
Write-Host "  已生成占位场景资源：$templateSceneResourcePath（scene.template_field，供 games/_template 测试用）"

$templateNavResourcePath = Join-Path $navResourceDir "template_field.json"
Set-Content -Path $templateNavResourcePath -Value $navResourceContent -NoNewline -Encoding utf8
Write-Host "  已生成占位导航资源：$templateNavResourcePath（nav.template_field，供 games/_template 测试用）"

# ---------------------------------------------------------------------------
# 5. 可选：打分发包 dist/<version>/
# ---------------------------------------------------------------------------
if ($DistRequested) {
    Write-Step "打分发包 dist/$DistDirVersion/"

    $DistRoot = Join-Path $RepoRoot ("dist\" + $DistDirVersion)
    if (Test-Path $DistRoot) {
        Remove-Item -Path $DistRoot -Recurse -Force -Confirm:$false
    }
    New-Item -ItemType Directory -Force -Path $DistRoot | Out-Null

    function Copy-DistDir {
        param(
            [string]$SourceRelative,
            [string]$DestName,
            [string[]]$ExcludeDirNames = @()
        )
        $src = Join-Path $RepoRoot $SourceRelative
        $dst = Join-Path $DistRoot $DestName
        New-Item -ItemType Directory -Force -Path $dst | Out-Null

        Get-ChildItem -Path $src -Recurse -Force | ForEach-Object {
            $item = $_
            $relative = $item.FullName.Substring($src.Length).TrimStart('\')
            if ($relative -eq "") { return }

            # 判断记录（2026-09-05 收尾修复）：排除判断此前只匹配"排除目录名出现在相对路径最前面"
            # 这一种情况（如 ".venv" 在 "toolchain/.venv/..."），漏掉了排除目录名出现在更深层级的
            # 情况（如 "__pycache__" 出现在 "toolchain/asset_import/__pycache__/..." ——
            # asset_import 是普通业务目录，其下的 __pycache__ 子目录也需要被排除）。改为按路径分段
            # 逐段比较（$relative 以 "\" 分隔），只要任意一段的名字精确等于某个排除名即命中，不再
            # 依赖"是否出现在开头"这一前提，覆盖"排除目录出现在任意深度"的情况。
            $skip = $false
            $relativeSegments = $relative -split '\\'
            foreach ($ex in $ExcludeDirNames) {
                if ($relativeSegments -contains $ex) {
                    $skip = $true
                    break
                }
            }
            if ($skip) { return }

            $targetPath = Join-Path $dst $relative
            if ($item.PSIsContainer) {
                New-Item -ItemType Directory -Force -Path $targetPath | Out-Null
            } else {
                $targetDir = Split-Path -Parent $targetPath
                if (-not (Test-Path $targetDir)) {
                    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
                }
                Copy-Item -Path $item.FullName -Destination $targetPath -Force
            }
        }

        $fileCount = (Get-ChildItem -Path $dst -Recurse -File).Count
        Write-Host ("  {0} -> dist\{1}\{2}  ({3} files)" -f $SourceRelative, $DistDirVersion, $DestName, $fileCount)
        return $fileCount
    }

    $adapterFileCount = Copy-DistDir -SourceRelative "adapters\unity\Packages\com.gamefoundation.adapter.unity" -DestName "adapters\unity\Packages\com.gamefoundation.adapter.unity"
    $templateFileCount = Copy-DistDir -SourceRelative "games\_template" -DestName "games\_template"
    $toolchainFileCount = Copy-DistDir -SourceRelative "toolchain" -DestName "toolchain" -ExcludeDirNames @(".venv", "__pycache__")
    $assetsFileCount = Copy-DistDir -SourceRelative "assets\_placeholder" -DestName "assets\_placeholder"
    # 数据目录框架/游戏分层任务新增：分发包只带框架级数据表（data/_framework），不带
    # data/_sample（那是本仓库自测用的示例数据，不代表任何真实游戏内容，见 data/README.md）。
    # 新游戏按 games/_template/README.md 的接入方式是"分发包 data/_framework + 自己的 data/<game>"
    # 两根合并加载/校验（见 data/README.md"多根加载与合并规则"）。
    $dataFrameworkFileCount = Copy-DistDir -SourceRelative "data\_framework" -DestName "data\_framework"

    # 消费方演练任务新增（toolchain/consumer_smoke.ps1 实跑暴露的 dist 缺口，见该脚本判断记录）：
    # TextMeshPro 是 games/_template 主菜单 UI（TemplateShellUi 经 UnityUISurface.DrawText）的运行期
    # 硬依赖，但 "TMP Essential Resources"（TMP_Settings.asset、SDF 着色器等）从未随 dist 分发——
    # 工作台工程（adapters/unity）把这些内容当作普通 Assets 提交在
    # adapters/unity/Assets/TextMesh Pro/（见 UnityUISurface.cs 顶部"判断记录（TMP 运行期依赖）"：
    # AssetDatabase.ImportPackage 在批处理下是异步的、无法可靠等待完成，因此不能指望新消费方工程
    # 自己在批处理流程里"导入 TMP Essential Resources"），此前 dist 只打包了
    # adapters/unity/Packages/com.gamefoundation.adapter.unity（Packages 目录），不包含
    # adapters/unity/Assets/ 下任何内容，导致任何新游戏工程照 games/_template/README.md 走完接入
    # 步骤后，主菜单一渲染文字就会因为 TMP_Settings 单例不存在而抛异常——首次跑
    # consumer_smoke.ps1 实测复现。现补一份 assets/textmesh_pro_essentials/，新消费方工程按
    # games/_template/README.md 的指引整份拷进自己的 Assets/TextMesh Pro/。
    $tmpEssentialsFileCount = Copy-DistDir -SourceRelative "adapters\unity\Assets\TextMesh Pro" -DestName "assets\textmesh_pro_essentials"

    # -------------------------------------------------------------------
    # 5.1 版本可追溯任务新增：把解析出的版本号写回 dist 内两个 package.json
    #     （含 games/_template 对适配层包的依赖版本号），保持"单一版本源"——
    #     源码仓库里的两个 package.json 已经在提交时同步改成当前 VERSION，这里
    #     针对的是"显式传入与仓库当前 VERSION 不同的版本号打历史/预发布快照"这一种
    #     场景（例如 -Dist auto 之外的显式覆盖），确保 dist 产物里的 package.json
    #     永远与本次打包的 $ResolvedDistVersion 一致，不依赖调用方提前手改源码。
    # -------------------------------------------------------------------
    function Set-DistPackageJsonVersion {
        param(
            [string]$JsonPath,
            [string]$Version
        )
        if (-not (Test-Path $JsonPath)) {
            Write-Host "  未找到 $JsonPath，跳过版本回写" -ForegroundColor Yellow
            return
        }
        # 判断记录：这些 package.json 是不带 BOM 的 UTF-8（git 常见约定）。Windows PowerShell 5.1
        # 的 Get-Content 在没有 BOM 时按系统 ANSI 代码页猜编码，会把文件里的中文字符读成乱码
        # （ConvertFrom-Json 甚至可能因此报"Invalid object passed in"）；必须显式 -Encoding UTF8。
        $obj = (Get-Content -Path $JsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
        $obj.version = $Version
        if (($obj.PSObject.Properties.Name -contains "dependencies") -and
            ($obj.dependencies.PSObject.Properties.Name -contains "com.gamefoundation.adapter.unity")) {
            $obj.dependencies."com.gamefoundation.adapter.unity" = $Version
        }
        ($obj | ConvertTo-Json -Depth 10) | Set-Content -Path $JsonPath -Encoding utf8
        Write-Host "  已回写版本号 $Version -> $JsonPath"
    }

    Set-DistPackageJsonVersion -JsonPath (Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\package.json") -Version $ResolvedDistVersion
    Set-DistPackageJsonVersion -JsonPath (Join-Path $DistRoot "games\_template\package.json") -Version $ResolvedDistVersion

    # -------------------------------------------------------------------
    # 5.2 版本可追溯任务新增：git_commit（工作树不干净时加 -dirty 后缀）
    # -------------------------------------------------------------------
    Push-Location $RepoRoot
    try {
        $gitCommitShort = (& git rev-parse --short HEAD).Trim()
        $gitStatusPorcelain = & git status --porcelain
        $gitDirty = $false
        if ($null -ne $gitStatusPorcelain) {
            $joined = ($gitStatusPorcelain -join "`n").Trim()
            if ($joined -ne "") { $gitDirty = $true }
        }
        if ($gitDirty) {
            $gitCommit = "$gitCommitShort-dirty"
        } else {
            $gitCommit = $gitCommitShort
        }
    } catch {
        $gitCommit = "(unknown：$($_.Exception.Message))"
    } finally {
        Pop-Location
    }

    # -------------------------------------------------------------------
    # 5.3 版本可追溯任务新增：architecture_docs —— 逐篇解析 architecture/0*.md、1*.md
    #     首行标题里的版本号（形如"# xxx vN"）。dist 本身不打包 architecture/ 目录（见
    #     Copy-DistDir 调用列表），这里只是把"打这份快照时，架构文档集处于哪个版本组合"
    #     记录进 MANIFEST，供 11 第 7 节要求的可追溯性核对。
    # -------------------------------------------------------------------
    $archDocLines = @()
    $archDir = Join-Path $RepoRoot "architecture"
    $archFiles = @()
    $archFiles += Get-ChildItem -Path $archDir -Filter "0*.md" -File -ErrorAction SilentlyContinue
    $archFiles += Get-ChildItem -Path $archDir -Filter "1*.md" -File -ErrorAction SilentlyContinue
    $archFiles = $archFiles | Sort-Object Name
    foreach ($af in $archFiles) {
        $firstLine = Get-Content -Path $af.FullName -TotalCount 1 -Encoding UTF8
        $m = [regex]::Match($firstLine, 'v(\d+)\s*$')
        if ($m.Success) {
            $archDocLines += ("  {0}: v{1}" -f $af.Name, $m.Groups[1].Value)
        } else {
            $archDocLines += ("  {0}: (未识别到版本号，首行：{1})" -f $af.Name, $firstLine.Trim())
        }
    }

    # -------------------------------------------------------------------
    # 5.4 版本可追溯任务新增：data_schemas —— 遍历 data/_framework 下每张表的
    #     table/schema_version（data/_sample 不随 dist 分发，因此不列入；见 data/README.md
    #     "两类目录"一节与 build.ps1 判断记录）。
    # -------------------------------------------------------------------
    $dataSchemaLines = @()
    $dataFrameworkSrcDir = Join-Path $RepoRoot "data\_framework"
    if (Test-Path $dataFrameworkSrcDir) {
        $schemaJsonFiles = Get-ChildItem -Path $dataFrameworkSrcDir -Filter "*.json" -File -Recurse | Sort-Object FullName
        foreach ($sjf in $schemaJsonFiles) {
            try {
                $tableObj = (Get-Content -Path $sjf.FullName -Raw -Encoding UTF8) | ConvertFrom-Json
                $dataSchemaLines += ("  {0}: schema_version={1}" -f $tableObj.table, $tableObj.schema_version)
            } catch {
                $dataSchemaLines += ("  {0}: 解析失败（{1}）" -f $sjf.Name, $_.Exception.Message)
            }
        }
    }

    # -------------------------------------------------------------------
    # 5.5 版本可追溯任务新增：core_assemblies —— 六个核心 DLL 的 sha256，取 dist 内
    #     刚拷贝进适配层包的那一份（与实际分发物一致，而不是仓库内 core/*/bin/ 下的构建产物，
    #     两者理论上内容相同，但直接对 dist 内文件取哈希更贴合"这份快照实际包含什么"）。
    # -------------------------------------------------------------------
    $coreAssemblyLines = @()
    # 版本管理方案新增：与 $coreAssemblyLines（人读文本）并行记一份哈希映射表，供下面 -Release
    # 流程生成 ws-game.lock（机读 JSON）复用，避免重新计算或反解析上面那行文本。
    $coreAssemblyShaMap = [ordered]@{}
    $distPluginsCoreDir = Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"
    foreach ($asm in $CoreAssemblies) {
        $distDllPath = Join-Path $distPluginsCoreDir ($asm.Name + ".dll")
        if (Test-Path $distDllPath) {
            $sha = (Get-FileHash -Path $distDllPath -Algorithm SHA256).Hash.ToLower()
            $coreAssemblyLines += ("  {0}.dll: sha256={1}" -f $asm.Name, $sha)
            $coreAssemblyShaMap[$asm.Name + ".dll"] = $sha
        } else {
            $coreAssemblyLines += ("  {0}.dll: 未找到（{1}）" -f $asm.Name, $distDllPath)
            $coreAssemblyShaMap[$asm.Name + ".dll"] = $null
        }
    }

    $manifestPath = Join-Path $DistRoot "MANIFEST.txt"
    $manifestLines = @(
        "version: $ResolvedDistVersion",
        "date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "git_commit: $gitCommit",
        "",
        "[directory_file_counts]",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity: $adapterFileCount files",
        "games/_template: $templateFileCount files",
        "toolchain: $toolchainFileCount files",
        "assets/_placeholder: $assetsFileCount files",
        "data/_framework: $dataFrameworkFileCount files",
        "assets/textmesh_pro_essentials: $tmpEssentialsFileCount files",
        "",
        "[architecture_docs]"
    ) + $archDocLines + @(
        "",
        "[data_schemas]"
    ) + $dataSchemaLines + @(
        "",
        "[core_assemblies]"
    ) + $coreAssemblyLines

    Set-Content -Path $manifestPath -Value $manifestLines -Encoding utf8
    Write-Host "已生成 $manifestPath"

    # -------------------------------------------------------------------
    # 5.6 版本管理方案新增：zip + lock。默认只在 -Release 时跑（单纯 -Dist/-Dist auto 只需要
    #     dist/<version>/ 目录本身，不需要额外打 zip/lock，见既有调用方 consumer_smoke.ps1/
    #     games/_template/README.md 的用法——都是直接指向 dist/<version>/ 目录，不消费 zip）；
    #     独立开关 -Zip 可以在不走 -Release 校验/提交/打标签的前提下单独触发这一步（见
    #     .PARAMETER Zip 说明，`.github/workflows/release.yml` 用这条路径）。
    # -------------------------------------------------------------------
    if ($ReleaseRequested -or $Zip) {
        Write-Step "打 zip + lock（dist/ws-game-$DistDirVersion.zip / .lock）"

        $zipPath = Join-Path $RepoRoot ("dist\ws-game-" + $DistDirVersion + ".zip")
        $zipTopLevelName = "ws-game-" + $DistDirVersion
        $zipStagingRoot = Join-Path $env:TEMP ("ws_game_zip_staging_" + [guid]::NewGuid().ToString("N"))
        $zipStagingDir = Join-Path $zipStagingRoot $zipTopLevelName
        New-Item -ItemType Directory -Force -Path $zipStagingDir | Out-Null
        try {
            # Copy-Item -Recurse 复制 $DistRoot 的内容（不含 $DistRoot 自身这层目录名）到
            # $zipStagingDir，这样 Compress-Archive 传入 $zipStagingDir 时，zip 内顶层目录名
            # 就是 $zipStagingDir 的 basename（即 "ws-game-<ver>"），而不是 "<ver>"。
            Copy-Item -Path (Join-Path $DistRoot "*") -Destination $zipStagingDir -Recurse -Force
            if (Test-Path $zipPath) {
                Remove-Item -Path $zipPath -Force
            }
            Compress-Archive -Path $zipStagingDir -DestinationPath $zipPath -CompressionLevel Optimal
        } finally {
            Remove-Item -Path $zipStagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        $zipSizeBytes = (Get-Item $zipPath).Length
        $zipSizeMb = [Math]::Round($zipSizeBytes / 1MB, 2)
        Write-Host ("  已生成 {0}（{1} MB，zip 内顶层目录 {2}/）" -f $zipPath, $zipSizeMb, $zipTopLevelName)

        # ws-game.lock 示例锁文件：版本号、git_commit、六个核心 DLL 的 sha256（复用上面 5.5 节已经
        # 算好的 $coreAssemblyShaMap，不重复计算）。字段内容一律用干净版本号 $ResolvedDistVersion
        # （不带 -dryrun 后缀）——DryRun 只是产物文件名带后缀以避免覆盖真实发布产物，锁文件内容
        # 描述的仍然是"这是版本 X.Y.Z 的锁定信息"这一事实本身。游戏仓库拿到这份文件后原样复制为
        # 自己的 ws-game.lock（见 toolchain/get_framework.ps1）。
        $lockPath = Join-Path $RepoRoot ("dist\ws-game-" + $DistDirVersion + ".lock")
        $lockObj = [ordered]@{
            version    = $ResolvedDistVersion
            git_commit = $gitCommit
            dlls       = $coreAssemblyShaMap
        }
        $lockJson = ($lockObj | ConvertTo-Json -Depth 5)
        [System.IO.File]::WriteAllText($lockPath, $lockJson, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  已生成 $lockPath"

        # -------------------------------------------------------------------
        # 5.7 版本管理方案新增：-Release 第 7 步——非 DryRun 时提交 + 打标签；DryRun 到此为止
        #     （第 6 步的 zip/lock 已经落在 dist/ 下的 -dryrun 后缀路径，dist/ 整体 .gitignore，
        #     不影响"结束时工作树干净"这条要求）。
        # -------------------------------------------------------------------
        if ($ReleaseRequested -and (-not $DryRun)) {
            Write-Step "-Release 第 7 步：提交版本号改动 + 打带注释标签 v$Release"

            $releaseNotesPath = Join-Path $RepoRoot ("dist\release-notes-" + $Release + ".txt")
            [System.IO.File]::WriteAllText($releaseNotesPath, $ReleaseChangelogSection, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "  已生成 $releaseNotesPath（CHANGELOG.md [$Release] 条目正文，供 gh release create --notes-file 使用）"

            Push-Location $RepoRoot
            try {
                & git add "VERSION" "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json" "games/_template/package.json" "CHANGELOG.md"
                if ($LASTEXITCODE -ne 0) { throw "git add 失败，退出码 $LASTEXITCODE" }

                $commitMessage = "发布 $Release"
                & git commit -m $commitMessage
                if ($LASTEXITCODE -ne 0) { throw "git commit 失败，退出码 $LASTEXITCODE" }
                Write-Host "  已提交：$commitMessage"

                $tagName = "v$Release"
                $tagMessageFile = Join-Path $RepoRoot ("dist\tag-message-" + $Release + ".txt")
                $tagMessageContent = "$tagName`n`n$ReleaseChangelogSection"
                [System.IO.File]::WriteAllText($tagMessageFile, $tagMessageContent, (New-Object System.Text.UTF8Encoding($false)))
                & git tag -a $tagName -F $tagMessageFile
                if ($LASTEXITCODE -ne 0) { throw "git tag 失败，退出码 $LASTEXITCODE" }
                Remove-Item -Path $tagMessageFile -Force -ErrorAction SilentlyContinue
                Write-Host "  已打标签：$tagName"
            } finally {
                Pop-Location
            }

            # 第 8 步：打印后续需要人工/设计层执行的两条命令；-Publish 时自动执行。
            $pushCmd = "git push origin main --tags"
            $releaseCmd = "gh release create $tagName `"$zipPath`" `"$lockPath`" --title `"$tagName`" --notes-file `"$releaseNotesPath`""

            Write-Host ""
            Write-Host "==== -Release 完成：$ReleaseCurrentVersion -> $Release（已提交 + 已打标签 $tagName） ====" -ForegroundColor Green
            Write-Host "后续需要人工/设计层执行（-Publish 可自动执行，本次未传则仅打印）：" -ForegroundColor Cyan
            Write-Host "  1) $pushCmd"
            Write-Host "  2) $releaseCmd"

            if ($ReleaseBumpIsMajorOrMinor) {
                $branchName = "release/" + $newParts.Major + "." + $newParts.Minor + ".x"
                $branchPoint = "v" + $newParts.Major + "." + $newParts.Minor + ".0"
                Write-Host ""
                Write-Host "本次版本号 MAJOR 或 MINOR 段发生了变化，建议开一条维护分支（见根 README.md" -ForegroundColor Cyan
                Write-Host "'维护分支与 PATCH 发布流程'一节）：" -ForegroundColor Cyan
                Write-Host "  git branch $branchName $branchPoint"
            }

            if ($Publish) {
                Write-Step "-Publish：自动执行上面两条命令"
                Push-Location $RepoRoot
                try {
                    Write-Host "  执行：$pushCmd"
                    & git push origin main --tags
                    if ($LASTEXITCODE -ne 0) { throw "git push 失败，退出码 $LASTEXITCODE" }

                    Write-Host "  执行：$releaseCmd"
                    & gh release create $tagName $zipPath $lockPath --title $tagName --notes-file $releaseNotesPath
                    if ($LASTEXITCODE -ne 0) { throw "gh release create 失败，退出码 $LASTEXITCODE" }
                } finally {
                    Pop-Location
                }
                Write-Host "  -Publish 完成：已推送并创建 GitHub Release $tagName"
            }
        } elseif ($ReleaseRequested -and $DryRun) {
            Write-Host ""
            Write-Host "==== -DryRun 完成：$Release 的发布流水线全流程校验 + 打包已跑通，未改写任何源码文件、未提交、未打标签 ====" -ForegroundColor Green
            Write-Host "  dist/$DistDirVersion/、$zipPath、$lockPath 均为验证产物（dist/ 已 .gitignore，可随时删除）"
        } else {
            Write-Host ""
            Write-Host "==== -Zip 完成：$zipPath、$lockPath 已生成，未涉及版本号写回/提交/打标签（-Zip 独立于 -Release 使用） ====" -ForegroundColor Green
        }
    }
} else {
    Write-Step "未传 -Dist/-Release，跳过打包步骤"
}

Write-Host ""
Write-Host "build.ps1 完成。" -ForegroundColor Green
exit 0
