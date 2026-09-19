<#
.SYNOPSIS
    通用参数化内容同步入口（ADR-0040 决策 3，对应消费方反馈第 69 条）：把"任意源内容目录"同步进
    "任意目标目录"，不绑定框架自身工作台布局、不绑定任何 UPM 包解析结果、不对目标目录做任何
    Unity 工程/StreamingAssets 子路径拼接假设——本脚本只认字面的 -SourceDir/-TargetDir 两个路径，
    调用方自己决定要同步到哪一层目录（例如某个游戏想把自己独有的内容数据同步进自己独立维护的
    运行期工程的 Assets/StreamingAssets/GameFoundation/data/<game>）。

    与既有两个同步入口并列新增，不取代、不包装（ADR-0040 决策 3；见
    architecture/adr/0040-运行期宿主命令行能力契约.md）：
      - toolchain/sync_package_content.ps1：只服务私服交付通道，源是按 UPM 包依赖解析出的
        com.gamefoundation.framework-data 包内容，目标子目录按该包既定用途固定拼接
        （Assets/StreamingAssets/GameFoundation 等），不接受任意源/任意目标。
      - build.ps1 -SyncContent：只服务框架仓库自己的开发自测工作台，一次调用同步好几组框架仓库
        自身专属的源/目标路径对，夹带只对框架自身工作台成立的专属规则（如 .meta 排除、离开预期
        状态即失败自检）。
    本脚本是面向任意外部工具（CI 脚本、内容作者自己的工具链、第三方联调工具等）的通用原语，不为
    任何具体消费方或具体既定流程定制专属协议——三者的选择依据见 toolchain/README.md
    "参数化内容同步入口"一节对照表。

.PARAMETER SourceDir
    源内容目录，任意路径，必填。

.PARAMETER TargetDir
    目标目录，任意路径，必填。本脚本按字面路径同步（源目录树的相对路径原样对应到目标目录下的
    同一相对路径），不额外拼接任何子目录、不假设任何 Unity 工程布局。

.PARAMETER OverridePolicy
    覆盖策略，取值 "Additive"（默认）或 "Mirror"：
      - Additive：只新增/更新有变化的文件，保留目标目录里源目录中不存在的文件，不删除任何文件。
      - Mirror：以源为准，同步完成后目标目录内容与源目录一致——目标目录中相对路径不在本次源
        目录内的文件会被删除。选择 Mirror 前调用方需自行确保目标目录适合被整体镜像（该目录被
        视为由本次同步内容独占）；本脚本不像 sync_package_content.ps1 那样维护跨次调用的清单
        文件去规避误删目标目录里毫不相干的文件——通用入口的契约就是"Mirror = 目标目录等于源
        目录"，若需要与其它文件共存于同一目录，请改用 Additive 策略或换一个专属子目录作
        -TargetDir。

.PARAMETER DryRun
    干跑：只打印将要发生的改动（新增/更新，Mirror 策略下含删除），不实际写入或删除任何文件。

.NOTES
    幂等性（ADR-0040 决策 3"幂等性承诺"）：源内容本身不变的前提下重复运行不应对目标产生有意义
    的差异——本脚本按内容哈希（而非修改时间）判定"是否有变化"，未变化的文件不会被重新拷贝，
    满足这一承诺。已知例外：若源内容来自某条已知会把整数字面量重写为浮点字面量的产出流水线，
    那是该流水线自身的缺陷（已独立立项跟踪修复），不计入本入口的幂等性承诺范围——本入口只承诺
    "同步动作本身"幂等，不承诺"上游内容产出过程本身幂等"。

    PowerShell 5.1 兼容：不使用 &&、??、三元运算符，惯例同 sync_package_content.ps1。

    退出码：0 成功；1 参数错误（缺 -SourceDir/-TargetDir、-SourceDir 不存在）。同步过程本身
    （拷贝/删除文件）不会因单个文件失败而部分退出——文件系统层面的异常按 $ErrorActionPreference
    = "Stop" 直接中止并非 0 退出，不静默吞掉。
#>
param(
    [string]$SourceDir = "",
    [string]$TargetDir = "",
    [ValidateSet("Additive", "Mirror")]
    [string]$OverridePolicy = "Additive",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# 复用仓库共享的 SHA256 哈希函数（纯粹的底层复制原语，不是"包装既有同步入口"——见头部判断记录、
# ADR-0040 决策 3"该新入口未来是否在内部实现上复用与既有入口相同的底层复制原语，是纯粹的实现期
# 内部选择"）。
. (Join-Path $PSScriptRoot "_hash.ps1")

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

if ($SourceDir -eq "") {
    Write-Host "-SourceDir 必填：要同步的源内容目录" -ForegroundColor Red
    exit 1
}
if ($TargetDir -eq "") {
    Write-Host "-TargetDir 必填：要同步进的目标目录" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $SourceDir)) {
    Write-Host "找不到 -SourceDir：$SourceDir" -ForegroundColor Red
    exit 1
}

$resolvedSource = (Resolve-Path $SourceDir).Path.TrimEnd('\', '/')

if ($DryRun) {
    Write-Step "干跑模式：$resolvedSource -> $TargetDir（策略=$OverridePolicy，不写入任何文件）"
} else {
    Write-Step "同步内容：$resolvedSource -> $TargetDir（策略=$OverridePolicy）"
}

if (-not (Test-Path $TargetDir)) {
    if ($DryRun) {
        Write-Host "  目标目录尚不存在，若非干跑将创建：$TargetDir" -ForegroundColor Yellow
    } else {
        New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
    }
}

# 干跑模式下目标目录可能仍不存在（未真正创建），Resolve-Path 对不存在的路径会报错——分两条路径
# 计算参与相对路径运算的基准，结果与真实创建后一致（都是去掉尾部分隔符的绝对/字面路径）。
if (Test-Path $TargetDir) {
    $resolvedTarget = (Resolve-Path $TargetDir).Path.TrimEnd('\', '/')
} else {
    $resolvedTarget = $TargetDir.TrimEnd('\', '/')
}

$sourceFiles = Get-ChildItem -Path $resolvedSource -Recurse -File
$keepRelative = New-Object System.Collections.Generic.HashSet[string]

$added = 0
$updated = 0
$unchanged = 0

foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($resolvedSource.Length).TrimStart('\', '/')
    [void]$keepRelative.Add($relative)
    $destPath = Join-Path $resolvedTarget $relative

    $destExists = Test-Path $destPath
    $same = $false
    if ($destExists) {
        $srcHash = Get-Sha256FileHash -Path $file.FullName
        $dstHash = Get-Sha256FileHash -Path $destPath
        $same = ($srcHash -eq $dstHash)
    }

    if ($same) {
        $unchanged++
        continue
    }

    if ($destExists) {
        $updated++
        Write-Host "  [更新] $relative"
    } else {
        $added++
        Write-Host "  [新增] $relative"
    }

    if (-not $DryRun) {
        $destFileDir = Split-Path -Parent $destPath
        if (-not (Test-Path $destFileDir)) { New-Item -ItemType Directory -Force -Path $destFileDir | Out-Null }
        Copy-Item -Path $file.FullName -Destination $destPath -Force
    }
}

$removed = 0
if ($OverridePolicy -eq "Mirror" -and (Test-Path $resolvedTarget)) {
    $destFiles = Get-ChildItem -Path $resolvedTarget -Recurse -File
    foreach ($destFile in $destFiles) {
        $relative = $destFile.FullName.Substring($resolvedTarget.Length).TrimStart('\', '/')
        if ($keepRelative.Contains($relative)) { continue }
        $removed++
        Write-Host "  [删除] $relative（Mirror 策略：目标独有，本次源目录中不存在）"
        if (-not $DryRun) {
            Remove-Item -Path $destFile.FullName -Force
        }
    }
}

Write-Host ""
if ($DryRun) {
    Write-Host ("==== 干跑完成：将新增 {0}，更新 {1}，删除 {2}，不变 {3}（未写入任何文件） ====" -f $added, $updated, $removed, $unchanged) -ForegroundColor Green
} else {
    Write-Host ("==== 同步完成：新增 {0}，更新 {1}，删除 {2}，不变 {3} ====" -f $added, $updated, $removed, $unchanged) -ForegroundColor Green
}
exit 0
