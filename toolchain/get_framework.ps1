<#
.SYNOPSIS
    游戏仓库用的框架引用工具：按版本号拉取本框架（ws-game）的发布产物（`ws-game-<ver>.zip` +
    `ws-game-<ver>.lock`），校验六个核心 DLL 的 sha256 与锁文件一致后解压到
    `<Target>/ws-game-<ver>/`，并在游戏仓库根写入/校验 `ws-game.lock`（见仓库根 README.md
    "版本与发布"一节、`architecture/落地计划/落地方案与分阶段计划.md` 第 3.5 节"新游戏如何消费
    本框架"）。本脚本运行在游戏仓库那一侧，不属于框架仓库自身的构建/门禁链路，只是框架随发布产物
    一并提供、供游戏侧调用的工具。

.PARAMETER Version
    要拉取的框架版本号，形如 X.Y.Z（必填）。

.PARAMETER Target
    解压目标的根目录，默认当前目录下 `packages`；实际解压目标是 `<Target>/ws-game-<Version>/`
    （若该目录已存在会被整体删除重建，视为一次全新拉取）。

.PARAMETER LockPath
    写入/校验的 `ws-game.lock` 路径，默认当前目录下 `ws-game.lock`（约定放在游戏仓库根，见 3.5
    节"游戏仓库根放 ws-game.lock"）。

.PARAMETER Repo
    框架发布所在的 GitHub 仓库（`owner/repo` 形式），默认 `wade004/ws-game`。只在在线拉取路径
    （未传 `-FromLocalDist`）时使用。

.PARAMETER FromLocalDist
    离线来源：直接指定本机已有的 `ws-game-<ver>.zip` 文件路径（同目录下需要有同名的
    `.lock` 文件，如 `ws-game-1.0.0.zip` 配 `ws-game-1.0.0.lock`），跳过 `gh release download`，
    用于本机验证发布产物、或没有网络访问 GitHub 权限的场景。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
    在线拉取依赖 `gh`（GitHub CLI）已登录且对 `-Repo` 指定的仓库有读权限；离线校验（
    `-FromLocalDist`）不依赖 `gh`、不需要网络。
    校验失败（DLL 哈希与锁文件不一致、或版本号不匹配）时不会把任何内容落地到 `-Target`，保持
    "校验通过才落地"的语义；已存在的旧版本目录只有在本次校验通过后才会被替换。
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Target = "packages",
    [string]$LockPath = "",
    [string]$Repo = "wade004/ws-game",
    [string]$FromLocalDist = ""
)

$ErrorActionPreference = "Stop"

$VersionFormatPattern = '^\d+\.\d+\.\d+$'
if ($Version -notmatch $VersionFormatPattern) {
    Write-Host "-Version 格式非法：'$Version'（需形如 X.Y.Z）" -ForegroundColor Red
    exit 1
}

if ($LockPath -eq "") {
    $LockPath = Join-Path (Get-Location).Path "ws-game.lock"
}

# 六个需要发布给 Unity 端的核心 DLL——与框架仓库 build.ps1 的 $CoreAssemblies 同一份清单，
# 只列 DLL 文件名（校验对象是锁文件里记录的 sha256，不需要 build.ps1 那边的源目录信息）。
$ExpectedDllNames = @(
    "Core.Foundation.dll",
    "Core.Numbers.dll",
    "Core.Rules.dll",
    "Core.Carriers.dll",
    "Core.Gameplay.dll",
    "Presentation.Common.dll"
)

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# -----------------------------------------------------------------------------
# 1. 取得 zip + lock（在线用 gh release download，离线用 -FromLocalDist 指定的本地 zip 文件，
#    约定同目录下有同名 .lock 文件——与 build.ps1 -Release 的产物落位方式一致：
#    dist/ws-game-<ver>.zip 配 dist/ws-game-<ver>.lock）。
# -----------------------------------------------------------------------------
$WorkTempRoot = Join-Path $env:TEMP ("ws_game_get_framework_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $WorkTempRoot | Out-Null

$ZipPath = ""
$LockSourcePath = ""

try {
    if ($FromLocalDist -ne "") {
        Write-Step "离线来源：-FromLocalDist $FromLocalDist"
        if (-not (Test-Path $FromLocalDist)) {
            throw "找不到 -FromLocalDist 指定的文件：$FromLocalDist"
        }
        $fromItem = Get-Item $FromLocalDist
        if ($fromItem.PSIsContainer) {
            throw "-FromLocalDist 需要指向一个 .zip 文件，收到的是目录：$FromLocalDist"
        }
        if ($fromItem.Extension -ne ".zip") {
            throw "-FromLocalDist 需要指向一个 .zip 文件，收到：$FromLocalDist"
        }
        $ZipPath = $fromItem.FullName
        $LockSourcePath = [System.IO.Path]::ChangeExtension($ZipPath, ".lock")
        if (-not (Test-Path $LockSourcePath)) {
            throw "找不到与 zip 同目录、同名的锁文件：$LockSourcePath（build.ps1 -Release 打包时 zip/lock 成对生成，见其 .PARAMETER Release 说明）"
        }
        Write-Host "  zip: $ZipPath"
        Write-Host "  lock: $LockSourcePath"
    } else {
        Write-Step "在线拉取：gh release download v$Version --repo $Repo"
        $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
        if (-not $ghCmd) {
            throw "找不到 gh（GitHub CLI），无法在线拉取；请先安装并登录 gh，或改用 -FromLocalDist 指定本地 zip 文件"
        }
        $downloadDir = Join-Path $WorkTempRoot "download"
        New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null

        & gh release download "v$Version" --repo $Repo --dir $downloadDir --pattern ("ws-game-" + $Version + ".zip") --pattern ("ws-game-" + $Version + ".lock") --clobber
        if ($LASTEXITCODE -ne 0) {
            throw "gh release download 失败（退出码 $LASTEXITCODE），请确认版本号 v$Version 对应的 Release 已发布且 gh 已登录有权限访问 $Repo"
        }

        $ZipPath = Join-Path $downloadDir ("ws-game-" + $Version + ".zip")
        $LockSourcePath = Join-Path $downloadDir ("ws-game-" + $Version + ".lock")
        if (-not (Test-Path $ZipPath)) {
            throw "gh release download 完成但找不到 $ZipPath（Release 资产命名是否与约定一致？）"
        }
        if (-not (Test-Path $LockSourcePath)) {
            throw "gh release download 完成但找不到 $LockSourcePath（Release 资产命名是否与约定一致？）"
        }
        Write-Host "  已下载 zip: $ZipPath"
        Write-Host "  已下载 lock: $LockSourcePath"
    }

    # -----------------------------------------------------------------------------
    # 2. 读锁文件，做基本校验（版本号字段应与 -Version 一致；不一致只警告不阻断——锁文件的
    #    version 字段描述的是"这份 zip 实际打包的版本"，理论上应与文件名/请求的 -Version 一致，
    #    但如果调用方明知故犯传了不匹配的 -FromLocalDist（例如临时验证用途），不强行阻断，只
    #    提醒；真正决定"内容是否可信"的是下面的 DLL 哈希比对，而不是这个字段本身）。
    # -----------------------------------------------------------------------------
    Write-Step "读取锁文件并校验"
    $lockRaw = Get-Content -Path $LockSourcePath -Raw -Encoding UTF8
    $lockObj = $lockRaw | ConvertFrom-Json
    if ($null -eq $lockObj.dlls) {
        throw "锁文件 $LockSourcePath 缺少 dlls 字段（格式不是本工具认识的 ws-game.lock 结构）"
    }
    if ($lockObj.version -ne $Version) {
        Write-Host "  警告：锁文件 version=$($lockObj.version) 与请求的 -Version=$Version 不一致" -ForegroundColor Yellow
    }
    Write-Host "  锁文件 version=$($lockObj.version)，git_commit=$($lockObj.git_commit)"

    # -----------------------------------------------------------------------------
    # 3. 解压到临时目录，按锁文件校验六个核心 DLL 的 sha256；校验通过前不改动 -Target。
    #    zip 内顶层目录名固定形如 "ws-game-<ver>"（build.ps1 -Release 打包约定，DryRun 产物额外
    #    带 -dryrun 后缀），这里不假设具体名字，取解压结果下唯一的顶层目录即可，保证 DryRun/正式
    #    两种命名都能正确处理。
    # -----------------------------------------------------------------------------
    Write-Step "解压并校验六个核心 DLL 的 sha256"
    $extractTempDir = Join-Path $WorkTempRoot "extract"
    Expand-Archive -Path $ZipPath -DestinationPath $extractTempDir -Force

    $topDirs = @(Get-ChildItem -Path $extractTempDir -Directory)
    if ($topDirs.Count -ne 1) {
        throw "zip 解压后顶层应恰好有 1 个目录，实际 $($topDirs.Count) 个（zip 内容是否符合 'ws-game-<ver>/...' 约定？）"
    }
    $innerRoot = $topDirs[0].FullName

    $pluginsCoreDir = Join-Path $innerRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"
    if (-not (Test-Path $pluginsCoreDir)) {
        throw "解压产物里找不到 $pluginsCoreDir（zip 内容是否完整？）"
    }

    $hashMismatches = @()
    foreach ($dllName in $ExpectedDllNames) {
        $expectedSha = $lockObj.dlls.$dllName
        $dllPath = Join-Path $pluginsCoreDir $dllName
        if (-not (Test-Path $dllPath)) {
            $hashMismatches += "$dllName：解压产物中不存在（$dllPath）"
            continue
        }
        if ([string]::IsNullOrEmpty($expectedSha)) {
            $hashMismatches += "$dllName：锁文件未记录该 DLL 的 sha256"
            continue
        }
        $actualSha = (Get-FileHash -Path $dllPath -Algorithm SHA256).Hash.ToLower()
        if ($actualSha -ne $expectedSha.ToLower()) {
            $hashMismatches += "$dllName：sha256 不一致（锁文件=$expectedSha，实际=$actualSha）"
        } else {
            Write-Host "  [通过] $dllName sha256=$actualSha"
        }
    }

    if ($hashMismatches.Count -gt 0) {
        throw ("DLL 哈希校验失败，未落地到 -Target（内容可能被篡改或下载不完整）：`n  " + ($hashMismatches -join "`n  "))
    }
    Write-Host "  六个核心 DLL 哈希全部与锁文件一致"

    # -----------------------------------------------------------------------------
    # 4. 校验通过：落地到 <Target>/ws-game-<Version>/（已存在则整体删除重建，视为一次全新拉取）。
    # -----------------------------------------------------------------------------
    Write-Step "落地到 $Target/ws-game-$Version/"
    $targetRootFull = $Target
    if (-not [System.IO.Path]::IsPathRooted($targetRootFull)) {
        $targetRootFull = Join-Path (Get-Location).Path $Target
    }
    if (-not (Test-Path $targetRootFull)) {
        New-Item -ItemType Directory -Force -Path $targetRootFull | Out-Null
    }
    $extractDir = Join-Path $targetRootFull ("ws-game-" + $Version)
    if (Test-Path $extractDir) {
        Write-Host "  目标目录已存在，整体删除重建：$extractDir" -ForegroundColor Yellow
        Remove-Item -Path $extractDir -Recurse -Force -Confirm:$false
    }
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
    Get-ChildItem -Path $innerRoot -Force | ForEach-Object {
        Move-Item -Path $_.FullName -Destination $extractDir -Force
    }
    Write-Host "  已落地：$extractDir"
    Write-Host "  引用示例（游戏工程 Packages/manifest.json，相对路径按实际目录层级调整）："
    Write-Host "    `"com.gamefoundation.adapter.unity`": `"file:.../$Target/ws-game-$Version/adapters/unity/Packages/com.gamefoundation.adapter.unity`""

    # -----------------------------------------------------------------------------
    # 5. 写入/校验游戏仓库根的 ws-game.lock：内容与本次下载的锁文件一致则跳过（已是最新）；
    #    不存在则新建；存在但内容不同则覆盖并打印旧/新版本号对比，方便看出这是一次升级/降级。
    # -----------------------------------------------------------------------------
    Write-Step "写入/校验 $LockPath"
    $newLockJson = ($lockObj | ConvertTo-Json -Depth 5)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    if (Test-Path $LockPath) {
        $existingLockRaw = Get-Content -Path $LockPath -Raw -Encoding UTF8
        $existingLockObj = $null
        try { $existingLockObj = $existingLockRaw | ConvertFrom-Json } catch {}
        $existingVersion = "(无法解析)"
        if ($null -ne $existingLockObj) { $existingVersion = $existingLockObj.version }

        if ($existingLockRaw.Trim() -eq $newLockJson.Trim()) {
            Write-Host "  $LockPath 内容已与本次下载一致，无需改写"
        } else {
            Write-Host "  $LockPath 已存在（version=$existingVersion），改写为 version=$($lockObj.version)" -ForegroundColor Yellow
            [System.IO.File]::WriteAllText($LockPath, $newLockJson, $utf8NoBom)
            Write-Host "  已改写：$LockPath"
        }
    } else {
        [System.IO.File]::WriteAllText($LockPath, $newLockJson, $utf8NoBom)
        Write-Host "  已新建：$LockPath"
    }
} finally {
    Remove-Item -Path $WorkTempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "get_framework.ps1 完成：version=$Version 已校验并落地到 $Target/ws-game-$Version/" -ForegroundColor Green
exit 0
