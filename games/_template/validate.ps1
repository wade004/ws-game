<#
.SYNOPSIS
    校验本游戏数据目录（默认 data\game）与框架分发包的框架级数据表（data\_framework）合并后
    是否 0 错误，调用框架仓库的 toolchain/validate_data.py（见该脚本文件头说明"新游戏数据目录
    校验"用法）。

.PARAMETER FrameworkRoot
    框架级数据表目录，默认按"本模板与框架仓库/分发包同级"猜测两处常见位置之一（优先分发包
    dist/<version>/data/_framework，其次开发期直接指向框架仓库的 data/_framework）；找不到时
    报错并提示显式传参。

.PARAMETER DataRoot
    本游戏数据目录，默认 .\data\game（复制模板改名后同步改这里的默认值）。

.PARAMETER PythonExe
    Python 可执行文件，默认 "python"（PATH 上可解析）。

.PARAMETER SkipDotnet
    只跑第一道骨架检查，跳过第二道真实校验（透传 validate_data.py 的 --skip-dotnet）。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
#>
param(
    [string]$FrameworkRoot = "",
    [string]$DataRoot = "",
    [string]$ValidateDataScript = "",
    [string]$PythonExe = "python",
    [switch]$SkipDotnet,
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

$TemplateRoot = $PSScriptRoot

if ($DataRoot -eq "") {
    $DataRoot = Join-Path $TemplateRoot "data\game"
}

if ($ValidateDataScript -eq "") {
    # 判断记录（找 toolchain/validate_data.py 的默认位置）：本模板被复制到新游戏工程后，
    # "框架仓库在哪"不是固定相对路径——可能是开发期直接引用框架仓库源码，也可能是引用分发包
    # dist/<version>/。按以下优先级依次尝试，找到第一个存在的就用；都找不到则报错提示用户显式传
    # -ValidateDataScript。
    $candidates = @(
        (Join-Path $TemplateRoot "..\..\toolchain\validate_data.py"),          # 开发期：模板仍在 ws-game 仓库内（games/_template 相对 toolchain）
        (Join-Path $TemplateRoot "..\toolchain\validate_data.py"),             # 分发包：dist/<version>/{games/_template, toolchain}/ 同级
        (Join-Path $TemplateRoot "toolchain\validate_data.py")                 # 新游戏工程把 toolchain 复制到了模板同一目录下
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $ValidateDataScript = (Resolve-Path $candidate).Path
            break
        }
    }
    if ($ValidateDataScript -eq "") {
        Write-Host "找不到 toolchain/validate_data.py，请用 -ValidateDataScript 显式指定其路径。" -ForegroundColor Red
        exit 2
    }
}

if ($FrameworkRoot -eq "") {
    $toolchainDir = Split-Path -Parent $ValidateDataScript
    $frameworkRepoRoot = Split-Path -Parent $toolchainDir
    $candidates = @(
        (Join-Path $frameworkRepoRoot "data\_framework"),     # toolchain 与 data 同级（框架仓库根，或分发包根）
        (Join-Path $TemplateRoot "..\data\_framework")        # 分发包 dist/<version>/{games/_template, data}/ 同级
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $FrameworkRoot = (Resolve-Path $candidate).Path
            break
        }
    }
    if ($FrameworkRoot -eq "") {
        Write-Host "找不到框架级数据表目录 data/_framework，请用 -FrameworkRoot 显式指定其路径。" -ForegroundColor Red
        exit 2
    }
}

if (-not (Test-Path $DataRoot)) {
    Write-Host "本游戏数据目录不存在：$DataRoot（复制模板改名后请同步 -DataRoot 默认值）" -ForegroundColor Red
    exit 2
}

Write-Host "框架级数据表：$FrameworkRoot"
Write-Host "本游戏数据：  $DataRoot"
Write-Host ""

$pythonArgs = @($ValidateDataScript, "--framework-root", $FrameworkRoot, "--data-root", $DataRoot)
if ($SkipDotnet) { $pythonArgs += "--skip-dotnet" }
if ($Strict) { $pythonArgs += "--strict" }

& $PythonExe @pythonArgs
exit $LASTEXITCODE
