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
    U2-1 新增。内容数据集同步（data/_sample、assets/_placeholder -> Unity 工程
    Assets/StreamingAssets/GameFoundation/）本身在默认构建流程与 -SyncOnly 下都会无条件执行
    （见下）；本开关单独传且不带 -SyncOnly 时，额外跳过 dotnet build/test 与 DLL 同步两步，
    只做内容同步（"只改了 data/_sample 或 assets/_placeholder、没有改任何 C# 代码"时的快速路径）。
    内容同步一律按文件哈希比较、只拷变化的文件，并镜像删除源目录里已经不存在、但上次同步残留在
    目标目录里的文件。

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
    [switch]$SyncContent,
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
# 4. 内容数据集同步（U2-1 新增，见 -SyncContent 参数说明）：
#      data/_sample            -> Assets/StreamingAssets/GameFoundation/data/_sample
#      assets/_placeholder     -> Assets/StreamingAssets/GameFoundation/assets/_placeholder（整体镜像）
#      assets/_placeholder/sprites -> Assets/StreamingAssets/GameFoundation/sprites（UnityResourceLoader
#        期望的 Image 路径规则，见该类型顶部注释）
#      assets/_placeholder/sfx     -> Assets/StreamingAssets/GameFoundation/audio（UnityResourceLoader
#        期望的 Audio 路径规则；源目录名 "sfx" 与目标目录名 "audio" 不同，是加载器一侧的固定
#        子目录约定，见该类型判断记录）
#    无条件执行（默认构建流程、-SyncOnly、-SyncContent 三种模式下都会执行，见参数说明）。
# ---------------------------------------------------------------------------
Write-Step "同步内容数据集到 StreamingAssets/GameFoundation/（哈希不同才拷贝，镜像删除源目录已不存在的文件）"

$StreamingAssetsRoot = Join-Path $RepoRoot "adapters\unity\Assets\StreamingAssets\GameFoundation"

# 按源目录 -> 目标目录逐一镜像同步；返回 拷贝/跳过/删除 计数，一律用绝对路径（不含尾部分隔符）
# 参与 Substring 计算相对路径，避免路径分隔符/结尾斜杠的边界情况算错相对路径。
function Sync-ContentTree {
    param(
        [string]$SourceDir,
        [string]$DestDir
    )

    if (-not (Test-Path $SourceDir)) {
        Write-Host "  源目录不存在，跳过同步：$SourceDir" -ForegroundColor Yellow
        return @{ Copied = 0; Skipped = 0; Removed = 0; Total = 0 }
    }

    $resolvedSource = (Resolve-Path $SourceDir).Path.TrimEnd('\', '/')
    if (-not (Test-Path $DestDir)) {
        New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    }
    $resolvedDest = (Resolve-Path $DestDir).Path.TrimEnd('\', '/')

    $copied = 0
    $skipped = 0
    $keepRelative = New-Object System.Collections.Generic.HashSet[string]

    $sourceFiles = Get-ChildItem -Path $resolvedSource -Recurse -File
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($resolvedSource.Length).TrimStart('\', '/')
        [void]$keepRelative.Add($relative)
        $destPath = Join-Path $resolvedDest $relative
        $changed = Copy-IfChanged -SourcePath $file.FullName -DestPath $destPath
        if ($changed) { $copied++ } else { $skipped++ }
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

    return @{ Copied = $copied; Skipped = $skipped; Removed = $removed; Total = $sourceFiles.Count }
}

$dataSyncResult = Sync-ContentTree -SourceDir (Join-Path $RepoRoot "data\_sample") -DestDir (Join-Path $StreamingAssetsRoot "data\_sample")
Write-Host ("  data/_sample -> StreamingAssets/GameFoundation/data/_sample：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $dataSyncResult.Total, $dataSyncResult.Copied, $dataSyncResult.Skipped, $dataSyncResult.Removed)

$placeholderMirrorResult = Sync-ContentTree -SourceDir (Join-Path $RepoRoot "assets\_placeholder") -DestDir (Join-Path $StreamingAssetsRoot "assets\_placeholder")
Write-Host ("  assets/_placeholder -> StreamingAssets/GameFoundation/assets/_placeholder（整体镜像）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $placeholderMirrorResult.Total, $placeholderMirrorResult.Copied, $placeholderMirrorResult.Skipped, $placeholderMirrorResult.Removed)

$spritesSyncResult = Sync-ContentTree -SourceDir (Join-Path $RepoRoot "assets\_placeholder\sprites") -DestDir (Join-Path $StreamingAssetsRoot "sprites")
Write-Host ("  assets/_placeholder/sprites -> StreamingAssets/GameFoundation/sprites（加载器路径规则）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $spritesSyncResult.Total, $spritesSyncResult.Copied, $spritesSyncResult.Skipped, $spritesSyncResult.Removed)

$audioSyncResult = Sync-ContentTree -SourceDir (Join-Path $RepoRoot "assets\_placeholder\sfx") -DestDir (Join-Path $StreamingAssetsRoot "audio")
Write-Host ("  assets/_placeholder/sfx -> StreamingAssets/GameFoundation/audio（加载器路径规则）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $audioSyncResult.Total, $audioSyncResult.Copied, $audioSyncResult.Skipped, $audioSyncResult.Removed)

# ADR-0016 决策 5 新增 ResourceKind.Effect：UnityResourceLoader.ResolveEffectDir 按
# "GameFoundation/vfx/<name>/" 解析（见该方法判断记录），与 assets/_placeholder/vfx/<name>/
# 同一套相对路径，因此整棵 vfx 目录树同步过去、不改名（不同于 sprites/sfx 需要改名到加载器
# 期望的扁平子目录，vfx 本身已经是"<kind 子目录>/<name>/"两级结构，直接对应）。
$vfxSyncResult = Sync-ContentTree -SourceDir (Join-Path $RepoRoot "assets\_placeholder\vfx") -DestDir (Join-Path $StreamingAssetsRoot "vfx")
Write-Host ("  assets/_placeholder/vfx -> StreamingAssets/GameFoundation/vfx（ResourceKind.Effect 加载器路径规则）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $vfxSyncResult.Total, $vfxSyncResult.Copied, $vfxSyncResult.Skipped, $vfxSyncResult.Removed)

$totalContentFiles = (Get-ChildItem -Path $StreamingAssetsRoot -Recurse -File -ErrorAction SilentlyContinue).Count
Write-Host ("StreamingAssets/GameFoundation/ 下文件总数（含以上五棵树的并集，sprites/audio/vfx 与 assets/_placeholder 下同名文件各自独立计数）：{0}" -f $totalContentFiles)

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

# ---------------------------------------------------------------------------
# 5. 可选：打分发包 dist/<version>/
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
