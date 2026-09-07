<#
.SYNOPSIS
    私服交付通道配套工具：把游戏工程通过 UPM 私服（作用域注册表 com.gamefoundation）解析到的
    com.gamefoundation.framework-data / com.gamefoundation.toolchain 两个包内容（Data~/Tools~，
    Unity 资产管线不导入，只在磁盘上）同步/定位到该工程可以实际用到的位置——引擎运行时只从
    Application.streamingAssetsPath（即工程自己的 Assets/StreamingAssets/）读数据表/占位资产字节
    （见 com.gamefoundation.adapter.unity 包 Runtime/EngineAdapter/UnityFileSystem.cs
    GetContentRootDir 判断记录），不可能直接指向 Library/PackageCache/ 下的包解析路径，因此需要
    这一步"从包目录拷贝/镜像到工程自己的 StreamingAssets（以及 TextMeshPro 运行期资源、占位字体
    两个不走 StreamingAssets 的例外目标）"。完整背景见 com.gamefoundation.framework-data 包内
    README.md"判断记录"一节。

    只服务私服（按版本号依赖）这一条消费通道；zip 快照通道（file: 引用 dist/<ver>/ 或
    packages/ws-game-<ver>/）的同款同步职责由框架仓库自己的 build.ps1（同步进框架自己的工作台
    工程）与 toolchain/consumer_smoke.ps1（消费方演练脚本，镜像拷贝进消费方工程）承担，两条通道
    互不依赖，见根 README.md"版本与发布"一节、toolchain/registry/README.md"离线 zip 通道并存
    说明"。

.PARAMETER UnityProjectPath
    要同步进哪个游戏工程，传该工程根目录（Packages/manifest.json 所在目录的上一级）。必填，除非
    只传 -ResolveOnly 且只是想看看包解析到了哪个路径。

.PARAMETER PackageName
    要处理的包名，默认 com.gamefoundation.framework-data；也支持 com.gamefoundation.toolchain
    （该包默认只解析路径不做拷贝，见下 -DestDir 说明）。

.PARAMETER DestDir
    framework-data 包的 StreamingAssets 同步目标根，默认
    "<UnityProjectPath>/Assets/StreamingAssets/GameFoundation"；toolchain 包不使用该参数
    （其内容按路径直接用 python 调用，不需要拷进工程，见 com.gamefoundation.toolchain 包 README）。

.PARAMETER ResolveOnly
    只解析并打印 Library/PackageCache/ 下该包实际落地的路径，不做任何拷贝；用于人工核对，或配合
    com.gamefoundation.toolchain 包 README 里"用 python 直接调用包解析路径下的脚本，不拷出来"
    这条更常见的用法。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
    判断记录（为什么用 Library/PackageCache/<name>@* 通配匹配而不是精确路径）：UPM 解析一个来自
    注册表的包时，落地目录名是 "<包名>@<解析出的版本号或内容哈希>"（具体后缀形式随 Unity 版本与
    包来源略有差异，不假设固定格式），调用方在写这个脚本时不知道、也不需要关心具体后缀，只关心
    "当前工程实际解析到了哪一个"——按包名前缀通配、要求"当前只应该有一个匹配目录"（UPM 同一个
    工程同一个包名同一时刻只会解析出一个版本），多于一个按"目录修改时间最新的一个"处理并打印警告
    （理论上不会发生，除非手工残留了旧版本目录未被 UPM 清理）。
#>
param(
    [string]$UnityProjectPath = "",
    [string]$PackageName = "com.gamefoundation.framework-data",
    [string]$DestDir = "",
    [switch]$ResolveOnly
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

if ($UnityProjectPath -eq "") {
    Write-Host "-UnityProjectPath 必填：要同步进哪个 Unity 工程" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $UnityProjectPath)) {
    Write-Host "找不到 -UnityProjectPath：$UnityProjectPath" -ForegroundColor Red
    exit 1
}
$UnityProjectPath = (Resolve-Path $UnityProjectPath).Path

$packageCacheDir = Join-Path $UnityProjectPath "Library\PackageCache"
if (-not (Test-Path $packageCacheDir)) {
    Write-Host "找不到 $packageCacheDir（工程是否已经用 Unity 打开过一次、解析完 Packages/manifest.json 里的依赖？）" -ForegroundColor Red
    exit 1
}

# -----------------------------------------------------------------------------
# 1. 解析包路径：Library/PackageCache/<PackageName>@* 通配匹配，见头部判断记录。
# -----------------------------------------------------------------------------
Write-Step "解析包路径：$packageCacheDir\$PackageName@*"
$matched = @(Get-ChildItem -Path $packageCacheDir -Directory -Filter ($PackageName + "@*"))
if ($matched.Count -eq 0) {
    Write-Host "找不到匹配 $PackageName@* 的目录——包是否已经在该工程的 Packages/manifest.json 里声明并成功解析？" -ForegroundColor Red
    exit 1
}
if ($matched.Count -gt 1) {
    Write-Host "警告：匹配到 $($matched.Count) 个目录，按修改时间取最新一个（理论上不应发生，见头部判断记录）：" -ForegroundColor Yellow
    foreach ($m in $matched) { Write-Host "  $($m.FullName)" }
}
$resolvedPackageDir = ($matched | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
Write-Host "  已解析：$resolvedPackageDir"

if ($ResolveOnly) {
    Write-Host ""
    Write-Host "==== -ResolveOnly：只打印路径，未做任何拷贝 ====" -ForegroundColor Green
    Write-Host "  $resolvedPackageDir"
    exit 0
}

# -----------------------------------------------------------------------------
# 2. 按包名分流：目前只有 framework-data 需要真正的同步动作。
# -----------------------------------------------------------------------------
if ($PackageName -ne "com.gamefoundation.framework-data") {
    Write-Host ""
    Write-Host "==== $PackageName 不需要同步动作，按路径直接调用即可（见该包 README.md） ====" -ForegroundColor Green
    Write-Host "  $resolvedPackageDir"
    exit 0
}

$dataDir = Join-Path $resolvedPackageDir "Data~"
if (-not (Test-Path $dataDir)) {
    Write-Host "找不到 $dataDir（包内容布局是否与 com.gamefoundation.framework-data README.md 描述一致？）" -ForegroundColor Red
    exit 1
}

if ($DestDir -eq "") {
    $DestDir = Join-Path $UnityProjectPath "Assets\StreamingAssets\GameFoundation"
}

# 与 build.ps1 Copy-IfChanged/Sync-ContentTree 同一套哈希比较 + 镜像删除逻辑（独立实现，本脚本
# 运行在游戏工程侧，不依赖框架仓库源码树，见头部判断记录第二段）。
function Copy-IfChanged {
    param([string]$SourcePath, [string]$DestPath)
    if (Test-Path $DestPath) {
        $srcHash = (Get-FileHash -Path $SourcePath -Algorithm SHA256).Hash
        $dstHash = (Get-FileHash -Path $DestPath -Algorithm SHA256).Hash
        if ($srcHash -eq $dstHash) { return $false }
    }
    $destDir = Split-Path -Parent $DestPath
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
    Copy-Item -Path $SourcePath -Destination $DestPath -Force
    return $true
}

# 判断记录（P03 根治，2026-09-07，审计 architecture/落地计划/audit-7e63d66-20260907/
# project-review.md P03）：此前 Sync-Tree 对目标目录做整树镜像——凡是目标目录里存在、但这一次
# 源目录里没有同名相对路径的文件，一律删除。这对包专属目录（如 Assets/StreamingAssets/
# GameFoundation/data/_framework）是对的（该目录树整体由框架内容拥有，理应镜像），但
# $tmpDest = "Assets\TextMesh Pro" 是 Unity/TextMeshPro 官方约定的公共目录，消费者游戏自己的
# 字体、材质、.meta 完全可能与框架同步进来的 TMP Essential Resources 共处同一目录树；整树镜像会
# 把这些消费者自己的文件一并删除（实测复现：预置的 game-owned-sentinel.txt 被删除）。
#
# 根治为清单制：每次同步在目标目录写一份 ".gamefoundation-sync-manifest.json"，记录"这一次本脚本
# 自己往这个目标目录写入的文件相对路径集合"；下一次运行时只对比"上一次清单里有、这一次源目录里
# 已经没有"的文件（即框架侧确实被移除的 stale 文件）执行删除，从不删除任何从未出现在某一次清单里
# 的文件——消费者自己的文件永远不会被本脚本写进清单，因此永远不会成为删除候选，无论目标目录是
# 包专属目录还是像 Assets/TextMesh Pro 这样的公共目录，同一套逻辑都安全。首次运行（尚无清单文件）
# 保守地不清理任何东西，只做拷贝——避免在采用这个新版本脚本的第一次运行时，把"这次改版之前已经
# 存在、但从未被清单记录过"的旧框架文件误判为消费者文件而永久遗留（可接受：那批遗留文件下次
# 框架内容变化、清单开始追踪后即可被正常清理；比起继续用整树镜像、冒删除消费者文件的风险，这个
# 权衡是有意为之）。
function Sync-Tree {
    param([string]$SourceDir, [string]$TargetDir, [string]$Label)
    if (-not (Test-Path $SourceDir)) {
        Write-Host "  源目录不存在，跳过：$SourceDir" -ForegroundColor Yellow
        return
    }
    if (-not (Test-Path $TargetDir)) { New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null }
    $resolvedSource = (Resolve-Path $SourceDir).Path.TrimEnd('\', '/')
    $resolvedTarget = (Resolve-Path $TargetDir).Path.TrimEnd('\', '/')

    $manifestPath = Join-Path $resolvedTarget ".gamefoundation-sync-manifest.json"
    $previousManaged = New-Object System.Collections.Generic.HashSet[string]
    if (Test-Path $manifestPath) {
        try {
            $manifestJson = Get-Content -Path $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($manifestJson -and $manifestJson.managed_files) {
                foreach ($p in @($manifestJson.managed_files)) { [void]$previousManaged.Add($p) }
            }
        } catch {
            Write-Host "  警告：解析既有同步清单失败，按空清单处理（本次只拷贝、不清理 stale 文件）：$manifestPath" -ForegroundColor Yellow
        }
    }

    $keep = New-Object System.Collections.Generic.HashSet[string]
    $copied = 0
    $skipped = 0
    foreach ($file in (Get-ChildItem -Path $resolvedSource -Recurse -File)) {
        $relative = $file.FullName.Substring($resolvedSource.Length).TrimStart('\', '/')
        [void]$keep.Add($relative)
        $destPath = Join-Path $resolvedTarget $relative
        if (Copy-IfChanged -SourcePath $file.FullName -DestPath $destPath) { $copied++ } else { $skipped++ }
    }

    $removed = 0
    foreach ($relative in $previousManaged) {
        if ($keep.Contains($relative)) { continue }
        $staleDestPath = Join-Path $resolvedTarget $relative
        if (Test-Path $staleDestPath) {
            Remove-Item -Path $staleDestPath -Force
            $removed++
            # 一并清理 Unity 为该框架文件自动生成的伴生 .meta（若存在）——.meta 本身不进清单
            # （清单只记录源目录里的真实文件），但既然同名的框架文件是本脚本上次写入、这次确认
            # stale 要清理的，这个 .meta 几乎必然是 Unity 针对它自动生成的，一并删除，避免残留
            # "meta 文件存在但对应资产已消失"的悬空 .meta。
            $staleMetaPath = $staleDestPath + ".meta"
            if (Test-Path $staleMetaPath) { Remove-Item -Path $staleMetaPath -Force }
        }
    }

    $manifestObj = [ordered]@{
        generated_by = "toolchain/sync_package_content.ps1"
        note          = "本文件记录框架包上一次同步实际写入本目录的文件清单，仅用于下一次同步判定" +
                         "哪些文件属于框架侧 stale（源目录已经删除，需要清理）——不在此清单内的文件" +
                         "（含消费者自己的文件）永远不会被本脚本删除，见脚本内 Sync-Tree 判断记录。"
        managed_files = @($keep | Sort-Object)
    }
    ($manifestObj | ConvertTo-Json -Depth 5) | Set-Content -Path $manifestPath -Encoding utf8

    Write-Host ("  {0}：共 {1} 个文件，拷贝 {2}，跳过 {3}，清理 stale {4}" -f $Label, $keep.Count, $copied, $skipped, $removed)
}

Write-Step "同步 Data~/data/_framework -> $DestDir\data\_framework"
Sync-Tree -SourceDir (Join-Path $dataDir "data\_framework") -TargetDir (Join-Path $DestDir "data\_framework") -Label "data/_framework"

Write-Step "同步 Data~/assets/_placeholder -> $DestDir\assets\_placeholder"
Sync-Tree -SourceDir (Join-Path $dataDir "assets\_placeholder") -TargetDir (Join-Path $DestDir "assets\_placeholder") -Label "assets/_placeholder"

# 判断记录（P07 根治，2026-09-07，审计 architecture/落地计划/audit-7e63d66-20260907/
# project-review.md P07）：上面这一步只是把 assets/_placeholder 原样整体镜像进
# $DestDir\assets\_placeholder（即 .../GameFoundation/assets/_placeholder/sfx/ui_click_01.wav
# 这样的原始子目录名），但 UnityResourceLoader（adapters/unity/Packages/com.gamefoundation.
# adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs）按资源种类查找的是
# GameFoundation/sprites/...、GameFoundation/audio/...、GameFoundation/vfx/... 这几个"扁平"
# 目标子目录，不是 GameFoundation/assets/_placeholder/<原始子目录名>/...——两棵目录树不是同一个
# 路径，新消费方按加载器实际路径规则请求资源会找不到。框架仓库自己的 build.ps1（同步进工作台
# adapters/unity/Assets/StreamingAssets/GameFoundation/）一直有这一步"改名/扁平化"同步，本脚本
# （私服交付通道，同步进消费方 Unity 工程）此前完全遗漏。现在两处从同一份
# toolchain/resource_layout_map.json（随本脚本一起打进 com.gamefoundation.toolchain 包
# Tools~/，见该文件判断记录）读取 source -> target 子目录名映射，不再只由 build.ps1 单独维护，
# 避免再次漂移。framework-data 包只随附 assets/_placeholder（不含 assets/_sample——那是框架仓库
# 自测用的示例资产，不随包分发，见该包 README.md），因此这里只需要单一源目录，不像 build.ps1
# 那样还要额外合并 assets/_sample。
$resourceLayoutMapPath = Join-Path $PSScriptRoot "resource_layout_map.json"
if (-not (Test-Path $resourceLayoutMapPath)) {
    Write-Host "找不到 $resourceLayoutMapPath（sprites/audio/vfx 目标目录映射表，P07 根治新增）" -ForegroundColor Red
    exit 1
}
$resourceLayoutMap = (Get-Content -Path $resourceLayoutMapPath -Raw -Encoding UTF8) | ConvertFrom-Json
foreach ($mapping in $resourceLayoutMap.mappings) {
    $sourceSubdir = $mapping.source
    $targetSubdir = $mapping.target
    $mappingSourceDir = Join-Path $dataDir ("assets\_placeholder\" + $sourceSubdir)
    $mappingTargetDir = Join-Path $DestDir $targetSubdir
    Write-Step "同步 Data~/assets/_placeholder/$sourceSubdir -> $mappingTargetDir（加载器路径规则，见 toolchain/resource_layout_map.json）"
    Sync-Tree -SourceDir $mappingSourceDir -TargetDir $mappingTargetDir -Label ("assets/_placeholder/" + $sourceSubdir + " -> " + $targetSubdir)
}

# 与 games/_template/README.md"复制为新游戏：改哪几处"第 8 步、com.gamefoundation.framework-data
# 包 README.md 判断记录一致：TMP 运行期资源与占位字体不进 StreamingAssets，是两个例外目标。
$tmpDest = Join-Path $UnityProjectPath "Assets\TextMesh Pro"
Write-Step "同步 Data~/assets/textmesh_pro_essentials -> $tmpDest（不进 StreamingAssets，见判断记录）"
Sync-Tree -SourceDir (Join-Path $dataDir "assets\textmesh_pro_essentials") -TargetDir $tmpDest -Label "assets/textmesh_pro_essentials"

$fontsSourceDir = Join-Path $dataDir "assets\_placeholder\fonts"
$fontsDestDir = Join-Path $UnityProjectPath "Assets\Framework\Resources\Fonts"
if (Test-Path $fontsSourceDir) {
    Write-Step "同步占位字体 -> $fontsDestDir（不进 StreamingAssets，同 build.ps1 字体同步判断记录）"
    if (-not (Test-Path $fontsDestDir)) { New-Item -ItemType Directory -Force -Path $fontsDestDir | Out-Null }
    $fontFiles = Get-ChildItem -Path $fontsSourceDir -File | Where-Object { $_.Extension -eq ".otf" -or $_.Extension -eq ".ttf" }
    $fontCopied = 0
    $fontSkipped = 0
    foreach ($f in $fontFiles) {
        $destPath = Join-Path $fontsDestDir $f.Name
        if (Copy-IfChanged -SourcePath $f.FullName -DestPath $destPath) { $fontCopied++ } else { $fontSkipped++ }
    }
    Write-Host ("  字体：共 {0} 个文件，拷贝 {1}，跳过 {2}" -f $fontFiles.Count, $fontCopied, $fontSkipped)
}

Write-Host ""
Write-Host "==== sync_package_content.ps1 完成 ====" -ForegroundColor Green
