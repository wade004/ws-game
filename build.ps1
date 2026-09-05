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

.PARAMETER Dist
    传入版本号（如 0.0.1）时，额外把引擎适配层包（含刚同步的 DLL）、games/_template、
    toolchain（排除 .venv 与 __pycache__）、assets/_placeholder 复制到 dist/<version>/ 下，
    并生成 MANIFEST.txt。不传则跳过打包步骤。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SyncOnly,
    [string]$Dist = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot "Core.sln"
$PluginsCoreDir = Join-Path $RepoRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"

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

if ($SyncOnly) {
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
# ---------------------------------------------------------------------------
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

# ---------------------------------------------------------------------------
# 4. 可选：打分发包 dist/<version>/
# ---------------------------------------------------------------------------
if ($Dist -ne "") {
    Write-Step "打分发包 dist/$Dist/"

    $DistRoot = Join-Path $RepoRoot ("dist\" + $Dist)
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

            $skip = $false
            foreach ($ex in $ExcludeDirNames) {
                if ($relative -eq $ex -or $relative.StartsWith($ex + "\")) {
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
        Write-Host ("  {0} -> dist\{1}\{2}  ({3} files)" -f $SourceRelative, $Dist, $DestName, $fileCount)
        return $fileCount
    }

    $adapterFileCount = Copy-DistDir -SourceRelative "adapters\unity\Packages\com.gamefoundation.adapter.unity" -DestName "adapters\unity\Packages\com.gamefoundation.adapter.unity"
    $templateFileCount = Copy-DistDir -SourceRelative "games\_template" -DestName "games\_template"
    $toolchainFileCount = Copy-DistDir -SourceRelative "toolchain" -DestName "toolchain" -ExcludeDirNames @(".venv", "__pycache__")
    $assetsFileCount = Copy-DistDir -SourceRelative "assets\_placeholder" -DestName "assets\_placeholder"

    $manifestPath = Join-Path $DistRoot "MANIFEST.txt"
    $manifestLines = @(
        "version: $Dist",
        "date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity: $adapterFileCount files",
        "games/_template: $templateFileCount files",
        "toolchain: $toolchainFileCount files",
        "assets/_placeholder: $assetsFileCount files"
    )
    Set-Content -Path $manifestPath -Value $manifestLines -Encoding utf8
    Write-Host "已生成 $manifestPath"
} else {
    Write-Step "未传 -Dist，跳过打包步骤"
}

Write-Host ""
Write-Host "build.ps1 完成。" -ForegroundColor Green
exit 0
