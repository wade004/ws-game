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

$dataFrameworkSyncResult = Sync-ContentTree -SourceDir (Join-Path $RepoRoot "data\_framework") -DestDir (Join-Path $StreamingAssetsRoot "data\_framework")
Write-Host ("  data/_framework -> StreamingAssets/GameFoundation/data/_framework：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $dataFrameworkSyncResult.Total, $dataFrameworkSyncResult.Copied, $dataFrameworkSyncResult.Skipped, $dataFrameworkSyncResult.Removed)

$dataSyncResult = Sync-ContentTree -SourceDir (Join-Path $RepoRoot "data\_sample") -DestDir (Join-Path $StreamingAssetsRoot "data\_sample")
Write-Host ("  data/_sample -> StreamingAssets/GameFoundation/data/_sample：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $dataSyncResult.Total, $dataSyncResult.Copied, $dataSyncResult.Skipped, $dataSyncResult.Removed)

# games/_template 自带的最小数据集（见 games/_template/data/README.md）同步进工作台 StreamingAssets，
# 供 games/_template/Runtime/GameBootstrap.cs 的 PlayMode 测试（在工作台里跑，见
# games/_template/Tests/Runtime）默认 GameOptions（_gameDatasetRoot = "data/game"）能找到数据；
# 与 data/_framework、data/_sample 同一治理方式（构建期产物、gitignore，不进源码库）。
$templateDataSyncResult = Sync-ContentTree -SourceDir (Join-Path $RepoRoot "games\_template\data\game") -DestDir (Join-Path $StreamingAssetsRoot "data\game")
Write-Host ("  games/_template/data/game -> StreamingAssets/GameFoundation/data/game（模板自带最小数据集，供模板 PlayMode 测试使用）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $templateDataSyncResult.Total, $templateDataSyncResult.Copied, $templateDataSyncResult.Skipped, $templateDataSyncResult.Removed)

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
    Write-Step "打分发包 dist/$ResolvedDistVersion/"

    $DistRoot = Join-Path $RepoRoot ("dist\" + $ResolvedDistVersion)
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
        Write-Host ("  {0} -> dist\{1}\{2}  ({3} files)" -f $SourceRelative, $ResolvedDistVersion, $DestName, $fileCount)
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
    $distPluginsCoreDir = Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"
    foreach ($asm in $CoreAssemblies) {
        $distDllPath = Join-Path $distPluginsCoreDir ($asm.Name + ".dll")
        if (Test-Path $distDllPath) {
            $sha = (Get-FileHash -Path $distDllPath -Algorithm SHA256).Hash.ToLower()
            $coreAssemblyLines += ("  {0}.dll: sha256={1}" -f $asm.Name, $sha)
        } else {
            $coreAssemblyLines += ("  {0}.dll: 未找到（{1}）" -f $asm.Name, $distDllPath)
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
} else {
    Write-Step "未传 -Dist，跳过打包步骤"
}

Write-Host ""
Write-Host "build.ps1 完成。" -ForegroundColor Green
exit 0
