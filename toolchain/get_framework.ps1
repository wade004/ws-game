<#
.SYNOPSIS
    游戏仓库用的框架引用工具：按版本号拉取本框架（ws-game）的发布产物（`ws-game-<ver>.zip` +
    `ws-game-<ver>.lock`），校验六个核心 DLL 的 sha256 与锁文件一致后解压到
    `<Target>/ws-game-<ver>/`，并在游戏仓库根写入/校验 `ws-game.lock`（见仓库根 README.md
    "版本与发布"一节、`architecture/落地计划/落地方案与分阶段计划.md` 第 3.5 节"新游戏如何消费
    本框架"）。本脚本运行在游戏仓库那一侧，不属于框架仓库自身的构建/门禁链路，只是框架随发布产物
    一并提供、供游戏侧调用的工具。

    依赖提示：本脚本用哈希校验依赖同目录 `_hash.ps1`（`Get-Sha256FileHash` 共享函数，见下方哈希
    校验小节），两个文件是一对，不能只复制本脚本单独使用——游戏仓库侧引用时请把 `toolchain`
    整个目录一起复制/引用，或至少同时携带 `_hash.ps1`；缺失时脚本会给出明确报错并退出，不会
    静默失败或退化。

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

.PARAMETER AllowVersionMismatch
    判断记录（P04 根治，2026-09-07，审计 architecture/落地计划/audit-7e63d66-20260907/
    project-review.md P04）：默认严格模式——锁文件里记录的实际版本号（`ws-game-<Version>.lock`
    的 `version` 字段，即这份 zip 真正打包的版本）与调用方 `-Version` 请求的版本号不一致时直接
    报错退出，不落地、不删除/不覆盖任何已有的 `-Target` 子目录（此前只 warning 后继续，会按
    "请求版本号"命名落地目录并可能先删除该目录，导致例如请求 `-Version 1.0.1` 但本地归档实际是
    1.0.0 时，1.0.0 的真实内容被落进名为 `ws-game-1.0.1` 的目录、且可能先删除了原本正确的
    `ws-game-1.0.1` 目录——目录名与实际内容身份不符，是比"覆盖/删除"更根本的问题）。这个开关
    默认关闭；显式传入后放行版本不一致的请求，但落地目录名与打印的引用示例改用锁文件记录的
    *实际* 版本号（不是请求的 `-Version`），确保目录名永远与其内容真实身份一致；同时打印源
    （锁文件 version）与目标（本次请求的 -Version，以及最终落地目录名）两侧身份，避免静默生效。
    只影响 zip 通道的版本号一致性判定，不影响 DLL 哈希校验（哈希校验始终执行、不受本开关影响）。

.PARAMETER FromRegistry
    私服交付通道新增：与上面"拉 zip 解压到 -Target"是完全不同的另一条通道，不下载/不解压任何
    文件——UPM 私服场景下，"拉包"这件事由 Unity 编辑器自己在打开工程/刷新包管理器时向注册表发
    请求完成，本脚本不代劳，只负责两件事：1）生成/更新游戏工程的 `Packages/manifest.json`，写入
    作用域注册表条目与三个包依赖（版本号 = `-Version`）；2）写 `ws-game.lock`，记录本次引用的
    来源是私服（注册表地址、作用域、三个包名）与版本号，与 zip 通道写的锁文件同一个文件、不同的
    `source.channel` 取值（`registry` / `zip`），供后续升级/核对时区分当前用的是哪条通道。传了
    `-FromRegistry` 后 `-Target`/`-Repo`/`-FromLocalDist` 不生效（忽略，不报错）。

.PARAMETER RegistryUrl
    配合 `-FromRegistry` 使用：私服地址。省略时按顺序尝试：1）本脚本同目录下 `registry/
    registry.json` 的 `url` 字段（框架仓库自己的工作树布局，或随 `com.gamefoundation.toolchain`
    包分发时若该文件恰好在场）；2）都找不到则退化为默认值 `http://127.0.0.1:4873`。

.PARAMETER ManifestPath
    配合 `-FromRegistry` 使用：要写入/更新的 `Packages/manifest.json` 路径。省略时依次尝试当前
    目录下的 `Packages/manifest.json`（游戏仓库根调用本脚本时的常见相对位置）；两者都找不到时不
    报错，只把应该写入的片段打印到控制台，由调用方自行合并进自己的 `manifest.json`（可能路径不
    是常见位置，或调用方想自己手工检查这一步改动）。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
    在线拉取依赖 `gh`（GitHub CLI）已登录且对 `-Repo` 指定的仓库有读权限；离线校验（
    `-FromLocalDist`）不依赖 `gh`、不需要网络；`-FromRegistry` 依赖私服本身可访问
    （`-RegistryUrl`/-/ping 返回 200），不依赖 `gh`。
    校验失败（DLL 哈希与锁文件不一致、或版本号不匹配）时不会把任何内容落地到 `-Target`，保持
    "校验通过才落地"的语义；已存在的旧版本目录只有在本次校验通过后才会被替换。这条"校验后才
    落地"的语义只适用于 zip 通道——`-FromRegistry` 通道的完整性校验交给 npm/UPM 自己的包传输
    机制（tarball 校验和），本脚本不重复实现。
    判断记录（PJ150-01 根治，2026-09-08，审计 architecture/落地计划/audit-3224ca1-20260908/
    AUDIT_REPORT.md PJ150-01）：`-AllowVersionMismatch` 放行版本不一致后，锁文件 `version` 字段
    此前未经格式校验就被用于拼接落地目录路径并触发 `Remove-Item -Recurse -Force`，构造成
    `x/../../outside_sentinel` 形式可越界删除 `-Target` 之外的目录，六个 DLL 的哈希校验不覆盖
    这个元数据字段。根治两层缺一不可：1）锁文件 `version` 与 `-Version` 同样严格校验语义化版本
    格式；2）落地路径规范化后必须仍是 `-Target` 的严格子目录（`Test-IsStrictSubPath` 函数），
    写入/删除前完成校验。同时把 zip 解压从无差别的 `Expand-Archive` 改为逐条目手动解压 + 同一套
    边界校验，堵住 zip 内条目路径本身携带 `../`（zip slip）的越界口子。详见 toolchain/README.md
    "`get_framework.ps1`（游戏侧按版本号引用本框架）"一节"路径边界判断记录"、回归测试
    `toolchain/tests/test_get_framework_path_boundary.py`。
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Target = "packages",
    [string]$LockPath = "",
    [string]$Repo = "wade004/ws-game",
    [string]$FromLocalDist = "",
    [switch]$AllowVersionMismatch,
    [switch]$FromRegistry,
    [string]$RegistryUrl = "",
    [string]$ManifestPath = ""
)

$ErrorActionPreference = "Stop"

# 判断记录（第九轮审计工具链条目，本机 Get-FileHash 在部分 Windows PowerShell 5.1 环境下不可用，
# 原因未查明）：DLL 哈希校验改用 toolchain/_hash.ps1 提供的 Get-Sha256FileHash 共享函数（内置
# Get-FileHash 可用时优先用，不可用时透明退化到不依赖该 cmdlet 的 .NET SHA256 兜底实现），见该
# 文件头注释。本脚本单独复制到游戏仓库时若漏带 _hash.ps1，两个文件不在同一目录会导致本脚本无法
# 工作；先显式检查存在性、给出明确报错，避免直接暴露点源不存在时的默认 PowerShell 报错。
$hashScriptPath = Join-Path $PSScriptRoot "_hash.ps1"
if (-not (Test-Path -LiteralPath $hashScriptPath)) {
    Write-Host "缺少同目录 _hash.ps1：'$hashScriptPath' 不存在。get_framework.ps1 依赖它提供的 Get-Sha256FileHash 共享函数，不能单独复制本脚本使用——请把 toolchain 整个目录一起复制/引用到游戏仓库，或至少同时携带 _hash.ps1。" -ForegroundColor Red
    exit 1
}
. $hashScriptPath

$VersionFormatPattern = '^\d+\.\d+\.\d+$'
if ($Version -notmatch $VersionFormatPattern) {
    Write-Host "-Version 格式非法：'$Version'（需形如 X.Y.Z）" -ForegroundColor Red
    exit 1
}

# 判断记录（P150-01 根治，2026-09-08，审计 architecture/落地计划/audit-3224ca1-20260908/
# AUDIT_REPORT.md PJ150-01）：锁文件里的 `version` 字段此前只在“等于 -Version”时才被信任；一旦
# 调用方传了 -AllowVersionMismatch 放行版本不一致，脚本会把锁文件 version 原样赋给
# $EffectiveVersion 并直接拼进落地目录路径（`<Target>/ws-game-<EffectiveVersion>/`），随后对该
# 路径执行 `Remove-Item -Recurse -Force`——锁文件是随 zip 一起搬运的数据文件，其 `version`
# 字段未经任何格式校验，构造成形如 `x/../../outside_sentinel` 的值即可让拼出的路径规范化后落到
# `-Target` 之外的任意兄弟目录，脚本会先删除那里已有的内容再落地框架文件（哈希校验只覆盖六个
# DLL 字节，不覆盖这个元数据字段，看不出异常）。根治两层：1）锁文件 version 与 `-Version` 参数
# 同样严格校验格式（允许可选的语义化版本预发布后缀，供本机验证/迁移场景使用非正式版本号归档，
# 但不允许出现路径分隔符/`..`/绝对路径等非版本号字符）；2）无论格式校验是否通过，落地目录规范化
# 后必须仍是 `-Target` 的严格子目录，任何写入/删除前都要经过这道边界检查——两层任一层单独失守
# 都不足以杜绝越界，必须同时具备。
$LockVersionFormatPattern = '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$'

# 判断记录（P150-01 根治，同上）：把“规范化落点是否为 -Target 的严格子目录”抽成一个可复用函数，
# 落地目录计算（zip 通道）与后续任何需要做同类边界校验的地方（例如 zip 内条目路径）共用同一份
# 判定逻辑，不重复实现、不因为遗漏某处校验而留下另一个越界口子。严格子目录：规范化后的绝对路径，
# 去掉结尾分隔符后再补一个分隔符作为前缀比较，故意不接受“等于根目录本身”（落地目录理应是根目录
# 下的一层子目录，不应该出现落地路径与 -Target 本身相同、进而在 Remove-Item 时把 -Target 自己删掉
# 的情况）。
function Test-IsStrictSubPath {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string]$RootPath
    )
    $rootFull = [System.IO.Path]::GetFullPath($RootPath)
    $candidateFull = [System.IO.Path]::GetFullPath($CandidatePath)
    $rootWithSep = $rootFull.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return $candidateFull.StartsWith($rootWithSep, [System.StringComparison]::OrdinalIgnoreCase)
}

if ($LockPath -eq "") {
    $LockPath = Join-Path (Get-Location).Path "ws-game.lock"
}

# -----------------------------------------------------------------------------
# 私服交付通道新增：-FromRegistry 是独立分支，做完就退出，不进入下面 zip 通道的任何逻辑
# （下载/解压/DLL 哈希校验对这条通道没有意义，见 .PARAMETER FromRegistry 说明）。
# -----------------------------------------------------------------------------
if ($FromRegistry) {
    function Write-RegistryStep {
        param([string]$Message)
        Write-Host ""
        Write-Host "==== $Message ====" -ForegroundColor Cyan
    }

    $ThreePackageNames = @(
        "com.gamefoundation.adapter.unity",
        "com.gamefoundation.framework-data",
        "com.gamefoundation.toolchain"
    )
    $RegistryScope = "com.gamefoundation"

    if ($RegistryUrl -eq "") {
        $siblingRegistryJson = Join-Path $PSScriptRoot "registry\registry.json"
        if (Test-Path $siblingRegistryJson) {
            $RegistryUrl = ((Get-Content -Path $siblingRegistryJson -Raw -Encoding UTF8) | ConvertFrom-Json).url
            Write-Host "未传 -RegistryUrl，取本机 $siblingRegistryJson 的 url：$RegistryUrl" -ForegroundColor Cyan
        } else {
            $RegistryUrl = "http://127.0.0.1:4873"
            Write-Host "未传 -RegistryUrl，且找不到 $siblingRegistryJson，退化为默认值：$RegistryUrl" -ForegroundColor Yellow
        }
    }
    $RegistryUrl = $RegistryUrl.TrimEnd('/')

    Write-RegistryStep "生成/更新 manifest.json 片段（作用域注册表 + 三个依赖，version=$Version）"

    $scopedRegistryEntry = [ordered]@{
        name   = "ws-game private registry"
        url    = $RegistryUrl
        scopes = @($RegistryScope)
    }

    if ($ManifestPath -eq "") {
        $defaultManifestPath = Join-Path (Get-Location).Path "Packages\manifest.json"
        if (Test-Path $defaultManifestPath) {
            $ManifestPath = $defaultManifestPath
            Write-Host "  未传 -ManifestPath，找到默认位置：$ManifestPath"
        }
    }

    if ($ManifestPath -ne "" -and (Test-Path $ManifestPath)) {
        $manifestRaw = Get-Content -Path $ManifestPath -Raw -Encoding UTF8
        $manifestObj = $manifestRaw | ConvertFrom-Json

        # scopedRegistries：按 url 去重覆盖（同一个私服地址只保留一条，避免重复调用本脚本时
        # manifest.json 里堆出多条一样的条目）；不存在则新增数组。
        $existingScoped = @()
        if ($manifestObj.PSObject.Properties.Name -contains "scopedRegistries") {
            $existingScoped = @($manifestObj.scopedRegistries | Where-Object { $_.url -ne $RegistryUrl })
        }
        $existingScoped += [PSCustomObject]$scopedRegistryEntry
        if ($manifestObj.PSObject.Properties.Name -contains "scopedRegistries") {
            $manifestObj.scopedRegistries = $existingScoped
        } else {
            $manifestObj | Add-Member -MemberType NoteProperty -Name "scopedRegistries" -Value $existingScoped
        }

        # dependencies：三个包依赖写入/覆盖为目标版本号，保留 manifest.json 里已有的其它依赖不变。
        if (-not ($manifestObj.PSObject.Properties.Name -contains "dependencies")) {
            $manifestObj | Add-Member -MemberType NoteProperty -Name "dependencies" -Value ([PSCustomObject]@{})
        }
        foreach ($pkgName in $ThreePackageNames) {
            if ($manifestObj.dependencies.PSObject.Properties.Name -contains $pkgName) {
                $manifestObj.dependencies.$pkgName = $Version
            } else {
                $manifestObj.dependencies | Add-Member -MemberType NoteProperty -Name $pkgName -Value $Version
            }
        }

        $manifestJson = ($manifestObj | ConvertTo-Json -Depth 10)
        [System.IO.File]::WriteAllText($ManifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  已写入：$ManifestPath" -ForegroundColor Green
    } else {
        Write-Host "  未找到可写入的 manifest.json（未传 -ManifestPath 且当前目录下没有 Packages\manifest.json），改为打印片段，请自行合并：" -ForegroundColor Yellow
        Write-Host ""
        Write-Host (@{
            scopedRegistries = @($scopedRegistryEntry)
            dependencies     = @{
                "com.gamefoundation.adapter.unity"   = $Version
                "com.gamefoundation.framework-data"  = $Version
                "com.gamefoundation.toolchain"       = $Version
            }
        } | ConvertTo-Json -Depth 10)
    }

    Write-RegistryStep "写入/校验 $LockPath"
    $registryLockObj = [ordered]@{
        version = $Version
        source  = [ordered]@{
            channel      = "registry"
            registry_url = $RegistryUrl
            scope        = $RegistryScope
            packages     = $ThreePackageNames
        }
    }
    $registryLockJson = ($registryLockObj | ConvertTo-Json -Depth 5)
    $utf8NoBomRegistry = New-Object System.Text.UTF8Encoding($false)
    if (Test-Path $LockPath) {
        $existingRegistryLockRaw = Get-Content -Path $LockPath -Raw -Encoding UTF8
        if ($existingRegistryLockRaw.Trim() -eq $registryLockJson.Trim()) {
            Write-Host "  $LockPath 内容已一致，无需改写"
        } else {
            Write-Host "  $LockPath 已存在，改写为 version=$Version，source.channel=registry" -ForegroundColor Yellow
            [System.IO.File]::WriteAllText($LockPath, $registryLockJson, $utf8NoBomRegistry)
            Write-Host "  已改写：$LockPath"
        }
    } else {
        [System.IO.File]::WriteAllText($LockPath, $registryLockJson, $utf8NoBomRegistry)
        Write-Host "  已新建：$LockPath"
    }

    Write-Host ""
    Write-Host "get_framework.ps1 -FromRegistry 完成：version=$Version，registry=$RegistryUrl" -ForegroundColor Green
    exit 0
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
    # 2. 读锁文件，做基本校验（版本号字段应与 -Version 一致）。判断记录（P04 根治，见
    #    .PARAMETER AllowVersionMismatch 说明）：默认严格模式，不一致直接报错退出、不落地、不
    #    删除/不覆盖任何已有目标目录；显式 -AllowVersionMismatch 才放行，且放行后落地目录名与
    #    引用示例一律改用锁文件记录的实际版本号（$EffectiveVersion），不再使用请求的 -Version。
    # -----------------------------------------------------------------------------
    Write-Step "读取锁文件并校验"
    $lockRaw = Get-Content -Path $LockSourcePath -Raw -Encoding UTF8
    $lockObj = $lockRaw | ConvertFrom-Json
    if ($null -eq $lockObj.dlls) {
        throw "锁文件 $LockSourcePath 缺少 dlls 字段（格式不是本工具认识的 ws-game.lock 结构）"
    }
    $EffectiveVersion = $Version
    if ($lockObj.version -ne $Version) {
        if (-not $AllowVersionMismatch) {
            throw ("锁文件 version=" + $lockObj.version + " 与请求的 -Version=" + $Version + " 不一致，默认严格模式下拒绝继续" +
                   "（未落地、未删除/覆盖任何已有目标目录）。若确认要用这份实际版本号不同的归档（例如本机验证/迁移场景），" +
                   "显式传 -AllowVersionMismatch 放行——放行后落地目录名与引用示例改用锁文件的实际版本号 " + $lockObj.version +
                   "，不使用请求的 -Version，避免目录名与实际内容身份不符。")
        }
        if ($lockObj.version -notmatch $LockVersionFormatPattern) {
            throw ("锁文件 " + $LockSourcePath + " 的 version 字段格式非法：'" + $lockObj.version +
                   "'（需形如 X.Y.Z 或 X.Y.Z-<预发布标识>，不允许路径分隔符/`..`/空白等字符）。" +
                   "该字段会被用于拼接落地目录名，格式校验失败前不会做任何写入/删除。")
        }
        $EffectiveVersion = $lockObj.version
        Write-Host ("  警告：锁文件 version=" + $lockObj.version + " 与请求的 -Version=" + $Version + " 不一致——已传 -AllowVersionMismatch，放行。") -ForegroundColor Yellow
        Write-Host ("  源（锁文件实际版本）=" + $lockObj.version + "；目标（本次请求版本）=" + $Version + "；落地目录名与引用示例将使用源版本号 " + $EffectiveVersion) -ForegroundColor Yellow
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
    New-Item -ItemType Directory -Force -Path $extractTempDir | Out-Null

    # 判断记录（P150-01 根治，同上，zip slip 部分）：不再直接用 `Expand-Archive` 无差别解压——zip
    # 条目自身的文件名可以携带 `../`（zip slip），一个被篡改或恶意构造的 zip 即使六个 DLL 哈希都
    # 对得上（哈希校验只挑六个固定名字的 DLL 比对，不校验其它条目），仍可能借助其它条目的路径把
    # 内容写到 $extractTempDir 之外。改为用 `System.IO.Compression.ZipFile` 逐条目手动解压，每条
    # 目标路径规范化后必须仍是 $extractTempDir 的严格子目录，不满足直接拒绝、不解压任何后续条目。
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($zipEntry in $zipArchive.Entries) {
            # 判断记录：本仓库 `build.ps1` 打的 zip 里目录条目的 `FullName` 以 `\`（Windows 分隔符）
            # 结尾（例如 `ws-game-1.5.0\adapters\`），不是 zip 规范惯例的 `/`；.NET 的
            # `ZipArchiveEntry.Name` 只按 `/` 切分，遇到这类条目时 `Name` 不为空（等于整个
            # `FullName`），不能再用"`Name` 是否为空"判断是否为目录条目——改为直接看 `FullName`
            # 是否以 `/` 或 `\` 结尾，兼容两种分隔符约定的 zip 生产者。
            $isDirEntry = $zipEntry.FullName.EndsWith("/") -or $zipEntry.FullName.EndsWith("\")
            if ($isDirEntry) {
                # 纯目录条目：只确保目录存在，不写文件内容。
                $entryDirPath = Join-Path $extractTempDir $zipEntry.FullName
                if (-not (Test-IsStrictSubPath -CandidatePath $entryDirPath -RootPath $extractTempDir)) {
                    throw "zip 条目路径越界（zip slip），已拒绝解压：'$($zipEntry.FullName)'"
                }
                New-Item -ItemType Directory -Force -Path $entryDirPath | Out-Null
                continue
            }
            $entryDestPath = Join-Path $extractTempDir $zipEntry.FullName
            if (-not (Test-IsStrictSubPath -CandidatePath $entryDestPath -RootPath $extractTempDir)) {
                throw "zip 条目路径越界（zip slip），已拒绝解压：'$($zipEntry.FullName)'"
            }
            $entryDestDir = [System.IO.Path]::GetDirectoryName($entryDestPath)
            if (-not (Test-Path $entryDestDir)) {
                New-Item -ItemType Directory -Force -Path $entryDestDir | Out-Null
            }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($zipEntry, $entryDestPath, $true)
        }
    } finally {
        $zipArchive.Dispose()
    }

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
        $actualSha = Get-Sha256FileHash -Path $dllPath
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
    Write-Step "落地到 $Target/ws-game-$EffectiveVersion/"
    $targetRootFull = $Target
    if (-not [System.IO.Path]::IsPathRooted($targetRootFull)) {
        $targetRootFull = Join-Path (Get-Location).Path $Target
    }
    if (-not (Test-Path $targetRootFull)) {
        New-Item -ItemType Directory -Force -Path $targetRootFull | Out-Null
    }
    # 判断记录（P04 根治）：落地目录名固定用 $EffectiveVersion（未触发 -AllowVersionMismatch 时
    # 等于 $Version，二者相同；触发且放行后等于锁文件实际版本号），保证目录名与实际落地内容的
    # 身份始终一致，不会出现"请求版本号命名的目录，装着另一个版本的真实内容"。
    $extractDir = Join-Path $targetRootFull ("ws-game-" + $EffectiveVersion)

    # 判断记录（P150-01 根治，同上）：无论 $EffectiveVersion 的格式校验是否已经拦下了非法字符，
    # 落地前再做一次独立的路径边界校验——两层防御互不依赖，任一层单独失效时另一层仍能拦截。这里
    # 用规范化后的绝对路径比较，而不是只看字符串是否包含 `..`（`GetFullPath` 会把 `a/../../b`
    # 这类构造实际解析成的越界路径原形毕露，字符串黑名单容易漏判）。校验失败直接 throw，不做任何
    # `Test-Path`/`Remove-Item`/`New-Item`。
    if (-not (Test-IsStrictSubPath -CandidatePath $extractDir -RootPath $targetRootFull)) {
        throw ("拒绝落地：规范化后的目标路径 '" + [System.IO.Path]::GetFullPath($extractDir) +
               "' 不是 -Target '" + [System.IO.Path]::GetFullPath($targetRootFull) +
               "' 的严格子目录（可能是锁文件 version 字段包含路径穿越片段）。未对该路径或 -Target 之外的任何目录执行写入/删除。")
    }
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
    Write-Host "    `"com.gamefoundation.adapter.unity`": `"file:.../$Target/ws-game-$EffectiveVersion/adapters/unity/Packages/com.gamefoundation.adapter.unity`""

    # -----------------------------------------------------------------------------
    # 5. 写入/校验游戏仓库根的 ws-game.lock：内容与本次下载的锁文件一致则跳过（已是最新）；
    #    不存在则新建；存在但内容不同则覆盖并打印旧/新版本号对比，方便看出这是一次升级/降级。
    #    私服交付通道新增：额外记一个 source 字段（channel=zip + 具体来源），与 -FromRegistry
    #    通道写的锁文件用同一个 source.channel 字段区分，见该分支说明。
    # -----------------------------------------------------------------------------
    Write-Step "写入/校验 $LockPath"
    if ($FromLocalDist -ne "") {
        $lockSourceInfo = [ordered]@{ channel = "zip"; local_path = $ZipPath }
    } else {
        $lockSourceInfo = [ordered]@{ channel = "zip"; repo = $Repo }
    }
    $lockObj | Add-Member -MemberType NoteProperty -Name "source" -Value ([PSCustomObject]$lockSourceInfo) -Force
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
Write-Host "get_framework.ps1 完成：version=$EffectiveVersion 已校验并落地到 $Target/ws-game-$EffectiveVersion/" -ForegroundColor Green
exit 0
