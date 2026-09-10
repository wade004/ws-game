<#
.SYNOPSIS
    发布前 ABI 探针（第十六方深度审核 codex 第十四轮修订版跟进，见
    architecture/落地计划/audit-c9ff301-20260909/ followup-2026-09-10.md、
    architecture/11_工程规范与测试.md 第 7 节"发布说明不得宣称未经验证的二进制兼容"）：验证一份
    只见过基线版本（`toolchain/abi_probe_baseline.txt`，默认 1.12.0）公开签名的已编译消费方，
    换上本次工作树刚构建出的正式 DLL、**不重新编译**，运行期是否仍然成功——不满足则说明这是一次
    未经声明的二进制破坏性变更（`System.MissingMethodException`/`TypeLoadException` 等），门禁
    直接失败，而不是像 1.13.0 那样等外部审计事后才发现。

    改造自 architecture/落地计划/audit-c9ff301-20260909/docs-project/api-compat/run-api-compat.ps1
    （codex 探针原型，硬编码 1.12.0→1.13.0、只测 FieldSchema 一个签名、且是"期望复现失败"的一次性
    取证脚本）：本脚本参数化基线版本、覆盖本轮恢复的全部签名，四个程序集，且是"期望通过"的常规门禁
    （非零退出即失败），供 check.ps1 全量步骤与后续 build.ps1 -Release 发布前重复调用。

    第十七方审核跟进（外部审计 audit-76d16a5-20260910，PJ114-01/02/03）新增两条独立小节：
    1）toolchain/abi_surface（System.Reflection.MetadataLoadContext 反射全部公开/受保护 API 表面，
       不再依赖 toolchain/abi_probe/Program.cs 手写死的少量签名）与本探针的既有 consumer 探针串联，
       任一失败本脚本都以非零退出；2）基线缺失/输出目录处理换了新语义，见下方两个 PARAMETER 小节。

.PARAMETER BaselineVersion
    比对基线版本号，默认读取本脚本同目录 `abi_probe_baseline.txt`（单行版本号，格式同仓库根
    VERSION 文件）。

    判断记录（为什么不默认"上一个已发布版本"）：若每次都拿"上一个版本"当基线，一旦某次 MINOR
    发布本身就引入了未声明的二进制破坏（1.13.0 的真实教训），下一轮探针会把这个已经破坏的状态
    当成新基线，永远比较不出问题、静默把破坏"合法化"。`abi_probe_baseline.txt` 是一个人工维护的
    "最后一个已知二进制兼容"锚点，只应在以下两种情况下手动推进：1）新做一轮探针扩容后，确认扩容
    后的探针本身对某个更新的版本仍然全绿；2）刻意做了一次 MAJOR 破坏性发布并在对应 ADR/CHANGELOG
    记录清楚。日常 MINOR/PATCH 发布不应该、也不需要推进这个文件。

.PARAMETER BaselineZip
    直接指定基线发行包 zip 路径，跳过 `-BaselineVersion` 到 `dist/ws-game-<ver>.zip` 的默认解析
    （供 CI 或非标准 dist 布局场景使用）。

.PARAMETER OutDir
    本次探针的全部中间产物（解出的旧 DLL、consumer 编译输出、表面 dump/compare 报告、summary.txt）
    落地目录。

    PJ114-03 根治（外部审计 audit-76d16a5-20260910，工具安全）：本参数此前对"显式已存在的 OutDir"
    直接 `Remove-Item -Recurse -Force`，没有任何边界检查——调用方一旦手滑传了个真实有内容的目录
    （哪怕只是想说"用这个目录"），会被整个递归删除。新语义：
      - 省略本参数：默认目录改为 `$env:TEMP\ws-game-abi-probe-<yyyyMMdd_HHmmss>-<8位随机十六进制>`，
        用 `New-Item`（非 `-Force`）创建；若该确切路径碰巧已存在（时间戳+随机数字符串仍有极小概率
        碰撞），换一个新随机数重试，不删除已存在的旧目录。
      - 显式传本参数：目标路径不存在 → 创建；已存在且为空目录 → 直接复用；已存在且非空 → 直接
        拒绝（打印原因、退出码 1），不做任何删除。
    任何分支都不再对已存在目录调用 `Remove-Item -Recurse`。

.PARAMETER SkipIfBaselineMissing
    默认行为：找不到 `-BaselineZip`/`dist/ws-game-<BaselineVersion>.zip` 时——`dist/` 是
    `.gitignore` 排除的本机构建缓存目录（见仓库根 .gitignore），全新 clone/CI 环境很可能没有历史
    版本的本地 zip，此时"探针无法运行"不等于"ABI 有问题"，不应该阻塞门禁。

    PJ114-02 根治（外部审计 audit-76d16a5-20260910）：此前这种情况本脚本打印警告后以**退出码 0**
    （PASS）收尾，`check.ps1` 又把子进程 stdout `| Out-Null` 丢弃，结果是完整 transcript 里这一步
    永远显示 PASS，却从没有真的跑过一次 consumer——这不是"探针通过"，是"探针整个没跑"。新语义：
      - `-SkipIfBaselineMissing`（默认 `$true`）：打印明确原因后以**退出码 3**（SKIP，不是 PASS，
        也不是 FAIL）结束；调用方（`check.ps1`）必须把 3 识别成可见 SKIP，不能当 0 处理。
      - 显式传 `-SkipIfBaselineMissing:$false`：基线缺失直接判失败，以**退出码 1**结束（供"探针
        必须真跑一次"的机器，如打包发布前的正式检查，见 check.ps1 `-AbiStrict` 开关）。

.PARAMETER ArtifactsPath
    复用 `dotnet build --artifacts-path <此值>` 产出的当前工作树 DLL（layout：
    `<ArtifactsPath>\bin\<项目名>\release\<程序集>.dll`，`check.ps1` 步骤 1 就是这样构建的，见该
    脚本 `$ArtifactsPath` 默认值 `bin\_check_artifacts`）——传入后本探针不再自己触发构建，直接读
    这份已经建好的产物，避免 `check.ps1` 全量门禁里重复构建一遍整个解决方案。省略时按顺序尝试：
    1）经典布局 `core\<module>\bin\$Configuration\netstandard2.1\<程序集>.dll`（若最近跑过一次不带
    `--artifacts-path` 的 `dotnet build`，例如手工调试本探针）；2）都找不到则本探针自己触发一次
    `dotnet build Core.sln -c $Configuration`（经典布局，不传 `--artifacts-path`），保证独立运行
    本探针（不经 check.ps1）时不依赖任何前置状态。

.PARAMETER Configuration
    与当前工作树 DLL 匹配的构建配置，默认 `Release`（ABI 探针只关心正式发布产物）。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。本文件含中文，UTF-8 with BOM（PS 5.1 默认按
    系统代码页读取不带 BOM 的脚本文件）。

    consumer 项目见 toolchain/abi_probe/（AbiProbeConsumer.csproj + Program.cs，改造自
    architecture/落地计划/audit-c9ff301-20260909/docs-project/api-compat/ 的探针原型，见该目录下
    两个文件各自头部判断记录）；表面差异工具见 toolchain/abi_surface/（AbiSurface.csproj +
    Program.cs/SurfaceDumper.cs/SurfaceCompare.cs/TypeNameFormatter.cs）。
#>
[CmdletBinding()]
param(
    [string]$BaselineVersion = "",
    [string]$BaselineZip = "",
    [string]$OutDir = "",
    [bool]$SkipIfBaselineMissing = $true,
    [string]$ArtifactsPath = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProbeDir = Join-Path $RepoRoot "toolchain\abi_probe"
$SurfaceDir = Join-Path $RepoRoot "toolchain\abi_surface"
$AllowlistFile = Join-Path $RepoRoot "toolchain\abi_surface_allowlist.txt"

function Write-ProbeLine {
    param([string]$Message)
    Write-Host "[abi_probe] $Message"
}

# -----------------------------------------------------------------------------
# PJ114-03 根治：OutDir 边界处理（见 .PARAMETER OutDir 判断记录）——任何分支都不再
# Remove-Item -Recurse 一个已经存在的目录。
#
# 判断记录（本节故意排在基线版本/zip 解析之前）：OutDir 的创建/校验不依赖基线是否存在，提前到
# 最前面执行，让"显式传入一个已存在且非空的 OutDir"这条边界路径不依赖调用方是否凑巧传了一个真实
# 可用的基线 zip 就能独立触发和验证（pytest 用一个不存在/任意的 -BaselineZip 也能测到这条路径，
# 见 toolchain/tests/test_abi_probe_outdir_safety.py 判断记录）——工具安全检查理应尽早生效，不
# 应该等其它前置步骤都通过了才轮到它。
# -----------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $created = $false
    for ($attempt = 0; $attempt -lt 20 -and -not $created; $attempt++) {
        $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $randomSuffix = -join ((1..8) | ForEach-Object { "{0:x}" -f (Get-Random -Maximum 16) })
        $candidate = Join-Path $env:TEMP ("ws-game-abi-probe-" + $stamp + "-" + $randomSuffix)
        if (Test-Path -LiteralPath $candidate) { continue }
        try {
            New-Item -ItemType Directory -Path $candidate -ErrorAction Stop | Out-Null
            $OutDir = $candidate
            $created = $true
        } catch {
            # 极小概率竞态：两个进程同时选中同一个随机路径。重试下一个随机数，不删除对方创建的目录。
        }
    }
    if (-not $created) {
        throw "连续 20 次都未能创建一个全新的默认 OutDir（$env:TEMP 下），放弃——请显式传 -OutDir 排查。"
    }
} else {
    $resolvedOutDir = [IO.Path]::GetFullPath($OutDir)
    if (Test-Path -LiteralPath $resolvedOutDir) {
        $existingItems = @(Get-ChildItem -LiteralPath $resolvedOutDir -Force -ErrorAction SilentlyContinue)
        if ($existingItems.Count -gt 0) {
            Write-ProbeLine "错误：显式 -OutDir 已存在且非空：$resolvedOutDir（PJ114-03 根治后不再对已有目录做任何删除，请换一个空/不存在的路径，或先手动清空该目录）"
            exit 1
        }
        Write-ProbeLine "显式 -OutDir 已存在且为空，直接复用：$resolvedOutDir"
        $OutDir = $resolvedOutDir
    } else {
        New-Item -ItemType Directory -Path $resolvedOutDir | Out-Null
        $OutDir = $resolvedOutDir
    }
}
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "consumer") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "consumer\lib") | Out-Null
Write-ProbeLine "中间产物目录：$OutDir"

if ([string]::IsNullOrWhiteSpace($BaselineVersion)) {
    $baselineFile = Join-Path $PSScriptRoot "abi_probe_baseline.txt"
    if (-not (Test-Path -LiteralPath $baselineFile)) {
        throw "找不到基线版本文件：$baselineFile（未显式传 -BaselineVersion 时必需）"
    }
    $BaselineVersion = (Get-Content -LiteralPath $baselineFile -Raw).Trim()
}
if ([string]::IsNullOrWhiteSpace($BaselineVersion)) {
    throw "基线版本号为空（-BaselineVersion 或 abi_probe_baseline.txt 内容）"
}

if ([string]::IsNullOrWhiteSpace($BaselineZip)) {
    $BaselineZip = Join-Path $RepoRoot ("dist\ws-game-" + $BaselineVersion + ".zip")
}

Write-ProbeLine "基线版本=$BaselineVersion 基线 zip=$BaselineZip"

if (-not (Test-Path -LiteralPath $BaselineZip)) {
    $msg = "基线发行包不存在：$BaselineZip（dist/ 是本机构建缓存，.gitignore 排除，未必存在于本机）"
    if ($SkipIfBaselineMissing) {
        Write-ProbeLine "警告：$msg —— 已跳过 ABI 探针（SKIP，退出码 3，不是 PASS）"
        exit 3
    } else {
        Write-ProbeLine "错误：$msg —— -SkipIfBaselineMissing:`$false 下基线缺失判定为失败（退出码 1）"
        exit 1
    }
}

$summaryLines = New-Object System.Collections.Generic.List[string]
$summaryLines.Add("ws-game ABI 探针 summary")
$summaryLines.Add("生成时间=" + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
$summaryLines.Add("基线版本=" + $BaselineVersion)
$summaryLines.Add("基线 zip=" + $BaselineZip)
$baselineZipHash = (Get-FileHash -LiteralPath $BaselineZip -Algorithm SHA256).Hash.ToLowerInvariant()
$summaryLines.Add("基线 zip sha256=" + $baselineZipHash)

# -----------------------------------------------------------------------------
# 六个 Core DLL 名（A5 补充：consumer 新增对 SkillHost 17 参数构造的调用，需要 Core.Numbers 提供
# IStatHost/IPowerHost 类型元数据，见 toolchain/abi_probe/AbiProbeConsumer.csproj 判断记录；
# Presentation.Common 只供 abi_surface 表面 dump 使用，consumer 探针自身不引用它）。
# -----------------------------------------------------------------------------
$consumerDllNames = @("Core.Foundation.dll", "Core.Numbers.dll", "Core.Carriers.dll", "Core.Rules.dll", "Core.Gameplay.dll")
$surfaceOnlyDllNames = @("Presentation.Common.dll")
$allDllNames = $consumerDllNames + $surfaceOnlyDllNames

# 判断记录（entry 前缀选取）：dist 发行包 zip 内这些 DLL 在多处重复存在（unity 包/toolchain
# validator 各一份），内容完全一致，任选一处即可——固定选 toolchain/validator/lib/ 这一份，
# 该路径在历次发布中都存在（见 build.ps1 -Dist 步骤），比 adapters/unity 包内路径更不容易随
# 未来目录调整而失效。
function Get-ZipEntryTo {
    param([string]$ZipPath, [string]$EntrySuffix, [string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $normalizedSuffix = $EntrySuffix.Replace('\', '/')
        $entry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/').EndsWith($normalizedSuffix) } | Select-Object -First 1
        if ($null -eq $entry) {
            throw "zip 内找不到匹配的条目（后缀 $EntrySuffix）：$ZipPath"
        }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
        $stream = $entry.Open()
        try {
            $target = [IO.File]::Create($Destination)
            try { $stream.CopyTo($target) } finally { $target.Dispose() }
        } finally { $stream.Dispose() }
    } finally {
        $archive.Dispose()
    }
}

foreach ($dll in $allDllNames) {
    $suffix = "toolchain/validator/lib/" + $dll
    $dest = Join-Path $OutDir ("consumer\lib\" + $dll)
    Get-ZipEntryTo -ZipPath $BaselineZip -EntrySuffix $suffix -Destination $dest
    Write-ProbeLine "已解出基线 DLL：$dll"
}
$summaryLines.Add("")
$summaryLines.Add("基线六个 DLL sha256：")
foreach ($dll in $allDllNames) {
    $h = (Get-FileHash -LiteralPath (Join-Path $OutDir ("consumer\lib\" + $dll)) -Algorithm SHA256).Hash.ToLowerInvariant()
    $summaryLines.Add("  $dll = $h")
}

Copy-Item -LiteralPath (Join-Path $ProbeDir "AbiProbeConsumer.csproj") -Destination (Join-Path $OutDir "consumer\AbiProbeConsumer.csproj")
Copy-Item -LiteralPath (Join-Path $ProbeDir "Program.cs") -Destination (Join-Path $OutDir "consumer\Program.cs")

$consumerDir = Join-Path $OutDir "consumer"
$consumerProj = Join-Path $OutDir "consumer\AbiProbeConsumer.csproj"
$buildLog = Join-Path $OutDir "build-against-baseline.log"
Write-ProbeLine "编译 consumer（针对基线 $BaselineVersion DLL）..."
& dotnet build $consumerProj -c Release --nologo *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $buildLog | Write-Host
    throw "consumer 针对基线 DLL 编译失败（见 $buildLog）——说明基线版本本身不具备探针假定的公开签名，需要检查 abi_probe_baseline.txt 或 toolchain/abi_probe/Program.cs 是否与基线版本匹配"
}

$consumerDll = Join-Path $OutDir "consumer\bin\Release\net8.0\AbiProbeConsumer.dll"
if (-not (Test-Path -LiteralPath $consumerDll)) {
    throw "未找到编译产物：$consumerDll"
}
$consumerHashBefore = (Get-FileHash -LiteralPath $consumerDll -Algorithm SHA256).Hash.ToLowerInvariant()

$runAgainstBaselineLog = Join-Path $OutDir "run-against-baseline.log"
& dotnet $consumerDll *> $runAgainstBaselineLog
$baselineExit = $LASTEXITCODE
$baselineOutput = (Get-Content -LiteralPath $runAgainstBaselineLog -Raw)
Write-ProbeLine "针对基线 DLL 运行退出码=$baselineExit"
if ($baselineExit -ne 0 -or ($baselineOutput -notmatch "ABI_PROBE_ALL_OK")) {
    Write-Host $baselineOutput
    throw "consumer 针对基线 DLL 本身运行失败（见 $runAgainstBaselineLog）——探针自检未通过，不代表当前工作树有 ABI 问题，需要先修好探针本身"
}
Write-ProbeLine "基线自检通过（旧 consumer 对旧 DLL 正常运行）。"

# 判断记录（两套输出布局）：`dotnet build --artifacts-path X` 与不传该参数是两套完全不同的产物
# 布局（前者连 obj/ 中间产物都会重定向，见本脚本改动时的实测：`<X>\bin\<项目名>\release\<程序集>.dll`
# vs 经典 `<module>\bin\$Configuration\netstandard2.1\<程序集>.dll`），互不覆盖也互不感知对方是否
# 存在。check.ps1 步骤 1 用前者（`$ArtifactsPath` 默认 `bin\_check_artifacts`），本探针复用同一份
# 产物时必须按同一套布局取 DLL，不能想当然用经典路径（那样在只跑过 --artifacts-path 构建的机器上
# 会永远读到"文件不存在"或读到一份过期的旧经典产物）。
$projectNames = @{
    "Core.Foundation.dll"    = "Core.Foundation"
    "Core.Numbers.dll"       = "Core.Numbers"
    "Core.Carriers.dll"      = "Core.Carriers"
    "Core.Rules.dll"         = "Core.Rules"
    "Core.Gameplay.dll"      = "Core.Gameplay"
    "Presentation.Common.dll" = "Presentation.Common"
}

function Resolve-CurrentDll {
    param([string]$DllName)

    if (-not [string]::IsNullOrWhiteSpace($ArtifactsPath)) {
        $artifactsCandidate = Join-Path $ArtifactsPath ("bin\" + $projectNames[$DllName] + "\" + $Configuration.ToLowerInvariant() + "\" + $DllName)
        if (Test-Path -LiteralPath $artifactsCandidate) {
            return $artifactsCandidate
        }
        throw "传入了 -ArtifactsPath 但找不到对应产物：$artifactsCandidate —— 请确认已先跑过 dotnet build --artifacts-path `"$ArtifactsPath`" -c $Configuration"
    }

    $classicRoots = @{
        "Core.Foundation.dll"     = "core\foundation"
        "Core.Numbers.dll"        = "core\numbers"
        "Core.Carriers.dll"       = "core\carriers"
        "Core.Rules.dll"          = "core\rules"
        "Core.Gameplay.dll"       = "core\gameplay"
        "Presentation.Common.dll" = "presentation"
    }
    $classicCandidate = Join-Path $RepoRoot ($classicRoots[$DllName] + "\bin\" + $Configuration + "\netstandard2.1\" + $DllName)
    if (Test-Path -LiteralPath $classicCandidate) {
        return $classicCandidate
    }

    if (-not $script:TriedStandaloneBuild) {
        Write-ProbeLine "未传 -ArtifactsPath 且经典布局产物不存在，独立触发一次 dotnet build Core.sln -c $Configuration（经典布局）..."
        $standaloneBuildLog = Join-Path $OutDir "standalone-build-current.log"
        & dotnet build (Join-Path $RepoRoot "Core.sln") -c $Configuration --nologo *> $standaloneBuildLog
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $standaloneBuildLog | Write-Host
            throw "独立构建当前工作树失败（见 $standaloneBuildLog）"
        }
        $script:TriedStandaloneBuild = $true
    }

    if (Test-Path -LiteralPath $classicCandidate) {
        return $classicCandidate
    }
    throw "构建后仍找不到当前工作树 DLL：$classicCandidate"
}

$currentLibDir = Join-Path $OutDir "current-lib"
New-Item -ItemType Directory -Force -Path $currentLibDir | Out-Null
foreach ($dll in $allDllNames) {
    $src = Resolve-CurrentDll -DllName $dll
    Copy-Item -LiteralPath $src -Destination (Join-Path $currentLibDir $dll) -Force
    if ($consumerDllNames -contains $dll) {
        Copy-Item -LiteralPath $src -Destination (Join-Path (Split-Path $consumerDll) $dll) -Force
    }
    Write-ProbeLine "当前工作树 DLL：$dll <- $src"
}
Write-ProbeLine "已用当前工作树 $Configuration DLL 就地替换 consumer 输出目录（未重新编译 consumer）。"
$summaryLines.Add("")
$summaryLines.Add("当前工作树六个 DLL sha256：")
foreach ($dll in $allDllNames) {
    $h = (Get-FileHash -LiteralPath (Join-Path $currentLibDir $dll) -Algorithm SHA256).Hash.ToLowerInvariant()
    $summaryLines.Add("  $dll = $h")
}

$runAgainstCurrentLog = Join-Path $OutDir "run-against-current.log"
& dotnet $consumerDll *> $runAgainstCurrentLog
$currentExit = $LASTEXITCODE
$currentOutput = (Get-Content -LiteralPath $runAgainstCurrentLog -Raw)
Write-ProbeLine "针对当前 DLL（不重编译）运行退出码=$currentExit"
$consumerHashAfter = (Get-FileHash -LiteralPath $consumerDll -Algorithm SHA256).Hash.ToLowerInvariant()

$overallOk = $true
if ($currentExit -ne 0 -or ($currentOutput -notmatch "ABI_PROBE_ALL_OK")) {
    Write-Host $currentOutput
    Write-ProbeLine "失败：旧编译 consumer 换上当前工作树的正式 DLL 后运行失败——存在未声明的二进制破坏性变更（详见上方输出与 $runAgainstCurrentLog）。"
    $overallOk = $false
} else {
    Write-ProbeLine "通过：旧编译 consumer（针对 $BaselineVersion）换上当前工作树 DLL、不重新编译，运行正常。"
}

$summaryLines.Add("")
$summaryLines.Add("consumer 程序集 sha256（替换 DLL 前）=" + $consumerHashBefore)
$summaryLines.Add("consumer 程序集 sha256（替换 DLL 后）=" + $consumerHashAfter)
$summaryLines.Add("consumer 程序集替换前后一致=" + ($consumerHashBefore -eq $consumerHashAfter))
$summaryLines.Add("consumer 针对基线 DLL 运行退出码=" + $baselineExit)
$summaryLines.Add("consumer 针对当前工作树 DLL 运行退出码（不重编译）=" + $currentExit)

# -----------------------------------------------------------------------------
# PJ114-02 根治：通用"公开 API 表面差异"门禁（toolchain/abi_surface），不再只依赖上面手写死的
# consumer 调用点——先构建 AbiSurface 工具，再分别 dump 基线/当前六个 DLL 的公开/受保护 API 表面，
# 最后 compare。任一环节失败都算本探针失败（与 consumer 探针结果一起决定最终退出码）。
# -----------------------------------------------------------------------------
Write-ProbeLine "构建 abi_surface 工具..."
$surfaceToolOut = Join-Path $OutDir "abi_surface_tool"
$surfaceBuildLog = Join-Path $OutDir "build-abi-surface.log"
& dotnet build (Join-Path $SurfaceDir "AbiSurface.csproj") -c Release --nologo -o $surfaceToolOut *> $surfaceBuildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $surfaceBuildLog | Write-Host
    throw "abi_surface 工具编译失败（见 $surfaceBuildLog）"
}
$surfaceToolDll = Join-Path $surfaceToolOut "AbiSurface.dll"

$baselineSurfaceDump = Join-Path $OutDir "surface-baseline.txt"
$currentSurfaceDump = Join-Path $OutDir "surface-current.txt"
$surfaceReport = Join-Path $OutDir "surface-report.txt"

$baselineDllPaths = $allDllNames | ForEach-Object { Join-Path $OutDir ("consumer\lib\" + $_) }
$currentDllPaths = $allDllNames | ForEach-Object { Join-Path $currentLibDir $_ }

$dumpBaselineLog = Join-Path $OutDir "surface-dump-baseline.log"
& dotnet $surfaceToolDll dump --out $baselineSurfaceDump @baselineDllPaths *> $dumpBaselineLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $dumpBaselineLog | Write-Host
    throw "abi_surface dump 基线 DLL 失败（见 $dumpBaselineLog）"
}
$dumpCurrentLog = Join-Path $OutDir "surface-dump-current.log"
& dotnet $surfaceToolDll dump --out $currentSurfaceDump @currentDllPaths *> $dumpCurrentLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $dumpCurrentLog | Write-Host
    throw "abi_surface dump 当前工作树 DLL 失败（见 $dumpCurrentLog）"
}

$compareArgs = @("compare", $baselineSurfaceDump, $currentSurfaceDump, "--out", $surfaceReport)
if (Test-Path -LiteralPath $AllowlistFile) {
    $compareArgs += @("--allowlist", $AllowlistFile)
}
& dotnet $surfaceToolDll @compareArgs
$surfaceExit = $LASTEXITCODE
Write-ProbeLine "abi_surface compare 退出码=$surfaceExit（报告：$surfaceReport）"
if ($surfaceExit -ne 0) {
    $overallOk = $false
}

$summaryLines.Add("")
$summaryLines.Add("表面差异 dump（基线）=" + $baselineSurfaceDump)
$summaryLines.Add("表面差异 dump（当前）=" + $currentSurfaceDump)
$summaryLines.Add("表面差异报告=" + $surfaceReport)
$summaryLines.Add("表面差异 compare 退出码=" + $surfaceExit)
$summaryLines.Add("")
$summaryLines.Add("结论=" + ($(if ($overallOk) { "PASS" } else { "FAIL" })))

$summaryPath = Join-Path $OutDir "summary.txt"
Set-Content -LiteralPath $summaryPath -Value $summaryLines -Encoding UTF8
Write-ProbeLine "已写入 summary.txt：$summaryPath"

if (-not $overallOk) {
    exit 1
}

exit 0
