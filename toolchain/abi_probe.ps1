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
    取证脚本）：本脚本参数化基线版本、覆盖本轮恢复的全部 8 个签名（六个整条删除的公开规则类型 +
    两处构造签名收窄，见 toolchain/abi_probe/Program.cs）、四个程序集，且是"期望通过"的常规门禁
    （非零退出即失败），供 check.ps1 全量步骤与后续 build.ps1 -Release 发布前重复调用。

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
    本次探针的全部中间产物（解出的旧 DLL、consumer 编译输出、日志）落地目录，默认本次会话
    scratchpad 下的临时目录（Join-Path 到 `$env:TEMP`），运行结束不清理（供失败后追查现场），
    每次运行使用带时间戳的全新子目录，不复用上一次运行的残留。

.PARAMETER SkipIfBaselineMissing
    默认行为：找不到 `-BaselineZip`/`dist/ws-game-<BaselineVersion>.zip` 时，本脚本打印明确警告
    并以 **退出码 0**（PASS，非 FAIL）结束——`dist/` 是 `.gitignore` 排除的本机构建缓存目录（见
    仓库根 .gitignore），全新 clone/CI 环境很可能没有历史版本的本地 zip，此时"探针无法运行"不等于
    "ABI 有问题"，不应该阻塞门禁；本机若曾经 `build.ps1 -Release` 打过 1.12.0，这份 zip 就会存在，
    探针才真正生效。显式传 `-SkipIfBaselineMissing:$false` 可以把"基线缺失"也当成失败处理（供
    专门要求"探针必须真跑一次"的机器，如打包发布前的正式检查）。

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

    consumer 项目见 toolchain/abi_probe/（AbiProbeConsumer.csproj + Program.cs），改造自
    architecture/落地计划/audit-c9ff301-20260909/docs-project/api-compat/ 的探针原型，见该目录下
    两个文件各自头部判断记录。
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

function Write-ProbeLine {
    param([string]$Message)
    Write-Host "[abi_probe] $Message"
}

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
        Write-ProbeLine "警告：$msg —— 已跳过 ABI 探针（不判定为失败）"
        exit 0
    } else {
        throw $msg
    }
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutDir = Join-Path $env:TEMP ("ws-game-abi-probe-" + $stamp)
}
if (Test-Path -LiteralPath $OutDir) {
    Remove-Item -LiteralPath $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "consumer") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "consumer\lib") | Out-Null
Write-ProbeLine "中间产物目录：$OutDir"

$dllNames = @("Core.Foundation.dll", "Core.Carriers.dll", "Core.Rules.dll", "Core.Gameplay.dll")
# 判断记录（entry 前缀选取）：dist 发行包 zip 内四个 DLL 在多处重复存在（unity 包/toolchain
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

foreach ($dll in $dllNames) {
    $suffix = "toolchain/validator/lib/" + $dll
    $dest = Join-Path $OutDir ("consumer\lib\" + $dll)
    Get-ZipEntryTo -ZipPath $BaselineZip -EntrySuffix $suffix -Destination $dest
    Write-ProbeLine "已解出基线 DLL：$dll"
}

Copy-Item -LiteralPath (Join-Path $ProbeDir "AbiProbeConsumer.csproj") -Destination (Join-Path $OutDir "consumer\AbiProbeConsumer.csproj")
Copy-Item -LiteralPath (Join-Path $ProbeDir "Program.cs") -Destination (Join-Path $OutDir "consumer\Program.cs")

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
    "Core.Foundation.dll" = "Core.Foundation"
    "Core.Carriers.dll"   = "Core.Carriers"
    "Core.Rules.dll"      = "Core.Rules"
    "Core.Gameplay.dll"   = "Core.Gameplay"
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
        "Core.Foundation.dll" = "core\foundation"
        "Core.Carriers.dll"   = "core\carriers"
        "Core.Rules.dll"      = "core\rules"
        "Core.Gameplay.dll"   = "core\gameplay"
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

foreach ($dll in $dllNames) {
    $src = Resolve-CurrentDll -DllName $dll
    $dest = Join-Path (Split-Path $consumerDll) $dll
    Copy-Item -LiteralPath $src -Destination $dest -Force
    Write-ProbeLine "当前工作树 DLL：$dll <- $src"
}
Write-ProbeLine "已用当前工作树 $Configuration DLL 就地替换 consumer 输出目录（未重新编译 consumer）。"

$runAgainstCurrentLog = Join-Path $OutDir "run-against-current.log"
& dotnet $consumerDll *> $runAgainstCurrentLog
$currentExit = $LASTEXITCODE
$currentOutput = (Get-Content -LiteralPath $runAgainstCurrentLog -Raw)
Write-ProbeLine "针对当前 DLL（不重编译）运行退出码=$currentExit"

if ($currentExit -ne 0 -or ($currentOutput -notmatch "ABI_PROBE_ALL_OK")) {
    Write-Host $currentOutput
    Write-ProbeLine "失败：旧编译 consumer 换上当前工作树的正式 DLL 后运行失败——存在未声明的二进制破坏性变更（详见上方输出与 $runAgainstCurrentLog）。"
    exit 1
}

Write-ProbeLine "通过：旧编译 consumer（针对 $BaselineVersion）换上当前工作树 DLL、不重新编译，运行正常。"
Write-Host $currentOutput
exit 0
