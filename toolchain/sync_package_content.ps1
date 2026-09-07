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

function Sync-Tree {
    param([string]$SourceDir, [string]$TargetDir, [string]$Label)
    if (-not (Test-Path $SourceDir)) {
        Write-Host "  源目录不存在，跳过：$SourceDir" -ForegroundColor Yellow
        return
    }
    if (-not (Test-Path $TargetDir)) { New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null }
    $resolvedSource = (Resolve-Path $SourceDir).Path.TrimEnd('\', '/')
    $resolvedTarget = (Resolve-Path $TargetDir).Path.TrimEnd('\', '/')

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
    if (Test-Path $resolvedTarget) {
        foreach ($destFile in (Get-ChildItem -Path $resolvedTarget -Recurse -File)) {
            $relative = $destFile.FullName.Substring($resolvedTarget.Length).TrimStart('\', '/')
            if (-not $keep.Contains($relative)) {
                Remove-Item -Path $destFile.FullName -Force
                $removed++
            }
        }
    }
    Write-Host ("  {0}：共 {1} 个文件，拷贝 {2}，跳过 {3}，删除 {4}" -f $Label, $keep.Count, $copied, $skipped, $removed)
}

Write-Step "同步 Data~/data/_framework -> $DestDir\data\_framework"
Sync-Tree -SourceDir (Join-Path $dataDir "data\_framework") -TargetDir (Join-Path $DestDir "data\_framework") -Label "data/_framework"

Write-Step "同步 Data~/assets/_placeholder -> $DestDir\assets\_placeholder"
Sync-Tree -SourceDir (Join-Path $dataDir "assets\_placeholder") -TargetDir (Join-Path $DestDir "assets\_placeholder") -Label "assets/_placeholder"

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
